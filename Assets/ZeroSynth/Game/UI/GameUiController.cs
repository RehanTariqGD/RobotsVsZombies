using System;
using UnityEngine;
using UnityEngine.UIElements;
using ZeroSynth.Flow;

namespace ZeroSynth.UI
{
    /// <summary>Uses managed snapshots and cached display values to keep HUD work off the simulation path.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameUiController : MonoBehaviour
    {
        GameFlow m_Flow;
        VisualElement m_Hud, m_PauseScreen, m_OutcomeScreen, m_CoreFill;
        Label m_Wave, m_Phase, m_Remaining, m_Core, m_Biomass, m_Scrap;
        Label m_OutcomeTitle, m_OutcomeWave, m_OutcomeBiomass, m_OutcomeScrap, m_OutcomeBest;
        Button[] m_Buttons;
        Action[] m_Actions;
        SettingsPanelController m_Settings;
        SafeAreaPadding m_SafeArea;
        int m_LastWave, m_LastPhase, m_LastRemaining, m_LastBiomass, m_LastScrap;
        int m_LastHealth, m_LastMaxHealth;
        float m_LastFraction;
        bool m_HasOutcome;
        GameState m_LastOutcomeState;
        RunOutcome m_LastOutcome;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            m_Hud = root.Q("hud");
            m_PauseScreen = root.Q("pause-screen");
            m_OutcomeScreen = root.Q("outcome-screen");
            m_CoreFill = root.Q("core-bar-fill");
            m_Wave = root.Q<Label>("wave-label");
            m_Phase = root.Q<Label>("phase-label");
            m_Remaining = root.Q<Label>("remaining-label");
            m_Core = root.Q<Label>("core-label");
            m_Biomass = root.Q<Label>("biomass-label");
            m_Scrap = root.Q<Label>("scrap-label");
            m_OutcomeTitle = root.Q<Label>("outcome-title");
            m_OutcomeWave = root.Q<Label>("outcome-wave");
            m_OutcomeBiomass = root.Q<Label>("outcome-biomass");
            m_OutcomeScrap = root.Q<Label>("outcome-scrap");
            m_OutcomeBest = root.Q<Label>("outcome-best");
            m_Settings = new SettingsPanelController(root.Q("settings-panel"));
            m_SafeArea = new SafeAreaPadding(root.Q("safe-area"));
            m_Buttons = new[] {
                root.Q<Button>("pause-button"), root.Q<Button>("resume-button"),
                root.Q<Button>("restart-button"), root.Q<Button>("pause-settings-button"),
                root.Q<Button>("menu-button"), root.Q<Button>("outcome-restart-button"),
                root.Q<Button>("outcome-menu-button")
            };
            m_Actions = new Action[] { Pause, Resume, Restart, m_Settings.Show, Menu, Restart, Menu };
            for (int i = 0; i < m_Buttons.Length; i++) m_Buttons[i].clicked += m_Actions[i];
            m_LastWave = m_LastPhase = m_LastRemaining = m_LastBiomass = m_LastScrap = int.MinValue;
            m_LastHealth = m_LastMaxHealth = int.MinValue;
            m_LastFraction = -1f;
            m_HasOutcome = false;
            ApplyState(GameState.Boot);
            BindFlow();
        }

        void BindFlow()
        {
            var flow = GameFlow.Instance;
            if (ReferenceEquals(flow, m_Flow)) return;
            if (m_Flow != null) m_Flow.StateChanged -= OnStateChanged;
            m_Flow = flow;
            if (m_Flow != null) m_Flow.StateChanged += OnStateChanged;
            ApplyState(m_Flow != null ? m_Flow.State : GameState.Boot);
        }

        void Update()
        {
            m_SafeArea.Update();
            BindFlow();
            if (m_Flow == null) return;
            var snapshot = m_Flow.Snapshot;
            if (!snapshot.Valid) return;

            SetNumber(m_Wave, "WAVE", snapshot.Wave, ref m_LastWave);
            SetNumber(m_Remaining, "REMAINING", snapshot.Remaining, ref m_LastRemaining);
            SetNumber(m_Biomass, "BIO", Mathf.FloorToInt(snapshot.Biomass), ref m_LastBiomass);
            SetNumber(m_Scrap, "SCRAP", Mathf.FloorToInt(snapshot.Scrap), ref m_LastScrap);
            if (m_LastPhase != (int)snapshot.Phase)
            {
                m_LastPhase = (int)snapshot.Phase;
                m_Phase.text = snapshot.Phase switch {
                    WavePhase.Preparing => "PREPARING",
                    WavePhase.Spawning => "HORDE INBOUND",
                    WavePhase.Fighting => "PURGE ACTIVE",
                    _ => "AWAITING SIGNAL"
                };
            }

            int health = Mathf.CeilToInt(snapshot.CoreHealth);
            int maxHealth = Mathf.CeilToInt(snapshot.CoreMaxHealth);
            if (health != m_LastHealth || maxHealth != m_LastMaxHealth)
            {
                m_LastHealth = health;
                m_LastMaxHealth = maxHealth;
                m_Core.text = $"CORE {health} / {maxHealth}";
            }
            float fraction = Mathf.Clamp01(snapshot.CoreFraction);
            if (fraction != m_LastFraction)
            {
                m_LastFraction = fraction;
                m_CoreFill.style.width = Length.Percent(fraction * 100f);
                m_CoreFill.EnableInClassList("bar__fill--warning", fraction >= 0.25f && fraction < 0.5f);
                m_CoreFill.EnableInClassList("bar__fill--danger", fraction < 0.25f);
            }
        }

        static void SetNumber(Label label, string prefix, int value, ref int cached)
        {
            if (value == cached) return;
            cached = value;
            label.text = $"{prefix} {value}";
        }

        void OnStateChanged(GameState previous, GameState next) => ApplyState(next);

        void ApplyState(GameState state)
        {
            bool ended = state == GameState.GameOver || state == GameState.Victory;
            m_Hud.style.display = state == GameState.Playing || state == GameState.Paused || ended
                ? DisplayStyle.Flex : DisplayStyle.None;
            m_PauseScreen.style.display = state == GameState.Paused ? DisplayStyle.Flex : DisplayStyle.None;
            m_OutcomeScreen.style.display = ended ? DisplayStyle.Flex : DisplayStyle.None;
            m_Settings.Hide();
            m_Buttons[0].SetEnabled(state == GameState.Playing);
            if (ended && m_Flow != null) ShowOutcome(state, m_Flow.Outcome);
        }

        void ShowOutcome(GameState state, RunOutcome outcome)
        {
            if (!m_HasOutcome || state != m_LastOutcomeState)
                m_OutcomeTitle.text = state == GameState.Victory ? "NETWORK SECURED" : "CORE LOST";
            if (!m_HasOutcome || outcome.WaveReached != m_LastOutcome.WaveReached || outcome.WavesToWin != m_LastOutcome.WavesToWin)
                m_OutcomeWave.text = $"WAVE {outcome.WaveReached} / {outcome.WavesToWin}";
            if (!m_HasOutcome || outcome.Biomass != m_LastOutcome.Biomass)
                m_OutcomeBiomass.text = $"BIOMASS {outcome.Biomass:0}";
            if (!m_HasOutcome || outcome.Scrap != m_LastOutcome.Scrap)
                m_OutcomeScrap.text = $"SCRAP {outcome.Scrap:0}";
            if (!m_HasOutcome || outcome.BestWave != m_LastOutcome.BestWave || outcome.NewBest != m_LastOutcome.NewBest)
                m_OutcomeBest.text = outcome.NewBest ? $"NEW BEST: WAVE {outcome.BestWave}" : $"BEST WAVE {outcome.BestWave}";
            m_LastOutcome = outcome;
            m_LastOutcomeState = state;
            m_HasOutcome = true;
        }

        void Pause() { if (m_Flow != null) m_Flow.Pause(); }
        void Resume() { if (m_Flow != null) m_Flow.Resume(); }
        void Restart() { if (m_Flow != null) m_Flow.Restart(); }
        void Menu() { if (m_Flow != null) m_Flow.QuitToMenu(); }

        void OnDisable()
        {
            if (m_Flow != null) m_Flow.StateChanged -= OnStateChanged;
            m_Flow = null;
            for (int i = 0; i < m_Buttons.Length; i++) m_Buttons[i].clicked -= m_Actions[i];
            m_Settings.Dispose();
            m_SafeArea.Dispose();
        }
    }
}
