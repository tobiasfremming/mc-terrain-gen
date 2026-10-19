using System.Collections.Generic;
using CreatureKit;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The kit's RigidLoft part (user feedback 2026-10-19: "make the shape of the head more interesting"), on the
    // SkyDragon's head (crown, squared muzzle, open jaw, brow ridges, nostrils): validation, superellipse sections
    // (flat-topped), the mesh appended after every legacy part, rigid to its joint, wound outward, the open mouth,
    // and size: the skinned head follows sizeScale exactly.
    public class CreatureRigidLoftTests
    {
        static SpeciesCtx Ctx(int joints) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, false);

        static LoftKey Key(float t, float hw, float hh, float top = 2f, float bot = 2f, float rise = 0f) =>
            new LoftKey { t = t, halfWidth = hw, halfHeight = hh, rise = rise, squareTop = top, squareBottom = bot };

        static RigidLoft Box(float top) => new RigidLoft
        {
            id = "l", at = new PartSocket(0, 0, Vector3.zero), direction = Vector3.forward, length = 0.1f,
            keys = new[] { Key(0f, 0.02f, 0.01f, top, 2f), Key(1f, 0.02f, 0.01f, top, 2f) },
        };

        [Test] public void Validate_RejectsBadData()
        {
            RigidLoft ok = Box(4f);
            Assert.IsNull(ok.Validate(Ctx(10)));
            Assert.AreEqual(0, ok.BoneCount(Ctx(10)), "mesh only: rigid to its joint");
            var bad = (RigidLoft)ok.Clone(); bad.at.joint = 10;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "joint out of range");
            bad = (RigidLoft)ok.Clone(); bad.length = 0f;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "no length");
            bad = (RigidLoft)ok.Clone(); bad.keys = new[] { Key(0f, 0.02f, 0.01f) };
            Assert.IsNotNull(bad.Validate(Ctx(10)), "one key");
            bad = (RigidLoft)ok.Clone(); bad.keys = new[] { Key(0f, 0.02f, 0.01f), Key(0.5f, 0.02f, 0.01f) };
            Assert.IsNotNull(bad.Validate(Ctx(10)), "keys not ending at 1");
            bad = (RigidLoft)ok.Clone(); bad.keys = new[] { Key(0f, 0.02f, 0f), Key(1f, 0.02f, 0.01f) };
            Assert.IsNotNull(bad.Validate(Ctx(10)), "zero height");
            bad = (RigidLoft)ok.Clone(); bad.keys = new[] { Key(0f, 0.02f, 0.01f, 0.5f), Key(1f, 0.02f, 0.01f) };
            Assert.IsNotNull(bad.Validate(Ctx(10)), "exponent below 1");
            bad = (RigidLoft)ok.Clone(); bad.direction = Vector3.zero;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "no direction");
            bad = (RigidLoft)ok.Clone(); bad.shade = 1;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "shade 0 or 2");
        }

        // A squared top is flat: at 60 deg up the section's height is nearly its half height (an ellipse: sin 60 =
        // 0.87); the bottom (exponent 2) stays elliptical; mirror flips x only.
        [Test] public void Section_SuperEllipse_FlatTop()
        {
            RigidLoft round = Box(2f), square = Box(6f);
            float phi = 60f * Mathf.Deg2Rad;
            float yr = round.Surface(0.5f, phi).y, ys = square.Surface(0.5f, phi).y;
            TestContext.WriteLine($"height at 60 deg: round {yr / 0.01f:F3}, squared {ys / 0.01f:F3} of the half height");
            Assert.AreEqual(Mathf.Sin(phi), yr / 0.01f, 1e-3f, "exponent 2 is the ellipse");
            Assert.Greater(ys / 0.01f, 0.95f, "exponent 6 is flat-topped");
            Assert.AreEqual(round.Surface(0.5f, -phi).y, square.Surface(0.5f, -phi).y, 1e-6f, "the bottom keeps its own exponent");
            RigidLoft l = Box(4f); l.at.offset = new Vector3(0.03f, 0f, 0f); l.direction = new Vector3(0.5f, 0f, 1f); l.rollDeg = 20f;
            RigidLoft r = (RigidLoft)l.Clone(); l.at.side = -1; r.at.side = 1;
            for (float a = -3f; a < 3f; a += 0.7f)
            {
                Vector3 pl = l.Surface(0.7f, a), pr = r.Surface(0.7f, Mathf.PI - a);
                Assert.AreEqual(-pl.x, pr.x, 1e-5f, "mirror x"); Assert.AreEqual(pl.y, pr.y, 1e-5f); Assert.AreEqual(pl.z, pr.z, 1e-5f);
            }
        }

        sealed class MeshData
        {
            public Vector3[] v, n; public int[] t;
            public List<Vector2> uv1 = new List<Vector2>(); public List<Vector4> uv2 = new List<Vector4>();
        }

        static MeshData Mesh(CreatureSpecies sp)
        {
            Mesh m = CreatureKitAssets.BuildMesh(sp);
            var d = new MeshData { v = m.vertices, n = m.normals, t = m.triangles };
            m.GetUVs(1, d.uv1); m.GetUVs(2, d.uv2);
            Object.DestroyImmediate(m);
            return d;
        }

        static List<RigidLoft> Lofts(CreatureSpecies sp)
        {
            var l = new List<RigidLoft>();
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is RigidLoft r) l.Add(r);
            return l;
        }

        static int LoftVertices(RigidLoft l) => (l.Rings + 3) * (l.Around + 1);   // sections + the two poles

        // The dragon's lofts: appended after every other vertex, rigid to their joint (one bone, weight 1), every
        // triangle wound outward (its face agrees with its vertex normals).
        [Test] public void Dragon_Lofts_RigidAppendedOutward()
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            List<RigidLoft> lofts = Lofts(sp);
            Assert.GreaterOrEqual(lofts.Count, 7, "crown, muzzle, jaw, two brows, two nostrils");
            MeshData d = Mesh(sp);
            int count = 0;
            foreach (RigidLoft l in lofts) count += LoftVertices(l);
            int first = d.v.Length - count;
            var joints = new HashSet<int>();
            foreach (RigidLoft l in lofts) joints.Add(l.at.joint);
            for (int i = first; i < d.v.Length; i++)
            {
                Vector4 s = d.uv2[i];
                Assert.AreEqual(1f, s.z, $"vertex {i} rigid"); Assert.AreEqual(s.x, s.y);
                Assert.IsTrue(joints.Contains((int)s.x), $"vertex {i} on a loft joint");
                int part = Mathf.RoundToInt(d.uv1[i].x);
                Assert.IsTrue(part == PartMeshEmitter.PartBody || part == PartMeshEmitter.PartFoot, $"vertex {i} shading");
            }
            int checkedTris = 0;
            for (int k = 0; k < d.t.Length; k += 3)
            {
                int a = d.t[k], b = d.t[k + 1], c = d.t[k + 2];
                if (a < first) continue;
                Assert.GreaterOrEqual(Mathf.Min(a, Mathf.Min(b, c)), first, "a loft triangle uses loft vertices only");
                Vector3 face = Vector3.Cross(d.v[b] - d.v[a], d.v[c] - d.v[a]);
                if (face.sqrMagnitude < 1e-16f) continue;
                Vector3 nn = d.n[a] + d.n[b] + d.n[c];
                Assert.Greater(Vector3.Dot(face, nn), 0f, $"loft triangle {k / 3} wound outward");
                checkedTris++;
            }
            TestContext.WriteLine($"{lofts.Count} lofts, {count} vertices from {first}, {checkedTris} triangles wound outward");
            Assert.Greater(checkedTris, 1000);
        }

        // The head reads as a dragon's: the muzzle's top is flat (a squared section), the mouth is open between the
        // muzzle and the jaw toward the tip, the brows stand above the eyes, the antlers sit behind the brows.
        [Test] public void Dragon_Head_SquaredSnout_OpenMouth()
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            RigidLoft muzzle = null, jaw = null, brow = null;
            foreach (RigidLoft l in Lofts(sp))
            {
                if (l.id == "muzzle") muzzle = l;
                else if (l.id == "jaw") jaw = l;
                else if (l.id == "brow.R") brow = l;
            }
            Assert.IsNotNull(muzzle); Assert.IsNotNull(jaw); Assert.IsNotNull(brow);
            // flat top: the muzzle at 45 deg up is above 80 % of its crest height (an ellipse: 71 %; exponent 4.5: 86 %)
            float t = 0.6f;
            muzzle.Centre(t, out Vector3 c, out Vector3 up, out _);
            float crest = Vector3.Dot(muzzle.Surface(t, 0.5f * Mathf.PI) - c, up), at45 = Vector3.Dot(muzzle.Surface(t, 0.25f * Mathf.PI) - c, up);
            Assert.Greater(at45 / crest, 0.8f, "flat-topped muzzle");
            // the mouth: at the front of the jaw its top lies below the muzzle's bottom (joint 0 frame, same joint)
            Assert.AreEqual(muzzle.at.joint, jaw.at.joint);
            Vector3 jawTop = jaw.Surface(0.9f, 0.5f * Mathf.PI);
            float gap = float.MaxValue;
            for (float s = 0f; s <= 1f; s += 0.01f)
            {
                Vector3 mb = muzzle.Surface(s, -0.5f * Mathf.PI);
                if (Mathf.Abs(mb.z - jawTop.z) < 0.004f) gap = Mathf.Min(gap, mb.y - jawTop.y);
            }
            TestContext.WriteLine($"mouth gap near the jaw's tip {gap * 1000f:F1} mm (scale 1), muzzle top at 45 deg {at45 / crest:P0} of its crest");
            Assert.That(gap, Is.InRange(0.003f, 0.03f), "a slightly open mouth");
            // brows over the eyes
            Vector3 eye = sp.eyeOffset;
            float browTop = float.MinValue;
            for (float s = 0f; s <= 1f; s += 0.05f) browTop = Mathf.Max(browTop, brow.Surface(s, 0.5f * Mathf.PI).y);
            Assert.AreEqual(sp.eyeJoint, brow.at.joint, "brow and eyes on the skull joint");
            Assert.Greater(browTop, eye.y + sp.eyeRadius, "the brow stands over the eye");
        }

        // Size: the head's lofts are rigid to their bone, so the skinned muzzle tip's distance from the head joint
        // follows sizeScale exactly (smallest and largest individual).
        [Test] public void Dragon_Head_ScalesWithSize()
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            RigidLoft muzzle = null;
            foreach (RigidLoft l in Lofts(sp)) if (l.id == "muzzle") muzzle = l;
            MeshData d = Mesh(sp);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            // the muzzle's tip pole: the vertex farthest along +z rigid to joint 0
            int tip = -1;
            for (int i = 0; i < d.v.Length; i++)
                if (d.uv2[i].z == 1f && (int)d.uv2[i].x == muzzle.at.joint && (tip < 0 || d.v[i].z > d.v[tip].z)) tip = i;
            var ratio = new float[2];
            float[] f = { 0.94f, 1.06f };
            var world = new Matrix4x4[sp.BoneCount];
            for (int c = 0; c < 2; c++)
            {
                float s = sp.scale * f[c];
                var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s);
                sim.Tick(1f / 60f, new Vector3(0f, 0f, 10f) * s);
                sim.WriteBoneWorld(world, 0);
                int b = muzzle.at.joint;
                Vector3 p = (world[b] * bind[b]).MultiplyPoint3x4(d.v[tip]);
                ratio[c] = (p - sim.Joints[b]).magnitude / s;
            }
            TestContext.WriteLine($"muzzle tip / size: x0.94 {ratio[0]:F5}, x1.06 {ratio[1]:F5}");
            Assert.AreEqual(ratio[0], ratio[1], 1e-4f, "the head scales with the individual");
        }
    }
}
