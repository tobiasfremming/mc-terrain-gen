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
