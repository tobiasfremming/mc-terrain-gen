using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Tree octopus task 6: WrapMarch on analytic surfaces (the same checks on real MeshColliders are in
    // CreatureOctopusPhysicsTests). Every pin must be ON the surface the probe answered, an arm radius out.
    public class OctopusWrapTests
    {
        // An arm whose tip has just reached the top of a horizontal branch (along +X at height y), coming
        // in from the side and above; returns the contact (a Cast down from above the tip).
        internal static TentacleChain ArmAtBranch(AnalyticSurfaces s, float branchY, out SurfaceHit contact, out Vector3 tipDir)
        {
            // lay the arm straight so its tip ends just above the branch top, coming in from the side and above
            Vector3 d = new Vector3(0f, -0.3f, 1f).normalized, target = new Vector3(0f, branchY + 0.06f, 0.02f);
            var arm = new TentacleChain(TentacleSpec.Default, target - d * TentacleSpec.Default.Length, OctoMath.Look(d, Vector3.up));
            Vector3 root = arm.X[0];
            for (int k = 0; k < arm.N; k++) { arm.X[k] = root + d * (arm.Spacing * k); arm.Prev[k] = arm.X[k]; }
            arm.UpdateFrames(0f);
            tipDir = d;
            Assert.IsTrue(s.Cast(arm.X[arm.N - 1] + Vector3.up * 0.2f, Vector3.down, 2f, out contact), "no branch under the tip");
            return arm;
        }

        static void AssertPinsOnSurface(AnalyticSurfaces s, TentacleChain arm, WrapMarch w, float tol)
        {
            for (int i = 0; i < w.Count; i++)
            {
                int k = w.Particle[i];
                float rk = arm.Spec.RadiusAt(arm.Spec.Spacing * k) * arm.SizeScale;
                Vector3 surf = w.Point[i] - w.Normal[i] * rk;
                float d = s.Distance(surf, out Vector3 n, out _);
                Assert.Less(Mathf.Abs(d), tol, $"pin {i} (particle {k}) is {d * 1000f:F1} mm off the surface");
                Assert.Greater(Vector3.Dot(n, w.Normal[i]), 0.95f, $"pin {i} normal is not the surface normal");
            }
        }

        [Test] public void Wrap_Cylinder_OnSurface()
        {
            var s = new AnalyticSurfaces().AddCapsule(new Vector3(-3f, 2f, 0f), new Vector3(3f, 2f, 0f), 0.09f);
            TentacleChain arm = ArmAtBranch(s, 2f, out SurfaceHit contact, out Vector3 tipDir);
            var w = new WrapMarch(arm.N);
            w.Plan(s, arm, contact, arm.N - 1, tipDir, arm.X[0], 6, 0);
            TestContext.Out.WriteLine($"wrap: {w.Count} pins, {w.WrapTurns:F2} turns, radius {w.Radius:F3}, confidence {w.Confidence:F2}, {w.Probes} probes");
            Assert.IsFalse(w.Spread, "a thin branch must be wrapped");
            Assert.Greater(w.WrapTurns, 1.0f, "less than a full turn");
            AssertPinsOnSurface(s, arm, w, 0.002f);
            for (int i = 1; i < w.Count; i++)
            {
                float gap = (w.Point[i] - w.Point[i - 1]).magnitude / arm.Spacing;
                Assert.That(gap, Is.InRange(0.75f, 1.25f), "pin spacing at " + i);
                Assert.AreEqual(w.Particle[i - 1] - 1, w.Particle[i], "pins run tip -> base");
            }
            Assert.LessOrEqual(w.Probes, 40, "probe budget");
        }

        [Test] public void Wrap_AxisEstimate()
        {
            foreach (float r in new[] { 0.05f, 0.1f, 0.2f })
                foreach (Vector3 dir in new[] { Vector3.right, new Vector3(1f, 0.3f, 0.5f).normalized, new Vector3(0.2f, 1f, 0.1f).normalized })
                {
                    var s = new AnalyticSurfaces().AddCapsule(-dir * 4f, dir * 4f, r);
                    Vector3 side = Vector3.Cross(dir, Vector3.forward).sqrMagnitude > 0.1f ? Vector3.Cross(dir, Vector3.forward).normalized : Vector3.up;
                    Assert.IsTrue(s.Cast(side * 1f, -side, 2f, out SurfaceHit h));
                    var w = new WrapMarch(8);
                    Assert.IsTrue(w.EstimateAxis(s, h, 0.03f, 0, out Vector3 axis, out float est, out float conf));
                    float ang = Vector3.Angle(axis, dir); ang = Mathf.Min(ang, 180f - ang);
                    TestContext.Out.WriteLine($"r {r} dir {dir}: axis err {ang:F2} deg, radius {est:F3}, confidence {conf:F2}");
                    Assert.Less(ang, 5f, "axis");
                    Assert.AreEqual(r, est, 0.15f * r, "radius");
                    Assert.Greater(conf, 0.8f);
                }
        }

        [Test] public void Wrap_TooThick_Spreads()
        {
            var s = new AnalyticSurfaces().AddCapsule(new Vector3(-3f, 1.5f, 0f), new Vector3(3f, 1.5f, 0f), 0.6f);
            TentacleChain arm = ArmAtBranch(s, 1.5f + 0.51f, out SurfaceHit contact, out Vector3 tipDir);
            var w = new WrapMarch(arm.N);
            w.Plan(s, arm, contact, arm.N - 1, tipDir, arm.X[0], 6, 0);
            Assert.IsTrue(w.Spread, $"a 0.6 m trunk cannot be wrapped (radius estimate {w.Radius:F2})");
            Assert.GreaterOrEqual(w.Count, 3);
            AssertPinsOnSurface(s, arm, w, 0.002f);
        }

        // A thin twig that ends just past the contact: the helix runs off its end, the probe misses, the wrap
        // stops there (never pins in the air past the tip).
        [Test] public void Wrap_EndsAtTwigTip()
        {
            var s = new AnalyticSurfaces().AddCapsule(new Vector3(-0.05f, 2f, 0f), new Vector3(3f, 2f, 0f), 0.06f);
            TentacleChain arm = ArmAtBranch(s, 2f, out SurfaceHit contact, out Vector3 tipDir);
            var w = new WrapMarch(arm.N);
            // the coil advances toward the root side; put the root side past the twig's end (-X)
            w.Plan(s, arm, contact, arm.N - 1, tipDir, new Vector3(-5f, 2f, 0f), 2, 0);
            TestContext.Out.WriteLine($"twig: {w.Count} pins, {w.WrapTurns:F2} turns");
            AssertPinsOnSurface(s, arm, w, 0.002f);
            var full = new WrapMarch(arm.N);
            var s2 = new AnalyticSurfaces().AddCapsule(new Vector3(-3f, 2f, 0f), new Vector3(3f, 2f, 0f), 0.06f);
            full.Plan(s2, arm, contact, arm.N - 1, tipDir, new Vector3(-5f, 2f, 0f), 2, 0);
            Assert.Less(w.Count, full.Count, "the wrap did not stop at the twig's end");
            for (int i = 0; i < w.Count; i++) Assert.Greater(w.Point[i].x, -0.05f - 0.1f, "a pin past the twig's end");
        }

        // Hold: the analytic capsule helix around a prey's grab volume.
        [Test] public void Wrap_AroundCapsule_Prey()
        {
            var arm = new TentacleChain(TentacleSpec.Default, Vector3.zero, Quaternion.identity);
            var w = new WrapMarch(arm.N);
            w.PlanAroundCapsule(arm, arm.N - 1, new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 1.6f), 0.12f, Vector3.up, 1f, 14);
            Assert.Greater(w.Count, 8);
            for (int i = 0; i < w.Count; i++)
            {
                Vector3 p = w.Point[i];
                float rk = arm.Spec.RadiusAt(arm.Spec.Spacing * w.Particle[i]);
                Assert.AreEqual(0.12f + rk, new Vector2(p.x, p.y).magnitude, 1e-3f, "pin " + i);
            }
            Assert.Greater(w.WrapTurns, 0.8f);
        }
    }
}
