using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;

namespace ZeroSynth
{
    /// <summary>
    /// Hides structures once their health reaches zero.
    /// </summary>
    /// <remarks>
    /// The rest of a structure's death already happens elsewhere: the wall
    /// system stops treating a dead wall as solid, and the power flood stops
    /// treating a dead node as powered, which silences a dead turret. What was
    /// missing is the visual — a destroyed barricade kept rendering. The
    /// entity itself is left in place rather than destroyed, so nothing that
    /// holds a reference to it can dangle.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DamageApplySystem))]
    public partial struct StructureDeathSystem : ISystem
    {
        EntityQuery m_DeadQuery;

        public void OnCreate(ref SystemState state)
        {
            m_DeadQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Structure, Health>()
                .WithNone<DisableRendering, MechUnit>()
                .Build(ref state);

            state.RequireForUpdate(m_DeadQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            state.CompleteDependency();

            var entities = m_DeadQuery.ToEntityArray(Allocator.Temp);
            var healthLookup = SystemAPI.GetComponentLookup<Health>(true);

            for (int i = 0; i < entities.Length; i++)
            {
                if (healthLookup[entities[i]].Current > 0f)
                    continue;

                state.EntityManager.AddComponent<DisableRendering>(entities[i]);
            }

            entities.Dispose();
        }
    }
}
