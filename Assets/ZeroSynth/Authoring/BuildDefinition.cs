using System;
using UnityEngine;

namespace ZeroSynth.Authoring
{
    /// <summary>
    /// Everything needed to place one kind of structure at runtime.
    /// </summary>
    /// <remarks>
    /// Default prices live in <see cref="StructureCosts"/> next to the economy.
    /// A definition may override its own, which is how several turret types
    /// share one kind at different prices.
    /// </remarks>
    [Serializable]
    public sealed class BuildDefinition
    {
        public string Label = "Wall";
        public StructureKind Kind = StructureKind.Wall;

        [Tooltip("Scrap cost. Zero uses the default price for this kind.")]
        [Min(0f)] public float Cost;

        /// <summary>What this costs to build, after any override.</summary>
        public float ScrapCost => Cost > 0f ? Cost : StructureCosts.Scrap(Kind);

        [Header("Rendering")]
        public Mesh Mesh;
        public Material Material;

        [Tooltip("Cells covered, X by Z.")]
        public Vector2Int Footprint = new Vector2Int(1, 1);

        [Tooltip("Scale applied to the mesh. For meshes authored at real size, leave at one.")]
        public Vector3 VisualScale = Vector3.one;

        [Tooltip("Stretch the mesh across the whole footprint. Used by walls, which are a unit cube.")]
        public bool StretchToFootprint;

        [Tooltip("Lifts the entity so a centre-pivoted mesh sits on the ground.")]
        public float YOffset;

        [Header("Durability")]
        [Min(1f)] public float Health = 400f;

        [Header("Turret (Kind = Turret)")]
        [Min(1f)] public float Range = 28f;
        [Min(0.1f)] public float FireRate = 4f;
        [Min(1f)] public float Damage = 10f;
        [Min(0f)] public float SplashRadius;

        [Header("Power (Kind = Pylon or Reactor)")]
        [Min(1f)] public float SupplyRange = 30f;

        [Header("Foundry (Kind = Foundry)")]
        [Tooltip("Biomass consumed per second while powered.")]
        [Min(0.1f)] public float BiomassPerSecond = 10f;

        [Tooltip("Scrap produced per biomass consumed.")]
        [Min(0.1f)] public float ScrapPerBiomass = 1.5f;
    }
}
