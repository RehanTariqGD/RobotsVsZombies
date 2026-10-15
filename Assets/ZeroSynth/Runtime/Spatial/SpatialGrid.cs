using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ZeroSynth
{
    /// <summary>
    /// Uniform grid used for every neighbour query in the game. Separate from
    /// <see cref="GridConfig"/>: pathfinding wants 1m cells, neighbour queries
    /// want cells near the largest query radius, and forcing one size on both
    /// makes one of them slow.
    /// </summary>
    public struct SpatialGridConfig : IComponentData
    {
        public float2 Origin;
        public float CellSize;
        public int Width;
        public int Height;

        public int CellCount => Width * Height;

        public int Index(int x, int y) => y * Width + x;
        public bool InBounds(int x, int y) => x >= 0 & x < Width & y >= 0 & y < Height;

        public int2 ToCell(float3 world) => (int2)math.floor((world.xz - Origin) / CellSize);
    }

    /// <summary>
    /// Counting-sort buckets, rebuilt every frame.
    /// </summary>
    /// <remarks>
    /// A flat packed array with per-cell offsets rather than a hash map:
    /// lookups become contiguous reads instead of pointer chasing, which is
    /// what makes neighbour queries cheap at horde scale.
    ///
    /// Positions are stored alongside the entities so consumers can reject by
    /// distance without a random component lookup per candidate.
    /// </remarks>
    public sealed class SpatialGridData : IComponentData, IDisposable
    {
        /// <summary>Start offset per cell, length CellCount + 1.</summary>
        public NativeArray<int> CellStart;

        /// <summary>Packed entity ids, ordered by cell.</summary>
        public NativeArray<Entity> Entities;

        /// <summary>Packed positions, parallel to <see cref="Entities"/>.</summary>
        public NativeArray<float3> Positions;

        /// <summary>Number of entries actually populated this frame.</summary>
        public NativeArray<int> Count;

        public int Capacity => Entities.IsCreated ? Entities.Length : 0;

        public void Allocate(int cellCount, int capacity)
        {
            Dispose();
            CellStart = new NativeArray<int>(cellCount + 1, Allocator.Persistent);
            Entities = new NativeArray<Entity>(capacity, Allocator.Persistent);
            Positions = new NativeArray<float3>(capacity, Allocator.Persistent);
            Count = new NativeArray<int>(1, Allocator.Persistent);
        }

        public void Dispose()
        {
            if (CellStart.IsCreated) CellStart.Dispose();
            if (Entities.IsCreated) Entities.Dispose();
            if (Positions.IsCreated) Positions.Dispose();
            if (Count.IsCreated) Count.Dispose();
        }
    }
}
