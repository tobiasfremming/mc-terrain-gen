using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // OctopusBody in planned mode (planned-body rework 2026-10-19): the body follows a planned rigid pose (origin = the
    // arm crown, body space), the soft lattice only adds secondary motion from the pose's acceleration, the arm and
    // mouth roots are kinematic (nothing is handed back to the body).
    public class OctopusPlannedBodyTests
    {
        const float Dt = 1f / 60f;

        // Both shipped cephalopods are planned (the tree octopus since 2026-10-20); the emergent path stays the
        // default (locomotion 0: CephalopodSpec.Default, the emergent tests).
        [Test] public void Swinger_And_Octopus_Assets_ArePlanned_DefaultIsNot()
        {
            Assert.IsTrue(OctopusSwingerTests.Spec().Planned, "Swinger.asset locomotion: planned");
            Assert.IsFalse(CephalopodSpec.Default.Planned);
            Assert.IsTrue(CreatureKitAssets.Load("TreeOctopus").cephalopod.Planned, "TreeOctopus.asset locomotion: planned");
        }

        static Quaternion Hang(Vector3 forward) => OctoMath.Look(forward, Vector3.down);

        // The crown and the arm roots sit exactly on the pose; the rest of the lattice stays near it.
        [Test] public void Planned_BodyFollowsPose()
        {
            foreach (float s in new[] { 1f, 1.5f })
            {
                CephalopodSpec sp = OctopusSwingerTests.Spec();
                var body = new OctopusBody(sp, Vector3.zero, Quaternion.identity, s);
                body.EnablePlanned();
                Vector3 pivot = new Vector3(0f, 3f, 0f) * s;
                float l = 1f * s, worstLattice = 0f, worstRoot = 0f, worstCrown = 0f;
                for (int f = 0; f < 240; f++)
                {
                    float t = f * Dt, a = 0.6f * Mathf.Sin(2f * Mathf.PI * t / (2f * Mathf.Sqrt(s)));
                    Vector3 pos = pivot + new Vector3(Mathf.Sin(a), -Mathf.Cos(a), 0f) * l;
                    Quaternion rot = OctoMath.Look(Vector3.forward, OctoMath.Norm(pos - pivot, Vector3.down));
                    body.SetPlannedPose(pos, rot, f == 0);
                    body.Tick(Dt);
                    for (int k = 0; k < 4; k++)
                        worstCrown = Mathf.Max(worstCrown, (body.Lattice.X[k] - (pos + rot * body.Lattice.Rest[k])).magnitude);
                    for (int i = 0; i < SoftLattice.Count; i++)
                        worstLattice = Mathf.Max(worstLattice, (body.Lattice.X[i] - (pos + rot * body.Lattice.Rest[i])).magnitude);
                    for (int arm = 0; arm < body.ArmCount; arm++)
                        worstRoot = Mathf.Max(worstRoot, (body.Arms[arm].X[0] - (pos + rot * (OctopusShape.ArmRoot(sp, arm) * s))).magnitude);
                }
                TestContext.Out.WriteLine($"scale {s}: crown {worstCrown:E1} m, roots {worstRoot:E1} m, lattice off the pose <= {worstLattice:F3} m");
                Assert.Less(worstCrown, 1e-4f * s);
                Assert.Less(worstRoot, 1e-4f * s);
                Assert.Less(worstLattice, 0.25f * sp.MantleLength * s + 1e-4f);
                Assert.Greater(worstLattice, 0.002f * s, "no secondary motion at all");
            }
        }

        // A steady acceleration up stretches the soft mantle down (lags behind), by about a / w^2 at the apex.
        [Test] public void Planned_SecondaryLagsAcceleration()
        {
            CephalopodSpec sp = OctopusSwingerTests.Spec();
            var body = new OctopusBody(sp, Vector3.zero, Quaternion.identity, 1f);
            body.EnablePlanned();
            Quaternion rot = Hang(Vector3.forward);
            float acc = 6f;
            for (int f = 0; f <= 90; f++)
            {
                float t = f * Dt;
                body.SetPlannedPose(new Vector3(0f, 0.5f * acc * t * t, 0f), rot, f == 0);
                body.Tick(Dt);
            }
            float t1 = 90 * Dt;
            Vector3 goal = new Vector3(0f, 0.5f * acc * t1 * t1, 0f) + rot * body.Lattice.Rest[OctopusShape.Apex];
            float lag = goal.y - body.Lattice.X[OctopusShape.Apex].y, w = OctopusBody.SecondaryOmega[4];
            TestContext.Out.WriteLine($"apex lag {lag:F4} m, a / w^2 {acc / (w * w):F4} m");
            Assert.AreEqual(acc / (w * w), lag, 0.3f * acc / (w * w));
        }

        // Asleep, nothing moves (the breathing only reaches the bones).
        [Test] public void Planned_SleepFreezes()
        {
            CephalopodSpec sp = OctopusSwingerTests.Spec();
            var body = new OctopusBody(sp, Vector3.zero, Quaternion.identity, 1f);
            body.EnablePlanned();
            Quaternion rot = Hang(Vector3.forward);
            for (int f = 0; f < 60; f++) { body.SetPlannedPose(Vector3.zero, rot, f == 0); body.Tick(Dt); }
            body.SleepPlanned();
            Vector3 tip = body.Arms[0].X[body.Arms[0].N - 1], apex = body.Lattice.X[OctopusShape.Apex];
            for (int f = 0; f < 60; f++) { body.SetPlannedPose(Vector3.zero, rot, false); body.Tick(Dt); }
            Assert.IsTrue(body.Asleep);
            Assert.AreEqual(tip, body.Arms[0].X[body.Arms[0].N - 1]);
            Assert.AreEqual(apex, body.Lattice.X[OctopusShape.Apex]);
        }
    }
}
