using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // AirChain over Vector3[]: exact lengths, 3D bend cones, the shoulder bob travelling down the body,
    // a stabilised head. Prototype (measure.py, grip x1, level flight): girdle bob 0.111 m p-p, mid-body
    // 0.060 (lags ~0.18 s), tail 0.027; chain bends <= 1.9 deg; segment error ~1e-15.
    public class WingedSerpentAirChainTests
    {
        const float H = 1f / 120f;
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        // FlightModel + CreatureWings driving an AirChain (the Phase B air sub-step, minus the sim).
        sealed class Flyer
        {
            public WingedSerpentFlightTests.Rig rig;
            public AirChain chain;
            public float scale;

            public void Step()
            {
                FlightModel m = rig.model;
                m.Step(H, rig.wings);
                chain.Step(H, rig.joints, m.Position, m.Velocity, m.BankedUp, m.Up);
                chain.WriteRotations(rig.rots);
                rig.wings.Pose();
            }
        }

        Flyer Make(float scale = 1f)
        {
            var s = WingedSerpentTestSpecies.Create();
            trash.Add(s.species);
            var rig = WingedSerpentFlightTests.MakeRig(s, scale);
            var chain = new AirChain(s.species, s.flight, WingedSerpentTestSpecies.Girdle, scale);
            chain.Reset(rig.model.Position, rig.model.Velocity);
            return new Flyer { rig = rig, chain = chain, scale = scale };
        }

        static float Bend(Vector3[] j, int i) => Vector3.Angle(j[i - 1] - j[i], j[i] - j[i + 1]);

        static void CheckChain(Flyer f, CreatureSpecies sp, string at)
        {
            Vector3[] j = f.rig.joints;
            float seg = f.chain.SegmentLength;
            for (int i = 0; i + 1 < j.Length; i++)
                Assert.AreEqual(seg, (j[i + 1] - j[i]).magnitude, 1e-4f * seg, $"segment {i} at {at}");
            for (int i = 1; i + 1 < j.Length; i++)
            {
                float lim = sp.joints[i].rigid ? 0f : sp.joints[i].bendLimitDeg;
                Assert.LessOrEqual(Bend(j, i), lim + 0.1f, $"bend at joint {i} at {at}");
            }
        }

        [Test] public void AirChain_LengthsExact()
        {
            var f = Make();
            var sp = f.rig.s.species;
            // level, climb, banked turn
            for (int k = 0; k < Mathf.RoundToInt(12f / H); k++)
            {
                float t = k * H;
                f.rig.model.AltitudeGoal = t < 4f ? 5f : 8f;
                f.rig.model.TurnRateGoal = t < 7f ? 0f : 0.6f;
                f.Step();
                if (k % 7 == 0) CheckChain(f, sp, $"t {t:F2}");
            }
        }

        // The cone is 3D: a hard pull sideways and down at once still keeps every joint inside it.
        [Test] public void AirChain_BendCone3D()
        {
            var s = WingedSerpentTestSpecies.Create();
            trash.Add(s.species);
            var sp = s.species;
            int n = sp.JointCount, g = WingedSerpentTestSpecies.Girdle;
            var chain = new AirChain(sp, s.flight, g, 1f);
            var j = new Vector3[n];
            for (int i = 0; i < n; i++) j[i] = new Vector3(0f, 5f, -sp.spacing * (i - g));
            Vector3 p = j[g], v = new Vector3(0f, 0f, 5f);
            chain.Reset(p, v);
            float maxBend = 0f;
            for (int k = 0; k < 600; k++)
            {
                float t = k * H;
                // a corkscrew: yaw and pitch at once, 3 rad/s
                v = 5f * new Vector3(Mathf.Sin(3f * t), 0.6f * Mathf.Sin(2.2f * t), Mathf.Cos(3f * t));
                p += v * H;
                chain.Step(H, j, p, v, Vector3.up, Vector3.up);
                for (int i = 1; i + 1 < n; i++)
                {
                    float lim = sp.joints[i].rigid ? 0f : sp.joints[i].bendLimitDeg;
                    float b = Bend(j, i);
                    maxBend = Mathf.Max(maxBend, b - lim);
                    Assert.LessOrEqual(b, lim + 0.1f, $"bend at joint {i}, step {k}");
                }
            }
            TestContext.WriteLine($"max bend over the limit: {maxBend:F3} deg");
        }

        static float PeakToPeak(List<float> a) { float lo = float.MaxValue, hi = float.MinValue; foreach (float x in a) { lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); } return hi - lo; }

        // high-pass (remove a moving average) so the slow altitude drift does not count as bob
        static List<float> HighPass(List<float> a, int w)
        {
            var o = new List<float>();
            for (int i = 0; i < a.Count; i++)
            {
                float s = 0f; int c = 0;
                for (int k = i - w / 2; k <= i + w / 2; k++) if (k >= 0 && k < a.Count) { s += a[k]; c++; }
                o.Add(a[i] - s / c);
            }
            return o;
        }

        static float Lag(List<float> a, List<float> b, int maxLag)
        {
            int best = 0; float bestC = float.MinValue;
            for (int l = 0; l <= maxLag; l++)
            {
                float c = 0f;
                for (int i = 0; i + l < a.Count; i++) c += a[i] * b[i + l];
                if (c > bestC) { bestC = c; best = l; }
            }
            return best * H;
        }

        // The wingbeat's shoulder bob travels down the body: the mid-body bobs less and later, the tail less still.
        [Test] public void AirChain_FollowsShoulderBob()
        {
            var f = Make();
            var gy = new List<float>(); var my = new List<float>(); var ty = new List<float>();
            int mid = 16, tail = f.rig.joints.Length - 1;
            for (int k = 0; k < Mathf.RoundToInt(7f / H); k++)
            {
                f.Step();
                if (k * H < 3.5f) continue;
                gy.Add(f.rig.joints[WingedSerpentTestSpecies.Girdle].y); my.Add(f.rig.joints[mid].y); ty.Add(f.rig.joints[tail].y);
            }
            var g = HighPass(gy, 45); var m = HighPass(my, 45); var t = HighPass(ty, 45);
            float pg = PeakToPeak(g), pm = PeakToPeak(m), pt = PeakToPeak(t);
            float lag = Lag(g, m, 60);
            TestContext.WriteLine($"bob p-p: girdle {pg:F3} m, mid {pm:F3}, tail {pt:F3}; mid lag {lag:F3} s (proto 0.111 / 0.060 / 0.027, 0.18 s)");
            Assert.That(pg, Is.InRange(0.06f, 0.2f), "girdle bob (stylised, ~10 cm)");
            Assert.Less(pm, pg, "mid-body bobs less than the shoulders");
            Assert.Greater(pm, 0.15f * pg, "mid-body follows the bob");
            Assert.Less(pt, pm, "tail bobs least");
            Assert.That(lag, Is.InRange(0.05f, 0.35f), "the wave travels down the body");
        }

        // The head glides: its bob is a fraction of the shoulders', and it stays ahead of the girdle.
        [Test] public void AirChain_HeadStabilised()
        {
            var f = Make();
            var gy = new List<float>(); var hy = new List<float>(); var ty = new List<float>(); float miss = 0f;
            int g = WingedSerpentTestSpecies.Girdle;
            float minAhead = float.MaxValue;
            for (int k = 0; k < Mathf.RoundToInt(7f / H); k++)
            {
                f.Step();
                if (k * H < 3.5f) continue;
                gy.Add(f.rig.joints[g].y); hy.Add(f.rig.joints[0].y); ty.Add(f.chain.HeadTarget.y);
                miss = Mathf.Max(miss, (f.chain.HeadTarget - f.rig.joints[0]).magnitude);
                float ahead = Vector3.Dot(f.rig.joints[0] - f.rig.joints[g], f.rig.model.Forward);
                minAhead = Mathf.Min(minAhead, ahead);
            }
            float pg = PeakToPeak(HighPass(gy, 45)), ph = PeakToPeak(HighPass(hy, 45));
            float neck = f.chain.SegmentLength * g;
            TestContext.WriteLine($"bob p-p: girdle {pg:F3} m, head target {PeakToPeak(HighPass(ty, 45)):F3}, head {ph:F3}; head misses its target by <= {miss:F3} m, leads by >= {minAhead:F3} of {neck:F3} m");
            Assert.Less(ph, 0.4f * pg, "head bob");
            Assert.Greater(minAhead, 0.85f * neck, "head leads");
            Assert.Less(miss, 0.1f * neck, "head reaches its target");
        }
    }
}
