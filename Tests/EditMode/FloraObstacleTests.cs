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

            // A NavMesh capsule source with size one is the unit capsule
            // (height 1, radius 0.5) scaled by the matrix: local y = -0.5 is
            // the foot, +0.5 the crown.
            AssertNear(basePos, m.MultiplyPoint3x4(new Vector3(0f, -0.5f, 0f)));
            AssertNear(basePos + up * h, m.MultiplyPoint3x4(new Vector3(0f, 0.5f, 0f)));
            // Unit radius 0.5 maps to the obstacle radius, perpendicular to up.
            Vector3 side = m.MultiplyVector(new Vector3(0.5f, 0f, 0f));
            Assert.That(side.magnitude, Is.EqualTo(r).Within(Eps));
            Assert.That(Vector3.Dot(side, up), Is.EqualTo(0f).Within(Eps));
        }
    }
}
