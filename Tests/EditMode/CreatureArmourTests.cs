using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The kit's skin-surface parts: ScuteRow (keeled osteoderm rows, the Crocodile's armour) and SkinFolds (loose
    // folds and a dewlap, the KomodoDragon's throat): validation; the meshes face outward (every scute facet and every
    // fold quad wound away from the spine); scutes sit on the skin (the base buried no deeper than its inset, the ridge
    // no higher than its height above the body's surface); folds grow out of the body and fade into it at both ends.
    // RED: a scute facet wound inward is caught by the outward check.
    public class CreatureArmourTests
    {
        static SpeciesCtx Ctx(int joints, bool soft = false) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, soft);

        [Test] public void ScuteRow_Validate_RejectsBadData()
        {
            var ok = new ScuteRow { id = "s", at = new PartSocket(2, 0, Vector3.zero), toJoint = 8 };
            Assert.IsNull(ok.Validate(Ctx(10)));
            Assert.AreEqual(0, ok.BoneCount(Ctx(10)), "skinned to the spine");
            Assert.AreEqual(7, ok.Count, "one scute a joint, both ends");
            var bad = (ScuteRow)ok.Clone(); bad.toJoint = 10; Assert.IsNotNull(bad.Validate(Ctx(10)), "past the last joint");
            bad = (ScuteRow)ok.Clone(); bad.heightEnd = 0f; Assert.IsNotNull(bad.Validate(Ctx(10)), "flat scute");
            bad = (ScuteRow)ok.Clone(); bad.aroundStartDeg = 170f; Assert.IsNotNull(bad.Validate(Ctx(10)), "under the belly");
            bad = (ScuteRow)ok.Clone(); bad.layer = 16; Assert.IsNotNull(bad.Validate(Ctx(10)), "layer out of the table");
            Assert.IsNotNull(ok.Validate(Ctx(10, true)), "needs a spine body");
        }

        [Test] public void SkinFolds_Validate_RejectsBadData()
        {
            var ok = new SkinFolds { id = "f", at = new PartSocket(1, 0, Vector3.zero), toJoint = 5 };
            Assert.IsNull(ok.Validate(Ctx(10)));
            var bad = (SkinFolds)ok.Clone(); bad.toJoint = 1; Assert.IsNotNull(bad.Validate(Ctx(10)), "empty stretch");
            bad = (SkinFolds)ok.Clone(); bad.height = 0f; bad.sag = 0f; Assert.IsNotNull(bad.Validate(Ctx(10)), "nothing to fold");
            bad = (SkinFolds)ok.Clone(); bad.folds = 0; Assert.IsNotNull(bad.Validate(Ctx(10)), "no folds");
            bad = (SkinFolds)ok.Clone(); bad.sharpness = 0.5f; Assert.IsNotNull(bad.Validate(Ctx(10)), "sharpness below 1");
        }

        // Triangles of a material layer (or, layer < 0, of every vertex the predicate keeps) in the rest mesh, with the
        // rest spine for the outward test.
        struct RestMesh { public Vector3[] v; public int[] tri; public List<Vector2> uv1; public CreatureSim rest; }

        static RestMesh Rest(CreatureSpecies sp)
        {
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            var r = new RestMesh { v = mesh.vertices, tri = mesh.triangles, uv1 = new List<Vector2>() };
            mesh.GetUVs(1, r.uv1);
            Object.DestroyImmediate(mesh);
            r.rest = new CreatureSim(sp.Compiled.Runtime, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward);
            return r;
        }

        // The nearest point on the rest spine polyline.
        static Vector3 Axis(CreatureSim rest, Vector3 p)
        {
            Vector3[] j = rest.Joints;
            Vector3 best = j[0]; float bd = float.PositiveInfinity;
            for (int i = 0; i + 1 < j.Length; i++)
            {
                Vector3 d = j[i + 1] - j[i];
                float t = Mathf.Clamp01(Vector3.Dot(p - j[i], d) / Mathf.Max(1e-12f, d.sqrMagnitude));
                Vector3 q = j[i] + d * t;
                float dd = (p - q).sqrMagnitude;
                if (dd < bd) { bd = dd; best = q; }
            }
            return best;
        }

        // Triangles of `layer` wound toward the spine (normal . (centroid - axis) <= 0), and how many were checked.
        static int Inward(RestMesh m, int layer, out int checkedTris, bool flipForRed = false)
        {
            int bad = 0; checkedTris = 0;
            for (int k = 0; k < m.tri.Length; k += 3)
            {
                int a = m.tri[k], b = m.tri[k + 1], c = m.tri[k + 2];
                if (Mathf.RoundToInt(m.uv1[a].x) != layer) continue;
                if (flipForRed && checkedTris == 0) { int t = b; b = c; c = t; }
                Vector3 n = Vector3.Cross(m.v[b] - m.v[a], m.v[c] - m.v[a]);
                Vector3 g = (m.v[a] + m.v[b] + m.v[c]) / 3f;
                if (!(Vector3.Dot(n, g - Axis(m.rest, g)) > 0f)) bad++;
                checkedTris++;
            }
            return bad;
        }

        [Test] public void ScuteRow_Crocodile_FacetsFaceOutward_CountsMatch()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("Crocodile");
            RestMesh m = Rest(sp);
            int bad = Inward(m, 12, out int n);
            int want = 0;
            foreach (CreaturePart p in sp.Compiled.Parts)
                if (p is ScuteRow r) want += r.Count * (r.keelWidth > 1e-6f ? 5 : 4) * 2;
            TestContext.Out.WriteLine($"{n} armour triangles ({want} expected), {bad} facing the spine");
            Assert.AreEqual(want, n, "four or five facets a scute, two triangles each");
            Assert.AreEqual(0, bad, "every scute facet faces away from the spine");
        }

        // Every armour vertex lies between its row's inset below and its height above the body's surface: measured as
        // the distance from the rest spine against the body's ellipse radius in that direction.
        [Test] public void ScuteRow_Crocodile_SitsOnTheSkin()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("Crocodile");
            CreatureSpecies rt = sp.Compiled.Runtime;
            RestMesh m = Rest(sp);
            float hMax = 0f;
            foreach (CreaturePart p in sp.Compiled.Parts) if (p is ScuteRow r) hMax = Mathf.Max(hMax, Mathf.Max(r.heightStart, r.heightEnd));
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            for (int i = 0; i < m.v.Length; i++)
            {
                if (Mathf.RoundToInt(m.uv1[i].x) != 12) continue;
                Vector3 p = m.v[i], ax = Axis(m.rest, p);
                // the joint nearest the axis point gives the section's half sizes
                int j = 0; float bd = float.PositiveInfinity;
                for (int k = 0; k < m.rest.Joints.Length; k++) { float d = (m.rest.Joints[k] - ax).sqrMagnitude; if (d < bd) { bd = d; j = k; } }
                Quaternion r = m.rest.Rotations[j];
                Vector3 local = Quaternion.Inverse(r) * (p - ax);
                float ang = Mathf.Atan2(local.y, local.x);
                float a = rt.joints[j].halfWidth, b = rt.joints[j].halfHeight;
                float radius = a * b / Mathf.Sqrt(b * b * Mathf.Cos(ang) * Mathf.Cos(ang) + a * a * Mathf.Sin(ang) * Mathf.Sin(ang));
                float off = new Vector2(local.x, local.y).magnitude - radius;
                lo = Mathf.Min(lo, off); hi = Mathf.Max(hi, off);
            }
            TestContext.Out.WriteLine($"armour vertices from {lo * 1000f:F1} to {hi * 1000f:F1} mm off the skin (tallest scute {hMax * 1000f:F1} mm)");
            // the spine joints are a coarse stand-in for the lofted rings: allow a fifth of the body's thickness
            Assert.Greater(lo, -0.35f * hMax - 0.012f, "no scute sunk into the body");
            Assert.Less(hi, hMax + 0.012f, "no scute floating off the body");
        }

        // The KomodoDragon's folds: the extra round the body is never negative (the shell never dips into the body), it
        // fades to nothing at both ends of the stretch, never exceeds the fold height plus the sag, and the dewlap
        // hangs only below (the back of a throat fold carries no sag).
        [Test] public void SkinFolds_Komodo_GrowOut_FadeAtTheEnds_SagBelow()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("KomodoDragon");
            int n = 0;
            foreach (CreaturePart p in sp.Compiled.Parts)
            {
                if (!(p is SkinFolds f)) continue;
                n++;
                float side = f.at.side < 0 ? -1f : 1f, lo = float.PositiveInfinity, hi = 0f, ends = 0f, back = 0f;
                for (int i = 0; i <= 200; i++)
                {
                    float u = Mathf.Lerp(f.at.joint, f.toJoint, i / 200f);
                    for (int k = 0; k < 72; k++)
                    {
                        float phi = -0.5f * Mathf.PI + 2f * Mathf.PI * k / 72f, e = f.Extra(u, phi, side);
                        lo = Mathf.Min(lo, e); hi = Mathf.Max(hi, e);
                        if (i == 0 || i == 200) ends = Mathf.Max(ends, Mathf.Abs(e));
                        if (Mathf.Sin(phi) > 0.2f) back = Mathf.Max(back, f.AlongAt(u).sag * SkinFolds.SagWeightAt(phi));
                    }
                }
                TestContext.Out.WriteLine($"{f.id}: extra {lo * 1000f:F2} .. {hi * 1000f:F2} mm, at the ends {ends * 1000f:F3} mm");
                Assert.GreaterOrEqual(lo, 0f, f.id + ": never into the body");
                Assert.LessOrEqual(hi, f.height + f.sag + 1e-6f, f.id + ": no taller than its folds and sag");
                Assert.Less(ends, 1e-6f, f.id + ": fades into the body at both ends");
                Assert.AreEqual(0f, back, f.id + ": no sag on the back");
                Assert.Greater(hi, 0.5f * Mathf.Max(f.height, f.sag), f.id + ": visible");
            }
            Assert.AreEqual(3, n, "the throat and the mirrored neck folds");
        }

        [Test] public void Red_ScuteFacet_WoundInward_IsCaught()
        {
            RestMesh m = Rest(CreatureKitAssets.Load("Crocodile"));
            int bad = Inward(m, 12, out _, true);
            Assert.AreEqual(1, bad, "one flipped facet is reported");
        }
    }
}
