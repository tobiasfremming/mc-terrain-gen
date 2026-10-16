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
    }
}
