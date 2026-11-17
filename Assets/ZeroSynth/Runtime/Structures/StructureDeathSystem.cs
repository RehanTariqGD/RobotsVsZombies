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
    ///
    /// Health is read into an array up front. Adding a component is a
    /// structural change, and a ComponentLookup taken before it is invalidated
    /// by it, so reading through one inside the loop throws on the second dead
    /// structure.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DamageApplySystem))]
    public partial struct StructureDeathSystem : ISystem
    {
        EntityQuery m_LiveQuery;

        public void OnCreate(ref SystemState state)
        {
            m_LiveQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Structure, Health>()
                .WithNone<DisableRendering, MechUnit>()
                .Build(ref state);

            state.RequireForUpdate(m_LiveQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            state.CompleteDependency();

            var entities = m_LiveQuery.ToEntityArray(Allocator.Temp);
            var healths = m_LiveQuery.ToComponentDataArray<Health>(Allocator.Temp);

            var dead = new NativeList<Entity>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (healths[i].Current <= 0f)
                    dead.Add(entities[i]);
            }

            // Structural changes happen only after every read is finished.
            for (int i = 0; i < dead.Length; i++)
                state.EntityManager.AddComponent<DisableRendering>(dead[i]);

            dead.Dispose();
            healths.Dispose();
            entities.Dispose();
        }
    }
}
