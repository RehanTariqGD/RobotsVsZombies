using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ZeroSynth
{
    public enum ShotKind : byte
    {
        /// <summary>A turret or mech round: a short bright tracer.</summary>
        Turret = 0,

        /// <summary>A Spitter's acid: a slower green streak.</summary>
        Acid = 1,
    }

    public struct ShotEvent
    {
        public float3 From;
        public float3 To;
        public ShotKind Kind;
    }

    public struct DeathEvent
    {
        public float3 Position;

        /// <summary>Roughly the unit's height in metres; scales the burst.</summary>
        public float Size;
    }

    /// <summary>Dirt thrown up by a tunnelling agent: a trail puff, or the burst of it breaking surface.</summary>
    public struct DustEvent
    {
        public float3 Position;
        public bool Surfacing;
    }

    /// <summary>
    /// Things that happened in the simulation which presentation wants to react
    /// to: tracers, death bursts, sounds.
    /// </summary>
    /// <remarks>
    /// The simulation never calls presentation code. It writes events here and a
    /// MonoBehaviour drains them once a frame, so effects can be added, removed
    /// or throttled without touching a single gameplay system. Queues are
    /// trimmed by the writer if nothing drains them, so a scene with no
    /// feedback director cannot leak.
    ///
    /// Managed singleton for the same reason as DamageBuffer: IComponentData
    /// cannot hold native containers.
    /// </remarks>
    public sealed class SimFeedback : IComponentData, IDisposable
    {
        public NativeQueue<ShotEvent> Shots;
        public NativeQueue<DeathEvent> Deaths;
        public NativeQueue<DustEvent> Dust;

        // Plain counters, written and read on the main thread only.
        public int Hits;
        public float CoreDamage;
        public int StructuresLost;

        public const int MaxQueued = 2048;

        public void Allocate()
        {
            Dispose();
            Shots = new NativeQueue<ShotEvent>(Allocator.Persistent);
            Deaths = new NativeQueue<DeathEvent>(Allocator.Persistent);
            Dust = new NativeQueue<DustEvent>(Allocator.Persistent);
        }

        /// <summary>Empties queues nobody is reading, so they cannot grow without bound.</summary>
        public void TrimIfUndrained()
        {
            if (Shots.IsCreated && Shots.Count > MaxQueued) Shots.Clear();
            if (Deaths.IsCreated && Deaths.Count > MaxQueued) Deaths.Clear();
            if (Dust.IsCreated && Dust.Count > MaxQueued) Dust.Clear();
        }

        public void Dispose()
        {
            if (Shots.IsCreated) Shots.Dispose();
            if (Deaths.IsCreated) Deaths.Dispose();
            if (Dust.IsCreated) Dust.Dispose();
        }
    }
}
