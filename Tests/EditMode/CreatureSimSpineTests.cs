using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class CreatureSimSpineTests
    {
        CreatureSpecies sp;
        PlaneField plane;
        SphereField sphere;

        [TearDown]
        public void TearDown()
        {
            if (sp != null) Object.DestroyImmediate(sp);
            if (plane != null) Object.DestroyImmediate(plane);
            if (sphere != null) Object.DestroyImmediate(sphere);
        }

        static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        CreatureSim OnPlane(float height = 0f, float undDeg = 0f, bool noLegs = false)
        {
            sp = CreatureTestSpecies.Lizardish(); sp.undulationDeg = undDeg;
            if (noLegs) sp.legs = new LegSpec[0]; // spine-only: the leg-limited walk speed is not under test
            plane = ScriptableObject.CreateInstance<PlaneField>(); plane.height = height;
            return new CreatureSim(sp, new FieldGroundProbe(plane), _ => Vector3.up, new Vector3(0, height, 0), Vector3.forward);
        }

        static void Circle(CreatureSim sim, int ticks)
        {
            for (int t = 0; t < ticks; t++)
            {
                float a = t / 60f * 0.8f;
                sim.Tick(1f / 60f, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.5f);
            }
        }

        static void AssertSegments(CreatureSim sim)
        {
            for (int i = 1; i < sim.Joints.Length; i++)
                Assert.AreEqual(sim.Species.spacing * sim.SizeScale, (sim.Joints[i] - sim.Joints[i - 1]).magnitude, 2e-3f, "segment " + i);
        }

        [Test] public void Chain_SegmentLengthsExact()
        {
            var sim = OnPlane();
            Circle(sim, 300);
            AssertSegments(sim);
        }

        [Test] public void Chain_BendWithinLimit()
        {
            var sim = OnPlane();
            float maxBend = 0f;
            for (int t = 0; t < 400; t++)
            {
                float a = t / 60f * 2.5f;
                sim.Tick(1f / 60f, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.4f);
                for (int i = 2; i < sim.Joints.Length; i++)
                {
                    Vector3 s0 = sim.Joints[i - 1] - sim.Joints[i - 2], s1 = sim.Joints[i] - sim.Joints[i - 1];
                    float ang = Vector3.Angle(s0, s1);
                    maxBend = Mathf.Max(maxBend, ang);
                    Assert.LessOrEqual(ang, 20.5f, "joint " + i);
                }
                // rigid joint 1: segment 1 stays on the head's back direction
                Vector3 back = -(sim.Rotations[0] * Vector3.forward);
                Assert.LessOrEqual(Vector3.Angle(back, sim.Joints[1] - sim.Joints[0]), 0.5f);
            }
            Assert.Greater(maxBend, 5f, "test should actually bend the body");
        }

        [Test] public void Joints_SitAtClearance_OnPlane()
        {
            var sim = OnPlane(2f);
            Circle(sim, 200);
            foreach (var j in sim.Joints) Assert.AreEqual(2.1f, j.y, 2e-3f);
        }

        [Test] public void Frames_Orthonormal_AndUpAlignedToGround()
        {
            sp = CreatureTestSpecies.Lizardish();
            sphere = ScriptableObject.CreateInstance<SphereField>(); sphere.center = new Vector3(0, -3, 0); sphere.radius = 3f;
            Vector3 c = sphere.center;
            var sim = new CreatureSim(sp, new FieldGroundProbe(sphere), p => (p - c).normalized, Vector3.zero, Vector3.forward);
            // target on the surface ~69 degrees round the ball, so the body ends up well away from the pole
            Vector3 target = c + new Vector3(0f, Mathf.Cos(1.2f), Mathf.Sin(1.2f)) * 3f;
            for (int t = 0; t < 600; t++) sim.Tick(1f / 60f, target);
            Assert.Greater(Vector3.Angle(Vector3.up, (sim.Joints[0] - c).normalized), 15f, "head must leave the pole");
            for (int i = 0; i < sim.Joints.Length; i++)
            {
                Quaternion r = sim.Rotations[i];
                Vector3 x = r * Vector3.right, y = r * Vector3.up, z = r * Vector3.forward;
                Assert.AreEqual(0f, Vector3.Dot(x, y), 1e-4f);
                Assert.AreEqual(0f, Vector3.Dot(y, z), 1e-4f);
                Assert.AreEqual(0f, Vector3.Dot(x, z), 1e-4f);
                Assert.AreEqual(1f, x.magnitude, 1e-4f);
                Assert.Greater(Vector3.Dot(y, (sim.Joints[i] - c).normalized), 0.98f, "joint " + i);
            }
        }

        static float LateralPeakToPeak(CreatureSim sim)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            Vector3 target = new Vector3(0, 0, 60f);
            for (int t = 0; t < 600; t++)
            {
                sim.Tick(1f / 60f, target);
                if (t < 60) continue;
                lo = Mathf.Min(lo, sim.Joints[0].x); hi = Mathf.Max(hi, sim.Joints[0].x);
            }
            return hi - lo;
        }

        [Test] public void Undulation_ProducesLateralSway()
        {
            Assert.Greater(LateralPeakToPeak(OnPlane(0f, 30f, true)), 0.05f);
            TearDown();
            Assert.Less(LateralPeakToPeak(OnPlane(0f, 0f, true)), 0.005f);
        }

        [Test] public void Undulation_FadesWhenIdle()
        {
            var sim = OnPlane(0f, 30f, true);
            Vector3 far = new Vector3(0, 0, 60f);
            for (int t = 0; t < 300; t++) sim.Tick(1f / 60f, far);
            // fixed target just ahead of the head: the creature settles with a small non-zero distance,
            // so the steering (and its undulation) keeps running; the body must not keep swinging
            Vector3 stop = sim.Joints[0] + Vector3.forward * 0.01f;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int t = 0; t < 300; t++)
            {
                sim.Tick(1f / 60f, stop);
                if (t < 240) continue;
                lo = Mathf.Min(lo, sim.Joints[3].x); hi = Mathf.Max(hi, sim.Joints[3].x);
            }
            Assert.Less(hi - lo, 0.005f);
        }

        [Test] public void Sim_LargeDt_StaysFinite_AndChainIntact()
        {
            var sim = OnPlane();
            Vector3 target = new Vector3(3, 0, 4);
            for (int i = 0; i < 5; i++) sim.Tick(0.25f, target);
            sim.Tick(0f, target);
            foreach (var j in sim.Joints) Assert.IsTrue(Finite(j));
            foreach (var r in sim.Rotations) Assert.IsFalse(float.IsNaN(r.x + r.y + r.z + r.w));
            AssertSegments(sim);
        }

        [Test] public void Bounds_ClampHead()
        {
            var sim = OnPlane(0f, 0f, true);
            var area = EnclosureArea.For(Vector3.zero, Vector3.up, 3f, sp, 1f);
            sim.Bounds = area;
            Vector3 target = new Vector3(20f, 0f, 0f);
            for (int t = 0; t < 600; t++) sim.Tick(1f / 60f, target);
            Assert.LessOrEqual(area.PlanarDistance(sim.Joints[0]), area.safeRadius + 1e-3f);
            Assert.Greater(area.PlanarDistance(sim.Joints[0]), area.safeRadius - 0.1f);
        }

        [Test] public void Speed_IsZero_WhenPinnedByBounds()
        {
            var sim = OnPlane();
            sim.Bounds = EnclosureArea.For(Vector3.zero, Vector3.up, 3f, sp, 1f);
            Vector3 target = new Vector3(20f, 0f, 0f);
            // 1200 ticks: the leg-limited walk (~0.3 m/s for the test species) needs ~10 s to reach the edge
            for (int t = 0; t < 1200; t++) sim.Tick(1f / 60f, target);
            Assert.Less(sim.Speed, 0.05f);
        }

        [Test] public void WriteBoneWorld_FillsAllBones_Finite()
        {
            var sim = OnPlane();
            Circle(sim, 30);
            var m = new Matrix4x4[sp.BoneCount + 3];
            sim.WriteBoneWorld(m, 3);
            for (int i = 0; i < sp.BoneCount; i++)
            {
                var mm = m[3 + i];
                Assert.IsTrue(Finite(mm.GetColumn(3)), "bone " + i);
                Assert.AreNotEqual(Matrix4x4.zero, mm);
            }
            Assert.AreEqual(sim.Joints[4].x, m[3 + 4].GetColumn(3).x, 1e-5f);
        }
    }
}
