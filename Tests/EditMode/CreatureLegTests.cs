using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class CreatureLegTests
    {
        CreatureSpecies sp;
        PlaneField plane;
        TiltedPlaneField tilt;
        HalfSlopeField half;
        LedgeField ledge;

        [TearDown]
        public void TearDown()
        {
            if (sp != null) Object.DestroyImmediate(sp);
            if (plane != null) Object.DestroyImmediate(plane);
            if (tilt != null) Object.DestroyImmediate(tilt);
            if (half != null) Object.DestroyImmediate(half);
            if (ledge != null) Object.DestroyImmediate(ledge);
        }

        CreatureSim OnPlane()
        {
            sp = CreatureTestSpecies.Lizardish();
            plane = ScriptableObject.CreateInstance<PlaneField>();
            return new CreatureSim(sp, new FieldGroundProbe(plane), _ => Vector3.up, Vector3.zero, Vector3.forward);
        }

        static void Walk(CreatureSim sim, int ticks, System.Action<CreatureSim> each = null)
        {
            for (int t = 0; t < ticks; t++)
            {
                sim.Tick(1f / 60f, new Vector3(0f, 0f, 1000f));
                if (each != null) each(sim);
            }
        }

        [Test] public void Gait_NeverTwoGroupsSteppingAtOnce()
        {
            var sim = OnPlane();
            Walk(sim, 600, s =>
            {
                int g = -1;
                for (int l = 0; l < s.Legs.Length; l++)
                {
                    if (!s.Legs[l].stepping) continue;
                    int lg = s.Species.legs[l].gaitGroup;
                    if (g >= 0) Assert.AreEqual(g, lg, "two groups stepping at t=" + s.Time);
                    g = lg;
                }
            });
        }

        [Test] public void Gait_EveryLegSteps()
        {
            var sim = OnPlane();
            var counts = new int[sim.Legs.Length];
            var was = new bool[sim.Legs.Length];
            Walk(sim, 600, s =>
            {
                for (int l = 0; l < counts.Length; l++)
                {
                    if (s.Legs[l].stepping && !was[l]) counts[l]++;
                    was[l] = s.Legs[l].stepping;
                }
            });
            for (int l = 0; l < counts.Length; l++) Assert.GreaterOrEqual(counts[l], 5, "leg " + l);
            Assert.GreaterOrEqual(sim.StepsTaken, 20);
        }

        [Test] public void PlantedFeet_DoNotMove()
        {
            var sim = OnPlane();
            int n = sim.Legs.Length;
            var prev = new Vector3[n]; var prevStep = new bool[n];
            for (int l = 0; l < n; l++) { prev[l] = sim.Legs[l].foot; prevStep[l] = sim.Legs[l].stepping; }
            int checkedCount = 0;
            Walk(sim, 600, s =>
            {
                for (int l = 0; l < n; l++)
                {
                    if (!prevStep[l] && !s.Legs[l].stepping)
                    {
                        Assert.Less((s.Legs[l].foot - prev[l]).magnitude, 1e-5f, "leg " + l);
                        checkedCount++;
                    }
                    prev[l] = s.Legs[l].foot; prevStep[l] = s.Legs[l].stepping;
                }
            });
            Assert.Greater(checkedCount, 500);
        }

        [Test] public void Legs_OnSlope_FeetWithinTolerance()
        {
            sp = CreatureTestSpecies.Lizardish();
            tilt = ScriptableObject.CreateInstance<TiltedPlaneField>();
            // 30 degrees about the walking direction (+Z): normal leans toward +X.
            tilt.normal = new Vector3(Mathf.Sin(30f * Mathf.Deg2Rad), Mathf.Cos(30f * Mathf.Deg2Rad), 0f);
            var sim = new CreatureSim(sp, new FieldGroundProbe(tilt), _ => Vector3.up, Vector3.zero, Vector3.forward);
            int planted = 0;
            Walk(sim, 600, s =>
            {
                for (int l = 0; l < s.Legs.Length; l++)
                {
                    if (s.Legs[l].stepping) continue;
                    planted++;
                    Assert.Less(Mathf.Abs(Vector3.Dot(s.Legs[l].foot, tilt.normal)), 0.02f, "leg " + l);
                }
                for (int i = 0; i < s.Joints.Length; i++)
                {
                    Vector3 j = s.Joints[i];
                    float surfaceY = -tilt.normal.x * j.x / tilt.normal.y;
                    Assert.GreaterOrEqual(j.y - surfaceY, sp.joints[i].clearance - 0.02f, "joint " + i);
                }
            });
            Assert.Greater(planted, 500);
        }

        [Test] public void Body_Uphill_NoSinking()
        {
            sp = CreatureTestSpecies.Lizardish();
            tilt = ScriptableObject.CreateInstance<TiltedPlaneField>();
            // 30 degrees rising along +Z: normal leans toward -Z.
            tilt.normal = new Vector3(0f, Mathf.Cos(30f * Mathf.Deg2Rad), -Mathf.Sin(30f * Mathf.Deg2Rad));
            var sim = new CreatureSim(sp, new FieldGroundProbe(tilt), _ => Vector3.up, Vector3.zero, Vector3.forward);
            Walk(sim, 480, s =>
            {
                for (int i = 0; i < s.Joints.Length; i++)
                {
                    Vector3 j = s.Joints[i];
                    float surfaceY = tilt.normal.z * -j.z / tilt.normal.y;
                    Assert.GreaterOrEqual(j.y - surfaceY, sp.joints[i].clearance - 0.02f, "joint " + i + " t=" + s.Time);
                }
            });
        }

        [Test] public void LegLengths_NeverExceeded()
        {
            var sim = OnPlane();
            Walk(sim, 600, s =>
            {
                for (int l = 0; l < s.Legs.Length; l++)
                {
                    LegSpec ls = s.Species.legs[l];
                    var st = s.Legs[l];
                    Assert.AreEqual(ls.upperLength * s.SizeScale, (st.elbow - st.shoulder).magnitude, 1e-3f);
                    Assert.LessOrEqual((st.foot - st.elbow).magnitude, ls.lowerLength * s.SizeScale + 1e-3f);
                }
            });
        }

        [Test] public void Idle_FeetSettleUnderBody()
        {
            var sim = OnPlane();
            Walk(sim, 300);
            for (int t = 0; t < 180; t++) sim.Tick(1f / 60f, sim.Joints[0]);
            for (int l = 0; l < sim.Legs.Length; l++)
            {
                LegSpec ls = sp.legs[l];
                Vector3 so = ls.restOffset; so.x *= ls.side;
                Vector3 rest = sim.Joints[ls.attachJoint] + sim.Rotations[ls.attachJoint] * so;
                rest.y = 0f;
                Assert.Less((sim.Legs[l].foot - rest).magnitude, 0.3f * ls.stepThreshold + 0.01f, "leg " + l);
            }
        }

        static int[] CountSteps(CreatureSim sim, int ticks)
        {
            var counts = new int[sim.Legs.Length];
            var was = new bool[counts.Length];
            Walk(sim, ticks, s =>
            {
                for (int l = 0; l < counts.Length; l++)
                {
                    if (s.Legs[l].stepping && !was[l]) counts[l]++;
                    was[l] = s.Legs[l].stepping;
                }
            });
            return counts;
        }

        [Test] public void Legs_HalfSlope_OneSide_FeetGrounded()
        {
            int[] flat = CountSteps(OnPlane(), 600);
            TearDown();
            sp = CreatureTestSpecies.Lizardish();
            half = ScriptableObject.CreateInstance<HalfSlopeField>();
            var sim = new CreatureSim(sp, new FieldGroundProbe(half), _ => Vector3.up, Vector3.zero, Vector3.forward);
            var counts = new int[sim.Legs.Length];
            var was = new bool[counts.Length];
            float worst = 0f, seg = 0f;
            Walk(sim, 600, s =>
            {
                for (int l = 0; l < counts.Length; l++)
                {
                    if (s.Legs[l].stepping && !was[l]) counts[l]++;
                    was[l] = s.Legs[l].stepping;
                    if (s.Legs[l].stepping) continue;
                    Vector3 f = s.Legs[l].foot;
                    worst = Mathf.Max(worst, Mathf.Abs(f.y - half.SurfaceY(f.x)));
                }
                for (int i = 1; i < s.Joints.Length; i++)
                    seg = Mathf.Max(seg, Mathf.Abs((s.Joints[i] - s.Joints[i - 1]).magnitude / (sp.spacing * s.SizeScale) - 1f));
            });
            Assert.Less(worst, 0.02f, "worst planted foot height error");
            Assert.Less(seg, 0.01f, "segment length error");
            Assert.Greater(sim.Joints[0].z, 0.5f, "head displacement");
            for (int l = 0; l < counts.Length; l++)
                Assert.Less(counts[l], 3 * flat[l], "leg " + l + " steps " + counts[l] + " vs flat " + flat[l]);
        }

        [Test] public void Walk_SpawnOverDrop_StillMoves()
        {
            sp = CreatureTestSpecies.Lizardish();
            ledge = ScriptableObject.CreateInstance<LedgeField>();
            var sim = new CreatureSim(sp, new FieldGroundProbe(ledge), _ => Vector3.up, Vector3.zero, Vector3.forward);
            Walk(sim, 600);
            Assert.Greater(sim.Joints[0].z, 0.5f);
        }

        [Test] public void Walk_SpeedIsSmooth()
        {
            var sim = OnPlane();
            float accel = sp.accel, dt = 1f / 60f;
            Walk(sim, 120);
            float prev = sim.Speed, min = float.MaxValue, sum = 0f, maxJump = 0f;
            int n = 600;
            Walk(sim, n, s =>
            {
                maxJump = Mathf.Max(maxJump, Mathf.Abs(s.Speed - prev));
                prev = s.Speed; min = Mathf.Min(min, s.Speed); sum += s.Speed;
            });
            float mean = sum / n;
            Assert.LessOrEqual(maxJump, 3f * accel * dt + 1e-4f, "max speed change per tick");
            Assert.Greater(min, 0.5f * mean, "min " + min + " mean " + mean);
        }
    }
}
