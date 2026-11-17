using UnityEngine;
using UnityEngine.UIElements;
using ZeroSynth.Flow;

namespace ZeroSynth.UI
{
    /// <summary>Keeps scene navigation in GameFlow while allowing an unbootstrapped menu to render.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] Texture2D m_Backdrop;

        Button m_Play;
        Button m_SettingsButton;
        Button m_Quit;
        SettingsPanelController m_Settings;
        SafeAreaPadding m_SafeArea;
        bool m_LoggedMissingFlow;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            m_Play = root.Q<Button>("play-button");
            m_SettingsButton = root.Q<Button>("settings-button");
            m_Quit = root.Q<Button>("quit-button");
            root.Q<Label>("version-label").text = $"BUILD {Application.version}";
            if (m_Backdrop != null)
                root.Q("backdrop").style.backgroundImage = new StyleBackground(m_Backdrop);
            m_Settings = new SettingsPanelController(root.Q("settings-panel"));
            m_Settings.Hide();
            m_Settings.Closed += OnSettingsClosed;
            m_SafeArea = new SafeAreaPadding(root.Q("safe-area"));
            m_Play.clicked += Play;
            m_SettingsButton.clicked += m_Settings.Show;
            m_Quit.clicked += Quit;
            m_Play.SetEnabled(GameFlow.Instance != null);
        }

        void Update()
        {
            m_SafeArea.Update();
            bool available = GameFlow.Instance != null;
            if (m_Play.enabledSelf != available) m_Play.SetEnabled(available);
            if (!available && !m_LoggedMissingFlow)
            {
                Debug.LogWarning("[ZeroSynth] Main menu requires GameFlow from SC_Boot. Play is disabled.", this);
                m_LoggedMissingFlow = true;
            }
        }

        void Play()
        {
            if (GameFlow.Instance != null) GameFlow.Instance.StartGame();
        }

        void Quit()
        {
            if (GameFlow.Instance != null) GameFlow.Instance.QuitApplication();
        }

        void OnSettingsClosed() => m_SettingsButton.Focus();

        void OnDisable()
        {
            m_Play.clicked -= Play;
            m_SettingsButton.clicked -= m_Settings.Show;
            m_Quit.clicked -= Quit;
            m_Settings.Closed -= OnSettingsClosed;
            m_Settings.Dispose();
            m_SafeArea.Dispose();
        }
    }
}
