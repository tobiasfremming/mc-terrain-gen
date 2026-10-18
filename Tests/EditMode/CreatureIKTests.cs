using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class CreatureIKTests
    {
        [Test] public void Reachable_ExactLengths()
        {
            Vector3 target = new Vector3(0.15f, -0.1f, 0.05f);
            Vector3 elbow = TwoBoneIK.Solve(Vector3.zero, target, 0.11f, 0.10f, Vector3.up, out Vector3 foot);
            Assert.AreEqual(0.11f, elbow.magnitude, 1e-4f);
            Assert.AreEqual(0.10f, (foot - elbow).magnitude, 1e-4f);
            Assert.AreEqual(0f, (foot - target).magnitude, 1e-4f);
        }

        [Test] public void Unreachable_ClampsAlongDirection()
        {
            Vector3 dir = new Vector3(1f, -0.5f, 0.3f).normalized;
            TwoBoneIK.Solve(Vector3.zero, dir * 0.5f, 0.11f, 0.10f, Vector3.up, out Vector3 foot);
            Assert.AreEqual(0.21f, foot.magnitude, 1e-3f);
            Assert.LessOrEqual(foot.magnitude, 0.21f);
            Assert.AreEqual(1f, Vector3.Dot(foot.normalized, dir), 1e-5f);
        }

        [Test] public void ElbowOnPoleSide()
        {
            Vector3 target = new Vector3(0.1f, -0.1f, 0.05f);
            foreach (Vector3 pole in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                Vector3 elbow = TwoBoneIK.Solve(Vector3.zero, target, 0.11f, 0.10f, pole, out Vector3 foot);
                Vector3 axis = foot.normalized;
                Vector3 perp = pole - axis * Vector3.Dot(pole, axis);
                Assert.Greater(Vector3.Dot(elbow - 0.5f * foot, perp), 0f, "pole " + pole);
            }
        }

        [Test] public void Degenerate_TargetAtShoulder_Finite()
        {
            Vector3 elbow = TwoBoneIK.Solve(Vector3.one, Vector3.one, 0.11f, 0.10f, Vector3.up, out Vector3 foot);
            foreach (var v in new[] { elbow, foot })
                Assert.IsFalse(float.IsNaN(v.x + v.y + v.z) || float.IsInfinity(v.x + v.y + v.z));
            elbow = TwoBoneIK.Solve(Vector3.zero, Vector3.up * 0.1f, 0.11f, 0.10f, Vector3.up, out foot); // pole parallel to axis
            Assert.IsFalse(float.IsNaN(elbow.x + elbow.y + elbow.z));
        }
    }
}
