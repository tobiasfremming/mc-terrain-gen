using System.Collections.Generic;
using CreatureKit;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Sideways walking (Walk.travel Sideways -> CreatureSpecies.sideways; a generic sim feature, off by default): the
    // heading stays the body's forward and the head travels along the body's lateral axis (a crab), to whichever side
    // needs the smaller turn, or forward (slowly) when the target lies dead ahead; a flip of side brakes to a stop first;
    // a turn pivots the rigid body about its girdles' middle, not its front joint; the legs aim their steps along the
    // travel. Checks on the crab form of ArthropodPlan: the travel follows the lateral axis (RED: the same crab walking
    // forward travels along its forward axis); planted feet never slide (the kit's weave, plus side flips); a turn in
    // place pivots about the middle; forward when ahead, at the forward factor; containment in the lab area at 0.9x and
    // 1.1x; Froude similarity (0.9x / 1.1x); a sideways body must be rigid. Every other species: bit-identical (the
    // goldens).
    public class CreatureSidewaysTests
    {
        const float kDt = 1f / 60f;

        static CreatureSpecies Crab(ArthropodTravel travel = ArthropodTravel.FormDefault)
        {
            var plan = new ArthropodPlan { id = "plan", form = ArthropodForm.Crab, bodyLength = 0.3f, travel = travel };
            return CreatureArchetypeTests.Make("SideCrab", new CreaturePart[] { plan }).Compiled.Runtime;
        }

        static CreatureSim Sim(CreatureSpecies sp, float s, int seed = 1) => new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, seed);

        static Vector3 Middle(CreatureSim sim)
        {
            Vector3 c = Vector3.zero;
            for (int i = 0; i < sim.Joints.Length; i++) c += sim.Joints[i];
            return c / sim.Joints.Length;
        }

        // The mean |cos| between the body's travel (its middle's velocity) and an axis of the body (frame of joint 2),
        // over a straight walk toward `target` (after 3 s), and how far the middle got.
        static float Alignment(CreatureSim sim, Vector3 target, Vector3 bodyAxis, float seconds, out float travelled)
        {
            float sum = 0f; int n = 0;
            Vector3 start = Middle(sim), prev = start;
            for (int t = 0; t < (int)(seconds / kDt); t++)
            {
                sim.Tick(kDt, target);
                Vector3 m = Middle(sim), v = m - prev; v.y = 0f;
                prev = m;
                if (t * kDt < 3f || v.magnitude < 1e-5f) continue;
                Vector3 axis = sim.Rotations[2] * bodyAxis; axis.y = 0f;
                sum += Mathf.Abs(Vector3.Dot(v.normalized, axis.normalized)); n++;
            }
            travelled = (prev - start).magnitude;
            return n > 0 ? sum / n : 0f;
        }

        [TestCase(1f), TestCase(0.9f), TestCase(1.1f)]
        public void Sideways_TravelAlongLateralAxis(float f)
        {
            CreatureSpecies sp = Crab();
            Assert.IsTrue(sp.sideways, "the crab form walks sideways by default");
            var sim = Sim(sp, f);
            float a = Alignment(sim, new Vector3(0f, 0f, 40f), Vector3.right, 12f, out float d);
            TestContext.Out.WriteLine($"x{f}: |cos(travel, body right)| {a:F3}, travelled {d:F2} m");
            Assert.Greater(a, 0.95f, "travels along its lateral axis");
            Assert.Greater(d, 0.5f * sp.speed * 9f * Mathf.Sqrt(f), "it gets somewhere");
        }

        // RED for the alignment: the same crab with forward travel moves along its forward axis.
        [Test] public void Red_Forward_TravelsAlongForward()
        {
            CreatureSpecies sp = Crab(ArthropodTravel.Forward);
            Assert.IsFalse(sp.sideways);
            float side = Alignment(Sim(sp, 1f), new Vector3(0f, 0f, 40f), Vector3.right, 12f, out _);
            float fwd = Alignment(Sim(sp, 1f), new Vector3(0f, 0f, 40f), Vector3.forward, 12f, out _);
            TestContext.Out.WriteLine($"forward travel: |cos| with right {side:F3}, with forward {fwd:F3}");
            Assert.Less(side, 0.3f);
            Assert.Greater(fwd, 0.95f);
        }

        // The kit's gait check (weaving walk: planted feet fixed, never over-reached, every leg steps, groups take turns).
        [Test] public void Sideways_Gait_KitCheck() => Assert.IsNull(KitChecks.GaitProblem(Crab()));

        // Side flips: the target jumps from far left to far right of the body every 3 s; planted feet stay put through
        // the brake and the reversal.
        [Test] public void Sideways_Flips_NoFootSlides()
        {
            CreatureSpecies sp = Crab();
            var sim = Sim(sp, 1f);
            int n = sp.legs.Length, planted = 0, flips = 0;
            var prev = new Vector3[n]; var was = new bool[n];
            int lastSide = 0;
            for (int t = 0; t < 900; t++)
            {
                Vector3 right = sim.Rotations[2] * Vector3.right;
                float sign = (t / 180) % 2 == 0 ? 1f : -1f;
                sim.Tick(kDt, Middle(sim) + right * (sign * 5f));
                if (sim.TravelSide != lastSide) { flips++; lastSide = sim.TravelSide; }
                for (int l = 0; l < n; l++)
                {
                    LegState st = sim.Legs[l];
                    bool now = !st.stepping || st.stepT < 0f;
                    if (!sp.legs[l].feeler && now && was[l])
                    {
                        Assert.Less((st.foot - prev[l]).magnitude, 1e-5f, $"t={t}: planted foot {l} slid");
                        planted++;
                    }
                    prev[l] = st.foot; was[l] = now;
                }
            }
            TestContext.Out.WriteLine($"{flips} side changes, {planted} planted samples");
            Assert.GreaterOrEqual(flips, 4, "it flips side");
            Assert.Greater(planted, 1000);
        }

        // A turn with hardly any travel pivots the rigid body about its girdles' middle, not about its front joint.
        [Test] public void Sideways_TurnInPlace_PivotsAboutTheMiddle()
        {
            CreatureSpecies sp = Crab();
            var sim = Sim(sp, 1f);
            for (int t = 0; t < 30; t++) sim.Tick(kDt, sim.Joints[0]);
            Vector3 mid0 = Middle(sim), head0 = sim.Joints[0], fwd0 = sim.Heading;
            // a target close by, 45 deg round from the right side: a turn, little travel
            Vector3 tgt = mid0 + Quaternion.AngleAxis(45f, Vector3.up) * (sim.Rotations[2] * Vector3.right) * (0.25f * sp.BodyLength);
            for (int t = 0; t < 120; t++) sim.Tick(kDt, tgt);
            float turned = Vector3.Angle(fwd0, sim.Heading);
            float mid = (Middle(sim) - mid0).magnitude, head = (sim.Joints[0] - head0).magnitude;
            TestContext.Out.WriteLine($"turned {turned:F1} deg; middle moved {mid * 1000f:F1} mm, front joint {head * 1000f:F1} mm");
            Assert.Greater(turned, 10f, "it turns");
            Assert.Less(mid, 0.5f * head, "about its middle");
        }

        // A target dead ahead of the body and near (a shuffle): it walks forward, at the forward factor of its speed.
        [Test] public void Sideways_ForwardSlowly_WhenAhead()
        {
            CreatureSpecies sp = Crab();
            Assert.Greater(sp.SidewaysForward, 0f, "the crab may walk forward");
            var sim = Sim(sp, 1f);
            Vector3 from = Vector3.zero;
            for (int t = 0; t < 600; t++)
            {
                sim.Tick(kDt, sim.Joints[0] + sim.Heading * (1.2f * sp.BodyLength));
                Assert.AreEqual(0, sim.TravelSide, $"t={t}: forward");
                if (t == 120) from = Middle(sim);
            }
            float top = (Middle(sim) - from).magnitude / (480 * kDt);
            TestContext.Out.WriteLine($"forward: mean speed {top:F3} m/s of {sp.speed:F3}");
            Assert.LessOrEqual(top, sp.SidewaysForward * sp.speed * 1.1f, "slowly");
            Assert.Greater(top, 0.5f * sp.SidewaysForward * sp.speed, "but it goes");
        }

        [TestCase(0.9f, 3), TestCase(1.1f, 4)]
        public void Sideways_Containment(float f, int seed)
        {
            CreatureSpecies sp = Crab();
            EnclosureArea area = KitChecks.LabArea(sp);
            Assert.IsNull(KitChecks.WanderProblem(sp, sp.scale * f, seed, area, 300f));
        }

        [Test] public void Sideways_Size_Froude()
        {
            CreatureSpecies sp = Crab();
            Assert.IsNull(KitChecks.SizeProblem(sp, 0.9f * sp.scale, 1.1f * sp.scale, 1.1f * sp.scale));
        }

        // A sideways body must be rigid (the chain follows the head's path: a bending body would be dragged sideways).
        [Test] public void Sideways_Validate_NeedsRigidBody()
        {
            var body = new CreatureKit.SpineBody { id = "body", spacing = 0.05f, joints = new JointDef[4] };
            for (int i = 0; i < 4; i++) body.joints[i] = new JointDef { halfWidth = 0.05f, halfHeight = 0.03f, clearance = 0.1f, bendLimitDeg = 20f };
            var eyes = new Eyes { id = "eyes", at = new PartSocket(0, 0, new Vector3(0.02f, 0.02f, 0.02f)), radius = 0.01f };
            CompiledSpecies c = CompiledSpecies.Compose("t", new List<CreaturePart> { body, eyes }, new List<CreatureModule> { new Walk { speed = 0.2f, turnRateDeg = 90f, accel = 1f, travel = TravelMode.Sideways } });
            StringAssert.Contains("rigid", c.Error);
        }
    }
}
