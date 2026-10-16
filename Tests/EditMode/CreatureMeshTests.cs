using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    public class CreatureMeshTests
    {
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        T Keep<T>(T o) where T : Object { trash.Add(o); return o; }

        sealed class Data
        {
            public Vector3[] v, n; public int[] t;
            public List<Vector2> uv0 = new List<Vector2>(), uv1 = new List<Vector2>();
            public List<Vector4> uv2 = new List<Vector4>();
        }

        Data Build(CreatureSpecies sp)
        {
            Mesh m = Keep(CreatureMeshBuilder.Build(sp));
            var d = new Data { v = m.vertices, n = m.normals, t = m.triangles };
            m.GetUVs(0, d.uv0); m.GetUVs(1, d.uv1); m.GetUVs(2, d.uv2);
            return d;
        }

        static IEnumerable<CreatureSpecies> AllSpecies()
        {
            yield return CreatureTestSpecies.Lizardish();
            yield return CreatureTestSpecies.Snake();
            yield return CreatureTestSpecies.Hexapod();
        }

        static int Part(Data d, int i) => Mathf.RoundToInt(d.uv1[i].x);

        // Skins the rest mesh with the given bone worlds (skin = boneWorld x bindPose).
        static Vector3 Skin(Data d, Matrix4x4[] skin, int i)
        {
            Vector4 s = d.uv2[i];
            return s.z * skin[(int)s.x].MultiplyPoint3x4(d.v[i]) + s.w * skin[(int)s.y].MultiplyPoint3x4(d.v[i]);
        }

        [Test] public void Build_Lizardish_WeightsSumToOne_IndicesInRange()
        {
            var sp = Keep(CreatureTestSpecies.Lizardish());
            var d = Build(sp);
            int nv = d.v.Length;
            Assert.Greater(nv, 0);
            Assert.AreEqual(nv, d.n.Length); Assert.AreEqual(nv, d.uv0.Count); Assert.AreEqual(nv, d.uv1.Count); Assert.AreEqual(nv, d.uv2.Count);
            for (int i = 0; i < nv; i++)
            {
                Vector4 s = d.uv2[i];
                Assert.AreEqual(1f, s.z + s.w, 1e-5f, "weights at " + i);
                Assert.GreaterOrEqual(s.z, 0f); Assert.GreaterOrEqual(s.w, 0f);
                foreach (float b in new[] { s.x, s.y })
                {
                    Assert.AreEqual(Mathf.Round(b), b, 1e-6f, "bone index not integral at " + i);
                    Assert.That(b, Is.InRange(0f, sp.BoneCount - 1f), "bone index at " + i);
                }
                Assert.AreEqual(1f, d.n[i].magnitude, 1e-3f, "normal length at " + i);
                float dorsal = d.uv1[i].y;
                Assert.That(dorsal, Is.InRange(-1f, 1f));
                Assert.That(Part(d, i), Is.InRange(CreatureMeshBuilder.PartBody, CreatureMeshBuilder.PartEye));
            }
            Assert.AreEqual(0, d.t.Length % 3);
            foreach (int ix in d.t) Assert.That(ix, Is.InRange(0, nv - 1));
        }

        [Test] public void Build_NoDegenerateTriangles()
        {
            foreach (var sp in AllSpecies())
            {
                Keep(sp);
                var d = Build(sp);
                for (int k = 0; k < d.t.Length; k += 3)
                {
                    Vector3 a = d.v[d.t[k]], b = d.v[d.t[k + 1]], c = d.v[d.t[k + 2]];
                    float area = 0.5f * Vector3.Cross(b - a, c - a).magnitude;
                    Assert.Greater(area, 1e-10f, $"{sp.JointCount}-joint species: triangle {k / 3} (part {Part(d, d.t[k])})");
                }
            }
        }

        // Body vertices come first, as consecutive rings of ringSegments + 1 vertices (seam duplicated),
        // snout tip to tail tip; the two tips are rings of identical vertices.
        [Test] public void Build_NormalsOutward()
        {
            var sp = Keep(CreatureTestSpecies.Lizardish());
            var d = Build(sp);
            int m = sp.ringSegments, per = m + 1;
            int body = 0;
            while (body < d.v.Length && Part(d, body) == CreatureMeshBuilder.PartBody) body++;
            Assert.Greater(body, per * sp.JointCount);
            Assert.AreEqual(0, body % per, "body is not whole rings");
            for (int i = body; i < d.v.Length; i++) Assert.AreNotEqual(CreatureMeshBuilder.PartBody, Part(d, i), "body vertex after the body block");
            int checkedCount = 0;
            for (int r = 0; r < body; r += per)
            {
                Vector3 c = Vector3.zero;
                for (int k = 0; k < m; k++) c += d.v[r + k];
                c /= m;
                for (int k = 0; k < per; k++)
                {
                    Vector3 off = d.v[r + k] - c;
                    if (off.magnitude < 1e-6f) continue; // tip
                    Assert.Greater(Vector3.Dot(d.n[r + k], off), 0f, $"ring {r / per} vertex {k}");
                    checkedCount++;
                }
            }
            Assert.Greater(checkedCount, sp.JointCount * m);
        }

        // The tail tip must ease out of the body's taper, not start as a cone at the last joint (a silhouette
        // kink). Measured on the ring half-widths from the last spine segment to the tip: the change of
        // taper slope between consecutive ring intervals, in units of the closing slope halfWidth/tailTipLength.
        // A cone puts the whole closing slope (1.0) at one ring; a smooth taper spreads it, about 1/3 per ring.
        [Test] public void Build_TailTaper_NoKink()
        {
            foreach (var sp in new[] { CreatureTestSpecies.Lizardish(), CreatureTestSpecies.Snake() })
            {
                Keep(sp);
                var d = Build(sp);
                int m = sp.ringSegments, per = m + 1;
                int body = 0;
                while (body < d.v.Length && Part(d, body) == CreatureMeshBuilder.PartBody) body++;
                float lastJoint = sp.headCapLength + sp.spacing * (sp.JointCount - 2); // start of the last segment
                var u = new List<float>(); var a = new List<float>();
                for (int r = 0; r + per <= body; r += per)
                {
                    Vector3 px = d.v[r + m / 4], nx = d.v[r + 3 * m / 4];   // phi = 0 (+x) and phi = pi (-x)
                    float half = 0.5f * (px - nx).magnitude;
                    if (d.uv0[r].x < lastJoint - 1e-5f || half < 1e-6f) continue; // tip pole
                    u.Add(d.uv0[r].x); a.Add(half);
                }
                Assert.Greater(u.Count, 4);
                float closing = sp.joints[sp.JointCount - 1].halfWidth / sp.tailTipLength, worst = 0f;
                for (int i = 0; i + 2 < u.Count; i++)
                {
                    float s0 = (a[i + 1] - a[i]) / (u[i + 1] - u[i]), s1 = (a[i + 2] - a[i + 1]) / (u[i + 2] - u[i + 1]);
                    worst = Mathf.Max(worst, Mathf.Abs(s1 - s0) / closing);
                }
                TestContext.Out.WriteLine($"{sp.JointCount}-joint species: worst taper-slope change per ring {worst:F2}");
                Assert.Less(worst, 0.5f, $"{sp.JointCount}-joint species: tail taper kink");
            }
        }

        [Test] public void Build_TrianglesWindOutward()
        {
            foreach (var sp in AllSpecies())
            {
                Keep(sp);
                var d = Build(sp);
                for (int k = 0; k < d.t.Length; k += 3)
                {
                    int i0 = d.t[k], i1 = d.t[k + 1], i2 = d.t[k + 2];
                    Vector3 fn = Vector3.Cross(d.v[i1] - d.v[i0], d.v[i2] - d.v[i0]);
                    Vector3 vn = d.n[i0] + d.n[i1] + d.n[i2];
                    Assert.Greater(Vector3.Dot(fn, vn), 0f, $"{sp.JointCount}-joint species: triangle {k / 3} (part {Part(d, i0)}) winds inward");
                }
            }
        }

        [Test] public void Build_Legless_HasNoLegParts()
        {
            var sp = Keep(CreatureTestSpecies.Snake());
            Assert.AreEqual(40, sp.JointCount);
            Assert.AreEqual(0, sp.legs.Length);
            var d = Build(sp);
            int eyes = 0;
            for (int i = 0; i < d.v.Length; i++)
            {
                int p = Part(d, i);
                Assert.AreNotEqual(CreatureMeshBuilder.PartLeg, p);
                Assert.AreNotEqual(CreatureMeshBuilder.PartFoot, p);
                if (p == CreatureMeshBuilder.PartEye) eyes++;
                Assert.Less(d.uv2[i].x, sp.BoneCount); Assert.Less(d.uv2[i].y, sp.BoneCount);
            }
            Assert.Greater(eyes, 0);
        }

        [Test] public void Build_Hexapod_Builds()
        {
            var sp = Keep(CreatureTestSpecies.Hexapod());
            Assert.IsNull(sp.Validate());
            var d = Build(sp);
            var legBones = new HashSet<int>(); var footBones = new HashSet<int>();
            for (int i = 0; i < d.v.Length; i++)
            {
                Assert.AreEqual(1f, d.uv2[i].z + d.uv2[i].w, 1e-5f);
                int p = Part(d, i);
                if (p == CreatureMeshBuilder.PartLeg) { legBones.Add((int)d.uv2[i].x); legBones.Add((int)d.uv2[i].y); }
                if (p == CreatureMeshBuilder.PartFoot) footBones.Add((int)d.uv2[i].x);
            }
            for (int l = 0; l < 6; l++)
            {
                Assert.IsTrue(legBones.Contains(sp.LegBone(l, 0)), "upper bone of leg " + l);
                Assert.IsTrue(legBones.Contains(sp.LegBone(l, 1)), "lower bone of leg " + l);
                Assert.IsTrue(legBones.Contains(sp.legs[l].attachJoint), "shoulder of leg " + l);
                Assert.IsTrue(footBones.Contains(sp.LegBone(l, 2)), "foot of leg " + l);
            }
        }

        [Test] public void RestPose_BindTimesWorldIsIdentity()
        {
            var sp = Keep(CreatureTestSpecies.Lizardish());
            Matrix4x4[] w = CreatureRestPose.BoneWorld(sp), b = CreatureRestPose.BindPoses(sp);
            Assert.AreEqual(sp.BoneCount, w.Length); Assert.AreEqual(sp.BoneCount, b.Length);
            for (int i = 0; i < w.Length; i++)
            {
                Matrix4x4 p = w[i] * b[i];
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 4; c++)
                        Assert.AreEqual(r == c ? 1f : 0f, p[r, c], 1e-4f, $"bone {i} [{r},{c}]");
            }
        }

        [Test] public void RestPose_LegBonesFollowConventions()
        {
            var sp = Keep(CreatureTestSpecies.Lizardish());
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward);
            Matrix4x4[] w = CreatureRestPose.BoneWorld(sp);
            for (int l = 0; l < sp.legs.Length; l++)
            {
                LegState s = sim.Legs[l];
                Matrix4x4 up = w[sp.LegBone(l, 0)], lo = w[sp.LegBone(l, 1)], ft = w[sp.LegBone(l, 2)];
                AssertNear(s.shoulder, up.GetColumn(3), "upper origin " + l);
                AssertNear((s.elbow - s.shoulder).normalized, up.rotation * Vector3.forward, "upper +Z " + l);
                AssertNear(s.elbow, lo.GetColumn(3), "lower origin " + l);
                AssertNear((s.foot - s.elbow).normalized, lo.rotation * Vector3.forward, "lower +Z " + l);
                AssertNear(s.foot, ft.GetColumn(3), "foot origin " + l);
                AssertNear(Vector3.up, ft.rotation * Vector3.up, "foot +Y " + l);
            }
        }

        static void AssertNear(Vector3 want, Vector3 got, string msg)
        {
            Assert.Less((want - got).magnitude, 1e-4f, $"{msg}: want {want:F5} got {got:F5}");
        }

        [Test] public void MeshBuilder_MaxBend_NoInvertedTriangles()
        {
            var sp = Keep(CreatureTestSpecies.Lizardish());
            MaxBendNoInvertedTriangles(sp);
        }

        // Skins the species' mesh through a 5 s minimum-radius circle: no triangle may invert. Shared
        // with the monitor tests.
        internal static void MaxBendNoInvertedTriangles(CreatureSpecies sp)
        {
            Mesh mesh = CreatureMeshBuilder.Build(sp);
            var d = new Data { v = mesh.vertices, n = mesh.normals, t = mesh.triangles };
            mesh.GetUVs(0, d.uv0); mesh.GetUVs(1, d.uv1); mesh.GetUVs(2, d.uv2);
            Object.DestroyImmediate(mesh);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            var world = new Matrix4x4[sp.BoneCount];
            var skin = new Matrix4x4[sp.BoneCount];
            var pos = new Vector3[d.v.Length];
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward);
            Vector3 centre = new Vector3(0f, 0f, 0.25f);
            float maxBend = 0f; int frames = 0;
            for (int t = 0; t < 300; t++) // 5 s, target circling at minimum radius
            {
                float a = t / 60f * 3f;
                sim.Tick(1f / 60f, centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.12f);
                if (t % 5 != 4) continue;
                for (int i = 1; i < sp.JointCount - 1; i++)
                    maxBend = Mathf.Max(maxBend, Vector3.Angle(sim.Joints[i - 1] - sim.Joints[i], sim.Joints[i] - sim.Joints[i + 1]));
                sim.WriteBoneWorld(world, 0);
                for (int b = 0; b < skin.Length; b++) skin[b] = world[b] * bind[b];
                for (int i = 0; i < pos.Length; i++) pos[i] = Skin(d, skin, i);
                for (int k = 0; k < d.t.Length; k += 3)
                {
                    int i0 = d.t[k], i1 = d.t[k + 1], i2 = d.t[k + 2];
                    Vector3 rest = Vector3.Cross(d.v[i1] - d.v[i0], d.v[i2] - d.v[i0]);
                    Vector3 now = Vector3.Cross(pos[i1] - pos[i0], pos[i2] - pos[i0]);
                    Vector3 want = skin[(int)d.uv2[i0].x].MultiplyVector(rest);
                    Assert.Greater(Vector3.Dot(now, want), 0f, $"t={t}: triangle {k / 3} (part {Part(d, i0)}, bones {d.uv2[i0]}) inverted; {Knees(sim)}");
                }
                frames++;
            }
            TestContext.Out.WriteLine($"max joint bend {maxBend:F1} deg over {frames} frames");
            Assert.Greater(maxBend, 0.9f * 20f, "the chain never reached its bend limit: test does not bite");
        }

        static string Knees(CreatureSim sim)
        {
            var sb = new System.Text.StringBuilder("knee bend deg:");
            for (int l = 0; l < sim.Legs.Length; l++)
            {
                LegState s = sim.Legs[l];
                sb.Append($" L{l}={Vector3.Angle(s.elbow - s.shoulder, s.foot - s.elbow):F0}");
            }
            return sb.ToString();
        }

        [Test] public void Build_Time_Under5ms()
        {
            var sp = Keep(CreatureTestSpecies.Lizardish());
            Object.DestroyImmediate(CreatureMeshBuilder.Build(sp)); // warm-up (JIT)
            var ms = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                var sw = Stopwatch.StartNew();
                Mesh m = CreatureMeshBuilder.Build(sp);
                sw.Stop();
                ms.Add(sw.Elapsed.TotalMilliseconds);
                Object.DestroyImmediate(m);
            }
            ms.Sort();
            TestContext.Out.WriteLine($"build ms: {string.Join(", ", ms)}");
            Assert.Less(ms[2], 5.0, "median build time");
        }
    }
}
