namespace ZeroSynth.Flow
{
    public enum GameState
    {
        /// <summary>Process just started; GameFlow is initialising.</summary>
        Boot,

        MainMenu,

        /// <summary>Between scenes. The ECS world is being reset.</summary>
        Loading,

        Playing,
        Paused,

        /// <summary>The core was destroyed.</summary>
        GameOver,

        /// <summary>The final wave was cleared.</summary>
        Victory,
    }

    /// <summary>Scene names are part of the naming convention (rule R9).</summary>
    public static class SceneNames
    {
        public const string Boot = "SC_Boot";
        public const string Loading = "SC_Loading";
        public const string MainMenu = "SC_MainMenu";
        public const string Game = "SC_Game";
    }
}
