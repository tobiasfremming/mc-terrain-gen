using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The kit's Pincer part on the Scolopendra's forcipules and ultimate legs: validation, the opening stays in its
    // range (closed .. openDeg) over walking and standing, held at walkOpen while walking and flexing (open, hold,
    // snap shut) at a standstill, the pair moves together and never touches (chains, tips and spines) at any opening,
    // the mesh follows the runtime's pose (the drawn tip is the runtime's tip at a flex peak), and the size rule
    // (lengths x s, the flex clock x sqrt s). RED proofs: a pair curled into each other is caught by the clearance
    // check; an opening clock that ignores the standstill would never flex.
    // Scolopendra v2 (2026-10-20): it has a ThreatDisplay, so at a standstill its pincers spread to their
    // displayOpenDeg (breathing with the flex clock) instead of flexing to openDeg: the range and the peaks are
    // measured against the opening each one actually reaches standing (Peak), and the pair clearance covers it.
    public class CreaturePincerTests
    {
        const float kDt = 1f / 60f;

        static CreatureSpecies Scolo() => CreatureKitAssets.Load("Scolopendra");

        static SpeciesCtx Ctx(int joints) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, false);

        static List<PincerRuntime> Pincers(CreatureSim sim)
        {
            var l = new List<PincerRuntime>();
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is PincerRuntime p) l.Add(p);
            return l;
        }

        static CreatureSim Walker(CreatureSpecies sp, float s) =>
            new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, 1);

        // The widest opening a pincer reaches standing: its display opening when the species has a ThreatDisplay.
        static float Peak(Pincer p, CreatureSpecies sp)
        {
            bool display = false;
            foreach (CreaturePart q in sp.Compiled.Parts) display |= q is ThreatDisplay;
            return display && p.displayOpenDeg > 0f ? Mathf.Max(p.openDeg, p.displayOpenDeg) : p.openDeg;
        }

        static Vector3 Ahead(CreatureSim sim) => sim.Joints[0] + (sim.Rotations[0] * Vector3.forward) * (10f * sim.SizeScale);

        static Pincer Forcipule() => new Pincer
        {
            id = "f", at = new PartSocket(1, 1, new Vector3(0.004f, -0.002f, 0.005f)), direction = new Vector3(0.64f, 0f, 0.77f),
            length = 0.0165f, radiusStart = 0.0023f, radiusEnd = 0.0013f, tipLength = 0.005f, curveDeg = 106f,
        };

        [Test] public void Pincer_Validate_RejectsBadData()
        {
            Pincer ok = Forcipule();
            Assert.IsNull(ok.Validate(Ctx(10)));
            Assert.AreEqual(2, ok.BoneCount(Ctx(10)), "one bone per segment");
            var bad = (Pincer)ok.Clone(); bad.segments = 5;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "too many segments");
            bad = (Pincer)ok.Clone(); bad.length = 0f;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "no length");
            bad = (Pincer)ok.Clone(); bad.at.side = 0;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "no side and not mirrored");
            bad = (Pincer)ok.Clone(); bad.direction = new Vector3(0f, 1f, 0f);
            Assert.IsNotNull(bad.Validate(Ctx(10)), "direction along the hinge");
            bad = (Pincer)ok.Clone(); bad.tipLength = 0.02f;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "tip longer than the last segment");
            bad = (Pincer)ok.Clone(); bad.openDeg = 120f;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "opening out of range");
            bad = (Pincer)ok.Clone(); bad.layer = 5;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "an octopus layer");
            bad = (Pincer)ok.Clone(); bad.at.joint = 10;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "joint out of range");
        }

        [Test] public void Pincer_Profile_OpensHoldsSnapsRests()
        {
            Assert.AreEqual(0f, PincerRuntime.Profile(0f), 1e-6f);
            Assert.AreEqual(1f, PincerRuntime.Profile(0.45f), 1e-6f, "held open");
            Assert.AreEqual(0f, PincerRuntime.Profile(0.8f), 1e-6f, "rests shut");
            float prev = 0f, maxStep = 0f;
            for (int i = 1; i <= 1000; i++)
            {
                float v = PincerRuntime.Profile(i / 1000f);
                Assert.That(v >= 0f && v <= 1f, $"profile {v} at {i / 1000f}");
                maxStep = Mathf.Max(maxStep, Mathf.Abs(v - prev)); prev = v;
            }
            Assert.Less(maxStep, 0.03f, "continuous: no jump in the cycle");
        }

        // ------------------------------------------------------------ range over walking and standing

        // Walk `walk` s, then stand `stand` s: the opening of every pincer per tick.
        static void Run(CreatureSim sim, float walk, float stand, System.Action<bool, List<PincerRuntime>> each)
        {
            List<PincerRuntime> ps = Pincers(sim);
            float T = Mathf.Sqrt(sim.SizeScale);
            for (int t = 0; t < (int)(walk * T / kDt); t++) { sim.Tick(kDt, Ahead(sim)); each(true, ps); }
            // standing: the target is the head itself (WanderBrain's pause)
            for (int t = 0; t < (int)(stand * T / kDt); t++) { sim.Tick(kDt, sim.Joints[0]); each(false, ps); }
        }

        [TestCase(0.9f)] [TestCase(1.1f)] public void Pincer_Opening_StaysInRange_FlexesAtAStandstill(float jitter)
        {
            CreatureSpecies sp = Scolo();
            float s = sp.scale * jitter;
            CreatureSim sim = Walker(sp, s);
            List<PincerRuntime> ps = Pincers(sim);
            Assert.AreEqual(4, ps.Count, "forcipules and ultimate legs, a pair each");
            var lo = new float[4]; var hi = new float[4]; var peaks = new int[4]; var rising = new bool[4]; var last = new float[4];
            var walkHi = new float[4]; float pairGap = 0f;
            for (int i = 0; i < 4; i++) { lo[i] = float.MaxValue; hi[i] = float.MinValue; }
            int tick = 0;
            Run(sim, 8f, 14f, (walking, list) =>
            {
                tick++;
                for (int i = 0; i < 4; i++)
                {
                    float o = list[i].Opening;
                    lo[i] = Mathf.Min(lo[i], o); hi[i] = Mathf.Max(hi[i], o);
                    if (walking && tick > 120) walkHi[i] = Mathf.Max(walkHi[i], Mathf.Abs(o - list[i].Part.walkOpen * list[i].Part.openDeg));
                    if (!walking)
                    {
                        if (o < last[i] && rising[i] && o > 0.9f * Peak(list[i].Part, sp)) peaks[i]++;
                        rising[i] = o > last[i];
                    }
                    last[i] = o;
                }
                pairGap = Mathf.Max(pairGap, Mathf.Abs(list[0].Opening - list[1].Opening), Mathf.Abs(list[2].Opening - list[3].Opening));
            });
            for (int i = 0; i < 4; i++)
            {
                Pincer p = ps[i].Part;
                float peak = Peak(p, sp);
                TestContext.WriteLine($"x{s:F2} {p.id}: opening {lo[i]:F2}..{hi[i]:F2} deg (open {p.openDeg}, standing peak {peak}), walking off walkOpen by <= {walkHi[i]:F3}, {peaks[i]} flex peaks standing");
                Assert.GreaterOrEqual(lo[i], -1e-3f, p.id + ": never past closed");
                Assert.LessOrEqual(hi[i], peak + 1e-3f, p.id + ": never past its widest opening (openDeg, or displayOpenDeg in a display)");
                Assert.Greater(hi[i], 0.95f * peak, p.id + ": a flex (or the display) opens fully");
                Assert.Less(walkHi[i], 0.02f * p.openDeg + 1e-3f, p.id + ": walking holds walkOpen");
                // standing 14 s x T: (14 - delay) / period flexes (+-1 for the phase)
                float want = (14f - p.idleDelay) / p.flexPeriod;
                Assert.AreEqual(want, peaks[i], 1.01f, p.id + ": one flex per flexPeriod");
            }
            Assert.AreEqual(0f, pairGap, 1e-5f, "a pair opens and closes together");
        }

        // ------------------------------------------------------------ the pair never touches

        // Sample points (joint frame, scale 1) and radii along a pincer at opening `open`: the chain every ~0.2 mm
        // and its spines.
        static void Capsules(Pincer p, float open, List<Vector4> into)
        {
            into.Clear();
            for (int k = 0; k < p.Segments; k++)
            {
                float L = p.SegmentLength(k), s0 = p.SegmentStart(k);
                int n = Mathf.Max(4, Mathf.CeilToInt(L / 0.0002f));
                for (int i = 0; i <= n; i++)
                {
                    float t = L * i / n;
                    Vector3 q = p.PointAt(k, t, open, out _);
                    into.Add(new Vector4(q.x, q.y, q.z, p.RadiusAt(s0 + t)));
                }
            }
            for (int i = 0; i < p.SpineCount; i++)
            {
                p.Spine(i, open, out Vector3 b, out Vector3 d, out _, out float r0);
                for (int j = 0; j <= 8; j++)
                {
                    float f = j / 8f;
                    Vector3 q = b + d * (p.spineLength * f);
                    into.Add(new Vector4(q.x, q.y, q.z, r0 * (1f - f)));
                }
            }
        }

        // Smallest surface clearance between the left and right pincer over the whole opening range (metres, scale 1):
        // closed .. openDeg, and on to displayOpenDeg when it opens wider in a display.
        static float PairClearance(Pincer left, Pincer right, out float atOpen)
        {
            var a = new List<Vector4>(); var b = new List<Vector4>();
            float worst = float.MaxValue; atOpen = 0f;
            for (float open = 0f; open <= Mathf.Max(left.openDeg, left.displayOpenDeg) + 1e-3f; open += 1f)
            {
                Capsules(left, open, a); Capsules(right, open, b);
                foreach (Vector4 u in a)
                    foreach (Vector4 v in b)
                    {
                        float d = ((Vector3)u - (Vector3)v).magnitude - u.w - v.w;
                        if (d < worst) { worst = d; atOpen = open; }
                    }
            }
            return worst;
        }

        static Pincer Side(CompiledSpecies c, string id)
        {
            foreach (CreaturePart p in c.Parts) if (p is Pincer q && q.id == id) return q;
            return null;
        }

        [TestCase("forcipule")] [TestCase("ultimate")] public void Pincer_Pair_NeverTouches(string id)
        {
            CompiledSpecies c = Scolo().Compiled;
            Pincer l = Side(c, id + ".L"), r = Side(c, id + ".R");
            Assert.IsNotNull(l); Assert.IsNotNull(r);
            Assert.AreEqual(l.at.joint, r.at.joint, "a pair on one joint (one frame)");
            float gap = PairClearance(l, r, out float at);
            TestContext.WriteLine($"{id}: smallest clearance {gap * 1000f:F2} mm at scale 1 (x {Scolo().scale} in the lab) at opening {at:F0} deg");
            Assert.Greater(gap, 0.0003f, id + ": the pair never touches (>= 0.3 mm at scale 1)");
        }

        [Test] public void Red_PairCurledIntoEachOther_IsCaught()
        {
            CompiledSpecies c = Scolo().Compiled;
            var l = (Pincer)Side(c, "forcipule.L").Clone(); var r = (Pincer)Side(c, "forcipule.R").Clone();
            l.curveDeg = r.curveDeg = 140f;
            float gap = PairClearance(l, r, out _);
            TestContext.WriteLine($"RED: curled 140 deg: clearance {gap * 1000f:F2} mm");
            Assert.Less(gap, 0f, "the clearance check bites: the tips cross into each other");
        }

        // ------------------------------------------------------------ the mesh follows the runtime

        [Test] public void Pincer_Mesh_TipIsTheRuntimeTip_AtAFlexPeak()
        {
            CreatureSpecies sp = Scolo();
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            float s = sp.scale;
            CreatureSim sim = Walker(sp, s);
            List<PincerRuntime> ps = Pincers(sim);
            Run(sim, 4f, 0f, (w, l) => { });
            int guard = 0;
            while (ps[0].Opening < 0.97f * Peak(ps[0].Part, sp) && guard++ < 2000) sim.Tick(kDt, sim.Joints[0]);
            Assert.Less(guard, 2000, "reached a flex peak");
            var bones = new Matrix4x4[sp.Compiled.Runtime.BoneCount];
            sim.WriteBoneWorld(bones, 0);
            var v = mesh.vertices;
            var uv2 = new List<Vector4>(); mesh.GetUVs(2, uv2);
            foreach (PincerRuntime p in ps)
            {
                int lastBone = p.FirstBone + p.Part.Segments - 1;
                Matrix4x4 skin = bones[lastBone] * bind[lastBone];
                Vector3 tip = p.Tip();
                float best = float.MaxValue;
                for (int i = 0; i < v.Length; i++)
                    if ((int)uv2[i].x == lastBone && uv2[i].z > 0.999f) best = Mathf.Min(best, (skin.MultiplyPoint3x4(v[i]) - tip).magnitude);
                TestContext.WriteLine($"{p.Part.id}: opening {p.Opening:F1} deg, nearest drawn vertex to the runtime tip {best * 1000f:F4} mm");
                Assert.Less(best, 1e-5f * s, p.Part.id + ": the drawn tip is the runtime's tip (mesh and pose agree)");
            }
            Object.DestroyImmediate(mesh);
        }

        // ------------------------------------------------------------ size

        // Time from stopping to the first flex peak, and the tip's reach from its root bone, at sizeScale s.
        static void Measure(CreatureSpecies sp, float s, out float tPeak, out float reach)
        {
            CreatureSim sim = Walker(sp, s);
            PincerRuntime f = Pincers(sim)[0];
            for (int t = 0; t < 240; t++) sim.Tick(kDt * Mathf.Sqrt(s / sp.scale), Ahead(sim));
            float t0 = sim.Time, prev = f.Opening;
            tPeak = -1f;
            for (int t = 0; t < 4000 && tPeak < 0f; t++)
            {
                sim.Tick(kDt * Mathf.Sqrt(s / sp.scale), sim.Joints[0]);
                if (f.Opening < prev && prev > 0.9f * f.Part.openDeg) tPeak = sim.Time - t0;
                prev = f.Opening;
            }
            var b = new Matrix4x4[sp.Compiled.Runtime.BoneCount];
            sim.WriteBoneWorld(b, 0);
            reach = (f.Tip() - (Vector3)b[f.FirstBone].GetColumn(3)).magnitude;
        }

        [Test] public void Pincer_Size_LengthsXs_FlexClockXSqrtS()
        {
            CreatureSpecies sp = Scolo();
            Measure(sp, 0.9f * sp.scale, out float tA, out float rA);
            Measure(sp, 1.1f * sp.scale, out float tB, out float rB);
            TestContext.WriteLine($"first peak {tA:F3} s / {tB:F3} s (ratio {tB / tA:F4}, sqrt {Mathf.Sqrt(1.1f / 0.9f):F4}); reach {rA * 1000f:F2} / {rB * 1000f:F2} mm");
            Assert.Greater(tA, 0f); Assert.Greater(tB, 0f);
            Assert.AreEqual(Mathf.Sqrt(1.1f / 0.9f), tB / tA, 0.02f, "the flex clock x sqrt s");
            Assert.AreEqual(1.1f / 0.9f, rB / rA, 0.01f, "lengths x s");
        }
    }
}
