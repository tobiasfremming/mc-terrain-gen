using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Tree octopus phase A, task 4: OctopusMeshBuilder + the bone-pose writer. Everything but the Unity Mesh
    // timing test works on the builder's raw arrays (engine-call free).
    public class OctopusMeshTests
    {
        static int Part(OctopusMeshBuilder.Data d, int i) => Mathf.RoundToInt(d.uv1[i].x);

        // The exact skin: (1 - e2 - e3) (w0 M0 + w1 M1) + e2 M2 + e3 M3 (uv3 carries the web's extra bones).
        // twoBone: what a shader that ignores uv3 draws.
        // bind given: arm tube vertices take the curve skin as the octopus shader draws them (OctopusArmSkin).
        internal static Vector3 Skin(OctopusMeshBuilder.Data d, Matrix4x4[] skin, int i, bool twoBone = false, Matrix4x4[] bind = null)
        {
            Vector4 s = d.uv2[i], e = d.uv3[i];
            Vector3 p = d.pos[i];
            if (bind != null && !twoBone && OctopusArmSkin.Takes(d.uv1[i], s, e))
            {
                int be = (int)Mathf.Min(s.x, s.y), bl = (int)Mathf.Max(s.x, s.y);
                float t = bl == (int)s.x ? s.z : s.w;
                return OctopusArmSkin.Skin(skin[be], skin[bl], bind[be], bind[bl], p, d.nrm[i], t, e.x, out _);
            }
            Vector3 two = OctoMath.MulPoint(skin[(int)s.x], p) * s.z + OctoMath.MulPoint(skin[(int)s.y], p) * s.w;
            if (twoBone || (e.z == 0f && e.w == 0f)) return two;
            return two * (1f - e.z - e.w) + OctoMath.MulPoint(skin[(int)e.x], p) * e.z + OctoMath.MulPoint(skin[(int)e.y], p) * e.w;
        }

        static Matrix4x4[] SkinMatrices(OctopusBody body, Matrix4x4[] bind)
        {
            var world = new Matrix4x4[body.BoneCount];
            body.WriteBoneWorld(world, 0);
            for (int b = 0; b < world.Length; b++) world[b] = OctoMath.MulAffine(world[b], bind[b]);
            return world;
        }

        [Test] public void Mesh_Octopus_Builds()
        {
            var sp = CephalopodSpec.Default;
            OctopusMeshBuilder.Data d = OctopusMeshBuilder.BuildData(sp);
            int nv = d.pos.Count;
            TestContext.Out.WriteLine($"{nv} vertices, {d.tris.Count / 3} triangles, {d.BoneCount} bones");
            Assert.AreEqual(201, OctopusBody.BoneCountFor(sp), "bone layout: 8 x 24 arm + 6 soft + 2 eyes + 1 siphon");
            Assert.AreEqual(201, d.BoneCount);
            Assert.Greater(nv, 0);
            Assert.Less(nv, 15000, "vertex budget");
            Assert.AreEqual(nv, d.uv3.Count);
            Assert.AreEqual(nv, d.nrm.Count); Assert.AreEqual(nv, d.uv0.Count); Assert.AreEqual(nv, d.uv1.Count); Assert.AreEqual(nv, d.uv2.Count);
            var used = new HashSet<int>();
            var parts = new HashSet<int>();
            for (int i = 0; i < nv; i++)
            {
                Vector4 s = d.uv2[i];
                Assert.AreEqual(1f, s.z + s.w, 1e-5f, "weights at " + i);
                Assert.GreaterOrEqual(s.z, s.w, "bone0 must be the heavier bone at " + i);
                Assert.GreaterOrEqual(s.w, 0f);
                foreach (float b in new[] { s.x, s.y })
                {
                    Assert.AreEqual(Mathf.Round(b), b, 1e-6f, "bone index not integral at " + i);
                    Assert.That(b, Is.InRange(0f, d.BoneCount - 1f), "bone index at " + i);
                }
                used.Add((int)s.x); if (s.w > 0f) used.Add((int)s.y);
                Vector4 e = d.uv3[i];
                Assert.GreaterOrEqual(e.z, 0f); Assert.GreaterOrEqual(e.w, 0f);
                Assert.LessOrEqual(e.z + e.w, 0.75f + 1e-5f, "extra weights cannot outweigh the main pair at " + i);
                if (e.z > 0f || e.w > 0f)
                {
                    Assert.AreEqual(OctopusMeshBuilder.PartWeb, Part(d, i), "only the web uses four bones");
                    Assert.That(e.x, Is.InRange(0f, d.BoneCount - 1f)); Assert.That(e.y, Is.InRange(0f, d.BoneCount - 1f));
                }
                Assert.AreEqual(1f, d.nrm[i].magnitude, 1e-3f, "normal length at " + i);
                Assert.That(d.uv1[i].y, Is.InRange(-1f, 1f));
                parts.Add(Part(d, i));
            }
            foreach (int p in new[] { OctopusMeshBuilder.PartEye, OctopusMeshBuilder.PartArm, OctopusMeshBuilder.PartWeb, OctopusMeshBuilder.PartMantle, OctopusMeshBuilder.PartSiphon })
                Assert.IsTrue(parts.Contains(p), "part " + p + " missing");
            for (int b = 0; b < d.BoneCount; b++) Assert.IsTrue(used.Contains(b), "bone " + b + " skins no vertex");
            Assert.AreEqual(0, d.tris.Count % 3);
            foreach (int ix in d.tris) Assert.That(ix, Is.InRange(0, nv - 1));
        }

        [Test] public void Mesh_Octopus_TrianglesWindOutward_NoDegenerates()
        {
            OctopusMeshBuilder.Data d = OctopusMeshBuilder.BuildData(CephalopodSpec.Default);
            for (int k = 0; k < d.tris.Count; k += 3)
            {
                int i0 = d.tris[k], i1 = d.tris[k + 1], i2 = d.tris[k + 2];
                Vector3 fn = Vector3.Cross(d.pos[i1] - d.pos[i0], d.pos[i2] - d.pos[i0]);
                Assert.Greater(fn.magnitude, 1e-11f, $"degenerate triangle {k / 3} (part {Part(d, i0)})");
                Assert.Greater(Vector3.Dot(fn, d.nrm[i0] + d.nrm[i1] + d.nrm[i2]), 0f, $"triangle {k / 3} (part {Part(d, i0)}) winds inward");
            }
        }

        // Closed tubes: arm normals point away from the arm's centre line (an arm ring is convex).
        [Test] public void Mesh_Octopus_ArmNormalsOutward()
        {
            var sp = CephalopodSpec.Default;
            OctopusMeshBuilder.Data d = OctopusMeshBuilder.BuildData(sp);
            int checkedCount = 0;
            for (int i = 0; i < d.pos.Count; i++)
            {
                if (Part(d, i) != OctopusMeshBuilder.PartArm) continue;
                float s = d.uv0[i].x;
                if (s <= 0f || s >= sp.arm.Length) continue;
                int a = ArmOf(sp, d.pos[i]);
                Vector3 c = OctopusShape.ArmRoot(sp, a) + OctopusShape.ArmDir(sp, a) * s;
                Vector3 off = d.pos[i] - c;
                Assert.Greater(Vector3.Dot(d.nrm[i], off), 0f, $"arm {a} vertex {i} at s {s:F3}");
                checkedCount++;
            }
            Assert.Greater(checkedCount, 1000);
        }

        static int ArmOf(in CephalopodSpec sp, Vector3 p)
        {
            int best = 0; float bd = -2f;
            for (int a = 0; a < sp.ArmCount; a++)
            {
                float dd = Vector3.Dot(new Vector3(p.x, 0f, p.z).normalized, OctopusShape.ArmDir(sp, a));
                if (dd > bd) { bd = dd; best = a; }
            }
            return best;
        }

        [Test] public void Octopus_RestPose_BindTimesWorldIsIdentity()
        {
            var sp = CephalopodSpec.Default;
            Matrix4x4[] w = OctopusRestPose.BoneWorld(sp), b = OctopusRestPose.BindPoses(sp);
            Assert.AreEqual(201, w.Length); Assert.AreEqual(201, b.Length);
            for (int i = 0; i < w.Length; i++)
            {
                Matrix4x4 p = OctoMath.MulAffine(w[i], b[i]);
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 4; c++)
                        Assert.AreEqual(r == c ? 1f : 0f, p[r, c], 1e-4f, $"bone {i} [{r},{c}]");
            }
        }

        // The pose writer at a size scale and a spawn rotation reproduces the rest mesh scaled and rotated:
        // bones, bind poses and mesh agree.
        [Test] public void Octopus_SpawnPose_SkinsRestMeshRigidly()
        {
            var sp = CephalopodSpec.Default;
            OctopusMeshBuilder.Data d = OctopusMeshBuilder.BuildData(sp);
            Matrix4x4[] bind = OctopusRestPose.BindPoses(sp);
            Quaternion rot = OctoMath.AxisAngle(new Vector3(0.3f, 1f, -0.2f).normalized, 1.1f);
            Vector3 at = new Vector3(4f, 2f, -3f);
            const float scale = 2.5f;
            var body = new OctopusBody(sp, at, rot, scale);
            Matrix4x4[] skin = SkinMatrices(body, bind);
            float worst = 0f;
            for (int i = 0; i < d.pos.Count; i++)
            {
                Vector3 want = at + OctoMath.Rotate(rot, d.pos[i] * scale);
                worst = Mathf.Max(worst, (Skin(d, skin, i, false, bind) - want).magnitude);
            }
            TestContext.Out.WriteLine($"worst rigid-pose skin error {worst * 1e3f:F3} mm");
            Assert.Less(worst, 1e-3f * scale);
        }

        // Arm 0 posed (pose only, not contact) as a helix around a branch at the tightest bend the chain
        // allows (the cone limit at every joint, 12 deg pitch), suckers on the bark, the wrapped run squeezed
        // and a bulge just past it. No triangle of the mesh may invert.
        [Test] public void Mesh_WrappedPose_NoInvertedTris()
        {
            var sp = CephalopodSpec.Default;
            OctopusMeshBuilder.Data d = OctopusMeshBuilder.BuildData(sp);
            Matrix4x4[] bind = OctopusRestPose.BindPoses(sp);
            var body = OctopusRestPose.Body(sp);
            TentacleChain arm = body.Arms[0];
            float bend = sp.arm.BendLimitDeg * Mathf.Deg2Rad, pitch = 12f * Mathf.Deg2Rad;
            PoseHelix(arm, 3, bend, pitch, out float radius);
            TestContext.Out.WriteLine($"helix radius {radius:F3} m (centre line), {(arm.N - 3) * bend * Mathf.Cos(pitch) / (2f * Mathf.PI):F2} turns");
            Matrix4x4[] skin = SkinMatrices(body, bind);
            int inverted = CountInverted(d, skin, bind, out string first);
            Assert.AreEqual(0, inverted, "inverted triangles; first: " + first);
        }

        // Lays particles from..N-1 on a helix that continues tangentially from the straight run before
        // `from`, with the joint angle `bend` between segments; frames: +Z along the helix, +Y (dorsal) away
        // from the helix axis; squeeze 0.92 on the wrap, 1.05 on the particle before it.
        internal static void PoseHelix(TentacleChain c, int from, float bend, float pitch, out float radius)
        {
            float sp = c.Spacing;
            // a helix of radius R and pitch angle p has curvature cos^2 p / R; the turn per segment is that x spacing
            radius = sp * Mathf.Cos(pitch) * Mathf.Cos(pitch) / (2f * Mathf.Sin(0.5f * bend));
            Vector3 p0 = c.X[from - 1];
            Vector3 dir = (c.X[from - 1] - c.X[from - 2]).normalized;
            Vector3 up = Vector3.up;
            Vector3 side = Vector3.Cross(up, dir).normalized; // helix axis direction (the branch runs sideways)
            // centre of the helix: below the arm (the branch is under the ventral side)
            Vector3 centre = p0 - up * radius;
            float dTheta = sp * Mathf.Cos(pitch) / radius, rise = sp * Mathf.Sin(pitch);
            for (int k = from; k < c.N; k++)
            {
                float th = dTheta * (k - from + 1);
                Vector3 radial = up * Mathf.Cos(th) + dir * Mathf.Sin(th);
                c.X[k] = centre + radial * radius + side * (rise * (k - from + 1));
            }
            for (int k = 0; k < c.N; k++)
            {
                Vector3 t = (k < c.N - 1 ? c.X[k + 1] - c.X[k] : c.X[k] - c.X[k - 1]).normalized;
                Vector3 rel = c.X[k] - centre;
                Vector3 u = k >= from - 1 ? rel - side * Vector3.Dot(rel, side) : Vector3.up;
                c.Frames[k] = OctoMath.Look(t, u);
                c.Stretch[k] = (k < c.N - 1 ? (c.X[k + 1] - c.X[k]).magnitude : (c.X[k] - c.X[k - 1]).magnitude) / sp;
                c.Squeeze[k] = k >= from ? c.Spec.WrapSqueeze : k == from - 1 ? c.Spec.Bulge : 1f;
            }
        }

        // Triangles whose skinned orientation flips against the heavier bone's mapping of the rest triangle.
        internal static int CountInverted(OctopusMeshBuilder.Data d, Matrix4x4[] skin, Matrix4x4[] bind, out string first, bool twoBone = false) =>
            CountInverted(d, skin, bind, out first, out _, twoBone);

        internal static int CountInverted(OctopusMeshBuilder.Data d, Matrix4x4[] skin, Matrix4x4[] bind, out string first, out int[] perPart, bool twoBone = false)
        {
            var pos = new Vector3[d.pos.Count];
            for (int i = 0; i < pos.Length; i++) pos[i] = Skin(d, skin, i, twoBone, bind);
            int inverted = 0; first = "none";
            perPart = new int[8];
            for (int k = 0; k < d.tris.Count; k += 3)
            {
                int i0 = d.tris[k], i1 = d.tris[k + 1], i2 = d.tris[k + 2];
                Vector3 rest = Vector3.Cross(d.pos[i1] - d.pos[i0], d.pos[i2] - d.pos[i0]);
                Vector3 now = Vector3.Cross(pos[i1] - pos[i0], pos[i2] - pos[i0]);
                // the bone's linear part maps the rest normal by the cofactor (inverse transpose) direction;
                // for a sign test the plain linear map of the face normal is sufficient for rotation x positive scale
                Vector3 want = OctoMath.MulVector(skin[(int)d.uv2[i0].x], rest);
                if (Vector3.Dot(now, want) <= 0f)
                {
                    if (inverted == 0) first = $"triangle {k / 3} part {Part(d, i0)} bones {d.uv2[i0]} uv0 {d.uv0[i0]}";
                    inverted++;
                    perPart[Part(d, i0)]++;
                }
            }
            if (inverted > 0)
            {
                first += "; per part:";
                for (int p = 0; p < perPart.Length; p++) if (perPart[p] > 0) first += $" {p}={perPart[p]}";
            }
            return inverted;
        }

        // Real sim poses: the body hanging from one arm while the others swing and curl, with a travelling
        // sinus, for 8 s. Arms, mantle, eyes and siphon never invert. The web may fold where two neighbouring
        // arms close together (a slack membrane pleats; those folds sit between the arms) but stays under 3 %
        // of its triangles; the 2-bone fallback count is printed for the shader task.
        [Test] public void Mesh_SimPoses_NoInvertedTris()
        {
            var sp = CephalopodSpec.Default;
            OctopusMeshBuilder.Data d = OctopusMeshBuilder.BuildData(sp);
            Matrix4x4[] bind = OctopusRestPose.BindPoses(sp);
            var body = new OctopusBody(sp, Vector3.zero, Quaternion.identity);
            TentacleChain arm = body.Arms[0];
            arm.Pin(arm.N - 1, arm.X[arm.N - 1], Vector3.up);
            int worst = 0, worstTwoBone = 0, worstWeb = 0; string first = "none";
            int webTris = 0;
            for (int k = 0; k < d.tris.Count; k += 3) if (Part(d, d.tris[k]) == OctopusMeshBuilder.PartWeb) webTris++;
            for (int f = 0; f < 480; f++)
            {
                for (int a = 1; a < body.ArmCount; a++) body.Arms[a].SinusAmount = (f / 120 + a) % 2;
                if (f == 240) body.SetAllLimp(true);
                body.Tick(1f / 60f);
                if (f % 20 != 19) continue;
                Matrix4x4[] skin = SkinMatrices(body, bind);
                CountInverted(d, skin, bind, out string fi, out int[] per);
                int inv = 0;
                for (int p = 0; p < per.Length; p++) if (p != OctopusMeshBuilder.PartWeb) inv += per[p];
                if (inv > worst) { worst = inv; first = $"frame {f}: {fi}"; }
                worstWeb = Mathf.Max(worstWeb, per[OctopusMeshBuilder.PartWeb]);
                worstTwoBone = Mathf.Max(worstTwoBone, CountInverted(d, skin, bind, out _, true));
            }
            // informational: a shader that ignores uv3 (CreatureSkin.hlsl today) folds the web this often
            TestContext.Out.WriteLine($"worst inverted per frame: non-web {worst}, web {worstWeb} of {webTris}, all parts with the 2-bone fallback {worstTwoBone}");
            Assert.AreEqual(0, worst, "inverted triangles outside the web; " + first);
            Assert.LessOrEqual(worstWeb, 0.03f * webTris, "web folds");
        }

        // The squash-and-stretch run (OctopusSimTests.SquishRun): the shell never inverts, at the stretch peak,
        // through the squash and the jiggle.
        [Test] public void Mesh_SquishPoses_NoInvertedTris()
        {
            var sp = CephalopodSpec.Default; sp.breathAmp = -1f;
            OctopusMeshBuilder.Data d = OctopusMeshBuilder.BuildData(sp);
            Matrix4x4[] bind = OctopusRestPose.BindPoses(sp);
            var body = new OctopusBody(sp, Vector3.zero, Quaternion.identity);
            body.SetAllLimp(true);
            TentacleChain arm = body.Arms[0];
            arm.Pin(arm.N - 1, arm.X[arm.N - 1], Vector3.up);
            body.ExtraDamping = 4f;
            for (int f = 0; f < 900; f++) body.Tick(1f / 60f);
            body.ExtraDamping = 0f;
            int worst = 0; string first = "none"; int sampled = 0;
            OctopusSimTests.SquishRun(body, sp, out _, out _, out _, out _, out _, (f, e) =>
            {
                if (f > 120 || f % 3 != 0) return;
                sampled++;
                CountInverted(d, SkinMatrices(body, bind), bind, out string fi, out int[] per);
                int inv = 0;
                for (int p = 0; p < per.Length; p++) if (p != OctopusMeshBuilder.PartWeb) inv += per[p];
                if (inv > worst) { worst = inv; first = $"frame {f} (extent x{e:F3}): {fi}"; }
            });
            Assert.Greater(sampled, 30);
            Assert.AreEqual(0, worst, "inverted triangles outside the web; " + first);
        }

        // The shared part ids: the eye is the creature shader's eye part; the octopus parts come after the spine ones.
        [Test] public void Mesh_Octopus_PartIdsMatchCreatureBuilder()
        {
            Assert.AreEqual(CreatureMeshBuilder.PartEye, OctopusMeshBuilder.PartEye);
            Assert.Greater(OctopusMeshBuilder.PartArm, CreatureMeshBuilder.PartEye);
        }

        // Unity Mesh: build time (median of 5, after a JIT warm-up) under 8 ms.
        [Test] public void Mesh_Octopus_BuildTime_Under8ms()
        {
            var sp = CephalopodSpec.Default;
            Object.DestroyImmediate(OctopusMeshBuilder.Build(sp));
            var ms = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                var sw = Stopwatch.StartNew();
                Mesh m = OctopusMeshBuilder.Build(sp);
                sw.Stop();
                ms.Add(sw.Elapsed.TotalMilliseconds);
                Assert.AreEqual(201, OctopusBody.BoneCountFor(sp));
                Assert.Less(m.vertexCount, 15000);
                Object.DestroyImmediate(m);
            }
            ms.Sort();
            TestContext.Out.WriteLine($"build ms: {string.Join(", ", ms)}");
            Assert.Less(ms[2], 8.0, "median build time");
        }
    }
}
