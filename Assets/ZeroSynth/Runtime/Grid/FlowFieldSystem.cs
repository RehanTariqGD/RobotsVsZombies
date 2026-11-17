using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace ZeroSynth
{
    /// <summary>
    /// Rebuilds the flow field when the grid or the objective changes.
    /// </summary>
    /// <remarks>
    /// Currently a full rebuild in one frame. At 256x256 that is a few
    /// milliseconds in Burst, which is fine while walls are static. Once walls
    /// are being destroyed mid-wave (M4) this needs to become an amortised
    /// rebuild into a back buffer with a generation swap, so agents never read
    /// a half-built field.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(AgentSteeringSystem))]
    public partial struct FlowFieldSystem : ISystem
    {
        EntityQuery m_GridQuery;

        public void OnCreate(ref SystemState state)
        {
            m_GridQuery = state.GetEntityQuery(ComponentType.ReadOnly<GridData>());
            state.RequireForUpdate<GridConfig>();
            state.RequireForUpdate<HordeTarget>();
            state.RequireForUpdate(m_GridQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            var grid = state.EntityManager.GetComponentObject<GridData>(m_GridQuery.GetSingletonEntity());

            var config = SystemAPI.GetSingleton<GridConfig>();
            var target = SystemAPI.GetSingleton<HordeTarget>();

            int2 goal = config.WorldToCell(target.Position);
            goal = math.clamp(goal, int2.zero, new int2(config.Width - 1, config.Height - 1));

            // Rebuild only when something actually changed. The objective
            // moving within a single cell is not a change.
            if (!grid.Dirty && goal.Equals(grid.GoalCell))
                return;

            var job = new BuildFlowFieldJob
            {
                Config = config,
                Goal = goal,
                Flags = grid.Flags,
                ExtraCost = grid.ExtraCost,
                Integration = grid.Integration,
                Direction = grid.Direction
            };

            // Completed immediately: consumers this frame expect a finished
            // field, and splitting it across frames is the M4 change.
            job.Schedule().Complete();

            grid.Dirty = false;
            grid.GoalCell = goal;
            grid.Generation++;
        }
    }

    /// <summary>
    /// Cost, integration and direction in one pass.
    /// </summary>
    /// <remarks>
    /// Queue-based relaxation rather than a priority queue. Costs here are
    /// near-uniform, so the number of re-visits stays low and this beats the
    /// bookkeeping of a heap.
    /// </remarks>
    [BurstCompile]
    public struct BuildFlowFieldJob : IJob
    {
        public GridConfig Config;
        public int2 Goal;

        [ReadOnly] public NativeArray<CellFlags> Flags;
        [ReadOnly] public NativeArray<float> ExtraCost;

        public NativeArray<float> Integration;
        public NativeArray<float2> Direction;

        const float Diagonal = 1.41421356f;

        public void Execute()
        {
            int w = Config.Width;
            int h = Config.Height;
            int count = w * h;

            for (int i = 0; i < count; i++)
                Integration[i] = float.MaxValue;

            int goalIndex = Config.Index(Goal);
            if ((Flags[goalIndex] & CellFlags.Impassable) != 0)
            {
                // Objective sits on impassable terrain: leave the field empty
                // rather than producing directions that lead nowhere.
                for (int i = 0; i < count; i++) Direction[i] = float2.zero;
                return;
            }

            var open = new NativeQueue<int>(Allocator.Temp);
            Integration[goalIndex] = 0f;
            open.Enqueue(goalIndex);

            while (open.TryDequeue(out int current))
            {
                int cx = current % w;
                int cy = current / w;
                float baseCost = Integration[current];

                for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (ox == 0 && oy == 0) continue;

                    int nx = cx + ox;
                    int ny = cy + oy;
                    if (!Config.InBounds(nx, ny)) continue;

                    int n = Config.Index(nx, ny);
                    if ((Flags[n] & CellFlags.Impassable) != 0) continue;

                    bool diagonal = ox != 0 && oy != 0;
                    if (diagonal)
                    {
                        // No corner cutting: both orthogonal neighbours must be
                        // open, otherwise agents slip through sealed corners.
                        int sideA = Config.Index(cx + ox, cy);
                        int sideB = Config.Index(cx, cy + oy);
                        if ((Flags[sideA] & CellFlags.Impassable) != 0) continue;
                        if ((Flags[sideB] & CellFlags.Impassable) != 0) continue;
                    }

                    float step = (diagonal ? Diagonal : 1f) * (1f + ExtraCost[n]);
                    float candidate = baseCost + step;

                    if (candidate < Integration[n])
                    {
                        Integration[n] = candidate;
                        open.Enqueue(n);
                    }
                }
            }

            open.Dispose();

            // Direction: point at the cheapest reachable neighbour.
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = Config.Index(x, y);

                if (Integration[i] == float.MaxValue)
                {
                    Direction[i] = float2.zero;
                    continue;
                }

                float best = Integration[i];
                int2 bestOffset = int2.zero;

                for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (ox == 0 && oy == 0) continue;

                    int nx = x + ox;
                    int ny = y + oy;
                    if (!Config.InBounds(nx, ny)) continue;

                    int n = Config.Index(nx, ny);

                    // The same no-corner-cutting rule the integration sweep
                    // used. Without it a diagonal across a sealed corner could
                    // win here purely because its integration value is lower
                    // by another route, pointing agents through the corner.
                    if (ox != 0 && oy != 0
                        && ((Flags[Config.Index(x + ox, y)] & CellFlags.Impassable) != 0
                            || (Flags[Config.Index(x, y + oy)] & CellFlags.Impassable) != 0))
                        continue;

                    if (Integration[n] < best)
                    {
                        best = Integration[n];
                        bestOffset = new int2(ox, oy);
                    }
                }

                Direction[i] = bestOffset.Equals(int2.zero)
                    ? float2.zero
                    : math.normalize((float2)bestOffset);
            }
        }
    }
}
