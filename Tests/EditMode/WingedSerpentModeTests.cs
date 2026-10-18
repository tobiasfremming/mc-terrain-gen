using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Phase B: the flight modes in CreatureSim (CreatureFlight): ground untouched, launch, air, flare,
    // touchdown, perching and the drape.
    public class WingedSerpentModeTests
    {
        const float kDt = 1f / 60f;
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        WingedSerpentTestSpecies.Serpent Serpent()
        {
            var s = WingedSerpentTestSpecies.Create();
            trash.Add(s.species);
            return s;
        }

        static CreatureSim Sim(CreatureSpecies sp, IGroundProbe g, Vector3 at = default, float scale = 1f)
            => new CreatureSim(sp, g, _ => Vector3.up, at, Vector3.forward, scale);

        internal static float MaxSegmentError(CreatureSim sim)
        {
            float seg = sim.Species.spacing * sim.SizeScale, worst = 0f;
            for (int i = 1; i < sim.Joints.Length; i++) worst = Mathf.Max(worst, Mathf.Abs((sim.Joints[i] - sim.Joints[i - 1]).magnitude - seg) / seg);
            return worst;
        }

        internal static void AssertFinite(CreatureSim sim, string at)
        {
            foreach (var j in sim.Joints) Assert.IsFalse(float.IsNaN(j.x + j.y + j.z) || float.IsInfinity(j.x + j.y + j.z), "joint not finite at " + at);
            foreach (var q in sim.Rotations) Assert.IsFalse(float.IsNaN(q.x + q.y + q.z + q.w), "rotation not finite at " + at);
        }

        // A winged species on the ground walks exactly as the same body without wings: the flight hooks in
        // the walking sub-step are inert until it launches (and the monitor's walking is the walking code).
        [Test] public void Mode_NoWings_BitIdentical()
        {
            var a = Serpent();
            var b = Serpent();
            b.species.wings = null; // the same body, no wings: CanFly false, no CreatureFlight
            Assert.IsTrue(a.species.CanFly); Assert.IsFalse(b.species.CanFly);
            var sa = Sim(a.species, new FlatGroundProbe());
            var sb = Sim(b.species, new FlatGroundProbe());
            Assert.IsNotNull(sa.Flight); Assert.IsNull(sb.Flight);
            for (int t = 0; t < 900; t++)
            {
                float tt = t * kDt;
                var target = new Vector3(3f * Mathf.Sin(0.7f * tt), 0f, 3f * Mathf.Cos(0.5f * tt) + 2f);
                sa.Tick(kDt, target); sb.Tick(kDt, target);
                for (int i = 0; i < sa.Joints.Length; i++)
                    Assert.IsTrue(sa.Joints[i] == sb.Joints[i] && sa.Joints[i].x == sb.Joints[i].x && sa.Joints[i].y == sb.Joints[i].y && sa.Joints[i].z == sb.Joints[i].z, $"joint {i} differs at tick {t}");
                for (int l = 0; l < sa.Legs.Length; l++)
                    Assert.IsTrue(sa.Legs[l].foot.x == sb.Legs[l].foot.x && sa.Legs[l].foot.y == sb.Legs[l].foot.y && sa.Legs[l].foot.z == sb.Legs[l].foot.z, $"foot {l} differs at tick {t}");
            }
            Assert.AreEqual(FlightMode.Ground, sa.Mode);
        }

        // Launch from a walk: the body leaves the ground head first, the air chain takes over, lengths hold.
        [Test] public void Launch_FromGround_Airborne()
        {
            var s = Serpent();
            var sim = Sim(s.species, new FlatGroundProbe());
            Vector3 ahead = new Vector3(0f, 0f, 100f);
            for (int t = 0; t < 60; t++) sim.Tick(kDt, ahead);
            Assert.IsTrue(sim.Flight.Launch(5f));
            float worstLen = 0f, worstBend = 0f;
            for (int t = 0; t < 240; t++)
            {
                sim.Tick(kDt, sim.Joints[0] + sim.Rotations[0] * Vector3.forward * 5f);
                AssertFinite(sim, "tick " + t);
                worstLen = Mathf.Max(worstLen, MaxSegmentError(sim));
                for (int i = 1; i + 1 < sim.Joints.Length; i++)
                    worstBend = Mathf.Max(worstBend, Vector3.Angle(sim.Joints[i - 1] - sim.Joints[i], sim.Joints[i] - sim.Joints[i + 1]));
            }
            float alt = sim.Joints[sim.Flight.Girdle].y;
            TestContext.WriteLine($"after 4 s: mode {sim.Mode}, girdle at {alt:F2} m, worst segment error {worstLen:P3}, worst bend {worstBend:F1} deg, beats {sim.Flight.Wings.State[0].beats}");
            Assert.AreEqual(FlightMode.Air, sim.Mode);
            Assert.Greater(alt, 3f, "climbed toward the 5 m goal");
            Assert.Less(worstLen, 1e-3f, "segment lengths through the launch");
            Assert.Less(worstBend, 40f, "no fold through the launch");
            foreach (float w in sim.Flight.AirWeight) Assert.AreEqual(1f, w, 1e-6f);
        }

        // Hitches (0.25 s frames, zero frames) in every mode: nothing explodes, no NaN, the chain holds.
        [Test] public void Flight_LargeDt_StaysFinite()
        {
            var s = Serpent();
            var sim = Sim(s.species, new FlatGroundProbe());
            float[] dts = { kDt, 0.25f, 0f, kDt, 0.25f, kDt };
            Assert.IsTrue(sim.Flight.Launch(4f));
            for (int t = 0; t < 600; t++)
            {
                if (t == 300) sim.Flight.Land(sim.Joints[0] + sim.Rotations[0] * Vector3.forward * 8f);
                sim.Tick(t % 37 == 0 ? dts[(t / 37) % dts.Length] : kDt, sim.Joints[0]);
                AssertFinite(sim, $"tick {t} ({sim.Mode})");
                Assert.Less(MaxSegmentError(sim), 0.02f, $"chain at tick {t} ({sim.Mode})");
                Assert.Less(sim.Flight.Model.Velocity.magnitude, 20f, "no blow-up");
            }
        }

        internal static void FlyAndLand(CreatureSim sim, Vector3 landAt, int source, float maxSeconds, out int ticks)
        {
            Assert.IsTrue(sim.Flight.Launch(landAt.y + 3f));
            ticks = 0;
            // climb out, turn back to the landing point, land
            for (; ticks < 180; ticks++) sim.Tick(kDt, sim.Joints[0]);
            sim.Flight.Land(landAt, source);
            int max = Mathf.RoundToInt(maxSeconds / kDt);
            for (; ticks < max && sim.Mode != FlightMode.Ground; ticks++)
            {
                sim.Tick(kDt, sim.Joints[0]); // the landing autopilot flies the approach
                AssertFinite(sim, "landing tick " + ticks);
            }
        }

        // Fly, land on flat ground: lands near the point, then stands with planted feet at its clearance.
        [Test] public void Land_Flat_FeetPlanted()
        {
            var s = Serpent();
            var sim = Sim(s.species, new FlatGroundProbe());
            Vector3 landAt = new Vector3(0f, 0f, 6f);
            FlyAndLand(sim, landAt, 0, 30f, out int ticks);
            Assert.AreEqual(FlightMode.Ground, sim.Mode, "landed");
            float miss = Vector3.ProjectOnPlane(sim.Joints[sim.Flight.Girdle] - landAt, Vector3.up).magnitude;
            TestContext.WriteLine($"landed after {ticks * kDt:F1} s, {miss:F2} m from the point");
            Assert.Less(miss, 4f, "lands near the point");
            // stand: feet planted, body at its clearance
            var feet = new Vector3[sim.Legs.Length];
            for (int t = 0; t < 30; t++) sim.Tick(kDt, sim.Joints[0]);
            for (int l = 0; l < feet.Length; l++) feet[l] = sim.Legs[l].foot;
            for (int t = 0; t < 60; t++)
            {
                sim.Tick(kDt, sim.Joints[0]);
                for (int l = 0; l < feet.Length; l++)
                    if (!sim.Legs[l].stepping) Assert.Less((sim.Legs[l].foot - feet[l]).magnitude, 1e-3f, $"foot {l} slides after landing");
                for (int l = 0; l < feet.Length; l++) feet[l] = sim.Legs[l].foot;
            }
            for (int i = 0; i < sim.Joints.Length; i++)
                Assert.AreEqual(s.species.joints[i].clearance, sim.Joints[i].y, 0.03f, $"joint {i} height after landing");
            Assert.Less(MaxSegmentError(sim), 1e-3f);
            Assert.IsFalse(sim.Flight.Perched);
        }

        // Size variation (species.scale x look.sizeJitter = sizeScale): a smaller and a larger individual both
        // take off, fly and land cleanly, and the flight's size-dependent quantities derive from sizeScale
        // (lengths x s, speeds x sqrt(s), rates / sqrt(s)).
        [TestCase(0.9f)]
        [TestCase(1.1f)]
        public void Size_Jittered_FliesAndLands(float scale)
        {
            var s = Serpent();
            var sim = Sim(s.species, new FlatGroundProbe(), default, scale);
            float T = Mathf.Sqrt(scale);
            Assert.AreEqual(scale, sim.SizeScale, 1e-6f);
            Assert.AreEqual(T, sim.TimeScale, 1e-5f, "timing scales with sqrt(scale)");
            FlightModel m = sim.Flight.Model;
            Assert.AreEqual(scale, m.SizeScale, 1e-6f, "the flight model takes the individual's size");
            Assert.AreEqual(s.species.flight.cruiseSpeed * T, m.SpeedGoal, 1e-4f, "cruise speed x sqrt(scale)");
            Assert.AreEqual(s.species.flight.strokeRateMin / T, m.StrokeRate, 1e-4f, "stroke rate / sqrt(scale)");

            for (int t = 0; t < 30; t++) sim.Tick(kDt, new Vector3(0f, 0f, 100f));
            Vector3 landAt = new Vector3(0f, 0f, 6f * scale);
            Assert.IsTrue(sim.Flight.Launch(landAt.y + 3f));
            float worstAir = 0f, top = 0f;
            for (int t = 0; t < 240; t++)
            {
                sim.Tick(kDt, sim.Joints[0] + sim.Rotations[0] * Vector3.forward * 5f);
                AssertFinite(sim, $"air tick {t}");
                worstAir = Mathf.Max(worstAir, MaxSegmentError(sim));
                top = Mathf.Max(top, sim.Joints[sim.Flight.Girdle].y);
            }
            Assert.AreEqual(FlightMode.Air, sim.Mode, "flying");
            Assert.Greater(top, 2f, "climbed toward the 3 m goal");
            Assert.Less(worstAir, 1e-3f, "segment lengths in the air");

            sim.Flight.Land(landAt, 0);
            int ticks = 0;
            for (; ticks < 1800 && sim.Mode != FlightMode.Ground; ticks++)
            {
                sim.Tick(kDt, sim.Joints[0]);
                AssertFinite(sim, "landing tick " + ticks);
            }
            Assert.AreEqual(FlightMode.Ground, sim.Mode, "landed");
            for (int t = 0; t < 60; t++) sim.Tick(kDt, sim.Joints[0]);
            float miss = Vector3.ProjectOnPlane(sim.Joints[sim.Flight.Girdle] - landAt, Vector3.up).magnitude;
            TestContext.WriteLine($"scale {scale}: top {top:F2} m, landed after {ticks * kDt:F1} s, {miss:F2} m from the point");
            Assert.Less(miss, 4f * scale, "lands near the point");
            for (int i = 0; i < sim.Joints.Length; i++)
                Assert.AreEqual(s.species.joints[i].clearance * scale, sim.Joints[i].y, 0.03f * scale, $"joint {i} clearance x scale after landing");
            Assert.Less(MaxSegmentError(sim), 1e-3f);
        }

        // Land on top of a rock (a non-terrain surface): it stands on the rock, perched, its tail hangs over
        // the edge.
        [Test] public void Land_RockTop_Perch_Drape_TailHangs()
        {
            var s = Serpent();
            var rock = new WingedTestSurfaces().AddRock(new Vector3(0f, 0f, 8f), new Vector3(1.6f, 1.2f, 1.6f), 7);
            var sim = Sim(s.species, rock);
            FlyAndLand(sim, new Vector3(0f, 1.2f, 8f), 7, 40f, out int ticks);
            Assert.AreEqual(FlightMode.Ground, sim.Mode, "landed");
            for (int t = 0; t < 120; t++) sim.Tick(kDt, sim.Joints[0]);
            int g = sim.Flight.Girdle;
            float top = 1.2f;
            TestContext.WriteLine($"girdle at {sim.Joints[g]}, tail tip at {sim.Joints[sim.Joints.Length - 1]}, perched {sim.Flight.Perched}");
            var ys = new System.Text.StringBuilder("joint heights:");
            for (int i = 0; i < sim.Joints.Length; i++) ys.Append($" {sim.Joints[i].y:F2}");
            TestContext.WriteLine(ys.ToString());
            Assert.IsTrue(sim.Flight.Perched);
            Assert.AreEqual(top + s.species.joints[g].clearance, sim.Joints[g].y, 0.08f, "girdle stands on the rock");
            Assert.Less(sim.Joints[sim.Joints.Length - 1].y, top - 0.3f, "the tail hangs over the edge");
            Assert.Less(MaxSegmentError(sim), 1e-3f);
            for (int i = 1; i + 1 < sim.Joints.Length; i++)
                Assert.LessOrEqual(Vector3.Angle(sim.Joints[i - 1] - sim.Joints[i], sim.Joints[i] - sim.Joints[i + 1]), 60f, $"fold at joint {i}");
        }
    }
}
