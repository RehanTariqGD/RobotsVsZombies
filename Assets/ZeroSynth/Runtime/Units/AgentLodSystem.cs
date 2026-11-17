using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace ZeroSynth
{
    /// <summary>
    /// Picks each live agent's mesh tier from how big it is on screen.
    /// </summary>
    /// <remarks>
    /// Every agent's RenderMeshArray holds all four tiers, so changing tier is
    /// a value write to MaterialMeshInfo, not a structural change. The write is
    /// skipped when the tier has not changed, which is nearly always, so the
    /// chunks are not dirtied every frame.
    ///
    /// Batching still holds: one material, so the cost of the ladder is at most
    /// four draw commands per unit kind.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AgentSteeringSystem))]
    public partial struct AgentLodSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<LodView>();
        }

        public void OnUpdate(ref SystemState state)
        {
            new AgentLodJob
            {
                View = SystemAPI.GetSingleton<LodView>()
            }.ScheduleParallel();
        }
    }

    [BurstCompile]
    [WithAll(typeof(Active))]
    public partial struct AgentLodJob : IJobEntity
    {
        public LodView View;

        public void Execute(ref MaterialMeshInfo mesh, in LodHeight height, in LocalTransform transform)
        {
            float distance = math.max(math.distance(View.CameraPosition, transform.Position), 0.5f);
            float pixels = height.Meters * View.PixelsPerMeter / distance;

            int lod = pixels >= View.Lod0Pixels ? 0
                    : pixels >= View.Lod1Pixels ? 1
                    : pixels >= View.Lod2Pixels ? 2
                    : 3;

            int wanted = MaterialMeshInfo.ArrayIndexToStaticIndex(lod);
            if (mesh.Mesh != wanted)
                mesh.Mesh = wanted;
        }
    }
}
