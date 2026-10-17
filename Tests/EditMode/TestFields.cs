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

    // Rolling ground: y = amplitude * sin(x * k) * cos(z * k). Positive (solid) below.
    public class RollingField : DensityField
    {
        public float amplitude = 0.08f, k = 4f;
        public float SurfaceY(float x, float z) => amplitude * Mathf.Sin(x * k) * Mathf.Cos(z * k);
        public override float Sample(Vector3 p, float filterWidth) => SurfaceY(p.x, p.z) - p.y;
        protected override void OnValidate() { }
    }

    // Flat ground at y = 0 with a dug spherical hole (TerrainDigger's CarveSphere: true 3D removal). The
    // density is min(plane, sphere SDF), a distance bound, so |Sample| is at most the distance to the surface.
    // centre.y < 0 overhangs the rim (columns near it cross ground, air, then the hole's floor); centre.y > 0
    // is an open bowl. `dug` = false is the untouched plane (for invalidation tests).
    public class PlaneWithSphereHoleField : DensityField
    {
        public Vector3 center = new Vector3(0f, -0.4f, 0f);
        public float radius = 1.5f;
        public bool dug = true;
        public override float Sample(Vector3 p, float filterWidth)
        {
            float plane = -p.y;
            return dug ? Mathf.Min(plane, Vector3.Distance(p, center) - radius) : plane;
        }
        protected override void OnValidate() { }
    }
}
