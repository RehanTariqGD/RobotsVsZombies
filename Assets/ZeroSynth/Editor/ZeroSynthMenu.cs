using UnityEditor;
using UnityEngine;

namespace ZeroSynth.EditorTools
{
    /// <summary>Small editor conveniences for testing.</summary>
    public static class ZeroSynthMenu
    {
        [MenuItem("ZeroSynth/Reset Tutorial")]
        static void ResetTutorial()
        {
            PlayerPrefs.DeleteKey("zs_tutorial_done");
            PlayerPrefs.Save();
            Debug.Log("[ZeroSynth] Tutorial prompts will show again on the next run.");
        }

        [MenuItem("ZeroSynth/Reset Best Wave and Settings")]
        static void ResetSaved()
        {
            if (!EditorUtility.DisplayDialog("Reset saved data",
                    "Clear the best wave, settings and tutorial flag stored in PlayerPrefs?", "Clear", "Cancel"))
                return;

            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            Debug.Log("[ZeroSynth] Saved data cleared.");
        }
    }
}
