using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class CreatureGroundProbeTests
    {
        static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        [Test] public void FieldProbe_Plane_FindsHeight()
        {
            var f = ScriptableObject.CreateInstance<PlaneField>(); f.height = 3f;
            try
            {
                var probe = new FieldGroundProbe(f);
                Assert.IsTrue(probe.Probe(new Vector3(0, 3.2f, 0), Vector3.up, 1f, out var p, out var n));
                Assert.AreEqual(3f, p.y, 1e-3f);
                Assert.Less((n - Vector3.up).magnitude, 1e-3f);
                Assert.Greater(probe.SampleCount, 0);
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void FieldProbe_Sphere_NormalIsRadial()
        {
            var f = ScriptableObject.CreateInstance<SphereField>(); f.radius = 100f;
            try
            {
                var probe = new FieldGroundProbe(f);
                float a = 30f * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Assert.IsTrue(probe.Probe(dir * 100.3f, dir, 1f, out var p, out var n));
                Assert.AreEqual(100f, p.magnitude, 1e-2f);
                Assert.Greater(Vector3.Dot(n, dir), 0.999f);
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void FieldProbe_WindowMiss_FallsBack_ThenHolds()
        {
            var f = ScriptableObject.CreateInstance<PlaneField>(); f.height = 0f;
            try
            {
                var probe = new FieldGroundProbe(f);
                Assert.IsTrue(probe.Probe(new Vector3(0, 5f, 0), Vector3.up, 0.5f, out var p, out var n));
                Assert.AreEqual(0f, p.y, 1e-2f);
                Assert.IsFalse(probe.Probe(new Vector3(0, 50f, 0), Vector3.up, 0.5f, out p, out n));
                Assert.IsTrue(Finite(p)); Assert.IsTrue(Finite(n));
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void FieldProbe_TwoLayer_PicksNearestBelowNear()
        {
            var f = ScriptableObject.CreateInstance<TwoLayerField>();
            try
            {
                var probe = new FieldGroundProbe(f);
                Assert.IsTrue(probe.Probe(new Vector3(0, 1f, 0), Vector3.up, 2f, out var p, out _));
                Assert.AreEqual(0f, p.y, 1e-2f);
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void FlatProbe_ReturnsHeight()
        {
            var probe = new FlatGroundProbe(2f);
            Assert.IsTrue(probe.Probe(new Vector3(4, 9, 5), Vector3.up, 1f, out var p, out var n));
            Assert.AreEqual(new Vector3(4, 2, 5), p);
            Assert.AreEqual(Vector3.up, n);
        }

        // ---- CachedGroundProbe (Task 9 perf): bilinear reads of probed lattice nodes ----

        [Test] public void Cached_MatchesField_OnCurvedGround()
        {
            var f = ScriptableObject.CreateInstance<SphereField>(); f.radius = 20f; f.center = new Vector3(0f, -20f, 0f);
            try
            {
                var field = new FieldGroundProbe(f);
                var cache = new CachedGroundProbe(new FieldGroundProbe(f), 2f);
                float worstH = 0f, worstN = 0f;
                for (int i = 0; i < 200; i++)
                {
                    var q = new Vector3(-1.5f + 0.0151f * i, 0.05f, 0.7f - 0.0093f * i);
                    Assert.IsTrue(field.Probe(q, Vector3.up, 0.5f, out var p0, out var n0));
                    Assert.IsTrue(cache.Probe(q, Vector3.up, 0.5f, out var p1, out var n1));
                    Assert.AreEqual(q.x, p1.x, 1e-6f); Assert.AreEqual(q.z, p1.z, 1e-6f);
                    worstH = Mathf.Max(worstH, Mathf.Abs(p1.y - p0.y));
                    worstN = Mathf.Max(worstN, Vector3.Angle(n0, n1));
                }
                TestContext.Out.WriteLine($"worst height {worstH * 1000f:F2} mm, worst normal {worstN:F2} deg, node probes {cache.NodeProbes}");
                Assert.LessOrEqual(worstH, 0.004f);   // bilinear on a 0.5 m lattice over a 20 m radius: c^2 / 8R = 1.6 mm per axis, 3.1 mm both
                Assert.LessOrEqual(worstN, 1f);
                Assert.LessOrEqual(cache.NodeProbes, 64, "nodes are probed once each");
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void Cached_TiltedPlane_IsExact()
        {
            var f = ScriptableObject.CreateInstance<TiltedPlaneField>();
            f.normal = Quaternion.AngleAxis(30f, new Vector3(1f, 0f, 0.4f).normalized) * Vector3.up;
            try
            {
                var cache = new CachedGroundProbe(new FieldGroundProbe(f), 2f);
                for (int i = 0; i < 50; i++)
                {
                    var q = new Vector3(0.037f * i - 0.9f, 0f, 0.6f - 0.029f * i);
                    q.y = -(f.normal.x * q.x + f.normal.z * q.z) / f.normal.y + 0.05f;
                    Assert.IsTrue(cache.Probe(q, Vector3.up, 0.5f, out var p, out var n));
                    Assert.AreEqual(0f, Vector3.Dot(p, f.normal), 2e-4f);
                    Assert.Less(Vector3.Angle(n, f.normal), 0.1f);
                }
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void Cached_ReusesNodes_WithinACell()
        {
            var f = ScriptableObject.CreateInstance<PlaneField>(); f.height = 1f;
            try
            {
                var inner = new FieldGroundProbe(f);
                var cache = new CachedGroundProbe(inner, 2f);
                for (int i = 0; i < 1000; i++)
                    Assert.IsTrue(cache.Probe(new Vector3(0.1f + 0.0003f * i, 1.1f, 0.2f), Vector3.up, 0.5f, out _, out _));
                Assert.AreEqual(4, cache.NodeProbes);
                Assert.AreEqual(1000, cache.Queries);
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void Cached_Invalidate_ReprobesEditedGround()
        {
            var f = ScriptableObject.CreateInstance<PlaneField>(); f.height = 0f;
            try
            {
                var cache = new CachedGroundProbe(new FieldGroundProbe(f), 2f);
                Assert.IsTrue(cache.Probe(new Vector3(0.2f, 0.1f, 0.2f), Vector3.up, 0.5f, out var p, out _));
                Assert.AreEqual(0f, p.y, 1e-3f);
                f.height = -0.1f; // an edit lowered the ground
                Assert.IsTrue(cache.Probe(new Vector3(0.2f, 0.1f, 0.2f), Vector3.up, 0.5f, out p, out _));
                Assert.AreEqual(0f, p.y, 1e-3f, "without an invalidation the cache holds the old ground");
                cache.Invalidate(new Bounds(new Vector3(0.2f, 0f, 0.2f), new Vector3(0.4f, 0.4f, 0.4f)));
                Assert.IsTrue(cache.Probe(new Vector3(0.2f, 0.1f, 0.2f), Vector3.up, 0.5f, out p, out _));
                Assert.AreEqual(-0.1f, p.y, 1e-3f);
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void Cached_OtherLayer_Reprobes()
        {
            var f = ScriptableObject.CreateInstance<TwoLayerField>();
            try
            {
                var cache = new CachedGroundProbe(new FieldGroundProbe(f), 2f);
                Assert.IsTrue(cache.Probe(new Vector3(0.1f, 0.1f, 0.1f), Vector3.up, 0.5f, out var p, out _));
                Assert.AreEqual(0f, p.y, 1e-2f);
                Assert.IsTrue(cache.Probe(new Vector3(0.1f, 30.1f, 0.1f), Vector3.up, 0.5f, out p, out _));
                Assert.AreEqual(30f, p.y, 1e-2f, "the roof, not the cached floor");
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void Cached_Miss_ReturnsFalse_Finite()
        {
            var f = ScriptableObject.CreateInstance<PlaneField>(); f.height = 0f;
            try
            {
                var cache = new CachedGroundProbe(new FieldGroundProbe(f), 2f);
                Assert.IsFalse(cache.Probe(new Vector3(0.3f, 50f, 0.3f), Vector3.up, 0.5f, out var p, out var n));
                Assert.IsTrue(Finite(p)); Assert.IsTrue(Finite(n));
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void Cached_NonVerticalUp_PassesThrough()
        {
            var f = ScriptableObject.CreateInstance<SphereField>(); f.radius = 100f;
            try
            {
                var cache = new CachedGroundProbe(new FieldGroundProbe(f), 2f);
                float a = 30f * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Assert.IsTrue(cache.Probe(dir * 100.3f, dir, 1f, out var p, out var n));
                Assert.AreEqual(100f, p.magnitude, 1e-2f);
                Assert.Greater(Vector3.Dot(n, dir), 0.999f);
                Assert.AreEqual(0, cache.NodeProbes);
            }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void World_CachesFieldProbe_NotAnalyticProbes()
        {
            var sp = CreatureTestSpecies.Lizardish();
            var f = ScriptableObject.CreateInstance<PlaneField>();
            try
            {
                var field = new FieldGroundProbe(f);
                Assert.IsInstanceOf<CachedGroundProbe>(CreatureWorld.SimGround(field, sp, 1f));
                var flat = new FlatGroundProbe();
                Assert.AreSame(flat, CreatureWorld.SimGround(flat, sp, 1f));
            }
            finally { Object.DestroyImmediate(f); Object.DestroyImmediate(sp); }
        }

        // ---- R25: a dug hole (user report: jitter and body deformation walking into a hole) ----

        // A column through an overhanging lip: solid lip 0 .. -0.25, the hole's air, its floor near -1.75
        // (sphere centre (0, -1, 0), radius 1.5; columns at x = 1.24 .. 1.35). A probe answers the surface
        // nearest to its near point - the lip from just above the lip, the floor from inside the hole - never
        // the other one, in any order (H1: the old window probe and the old cache answered either).
        static PlaneWithSphereHoleField OverhangColumn()
        {
            var f = ScriptableObject.CreateInstance<PlaneWithSphereHoleField>();
            f.center = new Vector3(0f, -1f, 0f); f.radius = 1.5f;
            return f;
        }

        static void AssertColumnSurfaces(IGroundProbe probe, string what)
        {
            for (int k = 0; k < 12; k++)
            {
                float x = 1.24f + 0.01f * k;
                float floor = -1f - Mathf.Sqrt(1.5f * 1.5f - x * x);
                Assert.IsTrue(probe.Probe(new Vector3(x, 0.1f, 0.05f), Vector3.up, 2f, out var p, out _), what);
                Assert.AreEqual(0f, p.y, 0.01f, $"{what}: from the lip (query {k})");
                Assert.IsTrue(probe.Probe(new Vector3(x, floor + 0.15f, 0.05f), Vector3.up, 2f, out p, out _), what);
                Assert.AreEqual(floor, p.y, 0.01f, $"{what}: from inside the hole (query {k})");
            }
        }

        [Test] public void FieldProbe_OverhangColumn_NearestSurface()
        {
            var f = OverhangColumn();
            try { AssertColumnSurfaces(new FieldGroundProbe(f), "field probe"); }
            finally { Object.DestroyImmediate(f); }
        }

        [Test] public void Cached_OverhangColumn_AnswersTheQueriedSurface()
        {
            var f = OverhangColumn();
            try { AssertColumnSurfaces(new CachedGroundProbe(new FieldGroundProbe(f), 6f), "cache"); }
            finally { Object.DestroyImmediate(f); }
        }

        // The 4x monitor walks straight across a dug hole through the ground cache, as in the lab.
        // (a) no vertical jitter: no joint reverses its vertical motion by more than 1 mm x scale per tick
        //     twice in a row (a joint snapping between the rim and the floor, H1);
        // (b) segments keep their 3D length within 5 % (the skinned body does not stretch, H2);
        // (c) finite; (d) every planted foot's landing point is on the surface within 2 cm, and so is a
        //     planted foot that reaches it; a foot whose landing point went out of reach (the body moved on
        //     over a pit deeper than the legs) counts as dangling, at most maxDangling of the planted ticks;
        // (e) the body does not cut into the ground by more than its half height (+ maxSink x scale);
        // (f) it gets across.
        static void WalkAcrossHole(float centreY, string tag, float maxDangling, float maxSink)
        {
            var sp = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureSpecies>(CreatureWorldTests.kMonitorPath);
            var f = ScriptableObject.CreateInstance<PlaneWithSphereHoleField>();
            f.radius = 1.6f; // a few overlapping scoops of the digger (1.1 m each): a hole ~3 m wide
            f.center = new Vector3(0.4f, centreY * f.radius, 0f);
            try
            {
                var ground = CachedGroundProbe.ForCreature(new FieldGroundProbe(f), sp, sp.scale);
                var sim = new CreatureSim(sp, ground, _ => Vector3.up, new Vector3(0f, 0f, -12f), Vector3.forward, sp.scale);
                int n = sim.Joints.Length;
                var prevY = new float[n]; var prevDy = new float[n];
                var stepped = new bool[sim.Legs.Length]; // a leg's landing point exists from its first step on
                for (int i = 0; i < n; i++) prevY[i] = sim.Joints[i].y;
                float seg = sp.spacing * sim.SizeScale, worstSeg = 0f, worstFoot = 0f, worstTarget = 0f, worstFlip = 0f, worstBend = 0f;
                int flips = 0, planted = 0, dangling = 0;
                string sinkAt = "-";
                float worstSink = 0f, flipAt = 0.001f * sim.SizeScale;
                for (int t = 0; t < 2400; t++)
                {
                    sim.Tick(1f / 60f, new Vector3(0f, 0f, 1000f));
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 j = sim.Joints[i];
                        Assert.IsTrue(Finite(j), $"{tag}: joint {i} not finite t={t}");
                        float dy = j.y - prevY[i];
                        if (t > 0 && dy * prevDy[i] < 0f && Mathf.Abs(dy) > flipAt && Mathf.Abs(prevDy[i]) > flipAt)
                        { flips++; worstFlip = Mathf.Max(worstFlip, Mathf.Min(Mathf.Abs(dy), Mathf.Abs(prevDy[i]))); }
                        prevDy[i] = dy; prevY[i] = j.y;
                        if (i > 0) worstSeg = Mathf.Max(worstSeg, Mathf.Abs((j - sim.Joints[i - 1]).magnitude - seg) / seg);
                        if (i > 0 && i < n - 1)
                            worstBend = Mathf.Max(worstBend, Mathf.Abs(Pitch(sim.Joints[i - 1] - j) - Pitch(j - sim.Joints[i + 1])));
                        float sink = f.Sample(j) - sp.joints[i].halfHeight * sim.SizeScale;
                        if (sink > worstSink) { worstSink = sink; sinkAt = $"joint {i} at {j.x:F2},{j.y:F2},{j.z:F2} t={t}"; }
                    }
                    for (int l = 0; l < sim.Legs.Length; l++)
                    {
                        LegState st = sim.Legs[l];
                        if (st.stepping) { stepped[l] = true; continue; }
                        if (!stepped[l]) continue;
                        planted++;
                        worstTarget = Mathf.Max(worstTarget, Mathf.Abs(f.Sample(st.stepTo)));
                        if ((st.foot - st.stepTo).magnitude > 1e-3f) { dangling++; continue; }
                        worstFoot = Mathf.Max(worstFoot, Mathf.Abs(f.Sample(st.foot)));
                    }
                }
                string sum = $"{tag}: vertical flips {flips} (worst {worstFlip * 100f:F1} cm), worst segment stretch {worstSeg * 100f:F1} %, " +
                             $"worst landing point off surface {worstTarget * 100f:F1} cm, worst reached foot {worstFoot * 100f:F1} cm, dangling {dangling}/{planted}, " +
                             $"body into ground {worstSink * 100f:F1} cm ({sinkAt}), worst vertical bend {worstBend:F0} deg, head z {sim.Joints[0].z:F1}, tail z {sim.Joints[n - 1].z:F1}, " +
                             $"cache node probes {ground.NodeProbes}, exact {ground.ExactQueries}/{ground.Queries}";
                TestContext.Out.WriteLine(sum);
                Assert.Greater(sim.Joints[n - 1].z, f.center.z + f.radius + 1f, $"the monitor never crossed the hole | {sum}");
                Assert.AreEqual(0, flips, $"joints jitter | {sum}");
                Assert.LessOrEqual(worstSeg, 0.05f, $"body segments stretch | {sum}");
                Assert.Greater(planted, 500, sum);
                Assert.LessOrEqual(worstTarget, 0.02f, $"a landing point is off the surface | {sum}");
                Assert.LessOrEqual(worstFoot, 0.02f, $"a planted foot is off the surface | {sum}");
                Assert.LessOrEqual(dangling, maxDangling * planted, $"feet dangle | {sum}");
                Assert.LessOrEqual(worstSink, maxSink * sim.SizeScale, $"the body cuts into the ground | {sum}");
            }
            finally { Object.DestroyImmediate(f); }
        }

        static float Pitch(Vector3 v) => Mathf.Atan2(v.y, new Vector2(v.x, v.z).magnitude) * Mathf.Rad2Deg;

        // The bowl (centre above the ground, 1.1 m deep) is within the legs' reach: feet dangle on 0.03 % of
        // the planted ticks (measured), the body stays out of the ground. The overhanging pit (2.1 m deep under
        // a lip) is deeper than the 4x monitor's legs (1.1 m): it still gets across without jitter or stretch,
        // but feet hang in the air on ~2-3 % of the planted ticks, and climbing out under the far lip the
        // tail tip cuts the lip's edge (~20 cm measured; a creature cannot get out from under an overhang
        // without climbing, which is a queued feature).
        [Test] public void Hole_Overhanging_NoJitter_NoStretch() => WalkAcrossHole(-0.3f, "overhanging hole", 0.05f, 0.08f);
        [Test] public void Hole_Bowl_NoJitter_NoStretch() => WalkAcrossHole(0.3f, "bowl", 0.002f, 0.02f);

        // A dig (CreatureWorld forwards RegionModified with the carve's bounds) must replace the cached ground.
        [Test] public void Cached_DigInvalidation_ReturnsNewSurface()
        {
            var f = ScriptableObject.CreateInstance<PlaneWithSphereHoleField>();
            f.dug = false; f.radius = 1.1f; f.center = new Vector3(0.3f, -0.385f, 0.2f);
            try
            {
                var cache = new CachedGroundProbe(new FieldGroundProbe(f), 6f);
                for (int i = 0; i < 25; i++)
                    Assert.IsTrue(cache.Probe(new Vector3(-1f + 0.1f * i, 0.1f, 0.2f), Vector3.up, 2f, out _, out _));
                f.dug = true;
                // TerrainModificationSystem.Notify for CarveSphere: centre, expanded by 2 x (radius + 2 voxels)
                var b = new Bounds(f.center, Vector3.zero); b.Expand(2f * (f.radius + 2f * 0.5f));
                cache.Invalidate(b);
                Assert.IsTrue(cache.Probe(new Vector3(0.3f, 0.1f, 0.2f), Vector3.up, 2f, out var p, out _));
                float floor = f.center.y - f.radius;
                TestContext.Out.WriteLine($"after the dig: ground {p.y:F3} at the hole centre, floor {floor:F3}");
                Assert.Less(p.y, -0.5f, "the cache still returns the ground from before the dig");
            }
            finally { Object.DestroyImmediate(f); }
        }
    }
}
