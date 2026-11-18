namespace ZeroSynth.Flow
{
    /// <summary>
    /// A plain managed copy of the run state, refreshed once per frame by
    /// GameFlow. UI reads this and never touches the ECS world: that keeps UI
    /// code ignorant of entities, and keeps world queries off the per-element
    /// path.
    /// </summary>
    public struct GameSnapshot
    {
        /// <summary>False until the run exists, e.g. in menus.</summary>
        public bool Valid;

        /// <summary>1-based wave number. Zero before the first wave begins.</summary>
        public int Wave;

        public WavePhase Phase;

        /// <summary>Seconds left in Preparing, or seconds elapsed in the other phases.</summary>
        public float PhaseTimer;

        /// <summary>Agents still to be released this wave.</summary>
        public int Remaining;

        /// <summary>Agents currently alive in the world.</summary>
        public int ActiveAgents;

        public float Biomass;
        public float Scrap;

        public float CoreHealth;
        public float CoreMaxHealth;

        /// <summary>
        /// Where the next wave (while preparing) or this one (once underway)
        /// comes from. <see cref="Fronts"/>.Arc of zero means all round.
        /// </summary>
        public SpawnFronts Fronts;

        /// <summary>World position the fronts are measured around, and how far out they spawn.</summary>
        public Unity.Mathematics.float3 FrontCentre;
        public float FrontRadius;

        public float CoreFraction => CoreMaxHealth > 0f ? CoreHealth / CoreMaxHealth : 0f;
    }

    /// <summary>What the game over and victory screens show.</summary>
    public struct RunOutcome
    {
        public bool Won;
        public int WaveReached;
        public int WavesToWin;
        public float Biomass;
        public float Scrap;

        /// <summary>Highest wave reached in any run, including this one.</summary>
        public int BestWave;

        /// <summary>This run beat the previous best.</summary>
        public bool NewBest;
    }
}
