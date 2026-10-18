using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Dunes: y = a sin(kx x) cos(kz z) + b sin(k2 (x + z)). Positive (solid) below.
    public class WingedDuneField : DensityField
    {
        public float a = 0.6f, kx = 0.35f, kz = 0.3f, b = 0.2f, k2 = 0.9f;
        public float SurfaceY(float x, float z) => a * Mathf.Sin(kx * x) * Mathf.Cos(kz * z) + b * Mathf.Sin(k2 * (x + z));
        public override float Sample(Vector3 p, float filterWidth) => SurfaceY(p.x, p.z) - p.y;
        protected override void OnValidate() { }
    }

    // Launch and landing on the density field through the creature's ground cache (the lab's ground), and a
    // landing on a branch.
    public class WingedSerpentFieldTests
    {
        const float kDt = 1f / 60f;
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        [Test] public void Launch_Land_Field_FeetOnTheDunes()
        {
            var s = WingedSerpentTestSpecies.Create();
            trash.Add(s.species);
            var field = ScriptableObject.CreateInstance<WingedDuneField>();
            trash.Add(field);
            IGroundProbe ground = CachedGroundProbe.ForCreature(new FieldGroundProbe(field), s.species, 1f);
            Vector3 start = new Vector3(0f, field.SurfaceY(0f, 0f), 0f);
            var sim = new CreatureSim(s.species, ground, _ => Vector3.up, start, Vector3.forward);
            for (int t = 0; t < 60; t++) sim.Tick(kDt, sim.Joints[0] + Vector3.forward);
            Vector3 landAt = new Vector3(2f, 0f, 9f); landAt.y = field.SurfaceY(landAt.x, landAt.z);
            WingedSerpentModeTests.FlyAndLand(sim, landAt, 0, 40f, out int ticks);
            Assert.AreEqual(FlightMode.Ground, sim.Mode, "landed on the dunes");
            for (int t = 0; t < 90; t++) sim.Tick(kDt, sim.Joints[0]);
            float worst = 0f;
            for (int i = 0; i < sim.Joints.Length; i++)
            {
                Vector3 j = sim.Joints[i];
                float above = j.y - field.SurfaceY(j.x, j.z);
                worst = Mathf.Max(worst, Mathf.Abs(above - s.species.joints[i].clearance));
            }
            float feet = 0f;
            foreach (var l in sim.Legs) feet = Mathf.Max(feet, Mathf.Abs(l.foot.y - field.SurfaceY(l.foot.x, l.foot.z)));
            TestContext.WriteLine($"landed after {ticks * kDt:F1} s; worst joint clearance error {worst * 100f:F1} cm, worst foot off the sand {feet * 100f:F1} cm");
            Assert.Less(worst, 0.06f, "the body lies at its clearance on the dunes");
            Assert.Less(feet, 0.03f, "feet on the sand");
            Assert.Less(WingedSerpentModeTests.MaxSegmentError(sim), 1e-3f);
        }

        // Land along a branch (analytic): stands on the branch top, perched, tail hanging behind its end.
        [Test] public void Land_Branch_Analytic_Perched()
        {
            var s = WingedSerpentTestSpecies.Create();
            trash.Add(s.species);
            var surfaces = new WingedTestSurfaces().AddBranch(-1.5f, 1.5f, 2.2f, 8f, 0.2f, 31);
            var sim = new CreatureSim(s.species, surfaces, _ => Vector3.up, Vector3.zero, Vector3.right);
            WingedSerpentModeTests.FlyAndLand(sim, new Vector3(0f, 2.4f, 8f), 31, 45f, out int ticks);
            Assert.AreEqual(FlightMode.Ground, sim.Mode, "landed");
            for (int t = 0; t < 120; t++) sim.Tick(kDt, sim.Joints[0]);
            int g = sim.Flight.Girdle;
            TestContext.WriteLine($"landed after {ticks * kDt:F1} s; girdle {sim.Joints[g]}, perched {sim.Flight.Perched}");
            Assert.IsTrue(sim.Flight.Perched);
            Assert.AreEqual(2.4f + s.species.joints[g].clearance, sim.Joints[g].y, 0.12f, "girdle on the branch top");
            Assert.Less(WingedSerpentModeTests.MaxSegmentError(sim), 1e-3f);
        }
    }
}
