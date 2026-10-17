using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class CreatureGroundProbeTests
    {
        static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        [Test] public void FieldProbe_Plane_FindsHeight()
        {
            var f = ScriptableObject.CreateInstance<PlaneField>(); f.height = 3f;
            try
            {
                var probe = new FieldGroundProbe(f);
                Assert.IsTrue(probe.Probe(new Vector3(0, 3.2f, 0), Vector3.up, 1f, out var p, out var n));
                Assert.AreEqual(3f, p.y, 1e-3f);
                Assert.Less((n - Vector3.up).magnitude, 1e-3f);
                Assert.Greater(probe.SampleCount, 0);
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void FieldProbe_Sphere_NormalIsRadial()
        {
            var f = ScriptableObject.CreateInstance<SphereField>(); f.radius = 100f;
            try
            {
                var probe = new FieldGroundProbe(f);
                float a = 30f * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Assert.IsTrue(probe.Probe(dir * 100.3f, dir, 1f, out var p, out var n));
                Assert.AreEqual(100f, p.magnitude, 1e-2f);
                Assert.Greater(Vector3.Dot(n, dir), 0.999f);
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void FieldProbe_WindowMiss_FallsBack_ThenHolds()
        {
            var f = ScriptableObject.CreateInstance<PlaneField>(); f.height = 0f;
            try
            {
                var probe = new FieldGroundProbe(f);
                Assert.IsTrue(probe.Probe(new Vector3(0, 5f, 0), Vector3.up, 0.5f, out var p, out var n));
                Assert.AreEqual(0f, p.y, 1e-2f);
                Assert.IsFalse(probe.Probe(new Vector3(0, 50f, 0), Vector3.up, 0.5f, out p, out n));
                Assert.IsTrue(Finite(p)); Assert.IsTrue(Finite(n));
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void FieldProbe_TwoLayer_PicksNearestBelowNear()
        {
            var f = ScriptableObject.CreateInstance<TwoLayerField>();
            try
            {
                var probe = new FieldGroundProbe(f);
                Assert.IsTrue(probe.Probe(new Vector3(0, 1f, 0), Vector3.up, 2f, out var p, out _));
                Assert.AreEqual(0f, p.y, 1e-2f);
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void FlatProbe_ReturnsHeight()
        {
            var probe = new FlatGroundProbe(2f);
            Assert.IsTrue(probe.Probe(new Vector3(4, 9, 5), Vector3.up, 1f, out var p, out var n));
            Assert.AreEqual(new Vector3(4, 2, 5), p);
            Assert.AreEqual(Vector3.up, n);
        }

        // ---- CachedGroundProbe (Task 9 perf): bilinear reads of probed lattice nodes ----

        [Test] public void Cached_MatchesField_OnCurvedGround()
        {
            var f = ScriptableObject.CreateInstance<SphereField>(); f.radius = 20f; f.center = new Vector3(0f, -20f, 0f);
            try
            {
                var field = new FieldGroundProbe(f);
                var cache = new CachedGroundProbe(new FieldGroundProbe(f), 2f);
                float worstH = 0f, worstN = 0f;
                for (int i = 0; i < 200; i++)
                {
                    var q = new Vector3(-1.5f + 0.0151f * i, 0.05f, 0.7f - 0.0093f * i);
                    Assert.IsTrue(field.Probe(q, Vector3.up, 0.5f, out var p0, out var n0));
                    Assert.IsTrue(cache.Probe(q, Vector3.up, 0.5f, out var p1, out var n1));
                    Assert.AreEqual(q.x, p1.x, 1e-6f); Assert.AreEqual(q.z, p1.z, 1e-6f);
                    worstH = Mathf.Max(worstH, Mathf.Abs(p1.y - p0.y));
                    worstN = Mathf.Max(worstN, Vector3.Angle(n0, n1));
                }
                TestContext.Out.WriteLine($"worst height {worstH * 1000f:F2} mm, worst normal {worstN:F2} deg, node probes {cache.NodeProbes}");
                Assert.LessOrEqual(worstH, 0.004f);   // bilinear on a 0.5 m lattice over a 20 m radius: c^2 / 8R = 1.6 mm per axis, 3.1 mm both
                Assert.LessOrEqual(worstN, 1f);
                Assert.LessOrEqual(cache.NodeProbes, 64, "nodes are probed once each");
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void Cached_TiltedPlane_IsExact()
        {
            var f = ScriptableObject.CreateInstance<TiltedPlaneField>();
            f.normal = Quaternion.AngleAxis(30f, new Vector3(1f, 0f, 0.4f).normalized) * Vector3.up;
            try
            {
                var cache = new CachedGroundProbe(new FieldGroundProbe(f), 2f);
                for (int i = 0; i < 50; i++)
                {
                    var q = new Vector3(0.037f * i - 0.9f, 0f, 0.6f - 0.029f * i);
                    q.y = -(f.normal.x * q.x + f.normal.z * q.z) / f.normal.y + 0.05f;
                    Assert.IsTrue(cache.Probe(q, Vector3.up, 0.5f, out var p, out var n));
                    Assert.AreEqual(0f, Vector3.Dot(p, f.normal), 2e-4f);
                    Assert.Less(Vector3.Angle(n, f.normal), 0.1f);
                }
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void Cached_ReusesNodes_WithinACell()
        {
            var f = ScriptableObject.CreateInstance<PlaneField>(); f.height = 1f;
            try
            {
                var inner = new FieldGroundProbe(f);
                var cache = new CachedGroundProbe(inner, 2f);
                for (int i = 0; i < 1000; i++)
                    Assert.IsTrue(cache.Probe(new Vector3(0.1f + 0.0003f * i, 1.1f, 0.2f), Vector3.up, 0.5f, out _, out _));
                Assert.AreEqual(4, cache.NodeProbes);
                Assert.AreEqual(1000, cache.Queries);
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void Cached_Invalidate_ReprobesEditedGround()
        {
            var f = ScriptableObject.CreateInstance<PlaneField>(); f.height = 0f;
            try
            {
                var cache = new CachedGroundProbe(new FieldGroundProbe(f), 2f);
                Assert.IsTrue(cache.Probe(new Vector3(0.2f, 0.1f, 0.2f), Vector3.up, 0.5f, out var p, out _));
                Assert.AreEqual(0f, p.y, 1e-3f);
                f.height = -0.1f; // an edit lowered the ground
                Assert.IsTrue(cache.Probe(new Vector3(0.2f, 0.1f, 0.2f), Vector3.up, 0.5f, out p, out _));
                Assert.AreEqual(0f, p.y, 1e-3f, "without an invalidation the cache holds the old ground");
                cache.Invalidate(new Bounds(new Vector3(0.2f, 0f, 0.2f), new Vector3(0.4f, 0.4f, 0.4f)));
                Assert.IsTrue(cache.Probe(new Vector3(0.2f, 0.1f, 0.2f), Vector3.up, 0.5f, out p, out _));
                Assert.AreEqual(-0.1f, p.y, 1e-3f);
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void Cached_OtherLayer_Reprobes()
        {
            var f = ScriptableObject.CreateInstance<TwoLayerField>();
            try
            {
                var cache = new CachedGroundProbe(new FieldGroundProbe(f), 2f);
                Assert.IsTrue(cache.Probe(new Vector3(0.1f, 0.1f, 0.1f), Vector3.up, 0.5f, out var p, out _));
                Assert.AreEqual(0f, p.y, 1e-2f);
                Assert.IsTrue(cache.Probe(new Vector3(0.1f, 30.1f, 0.1f), Vector3.up, 0.5f, out p, out _));
                Assert.AreEqual(30f, p.y, 1e-2f, "the roof, not the cached floor");
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void Cached_Miss_ReturnsFalse_Finite()
        {
            var f = ScriptableObject.CreateInstance<PlaneField>(); f.height = 0f;
            try
            {
                var cache = new CachedGroundProbe(new FieldGroundProbe(f), 2f);
                Assert.IsFalse(cache.Probe(new Vector3(0.3f, 50f, 0.3f), Vector3.up, 0.5f, out var p, out var n));
                Assert.IsTrue(Finite(p)); Assert.IsTrue(Finite(n));
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void Cached_NonVerticalUp_PassesThrough()
        {
            var f = ScriptableObject.CreateInstance<SphereField>(); f.radius = 100f;
            try
            {
                var cache = new CachedGroundProbe(new FieldGroundProbe(f), 2f);
                float a = 30f * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Assert.IsTrue(cache.Probe(dir * 100.3f, dir, 1f, out var p, out var n));
                Assert.AreEqual(100f, p.magnitude, 1e-2f);
                Assert.Greater(Vector3.Dot(n, dir), 0.999f);
                Assert.AreEqual(0, cache.NodeProbes);
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void World_CachesFieldProbe_NotAnalyticProbes()
        {
            var sp = CreatureTestSpecies.Lizardish();
            var f = ScriptableObject.CreateInstance<PlaneField>();
            try
            {
                var field = new FieldGroundProbe(f);
                Assert.IsInstanceOf<CachedGroundProbe>(CreatureWorld.SimGround(field, sp, 1f));
                var flat = new FlatGroundProbe();
                Assert.AreSame(flat, CreatureWorld.SimGround(flat, sp, 1f));
            }
            finally { Object.DestroyImmediate(f); Object.DestroyImmediate(sp); }
        }
    }
}
