using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using ZeroSynth.Authoring;
using ZeroSynth.Build;
using ZeroSynth.Flow;

namespace ZeroSynth.UI
{
    /// <summary>Routes touch controls through the build API without coupling the overlay to the simulation.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class BuildBarController : MonoBehaviour
    {
        GameFlow m_Flow;
        BuildController m_Build;
        VisualElement m_Screen, m_OptionsRoot;
        Button m_PauseButton, m_CancelButton, m_RotateButton, m_RepairButton, m_SellButton;
        Label m_Toast, m_PausedPill, m_Hint;
        int m_HintKey = -1;
        int m_BuildHintKey;
        SafeAreaPadding m_SafeArea;
        BuildOption[] m_Options = Array.Empty<BuildOption>();
        Button[] m_Buttons = Array.Empty<Button>();
        Action[] m_Actions = Array.Empty<Action>();
        bool[] m_Affordable = Array.Empty<bool>();
        bool m_ToastVisible;
        float m_ToastUntil;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            m_Screen = root.Q("build-screen");
            m_OptionsRoot = root.Q("build-options");
            m_PauseButton = root.Q<Button>("tactical-pause-button");
            m_CancelButton = root.Q<Button>("cancel-button");
            m_RotateButton = root.Q<Button>("rotate-button");
            m_RepairButton = root.Q<Button>("repair-button");
            m_SellButton = root.Q<Button>("sell-button");
            m_Toast = root.Q<Label>("build-toast");
            m_PausedPill = root.Q<Label>("paused-pill");
            m_Hint = root.Q<Label>("controls-hint");
            m_HintKey = -1;
            m_SafeArea = new SafeAreaPadding(root.Q("safe-area"));
            m_PauseButton.clicked += TogglePause;
            m_CancelButton.clicked += Cancel;
            m_RotateButton.clicked += Rotate;
            m_RepairButton.clicked += SelectRepair;
            m_SellButton.clicked += SelectSell;
            ApplyState(GameState.Boot);
            RefreshSelection();
            BindFlow();
            BindBuild();
        }

        void BindFlow()
        {
            var flow = GameFlow.Instance;
            if (ReferenceEquals(flow, m_Flow)) return;
            UnbindFlow();
            m_Flow = flow;
            if (m_Flow != null)
            {
                m_Flow.StateChanged += OnStateChanged;
                m_Flow.TacticalPauseChanged += RefreshPause;
            }
            ApplyState(m_Flow != null ? m_Flow.State : GameState.Boot);
        }

        /// <summary>Rebuilds bindings when a run supplies a new controller, including after this overlay enables.</summary>
        void BindBuild()
        {
            var build = BuildController.Instance;
            if (ReferenceEquals(build, m_Build)) return;
            UnbindBuild();
            HideToast();
            m_Build = build;
            if (m_Build != null)
            {
                int count = m_Build.Options.Count;
                m_Options = new BuildOption[count];
                m_Buttons = new Button[count];
                m_Actions = new Action[count];
                m_Affordable = new bool[count];
                for (int i = 0; i < count; i++)
                {
                    var option = m_Build.Options[i];
                    var button = new Button {
                        text = option.Label.ToUpperInvariant() + "  " + option.Cost.ToString("0.##", CultureInfo.InvariantCulture)
                    };
                    button.AddToClassList("button");
                    button.AddToClassList("build-button");
                    button.AddToClassList("build-option--locked");
                    m_Options[i] = option;
                    m_Buttons[i] = button;
                    m_Actions[i] = () => Select(option);
                    button.clicked += m_Actions[i];
                    m_OptionsRoot.Add(button);
                }
                m_Build.SelectionChanged += RefreshSelection;
                m_Build.Message += ShowToast;
            }
            RefreshSelection();
            RefreshAffordability();
        }

        /// <summary>
        /// Tells the player what a tap and a drag do right now. The same gesture
        /// means different things while building, aiming a turret and driving
        /// the mech, and nothing else on screen says which is active.
        /// </summary>
        static readonly string[] s_Hints =
        {
            "TAP A TURRET OR THE MECH TO TAKE CONTROL\nESC: PAUSE MENU",
            "AIMING TURRET\nHOLD AND DRAG: AIM  |  TAP IT AGAIN OR SPACE: RELEASE",
            "DRIVING MECH\nDRAG OR WASD: MOVE  |  TAP IT AGAIN OR SPACE: RELEASE",
            "BUILDING\nTAP THE GROUND TO PLACE  |  R OR ROTATE TURNS IT  |  CANCEL WHEN DONE",
            "REPAIR\nTAP A DAMAGED STRUCTURE  |  COSTS SCRAP PER MISSING HEALTH",
            "SELL\nTAP A STRUCTURE FOR HALF ITS COST BACK"
        };

        void RefreshHint()
        {
            // Keys below 1000 index s_Hints. The gunship states fold the fuel
            // percentage into the key, so the label is rebuilt only when the
            // number actually changes rather than allocating every frame.
            float fuel = PossessionController.GunshipFuel01;
            int pct = fuel < 0f ? -1 : Mathf.RoundToInt(fuel * 100f);

            int key;
            if (m_BuildHintKey != 0) key = m_BuildHintKey;
            else if (PossessionController.ActiveMode == PossessionMode.Turret) key = 1;
            else if (PossessionController.ActiveMode == PossessionMode.Mech) key = 2;
            else if (PossessionController.ActiveMode == PossessionMode.Gunship) key = 1000 + Mathf.Max(0, pct);
            else if (pct >= 0) key = 2000 + pct;
            else key = 0;

            if (key == m_HintKey) return;
            m_HintKey = key;

            if (key >= 2000)
                m_Hint.text = "TAP A TURRET, THE MECH OR THE GUNSHIP TO TAKE CONTROL\nESC: PAUSE MENU  |  GUNSHIP FUEL "
                              + pct.ToString(CultureInfo.InvariantCulture) + "%";
            else if (key >= 1000)
                m_Hint.text = "FLYING GUNSHIP  |  FUEL " + (key - 1000).ToString(CultureInfo.InvariantCulture)
                              + "%\nDRAG OR WASD: FLY  |  TAP IT AGAIN OR SPACE: RETURN TO PAD";
            else
                m_Hint.text = s_Hints[key];
        }

        void Update()
        {
            m_SafeArea.Update();
            BindFlow();
            BindBuild();
            RefreshHint();
            RefreshAffordability();
            if (m_ToastVisible && Time.unscaledTime >= m_ToastUntil) HideToast();
        }

        /// <summary>Caches affordability so scrap polling does not dirty styles every frame.</summary>
        void RefreshAffordability()
        {
            var snapshot = m_Flow != null ? m_Flow.Snapshot : default;
            for (int i = 0; i < m_Options.Length; i++)
            {
                bool affordable = snapshot.Valid && snapshot.Scrap >= m_Options[i].Cost;
                if (affordable == m_Affordable[i]) continue;
                m_Affordable[i] = affordable;
                m_Buttons[i].EnableInClassList("build-option--locked", !affordable);
            }
        }

        void RefreshSelection()
        {
            var mode = m_Build != null ? m_Build.Mode : BuildMode.None;
            bool selected = m_Build != null && m_Build.HasSelection;
            m_CancelButton.style.display = selected ? DisplayStyle.Flex : DisplayStyle.None;
            m_RotateButton.style.display = m_Build != null && m_Build.CanRotate ? DisplayStyle.Flex : DisplayStyle.None;
            m_RepairButton.EnableInClassList("build-option--selected", mode == BuildMode.Repair);
            m_SellButton.EnableInClassList("build-option--selected", mode == BuildMode.Sell);
            for (int i = 0; i < m_Options.Length; i++)
                m_Buttons[i].EnableInClassList("build-option--selected", mode == BuildMode.Place && m_Build.Selected == m_Options[i].Kind);
            m_BuildHintKey = mode switch {
                BuildMode.Place => 3,
                BuildMode.Repair => 4,
                BuildMode.Sell => 5,
                _ => 0
            };
            RefreshHint();
        }

        void RefreshPause()
        {
            bool paused = m_Flow != null && m_Flow.TacticalPause;
            string text = paused ? "RESUME" : "PAUSE";
            if (m_PauseButton.text != text) m_PauseButton.text = text;
            m_PausedPill.style.display = paused ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void OnStateChanged(GameState previous, GameState next) => ApplyState(next);

        void ApplyState(GameState state)
        {
            m_Screen.style.display = state == GameState.Playing ? DisplayStyle.Flex : DisplayStyle.None;
            if (state != GameState.Playing) HideToast();
            RefreshPause();
        }

        void Select(BuildOption option)
        {
            if (m_Build == null || m_Flow == null || m_Flow.State != GameState.Playing) return;
            if (m_Build.Mode == BuildMode.Place && m_Build.Selected == option.Kind) m_Build.Cancel();
            else m_Build.Select(option.Kind);
        }

        void Rotate()
        {
            if (m_Build == null || m_Flow == null || m_Flow.State != GameState.Playing) return;
            m_Build.Rotate();
        }

        void SelectRepair()
        {
            if (m_Build == null || m_Flow == null || m_Flow.State != GameState.Playing) return;
            if (m_Build.Mode == BuildMode.Repair) m_Build.Cancel();
            else m_Build.SelectRepair();
        }

        void SelectSell()
        {
            if (m_Build == null || m_Flow == null || m_Flow.State != GameState.Playing) return;
            if (m_Build.Mode == BuildMode.Sell) m_Build.Cancel();
            else m_Build.SelectSell();
        }

        void Cancel()
        {
            if (m_Build != null) m_Build.Cancel();
        }

        void TogglePause()
        {
            if (m_Flow != null) m_Flow.SetTacticalPause(!m_Flow.TacticalPause);
        }

        /// <summary>Uses unscaled time so feedback expires even while tactical pause freezes the simulation.</summary>
        void ShowToast(string message)
        {
            if (m_Flow == null || m_Flow.State != GameState.Playing) return;
            if (m_Toast.text != message) m_Toast.text = message;
            m_ToastUntil = Time.unscaledTime + 1.5f;
            m_ToastVisible = true;
            m_Toast.style.display = DisplayStyle.Flex;
        }

        void HideToast()
        {
            m_ToastVisible = false;
            m_Toast.style.display = DisplayStyle.None;
        }

        void UnbindFlow()
        {
            if (!ReferenceEquals(m_Flow, null))
            {
                m_Flow.StateChanged -= OnStateChanged;
                m_Flow.TacticalPauseChanged -= RefreshPause;
            }
            m_Flow = null;
        }

        void UnbindBuild()
        {
            if (!ReferenceEquals(m_Build, null))
            {
                m_Build.SelectionChanged -= RefreshSelection;
                m_Build.Message -= ShowToast;
            }
            m_Build = null;
            for (int i = 0; i < m_Buttons.Length; i++) m_Buttons[i].clicked -= m_Actions[i];
            m_OptionsRoot.Clear();
            m_Options = Array.Empty<BuildOption>();
            m_Buttons = Array.Empty<Button>();
            m_Actions = Array.Empty<Action>();
            m_Affordable = Array.Empty<bool>();
        }

        void OnDisable()
        {
            UnbindFlow();
            UnbindBuild();
            m_PauseButton.clicked -= TogglePause;
            m_CancelButton.clicked -= Cancel;
            m_RotateButton.clicked -= Rotate;
            m_RepairButton.clicked -= SelectRepair;
            m_SellButton.clicked -= SelectSell;
            m_SafeArea.Dispose();
            ApplyState(GameState.Boot);
        }
    }
}
