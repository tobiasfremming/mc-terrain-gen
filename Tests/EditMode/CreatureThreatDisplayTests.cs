using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The generic pieces of the menacing giant centipede (Scolopendra v2, 2026-10-20), each off by default:
    //   ThreatDisplay  at a standstill the forebody rears (CreatureSim.JointLift) and CreatureSim.Display ramps to 1;
    //                  it drops when the creature walks; the planted feet stay put; Froude-timed.
    //   Pincer display the pincers follow Display: spread to displayOpenDeg, lifted by displayRaiseDeg.
    //   Walk.burst     the cruise speed surges past cruise but never past the gait's stride limit; seeded per
    //                  individual (same seed, same bursts); factor <= 1 never surges.
    //   Antenna probe  probeDeg sweeps the strand's rest curve: a still body's antenna tips keep moving; 0 = still.
    //   Leg spurs      spurLength adds a raked horn per segment after the leg's own vertices (prefix unchanged).
    //   Plate keel / corner spikes, Wander arrive / pause params.
    // RED proofs: the display check bites on a creature that never stops; the burst check bites on factor 1.
    public class CreatureThreatDisplayTests
    {
        const float kDt = 1f / 60f;

        static CreatureSpecies Scolo() => CreatureKitAssets.Load("Scolopendra");

        static CreatureSim Sim(CreatureSpecies sp, float s, int seed = 1) =>
            new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, seed);

        static Vector3 Ahead(CreatureSim sim) => sim.Joints[0] + (sim.Rotations[0] * Vector3.forward) * (10f * sim.SizeScale);

        static ThreatDisplay Display(CreatureSpecies sp)
        {
            foreach (CreaturePart p in sp.Compiled.Parts) if (p is ThreatDisplay d) return d;
            return null;
        }

        static List<PincerRuntime> Pincers(CreatureSim sim)
        {
            var l = new List<PincerRuntime>();
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is PincerRuntime p) l.Add(p);
            return l;
        }

        static SpeciesCtx Ctx(int joints) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, false);

        // ------------------------------------------------------------ ThreatDisplay

        [Test] public void ThreatDisplay_Validate_RejectsBadData()
        {
            var ok = new ThreatDisplay { id = "t", headLift = 0.01f, liftJoints = 6 };
            Assert.IsNull(ok.Validate(Ctx(20)));
            Assert.AreEqual(0, ok.BoneCount(Ctx(20)));
            var bad = (ThreatDisplay)ok.Clone(); bad.headLift = -1f;
            Assert.IsNotNull(bad.Validate(Ctx(20)), "negative lift");
            bad = (ThreatDisplay)ok.Clone(); bad.riseSeconds = 0f;
            Assert.IsNotNull(bad.Validate(Ctx(20)), "no rise time");
            bad = (ThreatDisplay)ok.Clone(); bad.liftJoints = 0;
            Assert.IsNotNull(bad.Validate(Ctx(20)), "no lift joints");
            Assert.AreEqual(1f, ok.LiftShare(0), 1e-6f, "the head lifts fully");
            Assert.AreEqual(0f, ok.LiftShare(6), 1e-6f, "nothing at liftJoints");
            for (int i = 1; i < 6; i++) Assert.Less(ok.LiftShare(i), ok.LiftShare(i - 1), "the lift fades toward the tail");
        }

        // Walk `walk` s (x T), stand `stand` s (x T), walk `again` s (x T); per tick: (walking, sim).
        static void Run(CreatureSim sim, float walk, float stand, float again, System.Action<int, CreatureSim> each)
        {
            float T = sim.TimeScale;
            for (int t = 0; t < (int)(walk * T / kDt); t++) { sim.Tick(kDt, Ahead(sim)); each(0, sim); }
            for (int t = 0; t < (int)(stand * T / kDt); t++) { sim.Tick(kDt, sim.Joints[0]); each(1, sim); }
            for (int t = 0; t < (int)(again * T / kDt); t++) { sim.Tick(kDt, Ahead(sim)); each(2, sim); }
        }

        [TestCase(0.9f)] [TestCase(1.1f)] public void ThreatDisplay_RearsAtAStandstill_DropsWhenWalking(float jitter)
        {
            CreatureSpecies sp = Scolo();
            ThreatDisplay d = Display(sp);
            Assert.IsNotNull(d, "the Scolopendra has a ThreatDisplay");
            float s = sp.scale * jitter;
            CreatureSim sim = Sim(sp, s);
            int n = sim.Legs.Length;
            var planted = new Vector3[n]; var wasPlanted = new bool[n];
            float walkMax = 0f, standMax = 0f, headWalk = 0f, headStand = 0f, slide = 0f, endWalk = 1f;
            int ticksAgain = 0;
            Run(sim, 4f, 6f, 3f, (phase, w) =>
            {
                if (phase == 0) { walkMax = Mathf.Max(walkMax, w.Display); headWalk = w.Joints[0].y - w.Joints[12].y; }
                if (phase == 1)
                {
                    standMax = Mathf.Max(standMax, w.Display);
                    headStand = Mathf.Max(headStand, w.Joints[0].y - w.Joints[12].y);
                    for (int l = 0; l < n; l++)
                    {
                        bool p = !w.Legs[l].stepping;
                        if (p && wasPlanted[l]) slide = Mathf.Max(slide, (w.Legs[l].foot - planted[l]).magnitude);
                        planted[l] = w.Legs[l].foot; wasPlanted[l] = p;
                    }
                }
                if (phase == 2) { ticksAgain++; endWalk = w.Display; }
            });
            float lift = d.headLift * s;
            TestContext.WriteLine($"x{s:F2}: display walking <= {walkMax:F3}, standing up to {standMax:F3}, back to {endWalk:F3} walking; head over joint 12 {headWalk * 1000f:F1} -> {headStand * 1000f:F1} mm (lift {lift * 1000f:F1} mm); planted feet slid <= {slide * 1000f:F4} mm");
            Assert.AreEqual(0f, walkMax, 1e-6f, "no display while walking");
            Assert.AreEqual(1f, standMax, 1e-4f, "the full display at a standstill");
            Assert.Less(endWalk, 1e-4f, "the display drops when it walks off");
            Assert.Greater(headStand - headWalk, 0.6f * lift, "the head rears by about headLift");
            Assert.Less(headStand - headWalk, 1.2f * lift, "and not past it");
            Assert.Less(slide, 1e-5f * s, "planted feet stay put while it rears");
        }

        [Test] public void Red_ThreatDisplay_NeverStopping_NeverDisplays()
        {
            CreatureSpecies sp = Scolo();
            CreatureSim sim = Sim(sp, sp.scale);
            float max = 0f;
            Run(sim, 10f, 0f, 0f, (phase, w) => max = Mathf.Max(max, w.Display));
            Assert.AreEqual(0f, max, 1e-6f, "the display check bites: a creature that never stops never displays");
        }

        // Time from stopping to Display 0.5 at size s (seconds).
        static float HalfDisplayTime(CreatureSpecies sp, float s)
        {
            CreatureSim sim = Sim(sp, s);
            for (int t = 0; t < (int)(4f * sim.TimeScale / kDt); t++) sim.Tick(kDt, Ahead(sim));
            float t0 = sim.Time;
            for (int t = 0; t < 20000; t++)
            {
                sim.Tick(kDt * 0.25f, sim.Joints[0]);
                if (sim.Display >= 0.5f) return sim.Time - t0;
            }
            return -1f;
        }

        [Test] public void ThreatDisplay_Size_TimeXSqrtS()
        {
            CreatureSpecies sp = Scolo();
            float a = HalfDisplayTime(sp, 0.9f * sp.scale), b = HalfDisplayTime(sp, 1.1f * sp.scale);
            TestContext.WriteLine($"half display after {a:F3} s / {b:F3} s (ratio {b / a:F4}, sqrt {Mathf.Sqrt(1.1f / 0.9f):F4})");
            Assert.Greater(a, 0f); Assert.Greater(b, 0f);
            Assert.AreEqual(Mathf.Sqrt(1.1f / 0.9f), b / a, 0.04f, "Froude: the display's clock x sqrt s");
        }

        // ------------------------------------------------------------ the pincers' display pose

        [Test] public void Pincer_Display_SpreadsAndRaises()
        {
            CreatureSpecies sp = Scolo();
            CreatureSim sim = Sim(sp, sp.scale);
            List<PincerRuntime> ps = Pincers(sim);
            Assert.AreEqual(4, ps.Count);
            var restTip = new float[4];
            Run(sim, 4f, 0f, 0f, (p, w) => { });
            for (int i = 0; i < 4; i++) restTip[i] = ps[i].Tip().y - sim.Joints[ps[i].Part.at.joint].y;
            Run(sim, 0f, 6f, 0f, (p, w) => { });
            Assert.AreEqual(1f, sim.Display, 1e-4f);
            for (int i = 0; i < 4; i++)
            {
                Pincer p = ps[i].Part;
                float tipUp = ps[i].Tip().y - sim.Joints[p.at.joint].y;
                TestContext.WriteLine($"{p.id}: opening {ps[i].Opening:F1} deg (display {p.displayOpenDeg}), raised {ps[i].Raised:F1} deg (display {p.displayRaiseDeg}), tip over its joint {restTip[i] * 1000f:F0} -> {tipUp * 1000f:F0} mm");
                Assert.GreaterOrEqual(ps[i].Opening, 0.85f * p.displayOpenDeg, p.id + ": spread to its display opening");
                Assert.LessOrEqual(ps[i].Opening, p.displayOpenDeg + 1e-3f, p.id + ": never past it");
                Assert.AreEqual(p.displayRaiseDeg, ps[i].Raised, 0.05f * Mathf.Abs(p.displayRaiseDeg) + 1e-3f, p.id + ": lifted by displayRaiseDeg");
                if (p.displayRaiseDeg > 15f) Assert.Greater(tipUp, restTip[i] + 0.1f * p.length * sp.scale, p.id + ": the raised tip is higher");
            }
        }

        [Test] public void Pincer_Raise_IsARigidTurnAboutTheRoot()
        {
            var p = new Pincer
            {
                id = "u", at = new PartSocket(5, 1, new Vector3(0.006f, 0f, -0.003f)), direction = new Vector3(0.44f, 0.2f, -0.88f),
                hingeAxis = new Vector3(0f, -1f, 0f), length = 0.06f, segments = 4, radiusStart = 0.003f, radiusEnd = 0.0013f, tipLength = 0.006f, curveDeg = 16f,
            };
            p.at.side = 1;
            Vector3 root0 = p.PointAt(0, 0f, 10f, 30f, out _);
            Assert.Less((root0 - new Vector3(0.006f, 0f, -0.003f)).magnitude, 1e-6f, "the root stays");
            for (int k = 0; k < 4; k++)
            {
                Vector3 a = p.PointAt(k, 0.5f * p.SegmentLength(k), 10f, out _);
                Vector3 b = p.PointAt(k, 0.5f * p.SegmentLength(k), 10f, 30f, out _);
                Assert.AreEqual((a - root0).magnitude, (b - root0).magnitude, 1e-6f, "rigid: distances from the root kept");
                Assert.Greater(b.y, a.y, "raised toward the joint's up");
            }
            Vector3 t0 = p.PointAt(3, p.SegmentLength(3), 0f, 0f, out _), t1 = p.PointAt(3, p.SegmentLength(3), 0f, out _);
            Assert.AreEqual(t1, t0, "raise 0: the unraised chain exactly");
            // mirror symmetry
            var l = (Pincer)p.Clone(); l.at.side = -1;
            Vector3 r = p.PointAt(3, p.SegmentLength(3), 12f, 25f, out _), m = l.PointAt(3, l.SegmentLength(3), 12f, 25f, out _);
            Assert.AreEqual(r.x, -m.x, 1e-6f); Assert.AreEqual(r.y, m.y, 1e-6f); Assert.AreEqual(r.z, m.z, 1e-6f);
        }

        // ------------------------------------------------------------ Walk.burst

        static void Straight(CreatureSim sim, float seconds, out float vMax, out float vMean, out float bMax, List<float> trace = null)
        {
            vMax = 0f; vMean = 0f; bMax = 0f;
            int n = (int)(seconds * sim.TimeScale / kDt), m = 0;
            for (int t = 0; t < n; t++)
            {
                sim.Tick(kDt, Ahead(sim));
                if (t < n / 8) continue;
                vMax = Mathf.Max(vMax, sim.Speed); vMean += sim.Speed; m++;
                bMax = Mathf.Max(bMax, sim.BurstFactor);
                trace?.Add(sim.Speed);
            }
            vMean /= Mathf.Max(1, m);
        }

        [Test] public void Burst_SurgesPastCruise_NeverPastTheStrideLimit_SeededPerIndividual()
        {
            CreatureSpecies sp = Scolo();
            GaitBurst b = sp.Compiled.Runtime.gaitBurst;
            Assert.IsTrue(b.On, "the Scolopendra dashes");
            float s = sp.scale;
            CreatureSim sim = Sim(sp, s, 3);
            var trace = new List<float>();
            Straight(sim, 20f, out float vMax, out float vMean, out float bMax, trace);
            float cruise = sp.Compiled.Runtime.speed * sim.TimeScale, cap = sim.LegsInternal.MaxSpeed;
            TestContext.WriteLine($"x{s:F1}: cruise {cruise:F3} m/s, stride limit {cap:F3}, speed mean {vMean:F3} max {vMax:F3}, burst factor up to {bMax:F2}");
            Assert.Greater(vMax, 1.3f * cruise, "a burst runs well past the cruise speed");
            Assert.LessOrEqual(vMax, cap * 1.0001f, "never past the gait's stride limit");
            Assert.AreEqual(b.factor, bMax, 0.02f, "each burst peaks at its factor");
            int slow = 0; foreach (float v in trace) if (v < 1.05f * cruise) slow++;
            Assert.Greater(slow, trace.Count / 5, "and falls back to cruising between bursts");
            // the same seed: the same bursts; another seed: others
            CreatureSim twin = Sim(sp, s, 3), other = Sim(sp, s, 4);
            var t2 = new List<float>(); var t3 = new List<float>();
            Straight(twin, 20f, out _, out _, out _, t2);
            Straight(other, 20f, out _, out _, out _, t3);
            float same = 0f, diff = 0f;
            for (int i = 0; i < trace.Count; i++) { same = Mathf.Max(same, Mathf.Abs(trace[i] - t2[i])); diff = Mathf.Max(diff, Mathf.Abs(trace[i] - t3[i])); }
            Assert.AreEqual(0f, same, 0f, "deterministic per seed");
            Assert.Greater(diff, 0.1f * cruise, "individuals dash at their own times");
        }

        [Test] public void Red_Burst_FactorOne_NeverSurges()
        {
            CreatureSpecies sp = Object.Instantiate(Scolo());
            CreatureSpecies rt = sp.Compiled.Runtime;
            rt.gaitBurst.factor = 1f;
            CreatureSim sim = Sim(sp, sp.scale, 3);
            Straight(sim, 20f, out float vMax, out _, out float bMax);
            float cruise = rt.speed * sim.TimeScale;
            TestContext.WriteLine($"RED: factor 1: max {vMax:F3} m/s, cruise {cruise:F3}");
            Assert.LessOrEqual(vMax, cruise * 1.0001f, "the burst check bites: without bursts the speed stays at cruise");
            Assert.AreEqual(1f, bMax, 0f);
            Object.DestroyImmediate(sp);
        }

        // ------------------------------------------------------------ Antenna probing

        static float TipTravel(float probeDeg)
        {
            var spec = new AntennaSpec
            {
                joint = 0, side = 1, offset = new Vector3(0.005f, 0.001f, 0.006f), direction = new Vector3(0.3f, 0.12f, 0.95f), length = 0.05f,
                segments = 10, radiusStart = 0.0008f, radiusEnd = 0.0002f, stiffnessHz = 2.6f, damping = 0.8f, airDrag = 0.6f, droopDeg = 12f,
                probeDeg = probeDeg, probeHz = probeDeg > 0f ? 1.2f : 0f,
            };
            var joints = new[] { Vector3.zero, new Vector3(0f, 0f, -0.01f) };
            var rot = new[] { Quaternion.identity, Quaternion.identity };
            var a = new CreatureAntennae(new[] { spec }, joints, rot, 1f);
            int tip = a.FirstPoint(0) + spec.Segments;
            for (int t = 0; t < 120; t++) a.Step(kDt, joints, rot);   // settle
            Vector3 lo = a.Points[tip], hi = lo;
            for (int t = 0; t < 240; t++) { a.Step(kDt, joints, rot); lo = Vector3.Min(lo, a.Points[tip]); hi = Vector3.Max(hi, a.Points[tip]); }
            return (hi - lo).magnitude;
        }

        [Test] public void Antenna_Probe_SweepsTheTips_ZeroIsStill()
        {
            float still = TipTravel(0f), probing = TipTravel(16f);
            TestContext.WriteLine($"tip travel over 4 s on a still body: {still * 1000f:F3} mm still, {probing * 1000f:F2} mm probing 16 deg");
            Assert.Less(still, 1e-5f, "probeDeg 0: the strand rests");
            Assert.Greater(probing, 0.05f * 2f * Mathf.Sin(16f * Mathf.Deg2Rad), "probing sweeps the tip (a good part of its 2 L sin(probe) range)");
            var bad = new Antenna { id = "a", length = 0.05f, segments = 8, stiffnessHz = 2f, direction = Vector3.forward, probeDeg = 10f, probeHz = 0f };
            Assert.IsNotNull(bad.Validate(Ctx(4)), "probing needs a rate");
        }

        // ------------------------------------------------------------ leg spurs

        [Test] public void LegSpurs_AppendedAfterTheLeg_RakedTowardItsTip_OffIsIdentical()
        {
            var segs = new[]
            {
                new LegSegment { length = 0.01f, radiusStart = 0.0015f, radiusEnd = 0.0012f },
                new LegSegment { length = 0.012f, radiusStart = 0.0012f, radiusEnd = 0.001f },
                new LegSegment { length = 0.008f, radiusStart = 0.0008f, radiusEnd = 0.0002f },
            };
            var pts = new[] { Vector3.zero, new Vector3(0.01f, 0f, 0f), new Vector3(0.022f, 0f, 0f), new Vector3(0.03f, 0f, 0f) };
            var plain = new CreatureMeshLists();
            ArthropodLegMesh.Build(plain, segs, pts, 0, Vector3.forward, 1, 0, 6, 1, 0.002f);
            var spurred = (LegSegment[])segs.Clone();
            for (int k = 0; k < spurred.Length; k++) spurred[k].spurLength = 0.003f;
            var withSpurs = new CreatureMeshLists();
            ArthropodLegMesh.Build(withSpurs, spurred, pts, 0, Vector3.forward, 1, 0, 6, 1, 0.002f);
            Assert.Greater(withSpurs.pos.Count, plain.pos.Count, "spurs add vertices");
            for (int i = 0; i < plain.pos.Count; i++) Assert.AreEqual(plain.pos[i], withSpurs.pos[i], "the leg itself unchanged (spurs come after it)");
            for (int i = 0; i < plain.tris.Count; i++) Assert.AreEqual(plain.tris[i], withSpurs.tris[i]);
            // each spur: below its segment (frame up = cross(planeNormal, dir) = +y here), raked toward the tip, foot layer
            Vector3 up = Vector3.Cross(Vector3.forward, Vector3.right);
            int perSpur = (withSpurs.pos.Count - plain.pos.Count) / segs.Length;
            for (int k = 0; k < segs.Length; k++)
            {
                Vector3 c = Vector3.zero, low = new Vector3(0f, float.MaxValue, 0f);
                for (int i = plain.pos.Count + k * perSpur; i < plain.pos.Count + (k + 1) * perSpur; i++)
                {
                    Assert.AreEqual(ArthropodLegMesh.PartFoot, (int)withSpurs.uv1[i].x, "spurs are claw-coloured");
                    Assert.Less(withSpurs.nrm[i].sqrMagnitude, 1.0001f); Assert.Greater(withSpurs.nrm[i].sqrMagnitude, 0.9999f);
                    c += withSpurs.pos[i] / perSpur;
                    if (Vector3.Dot(withSpurs.pos[i], up) < low.y) low = new Vector3(withSpurs.pos[i].x, Vector3.Dot(withSpurs.pos[i], up), 0f);
                }
                TestContext.WriteLine($"spur {k}: centre {c * 1000f} mm, its point {low.y * 1000f:F2} mm below the axis at x {low.x * 1000f:F2} mm");
                Assert.Less(Vector3.Dot(c, up), 0f, "on the segment's lower side");
                Assert.Less(low.y, -0.5f * 0.003f, "standing well out of the segment");
                Assert.Greater(low.x, c.x, "raked toward the leg's tip");
                Assert.Less(c.x, pts[k + 1].x, "near the segment's distal end");
                Assert.Greater(c.x, pts[k].x + 0.5f * segs[k].length, "near the segment's distal end");
            }
        }

        // ------------------------------------------------------------ plate keel and corner spikes

        [Test] public void ArmourPlates_KeelAndCornerSpikes()
        {
            var a = new ArmourPlates { id = "p", toJoint = 4, keel = 0f, cornerSpike = 0f };
            var b = (ArmourPlates)a.Clone(); b.keel = 0.3f; b.cornerSpike = 0.35f;
            ArmourPlates.Plate pa = a.PlateAt(0, 0.0055f, 0.01f, 0.003f), pb = b.PlateAt(0, 0.0055f, 0.01f, 0.003f);
            Assert.Greater(b.TopY(pb, 0f, 0.6f), a.TopY(pa, 0f, 0.6f) + 0.25f * 0.003f, "a keel along the midline");
            Assert.AreEqual(a.TopY(pa, 0.6f, 0.3f), b.TopY(pb, 0.6f, 0.3f), 1e-7f, "the keel is narrow: the flanks keep their dome");
            float s = ArmourPlates.SpikePeak;
            Assert.AreEqual(a.HalfWidthAt(pa, s) + 0.35f * pa.w, b.HalfWidthAt(pb, s), 1e-6f, "the corner spike juts out by cornerSpike x the half width");
            Assert.Less(b.Top(pb, 1f, s).z, a.Top(pa, 1f, s).z, "and sweeps back");
            Assert.AreEqual(a.Top(pa, 1f, 0.2f), b.Top(pb, 1f, 0.2f), "the front half of the margin is plain");
            Assert.AreEqual(0f, ArmourPlates.SpikeAt(1f), 1e-6f, "the spike closes at the back margin");
            Assert.IsNull(b.Validate(Ctx(10)));
            var bad = (ArmourPlates)b.Clone(); bad.cornerSpike = 0.9f;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "spike out of range");
        }

        // ------------------------------------------------------------ Wander arrive / pause

        [Test] public void Wander_SpeciesPauses_AreFroudeTimed()
        {
            CreatureSpecies sp = Scolo();
            Wander w = null;
            foreach (CreatureModule m in sp.Compiled.Modules) if (m is Wander x) w = x;
            Assert.IsNotNull(w); Assert.IsTrue(w.Custom, "the Scolopendra sets its own wander");
            float s = sp.scale;
            EnclosureArea area = KitChecks.LabArea(sp);
            CreatureSim sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, area.center, Vector3.forward, s, 2) { Bounds = area };
            WanderBrain brain = WanderBrain.For(sp, area, 2);
            // a pause runs from the brain's arrival (PausesStarted) to its next goal (a target that is neither the head
            // nor its glance point, 0.3 x s ahead of it)
            float still = -1f; int started = 0; var pauses = new List<float>();
            for (int t = 0; t < (int)(240f / kDt) && pauses.Count < 6; t++)
            {
                Vector3 target = brain.Target(sim, kDt);
                if (brain.PausesStarted > started) { started = brain.PausesStarted; still = 0f; }
                else if (still >= 0f)
                {
                    float d = Vector3.ProjectOnPlane(target - sim.Joints[0], Vector3.up).magnitude;
                    if (d > 1e-4f && Mathf.Abs(d - 0.3f * s) > 1e-3f) { pauses.Add(still); still = -1f; }
                    else still += kDt;
                }
                sim.Tick(kDt, target);
            }
            Assert.GreaterOrEqual(pauses.Count, 3, "it pauses at its goals");
            float T = sim.TimeScale;
            foreach (float p in pauses)
            {
                TestContext.WriteLine($"pause {p:F2} s (range {w.pauseMin * T:F2}..{w.pauseMax * T:F2})");
                Assert.GreaterOrEqual(p, w.pauseMin * T - 2f * kDt); Assert.LessOrEqual(p, w.pauseMax * T + 2f * kDt);
            }
            Assert.IsNotNull(new Wander { pauseMin = 2f, pauseMax = 1f }.Validate(Ctx(4)), "pauseMin past pauseMax");
        }
    }
}
