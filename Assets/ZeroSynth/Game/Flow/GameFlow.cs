using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using ZeroSynth.Authoring;

namespace ZeroSynth.Flow
{
    /// <summary>
    /// The single owner of game state: which screen is up, whether the run is
    /// paused, and how a run starts and ends. Lives in SC_Boot and survives
    /// every scene load.
    /// </summary>
    /// <remarks>
    /// UI never changes scenes or touches the ECS world itself. It calls the
    /// public methods here, listens to <see cref="StateChanged"/>, and reads
    /// <see cref="Snapshot"/>.
    ///
    /// Every scene change goes through SC_Loading, and the ECS world is reset
    /// there. That is what makes Restart and "back to menu then Play again"
    /// safe: the previous run's scene is already gone and its world is thrown
    /// away before the next one is built.
    /// </remarks>
    public sealed class GameFlow : MonoBehaviour
    {
        public static GameFlow Instance { get; private set; }

        [Header("Run")]
        [Tooltip("Clearing this wave wins the run.")]
        [SerializeField, Min(1)] int m_WavesToWin = 20;

        [Tooltip("Shortest time spent on the loading scene, so it does not flash.")]
        [SerializeField, Min(0f)] float m_MinLoadSeconds = 0.25f;

        readonly RunReader m_Reader = new RunReader();
        bool m_Transitioning;
        bool m_TacticalPause;

        public GameState State { get; private set; } = GameState.Boot;
        public GameSnapshot Snapshot { get; private set; }
        public RunOutcome Outcome { get; private set; }
        public GameSettings Settings { get; private set; }
        public int WavesToWin => m_WavesToWin;

        /// <summary>Highest wave cleared in any run. Zero if none yet.</summary>
        public int BestWave => PlayerRecords.BestWave;

        /// <summary>(previous, current).</summary>
        public event Action<GameState, GameState> StateChanged;

        /// <summary>Raised after settings are applied and saved.</summary>
        public event Action SettingsChanged;

        /// <summary>
        /// The player froze the simulation to build or inspect. Distinct from
        /// the pause menu: the game stays in Playing, no overlay opens, and
        /// input keeps working. Reads false outside Playing.
        /// </summary>
        public bool TacticalPause => m_TacticalPause && State == GameState.Playing;

        public event Action TacticalPauseChanged;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Audio lives on this object so it survives scene loads and the
            // music does not restart between the menu and a run.
            if (GetComponent<ZeroSynth.Audio.AudioDirector>() == null)
                gameObject.AddComponent<ZeroSynth.Audio.AudioDirector>();

            Settings = GameSettings.Load();
            ApplySettings();
        }

        void Start()
        {
            if (Instance != this || State != GameState.Boot)
                return;

            // Pressing Play in the game scene itself, which is what happens when
            // working on it in the editor: there is no boot to come from, so
            // join the run already in progress instead of leaving for the menu.
            // In a normal run SC_Game's own GameFlow is the duplicate, and is
            // destroyed in Awake before it gets here.
            if (SceneManager.GetActiveScene().name == SceneNames.Game)
                SetState(GameState.Playing);
            else
                StartCoroutine(TransitionTo(SceneNames.MainMenu, GameState.MainMenu));
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (State == GameState.Playing || State == GameState.Paused)
            {
                if (m_Reader.TryRead(out var snapshot))
                    Snapshot = snapshot;
            }

            if (State == GameState.Playing)
                CheckOutcome();

            // Escape is also Android's back button under the Input System.
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (State == GameState.Playing) Pause();
                else if (State == GameState.Paused) Resume();
            }
        }

        // ---------------------------------------------------------------
        // Public API. This is what UI calls.
        // ---------------------------------------------------------------

        /// <summary>From the main menu: begin a new run.</summary>
        public void StartGame()
        {
            if (State != GameState.MainMenu) return;
            StartCoroutine(TransitionTo(SceneNames.Game, GameState.Playing));
        }

        public void Pause()
        {
            if (State != GameState.Playing) return;
            m_Reader.SetSimulationPaused(true);
            SetState(GameState.Paused);
        }

        public void Resume()
        {
            if (State != GameState.Paused) return;

            // Back to whatever the player had before the menu: if they were in
            // a tactical pause, the simulation stays frozen.
            m_Reader.SetSimulationPaused(m_TacticalPause);
            SetState(GameState.Playing);
        }

        /// <summary>
        /// Freeze or unfreeze the simulation without opening the pause menu.
        /// Only meaningful while playing.
        /// </summary>
        public void SetTacticalPause(bool paused)
        {
            if ((State != GameState.Playing && State != GameState.Paused) || m_TacticalPause == paused)
                return;

            m_TacticalPause = paused;

            // From the pause menu the simulation stays frozen whatever this says;
            // Resume applies the flag. It has to be accepted here though: opening
            // the menu mid-build cancels the build, which releases the tactical
            // pause, and refusing that left the flag stuck on and the game
            // frozen after the menu closed.
            if (State == GameState.Playing)
                m_Reader.SetSimulationPaused(paused);

            TacticalPauseChanged?.Invoke();
        }

        /// <summary>Abandon the current run and start a fresh one.</summary>
        public void Restart()
        {
            if (m_Transitioning) return;
            if (State != GameState.Paused && State != GameState.GameOver && State != GameState.Victory) return;
            RecordAbandonedProgress();
            StartCoroutine(TransitionTo(SceneNames.Game, GameState.Playing));
        }

        public void QuitToMenu()
        {
            if (m_Transitioning) return;
            if (State != GameState.Paused && State != GameState.GameOver && State != GameState.Victory) return;
            RecordAbandonedProgress();
            StartCoroutine(TransitionTo(SceneNames.MainMenu, GameState.MainMenu));
        }

        /// <summary>
        /// Leaving from the pause menu still counts the waves already cleared.
        /// A finished run recorded itself in <see cref="EndRun"/>.
        /// </summary>
        void RecordAbandonedProgress()
        {
            if (State != GameState.Paused || !Snapshot.Valid) return;

            int cleared = Snapshot.Phase == WavePhase.Preparing ? Snapshot.Wave : Snapshot.Wave - 1;
            PlayerRecords.SubmitWave(Mathf.Max(0, cleared));
        }

        public void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// Call after changing <see cref="Settings"/>: clamps, saves, applies
        /// and notifies listeners.
        /// </summary>
        public void CommitSettings()
        {
            Settings.Save();
            ApplySettings();
            SettingsChanged?.Invoke();
        }

        // ---------------------------------------------------------------
        // Internals
        // ---------------------------------------------------------------

        void ApplySettings()
        {
            AudioListener.volume = Settings.MasterVolume;

            if (Settings.QualityLevel >= 0 && Settings.QualityLevel != QualitySettings.GetQualityLevel())
                QualitySettings.SetQualityLevel(Settings.QualityLevel, true);

            var hud = FindFirstObjectByType<DebugHud>();
            if (hud != null)
                hud.SetVisible(Settings.ShowDebugHud);
        }

        void SetState(GameState next)
        {
            if (next == State) return;

            var previous = State;
            State = next;
            PossessionController.InputEnabled = next == GameState.Playing;
            StateChanged?.Invoke(previous, next);
        }

        void CheckOutcome()
        {
            var s = Snapshot;
            if (!s.Valid) return;

            if (s.CoreMaxHealth > 0f && s.CoreHealth <= 0f)
            {
                EndRun(won: false);
            }
            else if (s.Wave >= m_WavesToWin && s.Phase == WavePhase.Preparing)
            {
                // The wave index counts waves begun, and the phase only returns
                // to Preparing once that wave has been cleared.
                EndRun(won: true);
            }
        }

        void EndRun(bool won)
        {
            // Losing during wave N means wave N was reached but not cleared, so
            // it counts for less than clearing it. The wave index counts waves
            // begun, so a loss is recorded one lower.
            int cleared = won ? Snapshot.Wave : Mathf.Max(0, Snapshot.Wave - 1);
            bool newBest = PlayerRecords.SubmitWave(cleared);

            Outcome = new RunOutcome
            {
                Won = won,
                WaveReached = Snapshot.Wave,
                WavesToWin = m_WavesToWin,
                Biomass = Snapshot.Biomass,
                Scrap = Snapshot.Scrap,
                BestWave = PlayerRecords.BestWave,
                NewBest = newBest
            };

            m_Reader.SetSimulationPaused(true);
            SetState(won ? GameState.Victory : GameState.GameOver);
        }

        IEnumerator TransitionTo(string sceneName, GameState finalState)
        {
            m_Transitioning = true;
            Snapshot = default;
            m_TacticalPause = false;
            SetState(GameState.Loading);

            float started = Time.unscaledTime;

            // Leave the gameplay scene first. Its OnDestroy callbacks release
            // their native memory, which must happen before the world is
            // disposed.
            yield return SceneManager.LoadSceneAsync(SceneNames.Loading);

            EcsWorldReset.Reset();

            var load = SceneManager.LoadSceneAsync(sceneName);
            while (!load.isDone)
                yield return null;

            float wait = m_MinLoadSeconds - (Time.unscaledTime - started);
            if (wait > 0f)
                yield return new WaitForSecondsRealtime(wait);

            ApplySettings();
            m_Transitioning = false;
            SetState(finalState);
        }
    }
}
