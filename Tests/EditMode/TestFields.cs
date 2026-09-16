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

    // Solid ball; stands in for a smooth planet.
    public class SphereField : DensityField
    {
        public Vector3 center;
        public float radius = 100f;
        public override float Sample(Vector3 p, float filterWidth) => radius - Vector3.Distance(p, center);
        protected override void OnValidate() { }
    }
}
