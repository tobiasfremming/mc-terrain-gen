using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Plots are built on background threads against a live density field, so
    // PlantScatter.GetObstacles cannot be driven synchronously here. What it
    // does per instance -- instance matrix to (base, radius, height), and
    // obstacle to NavMesh capsule transform -- lives in FloraObstacleMath and
    // is tested directly.
    public class FloraObstacleTests
    {
        const float Eps = 1e-4f;

        static void AssertNear(Vector3 expected, Vector3 actual)
        {
            Assert.That((expected - actual).magnitude, Is.LessThan(Eps), $"expected {expected} got {actual}");
        }

        [Test]
        public void ObstacleBaseIsInstanceTranslationAndSizeFollowsScale()
        {
            var pos = new Vector3(12.5f, -3f, 400f);
            var trs = Matrix4x4.TRS(pos, Quaternion.Euler(4f, 73f, -2f), Vector3.one * 1.5f);
            Vector3 up = new Vector3(0.1f, 1f, 0f).normalized;

            Assert.That(FloraObstacleMath.TryMakeObstacle(trs, up, 0.4f, 3f, out var o), Is.True);
            AssertNear(pos, o.basePos);
            AssertNear(up, o.up);
            Assert.That(o.radius, Is.EqualTo(0.6f).Within(Eps));
            Assert.That(o.height, Is.EqualTo(4.5f).Within(Eps));
        }

        [Test]
        public void ZeroRadiusOrHeightIsNoObstacle()
        {
            var trs = Matrix4x4.TRS(Vector3.one, Quaternion.identity, Vector3.one);
            Assert.That(FloraObstacleMath.TryMakeObstacle(trs, Vector3.up, 0f, 3f, out _), Is.False);
            Assert.That(FloraObstacleMath.TryMakeObstacle(trs, Vector3.up, 0.4f, 0f, out _), Is.False);
        }

        [Test]
        public void CapsuleMatrixSpansBaseToTopAlongTiltedUp()
        {
            var basePos = new Vector3(100f, 50f, -20f);
            Vector3 up = new Vector3(1f, 1f, 0f).normalized;
            const float r = 0.6f, h = 4.5f;

            Matrix4x4 m = FloraObstacleMath.CapsuleSourceMatrix(basePos, up, r, h);
            Vector3 size = FloraObstacleMath.CapsuleSourceSize(r, h);

            // The dimensions live in `size`, the matrix is rigid: local
            // y = -h/2 is the foot, +h/2 the crown.
            AssertNear(basePos, m.MultiplyPoint3x4(new Vector3(0f, -h * 0.5f, 0f)));
            AssertNear(basePos + up * h, m.MultiplyPoint3x4(new Vector3(0f, h * 0.5f, 0f)));
            Vector3 side = m.MultiplyVector(new Vector3(r, 0f, 0f));
            Assert.That(side.magnitude, Is.EqualTo(r).Within(Eps));
            Assert.That(Vector3.Dot(side, up), Is.EqualTo(0f).Within(Eps));
            AssertNear(new Vector3(2f * r, h, 2f * r), size);
        }

        [Test]
        public void CapsuleDistanceMeasuresToTheSurface()
        {
            var basePos = new Vector3(10f, 0f, 0f);
            Vector3 up = Vector3.up;
            const float r = 0.5f, h = 4f;

            // Beside the shaft: 3 m from the axis at mid height, minus radius.
            Assert.That(FloraObstacleMath.CapsuleDistance(new Vector3(13f, 2f, 0f), basePos, up, r, h), Is.EqualTo(2.5f).Within(Eps));
            // Above the top: axis clamps to the top end (10,4,0); the point is
            // (3,4) away in x/y = 5 m, minus radius.
            Assert.That(FloraObstacleMath.CapsuleDistance(new Vector3(13f, 8f, 0f), basePos, up, r, h), Is.EqualTo(4.5f).Within(Eps));
            // Below the base: clamps to the base, 2 m straight down, minus radius.
            Assert.That(FloraObstacleMath.CapsuleDistance(new Vector3(10f, -2f, 0f), basePos, up, r, h), Is.EqualTo(1.5f).Within(Eps));
            // Inside the trunk: 0, never negative.
            Assert.That(FloraObstacleMath.CapsuleDistance(new Vector3(10.2f, 1f, 0f), basePos, up, r, h), Is.EqualTo(0f).Within(Eps));
        }
    }
}
