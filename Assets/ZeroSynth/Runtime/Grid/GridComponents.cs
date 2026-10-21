using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ZeroSynth
{
    [Flags]
    public enum CellFlags : byte
    {
        None = 0,

        /// <summary>Terrain. Never traversable, never attackable.</summary>
        Impassable = 1 << 0,

        /// <summary>Player structure. Traversable at a cost scaled to remaining health.</summary>
        Wall = 1 << 1,
    }

    /// <summary>
    /// Grid dimensions and the world/cell conversions. Every system that maps
    /// between the two goes through this, so the mapping exists in one place.
    /// </summary>
    public struct GridConfig : IComponentData
    {
        /// <summary>World XZ of the outer corner of cell (0,0).</summary>
        public float2 Origin;
        public float CellSize;
        public int Width;
        public int Height;

        public int CellCount => Width * Height;

        public int Index(int x, int y) => y * Width + x;
        public int Index(int2 c) => c.y * Width + c.x;

        public bool InBounds(int x, int y) => x >= 0 & x < Width & y >= 0 & y < Height;
        public bool InBounds(int2 c) => InBounds(c.x, c.y);

        public int2 WorldToCell(float3 world)
        {
            return (int2)math.floor((world.xz - Origin) / CellSize);
        }

        public float3 CellToWorld(int2 cell)
        {
            float2 p = Origin + ((float2)cell + 0.5f) * CellSize;
            return new float3(p.x, 0f, p.y);
        }
    }

    /// <summary>
    /// The grid's bulk arrays.
    /// </summary>
    /// <remarks>
    /// Managed singleton on purpose: IComponentData cannot hold native
    /// containers. Systems fetch this on the main thread and hand the arrays
    /// to Burst jobs by value, which is where the actual work happens — so
    /// nothing is lost by the wrapper being managed.
    /// </remarks>
    public sealed class GridData : IComponentData, IDisposable
    {
        /// <summary>Per-cell terrain and structure flags.</summary>
        public NativeArray<CellFlags> Flags;

        /// <summary>
        /// Extra traversal cost per cell. Zero for open ground; for a wall,
        /// scaled to remaining hit points so the field funnels the swarm at
        /// the weakest segment without any wall-seeking AI.
        /// </summary>
        public NativeArray<float> ExtraCost;

        /// <summary>Accumulated cost to the objective. Rebuilt on dirty.</summary>
        public NativeArray<float> Integration;

        /// <summary>Unit XZ heading toward the objective, per cell.</summary>
        public NativeArray<float2> Direction;

        /// <summary>
        /// Structure occupying each cell, or Entity.Null. Lets a blocked agent
        /// find the wall in front of it without any spatial query.
        /// </summary>
        public NativeArray<Entity> Occupant;

        /// <summary>Set by anything that changes Flags or ExtraCost.</summary>
        public bool Dirty = true;

        /// <summary>Bumped on every completed rebuild.</summary>
        public int Generation;

        /// <summary>Goal the current field was built for.</summary>
        public int2 GoalCell = new int2(int.MinValue, int.MinValue);

        public void Allocate(int cellCount)
        {
            Dispose();
            Flags = new NativeArray<CellFlags>(cellCount, Allocator.Persistent);
            ExtraCost = new NativeArray<float>(cellCount, Allocator.Persistent);
            Integration = new NativeArray<float>(cellCount, Allocator.Persistent);
            Direction = new NativeArray<float2>(cellCount, Allocator.Persistent);
            Occupant = new NativeArray<Entity>(cellCount, Allocator.Persistent);
        }

        public void Dispose()
        {
            if (Flags.IsCreated) Flags.Dispose();
            if (ExtraCost.IsCreated) ExtraCost.Dispose();
            if (Integration.IsCreated) Integration.Dispose();
            if (Direction.IsCreated) Direction.Dispose();
            if (Occupant.IsCreated) Occupant.Dispose();
        }
    }
}
