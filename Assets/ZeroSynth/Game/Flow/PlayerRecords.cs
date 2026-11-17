using UnityEngine;

namespace ZeroSynth.Flow
{
    /// <summary>
    /// Persistent bests. Deliberately separate from <see cref="GameSettings"/>:
    /// settings are what the player chose, records are what they achieved, and
    /// resetting one should never touch the other.
    /// </summary>
    public static class PlayerRecords
    {
        const string BestWaveKey = "zerosynth.bestwave";

        /// <summary>The highest wave reached in any run. Zero if none yet.</summary>
        public static int BestWave => PlayerPrefs.GetInt(BestWaveKey, 0);

        /// <summary>
        /// Records a result. Returns true if it beat the previous best.
        /// </summary>
        public static bool SubmitWave(int wave)
        {
            if (wave <= BestWave)
                return false;

            PlayerPrefs.SetInt(BestWaveKey, wave);
            PlayerPrefs.Save();
            return true;
        }
    }
}
