using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The StriderMatriarch (2026-10-20): the GolfballStrider's giant, sinister variant - species.scale 7.5 (an orb ~2.4 m
    // across, legs ~9-13 m), the knees-first stride, legs kept off the orb, and a maw of eight long Tendrils (maw mode:
    // writhe, probe the ground, lash) that keep clear of the orb and the legs. Size, gait, GC and normals are the kit's
    // generic checks (CreatureKitTests); here: the giant's size, its stride and clearance, and its tentacles - rooted on
    // the face, never inside the orb or a leg, never inverted, probing the ground and lashing - with RED proofs.
    public class CreatureStriderMatriarchTests
    {
        const string kSpecies = "StriderMatriarch";
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        static CreatureSpecies Rt() => CreatureKitAssets.Load(kSpecies).Compiled.Runtime;

        static CreatureSim Sim(CreatureSpecies rt, float scale, int seed = 1) =>
            new CreatureSim(rt, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, scale, seed);

        static List<TendrilRuntime> Maw(CreatureSim w)
        {
            var l = new List<TendrilRuntime>();
            foreach (IPartRuntime r in w.PartRuntimes) if (r is TendrilRuntime t && t.Part.mode == 1) l.Add(t);
            return l;
        }

        // ------------------------------------------------------------ the giant

        [Test] public void Matriarch_IsGiant_AndTheStriderIsNot()
        {
            CreatureSpecies rt = Rt();
            Assert.AreEqual(7.5f, rt.scale, 1e-6f);
            float orbD = 0f, legMin = float.MaxValue, legMax = 0f, maw = 0f;
            foreach (CreaturePart p in KitView.Parts(rt))
            {
                if (p is DimpledOrb o) orbD = 2f * o.radius * rt.scale;
                if (p is BendyLeg b && b.role == LimbRole.Walk) { legMin = Mathf.Min(legMin, b.length * rt.scale); legMax = Mathf.Max(legMax, b.length * rt.scale); }
                if (p is Tendril t && t.mode == 1) maw += 1f + (t.mirror ? 1f : 0f);
            }
            TestContext.Out.WriteLine($"orb {orbD:F2} m, legs {legMin:F1}..{legMax:F1} m, {maw} maw tentacles");
            Assert.That(orbD, Is.InRange(2f, 3f), "orb 2-3 m across");
            Assert.That(legMin, Is.GreaterThanOrEqualTo(8f)); Assert.That(legMax, Is.LessThanOrEqualTo(13f));
            Assert.That(maw, Is.InRange(6f, 10f), "6-10 face tentacles");
            Assert.AreEqual(1f, CreatureKitAssets.Load("GolfballStrider").Compiled.Runtime.scale, "the original stays as it was");
        }

        [Test] public void Matriarch_Stride_KneesTakeTheMotion()
        {
            CreatureSpecies rt = Rt();
            Assert.IsNull(CreatureBendyLegTests.KneeProblem(CreatureBendyLegTests.StrideRanges(rt, false, rt.scale)));
        }

        [Test] public void Matriarch_Legs_NeverInsideTheOrb() => Assert.IsNull(CreatureBendyLegTests.OrbProblem(CreatureKitAssets.Load(kSpecies), false, trash, 20f));

        // ------------------------------------------------------------ the maw

        // Every maw tendril's root rides the face: within the face cap (its half extents x 1.1) of the face's centre,
        // walking, turning and stopping.
        [Test] public void Matriarch_Tentacles_RootOnTheFace()
        {
            CreatureSpecies rt = Rt();
            SpiderFace face = null;
            foreach (CreaturePart p in KitView.Parts(rt)) if (p is SpiderFace f) face = f;
            Assert.IsNotNull(face);
            float s = rt.scale, reach = 1.1f * Mathf.Max(0.5f * face.width, Mathf.Max(0.5f * face.height, face.depth)) * s;
            CreatureSim w = Sim(rt, s);
            List<TendrilRuntime> maw = Maw(w);
            Assert.GreaterOrEqual(maw.Count, 6);
            float worst = 0f;
            for (int f = 0; f < 900; f++)
            {
                w.Tick(Mathf.Sqrt(s) / 60f, (f / 300) % 2 == 1 ? w.Joints[0] : CreatureBendyLegTests.Weave(w, f / 60f, s));
                Vector3 centre = w.Joints[face.at.joint] + w.Rotations[face.at.joint] * (face.at.offset * s);
                foreach (TendrilRuntime t in maw) worst = Mathf.Max(worst, (t.Root - centre).magnitude);
            }
            TestContext.Out.WriteLine($"roots within {worst:F3} m of the face centre (cap reach {reach:F3} m)");
            Assert.LessOrEqual(worst, reach);
        }

        // The tentacles are never still: over a minute of walking and standing every one probes, the maw lashes, and
        // the probing tips get down to the ground (within 15 % of a tentacle's length).
        [Test] public void Matriarch_Tentacles_ProbeTheGround_AndLash()
        {
            CreatureSpecies rt = Rt();
            float s = rt.scale;
            CreatureSim w = Sim(rt, s);
            List<TendrilRuntime> maw = Maw(w);
            float low = float.MaxValue;
            for (int f = 0; f < 3600; f++)
            {
                w.Tick(Mathf.Sqrt(s) / 60f, (f / 600) % 2 == 1 ? w.Joints[0] : CreatureBendyLegTests.Weave(w, f / 60f, s));
                foreach (TendrilRuntime t in maw) low = Mathf.Min(low, t.Tip.y / t.Chain.Length);
            }
            int lashes = 0;
            foreach (TendrilRuntime t in maw) { lashes += t.Lashes; Assert.GreaterOrEqual(t.Probes, 1, $"{t.Part.id} never probed"); }
            TestContext.Out.WriteLine($"lashes {lashes}, lowest tip {low:F3} x length");
            Assert.GreaterOrEqual(lashes, 2, "the maw lashes now and then");
            Assert.Less(low, 0.15f, "a probing tip gets down to the ground");
        }

        // Null, or the worst clip of a long walk with stops: a tentacle vertex (past its buried root cap) inside the orb, or a drawn tentacle
        // segment closer to a drawn leg segment than their radii. Out: the closest leg approach as a share of the radii.
        string ClipProblem(CreatureSpecies sp, out float legShare, float seconds = 40f)
        {
            CreatureSpecies rt = sp.Compiled.Runtime;
            float s = rt.scale;
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            trash.Add(mesh);
            Vector3[] v = mesh.vertices;
            var uv2 = new List<Vector4>(); mesh.GetUVs(2, uv2);
            var uv0 = new List<Vector2>(); mesh.GetUVs(0, uv0);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(rt);
            var world = new Matrix4x4[rt.BoneCount]; var skin = new Matrix4x4[rt.BoneCount];
            CreatureSim w = Sim(rt, s, 3);
            List<TendrilRuntime> maw = Maw(w);
            BendyLegSystem legs = CreatureBendyLegTests.System(w);
            DimpledOrbRuntime orb = null;
            foreach (IPartRuntime r in w.PartRuntimes) if (r is DimpledOrbRuntime o) orb = o;
            int lo = int.MaxValue, hi = 0;
            foreach (TendrilRuntime t in maw) { lo = Mathf.Min(lo, t.FirstBone); hi = Mathf.Max(hi, t.FirstBone + t.Chain.N); }
            var verts = new List<int>();
            // every tentacle vertex but the root's rounded cap (arc 0: buried in the face by design)
            for (int i = 0; i < v.Length; i++) if (uv2[i].x >= lo && uv2[i].x < hi && uv0[i].x > 1e-6f) verts.Add(i);
            float orbWorst = float.MaxValue; string orbWhere = null;
            legShare = float.MaxValue; string legWhere = null;
            int frames = Mathf.RoundToInt(seconds * 60f);
            for (int f = 0; f < frames; f++)
            {
                w.Tick(f % 97 == 0 ? 0.08f * Mathf.Sqrt(s) : Mathf.Sqrt(s) / 60f, (f / 400) % 3 == 2 ? w.Joints[0] : CreatureBendyLegTests.Weave(w, f / 60f, s));
                if (f % 3 != 0) continue;
                w.WriteBoneWorld(world, 0);
                for (int b = 0; b < skin.Length; b++) skin[b] = world[b] * bind[b];
                orb.Ellipsoid(out Vector3 c, out Quaternion rot, out Vector3 h);
                Quaternion inv = Quaternion.Inverse(rot);
                foreach (int i in verts)
                {
                    Vector4 k = uv2[i];
                    Vector3 p = k.z * skin[(int)k.x].MultiplyPoint3x4(v[i]) + k.w * skin[(int)k.y].MultiplyPoint3x4(v[i]);
                    Vector3 q = inv * (p - c);
                    float e = Mathf.Sqrt(Sq(q.x / h.x) + Sq(q.y / h.y) + Sq(q.z / h.z));
                    if (e < orbWorst)
                    {
                        orbWorst = e; orbWhere = $"frame {f}: tentacle vertex {i} (bones {k}) at {e:F3} of the orb's radius";
                        foreach (TendrilRuntime t in maw)
                            if (k.x >= t.FirstBone && k.x < t.FirstBone + t.Chain.N)
                            {
                                int j = (int)k.x - t.FirstBone;
                                Vector3 qs = inv * (t.Chain.X[j] - c), qd = inv * (t.Chain.DisplayX[j] - c);
                                orbWhere += $" {t.Part.id} {t.State} particle {j}: sim {Mathf.Sqrt(Sq(qs.x / h.x) + Sq(qs.y / h.y) + Sq(qs.z / h.z)):F3} display {Mathf.Sqrt(Sq(qd.x / h.x) + Sq(qd.y / h.y) + Sq(qd.z / h.z)):F3}";
                            }
                    }
                }
                foreach (TendrilRuntime t in maw)
                {
                    TentacleSpec spec = t.Part.Spec;
                    for (int j = 0; j + 1 < t.Chain.N; j++)
                    {
                        Vector3 a = t.Chain.DisplayX[j], b = t.Chain.DisplayX[j + 1];
                        float rt0 = spec.RadiusAt(t.Chain.Spacing / s * (j + 0.5f)) * s;
                        for (int ci = 0; ci < legs.CapsuleCount; ci++)
                        {
                            legs.Capsule(ci, out Vector3 la, out Vector3 lb, out float rl);
                            float d = SegDist(a, b, la, lb);
                            float share = d / (rt0 + rl);
                            if (share < legShare) { legShare = share; legWhere = $"frame {f}: {t.Part.id} segment {j} at {share:F2} x the radii from leg capsule {ci}"; }
                        }
                    }
                }
            }
            if (orbWorst < 1f) return orbWhere;
            if (legShare < 1f) return legWhere;
            return null;
        }

        static float Sq(float x) => x * x;

        static float SegDist(Vector3 p0, Vector3 p1, Vector3 q0, Vector3 q1)
        {
            // sampled closest approach (exact enough for a test: 16 x 16 points)
            float best = float.MaxValue;
            for (int i = 0; i <= 16; i++)
            {
                Vector3 p = Vector3.Lerp(p0, p1, i / 16f);
                Vector3 d = q1 - q0;
                float tt = d.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector3.Dot(p - q0, d) / d.sqrMagnitude) : 0f;
                best = Mathf.Min(best, (p - (q0 + d * tt)).magnitude);
            }
            return best;
        }

        [Test] public void Matriarch_Tentacles_KeepOffTheOrbAndTheLegs()
        {
            string problem = ClipProblem(CreatureKitAssets.Load(kSpecies), out float share);
            TestContext.Out.WriteLine($"closest tentacle-leg approach {share:F2} x the radii");
            Assert.IsNull(problem);
        }

        // RED: without the tentacles' clearance they hang into the legs or the orb.
        [Test] public void Matriarch_Red_NoClearance_TentaclesClip()
        {
            CreatureSpecies asset = CreatureKitAssets.LoadAsset(kSpecies);
            var old = new List<(Tendril, float)>();
            foreach (CreaturePart p in asset.parts) if (p is Tendril t) { old.Add((t, t.clearance)); t.clearance = 0f; }
            try
            {
                asset.InvalidateCompiled();
                string problem = ClipProblem(asset, out float share);
                TestContext.Out.WriteLine($"RED: {problem}");
                Assert.IsNotNull(problem);
            }
            finally { foreach (var (t, c) in old) t.clearance = c; asset.InvalidateCompiled(); }
        }

        // The tentacles' tubes never turn inside out while they writhe, probe and lash (walking, turning, standing).
        [Test] public void Matriarch_Tentacles_NoInvertedTriangles()
        {
            CreatureSpecies sp = CreatureKitAssets.Load(kSpecies), rt = sp.Compiled.Runtime;
            float s = rt.scale;
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            trash.Add(mesh);
            Vector3[] v = mesh.vertices; int[] tri = mesh.triangles;
            var uv2 = new List<Vector4>(); mesh.GetUVs(2, uv2);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(rt);
            var world = new Matrix4x4[rt.BoneCount]; var skin = new Matrix4x4[rt.BoneCount];
            var pos = new Vector3[v.Length];
            CreatureSim w = Sim(rt, s, 2);
            List<TendrilRuntime> maw = Maw(w);
            int lo = int.MaxValue;
            foreach (TendrilRuntime t in maw) lo = Mathf.Min(lo, t.FirstBone);
            int frames = 0;
            for (int f = 0; f < 2400; f++)
            {
                w.Tick(Mathf.Sqrt(s) / 60f, (f / 400) % 2 == 1 ? w.Joints[0] : CreatureBendyLegTests.Weave(w, f / 60f, s));
                if (f % 4 != 3) continue;
                frames++;
                w.WriteBoneWorld(world, 0);
                for (int b = 0; b < skin.Length; b++) skin[b] = world[b] * bind[b];
                for (int i = 0; i < pos.Length; i++)
                {
                    Vector4 k = uv2[i];
                    if (k.x < lo) continue;
                    pos[i] = k.z * skin[(int)k.x].MultiplyPoint3x4(v[i]) + k.w * skin[(int)k.y].MultiplyPoint3x4(v[i]);
                }
                for (int q = 0; q < tri.Length; q += 3)
                {
                    int i0 = tri[q], i1 = tri[q + 1], i2 = tri[q + 2];
                    if (uv2[i0].x < lo) continue;
                    Vector3 rest = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]);
                    if (rest.sqrMagnitude < 1e-20f) continue;
                    Vector3 now = Vector3.Cross(pos[i1] - pos[i0], pos[i2] - pos[i0]);
                    Vector3 want = skin[(int)uv2[i0].x].MultiplyVector(rest);
                    Assert.IsTrue(Vector3.Dot(now, want) > 0f, $"frame {f}: tentacle triangle {q / 3} (bones {uv2[i0]}) inverted");
                }
            }
            Assert.Greater(frames, 500);
        }

        // Clean normals: every vertex of the giant (orb with uneven deep pits, legs, face, maw) has a finite unit normal,
        // and the orb's normals face out of its centre.
        [Test] public void Matriarch_Mesh_CleanNormals()
        {
            CreatureSpecies sp = CreatureKitAssets.Load(kSpecies), rt = sp.Compiled.Runtime;
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            trash.Add(mesh);
            Vector3[] v = mesh.vertices, n = mesh.normals;
            var uv2 = new List<Vector4>(); mesh.GetUVs(2, uv2);
            int orbBone = -1;
            IReadOnlyList<CreaturePart> parts = KitView.Parts(rt);
            for (int i = 0; i < parts.Count; i++) if (parts[i] is DimpledOrb) orbBone = rt.Layout.Start(i);
            Vector3 c = Vector3.zero; int cn = 0;
            for (int i = 0; i < v.Length; i++) if ((int)uv2[i].x == orbBone) { c += v[i]; cn++; }
            Assert.Greater(cn, 1000);
            c /= cn;
            int bad = 0;
            for (int i = 0; i < v.Length; i++)
            {
                Assert.IsFalse(float.IsNaN(n[i].x + n[i].y + n[i].z) || float.IsInfinity(n[i].x + n[i].y + n[i].z), $"vertex {i}");
                Assert.AreEqual(1f, n[i].magnitude, 1e-3f, $"vertex {i} normal {n[i]}");
                if ((int)uv2[i].x == orbBone && Vector3.Dot(n[i], v[i] - c) <= 0f) bad++;
            }
            Assert.AreEqual(0, bad, "orb normals facing in");
        }

        // Zero GC per frame with the maw writhing, probing, lashing and keeping clear.
        [Test] public void Matriarch_NoGC()
        {
            CreatureSpecies rt = Rt();
            float s = rt.scale;
            CreatureSim w = Sim(rt, s);
            var bones = new Matrix4x4[rt.BoneCount];
            for (int f = 0; f < 240; f++) w.Tick(Mathf.Sqrt(s) / 60f, CreatureBendyLegTests.Weave(w, f / 60f, s));
            GcAllocs.AssertCounterWorks();
            int f0 = 240;
            int allocs = GcAllocs.Count(() =>
            {
                for (int f = f0; f < f0 + 600; f++)
                {
                    w.Tick(Mathf.Sqrt(s) / 60f, (f / 200) % 2 == 1 ? w.Joints[0] : CreatureBendyLegTests.Weave(w, f / 60f, s));
                    w.WriteBoneWorld(bones, 0);
                }
            });
            Assert.AreEqual(0, allocs, "allocations per frame");
        }
    }
}
