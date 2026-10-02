using System;
using Unity.Collections;
using Unity.Entities;

namespace ZeroSynth
{
    /// <summary>
    /// The single channel every damage producer writes to.
    /// </summary>
    /// <remarks>
    /// A queue rather than the NativeStream the design doc specifies. With one
    /// producer this is simpler and the difference is unmeasurable; revisit at
    /// M3 when turrets, projectiles and splash are all writing in parallel and
    /// the per-thread stream layout starts to matter.
    ///
    /// Managed singleton for the same reason as GridData: IComponentData
    /// cannot hold native containers.
    /// </remarks>
    public sealed class DamageBuffer : IComponentData, IDisposable
    {
        public NativeQueue<DamageEvent> Pending;
        public NativeQueue<Entity> Deaths;

        public void Allocate()
        {
            Dispose();
            Pending = new NativeQueue<DamageEvent>(Allocator.Persistent);
            Deaths = new NativeQueue<Entity>(Allocator.Persistent);
        }

        public void Dispose()
        {
            if (Pending.IsCreated) Pending.Dispose();
            if (Deaths.IsCreated) Deaths.Dispose();
        }
    }
}
