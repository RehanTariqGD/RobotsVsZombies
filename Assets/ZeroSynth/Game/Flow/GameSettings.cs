using System;
using UnityEngine;

namespace ZeroSynth.Flow
{
    /// <summary>
    /// Player-facing options. Persisted as JSON in PlayerPrefs.
    /// </summary>
    /// <remarks>
    /// Volumes are 0-1. Music and SFX are stored now so the settings screen is
    /// final; only Master has something to drive until audio exists.
    /// </remarks>
    [Serializable]
    public sealed class GameSettings
    {
        const string Key = "zerosynth.settings";

        public float MasterVolume = 1f;
        public float MusicVolume = 0.8f;
        public float SfxVolume = 1f;

        /// <summary>Index into QualitySettings.names.</summary>
        public int QualityLevel = -1;

        public bool ShowDebugHud = true;

        public static GameSettings Load()
        {
            try
            {
                if (PlayerPrefs.HasKey(Key))
                {
                    var loaded = JsonUtility.FromJson<GameSettings>(PlayerPrefs.GetString(Key));
                    if (loaded != null)
                    {
                        loaded.Clamp();
                        return loaded;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ZeroSynth] Settings could not be read, using defaults. {e.Message}");
            }

            return new GameSettings();
        }

        public void Save()
        {
            Clamp();
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        public void Clamp()
        {
            MasterVolume = Mathf.Clamp01(MasterVolume);
            MusicVolume = Mathf.Clamp01(MusicVolume);
            SfxVolume = Mathf.Clamp01(SfxVolume);

            int max = QualitySettings.names.Length - 1;
            if (QualityLevel > max) QualityLevel = max;
        }
    }
}
