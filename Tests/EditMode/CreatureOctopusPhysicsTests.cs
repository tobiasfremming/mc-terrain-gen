using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Tree octopus on REAL geometry: generated meshes with MeshColliders, the climbing PhysicsSurfaceProbe
    // (design §0: every contact comes from a probe on real colliders). Editor only (physics).
    public class CreatureOctopusPhysicsTests
    {
        ClimbTestMeshes geo;
        bool savedBackfaces;
        readonly List<Object> trash = new List<Object>();

        [SetUp] public void SetUp() { geo = new ClimbTestMeshes(); savedBackfaces = Physics.queriesHitBackfaces; }

        [TearDown] public void TearDown()
        {
            Physics.queriesHitBackfaces = savedBackfaces;
            geo.Dispose();
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        PhysicsSurfaceProbe Probe() => new PhysicsSurfaceProbe(geo.Registry);

        // Every pin's surface point (pin - normal x arm radius) is on the mesh: an independent raycast from just
        // outside along -normal meets the collider there.
        static void AssertPinsOnMesh(TentacleChain arm, WrapMarch w, float tol)
        {
            for (int i = 0; i < w.Count; i++)
            {
                float rk = arm.Spec.RadiusAt(arm.Spec.Spacing * w.Particle[i]) * arm.SizeScale;
                Vector3 surf = w.Point[i] - w.Normal[i] * rk;
                Vector3 from = surf + w.Normal[i] * 0.05f;
                Assert.IsTrue(Physics.Raycast(from, -w.Normal[i], out RaycastHit hit, 0.1f, 1 << ClimbTestMeshes.kLayer), $"pin {i}: no mesh under it");
                Assert.AreEqual(0.05f, hit.distance, tol, $"pin {i} is off the mesh");
            }
        }

        [Test] public void Wrap_RealBranch_OnSurface()
        {
            geo.Branch(new Vector3(-3f, 2f, 0f), 0.09f, 6f);
            using (PhysicsSurfaceProbe.Backfaces())
            {
                var probe = Probe();
                var s = new AnalyticSurfaces().AddCapsule(new Vector3(-3f, 2f, 0f), new Vector3(3f, 2f, 0f), 0.09f);
                TentacleChain arm = OctopusWrapTests.ArmAtBranch(s, 2f, out _, out Vector3 tipDir);
                Assert.IsTrue(probe.Cast(arm.X[arm.N - 1] + Vector3.up * 0.2f, Vector3.down, 2f, out SurfaceHit contact), "the probe does not see the branch");
                var w = new WrapMarch(arm.N);
                w.Plan(probe, arm, contact, arm.N - 1, tipDir, arm.X[0], 6, 0);
                TestContext.Out.WriteLine($"real branch: {w.Count} pins, {w.WrapTurns:F2} turns, radius {w.Radius:F3}, {w.Probes} probes, {probe.Raycasts} raycasts");
                Assert.IsFalse(w.Spread);
                Assert.Greater(w.WrapTurns, 1.0f);
                AssertPinsOnMesh(arm, w, 0.004f);
            }
        }

        // Wrap_ContactsComeFromProbe: a bent, non-cylindrical branch (a quarter torus with an elliptic section).
        // The local tube fit is wrong there by design; the pins still sit on the mesh because every one is a
        // probe answer.
        [Test] public void Wrap_ContactsComeFromProbe()
        {
            Mesh m = BentTube(1.0f, 0.1f, 0.075f, 48, 24);
            trash.Add(m);
            geo.Add(m, new Vector3(0f, 2f, 0f), Quaternion.identity, ClimbTestMeshes.kLayer, true);
            using (PhysicsSurfaceProbe.Backfaces())
            {
                var probe = Probe();
                // reach the top of the bend at 45 degrees
                Vector3 top = new Vector3(Mathf.Cos(0.25f * Mathf.PI), 0f, Mathf.Sin(0.25f * Mathf.PI)) * 1.0f + new Vector3(0f, 2f, 0f);
                Vector3 d = new Vector3(-0.3f, -0.3f, 1f).normalized;
                var arm = new TentacleChain(TentacleSpec.Default, top + Vector3.up * 0.12f - d * TentacleSpec.Default.Length, OctoMath.Look(d, Vector3.up));
                Vector3 root = arm.X[0];
                for (int k = 0; k < arm.N; k++) { arm.X[k] = root + d * (arm.Spacing * k); arm.Prev[k] = arm.X[k]; }
                Assert.IsTrue(probe.Cast(arm.X[arm.N - 1] + Vector3.up * 0.3f, Vector3.down, 1f, out SurfaceHit contact));
                var w = new WrapMarch(arm.N);
                w.Plan(probe, arm, contact, arm.N - 1, d, root, 6, 0);
                TestContext.Out.WriteLine($"bent tube: {w.Count} pins, {w.WrapTurns:F2} turns, spread {w.Spread}, confidence {w.Confidence:F2}");
                Assert.Greater(w.Count, 3);
                AssertPinsOnMesh(arm, w, 0.004f);
            }
        }

        // BranchHints from the real Baobab's regenerated skeleton line up with its bark mesh: a cast at each hint
        // from a hint-radius-scaled offset meets the bark about one branch radius from the centre line.
        [Test] public void Hints_SkeletonMatchesBark()
        {
            const string kSet = "Assets/TerrainGen/Config/Flora/Prototypes/BaobabPlant_Prototypes.asset";
            var set = UnityEditor.AssetDatabase.LoadAssetAtPath<PlantPrototypeSet>(kSet);
            if (set == null) Assert.Ignore(kSet + " is not baked on this machine (Config/Flora/Prototypes is gitignored)");
            const float plantScale = 2f;
            Vector3 at = new Vector3(0f, 0f, 0f);
            Mesh lod0 = set.Variant(0).Lod(0);
            SurfaceMeshCache.Entry e = SurfaceMeshCache.BarkOnly(lod0);
            var mc = geo.Registry.CreateProxy("Baobab bark", e, at, Quaternion.identity, Vector3.one * plantScale);
            Physics.SyncTransforms();
            try
            {
                var hints = new BranchHints();
                Assert.IsTrue(hints.Register(set, 0, Matrix4x4.TRS(at, Quaternion.identity, Vector3.one * plantScale)), "no skeleton (no source grammar?)");
                var results = new BranchHint[16];
                float h = set.Variant(0).bounds.max.y * plantScale;
                int n = hints.Query(new Vector3(0f, 0.7f * h, 0f), Vector3.up, -1f, 0.2f, 0.8f * h, 0.03f, 1.5f, results);
                Assert.Greater(n, 3, "no hints around the canopy");
                var probe = Probe();
                int matched = 0;
                using (PhysicsSurfaceProbe.Backfaces())
                    for (int i = 0; i < n; i++)
                    {
                        BranchHint hint = results[i];
                        Vector3 side = Vector3.Cross(hint.axis, Mathf.Abs(hint.axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                        float off = 3f * hint.radius + 0.1f;
                        if (!probe.Cast(hint.point + side * off, -side, off * 2f, out SurfaceHit hit)) continue;
                        float fromAxis = off - hit.distance;
                        if (Mathf.Abs(fromAxis - hint.radius) < 0.5f * hint.radius + 0.05f) matched++;
                    }
                TestContext.Out.WriteLine($"{matched} of {n} hints meet the bark at their radius");
                Assert.GreaterOrEqual(matched, (int)(0.7f * n), "the skeleton does not match the bark");
            }
            finally { geo.Registry.DestroyProxy(mc); SurfaceMeshCache.Clear(); }
        }

        // A quarter torus (major radius R, elliptic section a x b) around +Y, closed with flat caps.
        static Mesh BentTube(float R, float a, float b, int along, int around)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= along; i++)
            {
                float u = 0.5f * Mathf.PI * i / along;
                Vector3 c = new Vector3(Mathf.Cos(u), 0f, Mathf.Sin(u)) * R, radial = new Vector3(Mathf.Cos(u), 0f, Mathf.Sin(u));
                for (int j = 0; j < around; j++)
                {
                    float w = 2f * Mathf.PI * j / around;
                    v.Add(c + radial * (a * Mathf.Cos(w)) + Vector3.up * (b * Mathf.Sin(w)));
                }
            }
            for (int i = 0; i < along; i++)
                for (int j = 0; j < around; j++)
                {
                    int p0 = i * around + j, p1 = i * around + (j + 1) % around, q0 = p0 + around, q1 = p1 + around;
                    // outward: (along u) x (around) points inward here, so the order is reversed
                    t.Add(p0); t.Add(p1); t.Add(q0);
                    t.Add(p1); t.Add(q1); t.Add(q0);
                }
            int c0 = v.Count; v.Add(new Vector3(R, 0f, 0f));
            int c1 = v.Count; v.Add(new Vector3(0f, 0f, R));
            for (int j = 0; j < around; j++)
            {
                t.Add(c0); t.Add((j + 1) % around); t.Add(j);           // faces -Z (the u = 0 end)
                int o = along * around;
                t.Add(c1); t.Add(o + j); t.Add(o + (j + 1) % around);   // faces -X (the u = 90 deg end)
            }
            var m = new Mesh();
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
