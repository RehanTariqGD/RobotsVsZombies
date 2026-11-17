using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Random = Unity.Mathematics.Random;

namespace ZeroSynth
{
    /// <summary>
    /// Drives the prepare / spawn / fight cycle and releases agents from the
    /// pool, mixing the unit kinds.
    /// </summary>
    /// <remarks>
    /// Composition is derived from the run seed and the wave index, so a run
    /// replays identically — which is what lets the save in M8 persist a seed
    /// and a wave number instead of several thousand agents.
    ///
    /// Spawning never creates entities. It takes dormant ones from the pool
    /// and enables them, so a wave costs no structural changes at all. Each
    /// kind has its own pool, so a wave that wants more of a kind than remain
    /// simply gets fewer of that kind.
    ///
    /// Each frame picks ONE kind, weighted by how much of it is left in the
    /// wave, and spends that frame's whole spawn budget on it. Over many
    /// frames that mixes the kinds in proportion, and it needs only a single
    /// pool lookup per frame rather than one per agent.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(AgentSteeringSystem))]
    public partial struct WaveDirectorSystem : ISystem
    {
        EntityQuery m_DormantQuery;
        EntityQuery m_ActiveQuery;

        public void OnCreate(ref SystemState state)
        {
            m_DormantQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<HordeAgent, AgentKind>()
                .WithDisabled<Active>()
                .Build(ref state);

            m_ActiveQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<HordeAgent, Active>()
                .Build(ref state);

            state.RequireForUpdate<WaveConfig>();
            state.RequireForUpdate<WaveState>();
            state.RequireForUpdate<SpawnProfile>();
            state.RequireForUpdate<UnitTable>();
            state.RequireForUpdate<HordeTarget>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var clock = SystemAPI.GetSingleton<SimClock>();
            if (clock.Paused)
                return;

            float dt = SystemAPI.Time.DeltaTime * math.max(0f, clock.Speed);

            var config = SystemAPI.GetSingleton<WaveConfig>();
            var profile = SystemAPI.GetSingleton<SpawnProfile>();
            var table = SystemAPI.GetSingleton<UnitTable>();
            var target = SystemAPI.GetSingleton<HordeTarget>();

            var waveRw = SystemAPI.GetSingletonRW<WaveState>();
            var wave = waveRw.ValueRO;

            switch (wave.Phase)
            {
                case WavePhase.Preparing:
                    wave.Timer -= dt;
                    if (wave.Timer <= 0f)
                        BeginWave(ref wave, config);
                    break;

                case WavePhase.Spawning:
                    wave.Timer += dt;
                    Release(ref state, ref wave, config, profile, table, target, dt);
                    if (wave.RemainingTotal <= 0)
                        wave.Phase = WavePhase.Fighting;
                    break;

                case WavePhase.Fighting:
                    wave.Timer += dt;
                    if (m_ActiveQuery.IsEmpty)
                    {
                        var economy = SystemAPI.GetSingletonRW<Economy>();
                        economy.ValueRW.MetalScrap += config.ScrapPerWave;

                        wave.Phase = WavePhase.Preparing;
                        wave.Timer = config.PrepareDuration;
                    }
                    break;
            }

            waveRw.ValueRW = wave;
        }

        static void BeginWave(ref WaveState wave, WaveConfig config)
        {
            wave.Index++;
            wave.Phase = WavePhase.Spawning;
            wave.Timer = 0f;
            wave.Released = 0;
            wave.SpawnAccumulator = 0f;
            wave.Remaining = Composition(wave.Index, config);
        }

        /// <summary>
        /// How many of each kind a wave contains. Each kind grows from its own
        /// first wave, so a new type arrives small instead of at full size.
        /// </summary>
        public static int4 Composition(int index, WaveConfig config)
        {
            float growth = 1f + config.CountGrowth;

            int runners = (int)math.round(config.BaseCount * math.pow(growth, math.max(0, index - 1)));

            int spitters = 0;
            if (config.SpitterBase > 0 && index >= config.SpitterFirstWave)
                spitters = (int)math.round(config.SpitterBase * math.pow(growth, index - config.SpitterFirstWave));

            int burrowers = 0;
            if (config.BurrowerBase > 0 && index >= config.BurrowerFirstWave)
                burrowers = (int)math.round(config.BurrowerBase * math.pow(growth, index - config.BurrowerFirstWave));

            int titans = 0;
            if (config.TitanEvery > 0 && index % config.TitanEvery == 0)
                titans = 1 + index / (config.TitanEvery * 2);

            return new int4(runners, spitters, burrowers, titans);
        }

        void Release(
            ref SystemState state,
            ref WaveState wave,
            WaveConfig config,
            SpawnProfile profile,
            UnitTable table,
            HordeTarget target,
            float dt)
        {
            wave.SpawnAccumulator += config.SpawnRate * dt;

            int budget = (int)wave.SpawnAccumulator;
            if (budget <= 0) return;
            wave.SpawnAccumulator -= budget;

            // Seeded on the wave index and the running release count, so the
            // same wave of the same run always produces the same units in the
            // same places regardless of frame pacing.
            var random = Random.CreateFromIndex(config.Seed ^ (uint)(wave.Index * 7919 + wave.Released));

            var kind = PickKind(wave.Remaining, ref random);

            m_DormantQuery.SetSharedComponentFilter(new AgentKind { Value = kind });
            var dormant = m_DormantQuery.ToEntityArray(Allocator.Temp);
            m_DormantQuery.ResetFilter();

            if (dormant.Length == 0)
            {
                // Pool for this kind is exhausted. Drop the rest of that kind
                // rather than stalling in Spawning waiting for corpses.
                wave.Remaining[(int)kind] = 0;
                dormant.Dispose();
                return;
            }

            int take = math.min(math.min(budget, wave.Remaining[(int)kind]), dormant.Length);

            var stats = table.Get(kind);
            float speedScale = math.pow(1f + config.SpeedGrowth, math.max(0, wave.Index - 1));
            var em = state.EntityManager;

            for (int i = 0; i < take; i++)
            {
                var entity = dormant[i];

                float angle = random.NextFloat(0f, math.PI * 2f);
                float radius = random.NextFloat(profile.InnerRadius, profile.OuterRadius);
                float3 position = target.Position + new float3(
                    math.cos(angle) * radius, 0f, math.sin(angle) * radius);
                position.y = 0f;

                bool tunnels = em.HasComponent<Underground>(entity);
                if (tunnels)
                    position.y = profile.BurrowDepth;

                em.SetComponentData(entity, LocalTransform.FromPosition(position));
                em.SetComponentData(entity, new HordeAgent
                {
                    Speed = random.NextFloat(stats.MinSpeed, stats.MaxSpeed) * speedScale
                });
                em.SetComponentData(entity, new Health
                {
                    Current = stats.Health,
                    Max = stats.Health
                });

                if (em.HasComponent<AgentCombat>(entity))
                {
                    var combat = em.GetComponentData<AgentCombat>(entity);
                    combat.Cooldown = 0f;
                    em.SetComponentData(entity, combat);
                }

                if (em.HasComponent<RangedAttacker>(entity))
                {
                    var ranged = em.GetComponentData<RangedAttacker>(entity);
                    ranged.Target = Entity.Null;
                    ranged.Cooldown = 0f;
                    // Staggered so a whole wave of spitters does not rescan
                    // the structure list on the same frame.
                    ranged.RetargetTimer = random.NextFloat(0f, 0.4f);
                    em.SetComponentData(entity, ranged);
                    em.SetComponentEnabled<Holding>(entity, false);
                }

                if (tunnels)
                    em.SetComponentEnabled<Underground>(entity, true);

                em.SetComponentEnabled<Active>(entity, true);
            }

            wave.Remaining[(int)kind] -= take;
            wave.Released += take;

            dormant.Dispose();
        }

        /// <summary>Weighted pick over what is left in the wave.</summary>
        static UnitKind PickKind(int4 remaining, ref Random random)
        {
            int total = remaining.x + remaining.y + remaining.z + remaining.w;
            if (total <= 0)
                return UnitKind.Runner;

            int roll = random.NextInt(0, total);

            if (roll < remaining.x) return UnitKind.Runner;
            roll -= remaining.x;
            if (roll < remaining.y) return UnitKind.Spitter;
            roll -= remaining.y;
            if (roll < remaining.z) return UnitKind.Burrower;
            return UnitKind.Titan;
        }
    }
}
