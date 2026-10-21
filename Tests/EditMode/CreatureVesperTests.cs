using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // Vesper (Vesper step 1, the skeleton only; Config/Creatures/Vesper.asset from Tools~/SpeciesGen/Vesper.py): the Apex's
    // animal built skeleton-first - one FlowBody chain from the head's tip to the tail's tip (the head its first four joints,
    // stiff but jointed: bend limits <= 6 deg, no rigid block), Leg2Bone wing arms carrying DigitWing wings (4 finger chains +
    // a thumb claw), DrakeLeg hind legs, Eyes, SkeletonOnly. Invariants: it composes so; it walks 20 s on its folded wings
    // and hind legs - every wing wrist and hind foot steps and plants (the planted wrist's thumb contact still and on the
    // ground) and the kit's Gait check is clean; from the ground under its own FlightBrain it launches, beats, glides,
    // flares, touches down and wing-walks again within 120 s x sqrt s; in steady flight the girdle bobs >= 6 % of the body
    // length with each beat, the head <= 1/3 of it; folded on the ground, every finger tip stays on its own side of the
    // spine, the two wings' tips >= the chest's half width apart. No pops (task D): in the air the head's largest step in a
    // tick <= 3 x the girdle's + 0.02 L (headStabiliseBounded), through a rock perch or a landing neither the girdle nor the
    // head - nor any joint (drapeOwnHeading) - moves more than 0.05 L in a tick (touchdownFromHover), perched on a rock
    // it stays on top (perchFrontLevel), flown low past a rock's edge the girdle is not snapped up (floorLiftEased), in VesperLab's aviary (VesperLabLayout.AviaryFactor x the
    // lab's radius) the head does not swing at the wall (trunkTurnBounded; the cure containTurns - on the wall <= 2 % of the
    // air time, no sideways flight - is measured on a variant, not yet on in Vesper.asset), each with a
    // RED (flag off). The flight tests run several seeds and judge the property on each (task F: long chaotic runs take
    // other paths under Mono than under .NET), and the REDs assert their own measure (the step against its bound, the
    // sideways time, the reversals) over the seeds rather than one run's first failure message. The REDs: a supple head; wing arms carried folded and a
    // wrist planted off its thumb (the walk); folded fingers that cross over the back. The Apex's flat-bob RED covers the
    // bob thresholds (the same), CreatureApexTests the fly-and-land problem.
    public class CreatureVesperTests
    {
        const float kDt = 1f / 60f;
        internal const int kHeadJoints = 4;
        internal const float kHeadBendMax = 6f;

        // the asset (its runtime view)
        internal static CreatureSpecies Vesper() => CreatureKitAssets.Load("Vesper");

        // A copy of the asset's data, edited, compiled afresh (the asset itself is untouched).
        internal static CreatureSpecies Vesper(System.Action<List<CreaturePart>> parts, System.Action<FlightSpec> fly = null)
        {
            CreatureSpecies a = CreatureKitAssets.LoadAsset("Vesper");
            var ps = new List<CreaturePart>();
            foreach (CreaturePart p in a.parts) ps.Add(p.Clone());
            var ms = new List<CreatureModule>();
            foreach (CreatureModule m in a.modules)
            {
                if (m is Fly f)
                {
                    var c = new Fly { spec = CopyOf(f.spec), twist = f.twist };
                    fly?.Invoke(c.spec);
                    ms.Add(c);
                }
                else ms.Add(m.Clone());
            }
            parts?.Invoke(ps);
            return FlowKit.Build("VesperVariant", a.scale, ps, ms);
        }

        static FlightSpec CopyOf(FlightSpec s)
        {
            var c = new FlightSpec();
            foreach (FieldInfo f in typeof(FlightSpec).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) f.SetValue(c, f.GetValue(s));
            return c;
        }

        static int Count<T>(CreatureSpecies sp)
        {
            int n = 0;
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is T) n++;
            return n;
        }

        static List<T> All<T>(CreatureSpecies sp) where T : CreaturePart
        {
            var o = new List<T>();
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is T t) o.Add(t);
            return o;
        }

        // ------------------------------------------------------------ composition

        [Test] public void Vesper_Composes()
        {
            CreatureSpecies sp = Vesper();
            Assert.IsNotNull(sp, "Config/Creatures/Vesper.asset");
            Assert.IsNull(sp.Validate());
            Assert.IsTrue(sp.skeletonOnly, "SkeletonOnly: no skin in step 1");
            Assert.IsTrue(sp.CanFly);
            Assert.AreEqual(1, Count<FlowBody>(sp), "one chain, head tip to tail tip");
            Assert.AreEqual(2, Count<DigitWing>(sp)); Assert.AreEqual(2, Count<DrakeLeg>(sp)); Assert.AreEqual(1, Count<Eyes>(sp));
            Assert.AreEqual(0, Count<FleshWing>(sp) + Count<Wing>(sp) + Count<PteroWing>(sp), "the DigitWing is its only wing");
            Assert.AreEqual(0, Count<CrestBeakHead>(sp) + Count<LongNeck>(sp) + Count<SpineBody>(sp), "the head and neck are the chain's");
            // the wing arms: two Leg2Bone (the DigitWings' arms), short upper arm, very long forearm
            List<Leg2Bone> arms = All<Leg2Bone>(sp);
            Assert.AreEqual(2, arms.Count, "two wing-arm Leg2Bone");
            int shoulder = arms[0].at.joint;
            foreach (Leg2Bone a in arms)
            {
                Assert.AreEqual(shoulder, a.at.joint, $"{a.id} on the shoulder joint");
                Assert.AreEqual(0.4f, a.upperLength / a.lowerLength, 0.05f, $"{a.id}: upper arm ~0.4 x forearm");
            }
            foreach (DigitWing w in All<DigitWing>(sp))
            {
                Assert.AreEqual(4, w.spec.FingerCount, $"{w.id}: four fingers");
                Assert.GreaterOrEqual(w.fingerSegments, 2, $"{w.id}: jointed finger chains");
                Assert.GreaterOrEqual(w.thumbSegments, 1, $"{w.id}: a thumb claw");
                Assert.Greater(w.thumbLength, 0f);
                StringAssert.StartsWith("arm", w.arm, $"{w.id} rides a wing arm");
            }
            Assert.AreEqual(2, sp.WingCount);
            Assert.Greater(shoulder, kHeadJoints, "the wing arms hang behind the head and neck");
            for (int w = 0; w < sp.WingCount; w++) Assert.AreEqual(shoulder, sp.legs[sp.wings[w].leg].attachJoint, $"wing {w}'s arm leg");
            foreach (DrakeLeg h in All<DrakeLeg>(sp)) Assert.Greater(h.at.joint, shoulder, $"{h.id} on the hips, behind the shoulder");
            // the head: the first joints of the chain, stiff but jointed (not the rigid block)
            FlowBody body = All<FlowBody>(sp)[0];
            Assert.AreEqual(0, body.RigidHeadJoints, "no rigid head block");
            Assert.AreEqual(0, sp.RigidHead);
            string head = HeadBendProblem(sp);
            Assert.IsNull(head, head);
            // the authored joint radii (the future skin): every profile key's and every finger's
            for (int i = 0; i < body.profile.Length; i++)
            {
                Assert.Greater(body.profile[i].halfWidth, 0f, $"profile[{i}].halfWidth");
                Assert.Greater(body.profile[i].halfHeight, 0f, $"profile[{i}].halfHeight");
            }
            foreach (DigitWing w in All<DigitWing>(sp))
            {
                Assert.AreEqual(w.spec.FingerCount, w.fingerRadius.Length, $"{w.id}: a radius per finger");
                foreach (float r in w.fingerRadius) Assert.Greater(r, 0f, $"{w.id}: finger radius");
            }
            Assert.Greater(sp.joints[kHeadJoints + 2].bendLimitDeg, kHeadBendMax, "the neck behind the head is supple");
            // the skeleton's layout is the kit's (bones only: skeleton-only waives the mesh)
            Assert.IsNull(KitChecks.LayoutProblem(sp));
            TestContext.Out.WriteLine($"{sp.JointCount} joints, body {body.BodyLength * sp.scale:F2} m at scale {sp.scale}, {sp.BoneCount} bones; head bend limits {sp.joints[0].bendLimitDeg:F1} .. {sp.joints[kHeadJoints - 1].bendLimitDeg:F1} deg");
        }

        // The head: joints 0 .. kHeadJoints - 1 bend at most kHeadBendMax (stiff but jointed). null = fine, else the first
        // joint past it.
        internal static string HeadBendProblem(CreatureSpecies sp)
        {
            for (int i = 0; i < kHeadJoints && i < sp.JointCount; i++)
                if (sp.joints[i].bendLimitDeg > kHeadBendMax + 1e-4f) return $"head joint {i}'s bend limit {sp.joints[i].bendLimitDeg:F1} deg > {kHeadBendMax}";
            return null;
        }

        // RED: a variant whose head joints bend like the neck (14 deg) - the head clause fires and names the joint
        [Test] public void Red_Vesper_SuppleHead()
        {
            CreatureSpecies sp = Vesper(ps =>
            {
                foreach (CreaturePart p in ps)
                    if (p is FlowBody b) { b.profile[0].bendLimitDeg = 14f; b.profile[1].bendLimitDeg = 14f; }
            });
            string e = HeadBendProblem(sp);
            TestContext.Out.WriteLine("RED: " + e);
            Assert.IsNotNull(e);
            StringAssert.Contains("head joint 0", e);
        }

        // ------------------------------------------------------------ walking on the folded wings

        // A weaving walk of 20 s x sqrt s (bones every tick, as the renderer would): on the ground throughout; after the
        // first second every wing wrist and every hind foot steps >= kMinSteps times and plants; while an arm is planted its
        // thumb contact (CreatureWings.ThumbTip: the walked point) drifts < kWristDrift x the arm's length and stays within
        // kOnGround x s of the ground; a planted hind foot is on the ground too.
        internal const int kMinSteps = 4;
        internal const float kWristDrift = 0.01f, kOnGround = 0.03f;

        internal static string WalkProblem(CreatureSpecies sp, out string report)
        {
            report = "";
            float s = sp.scale, T = Mathf.Sqrt(s);
            var sim = FlowKit.Sim(sp, s);
            var bones = new Matrix4x4[sp.BoneCount];
            int n = sp.legs.Length;
            var steps = new int[n]; var was = new bool[n];
            var armOf = new int[n];
            for (int l = 0; l < n; l++) armOf[l] = -1;
            for (int w = 0; w < sp.WingCount; w++) armOf[sp.wings[w].leg] = w;
            var plantedAt = new Vector3[n]; var wasPlanted = new bool[n];
            float drift = 0f, highest = 0f;
            int ticks = Mathf.RoundToInt(20f * T / kDt), warm = Mathf.RoundToInt(1f * T / kDt);
            for (int t = 0; t < ticks; t++)
            {
                Vector3 fwd = sim.Rotations[0] * Vector3.forward;
                sim.Tick(kDt, sim.Joints[0] + Quaternion.AngleAxis(50f * Mathf.Sin(t / (60f * T)), Vector3.up) * fwd * (2f * s));
                sim.WriteBoneWorld(bones, 0);
                if (sim.Mode != FlightMode.Ground) return $"t={sim.Time:F1} s: not on the ground ({sim.Mode})";
                if (t < warm) continue;
                for (int l = 0; l < n; l++)
                {
                    LegState ls = sim.Legs[l];
                    if (ls.stepping && !was[l]) steps[l]++;
                    was[l] = ls.stepping;
                    bool planted = !sp.legs[l].feeler && (!ls.stepping || ls.stepT < 0f);   // a feeler (a carried arm) never plants
                    if (!planted) { wasPlanted[l] = false; continue; }
                    // the contact: the arm's walked thumb tip, the hind leg's foot
                    Vector3 q = armOf[l] >= 0 ? sim.Flight.Wings.ThumbTip(armOf[l]) : ls.foot;
                    highest = Mathf.Max(highest, Mathf.Abs(q.y) / s);
                    if (Mathf.Abs(q.y) > kOnGround * s) return $"t={sim.Time:F1} s: planted {(armOf[l] >= 0 ? "wing wrist" : "hind foot")} {l} is {q.y / s * 1000f:F1} mm (scale 1) off the ground";
                    if (armOf[l] < 0) continue;
                    if (!wasPlanted[l]) plantedAt[l] = q;
                    wasPlanted[l] = true;
                    drift = Mathf.Max(drift, (q - plantedAt[l]).magnitude / (sp.legs[l].TotalLength * s));
                }
            }
            report = $"steps per leg {string.Join(", ", steps)} (arms: {string.Join(", ", System.Array.ConvertAll(sp.wings, w => w.leg))}); planted wrist drift {drift * 100f:F3} % of the arm; highest planted contact {highest * 1000f:F1} mm (scale 1)";
            for (int l = 0; l < n; l++)
                if (steps[l] < kMinSteps) return $"{(armOf[l] >= 0 ? "wing wrist" : "hind foot")} {l} stepped {steps[l]} times in 20 s x sqrt s (< {kMinSteps})";
            if (drift >= kWristDrift) return $"a planted wing wrist's thumb contact slides {drift * 100f:F2} % of the arm's length";
            return KitChecks.GaitProblem(sp);
        }

        [Test] public void Vesper_WalksOnFoldedWings()
        {
            string e = WalkProblem(Vesper(), out string report);
            TestContext.Out.WriteLine(report);
            Assert.IsNull(e, e);
        }

        // RED: wing arms carried folded (LimbRole.Folded: posed, never planted - a biped on its hind legs) - the wrists
        // never step or plant
        [Test] public void Red_Vesper_WingsCarried()
        {
            CreatureSpecies sp = Vesper(ps => { foreach (CreaturePart p in ps) if (p is Leg2Bone a) a.role = LimbRole.Folded; });
            string e = WalkProblem(sp, out string report);
            TestContext.Out.WriteLine($"RED: {e} ({report})");
            Assert.IsNotNull(e);
            StringAssert.Contains("wing wrist", e);
        }

        // RED: the wrist planted on itself, not on the thumb claw (thumbPlant off) - the claw tip, the walked contact the
        // test measures, rides above the ground
        [Test] public void Red_Vesper_NotOnTheThumb()
        {
            CreatureSpecies sp = Vesper(ps => { foreach (CreaturePart p in ps) if (p is DigitWing w) w.spec.thumbPlant = false; });
            string e = WalkProblem(sp, out string report);
            TestContext.Out.WriteLine($"RED: {e} ({report})");
            Assert.IsNotNull(e);
            StringAssert.Contains("slides", e);
        }

        // ------------------------------------------------------------ the folded wings stay apart

        // Standing 2 s x sqrt s, then a weaving walk of 8 s x sqrt s; from 1 s x sqrt s on, every tick (bones written): each
        // wing's finger tips (the last joint of every finger chain) lie on that wing's own side of the spine (lateral = the
        // chest's right, from the shoulder joint), and the left-to-right tip gap (the right wing's leftmost tip less the left
        // wing's rightmost, across the chest) is >= kFoldGap x s: the folded fingers never cross over the back.
        internal const float kFoldGap = 0.12f;   // m at scale 1: the chest's half width

        internal static string FoldProblem(CreatureSpecies sp, out float minOwn, out float minGap)
        {
            minOwn = float.MaxValue; minGap = float.MaxValue;
            float s = sp.scale, T = Mathf.Sqrt(s);
            var sim = FlowKit.Sim(sp, s);
            var bones = new Matrix4x4[sp.BoneCount];
            var rts = new List<DigitWingRuntime>();
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is DigitWingRuntime d) rts.Add(d);
            if (rts.Count != 2) return $"{rts.Count} digit wings";
            int shoulder = sp.legs[sp.wings[0].leg].attachJoint;
            int stand = Mathf.RoundToInt(2f * T / kDt), ticks = stand + Mathf.RoundToInt(8f * T / kDt), warm = Mathf.RoundToInt(1f * T / kDt);
            string first = null;
            for (int t = 0; t < ticks; t++)
            {
                Vector3 fwd = sim.Rotations[0] * Vector3.forward;
                Vector3 target = t < stand ? sim.Joints[0] : sim.Joints[0] + Quaternion.AngleAxis(50f * Mathf.Sin(t / (60f * T)), Vector3.up) * fwd * (2f * s);
                sim.Tick(kDt, target);
                sim.WriteBoneWorld(bones, 0);
                if (t < warm) continue;
                string when = t < stand ? "standing" : "walking";
                Vector3 c = sim.Joints[shoulder], right = sim.Rotations[shoulder] * Vector3.right;
                float leftMost = float.MinValue, rightLeast = float.MaxValue;   // the left wing's rightmost tip, the right wing's leftmost
                foreach (DigitWingRuntime rt in rts)
                {
                    int side = sim.Flight.Wings.Side(rt.Wing);
                    for (int k = 0; k < rt.Fingers; k++)
                    {
                        float x = Vector3.Dot(rt.Joint(k, rt.Segments) - c, right);
                        float own = x * side;
                        minOwn = Mathf.Min(minOwn, own / s);
                        if (own < 0f && first == null) first = $"t={sim.Time:F1} s ({when}): wing {rt.Wing} (side {side}) finger {k}'s tip is {-own / s * 1000f:F0} mm (scale 1) across the spine";
                        if (side < 0) leftMost = Mathf.Max(leftMost, x); else rightLeast = Mathf.Min(rightLeast, x);
                    }
                }
                float gap = rightLeast - leftMost;
                minGap = Mathf.Min(minGap, gap / s);
                if (gap < kFoldGap * s && first == null) first = $"t={sim.Time:F1} s ({when}): the folded tips are only {gap / s * 1000f:F0} mm (scale 1) apart across the chest (< {kFoldGap * 1000f:F0} mm)";
            }
            TestContext.Out.WriteLine($"folded finger tips: least on its own side {minOwn * 1000f:F0} mm, least left-right gap {minGap * 1000f:F0} mm (scale 1; limit {kFoldGap * 1000f:F0} mm)");
            return first;
        }

        [Test] public void Vesper_FoldedWingsStaySeparate()
        {
            string e = FoldProblem(Vesper(), out _, out _);
            Assert.IsNull(e, e);
        }

        // RED: the folded fingers barely lifted off the forearm (groundLiftDeg 20) - they lie back over the body and cross
        [Test] public void Red_Vesper_FoldedWingsCross()
        {
            string e = FoldProblem(Vesper(ps => { foreach (CreaturePart p in ps) if (p is DigitWing w) w.spec.groundLiftDeg = 20f; }), out _, out _);
            TestContext.Out.WriteLine("RED: " + e);
            Assert.IsNotNull(e);
        }

        // ------------------------------------------------------------ flies and lands under its own brain

        // From the ground in its aviary under its own FlightBrain (FlightExcursion; the command brain is task 4), within
        // 120 s x sqrt s: wing-walk, launch, beat, glide, flare, touchdown and the wing-walk again
        // (CreatureApexTests.FlyAndLandProblem).
        [Test] public void Vesper_FliesAndLands()
        {
            CreatureSpecies sp = Vesper();
            string e = CreatureApexTests.FlyAndLandProblem(sp, 1, out string seen, out float at);
            TestContext.Out.WriteLine($"seed 1: {seen}; walking again at t = {at:F1} s");
            Assert.IsNull(e, e);
        }

        // ------------------------------------------------------------ the bob

        // Steady cruise (CreatureApexTests.MeasureBob): the girdle bobs >= 6 % of the body length per beat (peak to peak
        // about the running mean), the head <= 1/3 of the girdle's. The travelling wave down the tail is reported.
        internal static string BobProblem(CreatureSpecies sp, out CreatureApexTests.Bob b)
        {
            b = CreatureApexTests.MeasureBob(sp);
            float L = All<FlowBody>(sp)[0].BodyLength * sp.scale;
            TestContext.Out.WriteLine($"cruise after {b.climbSeconds:F1} s of climb: girdle bob {b.girdle:F3} m p-p = {b.girdle / L * 100f:F1} % of the body ({L:F2} m); head {b.head:F3} m ({b.head / Mathf.Max(1e-6f, b.girdle):F2} x girdle); joint {CreatureApexTests.kMidTail} {b.tail:F3} m lagging {b.lagBeats:F2} beat (steps girdle -> {string.Join(" -> ", CreatureApexTests.kWave)}: {string.Join(", ", System.Array.ConvertAll(b.steps, x => x.ToString("F2")))}); period {b.period:F2} s, {b.beats:F1} beats");
            if (b.climbSeconds < 0f) return "it never held its cruise altitude (60 s x sqrt s)";
            if (b.beats < 5f) return $"only {b.beats:F1} beats measured";
            if (b.girdle < CreatureApexTests.kBob * L) return $"the girdle bobs {b.girdle:F3} m p-p, < {CreatureApexTests.kBob} x the body length ({CreatureApexTests.kBob * L:F3} m)";
            if (b.head > CreatureApexTests.kHeadShare * b.girdle) return $"the head bobs {b.head:F3} m p-p, > 1/3 of the girdle's {b.girdle:F3} m";
            return null;
        }

        [Test] public void Vesper_Bob()
        {
            string e = BobProblem(Vesper(), out _);
            Assert.IsNull(e, e);
        }

        // ------------------------------------------------------------ no pops (task D)

        // In the air under its own FlightBrain (seeds 1-4, 120 s x sqrt s each; ticks that start and end in Air): the
        // head's largest step in one tick <= 3 x the girdle's largest + 0.02 L, and the head does not chatter - at most one
        // reversal (a tick's step turned > kChatterDeg from the last, both > kChatterStep L) per kChatterEvery s x sqrt s in
        // the air. The neck swings smoothly with the beat and the turns (measured 0.3-0.4 m against the girdle's 0.13 m;
        // over 48 seeds offline at most 0.59 of the bound); a pop is the neck flipping. RED (FlightSpec.headStabiliseBounded
        // off): the long head stabiliser trailed the turns by metres (the target 14 m behind the 4.4 m neck) and the
        // containment's cut of the outward speed swung the trunk up to 62 deg in a tick - the head jumped 6-7 m. RED
        // (AirChain.NoAimAheadForTest: headStabiliseBounded's aim kept ahead off): a head target swept past the head (inside
        // the neck's reach) left the bend cones to pick a side from a backward aim, and the neck's front flipped side to side
        // tick by tick - 1-10 reversals per seed over seeds 1-8 (0-1 with it), peaks up to 1.4 x the bound over 48 seeds
        // (in Unity 0.66 m > the bound on seed 1).
        const float kHeadJumpGirdles = 3f, kHeadJumpSlack = 0.02f;
        const float kChatterDeg = 120f, kChatterStep = 0.008f, kChatterEvery = 20f;
        internal struct HeadRun { public float air, head, headAt, girdle, bound; public int reversals, allowed; }

        internal static HeadRun MeasureHead(CreatureSpecies sp, int seed, float seconds = 120f)
        {
            EnclosureArea area = KitChecks.LabArea(sp);
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, area.center, Vector3.forward, sp.scale, seed) { Bounds = area };
            var brain = new FlightBrain(area, seed);
            float L = All<FlowBody>(sp)[0].BodyLength * sp.scale, T = Mathf.Sqrt(sp.scale);
            int n = Mathf.CeilToInt(seconds * T / kDt), g = sim.Flight.Girdle, airTicks = 0;
            var r = new HeadRun();
            Vector3 last = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 h0 = sim.Joints[0], g0 = sim.Joints[g];
                FlightMode m0 = sim.Mode;
                sim.Tick(kDt, brain.Target(sim, kDt));
                if (m0 != FlightMode.Air || sim.Mode != FlightMode.Air) { last = Vector3.zero; continue; }
                airTicks++;
                Vector3 step = sim.Joints[0] - h0;
                float jh = step.magnitude;
                if (jh > r.head) { r.head = jh; r.headAt = sim.Time; }
                r.girdle = Mathf.Max(r.girdle, (sim.Joints[g] - g0).magnitude);
                if (last != Vector3.zero && Mathf.Min(jh, last.magnitude) > kChatterStep * L && Vector3.Angle(step, last) > kChatterDeg) r.reversals++;
                last = step;
            }
            r.air = airTicks * kDt;
            r.bound = kHeadJumpGirdles * r.girdle + kHeadJumpSlack * L;
            r.allowed = Mathf.FloorToInt(r.air / T / kChatterEvery);
            TestContext.Out.WriteLine($"seed {seed}: {r.air:F0} s in the air; largest step in a tick: head {r.head:F3} m (t = {r.headAt:F2} s), girdle {r.girdle:F3} m; bound {r.bound:F3} m ({r.head / Mathf.Max(1e-6f, r.bound):F2} of it); head reversals {r.reversals} (at most {r.allowed})");
            return r;
        }

        internal static string HeadJumpProblem(CreatureSpecies sp, int seed)
        {
            HeadRun r = MeasureHead(sp, seed);
            if (r.air < 10f * Mathf.Sqrt(sp.scale)) return $"seed {seed}: only {r.air:F1} s in the air";
            if (r.head > r.bound) return $"seed {seed}: the head jumped {r.head:F3} m in one tick at t = {r.headAt:F2} s (> {kHeadJumpGirdles} x the girdle's {r.girdle:F3} m + {kHeadJumpSlack} L = {r.bound:F3} m)";
            if (r.reversals > r.allowed) return $"seed {seed}: the head reversed {r.reversals} times in {r.air:F0} s in the air (> {r.allowed}: it chatters)";
            return null;
        }

        [Test] public void Vesper_Flight_NoHeadJump()
        {
            CreatureSpecies sp = Vesper();
            Assert.IsTrue(sp.flight.headStabiliseBounded, "Vesper.asset: headStabiliseBounded");
            for (int seed = 1; seed <= 4; seed++)
            {
                string e = HeadJumpProblem(sp, seed);
                Assert.IsNull(e, e);
            }
        }

        // RED: headStabiliseBounded off - over seeds 1 and 2 the head's largest step crosses its bound
        [Test] public void Red_Vesper_HeadJump()
        {
            CreatureSpecies sp = Vesper(null, f => f.headStabiliseBounded = false);
            float worst = 0f;
            for (int seed = 1; seed <= 2; seed++) { HeadRun r = MeasureHead(sp, seed); worst = Mathf.Max(worst, r.head / r.bound); }
            TestContext.Out.WriteLine($"RED: the head's largest step {worst:F2} x its bound");
            Assert.Greater(worst, 1f, "the head's largest step against its bound (3 x the girdle's + 0.02 L)");
        }

        // RED: the aim kept ahead off (AirChain.NoAimAheadForTest) - over seeds 1-4 the head reverses (chatters) more than
        // it is allowed
        [Test] public void Red_Vesper_HeadChatter()
        {
            CreatureSpecies sp = Vesper();
            int reversals = 0, allowed = 0;
            AirChain.NoAimAheadForTest = true;
            try
            {
                for (int seed = 1; seed <= 4; seed++) { HeadRun r = MeasureHead(sp, seed); reversals += r.reversals; allowed += r.allowed; }
            }
            finally { AirChain.NoAimAheadForTest = false; }
            TestContext.Out.WriteLine($"RED: {reversals} head reversals over seeds 1-4 (allowed {allowed})");
            Assert.Greater(reversals, allowed, "head reversals (chatter) over seeds 1-4");
        }

        // Touchdowns under the command brain (CreatureCommandBrainTests.Rig; a 4.8 m rock): onto the rock from the air
        // (seeds 1-4) and from the ground (seeds 1-3), and onto open ground from the air (seeds 1-2). From the tick it turns
        // Touchdown until kTouchAfter s x sqrt s after it is walking (standing) again, neither the girdle nor the head - nor
        // any joint of the chain (task E: the draped tail) - moves more than 0.05 L in one tick (the command brain's snap
        // rule). A perch run that does not end on the rock must have given up as documented (CommandBrain: "could not reach
        // the perch: on open ground" - perching is CreatureCommandBrainTests' property, not this one's; such a run is not
        // judged), and at least kTouchPerched of the rock runs must perch. RED (FlightSpec.touchdownFromHover off): a
        // touchdown begun on a tick's last sub-step showed the walking restart's body for one tick (laid along the rock with
        // the girdle on its point; at the stance) and the eased blend carried it back the next - the girdle jumped 2.9 m
        // (from the air) and 1.6-2.8 m (from the ground) onto the rock. RED (FlightSpec.drapeOwnHeading off): the tail
        // hanging over the rock's edge was laid in the vertical plane of the one segment before the edge, taken afresh every
        // sub-step; when the edge moved to another joint or that segment's heading flipped, the whole hanging run swung about
        // the edge (with the body also sinking into the rock, Vesper_Perched_StaysOnTheRock, the tail tip whipped 5.7-7.3 m
        // in a tick). RED (CreatureFlight.NoStepCapsForTest: the step caps of touchdownFromHover and drapeOwnHeading off): the
        // together blend swung the tail held up in the hover down over the rock's edge at 0.5-0.6 m a tick, and an edge (or
        // the hanging run's end) moving to another joint relaid the run behind it at once: 0.6-1.2 m on 4 of 48 rock runs
        // offline (in Unity 0.68 m on the air run, seed 1). Each RED asserts its own measure (the largest step against the
        // bound) over several seeds, not one run's first failure.
        const float kTouchJump = 0.05f, kTouchAfter = 3f;
        const int kTouchPerched = 4;
        const float kSegStretch = 0.01f;   // the shown chain's segments within 1 % of the spacing (the step caps relay it)
        internal struct TouchRun { public string name, problem; public int touchdowns, worstJoint; public bool rock, perched, gaveUp; public float girdle, head, joint, jointAt, bound, stretch; public int stretchSeg; }

        internal static TouchRun MeasureTouchdown(CreatureSpecies sp, bool rock, bool fromAir, int seed)
        {
            var r = new CreatureCommandBrainTests.Rig(sp, rock ? CreatureCommandBrainTests.Rock(sp.scale) : null, seed);
            var t = new TouchRun { name = $"{(rock ? "perch" : "land")} from the {(fromAir ? "air" : "ground")} (seed {seed})", rock = rock, worstJoint = -1, bound = kTouchJump * r.L };
            r.Run(0.5f);
            if (fromAir) { string a = r.Airborne(); if (a != null) { t.problem = $"{t.name}: {a}"; return t; } }
            r.brain.Command(rock ? CreatureCommand.Perch : CreatureCommand.Land);
            int g = r.sim.Flight.Girdle, nj = sp.JointCount, n = Mathf.CeilToInt(120f * r.T / kDt), after = -1;
            float seg = sp.spacing * sp.scale;
            var prev = new Vector3[nj];
            for (int i = 0; i < n && after != 0; i++)
            {
                System.Array.Copy(r.sim.Joints, prev, nj);
                FlightMode m0 = r.sim.Mode;
                r.Tick();
                if (r.sim.Mode == FlightMode.Touchdown && m0 != FlightMode.Touchdown) { t.touchdowns++; after = -1; }
                bool watching = r.sim.Mode == FlightMode.Touchdown || after > 0;
                if (watching)
                {
                    t.head = Mathf.Max(t.head, (r.sim.Joints[0] - prev[0]).magnitude);
                    t.girdle = Mathf.Max(t.girdle, (r.sim.Joints[g] - prev[g]).magnitude);
                    for (int j = 0; j < nj; j++)
                    {
                        float d = (r.sim.Joints[j] - prev[j]).magnitude;
                        if (d > t.joint) { t.joint = d; t.worstJoint = j; t.jointAt = r.sim.Time; }
                    }
                    // the capped body keeps its segment lengths (the step caps clamp joints, then lay the chain again)
                    for (int j = 1; j < nj; j++)
                    {
                        float dev = Mathf.Abs((r.sim.Joints[j] - r.sim.Joints[j - 1]).magnitude / seg - 1f);
                        if (dev > t.stretch) { t.stretch = dev; t.stretchSeg = j; }
                    }
                }
                if (m0 == FlightMode.Touchdown && r.sim.Mode == FlightMode.Ground) after = Mathf.CeilToInt(kTouchAfter * r.T / kDt);
                else if (after > 0 && r.sim.Mode == FlightMode.Ground && !r.sim.Flight.Landing) after--;
            }
            t.perched = r.sim.Flight.Perched;
            t.gaveUp = r.brain.PerchGaveUp && r.brain.Doing == "could not reach the perch: on open ground";
            TestContext.Out.WriteLine($"{t.name}: {t.touchdowns} touchdown(s), perched {t.perched}{(t.gaveUp ? " (gave up the perch)" : "")}; largest step in a tick: girdle {t.girdle:F3} m, head {t.head:F3} m, any joint {t.joint:F3} m (joint {t.worstJoint}, t = {t.jointAt:F2} s); bound {t.bound:F3} m; segment lengths within {t.stretch * 100f:F2} % (segment {t.stretchSeg}); {r.State}");
            if (t.touchdowns == 0) t.problem = $"{t.name}: never touched down ({r.State})";
            else if (rock && !t.perched && !t.gaveUp) t.problem = $"{t.name}: neither perched on the rock nor gave it up ({r.State})";
            return t;
        }

        // the jump clauses of a run (null: none crossed). A rock run that gave up the perch is judged on the girdle and the
        // head only: lying across the rock after it, the walking chain steps joints over the 2 m ledge at once (a known
        // walking-over-a-ledge issue, not the touchdown's - up to 1 m on its joints); a girdle or head jump there fails
        // and says so.
        internal static string JumpProblem(in TouchRun t)
        {
            string ledge = t.rock && !t.perched ? " (gave up the perch: the known walking-over-a-ledge issue)" : "";
            if (t.girdle > t.bound) return $"{t.name}: the girdle jumped {t.girdle:F3} m in one tick of the touchdown (> {kTouchJump} L = {t.bound:F3} m){ledge}";
            if (t.head > t.bound) return $"{t.name}: the head jumped {t.head:F3} m in one tick of the touchdown (> {kTouchJump} L = {t.bound:F3} m){ledge}";
            if (t.rock && !t.perched) return null;
            if (t.stretch > kSegStretch) return $"{t.name}: segment {t.stretchSeg} is {t.stretch * 100f:F2} % off its length (> {kSegStretch * 100f} %)";
            if (t.girdle > t.bound) return $"{t.name}: the girdle jumped {t.girdle:F3} m in one tick of the touchdown (> {kTouchJump} L = {t.bound:F3} m)";
            if (t.head > t.bound) return $"{t.name}: the head jumped {t.head:F3} m in one tick of the touchdown (> {kTouchJump} L = {t.bound:F3} m)";
            if (t.joint > t.bound) return $"{t.name}: joint {t.worstJoint} jumped {t.joint:F3} m in one tick at t = {t.jointAt:F2} s (> {kTouchJump} L = {t.bound:F3} m)";
            return null;
        }

        static IEnumerable<(bool rock, bool air, int seed)> TouchRuns()
        {
            for (int seed = 1; seed <= 4; seed++) yield return (true, true, seed);
            for (int seed = 1; seed <= 3; seed++) yield return (true, false, seed);
            for (int seed = 1; seed <= 2; seed++) yield return (false, true, seed);
        }

        [Test] public void Vesper_Touchdown_NoGirdleJump()
        {
            CreatureSpecies sp = Vesper();
            Assert.IsTrue(sp.flight.touchdownFromHover, "Vesper.asset: touchdownFromHover");
            Assert.IsTrue(sp.flight.drapeOwnHeading, "Vesper.asset: drapeOwnHeading");
            int perched = 0;
            foreach (var (rock, air, seed) in TouchRuns())
            {
                TouchRun t = MeasureTouchdown(sp, rock, air, seed);
                Assert.IsNull(t.problem, t.problem);
                string e = JumpProblem(t);
                Assert.IsNull(e, e);
                if (rock && t.perched) perched++;
            }
            Assert.GreaterOrEqual(perched, kTouchPerched, "rock runs that perched (the others gave up as documented)");
        }

        // A RED's measure over perch runs (from the air and from the ground, seeds 1..maxSeed in turn): the largest step of
        // the girdle (girdle true) or of any joint in a perched run against 0.05 L, the worst seen (> 1: crossed; it stops
        // at the first run that crosses).
        static float RedTouchRuns(CreatureSpecies sp, bool girdle, int maxSeed)
        {
            float worst = 0f; string first = null; int perchedRuns = 0;
            for (int seed = 1; seed <= maxSeed && first == null; seed++)
                foreach (bool air in new[] { true, false })
                {
                    TouchRun t = MeasureTouchdown(sp, true, air, seed);
                    if (!t.perched) continue;
                    perchedRuns++;
                    float v = (girdle ? t.girdle : t.joint) / t.bound;
                    worst = Mathf.Max(worst, v);
                    if (v > 1f) { first = JumpProblem(t); break; }
                }
            TestContext.Out.WriteLine($"RED: {first ?? "none crossed"} (worst {worst:F2} x the bound, {perchedRuns} perched runs)");
            Assert.Greater(perchedRuns, 0, "perched runs to judge (none perched)");
            return worst;
        }

        [Test] public void Red_Vesper_TouchdownJump()
        {
            CreatureSpecies sp = Vesper(null, f => f.touchdownFromHover = false);
            Assert.Greater(RedTouchRuns(sp, true, 4), 1f, "the girdle's largest step through a rock touchdown against 0.05 L");
        }

        [Test] public void Red_Vesper_TailWhip()
        {
            CreatureSpecies sp = Vesper(null, f => f.drapeOwnHeading = false);
            Assert.Greater(RedTouchRuns(sp, false, 8), 1f, "any joint's largest step through a rock touchdown against 0.05 L");
        }

        [Test] public void Red_Vesper_TouchdownSweep()
        {
            CreatureSpecies sp = Vesper();
            float worst;
            CreatureFlight.NoStepCapsForTest = true;
            try { worst = RedTouchRuns(sp, false, 24); }
            finally { CreatureFlight.NoStepCapsForTest = false; }
            Assert.Greater(worst, 1f, "any joint's largest step through a rock touchdown against 0.05 L");
        }

        // ------------------------------------------------------------ perched on a rock (task E)

        // Perched on the command brain's rock (1.2 x 0.5 m at scale 1: 2 m high at scale 4; the 12.4 m body's head and tail
        // reach past its edges), standing kPerchStand s x sqrt s from the end of the touchdown: every tick the girdle stays
        // at least the rock's top + (1 - kPerchSink) x its clearance high, and no joint of the chain is inside the rock
        // (inside its footprint and below its top). RED (FlightSpec.perchFrontLevel off): the head held out over the edge
        // was grounded on the floor below, the neck dragged down after it took the floor too, and within ~1 s the whole
        // body sank into the rock (the girdle from 4.07 to 1.72 m).
        // (Perching itself is CreatureCommandBrainTests' property: the run takes the first seed of kPerchSeeds that perches -
        // a run that gives the perch up is not this test's; the test takes two such seeds.)
        const float kPerchStand = 10f, kPerchSink = 0.25f;
        const int kPerchSeeds = 8;
        internal struct PerchedRun { public int seed; public bool perched; public float lowest, floor, deepest; public string first; }

        // Perch with seeds from `fromSeed` on (up to kPerchSeeds of them) until one perches, then stand kPerchStand s x sqrt s
        internal static PerchedRun MeasurePerched(CreatureSpecies sp, int fromSeed)
        {
            WingedTestSurfaces rock = CreatureCommandBrainTests.Rock(sp.scale);
            WingedTestSurfaces.Box box = rock.Boxes[0];
            var o = new PerchedRun { lowest = float.MaxValue };
            for (int seed = fromSeed; seed < fromSeed + kPerchSeeds; seed++)
            {
                var r = new CreatureCommandBrainTests.Rig(sp, rock, seed);
                r.Run(0.5f);
                r.brain.Command(CreatureCommand.Perch);
                if (r.Until(() => r.sim.Mode == FlightMode.Ground && r.sim.Flight.Perched, 120f) < 0f) { TestContext.Out.WriteLine($"seed {seed}: never perched ({r.State}); the next seed"); continue; }
                o.seed = seed; o.perched = true;
                int g = r.sim.Flight.Girdle, n = Mathf.CeilToInt(kPerchStand * r.T / kDt);
                float clear = sp.joints[g].clearance * sp.scale;
                o.floor = box.max.y + clear * (1f - kPerchSink);
                for (int i = 0; i < n; i++)
                {
                    r.Tick();
                    if (r.sim.Mode != FlightMode.Ground) { o.first = o.first ?? $"seed {seed}: left the perch at t = {r.sim.Time:F1} s ({r.State})"; break; }
                    float gy = r.sim.Joints[g].y;
                    o.lowest = Mathf.Min(o.lowest, gy);
                    if (gy < o.floor && o.first == null) o.first = $"seed {seed}, t = {r.sim.Time:F1} s: the girdle sank to {gy:F2} m (< the rock's top {box.max.y:F2} m + {1f - kPerchSink} x its clearance {clear:F2} m)";
                    for (int j = 0; j < sp.JointCount; j++)
                    {
                        Vector3 p = r.sim.Joints[j];
                        if (p.x <= box.min.x || p.x >= box.max.x || p.z <= box.min.z || p.z >= box.max.z || p.y >= box.max.y) continue;
                        o.deepest = Mathf.Max(o.deepest, box.max.y - p.y);
                        if (o.first == null) o.first = $"seed {seed}, t = {r.sim.Time:F1} s: joint {j} is inside the rock ({box.max.y - p.y:F2} m below its top)";
                    }
                }
                TestContext.Out.WriteLine($"seed {seed}: perched {kPerchStand} s x sqrt s; lowest girdle {o.lowest:F2} m (limit {o.floor:F2} m), deepest joint inside the rock {o.deepest:F2} m; {r.State}");
                return o;
            }
            o.first = $"no seed of {fromSeed}..{fromSeed + kPerchSeeds - 1} perched";
            return o;
        }

        [Test] public void Vesper_Perched_StaysOnTheRock()
        {
            CreatureSpecies sp = Vesper();
            Assert.IsTrue(sp.flight.perchFrontLevel, "Vesper.asset: perchFrontLevel");
            int from = 1;
            for (int k = 0; k < 2; k++)
            {
                PerchedRun o = MeasurePerched(sp, from);
                Assert.IsTrue(o.perched, o.first);
                Assert.IsNull(o.first, o.first);
                from = o.seed + 1;
            }
        }

        // RED: perchFrontLevel off - on the first seed that perches the girdle sinks below its limit (or a joint is inside
        // the rock)
        [Test] public void Red_Vesper_PerchedSinks()
        {
            PerchedRun o = MeasurePerched(Vesper(null, f => f.perchFrontLevel = false), 1);
            TestContext.Out.WriteLine("RED: " + o.first);
            Assert.IsTrue(o.perched, o.first);
            Assert.IsTrue(o.lowest < o.floor || o.deepest > 0f, $"the girdle's lowest {o.lowest:F2} m against {o.floor:F2} m, the deepest joint inside the rock {o.deepest:F2} m");
        }

        // ------------------------------------------------------------ low past a rock's edge (task E)

        // Perch from the ground beside a tall rock (2.4 x 1 m at scale 1, centred (2.5, 0, 1.25) x s: Vesper's launch from the
        // aviary's middle vaults up right past its edge, below its top), the first kLowRockSeconds s x sqrt s: the girdle
        // never moves more than 0.05 L in one tick. RED (FlightSpec.floorLiftEased off): the air floor (the girdle kept its
        // clearance above the surface below it) jumped by the rock's height as the girdle crossed its edge, and the girdle
        // was snapped up 1.53 m in one tick (in the task-4 perch sweep also 1.36 m on a low pass while giving up).
        // While over the rock's footprint the girdle (eased up) never goes below the rock's top (kRockDepth).
        const float kLowRockSeconds = 5f, kRockDepth = 0f;
        internal static string LowOverRockProblem(CreatureSpecies sp) => LowOverRockProblem(sp, out _);

        internal static string LowOverRockProblem(CreatureSpecies sp, out float jumpShare)
        {
            var rock = new WingedTestSurfaces().AddRock(new Vector3(2.5f, 0f, 1.25f) * sp.scale, new Vector3(2.4f, 1f, 2.4f) * sp.scale, 11);
            WingedTestSurfaces.Box box = rock.Boxes[0];
            var r = new CreatureCommandBrainTests.Rig(sp, rock, 1);
            float deepest = float.MinValue;   // the girdle's depth below the rock's top while over its footprint (< 0: above it)
            r.brain.Command(CreatureCommand.Perch);
            int g = r.sim.Flight.Girdle, n = Mathf.CeilToInt(kLowRockSeconds * r.T / kDt), launches = 0;
            float worst = 0f, at = 0f; FlightMode during = FlightMode.Ground;
            for (int i = 0; i < n; i++)
            {
                Vector3 g0 = r.sim.Joints[g];
                FlightMode m0 = r.sim.Mode;
                r.Tick();
                if (m0 == FlightMode.Ground && r.sim.Mode == FlightMode.Launch) launches++;
                float j = (r.sim.Joints[g] - g0).magnitude;
                if (j > worst) { worst = j; at = r.sim.Time; during = r.sim.Mode; }
                Vector3 p = r.sim.Joints[g];
                if (p.x > box.min.x && p.x < box.max.x && p.z > box.min.z && p.z < box.max.z) deepest = Mathf.Max(deepest, box.max.y - p.y);
            }
            float bound = kTouchJump * r.L;
            jumpShare = worst / bound;
            TestContext.Out.WriteLine($"{launches} launch(es); largest girdle step in a tick {worst:F3} m at t = {at:F2} s ({during}); bound {bound:F3} m; the girdle over the rock at most {deepest:F3} m below its top (bound {kRockDepth} m); {r.State}");
            if (launches == 0) return $"never launched ({r.State})";
            if (deepest > kRockDepth) return $"the girdle went {deepest:F3} m below the rock's top over its footprint (> {kRockDepth} m: inside the rock)";
            return worst > bound ? $"the girdle jumped {worst:F3} m in one tick at t = {at:F2} s ({during}; > {kTouchJump} L = {bound:F3} m)" : null;
        }

        [Test] public void Vesper_LowOverRock_NoGirdleJump()
        {
            CreatureSpecies sp = Vesper();
            Assert.IsTrue(sp.flight.floorLiftEased, "Vesper.asset: floorLiftEased");
            string e = LowOverRockProblem(sp);
            Assert.IsNull(e, e);
        }

        [Test] public void Red_Vesper_LowOverRockJump()
        {
            string e = LowOverRockProblem(Vesper(null, f => f.floorLiftEased = false), out float share);
            TestContext.Out.WriteLine("RED: " + e);
            Assert.Greater(share, 1f, "the girdle's largest step against 0.05 L");
        }

        // ------------------------------------------------------------ the aviary's wall (task E)

        // The command brain's session in an aviary of radius `factor` x the kit's lab radius (KitChecks.LabArea: 27.1 m,
        // safe 17.9 m at scale 4) with the command tests' rock (4.8 x 2 m at (10, 0, 5)): Fly 60, Circle 30, Perch 60, Fly 60
        // (s x sqrt s), seeds 1-3, in the air: the share of the air time on the containment wall (the girdle at the safe
        // radius), the head's largest step in a tick, and the trunk against the flight - the planar angle between the trunk
        // (girdle - next joint) and the velocity: its largest, and the longest stretch above kSideways.
        // The cause: the containment cut the outward speed (up to 90 deg in one sub-step) and the wall turn reversed what was
        // left within ~0.3 s, far tighter than Vesper can turn; the body, following its velocity, flew sideways or tail first
        // and the head swung up to 3 m in one tick. FlightSpec.containTurns turns an outward flight along the wall at the
        // coordinated turn rate, begun early enough: Vesper_Aviary_ContainTurns (measured on: 0.16 % on the wall, trunk at
        // most 70 deg off and above 45 deg for 0.29 s x sqrt s, head 0.28 m). It is NOT on in Vesper.asset yet: the command
        // brain's perch line-up and landing lean on the cut's instant reversal (task E fix round 1 report). Meanwhile
        // FlightSpec.trunkTurnBounded (on: a mitigation) slows only the impossible reversal: Vesper_Aviary_NoHeadPop.
        // VesperLab's aviary is VesperLabLayout.AviaryFactor x the lab's radius (40.7 m; the lab's 27.1 m: 6-7 % on the wall).
        const float kWallShare = 0.02f, kSideways = 45f, kSidewaysFor = 0.5f;   // -, deg, s x sqrt s
        internal static EnclosureArea Aviary(CreatureSpecies sp, float factor)
        {
            EnclosureArea lab = KitChecks.LabArea(sp);
            EnclosureArea a = EnclosureArea.For(lab.center, lab.up, lab.radius * factor, sp, CreatureLab.MaxScale(sp));
            a.maxHeight = lab.maxHeight;
            return a;
        }

        internal struct WallRun { public float share, head, headBound, angle, sideways; }

        internal static WallRun WallSession(CreatureSpecies sp, float factor)
        {
            EnclosureArea area = Aviary(sp, factor);
            float s = sp.scale, T = Mathf.Sqrt(s), L = All<FlowBody>(sp)[0].BodyLength * s;
            int air = 0, wall = 0;
            float girdle = 0f, run = 0f, longestAt = 0f;
            var w = new WallRun();
            for (int seed = 1; seed <= 3; seed++)
            {
                var sim = new CreatureSim(sp, CreatureCommandBrainTests.Rock(s), _ => Vector3.up, area.center, Vector3.forward, s, seed) { Bounds = area };
                var brain = new CommandBrain(area, seed);
                int g = sim.Flight.Girdle;
                for (int k = 0; k < 4; k++)
                {
                    brain.Command(k == 1 ? CreatureCommand.Circle : k == 2 ? CreatureCommand.Perch : CreatureCommand.Fly);
                    int n = Mathf.CeilToInt((k == 1 ? 30f : 60f) * T / kDt);
                    for (int i = 0; i < n; i++)
                    {
                        FlightMode m0 = sim.Mode;
                        Vector3 h0 = sim.Joints[0], g0 = sim.Joints[g];
                        sim.Tick(kDt, brain.Target(sim, kDt));
                        if (m0 != FlightMode.Air || sim.Mode != FlightMode.Air) { run = 0f; continue; }
                        air++;
                        w.head = Mathf.Max(w.head, (sim.Joints[0] - h0).magnitude);
                        girdle = Mathf.Max(girdle, (sim.Joints[g] - g0).magnitude);
                        if (area.PlanarDistance(sim.Flight.Model.Position) >= area.safeRadius - 1e-3f) wall++;
                        Vector3 trunk = Vector3.ProjectOnPlane(sim.Joints[g] - sim.Joints[g + 1], area.up);
                        Vector3 v = Vector3.ProjectOnPlane(sim.Flight.Model.Velocity, area.up);
                        float a = v.sqrMagnitude > 0.25f * s ? Vector3.Angle(trunk, v) : 0f;
                        w.angle = Mathf.Max(w.angle, a);
                        run = a > kSideways ? run + kDt : 0f;
                        if (run / T > w.sideways) { w.sideways = run / T; longestAt = sim.Time; }
                    }
                }
            }
            w.share = wall / (float)Mathf.Max(1, air);
            w.headBound = kHeadJumpGirdles * girdle + kHeadJumpSlack * L;
            TestContext.Out.WriteLine($"aviary radius {area.radius:F1} m (safe {area.safeRadius:F1} m, {factor} x the lab's): {air * kDt:F0} s in the air, {w.share * 100f:F2} % of it on the wall; head's largest step in a tick {w.head:F3} m (bound {w.headBound:F3} m: {kHeadJumpGirdles} x the girdle's {girdle:F3} m + {kHeadJumpSlack} L); trunk vs velocity: largest {w.angle:F0} deg, longest above {kSideways} deg {w.sideways:F2} s x sqrt s (ending t = {longestAt:F1} s)");
            return w;
        }

        internal static string WallProblem(in WallRun w)
        {
            if (w.share > kWallShare) return $"on the wall {w.share * 100f:F2} % of the air time (> {kWallShare * 100f} %)";
            if (w.sideways >= kSidewaysFor) return $"flew sideways: the trunk more than {kSideways} deg off the velocity for {w.sideways:F2} s x sqrt s (>= {kSidewaysFor}), up to {w.angle:F0} deg";
            return HeadPopProblem(w);
        }

        internal static string HeadPopProblem(in WallRun w) => w.head > w.headBound ? $"the head jumped {w.head:F3} m in one tick (> {w.headBound:F3} m)" : null;

        // the cure (FlightSpec.containTurns on): on the wall <= 2 %, no sideways flight, no head pop
        [Test] public void Vesper_Aviary_ContainTurns()
        {
            string e = WallProblem(WallSession(Vesper(null, f => f.containTurns = true), VesperLabLayout.AviaryFactor));
            Assert.IsNull(e, e);
        }

        // RED: the shipped containment (Vesper.asset: containTurns off) - the cut and the reversal: sideways flight
        [Test] public void Red_Vesper_Aviary_ContainCut()
        {
            CreatureSpecies sp = Vesper();
            Assert.IsFalse(sp.flight.containTurns, "Vesper.asset: containTurns (off until the command brain stops leaning on the cut)");
            WallRun w = WallSession(sp, VesperLabLayout.AviaryFactor);
            TestContext.Out.WriteLine($"RED: the trunk more than {kSideways} deg off the velocity for {w.sideways:F2} s x sqrt s");
            Assert.GreaterOrEqual(w.sideways, kSidewaysFor, "the longest sideways stretch (s x sqrt s)");
        }

        // Vesper.asset as shipped (the mitigation trunkTurnBounded on, containTurns off): the head does not pop at the wall
        // (the sideways flight stays: 0.93 s x sqrt s measured)
        [Test] public void Vesper_Aviary_NoHeadPop()
        {
            CreatureSpecies sp = Vesper();
            Assert.IsTrue(sp.flight.trunkTurnBounded, "Vesper.asset: trunkTurnBounded");
            string e = HeadPopProblem(WallSession(sp, VesperLabLayout.AviaryFactor));
            Assert.IsNull(e, e);
        }

        // RED: trunkTurnBounded off - the head swings 3 m in a tick at the wall
        [Test] public void Red_Vesper_Aviary_HeadPop()
        {
            WallRun w = WallSession(Vesper(null, f => f.trunkTurnBounded = false), VesperLabLayout.AviaryFactor);
            TestContext.Out.WriteLine($"RED: the head's largest step {w.head:F3} m (bound {w.headBound:F3} m)");
            Assert.Greater(w.head, w.headBound, "the head's largest step at the wall against its bound");
        }
    }
}
