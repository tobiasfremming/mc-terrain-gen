using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The kit's Tendril part (T5.9: a tentacle rooted on a spine body) on the TentacleCrab: validation, the root rides
    // its socket, segment lengths stay within the chain's stretch limits, it hangs while the body walks and lays its
    // tip on the ground when the body stops, holds it there without trembling, and lets go when the body walks on;
    // the size rule (0.9x / 1.1x: the same motion in Froude units); the mesh never inverts while it reaches and holds.
    // RED: a hanging, writhing tip moves far more than the hold's bound (the tremble check bites).
    public class CreatureTendrilTests
    {
        const float kDt = 1f / 60f;

        static CreatureSpecies Crab() => CreatureKitAssets.Load("TentacleCrab").Compiled.Runtime;

        static CreatureSim Walker(CreatureSpecies sp, float s, int seed = 1) =>
            new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, seed);

        static List<TendrilRuntime> Tendrils(CreatureSim sim)
        {
            var l = new List<TendrilRuntime>();
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is TendrilRuntime t) l.Add(t);
            return l;
        }

        static SpeciesCtx Ctx(int joints, bool soft = false) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, soft);

        [Test] public void Tendril_Validate_RejectsBadData()
        {
            var ok = new Tendril { id = "t", at = new PartSocket(1, 1, Vector3.zero) };
            Assert.IsNull(ok.Validate(Ctx(6)));
            Assert.AreEqual(12, ok.BoneCount(Ctx(6)), "one bone per particle");
            Assert.IsNotNull(ok.Validate(Ctx(6, true)), "a soft body's arms are Tentacles");
            var bad = (Tendril)ok.Clone(); bad.segments = 3; Assert.IsNotNull(bad.Validate(Ctx(6)), "too few segments");
            bad = (Tendril)ok.Clone(); bad.tipRadius = 0.05f; Assert.IsNotNull(bad.Validate(Ctx(6)), "tip thicker than root");
            bad = (Tendril)ok.Clone(); bad.layer = 3; Assert.IsNotNull(bad.Validate(Ctx(6)), "an eye layer");
            bad = (Tendril)ok.Clone(); bad.at.joint = 6; Assert.IsNotNull(bad.Validate(Ctx(6)), "joint out of range");
            bad = (Tendril)ok.Clone(); bad.reach = 1f; Assert.IsNotNull(bad.Validate(Ctx(6)), "reach past the length");
        }

        // The root rides its socket; every drawn segment stays within the chain's stretch limits (0.9 .. 1.2 x spacing),
        // walking, turning, stopping and holding.
        [Test] public void RootedTendril_RootFollowsJoint_StretchBounds()
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale);
            List<TendrilRuntime> ts = Tendrils(sim);
            Assert.AreEqual(4, ts.Count, "two mirrored pairs");
            var bones = new Matrix4x4[sp.BoneCount];
            float worstRoot = 0f, lo = float.PositiveInfinity, hi = 0f;
            for (int t = 0; t < 1500; t++)
            {
                Vector3 goal = t < 600 ? new Vector3(Mathf.Sin(t / 90f), 0f, 1f) * (20f * sp.scale) : t < 900 ? sim.Joints[0] : new Vector3(-10f, 0f, 0f) * sp.scale;
                sim.Tick(kDt, goal);
                sim.WriteBoneWorld(bones, 0);
                foreach (TendrilRuntime r in ts)
                {
                    Tendril p = r.Part;
                    p.RootLocal(out Vector3 off, out _);
                    Vector3 want = sim.Joints[p.at.joint] + sim.Rotations[p.at.joint] * (off * sim.SizeScale);
                    worstRoot = Mathf.Max(worstRoot, (r.Root - want).magnitude);
                    TentacleChain c = r.Chain;   // the drawn chain (DisplayX, taken when the bones are written)
                    for (int k = 0; k + 1 < c.N; k++)
                    {
                        float f = (c.DisplayX[k + 1] - c.DisplayX[k]).magnitude / c.Spacing;
                        lo = Mathf.Min(lo, f); hi = Mathf.Max(hi, f);
                    }
                }
            }
            TestContext.Out.WriteLine($"root off its socket <= {worstRoot * 1000f:F4} mm; segments {lo:F3} .. {hi:F3} x spacing");
            Assert.Less(worstRoot, 1e-5f * sp.scale, "the root rides its socket");
            Assert.GreaterOrEqual(lo, 0.9f - 1e-3f, "no segment squashed past the limit");
            Assert.LessOrEqual(hi, 1.2f + 1e-3f, "no segment stretched past the limit");
        }

        // Walk, stop: every tendril reaches and touches down; while it holds, its tip stays put (no tremble);
        // walking on, it lets go within a fraction of a second. Returns the first touch time (Froude-normalised).
        static float TouchCycle(CreatureSpecies sp, float k, out float worstTremble, out int touches, out float releaseAfter, out int touchedEach) => TouchCycle(sp, k, out worstTremble, out touches, out releaseAfter, out touchedEach, out _);

        static float TouchCycle(CreatureSpecies sp, float k, out float worstTremble, out int touches, out float releaseAfter, out int touchedEach, out float settled)
        {
            float s = sp.scale * k, dt = kDt * Mathf.Sqrt(k);
            var sim = Walker(sp, s);
            List<TendrilRuntime> ts = Tendrils(sim);
            var prev = new Vector3[ts.Count]; var held = new bool[ts.Count]; var wasEasing = new bool[ts.Count];
            worstTremble = 0f; touches = 0; releaseAfter = float.PositiveInfinity; settled = 0f;
            float firstTouch = float.PositiveInfinity, stopAt = 3f, goAt = 12f;
            for (int t = 0; t < (int)(15f / kDt); t++)
            {
                float time = t * kDt;
                Vector3 goal = time < stopAt ? new Vector3(0f, 0f, 30f * s) : time < goAt ? sim.Joints[0] : new Vector3(30f * s, 0f, 30f * s);
                sim.Tick(dt, goal);
                bool anyHold = false;
                for (int i = 0; i < ts.Count; i++)
                {
                    bool h = ts[i].State == TendrilRuntime.Phase.Hold;
                    if (h && held[i]) worstTremble = Mathf.Max(worstTremble, (ts[i].Tip - prev[i]).magnitude);
                    bool easing = ts[i].Chain.PinEasing(ts[i].Chain.N - 1);
                    if (h && held[i] && !easing && !wasEasing[i]) settled = Mathf.Max(settled, (ts[i].Tip - prev[i]).magnitude);
                    wasEasing[i] = easing;
                    if (h && !held[i] && firstTouch == float.PositiveInfinity) firstTouch = time;
                    held[i] = h; prev[i] = ts[i].Tip;
                    anyHold |= h;
                }
                if (time >= goAt && !anyHold && releaseAfter == float.PositiveInfinity) releaseAfter = time - goAt;
            }
            touchedEach = int.MaxValue;
            foreach (TendrilRuntime r in ts) { touches += r.Touches; if (r.Part.reach > 0f) touchedEach = Mathf.Min(touchedEach, r.Touches); TestContext.Out.WriteLine($"  {r.Part.id}: {r.Touches} touches"); }
            return firstTouch;
        }

        [TestCase(0.9f)] [TestCase(1f)] [TestCase(1.1f)]
        public void Tendril_TouchesDown_HoldsWithoutTremble_LetsGo(float k)
        {
            CreatureSpecies sp = Crab();
            float first = TouchCycle(sp, k, out float tremble, out int touches, out float release, out int each, out float settled);
            TestContext.Out.WriteLine($"x{k:F1}: first touch {first:F2} s, {touches} touches (fewest by one tendril {each}), worst tip motion while holding {tremble * 1000f:F3} mm/frame, settled tip {settled * 1000f:F4} mm/frame, let go {release:F2} s after walking on");
            Assert.Less(first, 6f, "a tendril touched down within 3 s of stopping");
            Assert.GreaterOrEqual(each, 1, "every reaching tendril touches down while the body stands (the mouth feelers only writhe)");
            // an eased pin slides onto its spot at 0.8 m/s x sqrt(s) at most, then holds still
            Assert.Less(tremble, TentacleChain.PinEaseSpeed * Mathf.Sqrt(sp.scale * k) * kDt * Mathf.Sqrt(k) * 1.01f + 1e-6f, "a held tip only eases onto its spot");
            Assert.Less(settled, 1e-5f * sp.scale * k, "once eased onto its spot, a held tip does not move at all");
            Assert.Less(release, 0.5f, "walking on, every tendril lets go at once");
        }

        // Froude: a 0.9x and a 1.1x crab touch down at the same normalised time (dt x sqrt s), and the hanging
        // tendril's tip sits at the same place in body units.
        [Test] public void Tendril_Size_FroudeSimilar()
        {
            CreatureSpecies sp = Crab();
            float a = TouchCycle(sp, 0.9f, out _, out _, out _, out _), b = TouchCycle(sp, 1.1f, out _, out _, out _, out _);
            TestContext.Out.WriteLine($"first touch x0.9 {a:F3} s, x1.1 {b:F3} s (normalised)");
            Assert.AreEqual(a, b, 0.1f, "the same touch timing in Froude units");
            Vector3 TipRel(float k)
            {
                var sim = Walker(sp, sp.scale * k);
                for (int t = 0; t < 180; t++) sim.Tick(kDt * Mathf.Sqrt(k), new Vector3(0f, 0f, 30f * sp.scale * k));
                TendrilRuntime r = Tendrils(sim)[0];
                return Quaternion.Inverse(sim.Rotations[0]) * (r.Tip - sim.Joints[0]) / (sp.scale * k);
            }
            Vector3 ta = TipRel(0.9f), tb = TipRel(1.1f);
            TestContext.Out.WriteLine($"hanging tip / size: x0.9 {ta}, x1.1 {tb}");
            Assert.Less((ta - tb).magnitude, 0.03f * sp.Compiled.Runtime.BodyLength, "the same hang in body units");
        }

        // RED: the tremble bound bites - a hanging, writhing tip moves far more per frame.
        [Test] public void Red_HangingTip_MovesMoreThanTheHoldBound()
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale);
            TendrilRuntime r = Tendrils(sim)[0];
            Vector3 prev = r.Tip; float worst = 0f;
            for (int t = 0; t < 240; t++)
            {
                sim.Tick(kDt, new Vector3(0f, 0f, 30f * sp.scale));
                worst = Mathf.Max(worst, (r.Tip - prev).magnitude); prev = r.Tip;
            }
            float bound = TentacleChain.PinEaseSpeed * Mathf.Sqrt(sp.scale) * kDt;
            TestContext.Out.WriteLine($"RED: hanging tip {worst * 1000f:F2} mm/frame vs the hold bound {bound * 1000f:F2}");
            Assert.Greater(worst, bound, "the tremble check bites");
        }

        // The tendrils' tube never turns inside out while it reaches for the ground and holds it.
        [Test] public void Tendril_Mesh_NoInvertedTriangles_ReachAndHold()
        {
            CreatureSpecies sp = Crab();
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            Vector3[] v = mesh.vertices; int[] tri = mesh.triangles;
            var uv1 = new List<Vector2>(); var uv2 = new List<Vector4>();
            mesh.GetUVs(1, uv1); mesh.GetUVs(2, uv2);
            Object.DestroyImmediate(mesh);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            var world = new Matrix4x4[sp.BoneCount]; var skin = new Matrix4x4[sp.BoneCount];
            var pos = new Vector3[v.Length];
            var sim = Walker(sp, sp.scale, 2);
            int lo = Tendrils(sim)[0].FirstBone, frames = 0, holdFrames = 0;
            for (int t = 0; t < 900; t++)
            {
                sim.Tick(kDt, t < 180 ? new Vector3(0f, 0f, 30f * sp.scale) : sim.Joints[0]);
                if (t % 4 != 3) continue;
                frames++;
                foreach (TendrilRuntime r in Tendrils(sim)) if (r.State == TendrilRuntime.Phase.Hold) holdFrames++;
                sim.WriteBoneWorld(world, 0);
                for (int b = 0; b < skin.Length; b++) skin[b] = world[b] * bind[b];
                for (int i = 0; i < pos.Length; i++)
                {
                    Vector4 sk = uv2[i];
                    pos[i] = sk.z * skin[(int)sk.x].MultiplyPoint3x4(v[i]) + sk.w * skin[(int)sk.y].MultiplyPoint3x4(v[i]);
                }
                for (int q = 0; q < tri.Length; q += 3)
                {
                    int i0 = tri[q], i1 = tri[q + 1], i2 = tri[q + 2];
                    if (uv2[i0].x < lo) continue;   // the tendrils' bones come after the legs, eyes and antennae
                    Vector3 rest = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]);
                    Vector3 now = Vector3.Cross(pos[i1] - pos[i0], pos[i2] - pos[i0]);
                    Vector3 want = skin[(int)uv2[i0].x].MultiplyVector(rest);
                    Assert.IsTrue(Vector3.Dot(now, want) > 0f, $"t={sim.Time:F2}: tendril triangle {q / 3} (bones {uv2[i0]}) inverted");
                }
            }
            TestContext.Out.WriteLine($"{frames} frames, {holdFrames} tendril-frames holding");
            Assert.Greater(holdFrames, 0, "the check saw holds");
        }
    }
}
