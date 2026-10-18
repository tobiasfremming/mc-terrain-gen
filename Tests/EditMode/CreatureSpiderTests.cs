using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TerrainGen.Tests
{
    // The arthropod species on the multi-segment legs (design 2026-10-17-multi-segment-legs-spider-design.md):
    // a daddy-longlegs (pholcid: tiny body, eight very long legs, knees high above the body, a bouncy walk), a
    // tarantula (heavy hairy body, thick shorter legs, pedipalps, fangs, slow deliberate walk) and the darkling
    // beetle's insect legs (coxa, femur, tibia, tarsus). Everything at the asset's own scale, on flat ground.
    public class CreatureSpiderTests
    {
        public const string kDaddyPath = "Assets/TerrainGen/Config/Creatures/DaddyLongLegs.asset";
        public const string kTarantulaPath = "Assets/TerrainGen/Config/Creatures/Tarantula.asset";
        const float kDt = 1f / 60f;
        static readonly Vector3 kFar = new Vector3(0f, 0f, 1000f);

        static CreatureSpecies Load(string path)
        {
            var sp = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(path);
            Assert.IsNotNull(sp, path);
            Assert.IsNull(sp.Validate(), path);
            return sp;
        }

        static CreatureSim Flat(CreatureSpecies sp, int seed = 0)
            => new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, sp.scale, seed);

        static bool Walking(CreatureSpecies sp, int l) => !sp.legs[l].feeler;

        // A weaving walk (heading swings +-50 deg) - the gait, planted-feet and leg-order tests run through it.
        static Vector3 Weave(CreatureSim sim, CreatureSpecies sp, int t)
        {
            Vector3 fwd = sim.Rotations[0] * Vector3.forward;
            return sim.Joints[0] + Quaternion.AngleAxis(50f * Mathf.Sin(t / 50f), Vector3.up) * fwd * (2f * sp.scale);
        }

        // ================================================================ shared checks

        // Eight walking legs on two alternating tetrapods (L1 R2 L3 R4 / R1 L2 R3 L4) and two feelers.
        static void SpiderAsAuthored(CreatureSpecies sp, int segments)
        {
            var walking = new List<LegSpec>();
            foreach (var l in sp.legs) if (!l.feeler) walking.Add(l);
            Assert.AreEqual(8, walking.Count, "walking legs");
            Assert.AreEqual(2, sp.legs.Length - walking.Count, "pedipalps");
            for (int i = 0; i < walking.Count; i++)
            {
                LegSpec l = walking[i];
                int pair = i / 2;
                Assert.AreEqual(segments, l.segments.Length, $"leg {i} segments");
                Assert.AreEqual((pair % 2 == 0) == (l.side < 0) ? 0 : 1, l.gaitGroup, $"leg {i}: tetrapod group");
            }
        }

        // Never both tetrapods in the air; every walking leg steps; the feelers never step; it walks.
        static void TetrapodGait(CreatureSpecies sp, int minSteps)
        {
            var sim = Flat(sp);
            int n = sim.Legs.Length, both = 0, airborne = 0;
            var counts = new int[n]; var was = new bool[n];
            for (int t = 0; t < 900; t++)
            {
                sim.Tick(kDt, kFar);
                int g = -1;
                for (int l = 0; l < n; l++)
                {
                    if (sim.Legs[l].stepping && !was[l]) counts[l]++;
                    was[l] = sim.Legs[l].stepping;
                    if (!Walking(sp, l)) { Assert.IsFalse(sim.Legs[l].stepping, $"feeler {l} stepped"); continue; }
                    if (!sim.Legs[l].stepping) continue;
                    if (g >= 0 && g != sp.legs[l].gaitGroup) both++;
                    g = sp.legs[l].gaitGroup;
                }
                if (g >= 0) airborne++;
            }
            TestContext.Out.WriteLine($"{sp.name}: ticks with a tetrapod in the air {airborne}/900, overlapping {both}; steps per leg {string.Join(", ", counts)}; head {sim.Joints[0].z:F2} m");
            Assert.AreEqual(0, both, "both tetrapods in the air at once");
            Assert.Greater(airborne, 100, "hardly stepped: the test does not bite");
            for (int l = 0; l < n; l++) if (Walking(sp, l)) Assert.GreaterOrEqual(counts[l], minSteps, "leg " + l);
            Assert.Greater(sim.Joints[0].z, 4f * sp.BodyLength * sp.scale, "it must walk");
        }

        // A weaving walk: a planted foot never slides (including a leg held back by its lift delay).
        static void FeetPlanted(CreatureSpecies sp)
        {
            var sim = Flat(sp);
            int n = sim.Legs.Length, checkedCount = 0;
            var prev = new Vector3[n]; var prevStep = new bool[n]; var prevT = new float[n];
            for (int l = 0; l < n; l++) { prev[l] = sim.Legs[l].foot; prevStep[l] = sim.Legs[l].stepping; }
            for (int t = 0; t < 900; t++)
            {
                sim.Tick(kDt, Weave(sim, sp, t));
                for (int l = 0; l < n; l++)
                {
                    if (!Walking(sp, l)) continue;
                    LegState st = sim.Legs[l];
                    // planted: not stepping, or a step still waiting for its place in the wave (stepT < 0)
                    bool plantedNow = !st.stepping || st.stepT < 0f, plantedBefore = !prevStep[l] || prevT[l] < 0f;
                    if (plantedNow && plantedBefore)
                    {
                        Assert.Less((st.foot - prev[l]).magnitude, 1e-5f * sp.scale, $"leg {l} t={sim.Time:F2} stepping={st.stepping}:{st.stepT:F2}");
                        checkedCount++;
                    }
                    prev[l] = st.foot; prevStep[l] = st.stepping; prevT[l] = st.stepT;
                }
            }
            Assert.Greater(checkedCount, 2000);
        }

        // Plan-view order of the planted feet on each side, from the head back, never changes: the long legs never tangle.
        static void LegsNeverCross(CreatureSpecies sp)
        {
            var sim = Flat(sp);
            var order = new List<int>[2] { new List<int>(), new List<int>() };
            for (int l = 0; l < sp.legs.Length; l++) if (Walking(sp, l)) order[sp.legs[l].side < 0 ? 0 : 1].Add(l);
            float sc = sp.scale, ts = Mathf.Sqrt(sc);
            Vector3 centre = new Vector3(0f, 0f, 0.25f) * sc;
            float tightest = 999f;
            for (int t = 0; t < 1500; t++)
            {
                // a weaving walk, then the tight circle
                if (t < 900) sim.Tick(kDt, Weave(sim, sp, t));
                else { float a = t / 60f * 3f / ts; sim.Tick(kDt, centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.12f * sc)); }
                for (int side = 0; side < 2; side++)
                {
                    float last = -999f;
                    foreach (int l in order[side])
                    {
                        // planted feet only: a swinging foot passes above its neighbour's
                        if (sim.Legs[l].stepping && sim.Legs[l].stepT >= 0f) continue;
                        LegSpec ls = sp.legs[l];
                        Vector3 local = Quaternion.Inverse(sim.Rotations[ls.attachJoint]) * (sim.Legs[l].foot - sim.Joints[ls.attachJoint]);
                        float yaw = Mathf.Atan2(local.x * ls.side, local.z) * Mathf.Rad2Deg;   // 0 = forward, 90 = out to the side
                        if (yaw < -90f) yaw += 360f;                                           // a hind foot just past straight back
                        Assert.Greater(yaw, last, $"t={t} side {side}: leg {l}'s foot crossed the leg in front of it");
                        if (last > -999f) tightest = Mathf.Min(tightest, yaw - last);
                        last = yaw;
                    }
                }
            }
            TestContext.Out.WriteLine($"{sp.name}: closest neighbouring feet {tightest:F1} deg apart (plan view, about their joints)");
        }

        // Pedipalps never plant, never step, and stay off the ground.
        static void PalpsNeverPlant(CreatureSpecies sp)
        {
            var sim = Flat(sp);
            float lowest = 999f;
            for (int t = 0; t < 900; t++)
            {
                sim.Tick(kDt, Weave(sim, sp, t));
                for (int l = 0; l < sp.legs.Length; l++)
                {
                    if (Walking(sp, l)) continue;
                    Assert.IsFalse(sim.Legs[l].stepping, "a feeler stepped");
                    Vector3[] p = sim.LegPoints;
                    int o = sim.LegPointOffset(l);
                    for (int k = 0; k <= sp.legs[l].segments.Length; k++) lowest = Mathf.Min(lowest, p[o + k].y);
                }
            }
            TestContext.Out.WriteLine($"{sp.name}: lowest pedipalp point {lowest * 1000f:F1} mm above the ground");
            Assert.Greater(lowest, 0f, "a pedipalp went into the ground");
        }

        // A 3 minute wander in the lab's own enclosure: every joint, foot and knee stays inside.
        static void WandersContained(CreatureSpecies sp)
        {
            Mesh mesh = CreatureMeshBuilder.Build(sp);
            try { Assert.Greater(mesh.vertexCount, 0); Assert.Greater(mesh.triangles.Length, 0); }
            finally { Object.DestroyImmediate(mesh); }
            const float dt = 1f / 30f;
            float radius = Mathf.Max(5f, 2f * sp.BodyLength * sp.scale * (1f + Mathf.Abs(sp.look.sizeJitter))); // CreatureLab.EnclosureRadius
            var area = EnclosureArea.For(Vector3.zero, Vector3.up, radius, sp, sp.scale);
            for (int seed = 1; seed <= 2; seed++)
            {
                var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, sp.scale, seed) { Bounds = area };
                var brain = new WanderBrain(area, seed);
                float far = 0f, path = 0f;
                Vector3 last = sim.Joints[0];
                for (int t = 0; t < (int)(3 * 60 / dt); t++)
                {
                    sim.Tick(dt, brain.Target(sim, dt));
                    for (int i = 0; i < sim.Joints.Length; i++)
                    {
                        float d = area.PlanarDistance(sim.Joints[i]);
                        far = Mathf.Max(far, d);
                        Assert.LessOrEqual(d, area.radius, "seed " + seed + " joint " + i + " t=" + sim.Time);
                    }
                    for (int l = 0; l < sim.Legs.Length; l++)
                    {
                        Assert.LessOrEqual(area.PlanarDistance(sim.Legs[l].foot), area.radius, "seed " + seed + " foot " + l + " t=" + sim.Time);
                        Assert.LessOrEqual(area.PlanarDistance(sim.Legs[l].elbow), area.radius, "seed " + seed + " knee " + l + " t=" + sim.Time);
                    }
                    path += (sim.Joints[0] - last).magnitude; last = sim.Joints[0];
                }
                TestContext.Out.WriteLine($"{sp.name} seed {seed}: farthest joint {far:F2} m of {area.radius:F2} m, head travelled {path:F0} m");
                Assert.Greater(path, 5f, "it hardly moved: containment test does not bite");
            }
        }

        // Cruise speed (head, m/s) over 10 s after 3 s of acceleration, straight ahead.
        static float Cruise(CreatureSpecies sp)
        {
            var sim = Flat(sp);
            for (int t = 0; t < 180; t++) sim.Tick(kDt, kFar);
            float z0 = sim.Joints[0].z;
            for (int t = 0; t < 600; t++) sim.Tick(kDt, kFar);
            return (sim.Joints[0].z - z0) / 10f;
        }

        // ================================================================ daddy-longlegs

        [Test] public void DaddyLongLegs_IsAsAuthored()
        {
            var sp = Load(kDaddyPath);
            SpiderAsAuthored(sp, 7);
            float longest = 0f;
            foreach (var l in sp.legs) if (!l.feeler) longest = Mathf.Max(longest, l.TotalLength);
            float ratio = longest / sp.BodyLength;
            TestContext.Out.WriteLine($"longest leg {longest:F3} m, body {sp.BodyLength:F3} m: {ratio:F1}x");
            Assert.That(ratio, Is.InRange(8f, 20f), "a daddy-longlegs: legs 8-20x its body");
        }

        [Test] public void DaddyLongLegs_TetrapodGait_NeverOverlaps() => TetrapodGait(Load(kDaddyPath), 6);
        [Test] public void DaddyLongLegs_FeetPlanted_DoNotMove() => FeetPlanted(Load(kDaddyPath));
        [Test] public void DaddyLongLegs_AdjacentLegsNeverCross() => LegsNeverCross(Load(kDaddyPath));
        [Test] public void DaddyLongLegs_PalpsNeverPlant() => PalpsNeverPlant(Load(kDaddyPath));
        [Test] public void DaddyLongLegs_BuildsAndWanders_Contained() => WandersContained(Load(kDaddyPath));
        [Test] public void DaddyLongLegs_TightTurn_JointRangesStayBounded() => CreatureLegTests.TightTurnJointRanges(Load(kDaddyPath));
        [Test] public void DaddyLongLegs_MaxBend_NoInvertedTriangles() => CreatureMeshTests.MaxBendNoInvertedTriangles(Load(kDaddyPath));

        // The pholcid look: through a cruise the body hangs low between the knees, which peak far above it.
        [Test] public void DaddyLongLegs_KneesHighAboveBody()
        {
            var sp = Load(kDaddyPath);
            var sim = Flat(sp);
            float sum = 0f, min = 999f; int samples = 0; float body = 0f;
            for (int t = 0; t < 900; t++)
            {
                sim.Tick(kDt, kFar);
                if (t < 120) continue;
                body = Mathf.Max(body, sim.Joints[1].y);
                for (int l = 0; l < sp.legs.Length; l++)
                {
                    if (!Walking(sp, l) || sim.Legs[l].stepping) continue;
                    float k = sim.Legs[l].elbow.y / sim.Joints[1].y;   // knee height over body height
                    sum += k; min = Mathf.Min(min, k); samples++;
                }
            }
            TestContext.Out.WriteLine($"planted knees at {sum / samples:F2} x the body's height (min {min:F2}), body {body:F3} m up");
            Assert.GreaterOrEqual(sum / samples, 1.5f, "the knees must peak well above the body");
            Assert.GreaterOrEqual(min, 1.1f, "a planted knee sank to the body's height");
        }

        // A high-knee step: the knee rises far more in the swing than the foot does.
        [Test] public void DaddyLongLegs_SwingLiftsKnee()
        {
            var sp = Load(kDaddyPath);
            var sim = Flat(sp);
            int n = sim.Legs.Length;
            var planted = new float[n]; var count = new int[n]; var peak = new float[n]; var footPeak = new float[n];
            for (int t = 0; t < 900; t++)
            {
                sim.Tick(kDt, kFar);
                if (t < 120) continue;
                for (int l = 0; l < n; l++)
                {
                    if (!Walking(sp, l)) continue;
                    LegState st = sim.Legs[l];
                    float k = st.elbow.y - st.shoulder.y;
                    if (!st.stepping) { planted[l] += k; count[l]++; }
                    else if (st.stepT > 0f) { peak[l] = Mathf.Max(peak[l], k); footPeak[l] = Mathf.Max(footPeak[l], st.foot.y); }
                }
            }
            for (int l = 0; l < n; l++)
            {
                if (!Walking(sp, l)) continue;
                float rise = peak[l] - planted[l] / count[l], femur = sp.legs[l].segments[2].length * sp.scale;
                TestContext.Out.WriteLine($"leg {l}: knee rises {rise * 1000f:F0} mm in the swing ({rise / femur:F2} x femur), foot lifts {footPeak[l] * 1000f:F0} mm");
                Assert.GreaterOrEqual(rise, 0.08f * femur, $"leg {l}: the knee must rise in the swing");
                Assert.Greater(rise, footPeak[l], $"leg {l}: the knee must rise more than the foot");
            }
        }

        // The bouncy walk: the body bobs at cruise and settles when it stops.
        [Test] public void DaddyLongLegs_BodyBobs()
        {
            var sp = Load(kDaddyPath);
            var sim = Flat(sp);
            float amp = sp.gaitBob.amplitude * sp.scale;
            float lo = 999f, hi = -999f;
            for (int t = 0; t < 600; t++)
            {
                sim.Tick(kDt, kFar);
                if (t < 180) continue;
                lo = Mathf.Min(lo, sim.BobOffset); hi = Mathf.Max(hi, sim.BobOffset);
            }
            float cruise = hi - lo;

            float rest = 0f;
            for (int t = 0; t < 240; t++)
            {
                sim.Tick(kDt, sim.Joints[0]);   // as WanderBrain pauses: the target is the head itself
                if (t >= 180) rest = Mathf.Max(rest, Mathf.Abs(sim.BobOffset));
            }
            TestContext.Out.WriteLine($"bob peak-to-peak at cruise {cruise * 1000f:F1} mm (amplitude {amp * 1000f:F1} mm), {rest * 1000f:F2} mm after 3-4 s standing");
            Assert.That(cruise, Is.InRange(0.3f * amp, 3f * amp), "the body must bob with the gait");
            Assert.Less(rest, 0.05f * amp, "the bob must settle when it stands");
        }

        // The arthropod tick (8 legs x 7 segments, palps, bob) and its bones allocate nothing after warm-up.
        [Test] public void Spiders_TickAndBones_DoNotAllocate()
        {
            GcAllocs.AssertCounterWorks();
            foreach (string path in new[] { kDaddyPath, kTarantulaPath })
            {
                var sp = Load(path);
                var sim = Flat(sp);
                var bones = new Matrix4x4[sp.BoneCount];
                for (int t = 0; t < 60; t++) sim.Tick(kDt, Weave(sim, sp, t));
                int allocs = GcAllocs.Count(() =>
                {
                    for (int t = 60; t < 360; t++) { sim.Tick(kDt, Weave(sim, sp, t)); sim.WriteBoneWorld(bones, 0); }
                });
                Assert.AreEqual(0, allocs, sp.name + ": the tick allocated");
            }
        }

        // ================================================================ tarantula

        [Test] public void Tarantula_IsAsAuthored()
        {
            var sp = Load(kTarantulaPath);
            SpiderAsAuthored(sp, 7);
            float longest = 0f, shoulder = 0f;
            foreach (var l in sp.legs) if (!l.feeler) { longest = Mathf.Max(longest, l.TotalLength); shoulder = Mathf.Max(shoulder, l.shoulderOffset.x); }
            float span = 2f * (longest + shoulder);
            TestContext.Out.WriteLine($"leg span {span:F2} m, body {sp.BodyLength:F2} m: {span / sp.BodyLength:F1}x");
            Assert.That(span / sp.BodyLength, Is.InRange(2f, 3f), "a tarantula: leg span 2-3x its body");
            Assert.IsNotNull(sp.appendages); Assert.Greater(sp.appendages.Length, 0, "chelicerae");
            Assert.Greater(sp.appendages[0].fangLength, 0f, "fangs");
            Assert.Greater(sp.look.sheen, 0f, "velvet sheen");
        }

        [Test] public void Tarantula_TetrapodGait_NeverOverlaps() => TetrapodGait(Load(kTarantulaPath), 4);
        [Test] public void Tarantula_FeetPlanted_DoNotMove() => FeetPlanted(Load(kTarantulaPath));
        [Test] public void Tarantula_AdjacentLegsNeverCross() => LegsNeverCross(Load(kTarantulaPath));
        [Test] public void Tarantula_PalpsNeverPlant() => PalpsNeverPlant(Load(kTarantulaPath));
        [Test] public void Tarantula_BuildsAndWanders_Contained() => WandersContained(Load(kTarantulaPath));
        [Test] public void Tarantula_TightTurn_JointRangesStayBounded() => CreatureLegTests.TightTurnJointRanges(Load(kTarantulaPath));
        [Test] public void Tarantula_MaxBend_NoInvertedTriangles() => CreatureMeshTests.MaxBendNoInvertedTriangles(Load(kTarantulaPath));

        [Test] public void Tarantula_SlowCruise()
        {
            var sp = Load(kTarantulaPath);
            float v = Cruise(sp);
            TestContext.Out.WriteLine($"tarantula cruise {v:F3} m/s");
            Assert.That(v, Is.InRange(0.08f, 0.25f), "a slow, deliberate walk");
        }

        // A heavy body: it rolls over its planted legs visibly, and its bob is damped (no ringing).
        [Test] public void Tarantula_HeavySway()
        {
            var sp = Load(kTarantulaPath);
            var sim = Flat(sp);
            float roll = 0f;
            for (int t = 0; t < 900; t++)
            {
                sim.Tick(kDt, kFar);
                if (t < 180) continue;
                Vector3 r = sim.Rotations[1] * Vector3.right;
                roll = Mathf.Max(roll, Mathf.Abs(Mathf.Asin(Mathf.Clamp(r.y, -1f, 1f)) * Mathf.Rad2Deg));
            }
            TestContext.Out.WriteLine($"tarantula peak roll {roll:F1} deg");
            Assert.GreaterOrEqual(roll, 1.5f, "the body must roll over its planted legs");
            Assert.GreaterOrEqual(sp.gaitBob.damping, 0.6f, "a heavy body does not bounce");
        }

        // ================================================================ beetle

        // The darkling beetle's insect legs: coxa, femur, tibia, tarsus; planted, the femur rises from the coxa
        // and the tarsus lies on the ground at a shallow angle.
        [Test] public void DesertBeetle_InsectLegs()
        {
            var sp = Load(CreatureDesertSpeciesTests.kBeetlePath);
            foreach (var l in sp.legs) Assert.AreEqual(4, l.segments != null ? l.segments.Length : 0, "coxa, femur, tibia, tarsus");
            var sim = Flat(sp);
            float worstTarsus = 0f, worstRise = 999f; int samples = 0;
            for (int t = 0; t < 600; t++)
            {
                sim.Tick(kDt, kFar);
                if (t < 60) continue;
                for (int l = 0; l < sp.legs.Length; l++)
                {
                    if (sim.Legs[l].stepping) continue;
                    Vector3[] p = sim.LegPoints;
                    int o = sim.LegPointOffset(l);
                    Vector3 tar = p[o + 4] - p[o + 3];
                    worstTarsus = Mathf.Max(worstTarsus, Vector3.Angle(tar, Vector3.ProjectOnPlane(tar, Vector3.up)));
                    worstRise = Mathf.Min(worstRise, p[o + 2].y - p[o + 1].y);
                    samples++;
                }
            }
            TestContext.Out.WriteLine($"{samples} planted samples: tarsus at most {worstTarsus:F1} deg to the ground, femur rises at least {worstRise * 1000f:F1} mm");
            Assert.LessOrEqual(worstTarsus, 35f, "the tarsus must lie at a shallow angle");
            Assert.Greater(worstRise, 0f, "the femur must rise from the coxa");
        }
    }
}
