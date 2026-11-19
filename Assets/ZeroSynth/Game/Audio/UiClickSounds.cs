using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace ZeroSynth.Audio
{
    /// <summary>
    /// Plays a tick when any button in a UI document is pressed.
    /// </summary>
    /// <remarks>
    /// Added to every UIDocument automatically (see <see cref="Installer"/>),
    /// so a new screen gets button feedback without anyone remembering to wire
    /// it. Listens on the document root during the trickle-down phase, which
    /// sees the click whichever child of the button was hit.
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class UiClickSounds : MonoBehaviour
    {
        VisualElement m_Root;

        void OnEnable()
        {
            m_Root = GetComponent<UIDocument>().rootVisualElement;
            m_Root?.RegisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
        }

        void OnDisable()
        {
            m_Root?.UnregisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
            m_Root = null;
        }

        static void OnClick(ClickEvent evt)
        {
            if (!(evt.target is VisualElement element)) return;

            var button = element as Button ?? element.GetFirstAncestorOfType<Button>();
            if (button == null || !button.enabledInHierarchy) return;

            AudioDirector.Instance?.Play(Sfx.UiClick, 0.6f, 0.04f, 0.03f);
        }

        /// <summary>Finds documents as scenes load and gives each a listener.</summary>
        static class Installer
        {
            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
            static void Start()
            {
                SceneManager.sceneLoaded += (scene, mode) => Install();
                Install();
            }

            static void Install()
            {
                foreach (var document in Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (document.GetComponent<UiClickSounds>() == null)
                        document.gameObject.AddComponent<UiClickSounds>();
                }
            }
        }
    }
}
