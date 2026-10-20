using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The kit's BandShell part (2026-10-20, the Delver's carapace) and the plate-clip probe every banded part shares:
    // validation; the mesh (rigid to joints, finite unit normals facing out); the bands shingle in the rest pose (each
    // band's tucked front lies under the band ahead, and the bands overlap - no gap); no band clips through the one
    // ahead of it over a tight-turn walk at 0.9x and 1.1x (ShellClip); RED: without the shingle step the bend clips.
    public class CreatureBandShellTests
    {
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        static SpeciesCtx Ctx(int joints) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, false);

        static BandShell Ok() => new BandShell { id = "shell", at = new PartSocket(5, 0, Vector3.zero), toJoint = 12, thickness = 0.008f, shingle = 0.008f };

        [Test] public void BandShell_Validate_RejectsBadData()
        {
            Assert.IsNull(Ok().Validate(Ctx(30)));
            var bad = (BandShell)Ok().Clone(); bad.toJoint = 4;
            Assert.IsNotNull(bad.Validate(Ctx(30)));
            bad = (BandShell)Ok().Clone(); bad.toJoint = 29;
            Assert.IsNotNull(bad.Validate(Ctx(30)));
            bad = (BandShell)Ok().Clone(); bad.shingle = 0.003f;   // under half the thickness
            Assert.IsNotNull(bad.Validate(Ctx(30)));
            bad = (BandShell)Ok().Clone(); bad.thickness = 0f;
            Assert.IsNotNull(bad.Validate(Ctx(30)));
            bad = (BandShell)Ok().Clone(); bad.hoodBands = 9;
            Assert.IsNotNull(bad.Validate(Ctx(30)));
            bad = (BandShell)Ok().Clone(); bad.layer = 5;
            Assert.IsNotNull(bad.Validate(Ctx(30)));
            Assert.IsNotNull(Ok().Validate(new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], 0, true)), "soft body");
        }

        [Test] public void BandShell_Mesh_RigidToJoints_UnitNormalsFacingOut()
        {
            CreatureSpecies asset = CreatureKitAssets.Load("Delver");
            var shell = Find<BandShell>(asset);
            Mesh m = CreatureKitAssets.BuildMesh(asset); trash.Add(m);
            Vector3[] v = m.vertices, n = m.normals;
            var uv1 = new List<Vector2>(); var uv2 = new List<Vector4>(); m.GetUVs(1, uv1); m.GetUVs(2, uv2);
            CreatureSim rest = CreatureRestPose.Sim(asset.Compiled.Runtime);
            int count = 0, outward = 0, judged = 0;
            for (int i = 0; i < v.Length; i++)
            {
                if (Mathf.RoundToInt(uv1[i].x) != shell.layer) continue;
                int j = (int)uv2[i].x;
                if (j > shell.toJoint) continue;   // another armour part on the same layer (the tail's rings)
                count++;
                Assert.AreEqual(1f, uv2[i].z, 1e-6f, $"vertex {i}: rigid");
                Assert.That(j, Is.InRange(shell.at.joint, shell.toJoint), $"vertex {i}: its joint");
                Assert.AreEqual(1f, n[i].magnitude, 1e-3f, $"vertex {i} normal");
                // away from the joint's long axis (the inner surface faces in: half the shell; edges are judged by side)
                Vector3 local = Quaternion.Inverse(rest.Rotations[j]) * (v[i] - rest.Joints[j]);
                Vector3 ln = Quaternion.Inverse(rest.Rotations[j]) * n[i];
                Vector3 radial = new Vector3(local.x, local.y + shell.edgeDrop, 0f);
                if (radial.sqrMagnitude < 1e-8f) continue;
                judged++;
                if (Mathf.Abs(Vector3.Dot(ln, radial.normalized)) > 0.2f) outward++;
            }
            Assert.Greater(count, 1000, "shell vertices");
            Assert.Greater(outward / (float)judged, 0.85f, "normals across the shell (out on the outer surface, in on the inner)");
        }

        [Test] public void BandShell_Rest_Shingled_NoGaps()
        {
            CreatureSpecies asset = CreatureKitAssets.Load("Delver");
            CreatureSpecies sp = asset.Compiled.Runtime;
            CreatureSim rest = CreatureRestPose.Sim(sp);
            var world = new Matrix4x4[sp.BoneCount];
            rest.WriteBoneWorld(world, 0);
            Assert.IsNull(ShellClip.Problem(asset, world, 0f, out ShellClip.Stats st));
            Assert.Greater(st.covered, 0.9f, $"share of each band's tucked front under the band ahead ({st.covered:P0})");
        }

        // A tight turn at the smallest and largest individual: no band clips through the band ahead.
        [TestCase(0.9f)] [TestCase(1.1f)]
        public void BandShell_TightTurn_NoClipping(float rel)
        {
            CreatureSpecies asset = CreatureKitAssets.Load("Delver");
            Assert.IsNull(ShellClip.TightTurnProblem(asset, asset.scale * rel));
        }

        // RED: the same shell without its shingle step (and a hair of thickness) clips in the same turn.
        [Test] public void BandShell_Red_NoShingleClips()
        {
            CreatureSpecies asset = CreatureKitAssets.LoadAsset("Delver");
            var saved = new List<(BandShell, float, float, float)>();
            foreach (CreaturePart p in asset.parts) if (p is BandShell b) { saved.Add((b, b.shingle, b.thickness, b.lip)); b.shingle = 0.0006f; b.thickness = 0.001f; b.lip = 0f; }
            try
            {
                asset.InvalidateCompiled();
                Assert.IsNotNull(ShellClip.TightTurnProblem(asset, asset.scale));
            }
            finally
            {
                foreach ((BandShell b, float sh, float th, float lp) in saved) { b.shingle = sh; b.thickness = th; b.lip = lp; }
                asset.InvalidateCompiled();
            }
        }

        internal static T Find<T>(CreatureSpecies asset) where T : CreaturePart
        {
            foreach (CreaturePart p in asset.Compiled.Parts) if (p is T t) return t;
            return null;
        }
    }

    // Plates never clip: for every pair of bands rigid to consecutive joints (A ahead of B), B's outer surface over its
    // front half (the part tucked under A) lies under A's inner surface wherever A covers it. Bands come from every
    // IShellBands part of the species (BandShell, SerratedRings), so the shell's last band and the tail's first ring
    // are a pair too.
    internal static class ShellClip
    {
        public struct Stats { public float covered, worst; public int pairs; }   // covered: share of the front edge rows (u <= 0.1) under the band ahead

        struct BandRef { public ShellBand b; public int joint; public Matrix4x4 local; public string part; }

        static List<BandRef> Bands(CreatureSpecies asset, CreatureSim rest)
        {
            CreatureSpecies sp = asset.Compiled.Runtime;
            var list = new List<BandRef>();
            foreach (CreaturePart p in asset.Compiled.Parts)
                if (p is IShellBands s)
                    for (int k = 0; k < s.BandCount; k++)
                    {
                        ShellBand b = s.BandAt(sp, rest, k, out int j, out Matrix4x4 local);
                        list.Add(new BandRef { b = b, joint = j, local = local, part = p.id });
                    }
            return list;
        }

        // world: bone worlds of a sim (WriteBoneWorld). tolerance: metres x the size (a hair for numeric noise).
        public static string Problem(CreatureSpecies asset, Matrix4x4[] world, float tolerance, out Stats st)
        {
            CreatureSpecies sp = asset.Compiled.Runtime;
            CreatureSim rest = CreatureRestPose.Sim(sp);
            List<BandRef> bands = Bands(asset, rest);
            st = new Stats { worst = float.MaxValue };
            int samples = 0, covered = 0;
            foreach (BandRef a in bands)
                foreach (BandRef b in bands)
                {
                    if (b.joint != a.joint + 1 || a.local != Matrix4x4.identity || b.local != Matrix4x4.identity) continue;
                    st.pairs++;
                    Matrix4x4 toA = world[a.joint].inverse * world[b.joint];   // B's joint frame (size 1) -> A's
                    float s = world[a.joint].lossyScale.x;
                    for (int i = 0; i <= 24; i++)
                    {
                        float t = Mathf.Lerp(b.b.lo, b.b.hi, i / 24f);
                        for (int k = 0; k <= 4; k++)
                        {
                            float u = 0.4f * k / 4f;
                            Vector3 p = toA.MultiplyPoint3x4(b.b.Surface(t, u, 0f));
                            float c = a.b.Clearance(p);
                            if (u <= 0.1f) samples++;
                            if (float.IsPositiveInfinity(c)) continue;
                            if (u <= 0.1f) covered++;
                            st.worst = Mathf.Min(st.worst, c);
                            if (c < -tolerance) return $"{b.part} band at joint {b.joint} pokes {-c * s * 1000f:F2} mm through the band at joint {a.joint} (angle {t * Mathf.Rad2Deg:F0} deg, u {u:F2})";
                        }
                    }
                }
            st.covered = samples > 0 ? (float)covered / samples : 0f;
            return st.pairs > 0 ? null : "no consecutive band pairs: the check does not bite";
        }

        // The kit's MaxBend circle (minimum-radius walk), every 5th frame.
        public static string TightTurnProblem(CreatureSpecies asset, float sizeScale)
        {
            CreatureSpecies sp = asset.Compiled.Runtime;
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, sizeScale, 0);
            var world = new Matrix4x4[sp.BoneCount];
            float ts = Mathf.Sqrt(sizeScale);
            Vector3 centre = new Vector3(0f, 0f, 0.25f) * sizeScale;
            for (int f = 0; f < (int)(300 * ts); f++)
            {
                float a = f / 60f * 3f / ts;
                sim.Tick(1f / 60f, centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.12f * sizeScale));
                if (f % 5 != 4) continue;
                sim.WriteBoneWorld(world, 0);
                string e = Problem(asset, world, 1e-4f, out _);
                if (e != null) return $"t={f}: {e}";
            }
            return null;
        }
    }
}
