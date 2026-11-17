using Unity.Entities;
using Unity.Mathematics;

namespace ZeroSynth
{
    /// <summary>
    /// What the LOD system needs to know about the camera. Written once per
    /// frame by LodCameraDriver; without one in the scene every agent stays on
    /// LOD0.
    /// </summary>
    /// <remarks>
    /// The tier is chosen from projected screen height in pixels, not from
    /// distance. The tactical camera sits ~100 m from everything, so a distance
    /// ladder would put the whole field in a single tier. A screen-size rule
    /// also adapts for free to the mech camera and to the Titan being five
    /// times the size of a Runner.
    /// </remarks>
    public struct LodView : IComponentData
    {
        public float3 CameraPosition;

        /// <summary>Screen pixels covered by one metre at one metre distance.</summary>
        public float PixelsPerMeter;

        /// <summary>Projected height at or above which LOD0 is used.</summary>
        public float Lod0Pixels;

        public float Lod1Pixels;
        public float Lod2Pixels;
    }

    /// <summary>The agent's real height, which is what projects to pixels.</summary>
    public struct LodHeight : IComponentData
    {
        public float Meters;
    }
}
