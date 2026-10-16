using UnityEngine;

namespace TerrainGen.Tests
{
    // Flat ground at y = height. Positive (solid) below.
    public class PlaneField : DensityField
    {
        public float height;
        public override float Sample(Vector3 p, float filterWidth) => height - p.y;
        public override bool TryGetHeightBounds(out float minH, out float maxH) { minH = maxH = height; return true; }
        protected override void OnValidate() { } // don't ping TerrainTuning from tests
    }

    // Plane through the origin with outward unit normal `normal`. Positive (solid) below it.
    public class TiltedPlaneField : DensityField
    {
        public Vector3 normal = Vector3.up;
        public override float Sample(Vector3 p, float filterWidth) => -Vector3.Dot(p, normal);
        protected override void OnValidate() { }
    }

    // Flat at y = 0 for lateral x < edgeX, then sloping down at slopeDeg for x > edgeX.
    public class HalfSlopeField : DensityField
    {
        public float edgeX = 0.06f, slopeDeg = 30f;
        public float SurfaceY(float x) => x > edgeX ? -(x - edgeX) * Mathf.Tan(slopeDeg * Mathf.Deg2Rad) : 0f;
        public override float Sample(Vector3 p, float filterWidth) => SurfaceY(p.x) - p.y;
        protected override void OnValidate() { }
    }

    // Flat at y = 0 except a `drop`-deep patch for x < patchX and z in (zMin, zMax).
    public class LedgeField : DensityField
    {
        public float patchX = -0.15f, zMin = -0.5f, zMax = 0.2f, drop = 0.03f;
        public override float Sample(Vector3 p, float filterWidth)
            => (p.x < patchX && p.z > zMin && p.z < zMax ? -drop : 0f) - p.y;
        protected override void OnValidate() { }
    }

    // Solid ball; stands in for a smooth planet.
    public class SphereField : DensityField
    {
        public Vector3 center;
        public float radius = 100f;
        public override float Sample(Vector3 p, float filterWidth) => radius - Vector3.Distance(p, center);
        protected override void OnValidate() { }
    }

    // Ground solid below y = 0, plus a floating rock layer between y = 20 and
    // y = 30 (a cave roof / overhang); air everywhere else.
    public class TwoLayerField : DensityField
    {
        public override float Sample(Vector3 p, float filterWidth) => Mathf.Max(-p.y, Mathf.Min(p.y - 20f, 30f - p.y));
        protected override void OnValidate() { }
    }
}
