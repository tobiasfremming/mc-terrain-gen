using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class TerrainQueryTests
    {
        [Test]
        public void FindsFlatGroundBelow()
        {
            var f = ScriptableObject.CreateInstance<PlaneField>(); f.height = 12.3f;
            Assert.That(TerrainQuery.TryFindGround(f, new Vector3(5, 40, -7), Vector3.up, 10, 100, out var p, out var n), Is.True);
            Assert.That(p.y, Is.EqualTo(12.3f).Within(0.01f));
            Assert.That(p.x, Is.EqualTo(5f)); Assert.That(p.z, Is.EqualTo(-7f));
            Assert.That(Vector3.Dot(n, Vector3.up), Is.GreaterThan(0.999f));
            Object.DestroyImmediate(f);
        }

        [Test]
        public void FindsGroundWhenStartingUnderground()
        {
            var f = ScriptableObject.CreateInstance<PlaneField>(); f.height = 0f;
            Assert.That(TerrainQuery.TryFindGround(f, new Vector3(0, -5, 0), Vector3.up, 20, 20, out var p, out _), Is.True);
            Assert.That(p.y, Is.EqualTo(0f).Within(0.01f));
            Object.DestroyImmediate(f);
        }

        [Test]
        public void ReturnsFalseWhenNoCrossingInRange()
        {
            var f = ScriptableObject.CreateInstance<PlaneField>(); f.height = -500f;
            Assert.That(TerrainQuery.TryFindGround(f, Vector3.zero, Vector3.up, 10, 100, out _, out _), Is.False);
            Object.DestroyImmediate(f);
        }

        [Test]
        public void FindsSphereSurfaceAlongRadialUp()
        {
            var f = ScriptableObject.CreateInstance<SphereField>(); f.center = new Vector3(0, -1000, 0); f.radius = 1000f;
            Vector3 dir = new Vector3(1, 1, 0).normalized;
            Vector3 from = f.center + dir * 1030f;
            Assert.That(TerrainQuery.TryFindGround(f, from, dir, 5, 60, out var p, out var n), Is.True);
            Assert.That(Vector3.Distance(p, f.center), Is.EqualTo(1000f).Within(0.02f));
            Assert.That(Vector3.Dot(n, dir), Is.GreaterThan(0.999f));
            Object.DestroyImmediate(f);
        }
    }
}
