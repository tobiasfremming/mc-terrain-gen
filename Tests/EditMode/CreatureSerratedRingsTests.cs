using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The kit's SerratedRings part (2026-10-20, the Delver's tail armour): validation; the mesh (rigid to the run's joints,
    // finite unit normals, two blades a ring); the rings shingle (ShellClip covers them with BandShell's bands: the
    // shell's last band and the first ring are a pair); no blade cuts through the blade ahead of it over a tight-turn
    // walk at 0.9x and 1.1x; RED: blades without their shingle step do.
    public class CreatureSerratedRingsTests
    {
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        static SpeciesCtx Ctx(int joints) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, false);
        static SerratedRings Ok() => new SerratedRings { id = "tail", at = new PartSocket(15, 0, Vector3.zero), toJoint = 25 };

        [Test] public void SerratedRings_Validate_RejectsBadData()
        {
            Assert.IsNull(Ok().Validate(Ctx(30)));
            var bad = (SerratedRings)Ok().Clone(); bad.toJoint = 29;
            Assert.IsNotNull(bad.Validate(Ctx(30)));
            bad = (SerratedRings)Ok().Clone(); bad.shingle = 0.002f;
            Assert.IsNotNull(bad.Validate(Ctx(30)));
            bad = (SerratedRings)Ok().Clone(); bad.bladeStep = 0.001f;
            Assert.IsNotNull(bad.Validate(Ctx(30)));
            bad = (SerratedRings)Ok().Clone(); bad.bladeSweepDeg = 80f;
            Assert.IsNotNull(bad.Validate(Ctx(30)));
            bad = (SerratedRings)Ok().Clone(); bad.skirtDeg = 90f;
            Assert.IsNotNull(bad.Validate(Ctx(30)));
        }

        [Test] public void SerratedRings_Mesh_RigidToTheRun_UnitNormals()
        {
            CreatureSpecies asset = CreatureKitAssets.Load("Delver");
            var rings = CreatureBandShellTests.Find<SerratedRings>(asset);
            Mesh m = CreatureKitAssets.BuildMesh(asset); trash.Add(m);
            Vector3[] v = m.vertices, n = m.normals;
            var uv2 = new List<Vector4>(); m.GetUVs(2, uv2);
            var uv1 = new List<Vector2>(); m.GetUVs(1, uv1);
            var perJoint = new Dictionary<int, int>();
            for (int i = 0; i < v.Length; i++)
            {
                if (Mathf.RoundToInt(uv1[i].x) != rings.layer) continue;
                int j = (int)uv2[i].x;
                if (j < rings.at.joint) continue;   // the shell's bands share the layer
                Assert.AreEqual(1f, uv2[i].z, 1e-6f, $"vertex {i}: rigid");
                Assert.That(j, Is.InRange(rings.at.joint, rings.toJoint), $"vertex {i}: joint");
                Assert.AreEqual(1f, n[i].magnitude, 1e-3f, $"vertex {i} normal");
                Assert.IsFalse(float.IsNaN(v[i].x + v[i].y + v[i].z));
                perJoint.TryGetValue(j, out int c); perJoint[j] = c + 1;
            }
            Assert.AreEqual(rings.Rings, perJoint.Count, "a ring on every joint of the run");
        }

        [TestCase(0.9f)] [TestCase(1.1f)]
        public void SerratedRings_TightTurn_RingsAndBladesNeverClip(float rel)
        {
            CreatureSpecies asset = CreatureKitAssets.Load("Delver");
            Assert.IsNull(ShellClip.TightTurnProblem(asset, asset.scale * rel), "rings");
            Assert.IsNull(BladeClip.TightTurnProblem(asset, asset.scale * rel), "blades");
        }

        [Test] public void SerratedRings_Red_FlatBladesClip()
        {
            CreatureSpecies asset = CreatureKitAssets.LoadAsset("Delver");
            // long, swept, flat blades overlap their neighbours in plan: without the shingle step they cut through each other
            var saved = new List<(SerratedRings, float, float, float, float, float)>();
            foreach (CreaturePart p in asset.parts)
                if (p is SerratedRings r)
                {
                    saved.Add((r, r.bladeStep, r.bladeThick, r.bladeLength, r.bladeSweepDeg, r.bladeAtDeg));
                    r.bladeStep = 0.0004f; r.bladeThick = 0.0006f; r.bladeLength = 2.5f; r.bladeSweepDeg = 65f; r.bladeAtDeg = 0f;   // on the equator: a smaller ring's blade is not lower
                }
            try
            {
                asset.InvalidateCompiled();
                Assert.IsNotNull(BladeClip.TightTurnProblem(asset, asset.scale));
            }
            finally
            {
                foreach ((SerratedRings r, float st, float th, float len, float sw, float at) in saved) { r.bladeStep = st; r.bladeThick = th; r.bladeLength = len; r.bladeSweepDeg = sw; r.bladeAtDeg = at; }
                asset.InvalidateCompiled();
            }
        }
    }

    // Blades never cut through each other: blade k+1's top face (sampled) lies under blade k's bottom face wherever it
    // is inside blade k's plan triangle, same side, in blade k's ring frame.
    internal static class BladeClip
    {
        public static string Problem(CreatureSpecies asset, Matrix4x4[] world, float tolerance)
        {
            CreatureSpecies sp = asset.Compiled.Runtime;
            CreatureSim rest = CreatureRestPose.Sim(sp);
            foreach (CreaturePart p in asset.Compiled.Parts)
            {
                if (!(p is SerratedRings r) || r.bladeLength <= 0f) continue;
                for (int k = 0; k + 1 < r.Rings; k++)
                {
                    ShellBand ra = r.BandAt(sp, rest, k, out int ja, out _), rb = r.BandAt(sp, rest, k + 1, out int jb, out _);
                    Matrix4x4 toA = world[ja].inverse * world[jb];
                    float s = world[ja].lossyScale.x;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        SerratedRings.Blade a = r.BladeOf(sp, ra, k, side), b = r.BladeOf(sp, rb, k + 1, side);
                        for (int i = 0; i <= 8; i++)
                            for (int j = 0; j <= 8 - i; j++)
                            {
                                float w1 = i / 8f, w2 = j / 8f, w0 = 1f - w1 - w2;
                                Vector3 q = toA.MultiplyPoint3x4(SerratedRings.BladePoint(b, w0, w1, w2, 1));
                                float c = SerratedRings.BladeClearance(a, q);
                                if (c < -tolerance) return $"{r.id}: blade of ring {k + 1} (side {side}) cuts {-c * s * 1000f:F2} mm through the blade of ring {k}";
                            }
                    }
                }
            }
            return null;
        }

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
                string e = Problem(asset, world, 1e-4f);
                if (e != null) return $"t={f}: {e}";
            }
            return null;
        }
    }
}
