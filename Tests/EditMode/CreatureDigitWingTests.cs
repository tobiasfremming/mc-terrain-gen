using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // DigitWing (Vesper step 1, skeleton only): the wing is its fingers - one jointed 3D chain per finger rooted at the
    // wrist, aimed by the flight's finger directions, flexing with the beat in the air and drooping under gravity when
    // folded on the ground, plus a small forward thumb claw. The fixture is the FleshWing fixture's winged FlowBody
    // (CreatureFleshWingTests: the FlowBody without its keel, the wing arm Leg2Bone upper 0.2 / lower 0.5, restOffset
    // (0.06, 0, -0.22), pole (0.2, 1, -0.4), DrakeLeg hind legs, Eyes; Walk + Fly with the stance-bound flight flags +
    // FlightExcursion) with a mirrored DigitWing on its own defaults and SkeletonOnly. Invariants: the bone layout; the
    // outer joint lags the root in steady beating; folded on the ground the fingers droop (lower tips than without droop),
    // stay above the ground by their radius, and the planted wrist (its thumb contact) does not move; every bend within
    // maxBendDeg through launch, beat, glide, touchdown and a walk; Froude-similar chains; no GC; F3 draws every joint.
    // Each has a RED.
    public class CreatureDigitWingTests
    {
        const float kDt = 1f / 60f;

        readonly List<CreatureRenderer> renderers = new List<CreatureRenderer>();

        [TearDown] public void TearDown()
        {
            foreach (CreatureRenderer r in renderers) r.Dispose();
            renderers.Clear();
        }

        // The game's path as far as the harness reaches (CreatureWorld is Unity-only): one individual added to the
        // species' CreatureRenderer, ticked and rendered every frame - nothing calls WriteBoneWorld by hand.
        CreatureRenderer Rendered(CreatureSpecies sp, float scale, int frames, out CreatureSim sim)
        {
            sim = FlowKit.Sim(sp, scale, 3);
            var r = new CreatureRenderer(sp, null);
            renderers.Add(r);
            r.Add(sim, LabGroup.Individual(sp, 3, scale));
            for (int f = 0; f < frames; f++) { sim.Tick(kDt, Ahead(sim, scale)); r.Render(); }
            return r;
        }

        internal static DigitWing Wing() => new DigitWing
        {
            id = "wing", at = new PartSocket(CreatureFleshWingTests.kShoulder, 0, Vector3.zero), mirror = true, arm = "arm",
        };

        internal static CreatureSpecies Species(System.Action<DigitWing> edit = null, string name = "DigitWingFixture", bool skeletonOnly = true)
        {
            DigitWing w = Wing(); edit?.Invoke(w);
            FlowBody body = CreatureFlowBodyTests.Body(); body.profile[2].keel = 0f;
            var parts = new List<CreaturePart> { body, CreatureFleshWingTests.Arm(), CreatureFleshWingTests.Hind(), w, CreatureKitComposeTests.TwoEyes() };
            var mods = new List<CreatureModule>
            {
                new Walk { speed = 0.45f, turnRateDeg = 60f, accel = 1.5f, turnSlowdown = 0.5f }, CreatureFleshWingTests.FlyModule(), new FlightExcursion(),
            };
            if (skeletonOnly) mods.Add(new SkeletonOnly());
            return FlowKit.Build(name, 1f, parts, mods);
        }

        internal static DigitWing Part(CreatureSpecies sp)
        {
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is DigitWing d) return d;
            return null;
        }

        internal static DigitWingRuntime Runtime(CreatureSim sim, int w = 0)
        {
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is DigitWingRuntime d && d.Wing == w) return d;
            return null;
        }

        static Vector3 Ahead(CreatureSim sim, float s) => sim.Joints[0] + sim.Rotations[0] * Vector3.forward * (2f * s);

        // ------------------------------------------------------------ layout

        [Test] public void DigitWing_Bones_Layout()
        {
            CreatureSpecies sp = Species();
            Assert.IsNull(sp.Validate());
            string layout = KitChecks.LayoutProblem(sp);
            Assert.IsNull(layout, "the kit's Layout check (skeleton only: no skin asked for): " + layout);
            Assert.AreEqual(2, sp.WingCount);
            DigitWing p = Part(sp);
            Assert.AreEqual(4, p.spec.FingerCount, "four fingers");
            Assert.AreEqual(p.FlightBones + 4 * p.fingerSegments + p.thumbSegments, p.BoneCount(default), "the flight's, fingers x segments, the thumb");
            CompiledSpecies c = sp.Compiled;
            int found = 0;
            for (int i = 0; i < c.Parts.Count; i++)
                if (c.Parts[i] is DigitWing d)
                {
                    int w = PteroWing.WingIndex(c.Parts, i);
                    Assert.AreEqual(c.Layout.Start(i), sp.WingBone(w, 0), "the flight's bones start the block");
                    found++;
                }
            Assert.AreEqual(2, found, "mirrored: two parts");
            // the blocks are contiguous: every range starts where the one before ends, and they fill BoneCount
            int next = 0;
            var starts = new List<int>();
            foreach (BoneRange r in sp.Layout.Ranges) if (r.Count > 0) starts.Add(r.Start);
            starts.Sort();
            foreach (int st in starts)
                foreach (BoneRange r in sp.Layout.Ranges)
                    if (r.Start == st && r.Count > 0) { Assert.AreEqual(next, r.Start, $"part {r.Id} contiguous"); next += r.Count; break; }
            Assert.AreEqual(sp.BoneCount, next, "the ranges fill BoneCount");
            // the runtime's own bones follow the flight's, and posing writes every one (finite, not the zero matrix)
            var sim = FlowKit.Sim(sp, 1f);
            for (int w = 0; w < 2; w++)
            {
                DigitWingRuntime rt = Runtime(sim, w);
                Assert.IsNotNull(rt, $"runtime {w}");
                Assert.AreEqual(sp.WingBone(w, 0) + p.FlightBones, rt.FirstExtraBone);
            }
            FlowKit.Run(sim, 0.5f, Ahead(sim, 1f));
            var bones = new Matrix4x4[sp.BoneCount];
            for (int i = 0; i < bones.Length; i++) bones[i] = Matrix4x4.zero;
            sim.WriteBoneWorld(bones, 0);
            for (int w = 0; w < 2; w++)
            {
                DigitWingRuntime rt = Runtime(sim, w);
                for (int b = 0; b < p.ExtraBones; b++)
                {
                    Matrix4x4 m = bones[rt.FirstExtraBone + b];
                    Assert.AreNotEqual(Matrix4x4.zero, m, $"wing {w} bone {b} written");
                    for (int e = 0; e < 16; e++) Assert.IsFalse(float.IsNaN(m[e]) || float.IsInfinity(m[e]), $"wing {w} bone {b} finite");
                }
                // the finger chains start at the wrist; the segments have the finger's length
                Vector3 wrist = sim.Flight.Wings.State[w].wrist;
                for (int k = 0; k < rt.Fingers; k++)
                {
                    Assert.Less((rt.Joint(k, 0) - wrist).magnitude, 1e-4f, "rooted at the wrist");
                    float len = 0f;
                    for (int j = 0; j < rt.Segments; j++) len += (rt.Joint(k, j + 1) - rt.Joint(k, j)).magnitude;
                    Assert.AreEqual(sp.wings[w].fingerLengths[k], len, 1e-3f, "the finger's length");
                }
            }
        }

        [Test] public void DigitWing_Validate_RejectsBadData()
        {
            Assert.Throws<AssertionException>(() => Species(skeletonOnly: false, name: "DigitWingSkinned"), "a DigitWing species without SkeletonOnly does not compose");
            var ctx = new SpeciesCtx(new CreaturePart[] { CreatureFleshWingTests.Arm(), Wing() }, new CreatureModule[] { new SkeletonOnly() }, 24, false);
            DigitWing ok = Wing();
            Assert.IsNull(ok.Validate(ctx));
            DigitWing Bad(System.Action<DigitWing> f) { var b = (DigitWing)ok.Clone(); f(b); return b; }
            Assert.IsNotNull(Bad(b => b.fingerSegments = 0).Validate(ctx));
            Assert.IsNotNull(Bad(b => b.fingerRadius = new[] { 0.01f, 0.01f }).Validate(ctx), "one radius per finger");
            Assert.IsNotNull(Bad(b => b.fingerTaper = 0f).Validate(ctx));
            Assert.IsNotNull(Bad(b => b.flexHz = 0f).Validate(ctx));
            Assert.IsNotNull(Bad(b => b.maxBendDeg = 0f).Validate(ctx));
            Assert.IsNotNull(Bad(b => b.thumbLength = 0f).Validate(ctx));
            Assert.IsNotNull(Bad(b => b.thumbSegments = 0).Validate(ctx));
            var noSkel = new SpeciesCtx(new CreaturePart[] { CreatureFleshWingTests.Arm(), Wing() }, new CreatureModule[0], 24, false);
            Assert.IsNotNull(ok.Validate(noSkel), "needs SkeletonOnly");
        }

        // One thumb: the part's thumbLength is the flight's (spec.thumbLength, what CreatureWings.ThumbTip reads), and the
        // drawn claw ends on ThumbTip - in the air and on the ground.
        [Test] public void DigitWing_Thumb_EndsOnTheContact()
        {
            CreatureSpecies sp = Species(w => { w.thumbLength = 0.11f; w.spec.thumbLength = 0.03f; }, "DigitWingThumb");
            Assert.AreEqual(0.11f, sp.wings[0].thumbLength, 1e-6f, "the part's thumbLength drives the flight's");
            Assert.AreEqual(0.11f, sp.wings[1].thumbLength, 1e-6f);
            var sim = FlowKit.Sim(sp, 1.1f);
            var bones = new Matrix4x4[sp.BoneCount];
            float worst = 0f;
            void Check()
            {
                sim.WriteBoneWorld(bones, 0);
                for (int w = 0; w < 2; w++)
                {
                    DigitWingRuntime rt = Runtime(sim, w);
                    worst = Mathf.Max(worst, (rt.ThumbJoint(rt.ThumbSegments) - sim.Flight.Wings.ThumbTip(w)).magnitude);
                    // the arc's segments: the chord from the wrist runs to the tip, and every segment has length
                    for (int j = 0; j < rt.ThumbSegments; j++) Assert.Greater((rt.ThumbJoint(j + 1) - rt.ThumbJoint(j)).magnitude, 1e-3f);
                }
            }
            for (int t = 0; t < 120; t++) { sim.Tick(kDt, Ahead(sim, 1.1f)); Check(); }
            sim.Flight.Launch(6f * 1.1f);
            for (int t = 0; t < 240; t++) { sim.Flight.SetGoals(8f * 1.1f, sp.flight.cruiseSpeed * Mathf.Sqrt(1.1f), 0f); sim.Tick(kDt, sim.Joints[0]); Check(); }
            TestContext.Out.WriteLine($"thumb claw tip vs ThumbTip: at most {worst * 1000f:F3} mm");
            Assert.Less(worst, 1e-4f);
        }

        // ------------------------------------------------------------ air: the fingers lag the beat

        // Climbing (beating all the while), wing 0's leading finger: the outer joint's bend follows the root joint's by
        // >= kLag of the beat (largest correlation), and the root joint visibly bends (range >= kVisibleDeg).
        internal const float kLag = 0.05f, kVisibleDeg = 1f;

        internal static string LagProblem(CreatureSpecies sp, out string report)
        {
            report = "";
            float s = sp.scale, T = Mathf.Sqrt(s);
            var sim = FlowKit.Sim(sp, s);
            DigitWingRuntime rt = Runtime(sim, 0);
            var bones = new Matrix4x4[sp.BoneCount];
            var root = new List<float>(); var outer = new List<float>();
            if (!sim.Flight.Launch(6f * s)) return "no launch";
            int beats0 = -1, beats1 = 0; float time = 0f;
            for (int t = 0; t < 1500; t++)
            {
                sim.Flight.SetGoals(12f * s, sp.flight.cruiseSpeed * T, 0f);
                sim.Tick(kDt, sim.Joints[0]);
                sim.WriteBoneWorld(bones, 0);
                if (t < 300 || sim.Mode != FlightMode.Air) continue;
                if (beats0 < 0) beats0 = sim.Flight.Wings.State[0].beats;
                beats1 = sim.Flight.Wings.State[0].beats; time += kDt;
                root.Add(rt.BendDeg(0, 0)); outer.Add(rt.BendDeg(0, rt.Segments - 1));
            }
            int nb = beats1 - beats0;
            if (nb < 4) return $"only {nb} beats";
            float period = time / nb;
            float range = Range(root), rangeOut = Range(outer);
            float lag = Lag(root, outer, Mathf.RoundToInt(0.5f * period / kDt)) * kDt / period;
            report = $"beat {period / T:F2} s (scale 1); root joint bend range {range:F2} deg, outer {rangeOut:F2} deg; outer lags root by {lag:F3} of the beat";
            if (range < kVisibleDeg) return $"the root joint bends only {range:F2} deg over the beat (< {kVisibleDeg}): stiff";
            if (lag < kLag) return $"the outer joint lags the root by only {lag:F3} of the beat (< {kLag})";
            return null;
        }

        static float Range(List<float> l) { float lo = float.MaxValue, hi = float.MinValue; foreach (float x in l) { lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); } return l.Count > 0 ? hi - lo : 0f; }

        // the shift (ticks, 0..maxLag, to a fraction of a tick) by which y best follows x (CreatureFleshWingTests' rule)
        static float Lag(List<float> x, List<float> y, int maxLag)
        {
            int N = Mathf.Min(x.Count, y.Count);
            double mx = 0, my = 0;
            for (int i = 0; i < N; i++) { mx += x[i]; my += y[i]; }
            mx /= N; my /= N;
            var cs = new double[maxLag + 1];
            int best = 0;
            for (int L = 0; L <= maxLag; L++)
            {
                double c = 0;
                for (int i = 0; i + L < N; i++) c += (x[i] - mx) * (y[i + L] - my);
                cs[L] = c / (N - L);
                if (cs[L] > cs[best]) best = L;
            }
            if (best == 0 || best == maxLag) return best;
            double a = cs[best - 1], b = cs[best], d = cs[best + 1], den = a - 2 * b + d;
            return den < 0 ? best + (float)(0.5 * (a - d) / den) : best;
        }

        [Test] public void DigitWing_FlyingFingersLag()
        {
            string e = LagProblem(Species(), out string report);
            TestContext.Out.WriteLine(report);
            Assert.IsNull(e, e);
        }

        // RED: flexDeg 0 - stiff fingers, nothing lags
        [Test] public void Red_DigitWing_Stiff()
        {
            string e = LagProblem(Species(w => w.flexDeg = 0f), out string report);
            TestContext.Out.WriteLine($"RED: {e} ({report})");
            Assert.IsNotNull(e);
        }

        // RED for the lag alone: a near-instant chain (flexHz 25, critically damped) still bends, but the outer joint no longer lags
        [Test] public void Red_DigitWing_NoLag()
        {
            string e = LagProblem(Species(w => { w.flexHz = 25f; w.flexDamping = 1f; }), out string report);
            TestContext.Out.WriteLine($"RED: {e} ({report})");
            Assert.IsNotNull(e);
            StringAssert.Contains("lags the root by only", e, "fails on the lag, not on stiffness");
        }

        // ------------------------------------------------------------ ground: folded, drooping, above the ground

        // A weaving 10 s wing-walk (bones every tick, as the renderer would): after 1 s, every finger joint stays >= its
        // radius above the ground (flat, y = 0); the planted wrist's thumb contact (CreatureWings.ThumbTip) drifts
        // < kWristDrift x the arm's length over each plant. meanTip: the fingertips' mean height (scale 1).
        internal const float kWristDrift = 0.01f, kDroopTips = 0.01f;

        internal static string GroundProblem(CreatureSpecies sp, out float meanTip, out float lowestClearance, out float drift)
            => GroundProblem(sp, out meanTip, out lowestClearance, out drift, out _, out _);

        // maxBend: the largest joint bend seen (deg); maxLift: the largest root lift (deg)
        internal static string GroundProblem(CreatureSpecies sp, out float meanTip, out float lowestClearance, out float drift, out float maxBend, out float maxLift)
        {
            meanTip = 0f; lowestClearance = float.MaxValue; drift = 0f; maxBend = 0f; maxLift = 0f;
            float s = sp.scale;
            var sim = FlowKit.Sim(sp, s);
            var bones = new Matrix4x4[sp.BoneCount];
            var was = new bool[sp.WingCount]; var at0 = new Vector3[sp.WingCount];
            double tipSum = 0; int tipN = 0;
            string first = null;
            for (int t = 0; t < 600; t++)
            {
                Vector3 fwd = sim.Rotations[0] * Vector3.forward;
                sim.Tick(kDt, sim.Joints[0] + Quaternion.AngleAxis(50f * Mathf.Sin(t / 50f), Vector3.up) * fwd * (2f * s));
                sim.WriteBoneWorld(bones, 0);
                if (sim.Mode != FlightMode.Ground) return $"t{t}: not on the ground ({sim.Mode})";
                if (t < 60) continue;
                for (int w = 0; w < sp.WingCount; w++)
                {
                    DigitWingRuntime rt = Runtime(sim, w);
                    for (int k = 0; k < rt.Fingers; k++)
                    {
                        maxLift = Mathf.Max(maxLift, rt.RootLiftDeg(k));
                        for (int j = 0; j < rt.Segments; j++) maxBend = Mathf.Max(maxBend, Mathf.Abs(rt.BendDeg(k, j)));
                        for (int j = 0; j <= rt.Segments; j++)
                        {
                            float c = rt.Joint(k, j).y - rt.Radius(k, j);
                            lowestClearance = Mathf.Min(lowestClearance, c / s);
                            if (c < -1e-4f * s && first == null) first = $"t{t}: wing {w} finger {k} joint {j} is {-c / s * 1000f:F1} mm (scale 1) into the ground (radius incl.)";
                        }
                        tipSum += rt.Joint(k, rt.Segments).y / s; tipN++;
                    }
                    int leg = sp.wings[w].leg;
                    LegState ls = sim.Legs[leg];
                    bool planted = !ls.stepping || ls.stepT < 0f;
                    if (!planted) { was[w] = false; continue; }
                    Vector3 q = sim.Flight.Wings.ThumbTip(w);
                    if (!was[w]) at0[w] = q;
                    was[w] = true;
                    drift = Mathf.Max(drift, (q - at0[w]).magnitude / (sp.legs[leg].TotalLength * s));
                }
            }
            meanTip = tipN > 0 ? (float)(tipSum / tipN) : 0f;
            TestContext.Out.WriteLine($"ground: mean fingertip height {meanTip * 1000f:F1} mm, lowest joint clearance {lowestClearance * 1000f:F1} mm, planted thumb drift {drift * 100f:F3} % of the arm (scale 1); largest bend {maxBend:F2} deg, root lift up to {maxLift:F1} deg");
            if (first != null) return first;
            if (drift >= kWristDrift) return $"the planted wrist's thumb contact slides {drift * 100f:F2} % of the arm's length";
            return null;
        }

        internal static string DroopProblem(CreatureSpecies sp, CreatureSpecies noDroop, out float lower)
        {
            lower = 0f;
            string e = GroundProblem(sp, out float tip, out _, out _);
            if (e != null) return e;
            string e0 = GroundProblem(noDroop, out float tip0, out _, out _);
            if (e0 != null) return "without droop: " + e0;
            lower = tip0 - tip;
            TestContext.Out.WriteLine($"droop lowers the fingertips by {lower * 1000f:F1} mm on average (scale 1)");
            return lower >= kDroopTips ? null : $"the fingertips droop only {lower * 1000f:F1} mm (< {kDroopTips * 1000f:F0} mm)";
        }

        [Test] public void DigitWing_Folded_DroopsAboveGround_WristPlanted()
        {
            string e = DroopProblem(Species(), Species(w => w.droopDeg = 0f), out _);
            Assert.IsNull(e, e);
        }

        // RED: droopDeg 0 - the folded fingers stay straight (no lower than without droop)
        [Test] public void Red_DigitWing_NoDroop()
        {
            string e = DroopProblem(Species(w => w.droopDeg = 0f), Species(w => w.droopDeg = 0f), out _);
            TestContext.Out.WriteLine("RED: " + e);
            Assert.IsNotNull(e);
        }

        // The default fold stands the fingers up along the forearm, well clear of the ground; the ground plane matters for a
        // low fold: the fingers folded only part way back (lying behind the wrist) with a strong droop. Folded so, the
        // fingers reach the ground (lowest clearance < kTouches) and stay above it.
        internal const float kTouches = 0.005f;

        internal static CreatureSpecies LowFold() => Species(w =>
        {
            w.spec.fingerFoldDeg = new[] { 95f, 100f, 105f, 110f };
            w.droopDeg = 30f;
        }, "DigitWingLowFold");

        [Test] public void DigitWing_LowFold_StaysAboveGround()
        {
            DigitWing p = Part(LowFold());
            string e = GroundProblem(LowFold(), out _, out float lowest, out _, out float maxBend, out float maxLift);
            Assert.IsNull(e, e);
            Assert.Less(lowest, kTouches, "the low fold's fingers reach the ground (else this test proves nothing)");
            Assert.LessOrEqual(maxBend, p.maxBendDeg + 1e-2f, "the ground never bends a joint past maxBendDeg (it lifts the root instead)");
            Assert.Greater(maxLift, 0f, "the root lift is exercised");
        }

        // RED: the ground plane ignored - the drooping fingers sink into the ground
        [Test] public void Red_DigitWing_IntoGround()
        {
            DigitWingRuntime.IgnoreGroundForTest = true;
            try
            {
                string e = GroundProblem(LowFold(), out _, out _, out _);
                TestContext.Out.WriteLine("RED: " + e);
                Assert.IsNotNull(e);
            }
            finally { DigitWingRuntime.IgnoreGroundForTest = false; }
        }

        // ------------------------------------------------------------ bend limits through every phase

        // A walk, a launch, a beating climb, a glide (descending), a landing (flare, touchdown) and a walk: every joint's
        // drawn bend within maxBendDeg. The modes and whether it glided are reported.
        internal static string BendProblem(CreatureSpecies sp, out float worst, out string seen)
        {
            worst = 0f; seen = "";
            float s = sp.scale, T = Mathf.Sqrt(s);
            DigitWing p = Part(sp);
            var sim = FlowKit.Sim(sp, s);
            var bones = new Matrix4x4[sp.BoneCount];
            var modes = new HashSet<string>();
            bool glided = false;
            string first = null;
            float w0 = 0f;
            void Check(string when)
            {
                sim.WriteBoneWorld(bones, 0);
                modes.Add(sim.Mode.ToString());
                glided |= sim.Mode == FlightMode.Air && sim.Flight.Model.Gliding;
                for (int w = 0; w < sp.WingCount; w++)
                {
                    DigitWingRuntime rt = Runtime(sim, w);
                    for (int k = 0; k < rt.Fingers; k++)
                        for (int j = 0; j < rt.Segments; j++)
                        {
                            float b = Mathf.Abs(rt.BendDeg(k, j));
                            if (b > w0) w0 = b;
                            if (b > p.maxBendDeg + 1e-2f && first == null) first = $"{when} ({sim.Mode}): wing {w} finger {k} joint {j} bends {b:F2} deg > {p.maxBendDeg}";
                        }
                }
            }
            for (int t = 0; t < 120; t++) { sim.Tick(kDt, Ahead(sim, s)); Check($"walk t{t}"); }
            if (!sim.Flight.Launch(6f * s)) return "no launch";
            for (int t = 0; t < 600; t++) { sim.Flight.SetGoals(10f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]); Check($"climb t{t}"); }
            for (int t = 0; t < 600; t++) { sim.Flight.SetGoals(3f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]); Check($"descend t{t}"); }
            sim.Flight.Land(sim.Flight.Model.Position + sim.Flight.Model.Forward * (8f * s) - Vector3.up * sim.Flight.Model.Altitude);
            for (int t = 0; t < 7200 && sim.Mode != FlightMode.Ground; t++) { sim.Tick(kDt, sim.Joints[0]); Check($"landing t{t}"); }
            if (sim.Mode != FlightMode.Ground) return "it never landed";
            for (int t = 0; t < 180; t++) { sim.Tick(kDt, Ahead(sim, s)); Check($"after t{t}"); }
            worst = w0;
            seen = string.Join(", ", modes) + (glided ? " + glide" : " (no glide)");
            if (!glided) return "it never glided: " + seen;
            return first;
        }

        [Test] public void DigitWing_BendLimits()
        {
            string e = BendProblem(Species(), out float worst, out string seen);
            TestContext.Out.WriteLine($"largest bend {worst:F2} deg (max {Part(Species()).maxBendDeg}); phases: {seen}");
            Assert.IsNull(e, e);
        }

        // ------------------------------------------------------------ Froude

        // The 0.9x and 1.1x individuals beating the same climb in the same Froude time (ticks of dt x sqrt k, goals x k),
        // then walking folded: the outer joint's bend curve is the same within kFroude of its range (air and ground).
        internal const float kFroude = 0.02f;

        static void BendCurve(CreatureSpecies sp, float k, List<float> air, List<float> ground)
        {
            float s = sp.scale * k, T = Mathf.Sqrt(s), dt = kDt * Mathf.Sqrt(k);
            var sim = FlowKit.Sim(sp, s);
            DigitWingRuntime rt = Runtime(sim, 0);
            var bones = new Matrix4x4[sp.BoneCount];
            for (int t = 0; t < 240; t++)
            {
                sim.Tick(dt, sim.Joints[0] + sim.Rotations[0] * Vector3.forward * (2f * s));
                sim.WriteBoneWorld(bones, 0);
                ground.Add(rt.BendDeg(0, rt.Segments - 1));
            }
            sim.Flight.Launch(6f * s);
            for (int t = 0; t < 900; t++)
            {
                sim.Flight.SetGoals(12f * s, sp.flight.cruiseSpeed * T, 0f);
                sim.Tick(dt, sim.Joints[0]);
                sim.WriteBoneWorld(bones, 0);
                if (t >= 300) air.Add(rt.BendDeg(0, rt.Segments - 1));
            }
        }

        internal static string FroudeProblem(CreatureSpecies sp, out float worst)
        {
            worst = 0f;
            var aa = new List<float>(); var ga = new List<float>(); var ab = new List<float>(); var gb = new List<float>();
            BendCurve(sp, 0.9f, aa, ga); BendCurve(sp, 1.1f, ab, gb);
            float rA = Mathf.Max(1e-3f, Range(aa)), rG = Mathf.Max(1e-3f, Range(ga));
            float wa = 0f, wg = 0f;
            for (int i = 0; i < aa.Count && i < ab.Count; i++) wa = Mathf.Max(wa, Mathf.Abs(aa[i] - ab[i]) / rA);
            for (int i = 0; i < ga.Count && i < gb.Count; i++) wg = Mathf.Max(wg, Mathf.Abs(ga[i] - gb[i]) / rG);
            worst = Mathf.Max(wa, wg);
            TestContext.Out.WriteLine($"0.9x vs 1.1x: outer bend curves differ by at most {wa * 100f:F2} % (air, range {rA:F2} deg), {wg * 100f:F2} % (ground, range {rG:F2} deg)");
            return worst <= kFroude ? null : $"the bend curves of the 0.9x and 1.1x individuals differ by {worst * 100f:F2} % of their range in Froude time";
        }

        [Test] public void DigitWing_Froude()
        {
            string e = FroudeProblem(Species(), out _);
            Assert.IsNull(e, e);
        }

        // RED: the chains in plain seconds (no 1/sqrt size on their frequencies) - the two sizes part
        [Test] public void Red_DigitWing_NotFroude()
        {
            DigitWingRuntime.IgnoreSizeForTest = true;
            try
            {
                string e = FroudeProblem(Species(), out _);
                TestContext.Out.WriteLine("RED: " + e);
                Assert.IsNotNull(e);
            }
            finally { DigitWingRuntime.IgnoreSizeForTest = false; }
        }

        // ------------------------------------------------------------ no GC, F3

        sealed class Recorder : ICreatureDebugDraw
        {
            public int lines, points;
            public readonly List<Vector3> at = new List<Vector3>();
            public readonly List<float> radii = new List<float>();
            public bool record;
            public void Line(Vector3 a, Vector3 b, Color c) => lines++;
            public void Point(Vector3 p, float r, Color c) { points++; if (record) { at.Add(p); radii.Add(r); } }
        }

        [Test] public void DigitWing_NoGC()
        {
            CreatureSpecies sp = Species();
            float s = sp.scale, T = Mathf.Sqrt(s);
            var sim = FlowKit.Sim(sp, s);
            var bones = new Matrix4x4[sp.BoneCount];
            var rec = new Recorder();
            Vector3 lo = Vector3.zero, hi = Vector3.zero;
            void Frame()
            {
                sim.WriteBoneWorld(bones, 0);
                foreach (IPartRuntime r in sim.PartRuntimes) { r.DrawDebug(rec); r.EncapsulateBounds(ref lo, ref hi); }
            }
            for (int t = 0; t < 120; t++) { sim.Tick(kDt, Ahead(sim, s)); Frame(); }
            sim.Flight.Launch(6f * s);
            for (int t = 0; t < 300; t++) { sim.Flight.SetGoals(8f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]); Frame(); }
            GcAllocs.AssertCounterWorks();
            int allocs = GcAllocs.Count(() =>
            {
                for (int t = 0; t < 120; t++) { sim.Flight.DiveFold = t < 60 ? 0.9f : 0f; sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]); Frame(); }
            });
            Assert.AreEqual(0, allocs, "air");
            // the ground (droop, the ground plane, root lifts): a low-fold individual walking
            CreatureSpecies low = LowFold();
            var gs = FlowKit.Sim(low, s);
            var gb = new Matrix4x4[low.BoneCount];
            for (int t = 0; t < 60; t++) { gs.Tick(kDt, Ahead(gs, s)); gs.WriteBoneWorld(gb, 0); }
            int groundAllocs = GcAllocs.Count(() =>
            {
                for (int t = 0; t < 60; t++)
                {
                    gs.Tick(kDt, Ahead(gs, s)); gs.WriteBoneWorld(gb, 0);
                    foreach (IPartRuntime r in gs.PartRuntimes) { r.DrawDebug(rec); r.EncapsulateBounds(ref lo, ref hi); }
                }
            });
            Assert.AreEqual(FlightMode.Ground, gs.Mode);
            Assert.AreEqual(0, groundAllocs, "ground");
        }

        [Test] public void DigitWing_DrawDebug_DrawsEveryJoint()
        {
            CreatureSpecies sp = Species();
            Rendered(sp, 1.2f, 60, out CreatureSim sim);   // posed by the skeleton-only renderer, never by hand
            DigitWingRuntime rt = Runtime(sim, 0);
            Assert.IsTrue(rt.Posed, "the renderer posed the skeleton");
            var rec = new Recorder { record = true };
            rt.DrawDebug(rec);
            int segs = rt.Fingers * rt.Segments + rt.ThumbSegments;
            TestContext.Out.WriteLine($"{rec.lines} lines, {rec.points} points for {rt.Fingers} x {rt.Segments} finger segments + {rt.ThumbSegments} thumb");
            Assert.GreaterOrEqual(rec.lines, segs, "a line per finger and thumb segment");
            Assert.GreaterOrEqual(rec.points, rt.Fingers * (rt.Segments + 1), "a circle per finger joint");
            // every finger joint drawn at its place and radius
            for (int k = 0; k < rt.Fingers; k++)
                for (int j = 0; j <= rt.Segments; j++)
                {
                    bool hit = false;
                    for (int i = 0; i < rec.at.Count && !hit; i++)
                        hit = (rec.at[i] - rt.Joint(k, j)).magnitude < 1e-5f && Mathf.Abs(rec.radii[i] - rt.Radius(k, j)) < 1e-6f;
                    Assert.IsTrue(hit, $"finger {k} joint {j} drawn at its radius");
                }
            // the thumb claw is drawn, pointing forward of the wrist (in the air: along the leading side)
            Assert.Greater((rt.ThumbJoint(rt.ThumbSegments) - rt.ThumbJoint(0)).magnitude, 0.5f * Part(sp).thumbLength * 1.2f, "the thumb has its length");
        }

        // The game's path: a skeleton-only individual in its CreatureRenderer, ticked and rendered (no draw) - its bones are
        // posed every frame, so the fingers exist for F3 and their dynamics run (the droop targets come from the pose: the
        // folded fingers droop). Nothing is submitted for drawing.
        [Test] public void DigitWing_SkeletonOnlyRenderer_PosesTheFingers()
        {
            CreatureSpecies sp = Species();
            CreatureRenderer r = Rendered(sp, 1f, 240, out CreatureSim sim);
            Assert.IsNull(r.Mesh);
            Assert.AreEqual(0, r.SubmittedForTest, "nothing drawn");
            Assert.AreEqual(240, r.SkeletonPosedForTest, "posed every frame");
            var rec = new Recorder();
            foreach (IPartRuntime rt in sim.PartRuntimes) rt.DrawDebug(rec);
            Assert.Greater(rec.lines, 0, "F3 gets finger lines");
            DigitWingRuntime d = Runtime(sim, 0);
            Assert.IsTrue(d.Posed);
            float droop = 0f;
            for (int k = 0; k < d.Fingers; k++) for (int j = 0; j < d.Segments; j++) droop = Mathf.Max(droop, Mathf.Abs(d.BendDeg(k, j)));
            TestContext.Out.WriteLine($"{rec.lines} lines; largest folded bend {droop:F2} deg");
            Assert.Greater(droop, 0.5f, "the folded fingers droop (the dynamics see the pose)");
        }

        // RED: a runtime that draws nothing fails the F3 count
        [Test] public void Red_DigitWing_DrawsNothing()
        {
            var rec = new Recorder();
            CreatureSpecies sp = Species();
            var sim = FlowKit.Sim(sp, 1f);   // never posed: nothing to draw
            Runtime(sim, 0).DrawDebug(rec);
            Assert.Less(rec.lines, 4 * Part(sp).fingerSegments + Part(sp).thumbSegments, "RED: an unposed runtime draws nothing");
        }
    }
}
