using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Task 10: two new body plans authored as data only (no code change to the sim, legs, mesh builder or
    // renderer): a legless snake and a tripod-gait hexapod. Both run at their asset's own scale.
    public class CreatureExtensibilityTests
    {
        public const string kSnakePath = "Assets/TerrainGen/Config/Creatures/TestSnake.asset";
        public const string kHexapodPath = "Assets/TerrainGen/Config/Creatures/TestHexapod.asset";
        const float kDt = 1f / 60f;

        static CreatureSpecies Load(string path)
        {
            var sp = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(path);
            Assert.IsNotNull(sp, path);
            Assert.IsNull(sp.Validate(), path);
            return sp;
        }

        static CreatureSim Flat(CreatureSpecies sp)
            => new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, sp.scale);

        static void Walk(CreatureSim sim, int ticks, System.Action<CreatureSim> each = null)
        {
            for (int t = 0; t < ticks; t++)
            {
                sim.Tick(kDt, new Vector3(0f, 0f, 1000f));
                if (each != null) each(sim);
            }
        }

        // ---- snake ----

        [Test] public void Snake_IsLeglessAsAuthored()
        {
            var sp = Load(kSnakePath);
            Assert.AreEqual(40, sp.JointCount);
            Assert.AreEqual(0, sp.legs != null ? sp.legs.Length : 0);
            Assert.AreEqual(0.05f, sp.spacing, 1e-6f);
            // tapering: thin head, widest mid-body, a fine tail tip
            float head = sp.joints[0].halfWidth, tail = sp.joints[sp.JointCount - 1].halfWidth, max = 0f;
            for (int i = 0; i < sp.JointCount; i++)
            {
                SpineJoint j = sp.joints[i];
                max = Mathf.Max(max, j.halfWidth);
                Assert.AreEqual(0.8f * j.halfWidth, j.halfHeight, 1e-4f, "halfHeight " + i);
                Assert.AreEqual(j.halfHeight, j.clearance, 1e-4f, "clearance " + i);
                Assert.AreEqual(30f, j.bendLimitDeg, 1e-4f, "bend limit " + i);
            }
            Assert.AreEqual(0.02f, head, 1e-4f);
            Assert.AreEqual(0.035f, max, 1e-4f);
            Assert.AreEqual(0.004f, tail, 1e-4f);
        }

        // The mesh builds for a legless body, and a 5 minute wander in an 8 m (across) enclosure keeps every
        // joint inside it.
        [Test] public void Snake_BuildsAndWanders_Contained()
        {
            var sp = Load(kSnakePath);
            Mesh mesh = CreatureMeshBuilder.Build(sp);
            try
            {
                Assert.IsNotNull(mesh);
                Assert.Greater(mesh.vertexCount, 0);
                Assert.Greater(mesh.triangles.Length, 0);
            }
            finally { Object.DestroyImmediate(mesh); }

            const float dt = 1f / 30f;
            var area = EnclosureArea.For(Vector3.zero, Vector3.up, 4f * sp.scale, sp, sp.scale);
            for (int seed = 1; seed <= 2; seed++)
            {
                var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, sp.scale, seed) { Bounds = area };
                var brain = new WanderBrain(area, seed);
                float far = 0f, path = 0f;
                Vector3 last = sim.Joints[0];
                for (int t = 0; t < (int)(5 * 60 / dt); t++)
                {
                    sim.Tick(dt, brain.Target(sim, dt));
                    for (int i = 0; i < sim.Joints.Length; i++)
                    {
                        float d = area.PlanarDistance(sim.Joints[i]);
                        far = Mathf.Max(far, d);
                        Assert.LessOrEqual(d, area.radius, "seed " + seed + " joint " + i + " t=" + sim.Time);
                    }
                    path += (sim.Joints[0] - last).magnitude; last = sim.Joints[0];
                }
                TestContext.Out.WriteLine($"seed {seed}: farthest joint {far:F2} m of {area.radius:F2} m, head travelled {path:F0} m");
                Assert.Greater(path, 30f, "the snake hardly moved: containment test does not bite");
            }
        }

        // Lateral peak-to-peak of the head and a mid-body joint while heading straight for a far target. The
        // body follows the head as a drag chain, which cuts the corners of a short wave: at the brief's 0.8 Hz
        // the head swung 14 cm but mid-body only 2 cm (a stiff body with a wagging head). The asset's 40 deg at
        // 0.4 Hz (wavelength 1.5 m at 0.6 m/s) carries the wave down the body, which this test pins.
        [Test] public void Snake_Undulates()
        {
            var sp = Load(kSnakePath);
            var sim = Flat(sp);
            int mid = sp.JointCount / 2;
            float hLo = float.MaxValue, hHi = float.MinValue, mLo = float.MaxValue, mHi = float.MinValue, speed = 0f;
            int samples = 0;
            for (int t = 0; t < 900; t++)
            {
                sim.Tick(kDt, new Vector3(0f, 0f, 1000f));
                if (t < 180) continue; // up to speed, and the mid-body joint well onto the head's path
                hLo = Mathf.Min(hLo, sim.Joints[0].x); hHi = Mathf.Max(hHi, sim.Joints[0].x);
                mLo = Mathf.Min(mLo, sim.Joints[mid].x); mHi = Mathf.Max(mHi, sim.Joints[mid].x);
                speed += sim.Speed; samples++;
            }
            TestContext.Out.WriteLine($"lateral p2p head {(hHi - hLo) * 100f:F1} cm, joint {mid} {(mHi - mLo) * 100f:F1} cm; mean speed {speed / samples:F2} m/s; progress {sim.Joints[0].z:F2} m");
            Assert.Greater(hHi - hLo, 0.15f * sp.scale, "head lateral peak to peak");
            Assert.Greater(mHi - mLo, 0.10f * sp.scale, "mid-body lateral peak to peak");
            Assert.Greater(sim.Joints[0].z, 3f * sp.scale, "the snake must still make headway");
        }

        [Test] public void Snake_MaxBend_NoInvertedTriangles()
        {
            CreatureMeshTests.MaxBendNoInvertedTriangles(Load(kSnakePath));
        }

        // ---- hexapod ----

        [Test] public void Hexapod_IsTripodAsAuthored()
        {
            var sp = Load(kHexapodPath);
            Assert.AreEqual(8, sp.JointCount);
            Assert.AreEqual(0.05f, sp.spacing, 1e-6f);
            Assert.AreEqual(6, sp.legs.Length);
            foreach (var l in sp.legs)
            {
                Assert.AreEqual(0.12f, l.upperLength, 1e-6f);
                Assert.AreEqual(0.14f, l.lowerLength, 1e-6f);
                // tripods: L2 + R4 + L6 = 0, R2 + L4 + R6 = 1
                int want = ((l.attachJoint / 2) % 2 == 1) == (l.side < 0) ? 0 : 1;
                Assert.AreEqual(want, l.gaitGroup, $"leg at joint {l.attachJoint} side {l.side}");
            }
            for (int j = 2; j <= 6; j += 2)
                for (int side = -1; side <= 1; side += 2)
                    Assert.AreEqual(1, System.Array.FindAll(sp.legs, l => l.attachJoint == j && l.side == side).Length, $"joint {j} side {side}");
        }

        [Test] public void Hexapod_TripodGait_NeverOverlaps()
        {
            var sp = Load(kHexapodPath);
            var sim = Flat(sp);
            int both = 0, steppingTicks = 0;
            Walk(sim, 900, s =>
            {
                int g = -1;
                for (int l = 0; l < s.Legs.Length; l++)
                {
                    if (!s.Legs[l].stepping) continue;
                    int lg = sp.legs[l].gaitGroup;
                    if (g >= 0 && g != lg) both++;
                    g = lg;
                }
                if (g >= 0) steppingTicks++;
            });
            TestContext.Out.WriteLine($"ticks with a tripod in the air {steppingTicks}/900, overlapping {both}; steps {sim.StepsTaken}");
            Assert.AreEqual(0, both, "both tripods in the air at once");
            Assert.Greater(steppingTicks, 100, "the hexapod hardly stepped: the test does not bite");
        }

        [Test] public void Hexapod_EveryLegSteps()
        {
            var sp = Load(kHexapodPath);
            var sim = Flat(sp);
            var counts = new int[sim.Legs.Length];
            var was = new bool[sim.Legs.Length];
            Walk(sim, 900, s =>
            {
                for (int l = 0; l < counts.Length; l++)
                {
                    if (s.Legs[l].stepping && !was[l]) counts[l]++;
                    was[l] = s.Legs[l].stepping;
                }
            });
            TestContext.Out.WriteLine("steps per leg: " + string.Join(", ", counts) + $"; head {sim.Joints[0].z:F2} m in 15 s");
            for (int l = 0; l < counts.Length; l++) Assert.GreaterOrEqual(counts[l], 8, "leg " + l);
            Assert.Greater(sim.Joints[0].z, 3f * sp.scale, "the hexapod must walk");
        }

        // A weaving walk: a planted foot never slides.
        [Test] public void Hexapod_FeetPlanted_DoNotMove()
        {
            var sp = Load(kHexapodPath);
            var sim = Flat(sp);
            int n = sim.Legs.Length, checkedCount = 0;
            var prev = new Vector3[n]; var prevStep = new bool[n];
            for (int l = 0; l < n; l++) { prev[l] = sim.Legs[l].foot; prevStep[l] = sim.Legs[l].stepping; }
            for (int t = 0; t < 900; t++)
            {
                Vector3 fwd = sim.Rotations[0] * Vector3.forward;
                sim.Tick(kDt, sim.Joints[0] + Quaternion.AngleAxis(50f * Mathf.Sin(t / 50f), Vector3.up) * fwd * 2f);
                for (int l = 0; l < n; l++)
                {
                    if (!prevStep[l] && !sim.Legs[l].stepping)
                    {
                        Assert.Less((sim.Legs[l].foot - prev[l]).magnitude, 1e-5f, "leg " + l + " t=" + sim.Time);
                        checkedCount++;
                    }
                    prev[l] = sim.Legs[l].foot; prevStep[l] = sim.Legs[l].stepping;
                }
            }
            Assert.Greater(checkedCount, 2000);
        }

        [Test] public void Hexapod_TightTurn_JointRangesStayBounded()
        {
            CreatureLegTests.TightTurnJointRanges(Load(kHexapodPath));
        }

        [Test] public void Hexapod_MaxBend_NoInvertedTriangles()
        {
            CreatureMeshTests.MaxBendNoInvertedTriangles(Load(kHexapodPath));
        }
    }
}
