using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // Per-joint spine spacing (Vesper rev 2, task 1; spec docs/superpowers/specs/2026-10-21-vesper-proportions-design.md
    // Goals 1): a FlowBody's profile `spacing` channel (metres; 0 = the body's spacing) compiles to
    // CreatureSpecies.segmentLengths (scale 1, one per segment, joint i to i + 1), interpolated by the profile's monotone
    // cubic at each segment's midpoint; with every key 0 nothing is emitted (every shipped species: empty, bit-identical).
    // Invariants on a varying Vesper (the asset with spacing 0.05 at the head .. 0.1 at the tail tip): it compiles with
    // those lengths, smooth (neighbours <= 1.25 apart); ArcToJoint is their sum and FlowBody's joint <-> arc mapping,
    // KitRender's tail start and the legs' attachments sit on it; the walking chain keeps each segment's own length
    // (2 %), the bind lays them at the stance (1 %, heights 2 %), the flight - launch, air, landing - keeps them (3 %);
    // a part that only supports a uniform chain refuses it in Validate.
    public class CreatureSegmentSpacingTests
    {
        const float kDt = 1f / 60f;
        internal const float kHeadSpacing = 0.05f, kTailSpacing = 0.1f;

        // Vesper with the spacing channel: kHeadSpacing at t = 0 .. kTailSpacing at t = 1, linear over the keys. The
        // head and neck stand half as far above the chest as the asset's (kNeckRise): the asset's neck falls 0.43 m over
        // eight 0.1 m segments, which half-length segments cannot (FlowBody.Validate: steeper than the rest run allows).
        internal static CreatureSpecies Varying() => CreatureVesperTests.Vesper(ps => SetSpacing(ps, kHeadSpacing, kTailSpacing));
        const float kNeckRise = 0.5f;

        internal static void SetSpacing(List<CreaturePart> ps, float head, float tail)
        {
            foreach (CreaturePart p in ps)
                if (p is FlowBody b)
                {
                    // the chest: the widest key; the keys ahead of it rise kNeckRise as far above it
                    int chest = 0;
                    for (int i = 1; i < b.profile.Length; i++) if (b.profile[i].halfWidth > b.profile[chest].halfWidth) chest = i;
                    float c0 = b.profile[chest].clearance;
                    for (int i = 0; i < b.profile.Length; i++)
                    {
                        b.profile[i].spacing = Mathf.Lerp(head, tail, b.profile[i].t);
                        if (i < chest) b.profile[i].clearance = c0 + kNeckRise * (b.profile[i].clearance - c0);
                    }
                }
        }

        static FlowBody BodyOf(CreatureSpecies sp)
        {
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is FlowBody b) return b;
            return null;
        }

        // ------------------------------------------------------------ uniform: nothing changes

        // (every shipped asset but Vesper: Vesper rev 2 is the species with per-joint spacing - its profile's spacing
        // channel - and is checked to be the only one)
        [Test] public void UniformSpecies_SegmentLengthsEmpty()
        {
            int n = 0;
            var varying = new List<string>();
            foreach (string name in CreatureKitAssets.Species())
            {
                CreatureSpecies sp = CreatureKitAssets.Load(name);
                if (sp == null) continue;
                if (!sp.UniformSpacing) { varying.Add(name); continue; }
                n++;
                Assert.IsTrue(sp.segmentLengths == null || sp.segmentLengths.Length == 0, $"{name}: segmentLengths emitted ({sp.segmentLengths?.Length})");
                Assert.IsTrue(sp.UniformSpacing, name);
                if (sp.bodyPlan == CreatureBodyPlan.Cephalopod) continue;
                // the literal uniform arithmetic, to the bit
                for (int k = 0; k < sp.JointCount; k++) Assert.AreEqual(sp.spacing * k, sp.ArcToJoint(k), $"{name}: ArcToJoint({k})");
                for (int i = 0; i + 1 < sp.JointCount; i++) Assert.AreEqual(sp.spacing, sp.SegmentLength(i), $"{name}: SegmentLength({i})");
                Assert.AreEqual(sp.headCapLength + sp.spacing * (sp.JointCount - 1) + sp.tailTipLength, sp.BodyLength, $"{name}: BodyLength");
                FlowBody b = sp.kitVersion > 0 ? BodyOf(sp) : null;
                if (b != null)
                {
                    Assert.IsNull(b.SegmentLengths(), $"{name}: FlowBody emits no lengths without the channel");
                    float L = Mathf.Max(b.headCapLength, 1e-3f) + b.spacing * (b.jointCount - 1) + Mathf.Max(b.tailTipLength, 1e-3f);
                    for (int k = 0; k < b.jointCount; k++) Assert.AreEqual((Mathf.Max(b.headCapLength, 1e-3f) + b.spacing * k) / L, b.UAtJoint(k), $"{name}: UAtJoint({k})");
                }
            }
            Assert.Greater(n, 10, "the shipped species");
            Assert.AreEqual("Vesper", string.Join(", ", varying), "the species with per-joint spacing");
        }

        // ------------------------------------------------------------ the channel compiles

        [Test] public void VaryingSpacing_Compiles()
        {
            CreatureSpecies sp = Varying();
            Assert.IsNull(sp.Validate(), sp.Validate());
            Assert.IsFalse(sp.UniformSpacing);
            float[] s = sp.segmentLengths;
            Assert.AreEqual(sp.JointCount - 1, s.Length, "one length per segment");
            Assert.AreEqual(kHeadSpacing, s[0], 0.005f, "the head's segment");
            Assert.AreEqual(kTailSpacing, s[s.Length - 1], 0.005f, "the tail's last segment");
            for (int i = 1; i < s.Length; i++)
            {
                Assert.GreaterOrEqual(s[i], s[i - 1] - 1e-6f, $"monotone at segment {i}");
                Assert.LessOrEqual(Mathf.Max(s[i], s[i - 1]) / Mathf.Min(s[i], s[i - 1]), 1.25f, $"no jump at segment {i}");
            }
            for (int i = 0; i < s.Length; i++) Assert.AreEqual(s[i], sp.SegmentLength(i), $"SegmentLength({i})");
            TestContext.Out.WriteLine($"segments {s[0]:F4} .. {s[s.Length - 1]:F4} m; spine {sp.ArcToJoint(sp.JointCount - 1):F3} m, body {sp.BodyLength:F3} m (scale 1)");
        }

        // ------------------------------------------------------------ the arc

        [Test] public void VaryingSpacing_ArcAtJoint()
        {
            CreatureSpecies sp = Varying();
            FlowBody b = BodyOf(sp);
            float sum = 0f;
            float lh = Mathf.Max(b.headCapLength, 1e-3f);
            for (int k = 0; k < sp.JointCount; k++)
            {
                if (k > 0) sum += sp.SegmentLength(k - 1);
                Assert.AreEqual(sum, sp.ArcToJoint(k), 1e-5f, $"ArcToJoint({k})");
                Assert.AreEqual(lh + sum, b.UAtJoint(k) * b.BodyLength, 1e-5f, $"FlowBody.UAtJoint({k})");
                if (k + 1 < sp.JointCount)
                {
                    // half way along segment k: the fractional joint, and the skin there is that segment's blend
                    float half = sum + 0.5f * sp.SegmentLength(k);
                    Assert.AreEqual(lh + half, b.UAtJoint(k + 0.5f) * b.BodyLength, 1e-5f, $"FlowBody.UAtJoint({k}.5)");
                    Vector4 want = CreatureKit.SpineBody.SegmentSkin(k, 0.5f), got = b.SkinAt(lh + half);
                    // (bone, weight) pairs in either order: at an even blend a rounding picks which bone comes first
                    float off = Mathf.Abs(Weight(want, k) - Weight(got, k)) + Mathf.Abs(Weight(want, k + 1) - Weight(got, k + 1));
                    Assert.Less(off, 1e-4f, $"FlowBody.SkinAt half way along segment {k}: {got} (want {want})");
                }
            }
            Assert.AreEqual(sp.headCapLength + sum + sp.tailTipLength, sp.BodyLength, 1e-5f, "BodyLength");
            Assert.AreEqual(sp.BodyLength, b.BodyLength, 1e-5f, "the body's own length");
            // KitRender's tail start: the arc to the hips
            int hip = -1;
            for (int l = 0; l < sp.legs.Length; l++) hip = Mathf.Max(hip, sp.legs[l].attachJoint);
            KitRender rd = KitRender.For(sp);
            Assert.AreEqual(sp.headCapLength + sp.ArcToJoint(hip), rd.BodyParams.z, 1e-5f, "KitRender tail start");
            // the rest body: the chain's arc to each leg's joint is ArcToJoint (x s) and the leg's shoulder hangs there
            float s = sp.scale;
            CreatureSim sim = FlowKit.Sim(sp, s);
            for (int l = 0; l < sp.legs.Length; l++)
            {
                int a = sp.legs[l].attachJoint;
                float arc = 0f;
                for (int i = 1; i <= a; i++) arc += (sim.Joints[i] - sim.Joints[i - 1]).magnitude;
                Assert.AreEqual(sp.ArcToJoint(a) * s, arc, 0.01f * sp.ArcToJoint(a) * s, $"leg {l}: the arc to its joint {a}");
                float off = (sim.Legs[l].shoulder - sim.Joints[a]).magnitude;
                Assert.AreEqual(sp.legs[l].shoulderOffset.magnitude * s, off, 1e-3f * s, $"leg {l}: the shoulder on its joint {a}");
            }
        }

        static float Weight(Vector4 skin, int bone) => (Mathf.RoundToInt(skin.x) == bone ? skin.z : 0f) + (Mathf.RoundToInt(skin.y) == bone ? skin.w : 0f);

        // ------------------------------------------------------------ the walk

        [Test] public void VaryingSpacing_SimKeepsSegmentLengths()
        {
            CreatureSpecies sp = Varying();
            float s = sp.scale, T = Mathf.Sqrt(s);
            CreatureSim sim = FlowKit.Sim(sp, s);
            int n = sp.JointCount, ticks = Mathf.RoundToInt(10f * T / kDt);
            float worst = 0f; int worstSeg = -1;
            for (int t = 0; t < ticks; t++)
            {
                Vector3 fwd = sim.Rotations[0] * Vector3.forward;
                sim.Tick(kDt, sim.Joints[0] + Quaternion.AngleAxis(50f * Mathf.Sin(t / (60f * T)), Vector3.up) * fwd * (2f * s));
                Assert.AreEqual(FlightMode.Ground, sim.Mode, $"t={sim.Time:F1} s");
                for (int i = 1; i < n; i++)
                {
                    float dev = Mathf.Abs((sim.Joints[i] - sim.Joints[i - 1]).magnitude / (sp.SegmentLength(i - 1) * s) - 1f);
                    if (dev > worst) { worst = dev; worstSeg = i - 1; }
                }
            }
            TestContext.Out.WriteLine($"walked {sim.Time:F1} s; segment lengths within {worst * 100f:F3} % (segment {worstSeg})");
            Assert.LessOrEqual(worst, 0.02f, $"segment {worstSeg} off its own length by {worst * 100f:F2} %");
        }

        // ------------------------------------------------------------ the bind

        [Test] public void VaryingSpacing_RestMatchesStance()
        {
            CreatureSpecies sp = Varying();
            Assert.IsTrue(sp.restAtSpacing, "Vesper binds at its stance");
            CreatureSim rest = CreatureRestPose.BindSim(sp);
            for (int i = 1; i < sp.JointCount; i++)
            {
                float len = (rest.Joints[i] - rest.Joints[i - 1]).magnitude;
                Assert.AreEqual(sp.SegmentLength(i - 1), len, 0.01f * sp.SegmentLength(i - 1), $"rest segment {i - 1}");
            }
            for (int i = 0; i < sp.JointCount; i++)
                Assert.AreEqual(sp.joints[i].clearance, rest.Joints[i].y, 0.02f * sp.joints[i].clearance, $"rest joint {i}'s height");
        }

        // ------------------------------------------------------------ the flight

        [Test] public void VaryingSpacing_FlightKeepsSegmentLengths()
        {
            CreatureSpecies sp = Varying();
            var r = new CreatureCommandBrainTests.Rig(sp);
            int n = sp.JointCount;
            float worst = 0f; int worstSeg = -1; string worstAt = "";
            var seen = new HashSet<FlightMode>();
            void Watch()
            {
                seen.Add(r.sim.Mode);
                for (int i = 1; i < n; i++)
                {
                    float dev = Mathf.Abs((r.sim.Joints[i] - r.sim.Joints[i - 1]).magnitude / (sp.SegmentLength(i - 1) * r.s) - 1f);
                    if (dev > worst) { worst = dev; worstSeg = i - 1; worstAt = r.State; }
                }
            }
            // launch, then 10 s x sqrt s in the air
            r.brain.Command(CreatureCommand.Fly);
            int cap = Mathf.CeilToInt(30f * r.T / kDt), i0;
            for (i0 = 0; i0 < cap && r.sim.Mode != FlightMode.Air; i0++) { r.Tick(); Watch(); }
            Assert.AreEqual(FlightMode.Air, r.sim.Mode, $"never airborne ({r.State})");
            int air = Mathf.CeilToInt(10f * r.T / kDt);
            for (int i = 0; i < air; i++) { r.Tick(); Watch(); }
            // land, and on for 2 s x sqrt s on the ground
            r.brain.Command(CreatureCommand.Land);
            int land = Mathf.CeilToInt(120f * r.T / kDt), after = -1;
            for (int i = 0; i < land && after != 0; i++)
            {
                FlightMode m0 = r.sim.Mode;
                r.Tick(); Watch();
                if (m0 == FlightMode.Touchdown && r.sim.Mode == FlightMode.Ground) after = Mathf.CeilToInt(2f * r.T / kDt);
                else if (after > 0) after--;
            }
            TestContext.Out.WriteLine($"modes {string.Join(", ", seen)}; segment lengths within {worst * 100f:F3} % (segment {worstSeg}, {worstAt}); {r.State}");
            Assert.IsTrue(seen.Contains(FlightMode.Launch) && seen.Contains(FlightMode.Touchdown), "launched and touched down");
            Assert.AreEqual(FlightMode.Ground, r.sim.Mode, $"landed ({r.State})");
            Assert.LessOrEqual(worst, 0.03f, $"segment {worstSeg} off its own length by {worst * 100f:F2} % ({worstAt})");
        }

        // ------------------------------------------------------------ refused where not honoured

        static CompiledSpecies Compile(System.Action<List<CreaturePart>> edit, System.Action<List<CreatureModule>> editModules = null)
        {
            CreatureSpecies a = CreatureKitAssets.LoadAsset("Vesper");
            var sp = ScriptableObject.CreateInstance<CreatureSpecies>();
            sp.name = "VesperSpacingVariant"; sp.scale = a.scale; sp.kitVersion = 1;
            var ps = new List<CreaturePart>();
            foreach (CreaturePart p in a.parts) ps.Add(p.Clone());
            edit(ps);
            sp.parts = ps;
            sp.modules = new List<CreatureModule>();
            foreach (CreatureModule m in a.modules) sp.modules.Add(m.Clone());
            editModules?.Invoke(sp.modules);
            return sp.Compiled;
        }

        static Swell Hump() => new Swell { id = "swell", at = new PartSocket { joint = 12 }, toJoint = 16, hump = 0.03f };

        [Test] public void VaryingSpacing_UnsupportedPartRefused()
        {
            // a Swell (a mesh part laid out at uniform joint spacing) is fine on the uniform Vesper (rev 2's asset with its
            // spacing channel off) ...
            CompiledSpecies uniform = Compile(ps =>
            {
                foreach (CreaturePart p in ps) if (p is FlowBody fb) for (int k = 0; k < fb.profile.Length; k++) fb.profile[k].spacing = 0f;
                ps.Add(Hump());
            });
            Assert.IsNull(uniform.Error, uniform.Error);
            // ... and refused on the varying one, naming the part
            CompiledSpecies varying = Compile(ps => { SetSpacing(ps, kHeadSpacing, kTailSpacing); ps.Add(Hump()); });
            Assert.IsNotNull(varying.Error, "a uniform-only part composed with a varying chain");
            StringAssert.Contains("swell", varying.Error);
            StringAssert.Contains("Swell", varying.Error);
            TestContext.Out.WriteLine(varying.Error);
            // the varying Vesper alone composes
            CompiledSpecies alone = Compile(ps => SetSpacing(ps, kHeadSpacing, kTailSpacing));
            Assert.IsNull(alone.Error, alone.Error);
        }

        // Modules are an allow-list too (FlowBody.kModuleHonoursSpacing: the ones Vesper and the Apex are built with): any
        // other is refused on a varying chain, naming it. RED (the old deny-list, Climb / ClimbExcursion / Soar only):
        // Slither - its head wave untested on per-joint segments - composed. (Tunnel, whose Burrow lays the body at
        // sp.spacing x the arc, needs a Burrow part, which the parts' allow-list already refuses.)
        [Test] public void VaryingSpacing_UnlistedModuleRefused()
        {
            CompiledSpecies varying = Compile(ps => SetSpacing(ps, kHeadSpacing, kTailSpacing), ms => ms.Add(new Slither()));
            TestContext.Out.WriteLine(varying.Error ?? "(no error)");
            Assert.IsNotNull(varying.Error, "Slither composed with a varying chain");
            StringAssert.Contains("module Slither supports only a uniformly spaced spine", varying.Error);
            // ... and composes on the uniform chain (the refusal is the spacing's)
            CompiledSpecies uniform = Compile(ps =>
            {
                foreach (CreaturePart p in ps) if (p is FlowBody fb) for (int k = 0; k < fb.profile.Length; k++) fb.profile[k].spacing = 0f;
            }, ms => ms.Add(new Slither()));
            Assert.IsNull(uniform.Error, uniform.Error);
            // every module the varying Vesper (and the Apex) is built with is on the list: the asset composes
            Assert.IsNull(Compile(ps => SetSpacing(ps, kHeadSpacing, kTailSpacing)).Error);
        }
    }
}
