using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // LabLayout's ring (2026-10-19): a roster whose evenly spaced arc would wrap past kMaxSweepDeg (slots spaced by two
    // of the LARGEST radii: one aviary or big lizard makes every slot huge) is laid on a ring, each pair of neighbours
    // spaced by their own radii. Its slots keep every pair of neighbours' curbs kEnclosureGap apart and fit in the
    // sweep; a roster that does not wrap keeps the arc. RED: the old arc on the same radii wraps and puts two slots on
    // top of each other.
    public class CreatureLabRingTests
    {
        const float kSpacing = 25f, kArc = 30f, kSearch = 15f;
        static readonly float[] kRoster = { 13.7f, 5.4f, 5f, 14.7f, 5f, 5f, 21f, 5f, 5f, 14.7f, 5f, 18.2f, 16.5f };

        static Vector3 Slot(float angle, float r) => Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.up) * Vector3.forward * r;

        [Test] public void Ring_BigRoster_FitsTheSweep_NeighboursApart()
        {
            LabLayout.Arc(kRoster, kSpacing, kArc, kSearch, out float slotSpacing, out float slotRadius);
            Assert.IsTrue(LabLayout.Wraps(kRoster.Length, slotSpacing, slotRadius), "this roster wraps the even arc");
            float[] a = LabLayout.RingAngles(kRoster, kSpacing, slotRadius, out float ring);
            float sweep = (a[a.Length - 1] - a[0]) * Mathf.Rad2Deg;
            TestContext.Out.WriteLine($"ring radius {ring:F1} m (arc {slotRadius:F1}), sweep {sweep:F0} deg");
            Assert.LessOrEqual(sweep, LabLayout.kMaxSweepDeg + 1e-3f);
            Assert.GreaterOrEqual(ring, slotRadius, "never inside the arc's radius (the player's spot stays clear)");
            for (int k = 0; k + 1 < kRoster.Length; k++)
            {
                float d = (Slot(a[k + 1], ring) - Slot(a[k], ring)).magnitude;
                Assert.GreaterOrEqual(d, kRoster[k] + kRoster[k + 1] + LabLayout.kEnclosureGap, $"slots {k} and {k + 1}");
            }
            // the first and the last are apart across the gap the sweep leaves
            float wrap = (Slot(a[0], ring) - Slot(a[a.Length - 1], ring)).magnitude;
            Assert.GreaterOrEqual(wrap, kRoster[0] + kRoster[kRoster.Length - 1] + LabLayout.kEnclosureGap, "the ends do not meet");
        }

        [Test] public void Ring_SmallRoster_KeepsTheArc()
        {
            var small = new[] { 5f, 6f, 5f };
            LabLayout.Arc(small, kSpacing, kArc, kSearch, out float slotSpacing, out float slotRadius);
            Assert.IsFalse(LabLayout.Wraps(small.Length, slotSpacing, slotRadius), "three enclosures fit the arc");
        }

        // RED: the even arc on the big roster wraps: some pair of slots lands closer than their curbs allow.
        [Test] public void Red_EvenArc_BigRoster_SlotsCollide()
        {
            LabLayout.Arc(kRoster, kSpacing, kArc, kSearch, out float slotSpacing, out float slotRadius);
            int n = kRoster.Length, close = 0;
            for (int a = 0; a < n; a++)
                for (int b = a + 1; b < n; b++)
                {
                    Vector3 pa = LabLayout.ArcSlot(Vector3.zero, Vector3.forward, Vector3.up, a, n, slotSpacing, slotRadius);
                    Vector3 pb = LabLayout.ArcSlot(Vector3.zero, Vector3.forward, Vector3.up, b, n, slotSpacing, slotRadius);
                    if ((pa - pb).magnitude < kRoster[a] + kRoster[b] + LabLayout.kEnclosureGap) close++;
                }
            TestContext.Out.WriteLine($"RED: {close} pairs of even-arc slots overlap");
            Assert.Greater(close, 0, "the even arc wraps onto itself");
        }
    }
}
