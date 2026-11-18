using UnityEngine;
using UnityEngine.UIElements;
using ZeroSynth.Authoring;
using ZeroSynth.Build;
using ZeroSynth.Flow;

namespace ZeroSynth.UI
{
    /// <summary>
    /// A few one-line prompts across the first waves, then never again.
    /// </summary>
    /// <remarks>
    /// Driven entirely by what the player does and what wave it is, so it
    /// needs nothing from the simulation and cannot get stuck: every step also
    /// gives up after a timeout. It adds its own label to the build overlay's
    /// root, so there is no UXML to keep in step. Once the last step ends a
    /// flag is saved and the component switches itself off on later runs.
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class TutorialPrompts : MonoBehaviour
    {
        const string DoneKey = "zs_tutorial_done";

        /// <summary>Seconds any one prompt waits before moving on without being obeyed.</summary>
        [SerializeField, Min(5f)] float m_StepTimeout = 25f;

        enum Step { Build, Possess, Foundry, Special, Finished }

        static readonly string[] s_Text =
        {
            "SPEND YOUR SCRAP\nPICK A TURRET IN THE BAR BELOW, THEN TAP THE GROUND TO PLACE IT",
            "TAKE CONTROL\nTAP A TURRET AND DRAG TO AIM IT. TAP IT AGAIN TO LET GO",
            "KILLS DROP BIOMASS\nBUILD A FOUNDRY TO TURN IT INTO SCRAP. IT NEEDS POWER",
            "HEAVY SUPPORT\nTAP THE GOLIATH MECH TO DRIVE IT, OR THE GUNSHIP TO FLY. THEY RUN ON LIMITS"
        };

        Label m_Label;
        Step m_Step = Step.Build;
        float m_StepStarted;
        bool m_StepArmed;
        bool m_BuiltSinceStepStart;
        BuildController m_Build;

        void OnEnable()
        {
            if (PlayerPrefs.GetInt(DoneKey, 0) == 1)
            {
                enabled = false;
                return;
            }

            var root = GetComponent<UIDocument>().rootVisualElement;
            m_Label = new Label();
            m_Label.AddToClassList("build-toast");
            m_Label.pickingMode = PickingMode.Ignore;
            m_Label.style.position = Position.Absolute;
            m_Label.style.top = new Length(18f, LengthUnit.Percent);
            m_Label.style.left = new Length(20f, LengthUnit.Percent);
            m_Label.style.right = new Length(20f, LengthUnit.Percent);
            m_Label.style.whiteSpace = WhiteSpace.Normal;
            m_Label.style.display = DisplayStyle.None;
            root.Add(m_Label);

            m_Step = Step.Build;
            m_StepArmed = false;
            m_BuiltSinceStepStart = false;
        }

        void OnDisable()
        {
            if (m_Build != null) m_Build.Message -= OnBuildMessage;
            m_Build = null;

            if (m_Label != null && m_Label.parent != null)
                m_Label.parent.Remove(m_Label);
            m_Label = null;
        }

        void Update()
        {
            if (m_Label == null) return;

            WatchBuild();

            var flow = GameFlow.Instance;
            var snapshot = flow != null ? flow.Snapshot : default;

            // Nothing is shown outside live play, and nothing advances then
            // either, so a pause cannot burn through the timeouts.
            if (flow == null || flow.State != GameState.Playing || !snapshot.Valid)
            {
                SetVisible(false);
                return;
            }

            if (m_Step == Step.Finished)
            {
                SetVisible(false);
                return;
            }

            if (!m_StepArmed)
            {
                if (!ReadyToStart(m_Step, snapshot))
                {
                    SetVisible(false);
                    return;
                }

                m_StepArmed = true;
                m_StepStarted = Time.unscaledTime;
                m_BuiltSinceStepStart = false;
                m_Label.text = s_Text[(int)m_Step];
            }

            SetVisible(true);

            if (Completed(m_Step, snapshot) || Time.unscaledTime - m_StepStarted > m_StepTimeout)
                Advance();
        }

        /// <summary>A step does not begin until the game reaches the point it talks about.</summary>
        static bool ReadyToStart(Step step, GameSnapshot snapshot)
        {
            switch (step)
            {
                case Step.Build: return snapshot.Wave == 0;
                case Step.Possess: return true;
                case Step.Foundry: return snapshot.Wave >= 2;
                case Step.Special: return snapshot.Wave >= 3;
                default: return false;
            }
        }

        bool Completed(Step step, GameSnapshot snapshot)
        {
            switch (step)
            {
                case Step.Build:
                    return m_BuiltSinceStepStart || snapshot.Wave >= 1;
                case Step.Possess:
                    return PossessionController.ActiveMode != PossessionMode.None || snapshot.Wave >= 3;
                case Step.Foundry:
                    return m_BuiltSinceStepStart || snapshot.Wave >= 5;
                case Step.Special:
                    return PossessionController.ActiveMode == PossessionMode.Mech
                           || PossessionController.ActiveMode == PossessionMode.Gunship
                           || snapshot.Wave >= 6;
                default:
                    return true;
            }
        }

        void Advance()
        {
            m_Step++;
            m_StepArmed = false;

            if (m_Step == Step.Finished)
            {
                PlayerPrefs.SetInt(DoneKey, 1);
                PlayerPrefs.Save();
            }
        }

        void SetVisible(bool visible)
        {
            var display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (m_Label.style.display != display)
                m_Label.style.display = display;
        }

        /// <summary>BuildController is created with the scene, after this enables, so it is picked up lazily.</summary>
        void WatchBuild()
        {
            var build = BuildController.Instance;
            if (ReferenceEquals(build, m_Build)) return;

            if (!ReferenceEquals(m_Build, null)) m_Build.Message -= OnBuildMessage;
            m_Build = build;
            if (m_Build != null) m_Build.Message += OnBuildMessage;
        }

        void OnBuildMessage(string message)
        {
            if (message != null && message.EndsWith("BUILT"))
                m_BuiltSinceStepStart = true;
        }
    }
}
