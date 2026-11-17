using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ZeroSynth
{
    /// <summary>
    /// Converts stored biomass into scrap, in proportion to how many powered
    /// foundries are standing.
    /// </summary>
    /// <remarks>
    /// Runs inside the simulation group, so the tactical pause freezes it with
    /// everything else. Foundries are few, so this stays on the main thread
    /// rather than scheduling a job for a handful of entities.
    ///
    /// It only ever converts what is stored: with no biomass there is nothing to
    /// spend, so extra foundries do not help until kills have paid in.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(RecycleSystem))]
    public partial struct FoundrySystem : ISystem
    {
        EntityQuery m_Query;

        public void OnCreate(ref SystemState state)
        {
            m_Query = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Foundry, Powered, Health>()
                .Build(ref state);

            state.RequireForUpdate(m_Query);
            state.RequireForUpdate<Economy>();
        }

        public void OnUpdate(ref SystemState state)
        {
            state.CompleteDependency();

            var foundries = m_Query.ToComponentDataArray<Foundry>(Allocator.Temp);
            var healths = m_Query.ToComponentDataArray<Health>(Allocator.Temp);

            float capacity = 0f;
            float scrapRate = 0f;

            for (int i = 0; i < foundries.Length; i++)
            {
                if (healths[i].Current <= 0f) continue;

                capacity += foundries[i].BiomassPerSecond;
                scrapRate = math.max(scrapRate, foundries[i].ScrapPerBiomass);
            }

            foundries.Dispose();
            healths.Dispose();

            if (capacity <= 0f) return;

            var economy = SystemAPI.GetSingletonRW<Economy>();
            float converted = math.min(economy.ValueRO.Biomass, capacity * SystemAPI.Time.DeltaTime);
            if (converted <= 0f) return;

            economy.ValueRW.Biomass -= converted;
            economy.ValueRW.MetalScrap += converted * scrapRate;
        }
    }
}
