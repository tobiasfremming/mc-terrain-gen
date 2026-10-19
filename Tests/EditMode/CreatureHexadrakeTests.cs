using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The Hexadrake (creature kit T5.4/T5.5): a six-limbed dragon - four walking legs and two wings whose arms are
    // LimbRole.Folded: on the ground the wrist is held at its folded point (never plants, never steps) and the wing
    // folds on it; in the air the wing flies as the winged serpent's does. Proven: compose and validation of the
    // Folded role, the folded wings never plant and hold their pose through a weaving walk without a membrane
    // triangle flipping, the wrist stays clear of the ground, and it launches, flies and lands with its wings
    // folding again. RED: the same arm as a WingArm walks (it plants and steps).
    public class CreatureHexadrakeTests
    {
        const float kDt = 1f / 60f;

        static CreatureSpecies Drake() => CreatureKitAssets.Load("Hexadrake").Compiled.Runtime;

        static CreatureSim Walker(CreatureSpecies sp, float s, int seed = 1) =>
            new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, seed);

        static Vector3 Weave(CreatureSim sim, int t)
        {
            Vector3 fwd = sim.Rotations[0] * Vector3.forward;
            return sim.Joints[0] + Quaternion.AngleAxis(60f * Mathf.Sin(t / 45f), Vector3.up) * fwd * (2f * sim.SizeScale);
        }

        static List<int> WingLegs(CreatureSpecies sp)
        {
            var l = new List<int>();
            for (int w = 0; w < sp.WingCount; w++) l.Add(sp.wings[w].leg);
            return l;
        }

        [Test] public void Folded_Compose_IsAWingArmThatNeverWalks()
        {
            CompiledSpecies c = CreatureKitAssets.LoadAsset("Hexadrake").Compiled;
            Assert.IsNull(c.Error, c.Error);
            int folded = 0, walking = 0;
            foreach (LimbPart l in c.Limbs)
            {
                if (l.role == LimbRole.Folded) { folded++; Assert.IsTrue(l.ToLegSpec().feeler, l.id + ": a folded arm is posed, never plants"); Assert.IsFalse(l.Walks); }
                else { walking++; Assert.AreEqual(LimbRole.Walk, l.role, l.id); }
            }
            Assert.AreEqual(2, folded, "two folded wing arms");
            Assert.AreEqual(4, walking, "four walking legs");
            Assert.AreEqual(2, c.Wings.Count);
            CreatureSpecies sp = Drake();
            Assert.AreEqual(6, sp.legs.Length);
            foreach (int l in WingLegs(sp)) Assert.IsTrue(sp.legs[l].feeler, $"wing leg {l} is posed");

            // Folded is only for a Wing's arm
            var lone = new Leg2Bone { id = "arm", role = LimbRole.Folded, at = new PartSocket(2, 1, Vector3.zero), upperLength = 0.1f, lowerLength = 0.1f };
            var ctx = new SpeciesCtx(new CreaturePart[] { lone }, new CreatureModule[0], 10, false);
            StringAssert.Contains("Folded", lone.Validate(ctx) ?? "none");
        }

        // The folded wings never plant and never step; the wrist stays at its folded point in the shoulder's frame
        // (it rides the body), clear of the ground; every walking leg steps.
        [TestCase(0.9f)] [TestCase(1f)] [TestCase(1.1f)]
        public void FoldedWing_NeverPlants(float k)
        {
            CreatureSpecies sp = Drake();
            float s = sp.scale * k;
            var sim = Walker(sp, s);
            List<int> arms = WingLegs(sp);
            var steps = new int[sp.legs.Length]; var was = new bool[sp.legs.Length];
            var bones = new Matrix4x4[sp.BoneCount];
            float worstOff = 0f, lowest = float.PositiveInfinity;
            for (int t = 0; t < 1200; t++)
            {
                sim.Tick(kDt * Mathf.Sqrt(k), Weave(sim, t));
                sim.WriteBoneWorld(bones, 0);   // poses the wings (the wrist on the held arm)
                for (int l = 0; l < sp.legs.Length; l++)
                {
                    if (sim.Legs[l].stepping && !was[l]) steps[l]++;
                    was[l] = sim.Legs[l].stepping;
                }
                foreach (int l in arms)
                {
                    LegSpec a = sp.legs[l];
                    Vector3 rest = a.restOffset; rest.x *= a.side;
                    Vector3 want = sim.Joints[a.attachJoint] + sim.Rotations[a.attachJoint] * (rest * s);
                    worstOff = Mathf.Max(worstOff, (sim.Flight.Wings.State[arms.IndexOf(l)].wrist - want).magnitude);
                    lowest = Mathf.Min(lowest, sim.Flight.Wings.State[arms.IndexOf(l)].wrist.y);
                }
            }
            TestContext.Out.WriteLine($"x{k:F1}: steps {string.Join(", ", steps)}; wrist off its fold point <= {worstOff * 1000f:F2} mm, lowest wrist {lowest:F3} m");
            foreach (int l in arms) Assert.AreEqual(0, steps[l], $"folded arm {l} stepped");
            for (int l = 0; l < sp.legs.Length; l++) if (!arms.Contains(l)) Assert.GreaterOrEqual(steps[l], 5, $"leg {l} walks");
            Assert.Less(worstOff, 1e-3f * s, "the wrist rides its folded point");
            Assert.Greater(lowest, 0.25f * s, "the folded wrist stays well clear of the ground");
        }

        // RED: the same asset with the arms as WingArms (the winged serpent's wing-walk) plants and steps on them.
        [Test] public void Red_WingArm_Walks()
        {
            CreatureSpecies sp = Drake();
            List<int> arms = WingLegs(sp);
            foreach (int l in arms) sp.legs[l].feeler = false;
            try
            {
                var sim = Walker(sp, sp.scale);
                int steps = 0;
                for (int t = 0; t < 600; t++)
                {
                    sim.Tick(kDt, new Vector3(0f, 0f, 1000f));
                    foreach (int l in arms) if (sim.Legs[l].stepping) steps++;
                }
                TestContext.Out.WriteLine($"RED: walked arms stepping on {steps} leg-ticks");
                Assert.Greater(steps, 0, "the never-plants check bites: a walked arm steps");
            }
            finally { foreach (int l in arms) sp.legs[l].feeler = true; }
        }

        // Through a weaving walk, tight turns and stops, the folded wing's membrane never turns inside out, and
        // every finger tip moves continuously (no pose flips between frames).
        [Test] public void FoldedWing_PoseStable_NoMembraneFlips()
        {
            CreatureSpecies sp = Drake();
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            Vector3[] v = mesh.vertices; int[] tri = mesh.triangles;
            var uv1 = new List<Vector2>(); var uv2 = new List<Vector4>();
            mesh.GetUVs(1, uv1); mesh.GetUVs(2, uv2);
            Object.DestroyImmediate(mesh);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            var world = new Matrix4x4[sp.BoneCount]; var skin = new Matrix4x4[sp.BoneCount];
            var pos = new Vector3[v.Length];
            var sim = Walker(sp, sp.scale, 3);
            CreatureWings W = sim.Flight.Wings;
            int fingers = 0;
            for (int w = 0; w < W.Count; w++) fingers += W.FingerCount(w);
            var prevTip = new Vector3[fingers];   // in the wing's attach-joint frame: the fold's own motion
            float worstJump = 0f; int frames = 0, membrane = 0;
            for (int t = 0; t < 1800; t++)
            {
                // weave, then a tight circle, then stand
                Vector3 goal = t < 900 ? Weave(sim, t) : t < 1500 ? new Vector3(Mathf.Cos(t / 40f), 0f, Mathf.Sin(t / 40f)) * (0.3f * sp.scale) : sim.Joints[0];
                sim.Tick(kDt, goal);
                sim.WriteBoneWorld(world, 0);
                for (int i = 0, f = 0; i < W.Count; i++)
                    for (int kf = 0; kf < W.FingerCount(i); kf++, f++)
                    {
                        int aj = sp.legs[sp.wings[i].leg].attachJoint;
                        Vector3 tip = Quaternion.Inverse(sim.Rotations[aj]) * (W.FingerTip[W.FingerIndex(i, kf)] - sim.Joints[aj]);
                        if (t > 0) worstJump = Mathf.Max(worstJump, (tip - prevTip[f]).magnitude);
                        prevTip[f] = tip;
                    }
                if (t % 3 != 2) continue;
                frames++;
                for (int b = 0; b < skin.Length; b++) skin[b] = world[b] * bind[b];
                for (int i = 0; i < pos.Length; i++)
                {
                    Vector4 sk = uv2[i];
                    pos[i] = sk.z * skin[(int)sk.x].MultiplyPoint3x4(v[i]) + sk.w * skin[(int)sk.y].MultiplyPoint3x4(v[i]);
                }
                for (int q = 0; q < tri.Length; q += 3)
                {
                    int i0 = tri[q], i1 = tri[q + 1], i2 = tri[q + 2];
                    if (Mathf.RoundToInt(uv1[i0].x) != 8) continue;
                    if (frames == 1) membrane++;
                    Vector3 rest = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]);
                    Vector3 now = Vector3.Cross(pos[i1] - pos[i0], pos[i2] - pos[i0]);
                    Vector3 want = skin[(int)uv2[i0].x].MultiplyVector(rest);
                    Assert.IsTrue(Vector3.Dot(now, want) > 0f, $"t={sim.Time:F2}: membrane triangle {q / 3} (bones {uv2[i0]}) inverted");
                }
            }
            // in its shoulder's frame a folded finger tip only sways with the gait: a pose flip jumps it
            float bound = 0.03f * sp.scale;
            TestContext.Out.WriteLine($"{frames} frames x {membrane} membrane triangles; fastest finger tip {worstJump * 1000f:F1} mm/frame (bound {bound * 1000f:F1})");
            Assert.Less(worstJump, bound, "a folded finger jumped between frames");
        }

        // In its aviary it launches, flies, lands, and folds its wings back onto the held arms after touching down.
        [Test] public void Hexadrake_FliesAndLands_FoldsAgain()
        {
            CreatureSpecies sp = Drake();
            EnclosureArea area = KitChecks.LabArea(sp);
            const float dt = 1f / 30f;
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, area.center, Vector3.forward, sp.scale, 2) { Bounds = area };
            var brain = new FlightBrain(area, 2);
            List<int> arms = WingLegs(sp);
            var bones = new Matrix4x4[sp.BoneCount];
            float groundAfterLanding = 0f; int foldedChecks = 0;
            for (int t = 0; t < (int)(240f / dt); t++)
            {
                sim.Tick(dt, brain.Target(sim, dt));
                sim.WriteBoneWorld(bones, 0);
                if (sim.Flight.Landings > 0 && sim.Flight.Mode == FlightMode.Ground)
                {
                    groundAfterLanding += dt;
                    if (groundAfterLanding > 2f * Mathf.Sqrt(sp.scale))
                    {
                        foreach (int l in arms)
                        {
                            LegSpec a = sp.legs[l];
                            Vector3 rest = a.restOffset; rest.x *= a.side;
                            Vector3 want = sim.Joints[a.attachJoint] + sim.Rotations[a.attachJoint] * (rest * sim.SizeScale);
                            Assert.Less((sim.Flight.Wings.State[arms.IndexOf(l)].wrist - want).magnitude, 1e-3f * sp.scale, "landed: the wing folds back onto the held arm");
                            Assert.AreEqual(1f, sim.Flight.Wings.State[arms.IndexOf(l)].fold, 1e-4f, "landed: folded");
                        }
                        foldedChecks++;
                    }
                }
                else groundAfterLanding = 0f;
            }
            TestContext.Out.WriteLine($"launches {sim.Flight.Launches}, landings {sim.Flight.Landings}, folded-on-the-ground checks {foldedChecks}");
            Assert.GreaterOrEqual(sim.Flight.Launches, 1, "it flies");
            Assert.GreaterOrEqual(sim.Flight.Landings, 1, "it lands");
            Assert.Greater(foldedChecks, 0, "it stood on the ground after a landing");
        }
    }
}
