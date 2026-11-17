using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Rendering;

namespace ZeroSynth.Authoring
{
    /// <summary>
    /// Creates structure entities at runtime, in the same shapes the authoring
    /// components produce for hand-placed ones.
    /// </summary>
    /// <remarks>
    /// Kept separate from the authoring components on purpose: those read their
    /// settings off a scene object in Start, which a build menu cannot do. Both
    /// paths must agree on what a structure is, so if a component is added to
    /// one, check the other.
    /// </remarks>
    public static class StructureSpawner
    {
        public static Entity Spawn(EntityManager em, GridConfig config, BuildDefinition def, int2 anchor)
        {
            var footprint = new int2(def.Footprint.x, def.Footprint.y);
            var entity = em.CreateEntity();
            em.SetName(entity, "Built_" + def.Label);

            float3 centre = config.FootprintCentre(anchor, footprint);
            centre.y = def.YOffset;

            em.AddComponentData(entity, LocalTransform.FromPosition(centre));
            em.AddComponentData(entity, new Structure { Kind = def.Kind, Footprint = footprint });
            em.AddComponentData(entity, new GridAnchor { Cell = anchor });
            em.AddComponentData(entity, new Health { Current = def.Health, Max = def.Health });

            switch (def.Kind)
            {
                case StructureKind.Wall:
                    em.AddComponent<Wall>(entity);
                    break;

                case StructureKind.Turret:
                    AddTurret(em, entity, def);
                    break;

                case StructureKind.Pylon:
                case StructureKind.Reactor:
                    AddPowerNode(em, entity, def);
                    break;
            }

            if (def.Mesh != null && def.Material != null)
            {
                float4x4 post = AddRendering(em, entity, def, config, footprint);

                // Written here, not left to the transform system. That system
                // lives in the simulation group, which the tactical pause
                // freezes, so a structure built while paused would sit at a zero
                // matrix, invisible, until the player resumed.
                em.SetComponentData(entity, new LocalToWorld
                {
                    Value = math.mul(float4x4.TRS(centre, quaternion.identity, new float3(1f)), post)
                });
            }

            return entity;
        }

        static void AddTurret(EntityManager em, Entity entity, BuildDefinition def)
        {
            em.AddComponentData(entity, new Turret
            {
                Range = def.Range,
                FireRate = def.FireRate,
                Damage = def.Damage,
                SplashRadius = def.SplashRadius,
                Cooldown = 0f
            });
            em.AddComponentData(entity, new TurretStance { Value = TargetingStance.Closest });
            em.AddComponentData(entity, new TurretTarget { Value = Entity.Null });

            // Both present but disabled, exactly as TurretAuthoring does: the
            // targeting queries match WithDisabled(ManualAim), and an entity
            // that lacks the component never matches that at all.
            em.AddComponent<Possessed>(entity);
            em.SetComponentEnabled<Possessed>(entity, false);
            em.AddComponent<ManualAim>(entity);
            em.SetComponentEnabled<ManualAim>(entity, false);

            // A consumer: it needs power but cannot relay it.
            em.AddComponentData(entity, new PowerNode { SupplyRange = 0f, IsSource = false });
            em.AddComponent<Powered>(entity);
            em.SetComponentEnabled<Powered>(entity, false);
        }

        static void AddPowerNode(EntityManager em, Entity entity, BuildDefinition def)
        {
            em.AddComponentData(entity, new PowerNode
            {
                SupplyRange = def.SupplyRange,
                IsSource = def.Kind == StructureKind.Reactor
            });
            em.AddComponent<Powered>(entity);
            em.SetComponentEnabled<Powered>(entity, false);
        }

        /// <summary>Returns the post-transform matrix applied, or identity if none.</summary>
        static float4x4 AddRendering(EntityManager em, Entity entity, BuildDefinition def, GridConfig config, int2 footprint)
        {
            var description = new RenderMeshDescription(
                shadowCastingMode: ShadowCastingMode.Off,
                receiveShadows: false);

            var array = new RenderMeshArray(new[] { def.Material }, new[] { def.Mesh }, null);

            RenderMeshUtility.AddComponents(
                entity,
                em,
                description,
                array,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0, 0));

            float3 scale = def.VisualScale;
            if (def.StretchToFootprint)
            {
                scale.x *= footprint.x * config.CellSize;
                scale.z *= footprint.y * config.CellSize;
            }

            // PostTransformMatrix stretches the mesh without touching
            // LocalTransform.Scale, which must stay uniform.
            if (scale.Equals(new float3(1f, 1f, 1f)))
                return float4x4.identity;

            var post = float4x4.Scale(scale);
            em.AddComponentData(entity, new PostTransformMatrix { Value = post });
            return post;
        }
    }
}
