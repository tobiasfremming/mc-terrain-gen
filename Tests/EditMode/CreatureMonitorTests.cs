using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TerrainGen.Tests
{
    // The desert monitor's gait on a flat plane (ruling R16): long forward reach, long strides, a brisk
    // cruise (R8), and the joint-range and skinning checks the test species already pass.
    public class CreatureMonitorTests
    {
        const float kDt = 1f / 60f;
        const int kWarmup = 120, kTicks = 600;

        static CreatureSpecies Monitor()
        {
            var sp = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(CreatureWorldTests.kMonitorPath);
            Assert.IsNotNull(sp, CreatureWorldTests.kMonitorPath);
            return sp;
        }

        static CreatureSim Cruise(CreatureSpecies sp)
        {
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward);
            for (int t = 0; t < kWarmup; t++) sim.Tick(kDt, new Vector3(0f, 0f, 1000f));
            return sim;
        }

        static int FrontJoint(CreatureSpecies sp)
        {
            int j = int.MaxValue;
            foreach (var l in sp.legs) j = Mathf.Min(j, l.attachJoint);
            return j;
        }

        [Test] public void Monitor_FrontFootReachesForward()
        {
            var sp = Monitor();
            var sim = Cruise(sp);
            int front = FrontJoint(sp), c = sim.Legs.Length, landings = 0;
            var was = new bool[c];
            for (int l = 0; l < c; l++) was[l] = sim.Legs[l].stepping;
            float worst = float.MaxValue, sum = 0f;
            for (int t = 0; t < kTicks; t++)
            {
                sim.Tick(kDt, new Vector3(0f, 0f, 1000f));
                for (int l = 0; l < c; l++)
                {
                    LegSpec ls = sp.legs[l];
                    bool landed = was[l] && !sim.Legs[l].stepping;
                    was[l] = sim.Legs[l].stepping;
                    if (!landed || ls.attachJoint != front) continue;
                    Vector3 fwd = sim.Rotations[ls.attachJoint] * Vector3.forward;
                    float ahead = Vector3.Dot(sim.Legs[l].foot - sim.Legs[l].shoulder, fwd) / ((ls.upperLength + ls.lowerLength) * sim.SizeScale);
                    worst = Mathf.Min(worst, ahead); sum += ahead; landings++;
                }
            }
            TestContext.Out.WriteLine($"front landings {landings}: ahead of shoulder min {worst:F2}, mean {sum / Mathf.Max(1, landings):F2} x leg length");
            Assert.GreaterOrEqual(landings, 6, "too few front-foot landings");
            Assert.GreaterOrEqual(worst, 0.6f, "front foot lands too close to its shoulder");
        }

        [Test] public void Monitor_StrideLength()
        {
            var sp = Monitor();
            var sim = Cruise(sp);
            int c = sim.Legs.Length, steps = 0;
            var was = new bool[c]; var lift = new Vector3[c]; var prev = new Vector3[c]; var lifted = new bool[c];
            for (int l = 0; l < c; l++) { was[l] = sim.Legs[l].stepping; lift[l] = sim.Legs[l].foot; }
            float sum = 0f, min = float.MaxValue;
            for (int t = 0; t < kTicks; t++)
            {
                for (int l = 0; l < c; l++) prev[l] = sim.Legs[l].foot;
                sim.Tick(kDt, new Vector3(0f, 0f, 1000f));
                for (int l = 0; l < c; l++)
                {
                    bool s = sim.Legs[l].stepping;
                    if (s && !was[l]) { lift[l] = prev[l]; lifted[l] = true; }
                    if (!s && was[l] && lifted[l]) // a step already in flight after the warm-up is not counted
                    {
                        LegSpec ls = sp.legs[l];
                        Vector3 d = sim.Legs[l].foot - lift[l]; d.y = 0f;
                        float r = d.magnitude / ((ls.upperLength + ls.lowerLength) * sim.SizeScale);
                        sum += r; min = Mathf.Min(min, r); steps++;
                    }
                    was[l] = s;
                }
            }
            float mean = sum / Mathf.Max(1, steps);
            TestContext.Out.WriteLine($"steps {steps}: stride mean {mean:F2}, min {min:F2} x leg length");
            Assert.GreaterOrEqual(steps, 12, "too few steps");
            Assert.GreaterOrEqual(mean, 0.9f, "mean stride per step");
        }

        [Test] public void Monitor_CruiseSpeed()
        {
            var sp = Monitor();
            var sim = Cruise(sp);
            float sum = 0f, min = float.MaxValue;
            for (int t = 0; t < kTicks; t++)
            {
                sim.Tick(kDt, new Vector3(0f, 0f, 1000f));
                sum += sim.Speed; min = Mathf.Min(min, sim.Speed);
            }
            float mean = sum / kTicks;
            TestContext.Out.WriteLine($"cruise speed mean {mean:F2} m/s, min {min:F2} m/s");
            Assert.GreaterOrEqual(mean, 0.5f, "cruise speed (R8)");
        }

        // ---- R17: gait-coupled spine sway ----

        // Leftward offset of the displayed joint from the body path, in the path frame.
        static float LeftOffset(CreatureSim sim, int j)
        {
            Vector3 right = sim.Rotations[j] * Vector3.right;
            return Vector3.Dot(sim.Joints[j] - sim.Path[j], -right);
        }

        // Forward reach of the left legs minus the right legs of a girdle, in leg lengths.
        static float ReachDiff(CreatureSim sim, int joint)
        {
            float d = 0f;
            Vector3 fwd = sim.Rotations[joint] * Vector3.forward;
            for (int l = 0; l < sim.Legs.Length; l++)
            {
                LegSpec ls = sim.Species.legs[l];
                if (ls.attachJoint != joint) continue;
                float reach = Vector3.Dot(sim.Legs[l].foot - sim.Path[joint], fwd) / ((ls.upperLength + ls.lowerLength) * sim.SizeScale);
                d -= ls.side * reach; // left (-1) counts positive
            }
            return d;
        }

        static float Correlation(float[] a, float[] b, int n)
        {
            double ma = 0, mb = 0;
            for (int i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
            ma /= n; mb /= n;
            double sab = 0, saa = 0, sbb = 0;
            for (int i = 0; i < n; i++) { double x = a[i] - ma, y = b[i] - mb; sab += x * y; saa += x * x; sbb += y * y; }
            return (float)(sab / System.Math.Sqrt(System.Math.Max(1e-20, saa * sbb)));
        }

        static int HindJoint(CreatureSpecies sp)
        {
            int j = -1;
            foreach (var l in sp.legs) j = Mathf.Max(j, l.attachJoint);
            return j;
        }

        // At cruise the shoulder girdle swings toward the front leg that reaches forward (phase-locked to
        // the feet, not a free sine), the hip girdle swings the other way (standing S-wave), and the swing
        // is big enough to see: >= 1.5 cm peak to peak at the shoulders.
        [Test] public void Gait_SpineSway_PhaseLockedToSteps()
        {
            var sp = Monitor();
            Assert.Greater(sp.gaitSwayDeg, 0f, "the monitor has no gait sway");
            var sim = Cruise(sp);
            int front = FrontJoint(sp), hind = HindJoint(sp);
            var off = new float[kTicks]; var diff = new float[kTicks]; var hip = new float[kTicks];
            float lo = float.MaxValue, hi = float.MinValue;
            for (int t = 0; t < kTicks; t++)
            {
                sim.Tick(kDt, new Vector3(0f, 0f, 1000f));
                off[t] = LeftOffset(sim, front); diff[t] = ReachDiff(sim, front); hip[t] = LeftOffset(sim, hind);
                lo = Mathf.Min(lo, off[t]); hi = Mathf.Max(hi, off[t]);
            }
            float cr = Correlation(off, diff, kTicks), ch = Correlation(off, hip, kTicks);
            TestContext.Out.WriteLine($"shoulder sway p2p {(hi - lo) * 100f:F1} cm; corr(shoulder, reach L-R) {cr:F2}; corr(shoulder, hip) {ch:F2}");
            Assert.Greater(cr, 0.5f, "shoulder sway not phase-locked to the front legs");
            Assert.Less(ch, -0.5f, "hip girdle not in opposite phase");
            Assert.GreaterOrEqual(hi - lo, 0.015f, "shoulder sway amplitude");
        }

        [Test] public void Gait_SpineSway_ZeroWhenIdle()
        {
            var sp = Monitor();
            var sim = Cruise(sp);
            for (int t = 0; t < 90; t++) sim.Tick(kDt, sim.Joints[0]); // brake and settle for 1.5 s
            int c = sp.legs.Length;
            var start = new float[c]; float worst = 0f;
            for (int l = 0; l < c; l++) start[l] = LeftOffset(sim, sp.legs[l].attachJoint);
            for (int t = 0; t < 60; t++)
            {
                sim.Tick(kDt, sim.Joints[0]);
                for (int l = 0; l < c; l++)
                    worst = Mathf.Max(worst, Mathf.Abs(LeftOffset(sim, sp.legs[l].attachJoint) - start[l]));
            }
            TestContext.Out.WriteLine($"idle girdle lateral motion {worst * 1000f:F2} mm");
            Assert.Less(worst, 0.005f);
        }

        // The sway must keep what the gait guarantees: planted feet stay put, segments keep their length,
        // the speed stays smooth, the enclosure holds.
        [Test] public void Monitor_PlantedFeet_DoNotMove_SegmentsExact()
        {
            var sp = Monitor();
            var sim = Cruise(sp);
            int n = sim.Legs.Length, checkedCount = 0;
            var prev = new Vector3[n]; var prevStep = new bool[n];
            for (int l = 0; l < n; l++) { prev[l] = sim.Legs[l].foot; prevStep[l] = sim.Legs[l].stepping; }
            float seg = 0f;
            for (int t = 0; t < kTicks; t++)
            {
                // a weaving walk, so the path bends as well as sways
                Vector3 fwd = sim.Rotations[0] * Vector3.forward;
                sim.Tick(kDt, sim.Joints[0] + Quaternion.AngleAxis(40f * Mathf.Sin(t / 60f), Vector3.up) * fwd * 2f);
                for (int l = 0; l < n; l++)
                {
                    if (!prevStep[l] && !sim.Legs[l].stepping) { Assert.Less((sim.Legs[l].foot - prev[l]).magnitude, 1e-5f, "leg " + l); checkedCount++; }
                    prev[l] = sim.Legs[l].foot; prevStep[l] = sim.Legs[l].stepping;
                }
                // the sway bends the body but keeps the chain's own segment lengths (the path's, which the
                // ground step already varies a little where the clearance changes along the body)
                for (int i = 1; i < sim.Joints.Length; i++)
                {
                    float want = (sim.Path[i] - sim.Path[i - 1]).magnitude;
                    seg = Mathf.Max(seg, Mathf.Abs((sim.Joints[i] - sim.Joints[i - 1]).magnitude - want) / (sp.spacing * sim.SizeScale));
                }
            }
            Assert.Greater(checkedCount, 500);
            Assert.Less(seg, 1e-4f, "segment length change from the sway");
        }

        [Test] public void Monitor_SpeedIsSmooth()
        {
            var sp = Monitor();
            var sim = Cruise(sp);
            float prev = sim.Speed, min = float.MaxValue, sum = 0f, maxJump = 0f;
            for (int t = 0; t < kTicks; t++)
            {
                sim.Tick(kDt, new Vector3(0f, 0f, 1000f));
                maxJump = Mathf.Max(maxJump, Mathf.Abs(sim.Speed - prev));
                prev = sim.Speed; min = Mathf.Min(min, sim.Speed); sum += sim.Speed;
            }
            float mean = sum / kTicks;
            Assert.LessOrEqual(maxJump, 3f * sp.accel * kDt + 1e-4f, "max speed change per tick");
            Assert.Greater(min, 0.5f * mean, "min " + min + " mean " + mean);
        }

        [Test] public void Monitor_TargetOutside_Contained()
        {
            var sp = Monitor();
            var area = EnclosureArea.For(Vector3.zero, Vector3.up, 4f, sp, 1f);
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward) { Bounds = area };
            var target = new Vector3(30f, 0f, 5f);
            for (int t = 0; t < 1200; t++)
            {
                sim.Tick(1f / 30f, target);
                for (int i = 0; i < sim.Joints.Length; i++)
                    Assert.LessOrEqual(area.PlanarDistance(sim.Joints[i]), area.radius, "joint " + i + " t=" + sim.Time);
                for (int l = 0; l < sim.Legs.Length; l++)
                    Assert.LessOrEqual(area.PlanarDistance(sim.Legs[l].foot), area.radius, "foot " + l + " t=" + sim.Time);
            }
        }

        [Test] public void Monitor_TightTurn_JointRangesStayBounded()
        {
            CreatureLegTests.TightTurnJointRanges(Monitor());
        }

        [Test] public void Monitor_MaxBend_NoInvertedTriangles()
        {
            CreatureMeshTests.MaxBendNoInvertedTriangles(Monitor());
        }
    }
}
