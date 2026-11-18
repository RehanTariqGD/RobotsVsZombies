using Unity.Entities;

namespace ZeroSynth.Flow
{
    /// <summary>
    /// Reads run state out of the ECS world, and drives the simulation's pause.
    /// </summary>
    /// <remarks>
    /// Queries are cached per world and rebuilt when the world changes, since
    /// the world is replaced between runs.
    /// </remarks>
    public sealed class RunReader
    {
        World m_World;
        EntityQuery m_RunQuery;
        EntityQuery m_CoreQuery;
        EntityQuery m_ActiveQuery;
        EntityQuery m_TargetQuery;

        bool Bind()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                m_World = null;
                return false;
            }

            if (world == m_World)
                return true;

            var em = world.EntityManager;
            m_RunQuery = em.CreateEntityQuery(ComponentType.ReadOnly<WaveState>());
            m_CoreQuery = em.CreateEntityQuery(ComponentType.ReadOnly<Core>(), ComponentType.ReadOnly<Health>());
            m_ActiveQuery = em.CreateEntityQuery(ComponentType.ReadOnly<HordeAgent>(), ComponentType.ReadOnly<Active>());
            m_TargetQuery = em.CreateEntityQuery(ComponentType.ReadOnly<HordeTarget>());
            m_World = world;
            return true;
        }

        public bool TryRead(out GameSnapshot snapshot)
        {
            snapshot = default;
            if (!Bind() || m_RunQuery.IsEmpty)
                return false;

            var em = m_World.EntityManager;
            var run = m_RunQuery.GetSingletonEntity();
            var wave = em.GetComponentData<WaveState>(run);
            var economy = em.GetComponentData<Economy>(run);

            snapshot.Valid = true;
            snapshot.Wave = wave.Index;
            snapshot.Phase = wave.Phase;
            snapshot.PhaseTimer = wave.Timer;
            snapshot.Remaining = wave.RemainingTotal;
            snapshot.ActiveAgents = m_ActiveQuery.CalculateEntityCount();
            snapshot.Biomass = economy.Biomass;
            snapshot.Scrap = economy.MetalScrap;

            if (em.HasComponent<WaveConfig>(run) && em.HasComponent<SpawnProfile>(run))
            {
                var config = em.GetComponentData<WaveConfig>(run);
                int upcoming = wave.Phase == WavePhase.Preparing ? wave.Index + 1 : wave.Index;
                snapshot.Fronts = WaveDirectorSystem.Fronts(upcoming, config);
                snapshot.FrontRadius = em.GetComponentData<SpawnProfile>(run).InnerRadius;
            }

            if (!m_TargetQuery.IsEmpty)
                snapshot.FrontCentre = m_TargetQuery.GetSingleton<HordeTarget>().Position;

            if (!m_CoreQuery.IsEmpty)
            {
                var health = em.GetComponentData<Health>(m_CoreQuery.GetSingletonEntity());
                snapshot.CoreHealth = health.Current;
                snapshot.CoreMaxHealth = health.Max;
            }

            return true;
        }

        /// <summary>
        /// Freezes or resumes the whole simulation. Camera, UI and rendering
        /// keep running; only the simulation group stops. Not a time scale of
        /// zero, which would leave every system running on a zero delta.
        /// </summary>
        public void SetSimulationPaused(bool paused)
        {
            if (!Bind()) return;

            var group = m_World.GetExistingSystemManaged<SimulationSystemGroup>();
            if (group != null)
                group.Enabled = !paused;

            var em = m_World.EntityManager;
            if (!m_RunQuery.IsEmpty)
            {
                // SimClock lives on the same entity as the wave state.
                var run = m_RunQuery.GetSingletonEntity();
                if (em.HasComponent<SimClock>(run))
                {
                    var clock = em.GetComponentData<SimClock>(run);
                    clock.Paused = paused;
                    em.SetComponentData(run, clock);
                }
            }
        }
    }
}
