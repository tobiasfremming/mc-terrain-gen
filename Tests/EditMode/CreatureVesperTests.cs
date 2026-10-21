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
    // it stays on top (perchFrontLevel), each with a RED (flag off). The REDs: a supple head; wing arms carried folded and a
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

        // In the air under its own FlightBrain (seeds 1 and 2, 120 s x sqrt s each; ticks that start and end in Air): the
        // head's largest step in one tick <= 3 x the girdle's largest + 0.02 L. The neck swings smoothly with the beat and the
        // turns (measured 0.48 m against the girdle's 0.13 m); a pop is the neck flipping. RED (FlightSpec.headStabiliseBounded
        // off): the long head stabiliser trailed the turns by metres (the target 14 m behind the 4.4 m neck) and the
        // containment's cut of the outward speed swung the trunk up to 62 deg in a tick - the head jumped 7.45 m.
        const float kHeadJumpGirdles = 3f, kHeadJumpSlack = 0.02f;
        internal static string HeadJumpProblem(CreatureSpecies sp, int seed, float seconds = 120f)
        {
            EnclosureArea area = KitChecks.LabArea(sp);
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, area.center, Vector3.forward, sp.scale, seed) { Bounds = area };
            var brain = new FlightBrain(area, seed);
            float L = All<FlowBody>(sp)[0].BodyLength * sp.scale;
            int n = Mathf.CeilToInt(seconds * Mathf.Sqrt(sp.scale) / kDt), g = sim.Flight.Girdle, airTicks = 0;
            float head = 0f, girdle = 0f, headAt = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 h0 = sim.Joints[0], g0 = sim.Joints[g];
                FlightMode m0 = sim.Mode;
                sim.Tick(kDt, brain.Target(sim, kDt));
                if (m0 != FlightMode.Air || sim.Mode != FlightMode.Air) continue;
                airTicks++;
                float jh = (sim.Joints[0] - h0).magnitude;
                if (jh > head) { head = jh; headAt = sim.Time; }
                girdle = Mathf.Max(girdle, (sim.Joints[g] - g0).magnitude);
            }
            float bound = kHeadJumpGirdles * girdle + kHeadJumpSlack * L;
            TestContext.Out.WriteLine($"seed {seed}: {airTicks * kDt:F0} s in the air; largest step in a tick: head {head:F3} m (t = {headAt:F2} s), girdle {girdle:F3} m; bound {bound:F3} m");
            if (airTicks < 60f * Mathf.Sqrt(sp.scale) * 10f) return $"seed {seed}: only {airTicks * kDt:F1} s in the air";
            return head > bound ? $"seed {seed}: the head jumped {head:F3} m in one tick at t = {headAt:F2} s (> {kHeadJumpGirdles} x the girdle's {girdle:F3} m + {kHeadJumpSlack} L = {bound:F3} m)" : null;
        }

        [Test] public void Vesper_Flight_NoHeadJump()
        {
            CreatureSpecies sp = Vesper();
            Assert.IsTrue(sp.flight.headStabiliseBounded, "Vesper.asset: headStabiliseBounded");
            string e = HeadJumpProblem(sp, 1) ?? HeadJumpProblem(sp, 2);
            Assert.IsNull(e, e);
        }

        [Test] public void Red_Vesper_HeadJump()
        {
            string e = HeadJumpProblem(Vesper(null, f => f.headStabiliseBounded = false), 1);
            TestContext.Out.WriteLine(e);
            Assert.IsNotNull(e);
        }

        // Touchdowns under the command brain (CreatureCommandBrainTests.Rig; a 4.8 m rock): onto the rock from the air and
        // from the ground (it perches; air seeds 1 and 3), and onto open ground from the air (seed 3). From the tick it turns
        // Touchdown until kTouchAfter s x sqrt s after it is walking (standing) again, neither the girdle nor the head - nor
        // any joint of the chain (task E: the draped tail) - moves more than 0.05 L in one tick (the command brain's snap
        // rule). RED (FlightSpec.touchdownFromHover off): a touchdown begun on a tick's last sub-step showed the walking
        // restart's body for one tick (laid along the rock with the girdle on its point; at the stance) and the eased blend
        // carried it back the next - the girdle jumped 2.93 m (from the air) and 1.61 m (from the ground) onto the rock, the
        // head 2.45 m. RED (FlightSpec.drapeOwnHeading off): the tail hanging over the rock's edge was laid in the vertical
        // plane of the one segment before the edge, taken afresh every sub-step; when the edge moved to another joint or that
        // segment's heading flipped, the whole hanging run swung about the edge (with the body also sinking into the rock,
        // Vesper_Perched_StaysOnTheRock, the tail tip whipped 5.7-7.3 m in a tick).
        const float kTouchJump = 0.05f, kTouchAfter = 3f;
        internal static string TouchdownJumpProblem(CreatureSpecies sp, bool rock, bool fromAir, int seed)
        {
            var r = new CreatureCommandBrainTests.Rig(sp, rock ? CreatureCommandBrainTests.Rock(sp.scale) : null, seed);
            string name = $"{(rock ? "perch" : "land")} from the {(fromAir ? "air" : "ground")} (seed {seed})";
            r.Run(0.5f);
            if (fromAir) { string a = r.Airborne(); if (a != null) return $"{name}: {a}"; }
            r.brain.Command(rock ? CreatureCommand.Perch : CreatureCommand.Land);
            int g = r.sim.Flight.Girdle, nj = sp.JointCount, n = Mathf.CeilToInt(120f * r.T / kDt), after = -1, touchdowns = 0, worstJoint = -1;
            var prev = new Vector3[nj];
            float head = 0f, girdle = 0f, joint = 0f, jointAt = 0f;
            for (int i = 0; i < n && after != 0; i++)
            {
                System.Array.Copy(r.sim.Joints, prev, nj);
                FlightMode m0 = r.sim.Mode;
                r.Tick();
                if (r.sim.Mode == FlightMode.Touchdown && m0 != FlightMode.Touchdown) { touchdowns++; after = -1; }
                bool watching = r.sim.Mode == FlightMode.Touchdown || after > 0;
                if (watching)
                {
                    head = Mathf.Max(head, (r.sim.Joints[0] - prev[0]).magnitude);
                    girdle = Mathf.Max(girdle, (r.sim.Joints[g] - prev[g]).magnitude);
                    for (int j = 0; j < nj; j++)
                    {
                        float d = (r.sim.Joints[j] - prev[j]).magnitude;
                        if (d > joint) { joint = d; worstJoint = j; jointAt = r.sim.Time; }
                    }
                }
                if (m0 == FlightMode.Touchdown && r.sim.Mode == FlightMode.Ground) after = Mathf.CeilToInt(kTouchAfter * r.T / kDt);
                else if (after > 0 && r.sim.Mode == FlightMode.Ground && !r.sim.Flight.Landing) after--;
            }
            float bound = kTouchJump * r.L;
            TestContext.Out.WriteLine($"{name}: {touchdowns} touchdown(s), perched {r.sim.Flight.Perched}; largest step in a tick: girdle {girdle:F3} m, head {head:F3} m, any joint {joint:F3} m (joint {worstJoint}, t = {jointAt:F2} s); bound {bound:F3} m");
            if (touchdowns == 0) return $"{name}: never touched down ({r.State})";
            if (rock && !r.sim.Flight.Perched) return $"{name}: not perched on the rock ({r.State})";
            if (girdle > bound) return $"{name}: the girdle jumped {girdle:F3} m in one tick of the touchdown (> {kTouchJump} L = {bound:F3} m)";
            if (head > bound) return $"{name}: the head jumped {head:F3} m in one tick of the touchdown (> {kTouchJump} L = {bound:F3} m)";
            if (joint > bound) return $"{name}: joint {worstJoint} jumped {joint:F3} m in one tick at t = {jointAt:F2} s (> {kTouchJump} L = {bound:F3} m)";
            return null;
        }

        [Test] public void Vesper_Touchdown_NoGirdleJump()
        {
            CreatureSpecies sp = Vesper();
            Assert.IsTrue(sp.flight.touchdownFromHover, "Vesper.asset: touchdownFromHover");
            Assert.IsTrue(sp.flight.drapeOwnHeading, "Vesper.asset: drapeOwnHeading");
            string e = TouchdownJumpProblem(sp, true, true, 1) ?? TouchdownJumpProblem(sp, true, false, 1) ?? TouchdownJumpProblem(sp, true, true, 3)
                ?? TouchdownJumpProblem(sp, false, true, 3);
            Assert.IsNull(e, e);
        }

        [Test] public void Red_Vesper_TouchdownJump()
        {
            CreatureSpecies sp = Vesper(null, f => f.touchdownFromHover = false);
            string air = TouchdownJumpProblem(sp, true, true, 1), ground = TouchdownJumpProblem(sp, true, false, 1);
            TestContext.Out.WriteLine(air); TestContext.Out.WriteLine(ground);
            Assert.IsNotNull(air);
            Assert.IsNotNull(ground);
        }

        [Test] public void Red_Vesper_TailWhip()
        {
            string e = TouchdownJumpProblem(Vesper(null, f => f.drapeOwnHeading = false), true, true, 3);
            TestContext.Out.WriteLine("RED: " + e);
            Assert.IsNotNull(e);
            StringAssert.Contains("joint", e);
        }

        // ------------------------------------------------------------ perched on a rock (task E)

        // Perched on the command brain's rock (1.2 x 0.5 m at scale 1: 2 m high at scale 4; the 12.4 m body's head and tail
        // reach past its edges), standing kPerchStand s x sqrt s from the end of the touchdown: every tick the girdle stays
        // at least the rock's top + (1 - kPerchSink) x its clearance high, and no joint of the chain is inside the rock
        // (inside its footprint and below its top). RED (FlightSpec.perchFrontLevel off): the head held out over the edge
        // was grounded on the floor below, the neck dragged down after it took the floor too, and within ~1 s the whole
        // body sank into the rock (the girdle from 4.07 to 1.72 m).
        const float kPerchStand = 10f, kPerchSink = 0.25f;
        internal static string PerchedProblem(CreatureSpecies sp, int seed)
        {
            WingedTestSurfaces rock = CreatureCommandBrainTests.Rock(sp.scale);
            WingedTestSurfaces.Box box = rock.Boxes[0];
            var r = new CreatureCommandBrainTests.Rig(sp, rock, seed);
            r.Run(0.5f);
            r.brain.Command(CreatureCommand.Perch);
            if (r.Until(() => r.sim.Mode == FlightMode.Ground && r.sim.Flight.Perched, 120f) < 0f) return $"seed {seed}: never perched ({r.State})";
            int g = r.sim.Flight.Girdle, n = Mathf.CeilToInt(kPerchStand * r.T / kDt);
            float clear = sp.joints[g].clearance * sp.scale, floor = box.max.y + clear * (1f - kPerchSink), lowest = float.MaxValue, deepest = 0f;
            string first = null;
            for (int i = 0; i < n; i++)
            {
                r.Tick();
                if (r.sim.Mode != FlightMode.Ground) return $"seed {seed}: left the perch at t = {r.sim.Time:F1} s ({r.State})";
                float gy = r.sim.Joints[g].y;
                lowest = Mathf.Min(lowest, gy);
                if (gy < floor && first == null) first = $"seed {seed}, t = {r.sim.Time:F1} s: the girdle sank to {gy:F2} m (< the rock's top {box.max.y:F2} m + {1f - kPerchSink} x its clearance {clear:F2} m)";
                for (int j = 0; j < sp.JointCount; j++)
                {
                    Vector3 p = r.sim.Joints[j];
                    if (p.x <= box.min.x || p.x >= box.max.x || p.z <= box.min.z || p.z >= box.max.z || p.y >= box.max.y) continue;
                    deepest = Mathf.Max(deepest, box.max.y - p.y);
                    if (first == null) first = $"seed {seed}, t = {r.sim.Time:F1} s: joint {j} is inside the rock ({box.max.y - p.y:F2} m below its top)";
                }
            }
            TestContext.Out.WriteLine($"seed {seed}: perched {kPerchStand} s x sqrt s; lowest girdle {lowest:F2} m (limit {floor:F2} m), deepest joint inside the rock {deepest:F2} m; {r.State}");
            return first;
        }

        [Test] public void Vesper_Perched_StaysOnTheRock()
        {
            CreatureSpecies sp = Vesper();
            Assert.IsTrue(sp.flight.perchFrontLevel, "Vesper.asset: perchFrontLevel");
            string e = PerchedProblem(sp, 1) ?? PerchedProblem(sp, 4);
            Assert.IsNull(e, e);
        }

        [Test] public void Red_Vesper_PerchedSinks()
        {
            string e = PerchedProblem(Vesper(null, f => f.perchFrontLevel = false), 1);
            TestContext.Out.WriteLine("RED: " + e);
            Assert.IsNotNull(e);
        }
    }
}
