using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The kit's posture parts on the RockCrab: Tuck (scared -> halt, crouch, fold the legs, claws and EyeStalks away,
    // sleep exactly still, peek, unfold, walk on) and EyeStalk (a stalk on its own bone that folds, its eye riding
    // the tip), and the CreatureSim posture inputs they use (Scare, Halt, Crouch, Asleep: off by default, every other
    // species bit-identical - the goldens). Checks: validation; the scare input (weak scares ignored, the hold grows with
    // the intensity); the tucked pose is reached within its time bound; tucked, nothing moves (0 mm/frame, the sim
    // sleeps); tucked, nothing pokes out of the rock (every leg, claw, stalk and eye vertex inside the lumpy shell or in
    // the ground); no inverted triangle through a whole tuck; it unfolds and walks on; random scares are staggered
    // (individuals never tuck in sync); Froude (0.9x / 1.1x tuck in sqrt-scaled time); no GC through a tuck.
    // RED: the walking pose pokes out (the silhouette check bites); a walking frame moves (the stillness check bites).
    public class CreatureTuckTests
    {
        const float kDt = 1f / 60f;
        const int CreatureLab_FirstSeed = 7 + 1000;   // a lab group's seeds: (lab seed + slot) + 1000 + individual

        static CreatureSpecies Crab() => CreatureKitAssets.Load("RockCrab").Compiled.Runtime;

        static CreatureSim Walker(CreatureSpecies sp, float s, int seed = 1) =>
            new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, seed);

        static TuckRuntime TuckOf(CreatureSim sim)
        {
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is TuckRuntime t) return t;
            return null;
        }

        static RigidLoft Shell(CreatureSpecies sp)
        {
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is RigidLoft l && l.id == "carapace") return l;
            return null;
        }

        static SpeciesCtx Ctx(IList<CreaturePart> parts, int joints = 5) => new SpeciesCtx(parts as IReadOnlyList<CreaturePart> ?? new List<CreaturePart>(parts), new CreatureModule[0], joints, false);

        static Vector3 Far(CreatureSpecies sp) => new Vector3(0.3f, 0f, 30f) * sp.scale;

        // Walks toward a far point for `seconds`, then scares it; returns the ticks until it sleeps tucked (-1: never).
        static int TuckAfterWalk(CreatureSim sim, TuckRuntime tk, float walkSeconds, float intensity = 1f, int max = 600)
        {
            Vector3 far = Far(sim.Species) * sim.SizeScale;
            for (int t = 0; t < (int)(walkSeconds * sim.TimeScale / kDt); t++) sim.Tick(kDt, far);
            sim.Scare(intensity);
            for (int t = 0; t < max; t++)
            {
                sim.Tick(kDt, far);
                if (tk.State == TuckRuntime.Phase.Curled) return t + 1;
            }
            return -1;
        }

        // ------------------------------------------------------------ skinning and the shell

        sealed class Skinner
        {
            public readonly Vector3[] v; public readonly List<Vector2> uv1 = new List<Vector2>(); public readonly List<Vector4> uv2 = new List<Vector4>();
            public readonly int[] tri; public readonly Matrix4x4[] bind, world, skin; public readonly Vector3[] pos;
            public Skinner(CreatureSpecies sp)
            {
                Mesh mesh = CreatureKitAssets.BuildMesh(sp);
                v = mesh.vertices; tri = mesh.triangles; mesh.GetUVs(1, uv1); mesh.GetUVs(2, uv2);
                Object.DestroyImmediate(mesh);
                bind = CreatureRestPose.BindPoses(sp);
                world = new Matrix4x4[sp.BoneCount]; skin = new Matrix4x4[sp.BoneCount]; pos = new Vector3[v.Length];
            }
            public void Skin(CreatureSim sim)
            {
                sim.WriteBoneWorld(world, 0);
                for (int b = 0; b < skin.Length; b++) skin[b] = world[b] * bind[b];
                for (int i = 0; i < pos.Length; i++)
                {
                    Vector4 s = uv2[i];
                    pos[i] = s.z * skin[(int)s.x].MultiplyPoint3x4(v[i]) + s.w * skin[(int)s.y].MultiplyPoint3x4(v[i]);
                }
            }
            // the first inverted triangle (null: none)
            public string Inverted()
            {
                for (int k = 0; k < tri.Length; k += 3)
                {
                    int i0 = tri[k], i1 = tri[k + 1], i2 = tri[k + 2];
                    Vector3 rest = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]);
                    Vector3 now = Vector3.Cross(pos[i1] - pos[i0], pos[i2] - pos[i0]);
                    Vector3 want = skin[(int)uv2[i0].x].MultiplyVector(rest);
                    if (!(Vector3.Dot(now, want) > 0f)) return $"triangle {k / 3} (part {Mathf.RoundToInt(uv1[i0].x)}, bones {uv2[i0]}) inverted";
                }
                return null;
            }
        }

        // How far (m, this individual's) a world point lies outside the lumpy shell; <= 0 inside it or in the ground.
        internal static float ShellExcess(CreatureSim sim, RigidLoft loft, Vector3 world)
        {
            if (world.y <= 0f) return 0f;   // flat ground at y = 0: in the ground is hidden
            int j = loft.at.joint;
            float s = sim.SizeScale;
            Vector3 p = Quaternion.Inverse(sim.Rotations[j]) * (world - sim.Joints[j]) / s;
            // the section whose plane holds p (the centre line barely curls)
            float bestT = 0f, bestD = float.PositiveInfinity;
            for (int k = 0; k <= 200; k++)
            {
                float t = k / 200f;
                loft.Centre(t, out Vector3 c, out Vector3 up, out Vector3 right);
                Vector3 tan = Vector3.Cross(right, up);
                float d = Mathf.Abs(Vector3.Dot(p - c, tan));
                if (d < bestD) { bestD = d; bestT = t; }
            }
            RigidLoft.Section q = loft.SectionAt(bestT);
            Vector3 tangent = Vector3.Cross(q.right, q.up);
            float along = Vector3.Dot(p - q.c, tangent);
            float x = Vector3.Dot(p - q.c, q.right), y = Vector3.Dot(p - q.c, q.up);
            float ang = Mathf.Atan2(y, x), r = Mathf.Sqrt(x * x + y * y);
            float bestA = float.PositiveInfinity, R = 0f;
            for (int k = 0; k < 720; k++)
            {
                float phi = 2f * Mathf.PI * k / 720f;
                Vector3 sp = loft.Lumped(q, phi) - q.c;
                float dx = Vector3.Dot(sp, q.right), dy = Vector3.Dot(sp, q.up);
                float da = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, ang * Mathf.Rad2Deg));
                if (da < bestA) { bestA = da; R = Mathf.Sqrt(dx * dx + dy * dy); }
            }
            float excess = r - R;
            // past either end of the loft: outside by at least how far past its last section
            if ((bestT <= 0f && along < 0f) || (bestT >= 1f && along > 0f)) excess = Mathf.Max(excess, Mathf.Abs(along));
            return excess * s;
        }

        // The limb vertices (legs, claws, stalks, eyes: skinned to any bone past the spine) that poke out of the shell:
        // the worst excess and its part / bone.
        static float WorstPoke(CreatureSim sim, Skinner sk, RigidLoft loft, out string where)
        {
            int spine = sim.Species.JointCount;
            float worst = 0f; where = "none";
            for (int i = 0; i < sk.pos.Length; i += 1)
            {
                if ((int)sk.uv2[i].x < spine) continue;
                float e = ShellExcess(sim, loft, sk.pos[i]);
                if (e > worst) { worst = e; where = $"vertex {i} part {Mathf.RoundToInt(sk.uv1[i].x)} bone {(int)sk.uv2[i].x} at {sk.pos[i]}"; }
            }
            return worst;
        }

        // ------------------------------------------------------------ data

        [Test] public void Tuck_Validate_RejectsBadData()
        {
            CreatureSpecies sp = Crab();
            CompiledSpecies c = sp.Compiled;
            Assert.IsNull(c.Error, c.Error);
            var parts = new List<CreaturePart>(c.Parts);
            Tuck tuck = null;
            foreach (CreaturePart p in parts) if (p is Tuck t) tuck = t;
            Assert.NotNull(tuck);
            Assert.IsNull(tuck.Validate(Ctx(parts)));
            Assert.AreEqual(0, tuck.BoneCount(Ctx(parts)));
            // not the last part
            var moved = new List<CreaturePart>(parts); moved.Remove(tuck); moved.Insert(1, tuck);
            Assert.IsNotNull(tuck.Validate(Ctx(moved)), "must be the last part");
            var bad = (Tuck)tuck.Clone(); bad.legs[0].pitchDeg = new float[2];
            var withBad = new List<CreaturePart>(parts); withBad[withBad.Count - 1] = bad;
            Assert.IsNotNull(bad.Validate(Ctx(withBad)), "one pitch per segment");
            bad = (Tuck)tuck.Clone(); bad.legs[0].leg = "nope"; withBad[withBad.Count - 1] = bad;
            Assert.IsNotNull(bad.Validate(Ctx(withBad)), "a fold names a limb");
            bad = (Tuck)tuck.Clone(); bad.curlSeconds = 0f; withBad[withBad.Count - 1] = bad;
            Assert.IsNotNull(bad.Validate(Ctx(withBad)), "curl time > 0");
            var stalk = new EyeStalk { id = "s", at = new PartSocket(0, 1, Vector3.zero) };
            Assert.IsNull(stalk.Validate(Ctx(parts)));
            Assert.AreEqual(1, stalk.BoneCount(Ctx(parts)));
            stalk.layer = 3; Assert.IsNotNull(stalk.Validate(Ctx(parts)), "an eye layer");
        }

        // At rest the stalk's bone is rigid to its joint; the eye of its side sits on its tip.
        [Test] public void EyeStalk_RigidToJoint_EyeOnTip()
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale);
            var bones = new Matrix4x4[sp.BoneCount];
            int n = 0;
            for (int t = 0; t < 240; t++)
            {
                sim.Tick(kDt, Far(sp));
                sim.WriteBoneWorld(bones, 0);
                foreach (IPartRuntime r in sim.PartRuntimes)
                {
                    if (!(r is EyeStalkRuntime es)) continue;
                    n++;
                    EyeStalk p = es.Part;
                    Vector3 root = bones[es.Bone].MultiplyPoint3x4(Vector3.zero);
                    Assert.Less((root - es.Root).magnitude, 1e-5f, "root on its socket");
                    Vector3 tip = bones[es.Bone].MultiplyPoint3x4(new Vector3(0f, 0f, p.length + 0.004f));
                    Vector3 eye = bones[sp.EyeBone(p.EyeIndex)].MultiplyPoint3x4(Vector3.zero);
                    Assert.Less((tip - eye).magnitude, 2e-3f * sp.scale, "the eye sits on the stalk's tip");
                }
            }
            Assert.AreEqual(2 * 240, n, "two stalks");
        }

        // ------------------------------------------------------------ the scare input and the time bound

        [Test] public void Tuck_WeakScareIgnored_StrongScareTucksWithinBound()
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale);
            TuckRuntime tk = TuckOf(sim);
            Assert.NotNull(tk);
            for (int t = 0; t < 60; t++) sim.Tick(kDt, Far(sp));
            sim.Scare(0.1f);
            for (int t = 0; t < 30; t++) sim.Tick(kDt, Far(sp));
            Assert.AreEqual(TuckRuntime.Phase.Active, tk.State, "a scare below minScare is ignored");
            int ticks = TuckAfterWalk(sim, tk, 0.5f);
            float seconds = ticks * kDt, bound = (tk.Part.curlSeconds + 0.25f) * sim.TimeScale + 2f * kDt;
            TestContext.Out.WriteLine($"tucked and asleep {seconds:F3} s after the scare (bound {bound:F3} s)");
            Assert.Greater(ticks, 0, "it tucks");
            Assert.LessOrEqual(seconds, bound, "the rock pose within curlSeconds + settle");
            Assert.IsTrue(sim.Asleep, "tucked: the sim sleeps");
            Assert.IsTrue(sim.Halt);
            Assert.AreEqual(tk.Part.crouch * sim.SizeScale, sim.Crouch, 1e-6f, "fully crouched");
        }

        // A stronger scare holds longer (the same individual, the same tuck count).
        [Test] public void Tuck_HoldGrowsWithIntensity()
        {
            CreatureSpecies sp = Crab();
            float Hold(float intensity)
            {
                var sim = Walker(sp, sp.scale, 5);
                TuckRuntime tk = TuckOf(sim);
                Assert.Greater(TuckAfterWalk(sim, tk, 0.5f, intensity), 0);
                int t = 0;
                while (tk.State == TuckRuntime.Phase.Curled && t < 3000) { sim.Tick(kDt, Far(sp)); t++; }
                return t * kDt;
            }
            float weak = Hold(0.4f), strong = Hold(1f);
            TestContext.Out.WriteLine($"held {weak:F2} s at 0.4, {strong:F2} s at 1.0");
            Assert.Greater(strong, weak * 1.2f);
        }

        // ------------------------------------------------------------ tucked: still, and a rock

        // Tucked, nothing moves: every bone and so every vertex is bit-identical frame to frame (0 mm/frame).
        [Test] public void Tuck_Curled_ZeroMotion()
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale);
            TuckRuntime tk = TuckOf(sim);
            Assert.Greater(TuckAfterWalk(sim, tk, 1f), 0);
            var a = new Matrix4x4[sp.BoneCount]; var b = new Matrix4x4[sp.BoneCount];
            sim.WriteBoneWorld(a, 0);
            Vector3 head = sim.Joints[0];
            int frames = 0; float worst = 0f;
            while (tk.State == TuckRuntime.Phase.Curled && frames < 2000)
            {
                sim.Tick(kDt, Far(sp));
                if (tk.State != TuckRuntime.Phase.Curled) break;
                sim.WriteBoneWorld(b, 0);
                for (int i = 0; i < a.Length; i++)
                    for (int k = 0; k < 16; k++) worst = Mathf.Max(worst, Mathf.Abs(a[i][k] - b[i][k]));
                frames++;
            }
            TestContext.Out.WriteLine($"{frames} tucked frames, largest bone matrix change {worst:E2}");
            Assert.Greater(frames * kDt, 0.5f * tk.Part.holdSeconds * sim.TimeScale * 0.8f, "held tucked for its hold");
            Assert.AreEqual(0f, worst, "tucked: exactly still");
            Assert.AreEqual(head, sim.Joints[0]);
        }

        // RED for the stillness: walking frames move.
        [Test] public void Red_Walking_Moves()
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale);
            var a = new Matrix4x4[sp.BoneCount]; var b = new Matrix4x4[sp.BoneCount];
            for (int t = 0; t < 60; t++) sim.Tick(kDt, Far(sp));
            sim.WriteBoneWorld(a, 0);
            sim.Tick(kDt, Far(sp));
            sim.WriteBoneWorld(b, 0);
            float worst = 0f;
            for (int i = 0; i < a.Length; i++) for (int k = 0; k < 16; k++) worst = Mathf.Max(worst, Mathf.Abs(a[i][k] - b[i][k]));
            Assert.Greater(worst, 1e-4f, "a walking frame moves");
        }

        // Tucked, nothing pokes out of the rock: every vertex of the legs, claws, stalks and eyes lies inside the lumpy
        // shell (or in the ground), at 0.9x and 1.1x.
        [TestCase(0.9f)]
        [TestCase(1.1f)]
        public void Tuck_Curled_NothingPokesOut(float f)
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale * f);
            TuckRuntime tk = TuckOf(sim);
            Assert.Greater(TuckAfterWalk(sim, tk, 1f), 0);
            var sk = new Skinner(sp);
            sk.Skin(sim);
            RigidLoft shell = Shell(sp);
            float worst = WorstPoke(sim, sk, shell, out string where);
            TestContext.Out.WriteLine($"x{f}: worst poke {worst * 1000f:F2} mm ({where})");
            Assert.LessOrEqual(worst, 1e-3f * sim.SizeScale, "a limb pokes out of the rock: " + where);
        }

        // RED for the silhouette: standing, the legs reach out past the shell.
        [Test] public void Red_Standing_PokesOut()
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale);
            for (int t = 0; t < 60; t++) sim.Tick(kDt, Far(sp));
            var sk = new Skinner(sp);
            sk.Skin(sim);
            float worst = WorstPoke(sim, sk, Shell(sp), out string where);
            TestContext.Out.WriteLine($"standing: worst poke {worst * 1000f:F1} mm ({where})");
            Assert.Greater(worst, 0.05f * sp.scale, "the legs stand out of the shell");
        }

        // No inverted triangle anywhere through a whole tuck: walking, curling, tucked, peeking, unfolding, walking on.
        [Test] public void Tuck_WholeCycle_NoInvertedTriangles()
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale);
            TuckRuntime tk = TuckOf(sim);
            var sk = new Skinner(sp);
            var seen = new HashSet<TuckRuntime.Phase>();
            int frames = 0, walkedOn = 0;
            for (int t = 0; t < 1500; t++)
            {
                if (t == 60) sim.Scare(1f);
                float a = t / 60f;
                sim.Tick(kDt, new Vector3(Mathf.Sin(a) * 2f, 0f, 3f + a) * sp.scale);
                seen.Add(tk.State);
                // a second second of walking after it stood up, then done (a random scare may come later)
                if (seen.Contains(TuckRuntime.Phase.Uncurling) && tk.State == TuckRuntime.Phase.Active && ++walkedOn > 60) break;
                if (t % 3 != 0) continue;
                sk.Skin(sim);
                string bad = sk.Inverted();
                Assert.IsNull(bad, $"t={t} {tk.State} u={tk.Progress:F2}: {bad}");
                frames++;
            }
            Assert.IsTrue(seen.Contains(TuckRuntime.Phase.Curled) && seen.Contains(TuckRuntime.Phase.Peeking) && seen.Contains(TuckRuntime.Phase.Uncurling),
                          "the whole cycle ran: " + string.Join(",", seen));
            Assert.AreEqual(TuckRuntime.Phase.Active, tk.State, "back on its feet");
            TestContext.Out.WriteLine($"{frames} frames checked");
        }

        // ------------------------------------------------------------ uncurl, size, sync, GC

        // After the hold it peeks (stalks up first, body still down), unfolds and walks on.
        [Test] public void Tuck_Uncurl_ResumesWalking()
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale);
            TuckRuntime tk = TuckOf(sim);
            Assert.Greater(TuckAfterWalk(sim, tk, 1f), 0);
            Vector3 tucked = sim.Joints[0];
            bool peekedDown = false;
            int t = 0;
            while (tk.State != TuckRuntime.Phase.Active && t < 3000)
            {
                sim.Tick(kDt, Far(sp));
                if (tk.State == TuckRuntime.Phase.Peeking && tk.StalkFold < 0.5f && tk.Progress >= 1f) peekedDown = true;
                t++;
            }
            Assert.AreEqual(TuckRuntime.Phase.Active, tk.State, "it unfolds");
            Assert.IsTrue(peekedDown, "the stalks rise while the body is still tucked (the peek)");
            Assert.IsFalse(sim.Halt || sim.Asleep);
            Assert.AreEqual(0f, sim.Crouch, "standing again");
            for (int k = 0; k < (int)(2f / kDt); k++) sim.Tick(kDt, Far(sp));
            float walked = (sim.Joints[0] - tucked).magnitude;
            TestContext.Out.WriteLine($"walked {walked:F2} m in the 2 s after it stood up");
            Assert.Greater(walked, 0.25f * sp.speed * sim.TimeScale * 2f, "it walks on");
        }

        // Froude: a 0.9x and a 1.1x individual, ticked at dt x sqrt s, run the whole tuck (curl, sleep, peek, unfold) on
        // the same frames.
        [Test] public void Tuck_Size_FroudeTime()
        {
            CreatureSpecies sp = Crab();
            string Run(float f)
            {
                var sim = Walker(sp, sp.scale * f);
                TuckRuntime tk = TuckOf(sim);
                float dt = kDt * Mathf.Sqrt(f);
                var sb = new System.Text.StringBuilder();
                TuckRuntime.Phase ph = tk.State;
                for (int t = 0; t < 900; t++)
                {
                    if (t == 30) sim.Scare(1f);
                    sim.Tick(dt, Far(sp) * f);
                    if (tk.State != ph) { sb.Append($" {t}:{tk.State}"); ph = tk.State; }
                }
                return sb.ToString();
            }
            string a = Run(0.9f), b = Run(1.1f);
            TestContext.Out.WriteLine($"x0.9{a}; x1.1{b}");
            Assert.AreEqual(a, b, "the same phases on the same frames");
            StringAssert.Contains("Uncurling", a);
        }

        // Five individuals on random scares, consecutive seeds as the lab spawns them: they never tuck in sync - the first
        // tucks evenly staggered (golden-ratio sequence), the later ones independent.
        [Test] public void Tuck_RandomScares_Staggered()
        {
            CreatureSpecies sp = Crab();
            const int kN = 5;
            var sims = new CreatureSim[kN]; var tks = new TuckRuntime[kN];
            var starts = new List<float>[kN];
            for (int i = 0; i < kN; i++)
            {
                sims[i] = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, new Vector3(3f * i, 0f, 0f), Vector3.forward, sp.scale, CreatureLab_FirstSeed + i);
                tks[i] = TuckOf(sims[i]); starts[i] = new List<float>();
            }
            int allTucked = 0, frames = (int)(180f / kDt);
            var last = new int[kN];
            for (int t = 0; t < frames; t++)
            {
                int tucked = 0;
                for (int i = 0; i < kN; i++)
                {
                    float a = t * kDt * 0.3f + i;
                    sims[i].Tick(kDt, new Vector3(3f * i + Mathf.Sin(a) * 2f, 0f, Mathf.Cos(a) * 2f));
                    if (tks[i].Tucks != last[i]) { starts[i].Add(t * kDt); last[i] = tks[i].Tucks; }
                    if (tks[i].State != TuckRuntime.Phase.Active) tucked++;
                }
                if (tucked == kN) allTucked++;
            }
            // pairs: the share of one's tuck starts within 1 s of one of the other's (in sync: ~1)
            float worstShare = 0f;
            for (int i = 0; i < kN; i++)
            {
                Assert.GreaterOrEqual(starts[i].Count, 5, $"individual {i} tucks now and then ({starts[i].Count} in 3 min)");
                for (int j = 0; j < kN; j++)
                {
                    if (j == i) continue;
                    int near = 0;
                    foreach (float a in starts[i]) foreach (float b in starts[j]) if (Mathf.Abs(a - b) < 1f) { near++; break; }
                    worstShare = Mathf.Max(worstShare, (float)near / starts[i].Count);
                }
            }
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < kN; i++) sb.Append($" [{i}] " + string.Join(" ", starts[i].ConvertAll(x => x.ToString("F1"))));
            TestContext.Out.WriteLine($"tuck starts:{sb}; worst pair: {worstShare:P0} of the starts within 1 s; all five tucked {allTucked * kDt:F1} s of 180");
            float minFirst = float.PositiveInfinity;
            for (int i = 0; i < kN; i++) for (int j = i + 1; j < kN; j++) minFirst = Mathf.Min(minFirst, Mathf.Abs(starts[i][0] - starts[j][0]));
            TestContext.Out.WriteLine($"first tucks >= {minFirst:F2} s apart");
            Assert.Greater(minFirst, 1f, "the first tucks staggered");
            Assert.Less(worstShare, 0.4f, "no two individuals tuck together");
            Assert.Less(allTucked * kDt, 0.1f * 180f, "rarely all tucked at once");
        }

        // Zero GC through a whole tuck (scare, curl, sleep, peek, unfold), bones included.
        [Test] public void Tuck_NoGC()
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale);
            var bones = new Matrix4x4[sp.BoneCount];
            for (int t = 0; t < 120; t++) { sim.Tick(kDt, Far(sp)); sim.WriteBoneWorld(bones, 0); }
            GcAllocs.AssertCounterWorks();
            int allocs = GcAllocs.Count(() =>
            {
                for (int t = 0; t < 900; t++)
                {
                    if (t == 10) sim.Scare(1f);
                    sim.Tick(kDt, Far(sp));
                    sim.WriteBoneWorld(bones, 0);
                }
            });
            Assert.AreEqual(0, allocs);
        }

        // The posture inputs are off by default and a species without a Tuck never sets them.
        [Test] public void PostureInputs_OffByDefault()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("TentacleCrab").Compiled.Runtime;
            var sim = Walker(sp, sp.scale);
            for (int t = 0; t < 120; t++) sim.Tick(kDt, Far(sp));
            Assert.IsFalse(sim.Halt || sim.Asleep);
            Assert.AreEqual(0f, sim.Crouch);
            sim.Scare(1f);
            sim.Tick(kDt, Far(sp));
            Assert.IsFalse(sim.Halt || sim.Asleep, "no posture part: a scare changes nothing");
            Assert.AreEqual(1f, sim.TakeScare(), "the input is kept for whoever reads it");
        }

        // The lumps are within lumpHeight of the smooth loft, and a loft without lumps is exactly the smooth one.
        [Test] public void RigidLoft_Lumps_Bounded()
        {
            RigidLoft shell = Shell(Crab());
            Assert.Greater(shell.lumpHeight, 0f);
            float worst = 0f;
            for (int k = 1; k < 40; k++)
                for (int a = 0; a < 64; a++)
                {
                    RigidLoft.Section q = shell.SectionAt(k / 40f);
                    float phi = 2f * Mathf.PI * a / 64f;
                    worst = Mathf.Max(worst, (shell.Lumped(q, phi) - RigidLoft.Point(q, phi)).magnitude);
                }
            Assert.LessOrEqual(worst, shell.lumpHeight + 1e-6f);
            Assert.Greater(worst, 0.3f * shell.lumpHeight, "lumpy");
            var smooth = (RigidLoft)shell.Clone(); smooth.lumpHeight = 0f;
            RigidLoft.Section s0 = smooth.SectionAt(0.5f);
            Assert.AreEqual(RigidLoft.Point(s0, 1f), smooth.Lumped(s0, 1f));
        }
    }
}
