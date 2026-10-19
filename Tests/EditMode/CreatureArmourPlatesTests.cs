using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The kit's ArmourPlates part on the Scolopendra's tergites: validation, one plate per `step` joints, every plate
    // vertex rigid to its plate joint's bone, the plates cover the body (wider than it and above its back), and the
    // shingling: wherever a plate reaches over the next one, its top lies clearly above the next plate's top (the seam
    // shows as a step, never a z-fight) - in the rest pose and through the minimum-radius circle, where the plates
    // slide over each other. RED: flat (unshingled) plates are caught by the same check.
    public class CreatureArmourPlatesTests
    {
        // the earlier plate's top over the next one's, in the later plate's half heights (flat plates: ~0.08 from the
        // body's taper alone)
        const float kStep = 0.15f;

        static CreatureSpecies Scolo() => CreatureKitAssets.Load("Scolopendra");

        static SpeciesCtx Ctx(int joints) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, false);

        static ArmourPlates Plates(CreatureSpecies sp)
        {
            foreach (CreaturePart p in sp.Compiled.Parts) if (p is ArmourPlates a && !a.ventral) return a;
            return null;
        }

        [Test] public void Plates_Validate_RejectsBadData()
        {
            var ok = new ArmourPlates { id = "t", at = new PartSocket(3, 0, Vector3.zero), toJoint = 21, step = 2 };
            Assert.IsNull(ok.Validate(Ctx(30)));
            Assert.AreEqual(10, ok.PlateCount);
            Assert.AreEqual(0, ok.BoneCount(Ctx(30)), "mesh only");
            var bad = (ArmourPlates)ok.Clone(); bad.toJoint = 30;
            Assert.IsNotNull(bad.Validate(Ctx(30)), "toJoint out of range");
            bad = (ArmourPlates)ok.Clone(); bad.toJoint = 2;
            Assert.IsNotNull(bad.Validate(Ctx(30)), "toJoint before the first");
            bad = (ArmourPlates)ok.Clone(); bad.step = 0;
            Assert.IsNotNull(bad.Validate(Ctx(30)), "no step");
            bad = (ArmourPlates)ok.Clone(); bad.width = 3f;
            Assert.IsNotNull(bad.Validate(Ctx(30)), "too wide");
            bad = (ArmourPlates)ok.Clone(); bad.overlap = 0.8f;
            Assert.IsNotNull(bad.Validate(Ctx(30)), "overlap out of range");
            bad = (ArmourPlates)ok.Clone(); bad.layer = 6;
            Assert.IsNotNull(bad.Validate(Ctx(30)), "an octopus layer");
        }

        [Test] public void Plates_RigidToTheirJoints_OnePerStep()
        {
            CreatureSpecies sp = Scolo();
            ArmourPlates a = Plates(sp);
            Assert.IsNotNull(a);
            Assert.AreEqual(21, a.PlateCount, "20 leg segments and the ultimate segment");
            var joints = new HashSet<int>();
            for (int k = 0; k < a.PlateCount; k++) joints.Add(a.PlateJoint(k));
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            var uv1 = new List<Vector2>(); mesh.GetUVs(1, uv1);
            var uv2 = new List<Vector4>(); mesh.GetUVs(2, uv2);
            int n = 0; var seen = new HashSet<int>();
            for (int i = 0; i < uv1.Count; i++)
            {
                if ((int)uv1[i].x != PartMeshEmitter.PartPlate) continue;
                n++;
                Assert.AreEqual(1f, uv2[i].z, 0f, "rigid: one bone, weight 1");
                Assert.IsTrue(joints.Contains((int)uv2[i].x), $"vertex {i} on bone {uv2[i].x}: a plate joint");
                seen.Add((int)uv2[i].x);
            }
            Assert.Greater(n, 21 * 100, "the plates are drawn");
            Assert.AreEqual(joints.Count, seen.Count, "every plate drawn");
            Object.DestroyImmediate(mesh);
        }

        [Test] public void Plates_CoverTheBody()
        {
            CreatureSpecies sp = Scolo();
            ArmourPlates a = Plates(sp);
            for (int k = 0; k < a.PlateCount; k++)
            {
                int j = a.PlateJoint(k);
                ArmourPlates.Plate p = a.PlateAt(k, sp.spacing, sp.joints[j].halfWidth, sp.joints[j].halfHeight);
                Assert.Greater(p.w, p.hw, $"plate {k} overhangs the flanks");
                for (float u = -0.95f; u <= 0.95f; u += 0.05f)
                {
                    float x = u * a.HalfWidthAt(p, 0.5f);
                    float body = x * x < p.hw * p.hw ? p.hh * Mathf.Sqrt(1f - x * x / (p.hw * p.hw)) : 0f;
                    Assert.Greater(a.TopY(p, u, 0.5f), body + 0.1f * p.hh, $"plate {k} above the body's back at u {u:F2}");
                }
            }
        }

        // ventral: the same row mirrored top to bottom (every vertex), its dome facing the ground
        [Test] public void Plates_Ventral_MirrorsTheRow()
        {
            var up = new ArmourPlates { id = "t", toJoint = 4, keel = 0.2f, cornerSpike = 0.3f };
            var down = (ArmourPlates)up.Clone(); down.ventral = true;
            ArmourPlates.Plate p = up.PlateAt(0, 0.0055f, 0.01f, 0.006f);
            for (float s = 0f; s <= 1f; s += 0.25f)
                for (float u = -1f; u <= 1f; u += 0.25f)
                {
                    Vector3 a = up.Top(p, u, s), b = down.Top(p, u, s), c = up.Bottom(p, u, s), d = down.Bottom(p, u, s);
                    Assert.AreEqual(new Vector3(a.x, -a.y, a.z), b, $"top mirrored at u {u} s {s}");
                    Assert.AreEqual(new Vector3(c.x, -c.y, c.z), d, $"underside mirrored at u {u} s {s}");
                }
        }

        // ------------------------------------------------------------ shingling

        // The smallest height of plate k's top over plate k + 1's top where k reaches over k + 1 (world space, the
        // plates rigid to their joints of `sim`), as a fraction of plate k + 1's half height. Points of the later plate
        // are taken into the earlier plate's joint frame and compared with its top there.
        static float WorstStep(CreatureSpecies sp, ArmourPlates a, CreatureSim sim, float scale)
        {
            float worst = float.MaxValue;
            for (int k = 0; k + 1 < a.PlateCount; k++)
            {
                int j0 = a.PlateJoint(k), j1 = a.PlateJoint(k + 1);
                ArmourPlates.Plate p0 = a.PlateAt(k, sp.spacing, sp.joints[j0].halfWidth, sp.joints[j0].halfHeight);
                ArmourPlates.Plate p1 = a.PlateAt(k + 1, sp.spacing, sp.joints[j1].halfWidth, sp.joints[j1].halfHeight);
                Quaternion inv0 = Quaternion.Inverse(sim.Rotations[j0]);
                for (float s = 0f; s <= 0.4f; s += 0.02f)
                    for (float u = -0.9f; u <= 0.9f; u += 0.05f)
                    {
                        Vector3 q1 = a.Top(p1, u, s);
                        Vector3 w = sim.Joints[j1] + sim.Rotations[j1] * (q1 * scale);
                        Vector3 l = inv0 * (w - sim.Joints[j0]) / scale;   // plate k's joint frame, scale 1
                        float s0 = (p0.zFront - l.z) / (p0.zFront - p0.zBack);
                        if (s0 < 0.02f || s0 > 0.98f) continue;
                        float hw0 = a.HalfWidthAt(p0, s0);
                        if (Mathf.Abs(l.x) > 0.9f * hw0) continue;
                        float above = a.TopY(p0, l.x / hw0, s0) - l.y;
                        worst = Mathf.Min(worst, above / p1.hh);
                    }
            }
            return worst;
        }

        static CreatureSim Rest(CreatureSpecies sp, float s) =>
            new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, 1);

        [Test] public void Plates_Shingled_EachReachesOverTheNext()
        {
            CreatureSpecies sp = Scolo();
            ArmourPlates a = Plates(sp);
            float rest = WorstStep(sp, a, Rest(sp, 1f), 1f);
            // through the minimum-radius circle (the kit's MaxBend walk): the plates slide over each other
            CreatureSim sim = Rest(sp, sp.scale);
            float bend = float.MaxValue;
            for (int t = 0; t < 300; t++)
            {
                Vector3 fwd = sim.Rotations[0] * Vector3.forward, right = sim.Rotations[0] * Vector3.right;
                sim.Tick(1f / 60f, sim.Joints[0] + (fwd + 2f * right) * sim.SizeScale);
                if (t % 10 == 0) bend = Mathf.Min(bend, WorstStep(sp, a, sim, sim.SizeScale));
            }
            TestContext.WriteLine($"plate over the next: >= {rest:F3} half heights at rest, >= {bend:F3} through a tight circle");
            Assert.Greater(rest, kStep, "at rest every plate's back lies above the next plate's front: the seam is a step");
            Assert.Greater(bend, 0.5f * kStep, "bending, the earlier plate stays on top");
        }

        [Test] public void Red_FlatPlates_NoStep()
        {
            CreatureSpecies sp = Scolo();
            var a = (ArmourPlates)Plates(sp).Clone();
            a.slope = 0f; a.rim = 0f;
            float rest = WorstStep(sp, a, Rest(sp, 1f), 1f);
            TestContext.WriteLine($"RED: unshingled plates: {rest:F3} half heights");
            Assert.Less(rest, kStep, "the shingling check bites: flat plates step only by the body's taper");
        }
    }
}
