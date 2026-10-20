using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // Kit services (2026-10-20): the group registry, separation helpers, the lab's group placement, the mesh-fitted
    // bone capsules and the pose phases.
    public class CreatureKitServicesTests
    {
        [SetUp] public void SetUp() => KitGroup.ClearAll();
        [TearDown] public void TearDown() => KitGroup.ClearAll();

        static CreatureSim Sim(CreatureSpecies sp, Vector3 at, int seed)
            => new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, at, Vector3.forward, sp.scale, seed);

        sealed class Counter { public int value; }

        [Test] public void Group_JoinLiveNearest_SharedStartsOver()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("DesertMonitor");
            var area = EnclosureArea.For(Vector3.zero, Vector3.up, 5f, sp, sp.scale);
            KitGroup g = KitGroup.For(sp, area);
            Assert.AreSame(g, KitGroup.For(sp, area), "one group per species and enclosure");
            var other = EnclosureArea.For(new Vector3(30f, 0f, 0f), Vector3.up, 5f, sp, sp.scale);
            Assert.AreNotSame(g, KitGroup.For(sp, other), "another enclosure, another group");

            CreatureSim a = Sim(sp, Vector3.zero, 1), b = Sim(sp, new Vector3(1f, 0f, 0f), 2), c = Sim(sp, new Vector3(4f, 0f, 0f), 3);
            Assert.IsTrue(g.Join(a), "the first member starts the group");
            g.Shared<Counter>().value = 5;
            Assert.IsFalse(g.Join(b)); Assert.IsFalse(g.Join(c)); Assert.IsFalse(g.Join(c), "joining twice is a no-op");
            Assert.AreEqual(3, g.Members.Count);
            Assert.AreEqual(2, g.LiveCount(a));
            Assert.AreSame(b, g.Nearest(a, 10f));
            Assert.IsNull(g.Nearest(a, 0.5f), "none within 0.5 m");
            var buf = new CreatureSim[4];
            Assert.AreEqual(1, g.Neighbours(a, 2f, buf));
            Assert.AreEqual(5, g.Shared<Counter>().value, "shared state is the group's");

            // a group whose members' clocks stopped long ago (a respawn, a new test) starts over: members and state
            CreatureSim d = Sim(sp, Vector3.zero, 4);
            for (int t = 0; t < 60; t++) d.Tick(1f / 60f, Vector3.forward);
            Assert.IsTrue(g.Join(d), "a newcomer whose clock runs 1 s ahead finds no live member");
            Assert.AreEqual(1, g.Members.Count);
            Assert.AreEqual(0, g.Shared<Counter>().value, "the shared state starts over too");
        }

        [Test] public void Separation_ClosestApproach()
        {
            // head on along x, 10 m apart at 1 m/s each: they meet after 5 s (horizon 10), 1 m apart in z
            Vector3 d = Separation.ClosestApproach(new Vector3(-5f, 0f, 0f), Vector3.right, new Vector3(5f, 0f, 1f), Vector3.left, 10f, out float tc);
            Assert.AreEqual(5f, tc, 1e-4f);
            Assert.AreEqual(1f, d.magnitude, 1e-4f);
            // separating: now is the closest
            Separation.ClosestApproach(Vector3.zero, Vector3.left, Vector3.right, Vector3.right, 10f, out tc);
            Assert.AreEqual(0f, tc);
            // the horizon caps it
            Separation.ClosestApproach(new Vector3(-50f, 0f, 0f), Vector3.right, Vector3.zero, Vector3.zero, 3f, out tc);
            Assert.AreEqual(3f, tc);
            Assert.AreEqual(1f, Separation.Weight(0f, 2f)); Assert.AreEqual(0.5f, Separation.Weight(1f, 2f)); Assert.AreEqual(0f, Separation.Weight(3f, 2f));
        }

        [Test] public void LabGroup_Placement_Seeds_Sizes()
        {
            LabGroup.Start(Vector3.zero, Vector3.up, Vector3.forward, 10f, 0, 1, out Vector3 at, out Vector3 dir);
            Assert.AreEqual(Vector3.zero, at); Assert.AreEqual(Vector3.forward, dir);
            for (int i = 0; i < 5; i++)
            {
                LabGroup.Start(Vector3.zero, Vector3.up, Vector3.forward, 10f, i, 5, out at, out dir);
                Assert.AreEqual(4f, at.magnitude, 1e-4f, "on the ring at 0.4 of the safe radius");
            }
            Assert.AreEqual(7 + 3, LabGroup.Seed(7, 3, 2, 1));
            Assert.AreEqual(7 + 3 + 1000 + 2, LabGroup.Seed(7, 3, 2, 5));
            CreatureSpecies sp = CreatureKitAssets.Load("RockCrab");
            float j = Mathf.Abs(sp.look.sizeJitter);
            for (int s = 0; s < 20; s++)
            {
                float k = LabGroup.Individual(sp, s, 1f).sizeScale;
                Assert.That(k >= 1f - j - 1e-6f && k <= 1f + j + 1e-6f, $"seed {s}: size {k} within the jitter");
            }
        }

        // A tube of points along z (radius 0.1, length 1) on one bone: one capsule along z, radius 0.1, ends 0.1 in.
        [Test] public void BoneCapsules_FitATube()
        {
            var v = new List<Vector3>(); var skin = new List<Vector4>();
            for (int r = 0; r <= 10; r++)
                for (int k = 0; k < 12; k++)
                {
                    float a = 2f * Mathf.PI * k / 12f;
                    v.Add(new Vector3(0.1f * Mathf.Cos(a), 0.1f * Mathf.Sin(a), r * 0.1f));
                    skin.Add(new Vector4(0, 0, 1, 0));
                }
            BoneCapsules c = BoneCapsules.Fit(v.ToArray(), skin, new[] { Matrix4x4.identity });
            Assert.AreEqual(1, c.Count);
            Assert.AreEqual(0.1f, c.R[0], 1e-3f);
            Assert.AreEqual(0.1f, Mathf.Min(c.A[0].z, c.B[0].z), 1e-3f);
            Assert.AreEqual(0.9f, Mathf.Max(c.A[0].z, c.B[0].z), 1e-3f);
            // carried by a bone at scale 2, moved 3 m up
            var a2 = new Vector3[1]; var b2 = new Vector3[1]; var r2 = new float[1];
            c.World(new[] { Matrix4x4.TRS(Vector3.up * 3f, Quaternion.identity, Vector3.one * 2f) }, a2, b2, r2);
            Assert.AreEqual(0.2f, r2[0], 1e-3f);
            Assert.AreEqual(3f, a2[0].y, 1e-3f);
        }

        // A flat sheet (1 x 1, 2 mm thick) is several thin capsules, not one fat cylinder.
        [Test] public void BoneCapsules_SheetIsSplit()
        {
            var v = new List<Vector3>(); var skin = new List<Vector4>();
            for (int x = 0; x <= 10; x++) for (int z = 0; z <= 20; z++) for (int y = -1; y <= 1; y += 2)
            {
                v.Add(new Vector3(x * 0.1f, 0.001f * y, z * 0.05f)); skin.Add(new Vector4(0, 0, 1, 0));
            }
            BoneCapsules c = BoneCapsules.Fit(v.ToArray(), skin, new[] { Matrix4x4.identity });
            Assert.AreEqual(4, c.Count, "split across its width");
            for (int i = 0; i < c.Count; i++) Assert.Less(c.R[i], 0.2f, $"capsule {i}: radius {c.R[i]} (a single capsule's would be 0.5)");
        }

        [Test] public void SegmentDistance_Cases()
        {
            Assert.AreEqual(1f, BoneCapsules.SegmentDistance(Vector3.zero, Vector3.right, new Vector3(0.5f, 1f, 0f), new Vector3(0.5f, 1f, 1f), out _, out _), 1e-5f, "crossing, 1 apart");
            Assert.AreEqual(1f, BoneCapsules.SegmentDistance(Vector3.zero, Vector3.right, new Vector3(2f, 0f, 0f), new Vector3(3f, 0f, 0f), out _, out _), 1e-5f, "collinear, end to end");
            Assert.AreEqual(Mathf.Sqrt(2f), BoneCapsules.SegmentDistance(Vector3.zero, Vector3.zero, Vector3.one - Vector3.up, Vector3.one - Vector3.up, out _, out _), 1e-5f, "two points");
        }

        // The phase channels of a flyer and a burrower, and the walk-only label of a plain walker.
        [Test] public void PosePhases_Channels()
        {
            CreatureSpecies dragon = CreatureKitAssets.Load("Hexadrake");
            var sim = Sim(dragon, Vector3.zero, 1);
            List<IPosePhases> ch = PosePhases.Collect(sim, null);
            Assert.IsTrue(ch.Exists(c => c.PhaseChannel == "flight"), "a flyer has the flight channel");
            Assert.AreEqual("flight=wing-walk", PosePhases.Label(ch), "on the ground it wing-walks");
            CreatureSpecies delver = CreatureKitAssets.Load("Delver");
            Assert.IsTrue(PosePhases.Collect(Sim(delver, Vector3.zero, 1), null).Exists(c => c.PhaseChannel == "burrow"));
            CreatureSpecies monitor = CreatureKitAssets.Load("DesertMonitor");
            Assert.AreEqual("walk", PosePhases.Label(PosePhases.Collect(Sim(monitor, Vector3.zero, 1), null)));
        }
    }
}
