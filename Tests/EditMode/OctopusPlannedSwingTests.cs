using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // The planned body's reduced-order model (planned-body rework, docs/superpowers/specs/2026-10-19-planned-body-
    // tentacle-rework-design.md): a point (the arm crown) on up to MaxHolds unilateral ropes - a pendulum on one hold,
    // a sling on two, ballistic with none - with energy control toward a target amplitude and damping toward rest.
    public class OctopusPlannedSwingTests
    {
        const float Dt = 1f / 60f;
        static readonly Vector3 P = new Vector3(0f, 3f, 0f);

        static PlannedSwing Hanging(float l, float angleDeg)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            var m = new PlannedSwing();
            m.Reset(P + new Vector3(Mathf.Sin(a), -Mathf.Cos(a), 0f) * l, Vector3.zero);
            m.AddHold(P, l);
            m.SwingDir = Vector3.right;
            return m;
        }

        // A free pendulum keeps the pendulum's period (small-angle corrected) within 2 %.
        [Test] public void Pendulum_Period()
        {
            float l = 1f, a0 = 10f;
            PlannedSwing m = Hanging(l, a0);
            float prevX = m.Position.x, t = 0f; int crossings = 0; float first = -1f, last = -1f;
            for (int f = 0; f < 60 * 10; f++)
            {
                m.Step(Dt); t += Dt;
                float x = m.Position.x;
                if (prevX > 0f && x <= 0f || prevX < 0f && x >= 0f)
                {
                    float tc = t - Dt * x / (x - prevX);
                    if (first < 0f) first = tc; else { last = tc; crossings++; }
                }
                prevX = x;
            }
            float period = 2f * (last - first) / crossings;
            float a = a0 * Mathf.Deg2Rad, expect = 2f * Mathf.PI * Mathf.Sqrt(l / 9.81f) * (1f + a * a / 16f);
            TestContext.Out.WriteLine($"period {period:F4} s, expected {expect:F4} s, amplitude now {m.Amplitude * Mathf.Rad2Deg:F2} deg");
            Assert.AreEqual(expect, period, 0.02f * expect);
            Assert.Greater(m.Amplitude * Mathf.Rad2Deg, 0.9f * a0, "a free pendulum barely loses energy");
        }

        // No hold: a ballistic parabola.
        [Test] public void Ballistic_Parabola()
        {
            var m = new PlannedSwing();
            Vector3 x0 = new Vector3(1f, 2f, 3f), v0 = new Vector3(1.5f, 2f, -0.5f);
            m.Reset(x0, v0);
            for (int f = 0; f < 60; f++) m.Step(Dt);
            Vector3 expect = x0 + v0 * 1f + 0.5f * new Vector3(0f, -9.81f, 0f);
            Assert.Less((m.Position - expect).magnitude, 0.01f, $"{m.Position} vs {expect}");
            Assert.AreEqual(0, m.HoldCount);
        }

        // A rope caught while falling stops the fall into it at once (inelastic): never longer than its length after.
        [Test] public void Rope_CatchIsInelastic()
        {
            var m = new PlannedSwing();
            m.Reset(P + new Vector3(0.3f, -0.5f, 0f), new Vector3(1f, -2f, 0f));
            m.AddHold(P, 0.6f);
            float worst = 0f;
            for (int f = 0; f < 120; f++) { m.Step(Dt); worst = Mathf.Max(worst, (m.Position - P).magnitude - 0.6f); }
            Assert.Less(worst, 1e-4f);
        }

        // Pumping from rest reaches the target amplitude within a few swings and does not overshoot it far.
        [Test] public void Pump_ReachesTarget_EnergyBounded()
        {
            foreach (float l in new[] { 0.9f, 1.4f })
            {
                PlannedSwing m = Hanging(l, 0f);
                m.TargetAmplitude = 35f * Mathf.Deg2Rad; m.Pumping = true;
                float max = 0f, at = -1f;
                for (int f = 0; f < 60 * 15; f++)
                {
                    m.Step(Dt);
                    max = Mathf.Max(max, m.Amplitude);
                    if (at < 0f && m.Amplitude > 32f * Mathf.Deg2Rad) at = f * Dt;
                }
                TestContext.Out.WriteLine($"l {l}: reached 32 deg at {at:F1} s, max {max * Mathf.Rad2Deg:F1} deg, now {m.Amplitude * Mathf.Rad2Deg:F1}");
                Assert.That(at, Is.InRange(0.5f, 6f * Mathf.Sqrt(l)), "too slow (or a kick)");
                Assert.Less(max * Mathf.Rad2Deg, 40f, "overshoot");
                Assert.AreEqual(35f, m.Amplitude * Mathf.Rad2Deg, 3f);
            }
        }

        // Rest damping stills a wide swing (and its sideways part) within a few seconds.
        [Test] public void Rest_Damps()
        {
            PlannedSwing m = Hanging(1.2f, 40f);
            m.Velocity = new Vector3(0f, 0f, 0.5f);
            m.Pumping = false; m.RestDamping = 1.5f;
            for (int f = 0; f < 60 * 6; f++) m.Step(Dt);
            TestContext.Out.WriteLine($"speed {m.Velocity.magnitude:E2} m/s, amplitude {m.Amplitude * Mathf.Rad2Deg:F2} deg");
            Assert.Less(m.Velocity.magnitude, 0.01f);
        }

        // Two holds: the sling holds the body still between them.
        [Test] public void Sling_HangsBetween()
        {
            var m = new PlannedSwing();
            Vector3 a = new Vector3(0f, 3f, 0f), b = new Vector3(1f, 3f, 0f);
            m.Reset(new Vector3(0.5f, 2.3f, 0f), Vector3.zero);
            m.AddHold(a, 0.9f); m.AddHold(b, 0.9f);
            m.RestDamping = 1f;
            for (int f = 0; f < 60 * 5; f++) m.Step(Dt);
            Assert.AreEqual(0.5f, m.Position.x, 0.02f);
            Assert.AreEqual(3f - Mathf.Sqrt(0.81f - 0.25f), m.Position.y, 0.02f);
        }

        // The forward apex is predicted from the state: angle and time.
        [Test] public void Predict_Apex()
        {
            float l = 1f, A = 30f * Mathf.Deg2Rad;
            var m = new PlannedSwing();
            m.Reset(P - Vector3.up * l, Vector3.right * Mathf.Sqrt(2f * 9.81f * l * (1f - Mathf.Cos(A))));
            m.AddHold(P, l); m.SwingDir = Vector3.right;
            var arc = new Vector3[32];
            Assert.IsTrue(m.PredictApex(2f, arc, out int n, out Vector3 apex, out float t));
            float ang = Mathf.Atan2(apex.x - P.x, P.y - apex.y) * Mathf.Rad2Deg;
            float quarter = 0.25f * 2f * Mathf.PI * Mathf.Sqrt(l / 9.81f) * (1f + A * A / 16f);
            TestContext.Out.WriteLine($"apex {ang:F2} deg at {t:F3} s (quarter period {quarter:F3}); {n} arc points");
            Assert.AreEqual(30f, ang, 1f);
            Assert.AreEqual(quarter, t, 0.04f);
            Assert.Greater(n, 4);
        }

        [Test] public void Deterministic()
        {
            PlannedSwing a = Hanging(1.1f, 12f), b = Hanging(1.1f, 12f);
            a.TargetAmplitude = b.TargetAmplitude = 0.6f; a.Pumping = b.Pumping = true;
            for (int f = 0; f < 600; f++) { a.Step(Dt); b.Step(Dt); }
            Assert.AreEqual(a.Position.x, b.Position.x); Assert.AreEqual(a.Position.y, b.Position.y); Assert.AreEqual(a.Velocity.z, b.Velocity.z);
        }

        [Test] public void NoGc()
        {
            GcAllocs.AssertCounterWorks();
            PlannedSwing m = Hanging(1f, 20f);
            m.TargetAmplitude = 0.6f; m.Pumping = true;
            var arc = new Vector3[32];
            int allocs = GcAllocs.Count(() => { for (int f = 0; f < 600; f++) { m.Step(Dt); m.PredictApex(2f, arc, out _, out _, out _); } });
            Assert.AreEqual(0, allocs);
        }
    }
}
