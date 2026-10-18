using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using TerrainGen.Tests;
using UnityEditor;
using UnityEngine;

// Timing tests live in their own namespace so the routine creature filter (`TerrainGen.Tests.Creature`,
// a regex on the full test name) does not run them: wall-time bounds are noisy in a busy Editor. They still
// run in a full EditMode run, and on demand with the group filter `TerrainGen.Tests.Perf` or category `Perf`.
namespace TerrainGen.Tests.Perf
{
    // Simulation cost on the real lab terrain (ruling R3: Config/Test/DesertWorld.asset, the BiomeDensityField
    // the Creature Lab uses), with the ground set up exactly as CreatureWorld.Spawn sets it up.
    // Median of 300 frames after 60 warm-up frames (the warm-up includes the ground cache's first fill).
    [Category("Perf")]
    public class CreaturePerfTests
    {
        const float kDt = 1f / 60f;
        const int kWarmup = 60, kFrames = 300;

        DensityField field;

        [SetUp] public void SetUp() => field = CreatureDesertWorld.Field();

        // Per-frame wall time of ticking every sim once (median over kFrames after kWarmup), plus field samples
        // and the managed allocations over the timed frames (GcAllocs: a counter that works on Unity's Mono).
        static double Run(List<CreatureSim> sims, List<WanderBrain> brains, FieldGroundProbe shared, out double meanMs, out double maxMs, out float samplesPerFrame, out int gcAllocs)
        {
            var sw = new Stopwatch();
            var times = new double[kFrames];
            for (int f = 0; f < kWarmup; f++)
                for (int i = 0; i < sims.Count; i++) sims[i].Tick(kDt, brains[i].Target(sims[i], kDt));
            int s0 = shared.SampleCount;
            gcAllocs = GcAllocs.Count(() =>
            {
                for (int f = 0; f < kFrames; f++)
                {
                    sw.Restart();
                    for (int i = 0; i < sims.Count; i++) sims[i].Tick(kDt, brains[i].Target(sims[i], kDt));
                    sw.Stop();
                    times[f] = sw.Elapsed.TotalMilliseconds;
                }
            });
            samplesPerFrame = (shared.SampleCount - s0) / (float)kFrames;
            double sum = 0, max = 0;
            for (int i = 0; i < kFrames; i++) { sum += times[i]; max = Math.Max(max, times[i]); }
            meanMs = sum / kFrames; maxMs = max;
            Array.Sort(times);
            return times[kFrames / 2];
        }

        [Test] public void Perf_OneLizard_Under0_1ms()
        {
            var shared = new FieldGroundProbe(field);
            var sims = CreatureDesertWorld.Spawn(shared, 1, out var brains, out var caches);
            double median = Run(sims, brains, shared, out double mean, out double max, out float spf, out int gc);
            float moved = (sims[0].Joints[0] - CreatureDesertWorld.Ground(shared, CreatureDesertWorld.kLabCentre)).magnitude;
            TestContext.Out.WriteLine($"1 monitor on DesertWorld: median {median:F4} ms, mean {mean:F4} ms, max {max:F4} ms; " +
                $"field samples/frame {spf:F1}; ground queries {caches[0].Queries}, node probes {caches[0].NodeProbes}; GC allocs {gc}; head moved {moved:F2} m");
            GcAllocs.AssertCounterWorks();
            Assert.AreEqual(0, gc, "managed allocations while ticking");
            Assert.LessOrEqual(median, 0.1, "median sim ms per creature per frame");
        }

        [Test] public void Perf_100Lizards_Under3ms() // name kept from the brief; bound per R23
        {
            var shared = new FieldGroundProbe(field);
            var sims = CreatureDesertWorld.Spawn(shared, 100, out var brains, out var caches);
            double median = Run(sims, brains, shared, out double mean, out double max, out float spf, out int gc);
            int nodes = 0; foreach (var c in caches) nodes += c.NodeProbes;
            TestContext.Out.WriteLine($"100 monitors on DesertWorld: median {median:F3} ms, mean {mean:F3} ms, max {max:F3} ms; " +
                $"field samples/frame {spf:F1}; node probes {nodes}; GC allocs {gc}");
            GcAllocs.AssertCounterWorks();
            Assert.AreEqual(0, gc, "managed allocations while ticking");
            // Ruling R23: 4 ms, not the spec's 3 ms stress target. The single-creature target is beaten 2.5x; what
            // remains is Mono per-operation overhead across 26 joints x 100 (ground probing is ~2% of it now).
            // Burst/jobs over the flat per-creature arrays is the spec's later path to 3 ms.
            Assert.LessOrEqual(median, 4.0, "median sim ms for 100 creatures per frame (R23)");
        }

        // Arthropod legs (design 2026-10-17-multi-segment-legs-spider-design.md): 8 legs x 7 segments solved by
        // ArthropodLegIK, plus pedipalps. Budget: one spider <= 0.1 ms like a monitor; 20 spiders <= 2 ms.
        static void SpiderPerf(DensityField field, string path, int n, double boundMs)
        {
            var sp = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(path);
            Assert.IsNotNull(sp, path);
            var shared = new FieldGroundProbe(field);
            var sims = CreatureDesertWorld.Spawn(shared, n, sp, out var brains, out _);
            double median = Run(sims, brains, shared, out double mean, out double max, out float spf, out int gc);
            TestContext.Out.WriteLine($"{n} x {sp.name} on DesertWorld: median {median:F4} ms, mean {mean:F4} ms, max {max:F4} ms; field samples/frame {spf:F1}; GC allocs {gc}");
            GcAllocs.AssertCounterWorks();
            Assert.AreEqual(0, gc, "managed allocations while ticking");
            Assert.LessOrEqual(median, boundMs, "median sim ms per frame");
        }

        [Test] public void Perf_OneSpider_Under0_1ms()
        {
            SpiderPerf(field, "Assets/TerrainGen/Config/Creatures/DaddyLongLegs.asset", 1, 0.1);
            SpiderPerf(field, "Assets/TerrainGen/Config/Creatures/Tarantula.asset", 1, 0.1);
        }

        [Test] public void Perf_20Spiders_Under2ms()
        {
            SpiderPerf(field, "Assets/TerrainGen/Config/Creatures/DaddyLongLegs.asset", 20, 2.0);
            SpiderPerf(field, "Assets/TerrainGen/Config/Creatures/Tarantula.asset", 20, 2.0);
        }

        [Test] public void Build_Time_Under5ms()
        {
            var sp = CreatureTestSpecies.Lizardish();
            try
            {
                UnityEngine.Object.DestroyImmediate(CreatureMeshBuilder.Build(sp)); // warm-up (JIT)
                var ms = new List<double>();
                for (int i = 0; i < 5; i++)
                {
                    var sw = Stopwatch.StartNew();
                    Mesh m = CreatureMeshBuilder.Build(sp);
                    sw.Stop();
                    ms.Add(sw.Elapsed.TotalMilliseconds);
                    UnityEngine.Object.DestroyImmediate(m);
                }
                ms.Sort();
                TestContext.Out.WriteLine($"build ms: {string.Join(", ", ms)}");
                Assert.Less(ms[2], 5.0, "median build time");
            }
            finally { UnityEngine.Object.DestroyImmediate(sp); }
        }
    }
}

namespace TerrainGen.Tests
{
    // The lab's real terrain and the monitor spawned on it as CreatureWorld.Spawn does it; shared by the
    // perf tests and the correctness test below.
    static class CreatureDesertWorld
    {
        const string kDesertWorld = "Assets/TerrainGen/Config/Test/DesertWorld.asset";
        // the lab's enclosure on the DesertWorld dunes (Task 8 placement)
        internal static readonly Vector3 kLabCentre = new Vector3(-11.87f, 2.44f, 26.28f);
        static readonly Func<Vector3, Vector3> Up = _ => Vector3.up;

        internal static DensityField Field()
        {
            var field = AssetDatabase.LoadAssetAtPath<DensityField>(kDesertWorld);
            Assert.IsNotNull(field, kDesertWorld);
            return field;
        }

        static CreatureSpecies Monitor()
        {
            var sp = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(CreatureWorldTests.kMonitorPath);
            Assert.IsNotNull(sp, CreatureWorldTests.kMonitorPath);
            return sp;
        }

        internal static Vector3 Ground(FieldGroundProbe probe, Vector3 p)
        {
            Assert.IsTrue(probe.Probe(new Vector3(p.x, p.y + 4f, p.z), Vector3.up, 12f, out Vector3 g, out _), "no ground at " + p);
            return g;
        }

        // n monitors in one lab-sized enclosure, each with its own ground cache over one shared field probe.
        internal static List<CreatureSim> Spawn(FieldGroundProbe shared, int n, out List<WanderBrain> brains, out List<CachedGroundProbe> caches)
            => Spawn(shared, n, Monitor(), out brains, out caches);

        // The same lab-rule spawn for any species asset (the arthropod perf tests).
        internal static List<CreatureSim> Spawn(FieldGroundProbe shared, int n, CreatureSpecies sp, out List<WanderBrain> brains, out List<CachedGroundProbe> caches)
        {
            Vector3 c = Ground(shared, kLabCentre);
            // the shipped monitor at its species scale (R21), in a lab-rule enclosure
            float maxScale = CreatureLab.MaxScale(sp);
            var area = EnclosureArea.For(c, Vector3.up, CreatureLab.EnclosureRadius(sp, maxScale), sp, maxScale);
            var sims = new List<CreatureSim>(); brains = new List<WanderBrain>(); caches = new List<CachedGroundProbe>();
            int side = Mathf.CeilToInt(Mathf.Sqrt(n));
            for (int k = 0; k < n; k++)
            {
                // a grid of spawn points inside the safe disc
                float u = side > 1 ? (k % side) / (side - 1f) - 0.5f : 0f, v = side > 1 ? (k / side) / (side - 1f) - 0.5f : 0f;
                Vector3 p = area.ClampToSafe(c + new Vector3(u, 0f, v) * (1.3f * area.safeRadius));
                p = Ground(shared, p);
                var cache = CreatureWorld.SimGround(shared, sp, sp.scale) as CachedGroundProbe;
                Assert.IsNotNull(cache, "CreatureWorld must cache the field probe per creature");
                var sim = new CreatureSim(sp, cache, Up, p, Quaternion.AngleAxis(37f * k, Vector3.up) * Vector3.forward, sp.scale, k) { Bounds = area };
                sims.Add(sim); brains.Add(new WanderBrain(area, 7 + k)); caches.Add(cache);
            }
            return sims;
        }
    }

    public class CreatureDesertWorldTests
    {
        const float kDt = 1f / 60f;

        // The cache must not cost the look: on the real dunes, planted feet stay on the true sand and the spine
        // keeps its clearance over the true ground (no float, no sink).
        [Test] public void Cached_Monitor_OnDesertWorld_StaysOnTheSand()
        {
            var field = CreatureDesertWorld.Field();
            var shared = new FieldGroundProbe(field);
            var truth = new FieldGroundProbe(field);
            var sims = CreatureDesertWorld.Spawn(shared, 1, out var brains, out _);
            CreatureSim sim = sims[0];
            var sp = sim.Species;
            float worstFoot = 0f, worstSink = float.NegativeInfinity, worstFloat = float.NegativeInfinity, minSlopeDot = 1f;
            int planted = 0;
            for (int f = 0; f < 900; f++)
            {
                sim.Tick(kDt, brains[0].Target(sim, kDt));
                if (f < 60) continue;
                for (int l = 0; l < sim.Legs.Length; l++)
                {
                    if (sim.Legs[l].stepping) continue;
                    Vector3 foot = sim.Legs[l].foot;
                    Assert.IsTrue(truth.Probe(foot, Vector3.up, 0.5f, out Vector3 g, out Vector3 n));
                    worstFoot = Mathf.Max(worstFoot, Mathf.Abs(foot.y - g.y));
                    minSlopeDot = Mathf.Min(minSlopeDot, n.y);
                    planted++;
                }
                for (int i = 0; i < sim.Joints.Length; i++)
                {
                    float clear = sp.joints[i].clearance * sim.SizeScale, half = sp.joints[i].halfHeight * sim.SizeScale;
                    Vector3 j = sim.Joints[i];
                    Assert.IsTrue(truth.Probe(j - Vector3.up * clear, Vector3.up, 0.5f, out Vector3 g, out _));
                    float above = j.y - g.y;
                    worstSink = Mathf.Max(worstSink, half - above);      // the body's underside below the sand
                    worstFloat = Mathf.Max(worstFloat, above - clear);   // the joint above its designed clearance
                }
            }
            TestContext.Out.WriteLine($"planted samples {planted}: worst |foot - sand| {worstFoot * 100f:F2} cm; underside below sand max {worstSink * 100f:F2} cm; joint above clearance max {worstFloat * 100f:F2} cm; steepest ground cos {minSlopeDot:F3}");
            Assert.Greater(planted, 1000);
            Assert.LessOrEqual(worstFoot, 0.02f, "a planted foot floats or sinks against the true field");
            // body tolerances in the creature's own size (1 cm sink, 2 cm float at the reference size); feet absolute
            Assert.LessOrEqual(worstSink, 0.01f * sim.SizeScale, "the body sinks into the true sand");
            Assert.LessOrEqual(worstFloat, 0.02f * sim.SizeScale, "a spine joint floats above its clearance");
        }

        // The routine no-alloc check on the real terrain (the perf tests carry the same check but are not routine):
        // ticking through the ground cache over the field probe, after the cache's first fill.
        [Test] public void Monitor_OnDesertWorld_TickDoesNotAllocate()
        {
            var shared = new FieldGroundProbe(CreatureDesertWorld.Field());
            var sims = CreatureDesertWorld.Spawn(shared, 2, out var brains, out _);
            Action frame = () => { for (int i = 0; i < sims.Count; i++) sims[i].Tick(kDt, brains[i].Target(sims[i], kDt)); };
            for (int f = 0; f < 60; f++) frame();
            GcAllocs.AssertCounterWorks();
            int allocs = GcAllocs.Count(() => { for (int f = 0; f < 240; f++) frame(); });
            Assert.AreEqual(0, allocs, "managed allocations while ticking on the real terrain");
        }
    }
}
