using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The kit's BendyLeg part (2026-10-20, the GolfballStrider's legs): a long thin limb whose arthropod segments are
    // GENERATED from a few numbers (length, segment count, rise, arch, knee spread, seeded kinks and length jitter) and
    // which flexes and wobbles on its own (BendyLegSystem): validation, the generator's invariants, the IK model it
    // makes, the rest reach helper, its mesh, and its runtime (rest = the IK's bones, the drawn tip stays on a planted
    // foot, the wobble is bounded and settles, Froude, no GC) with RED proofs.
    public class CreatureBendyLegTests
    {
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        static SpeciesCtx Ctx(int joints) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, false);

        internal static BendyLeg Ok() => new BendyLeg
        {
            id = "leg.L", at = new PartSocket(0, -1, new Vector3(0.02f, -0.01f, 0.01f)), restOffset = new Vector3(0.9f, 0f, 0.4f),
            stepThreshold = 0.12f, stepDuration = 0.3f, stepLift = 0.05f, segmentCount = 7, length = 1.4f, rootRadius = 0.007f, tipRadius = 0.002f,
            riseDeg = 60f, archDeg = 120f, kneeAt = 0.35f, kneeSpread = 0.3f, kinkDeg = 12f, lengthJitter = 0.3f, seed = 3,
        };

        // ------------------------------------------------------------ data and the generator

        [Test] public void BendyLeg_Validate_RejectsBadData()
        {
            Assert.IsNull(Ok().Validate(Ctx(4)));
            BendyLeg bad;
            bad = (BendyLeg)Ok().Clone(); bad.segmentCount = 2; Assert.IsNotNull(bad.Validate(Ctx(4)), "2 segments");
            bad = (BendyLeg)Ok().Clone(); bad.segmentCount = 8; Assert.IsNotNull(bad.Validate(Ctx(4)), "8 segments (3..7, as an ArthropodLeg)");
            bad = (BendyLeg)Ok().Clone(); bad.length = 0f; Assert.IsNotNull(bad.Validate(Ctx(4)), "no length");
            bad = (BendyLeg)Ok().Clone(); bad.rootRadius = 0f; Assert.IsNotNull(bad.Validate(Ctx(4)), "no radius");
            bad = (BendyLeg)Ok().Clone(); bad.tarsusShare = 0.7f; Assert.IsNotNull(bad.Validate(Ctx(4)), "tarsus most of the leg");
            bad = (BendyLeg)Ok().Clone(); bad.archDeg = 330f; Assert.IsNotNull(bad.Validate(Ctx(4)), "an arch that curls past itself is not a monotone reach");
            bad = (BendyLeg)Ok().Clone(); bad.wobbleDamping = -1f; Assert.IsNotNull(bad.Validate(Ctx(4)), "negative damping");
            bad = (BendyLeg)Ok().Clone(); bad.whip = 1.5f; Assert.IsNotNull(bad.Validate(Ctx(4)), "whip > 1");
            Assert.AreEqual(7, Ok().BoneCount(Ctx(4)), "one bone per segment");
        }

        [Test] public void BendyLeg_Segments_SumToLength_TarsusShare()
        {
            foreach (int n in new[] { 3, 5, 7 })
            {
                BendyLeg p = Ok(); p.segmentCount = n;
                LegSpec l = p.ToLegSpec();
                Assert.IsTrue(l.IsArthropod);
                Assert.AreEqual(n, l.segments.Length);
                float sum = 0f;
                foreach (LegSegment s in l.segments) { sum += s.length; Assert.Greater(s.length, 0f); }
                Assert.AreEqual(p.length, sum, 1e-5f, "total length");
                Assert.AreEqual(p.tarsusShare * p.length, l.segments[n - 1].length, 1e-5f, "tarsus");
                Assert.AreEqual(p.rootRadius, l.segments[0].radiusStart, 1e-6f);
                Assert.AreEqual(p.tipRadius, l.segments[n - 1].radiusEnd, 1e-6f);
            }
        }

        // The solved segments turn by exactly archDeg from the rising root (kinks are zero-sum), whatever the seed.
        [Test] public void BendyLeg_Segments_TurnByTheArch()
        {
            foreach (int seed in new[] { 0, 1, 7, 99 })
            {
                BendyLeg p = Ok(); p.seed = seed;
                LegSegment[] s = p.ToLegSpec().segments;
                Assert.AreEqual(p.riseDeg, s[0].restBendDeg, 1e-5f, "root elevation");
                float turn = 0f;
                for (int i = 1; i < s.Length - 1; i++) turn += s[i].restBendDeg;
                Assert.AreEqual(-p.archDeg, turn, 1e-3f, $"seed {seed}: total turn");
            }
        }

        [Test] public void BendyLeg_Kinks_SeededAndIrregular()
        {
            LegSegment[] a = Ok().ToLegSpec().segments, b = Ok().ToLegSpec().segments;
            for (int i = 0; i < a.Length; i++) { Assert.AreEqual(a[i].restBendDeg, b[i].restBendDeg); Assert.AreEqual(a[i].length, b[i].length); }
            BendyLeg o = Ok(); o.seed = 4;
            LegSegment[] c = o.ToLegSpec().segments;
            bool differs = false;
            for (int i = 0; i < a.Length; i++) differs |= a[i].restBendDeg != c[i].restBendDeg || a[i].length != c[i].length;
            Assert.IsTrue(differs, "another seed, another leg");
            // no kinks, no jitter: solved segments equal, bends follow the smooth knee profile (one sign)
            BendyLeg plain = Ok(); plain.kinkDeg = 0f; plain.lengthJitter = 0f;
            LegSegment[] d = plain.ToLegSpec().segments;
            for (int i = 1; i < d.Length - 1; i++)
            {
                Assert.AreEqual(d[0].length, d[i].length, 1e-5f, $"segment {i} length");
                Assert.LessOrEqual(d[i].restBendDeg, 0f, $"joint {i} bends down");
            }
        }

        // Every generated leg makes a valid synergy model (monotone reach around the rest pose) over the authoring range.
        [Test] public void BendyLeg_Model_ValidOverTheRange()
        {
            foreach (int n in new[] { 3, 4, 6, 7 })
                foreach (float arch in new[] { 40f, 90f, 150f })
                    foreach (float spread in new[] { 0.1f, 0.5f, 1f })
                        foreach (int seed in new[] { 0, 5 })
                        {
                            BendyLeg p = Ok(); p.segmentCount = n; p.archDeg = arch; p.kneeSpread = spread; p.seed = seed;
                            LegSpec l = p.ToLegSpec();
                            var m = new ArthropodLegModel(l.segments, l.ModelParams);
                            Assert.IsNull(m.Error, $"n {n} arch {arch} spread {spread} seed {seed}: {m.Error}");
                            Assert.IsNull(p.Validate(Ctx(4)));
                        }
        }

        // RestReach: a foot placed at the returned horizontal distance below the coxa solves to the rest shape (s = 0).
        [Test] public void BendyLeg_RestReach_SolvesToTheRestShape()
        {
            foreach (float drop in new[] { 0.3f, 0.5f, 0.7f })
            {
                BendyLeg p = Ok();
                LegSpec l = p.ToLegSpec();
                var m = new ArthropodLegModel(l.segments, l.ModelParams);
                float reach = p.RestReach(drop);
                Assert.Greater(reach, 0f);
                var pts = new Vector3[m.PointCount];
                ArthropodLegIK.Solve(m, Vector3.zero, new Vector3(reach, -drop, 0f), Vector3.up, Vector3.up, Vector3.right, 0f, 1f, pts, 0, out ArthropodLegPose pose);
                Assert.IsFalse(pose.clamped, $"drop {drop}");
                Assert.AreEqual(0f, pose.s, 0.5f, $"drop {drop}: s at the rest reach");
            }
        }

        // ------------------------------------------------------------ the mesh (on the GolfballStrider)

        internal const string kSpecies = "GolfballStrider";

        sealed class MeshData
        {
            public Vector3[] v, n; public int[] t;
            public List<Vector2> uv0 = new List<Vector2>(), uv1 = new List<Vector2>(); public List<Vector4> uv2 = new List<Vector4>();
        }

        MeshData Mesh(CreatureSpecies sp)
        {
            Mesh m = CreatureKitAssets.BuildMesh(sp);
            trash.Add(m);
            var d = new MeshData { v = m.vertices, n = m.normals, t = m.triangles };
            m.GetUVs(0, d.uv0); m.GetUVs(1, d.uv1); m.GetUVs(2, d.uv2);
            return d;
        }

        // leg l's bone range, or -1 when the vertex's heavier bone is not a leg bone
        static int LegOf(CreatureSpecies rt, int bone)
        {
            for (int l = 0; l < rt.legs.Length; l++)
                if (bone >= rt.LegBone(l, 0) && bone < rt.LegBone(l, 0) + rt.legs[l].SegmentCount) return l;
            return -1;
        }

        // Leg vertices (part 1 on a leg bone) face away from their ring's centre: rings are the vertices of one leg at
        // one arc length (uv0.x), so the check follows the rounded joints exactly. Null = fine.
        static string OutwardProblem(CreatureSpecies sp, MeshData d, float minShare)
        {
            CreatureSpecies rt = sp.Compiled.Runtime;
            var sum = new Dictionary<long, Vector3>(); var cnt = new Dictionary<long, int>();
            var key = new long[d.v.Length];
            for (int i = 0; i < d.v.Length; i++)
            {
                key[i] = -1;
                if (Mathf.RoundToInt(d.uv1[i].x) != PartMeshEmitter.PartLeg) continue;
                int l = LegOf(rt, (int)d.uv2[i].x);
                if (l < 0) continue;
                key[i] = l * 100000000L + Mathf.RoundToInt(d.uv0[i].x * 1e5f);
                sum.TryGetValue(key[i], out Vector3 c); sum[key[i]] = c + d.v[i];
                cnt.TryGetValue(key[i], out int k); cnt[key[i]] = k + 1;
            }
            int bad = 0, total = 0;
            for (int i = 0; i < d.v.Length; i++)
            {
                if (key[i] < 0 || cnt[key[i]] < 3) continue;   // poles
                Vector3 away = d.v[i] - sum[key[i]] / cnt[key[i]];
                if (away.sqrMagnitude < 1e-12f) continue;
                total++;
                if (Vector3.Dot(d.n[i], away) <= 0f) bad++;
            }
            if (total < 1000) return $"only {total} leg vertices: the check does not bite";
            float share = 1f - (float)bad / total;
            return share >= minShare ? null : $"{bad} of {total} leg normals face into the leg ({share:P1} outward < {minShare:P0})";
        }

        [Test] public void BendyLeg_Mesh_FiniteUnitNormals_FacingOut()
        {
            CreatureSpecies sp = CreatureKitAssets.Load(kSpecies);
            MeshData d = Mesh(sp);
            for (int i = 0; i < d.v.Length; i++)
            {
                Vector3 p = d.v[i], n = d.n[i];
                Assert.IsFalse(float.IsNaN(p.x + p.y + p.z) || float.IsInfinity(p.x + p.y + p.z), $"vertex {i} not finite");
                Assert.AreEqual(1f, n.magnitude, 1e-3f, $"vertex {i} normal {n}");
            }
            Assert.IsNull(OutwardProblem(sp, d, 0.995f));
        }

        // RED: the same mesh with its normals turned inward fails the outward check.
        [Test] public void BendyLeg_Red_InwardNormalsAreCaught()
        {
            CreatureSpecies sp = CreatureKitAssets.Load(kSpecies);
            MeshData d = Mesh(sp);
            for (int i = 0; i < d.n.Length; i++) d.n[i] = -d.n[i];
            Assert.IsNotNull(OutwardProblem(sp, d, 0.995f));
        }

        // Every leg bone carries skin; with smooth > 0 the tube blends across each joint (weights split between the two
        // segments' bones), with smooth 0 every leg vertex is rigid to one bone.
        [Test] public void BendyLeg_Mesh_BlendsAcrossJoints_OnlyWhenSmooth()
        {
            CreatureSpecies sp = CreatureKitAssets.Load(kSpecies);
            CreatureSpecies rt = sp.Compiled.Runtime;
            MeshData d = Mesh(sp);
            var used = new HashSet<int>();
            var blendedAt = new HashSet<int>();
            for (int i = 0; i < d.v.Length; i++)
            {
                Vector4 s = d.uv2[i];
                if (LegOf(rt, (int)s.x) < 0) continue;
                used.Add((int)s.x);
                if (s.w > 0.05f) { Assert.AreEqual(1, Mathf.Abs((int)s.x - (int)s.y), $"vertex {i} blends non-neighbouring bones {s}"); blendedAt.Add(Mathf.Max((int)s.x, (int)s.y)); }
            }
            int joints = 0;
            for (int l = 0; l < rt.legs.Length; l++)
                for (int k = 0; k < rt.legs[l].SegmentCount; k++)
                {
                    Assert.IsTrue(used.Contains(rt.LegBone(l, k)), $"leg {l} bone {k} unskinned");
                    if (k > 0) { joints++; Assert.IsTrue(blendedAt.Contains(rt.LegBone(l, k)), $"leg {l} joint {k} not blended"); }
                }
            Assert.Greater(joints, 40);

            CreatureSpecies asset = CreatureKitAssets.LoadAsset(kSpecies);
            var old = new List<(BendyLeg, float)>();
            foreach (CreaturePart p in asset.parts) if (p is BendyLeg b) { old.Add((b, b.smooth)); b.smooth = 0f; }
            try
            {
                asset.InvalidateCompiled();
                MeshData r = Mesh(asset);
                CreatureSpecies rr = asset.Compiled.Runtime;
                for (int i = 0; i < r.v.Length; i++)
                    if (LegOf(rr, (int)r.uv2[i].x) >= 0) Assert.AreEqual(0f, r.uv2[i].w, $"vertex {i}: smooth 0 is rigid");
            }
            finally
            {
                foreach ((BendyLeg b, float s) in old) b.smooth = s;
                asset.InvalidateCompiled();
            }
        }

        // ------------------------------------------------------------ the runtime (BendyLegSystem)

        internal static CreatureSim Sim(CreatureSpecies rt, float scale = 1f, int seed = 1) =>
            new CreatureSim(rt, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, scale, seed);

        internal static BendyLegSystem System(CreatureSim w)
        {
            foreach (IPartRuntime r in w.PartRuntimes) if (r is BendyLegSystem b) return b;
            return null;
        }

        // A target that weaves: ahead, swinging left and right every few seconds (turns and speed changes shake the legs).
        internal static Vector3 Weave(CreatureSim w, float t, float s) =>
            w.Joints[0] + Quaternion.AngleAxis(45f * Mathf.Sin(0.7f * t), Vector3.up) * (Vector3.forward * (2f * s));

        // One system for every BendyLeg; before any step the bones are exactly the IK's (the rest and bind poses).
        [Test] public void BendyLeg_Runtime_OneSystem_RestBonesAreTheIKs()
        {
            CreatureSpecies rt = CreatureKitAssets.Load(kSpecies).Compiled.Runtime;
            CreatureSim w = Sim(rt);
            int systems = 0;
            foreach (IPartRuntime r in w.PartRuntimes) if (r is BendyLegSystem) systems++;
            Assert.AreEqual(1, systems);
            Assert.AreEqual(rt.legs.Length, System(w).LegCount, "every leg of the strider is a BendyLeg");
            var bones = new Matrix4x4[rt.BoneCount];
            w.WriteBoneWorld(bones, 0);
            for (int l = 0; l < rt.legs.Length; l++)
            {
                var ik = new Matrix4x4[rt.BoneCount];
                ArthropodLegIK.WriteBones(ik, rt.LegBone(l, 0), w.LegPoints, w.LegPointOffset(l), rt.legs[l].SegmentCount, w.LegPlaneNormal(l), 1f);
                for (int k = 0; k < rt.legs[l].SegmentCount; k++)
                    for (int e = 0; e < 16; e++) Assert.AreEqual(ik[rt.LegBone(l, k)][e], bones[rt.LegBone(l, k)][e], 1e-6f, $"leg {l} bone {k}");
            }
        }

        // The drawn tip of the last segment: the bone's local (0, 0, rest tarsus length).
        internal static Vector3 DrawnTip(CreatureSpecies rt, Matrix4x4[] bones, int l)
        {
            int n = rt.legs[l].SegmentCount;
            return bones[rt.LegBone(l, n - 1)].MultiplyPoint3x4(new Vector3(0f, 0f, rt.legs[l].segments[n - 1].length));
        }

        // Gait for a bendy leg: the sim's planted feet never move (the kit's Gait check); here the DRAWN tip of every
        // planted leg is exactly on its planted foot while the joints flex (the deviation is real: > 2 mm somewhere).
        // Returns the largest planted tip error (m) and the largest joint deviation.
        static void WalkTips(CreatureSpecies rt, float seconds, out float tipErr, out float maxDev, out string where)
        {
            CreatureSim w = Sim(rt);
            BendyLegSystem sys = System(w);
            var bones = new Matrix4x4[rt.BoneCount];
            tipErr = 0f; maxDev = 0f; where = "";
            int frames = Mathf.RoundToInt(seconds * 60f);
            for (int f = 0; f < frames; f++)
            {
                w.Tick(1f / 60f, Weave(w, f / 60f, 1f));
                w.WriteBoneWorld(bones, 0);
                for (int i = 0; i < sys.LegCount; i++)
                {
                    int l = sys.SimLeg(i);
                    for (int k = 1; k + 1 < sys.PointCount(i); k++) maxDev = Mathf.Max(maxDev, sys.Deviation(i, k).magnitude);
                    if (rt.legs[l].feeler || w.Legs[l].stepping) continue;
                    float e = (DrawnTip(rt, bones, l) - w.Legs[l].foot).magnitude;
                    if (e > tipErr) { tipErr = e; where = $"leg {l} frame {f}"; }
                }
            }
        }

        [Test] public void BendyLeg_DrawnTip_StaysOnPlantedFoot()
        {
            CreatureSpecies rt = CreatureKitAssets.Load(kSpecies).Compiled.Runtime;
            WalkTips(rt, 8f, out float tipErr, out float maxDev, out string where);
            Assert.Less(tipErr, 2e-4f, $"a planted tip is drawn {tipErr * 1000f:F2} mm off its foot ({where})");
            Assert.Greater(maxDev, 0.002f, "the legs flex");
        }

        // RED: unpinned planted tips (whip applied on the ground) are caught.
        [Test] public void BendyLeg_Red_UnpinnedTipSlides()
        {
            CreatureSpecies rt = CreatureKitAssets.Load(kSpecies).Compiled.Runtime;
            BendyLegSystem.UnpinForTest = true;
            try
            {
                WalkTips(rt, 8f, out float tipErr, out _, out _);
                Assert.Greater(tipErr, 2e-4f, "an unpinned tip drifts off its foot");
            }
            finally { BendyLegSystem.UnpinForTest = false; }
        }

        // The wobble is bounded (maxDeflect x length x s) even on long ticks, and settles when the strider stops: the
        // flex comes to rest (the deviation from the IK pose stops changing; the sag stays) within a few seconds.
        [Test] public void BendyLeg_Wobble_BoundedAndSettles()
        {
            CreatureSpecies rt = CreatureKitAssets.Load(kSpecies).Compiled.Runtime;
            CreatureSim w = Sim(rt);
            BendyLegSystem sys = System(w);
            float bound = 0f;
            foreach (CreaturePart p in KitView.Parts(rt)) if (p is BendyLeg b) bound = Mathf.Max(bound, b.maxDeflect * b.length);
            for (int f = 0; f < 600; f++)
            {
                w.Tick(f % 50 == 0 ? 0.1f : 1f / 60f, Weave(w, f / 60f, 1f));   // with a long tick now and then
                for (int i = 0; i < sys.LegCount; i++)
                    for (int k = 0; k < sys.PointCount(i); k++) Assert.LessOrEqual(sys.Deviation(i, k).magnitude, bound + 1e-4f, $"frame {f} leg {i} point {k}");
            }
            w.Halt = true;   // comes to rest where it stands
            Vector3 stop = w.Joints[0];
            for (int f = 0; f < 600; f++) w.Tick(1f / 60f, stop);
            var before = new List<Vector3>();
            for (int i = 0; i < sys.LegCount; i++) for (int k = 0; k < sys.PointCount(i); k++) before.Add(sys.Deviation(i, k));
            w.Tick(1f / 60f, stop);
            int q = 0;
            for (int i = 0; i < sys.LegCount; i++)
                for (int k = 0; k < sys.PointCount(i); k++)
                    Assert.Less((sys.Deviation(i, k) - before[q++]).magnitude, 2e-5f, $"leg {i} point {k} still wobbles after 10 s at rest");
        }

        // The kit's MaxBend walks a tight circle; a flexing leg is also checked on a long weave with starts, stops and
        // long ticks: no skinned leg triangle ever turns over (the rounded joints and the foot fade hold).
        [Test] public void BendyLeg_NoInvertedTriangles_Weaving() => Assert.IsNull(InvertedProblem(CreatureKitAssets.Load(kSpecies)));

        // RED: wild flex (heavy inertia, no damping, big deflections, a loose tip) folds the thin tube over.
        [Test] public void BendyLeg_Red_WildFlexInverts()
        {
            CreatureSpecies asset = CreatureKitAssets.LoadAsset(kSpecies);
            var old = new List<(BendyLeg, float, float, float, float)>();
            foreach (CreaturePart p in asset.parts) if (p is BendyLeg b) { old.Add((b, b.inertia, b.wobbleDamping, b.maxDeflect, b.wobbleHz)); b.inertia = 3f; b.wobbleDamping = 0f; b.maxDeflect = 0.5f; b.wobbleHz = 1f; }
            try { asset.InvalidateCompiled(); Assert.IsNotNull(InvertedProblem(asset)); }
            finally { foreach (var (b, i, d, m, h) in old) { b.inertia = i; b.wobbleDamping = d; b.maxDeflect = m; b.wobbleHz = h; } asset.InvalidateCompiled(); }
        }

        // Null, or the first skinned leg triangle that turns over on a 20 s weave with stops and long ticks.
        string InvertedProblem(CreatureSpecies sp)
        {
            CreatureSpecies rt = sp.Compiled.Runtime;
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            trash.Add(mesh);
            Vector3[] v = mesh.vertices; int[] tri = mesh.triangles;
            var uv1 = new List<Vector2>(); var uv2 = new List<Vector4>();
            mesh.GetUVs(1, uv1); mesh.GetUVs(2, uv2);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(rt);
            var world = new Matrix4x4[rt.BoneCount]; var skin = new Matrix4x4[rt.BoneCount];
            var pos = new Vector3[v.Length];
            CreatureSim w = Sim(rt);
            int checkedFrames = 0;
            for (int f = 0; f < 1200; f++)
            {
                bool stop = (f / 240) % 2 == 1;
                w.Tick(f % 97 == 0 ? 0.08f : 1f / 60f, stop ? w.Joints[0] : Weave(w, f / 60f, 1f));
                if (f % 3 != 0) continue;
                w.WriteBoneWorld(world, 0);
                for (int b = 0; b < skin.Length; b++) skin[b] = world[b] * bind[b];
                for (int i = 0; i < pos.Length; i++)
                {
                    Vector4 s = uv2[i];
                    pos[i] = s.z * skin[(int)s.x].MultiplyPoint3x4(v[i]) + s.w * skin[(int)s.y].MultiplyPoint3x4(v[i]);
                }
                for (int k = 0; k < tri.Length; k += 3)
                {
                    int i0 = tri[k], i1 = tri[k + 1], i2 = tri[k + 2];
                    if (Mathf.RoundToInt(uv1[i0].x) != PartMeshEmitter.PartLeg) continue;
                    Vector3 rest = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]);
                    if (rest.sqrMagnitude < 1e-20f) continue;   // a pole's sliver
                    Vector3 now = Vector3.Cross(pos[i1] - pos[i0], pos[i2] - pos[i0]);
                    Vector3 want = skin[(int)uv2[i0].x].MultiplyVector(rest);
                    if (!(Vector3.Dot(now, want) > 0f)) return $"frame {f}: leg triangle {k / 3} (bones {uv2[i0]}) inverted";
                }
                checkedFrames++;
            }
            return checkedFrames > 300 ? null : "too few frames checked";
        }

        // Froude: at 0.9x and 1.1x, ticked at dt x sqrt(s), the flex is the same motion scaled by s.
        [Test] public void BendyLeg_Wobble_FroudeSimilar()
        {
            CreatureSpecies rt = CreatureKitAssets.Load(kSpecies).Compiled.Runtime;
            float[] peak = new float[2];
            float[] scales = { 0.9f, 1.1f };
            for (int c = 0; c < 2; c++)
            {
                float s = scales[c];
                CreatureSim w = Sim(rt, s);
                BendyLegSystem sys = System(w);
                for (int f = 0; f < 300; f++)
                {
                    w.Tick(Mathf.Sqrt(s) / 60f, w.Joints[0] + Vector3.forward * (2f * s));
                    for (int i = 0; i < sys.LegCount; i++)
                        for (int k = 1; k + 1 < sys.PointCount(i); k++) peak[c] = Mathf.Max(peak[c], sys.Deviation(i, k).magnitude / s);
                }
            }
            Assert.Greater(peak[0], 1e-3f);
            Assert.AreEqual(peak[0], peak[1], 0.02f * peak[0], "normalised peak flex at 0.9x vs 1.1x");
        }
    }
}
