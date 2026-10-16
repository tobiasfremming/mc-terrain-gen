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
            float flatDist = FlatHeadDistance(600);
            Assert.GreaterOrEqual(sim.Joints[0].magnitude, 0.7f * flatDist, $"cross-slope progress vs flat {flatDist:F2} m");
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
            var flatSim = OnPlane();
            int[] flat = CountSteps(flatSim, 600);
            float flatDist = flatSim.Joints[0].z;
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
            Assert.GreaterOrEqual(sim.Joints[0].z, 0.7f * flatDist, $"half-slope progress vs flat {flatDist:F2} m");
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
    
        // Upper-bone swing (vs its rest direction, in the attach-joint frame) and knee bend of leg l.
        static void JointRanges(CreatureSim s, int l, out Vector3 upperLocal, out float knee)
        {
            LegState st = s.Legs[l];
            upperLocal = Quaternion.Inverse(s.Rotations[s.Species.legs[l].attachJoint]) * (st.elbow - st.shoulder).normalized;
            knee = Vector3.Angle(st.elbow - st.shoulder, st.foot - st.elbow);
        }

        // The same 5 s minimum-radius circle as MeshBuilder_MaxBend_NoInvertedTriangles: the joint-range
        // step triggers keep every leg near its rest pose, so the skinned mesh cannot fold.
        [Test] public void Legs_TightTurn_JointRangesStayBounded()
        {
            sp = CreatureTestSpecies.Lizardish();
            TightTurnJointRanges(sp);
        }

        // Joint ranges must stay within the species' own limits plus a 15 deg margin (with the 55/35
        // defaults: swing <= 70, knee deviation <= 50) through the tight circle. Shared with the monitor tests.
        internal static void TightTurnJointRanges(CreatureSpecies sp)
        {
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward);
            int c = sim.Legs.Length;
            var restDir = new Vector3[c]; var restKnee = new float[c];
            for (int l = 0; l < c; l++) JointRanges(sim, l, out restDir[l], out restKnee[l]);
            Vector3 centre = new Vector3(0f, 0f, 0.25f);
            float swingMax = 0f, kneeDevMax = 0f, maxBend = 0f, kneeMin = 999f, kneeMax = 0f;
            for (int t = 0; t < 300; t++)
            {
                float a = t / 60f * 3f;
                sim.Tick(1f / 60f, centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.12f);
                for (int i = 1; i < sp.JointCount - 1; i++)
                    maxBend = Mathf.Max(maxBend, Vector3.Angle(sim.Joints[i - 1] - sim.Joints[i], sim.Joints[i] - sim.Joints[i + 1]));
                for (int l = 0; l < c; l++)
                {
                    JointRanges(sim, l, out Vector3 dir, out float knee);
                    float swing = Vector3.Angle(dir, restDir[l]), signed = knee - restKnee[l], dev = Mathf.Abs(signed);
                    // the limits for the direction actually moved in (as CreatureLegs.JointAngles): forward/back
                    // swing blended by the forward share of the move, knee flexion vs extension
                    LegSpec ls = sp.legs[l];
                    Vector3 moved = dir - restDir[l];
                    float fwd = moved.magnitude > 1e-6f ? Mathf.Clamp(moved.z / moved.magnitude, -1f, 1f) : 0f;
                    float swingBound = Mathf.Lerp(ls.BackSwingLimit, ls.ForwardSwingLimit, 0.5f + 0.5f * fwd) + 15f;
                    float kneeBound = (signed > 0f ? ls.KneeFlex : ls.KneeRange) + 15f;
                    swingMax = Mathf.Max(swingMax, swing); kneeDevMax = Mathf.Max(kneeDevMax, dev);
                    kneeMin = Mathf.Min(kneeMin, knee); kneeMax = Mathf.Max(kneeMax, knee);
                    Assert.LessOrEqual(swing, swingBound, $"t={t} leg {l}: upper swing {swing:F0} stepping={sim.Legs[l].stepping}:{sim.Legs[l].stepT:F2} speed={sim.Speed:F2}");
                    Assert.LessOrEqual(dev, kneeBound, $"t={t} leg {l}: knee {knee:F0} vs rest {restKnee[l]:F0}, stepping={sim.Legs[l].stepping}");
                }
            }
            TestContext.Out.WriteLine($"swing max {swingMax:F0} deg, knee {kneeMin:F0}..{kneeMax:F0} deg (deviation max {kneeDevMax:F0}), spine bend max {maxBend:F1}, steps {sim.StepsTaken}");
            Assert.Greater(maxBend, 0.9f * 20f, "the turn never reached the bend limit");
        }
    
        // The lower bone's roll must follow the upper bone (shared knee hinge): projecting the pole onto
        // each bone separately flips it by 180 deg when the pole lies between them, collapsing the knee skin.
        [Test] public void LegBones_LowerRollFollowsUpper()
        {
            sp = CreatureTestSpecies.Lizardish();
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward);
            var w = new Matrix4x4[sp.BoneCount];
            Vector3 centre = new Vector3(0f, 0f, 0.25f);
            for (int t = 0; t < 300; t++)
            {
                float a = t / 60f * 3f;
                sim.Tick(1f / 60f, centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.12f);
                sim.WriteBoneWorld(w, 0);
                for (int l = 0; l < sp.legs.Length; l++)
                {
                    Quaternion up = w[sp.LegBone(l, 0)].rotation, lo = w[sp.LegBone(l, 1)].rotation;
                    LegState st = sim.Legs[l];
                    Assert.Less((lo * Vector3.forward - (st.foot - st.elbow).normalized).magnitude, 1e-3f, "lower +Z");
                    // the hinge axis (+X) is shared: the lower frame is the upper turned about it
                    Assert.Greater(Vector3.Dot(up * Vector3.right, lo * Vector3.right), 0.99f, $"t={t} leg {l}: lower bone roll flipped");
                }
            }
        }
    
        static float FlatHeadDistance(int ticks)
        {
            var s = CreatureTestSpecies.Lizardish();
            var sim = new CreatureSim(s, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward);
            Walk(sim, ticks);
            float d = sim.Joints[0].magnitude;
            Object.DestroyImmediate(s);
            return d;
        }

        // Swinging feet per tick: displacement and its change (second difference) over a run.
        static void SwingMotion(CreatureSim sim, int ticks, System.Func<CreatureSim, Vector3> target, ref float maxStep, ref float maxJerk)
        {
            int c = sim.Legs.Length;
            var p0 = new Vector3[c]; var p1 = new Vector3[c]; var n = new int[c];
            for (int t = 0; t < ticks; t++)
            {
                sim.Tick(1f / 60f, target(sim));
                for (int l = 0; l < c; l++)
                {
                    Vector3 f = sim.Legs[l].foot;
                    if (!sim.Legs[l].stepping) { n[l] = 0; continue; }
                    if (n[l] >= 1) maxStep = Mathf.Max(maxStep, (f - p1[l]).magnitude);
                    if (n[l] >= 2) maxJerk = Mathf.Max(maxJerk, (f - 2f * p1[l] + p0[l]).magnitude);
                    p0[l] = p1[l]; p1[l] = f; n[l]++;
                }
            }
        }

        // A creature braking from cruise to idle mid-step must not pop its swinging feet: the step's
        // landing point may only move continuously, so the foot's per-tick motion stays within the
        // range a cruising swing already has.
        [Test] public void Step_TargetMovesSmoothly_WhileBraking()
        {
            float cruiseStep = 0f, cruiseJerk = 0f, brakeStep = 0f, brakeJerk = 0f, spAccel = 0f, spStepDuration = 0f;
            for (int trial = 0; trial < 12; trial++)
            {
                var s = CreatureTestSpecies.Lizardish();
                spAccel = s.accel; spStepDuration = s.legs[0].stepDuration;
                var sim = new CreatureSim(s, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward);
                Walk(sim, 90);
                SwingMotion(sim, 30 + 2 * trial, _ => new Vector3(0f, 0f, 1000f), ref cruiseStep, ref cruiseJerk);
                SwingMotion(sim, 60, x => x.Joints[0], ref brakeStep, ref brakeJerk); // target = head: brake to idle
                Object.DestroyImmediate(s);
            }
            TestContext.Out.WriteLine($"cruise: step {cruiseStep * 100f:F2} cm jerk {cruiseJerk * 100f:F2} cm; braking: step {brakeStep * 100f:F2} cm jerk {brakeJerk * 100f:F2} cm");
            Assert.Greater(cruiseJerk, 0f);
            Assert.LessOrEqual(brakeStep, 1.25f * cruiseStep, "per-tick foot displacement while braking");
            // Braking legitimately re-aims the landing point (travel = speed * stepDuration): its velocity can
            // switch on within one tick by at most the sim's braking rate (3 * accel) * stepDuration.
            float aimJerk = 3f * spAccel * spStepDuration * (1f / 60f);
            Assert.LessOrEqual(brakeJerk, cruiseJerk + aimJerk, $"per-tick foot jerk while braking (bound {(cruiseJerk + aimJerk) * 100f:F2} cm)");
        }
    }
}
