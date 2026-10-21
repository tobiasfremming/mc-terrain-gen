using System.Collections.Generic;
using CreatureKit;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Vesper step 1: the opt-in "skeleton only" switch (the SkeletonOnly look module -> CreatureSpecies.skeletonOnly).
    // Such a species is simulated and ticked like any other, but its CreatureRenderer (the one draw path:
    // CreatureWorld.Spawn / AddBody -> RendererFor) builds no mesh and submits nothing; the F3 view still draws its
    // skeleton from the sim's bones. Off - every shipped species - the renderer is unchanged.
    public class CreatureSkeletonOnlyTests
    {
        readonly List<CreatureRenderer> renderers = new List<CreatureRenderer>();

        [TearDown] public void TearDown()
        {
            CreatureRenderer.IgnoreSkeletonOnlyForTest = false;
            foreach (CreatureRenderer r in renderers) r.Dispose();
            renderers.Clear();
        }

        static CreatureSpecies Species(bool skeletonOnly)
        {
            var parts = new List<CreaturePart> { CreatureKitComposeTests.Body(8), CreatureKitComposeTests.Leg("leg", 3), CreatureKitComposeTests.TwoEyes() };
            var modules = new List<CreatureModule> { new Walk { speed = 0.6f, turnRateDeg = 160f, accel = 2f } };
            if (skeletonOnly) modules.Add(new SkeletonOnly());
            return FlowKit.Build(skeletonOnly ? "SkeletonOnlyFixture" : "SkinnedFixture", 1f, parts, modules);
        }

        // A renderer drawing one individual of sp, rendered every frame of a 120-frame walk.
        CreatureRenderer RunDrawn(CreatureSpecies sp, out CreatureSim sim)
        {
            sim = FlowKit.Sim(sp, 1f, 3);
            var r = new CreatureRenderer(sp, null);
            renderers.Add(r);
            r.Add(sim, LabGroup.Individual(sp, 3, sp.scale));
            for (int f = 0; f < 120; f++) { sim.Tick(FlowKit.kDt, new Vector3(0f, 0f, 3f)); r.Render(); }
            return r;
        }

        // Nothing is drawn: no mesh built, no frame submitted.
        static void AssertDrawsNothing(CreatureRenderer r)
        {
            Assert.IsNull(r.Mesh, "a skeleton-only species built a mesh");
            Assert.AreEqual(0, r.SubmittedForTest, "a skeleton-only species was submitted for drawing");
        }

        [Test] public void SkeletonOnly_ModuleSetsFlag()
        {
            Assert.IsTrue(Species(true).skeletonOnly);
            Assert.IsFalse(Species(false).skeletonOnly);
        }

        [Test] public void SkeletonOnly_SpawnsAndTicks_DrawsNothing()
        {
            CreatureSpecies sp = Species(true);
            CreatureRenderer r = RunDrawn(sp, out CreatureSim sim);
            AssertDrawsNothing(r);
            Assert.AreEqual(120, r.SkeletonPosedForTest, "a skeleton-only species is still posed every frame (no draw)");
            Assert.AreEqual(1, r.Count, "the individual is still registered (despawn / remove work as before)");
            Assert.Greater(sim.Joints[0].z, 0.2f, "the sim did not walk");
            // the bones F3 (CreatureDebugView) and the part runtimes read are still written
            foreach (Matrix4x4 m in FlowKit.Bones(sim))
                Assert.IsFalse(float.IsNaN(m.m03) || float.IsNaN(m.m13) || float.IsNaN(m.m23), "NaN bone");
            Assert.IsTrue(r.Remove(sim));
            Assert.AreEqual(0, r.Count);
        }

        // The same species without the module is drawn: a mesh, every frame submitted.
        [Test] public void SkeletonOnly_Off_Draws()
        {
            CreatureRenderer r = RunDrawn(Species(false), out _);
            Assert.IsNotNull(r.Mesh);
            Assert.AreEqual(120, r.SubmittedForTest);
        }

        // A species that lists the SkeletonOnly module (Vesper, step 1 of a skeleton-first build) may be skeleton-only;
        // every other shipped species is off. DesertMonitor's drawn mesh is byte-identical to its golden mesh.
        static bool ListsSkeletonOnly(CreatureSpecies sp)
        {
            foreach (CreatureModule m in CreatureKit.KitView.Modules(sp)) if (m is SkeletonOnly) return true;
            return false;
        }

        [Test] public void SkeletonOnly_Off_ShippedUnchanged()
        {
            foreach (string n in CreatureKitAssets.Species())
            {
                CreatureSpecies shipped = CreatureKitAssets.Load(n);
                if (!ListsSkeletonOnly(shipped)) Assert.IsFalse(shipped.skeletonOnly, n + " is skeleton-only");
            }
            Assert.IsTrue(CreatureKitAssets.Load("Vesper").skeletonOnly, "Vesper is the deliberately skeleton-only species");
            CreatureSpecies sp = CreatureKitAssets.Load("DesertMonitor");
            CreatureRenderer r = RunDrawn(sp, out _);
            Assert.IsNotNull(r.Mesh);
            Assert.AreEqual(120, r.SubmittedForTest);
            ulong drawn = LegacyLegsGoldenTests.MeshHashOf(r.Mesh);
            Assert.AreEqual(LegacyLegsGoldenTests.GoldenMesh("DesertMonitor"), drawn, $"DesertMonitor's drawn mesh changed: 0x{drawn:X16}");
        }

        // The kit's mesh-based checks skip a skeleton-only species with one reason; every other check still runs,
        // and the same species with a skin skips none of them.
        [Test] public void SkeletonOnly_KitSkipsMeshChecks()
        {
            CreatureSpecies bare = Species(true), skinned = Species(false);
            foreach (string c in new[] { CreatureKitAssets.MeshTime, CreatureKitAssets.MaxBend, CreatureKitAssets.Bounds, CreatureKitAssets.PoseSweep, CreatureKitAssets.Group })
            {
                Assert.AreEqual(CreatureKitAssets.SkeletonOnlySkip, CreatureKitAssets.SkipReason(bare, c), c);
                Assert.IsNull(CreatureKitAssets.SkipReason(skinned, c), c);
            }
            foreach (string c in new[] { CreatureKitAssets.Validate, CreatureKitAssets.NoMissingTypes, CreatureKitAssets.Layout, CreatureKitAssets.Wander,
                                         CreatureKitAssets.Gait, CreatureKitAssets.Size, CreatureKitAssets.Gc, CreatureKitAssets.Perf, CreatureKitAssets.Lab })
                Assert.IsNull(CreatureKitAssets.SkipReason(bare, c), c);
        }

        // RED: the flag set but the renderer's skip forced off (a test hook) - a mesh is built and drawn, the
        // draws-nothing assertion fails.
        [Test] public void Red_SkeletonOnly_Ignored()
        {
            CreatureRenderer.IgnoreSkeletonOnlyForTest = true;
            CreatureRenderer r = RunDrawn(Species(true), out _);
            Assert.Throws<AssertionException>(() => AssertDrawsNothing(r));
        }
    }
}
