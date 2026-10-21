using System;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests.Perf
{
    // Creature kit: the generic kit's two timing checks (CreatureKitTests 4 and 11), one fixture per species
    // asset like CreatureKitTests. Kept in their own class and namespace so a routine kit run (the run_tests
    // tool cannot exclude a category) leaves them out; run them on a quiet machine with the group filter
    // `TerrainGen.Tests.Perf.CreatureKitPerfTests`, offline with `creature_kit.py test CreatureKitPerfTests`.
    [TestFixtureSource(typeof(CreatureKitAssets), nameof(CreatureKitAssets.Species))]
    [Category("Perf")]
    public class CreatureKitPerfTests
    {
        const float kMeshBudgetMs = 5f;

        readonly string name;
        CreatureSpecies sp;

        public CreatureKitPerfTests(string species) { name = species; }

        [OneTimeSetUp] public void LoadSpecies() { sp = CreatureKitAssets.Load(name); }

        void Applies(string check)
        {
            Assert.IsNotNull(sp, CreatureKitAssets.PathOf(name) + " did not load as a CreatureSpecies");
            string why = CreatureKitAssets.SkipReason(sp, check);
            if (why != null) Assert.Ignore(why);
        }

        // 4. The rest mesh builds in under 5 ms (it is built at runtime, per species).
        [Test] public void MeshTime()
        {
            Applies(CreatureKitAssets.MeshTime);
            double best = double.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                var sw = Stopwatch.StartNew();
                Mesh m = CreatureKitAssets.BuildMesh(sp);
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
                Object.DestroyImmediate(m);
            }
            TestContext.Out.WriteLine($"{name}: mesh build {best:F2} ms (best of 3)");
            Assert.Less(best, kMeshBudgetMs, name + ": mesh build time");
        }

        // 4b. The first (uncached) build: the surface parts' rest meshes (RestMeshCache), the head's built geometry
        // (CrestBeakHead) and its SDF polygonisations (SculptMesher) all cleared, one build timed. It runs once per
        // species per session (the lab prewarms it, a frame per species, before it places the enclosures), never per
        // frame, so the bound is a generous regression tripwire (~10x the Apex's first build in a process, JIT included: ~0.3 s offline), not a budget:
        // a tight bound would mostly measure the machine's load.
        const float kColdBuildLimitMs = 3000f;

        [Test] public void MeshTimeCold()
        {
            Applies(CreatureKitAssets.MeshTime);
            var first = Stopwatch.StartNew();
            Object.DestroyImmediate(CreatureKitAssets.BuildMesh(sp));   // JIT and first-use statics out of the timing
            double warmup = first.Elapsed.TotalMilliseconds;
            CreatureKit.RestMeshCache.ClearForTest(); CreatureKit.SculptMesher.ClearCache(); CreatureKit.CrestBeakHead.ClearBuiltForTest();
            var sw = Stopwatch.StartNew();
            Mesh m = CreatureKitAssets.BuildMesh(sp);
            double cold = sw.Elapsed.TotalMilliseconds;
            int verts = m.vertexCount;
            Object.DestroyImmediate(m);
            TestContext.Out.WriteLine($"{name}: cold mesh build {cold:F1} ms ({verts} vertices, caches cleared; the build before it {warmup:F1} ms)");
            Assert.Less(cold, kColdBuildLimitMs, name + ": cold mesh build time");
        }

        // 11. One individual's tick + bone write within the species' budget (flat ground; run on a quiet machine).
        [Test] public void Perf()
        {
            Applies(CreatureKitAssets.Perf);
            double median = KitChecks.TickMedianMs(sp);
            float budget = CreatureKitAssets.PerfBudgetMs(sp);
            TestContext.Out.WriteLine($"{name}: median tick + bones {median * 1000.0:F1} us (budget {budget * 1000f:F0} us)");
            Assert.LessOrEqual(median, budget, name + ": per-frame cost");
        }

    }
}
