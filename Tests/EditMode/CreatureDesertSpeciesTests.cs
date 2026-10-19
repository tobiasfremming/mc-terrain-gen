using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TerrainGen.Tests
{
    // The lab species grown out of the Task 10 extensibility proof (data only, like TestSnake/TestHexapod):
    // a sand viper and a darkling beetle. Everything runs at the asset's own scale, on flat ground.
    public class CreatureDesertSpeciesTests
    {
        public const string kSnakePath = "Assets/TerrainGen/Config/Creatures/DesertSnake.asset";
        public const string kBeetlePath = "Assets/TerrainGen/Config/Creatures/DesertBeetle.asset";
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

        static readonly Vector3 kFar = new Vector3(0f, 0f, 1000f);

        // A 3 minute wander in the lab's own enclosure for the species: every joint (and foot) stays inside.
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
                        Assert.LessOrEqual(area.PlanarDistance(sim.Legs[l].foot), area.radius, "seed " + seed + " foot " + l + " t=" + sim.Time);
                    path += (sim.Joints[0] - last).magnitude; last = sim.Joints[0];
                }
                TestContext.Out.WriteLine($"{sp.name} seed {seed}: farthest joint {far:F2} m of {area.radius:F2} m, head travelled {path:F0} m");
                Assert.Greater(path, 10f * sp.BodyLength * sp.scale, "it hardly moved: containment test does not bite");
            }
        }

        // ================================================================ snake

        // A viper's outline: a wedge head clearly wider than the neck behind it, the girth in the middle of
        // the body, a short tail ending in a fine tip. The head is one rigid piece.
        [Test] public void DesertSnake_ViperProfile()
        {
            var sp = Load(kSnakePath);
            Assert.AreEqual(0, sp.legs != null ? sp.legs.Length : 0);
            int n = sp.JointCount;
            Assert.IsTrue(sp.joints[1].rigid, "the head (joints 0-1) is rigid");
            float neck = float.MaxValue;
            for (int i = 2; i <= 5; i++) neck = Mathf.Min(neck, sp.joints[i].halfWidth);
            float jaw = Mathf.Max(sp.joints[0].halfWidth, sp.joints[1].halfWidth);
            Assert.GreaterOrEqual(jaw, 1.5f * neck, "jaw vs neck");
            Assert.Less(sp.joints[0].halfWidth, sp.joints[1].halfWidth, "the head narrows toward the snout");
            int widest = 0;
            for (int i = 0; i < n; i++) if (sp.joints[i].halfWidth > sp.joints[widest].halfWidth) widest = i;
            Assert.That(widest, Is.InRange(n / 3, 2 * n / 3), "girth mid-body");
            Assert.LessOrEqual(sp.joints[n - 1].halfWidth, 0.2f * sp.joints[widest].halfWidth, "fine tail tip");
            for (int i = 0; i < n; i++) Assert.Less(sp.joints[i].halfHeight, sp.joints[i].halfWidth, "flatter than wide at " + i);
        }

        [Test] public void DesertSnake_BuildsAndWanders_Contained() => WandersContained(Load(kSnakePath));

        // Lateral undulation must travel down the whole body, not just wag the head (ruling R27: the drag
        // chain cuts the corners of short waves). Peak-to-peak sideways sway of the head, mid-body and the
        // three-quarter joint while heading for a far target, in body lengths.
        [Test] public void DesertSnake_Undulates_AlongTheBody()
        {
            var sp = Load(kSnakePath);
            var sim = Flat(sp);
            int n = sp.JointCount;
            int[] at = { 0, n / 2, 3 * n / 4 };
            var lo = new float[3]; var hi = new float[3];
            for (int k = 0; k < 3; k++) { lo[k] = float.MaxValue; hi[k] = float.MinValue; }
            float speed = 0f; int samples = 0;
            int warm = (int)(4f * Mathf.Sqrt(sp.scale) / kDt);
            for (int t = 0; t < warm + 900; t++)
            {
                sim.Tick(kDt, kFar);
                if (t < warm) continue; // up to speed, and the whole body on the head's path
                for (int k = 0; k < 3; k++) { lo[k] = Mathf.Min(lo[k], sim.Joints[at[k]].x); hi[k] = Mathf.Max(hi[k], sim.Joints[at[k]].x); }
                speed += sim.Speed; samples++;
            }
            float len = sp.BodyLength * sp.scale;
            float head = (hi[0] - lo[0]) / len, mid = (hi[1] - lo[1]) / len, rear = (hi[2] - lo[2]) / len;
            TestContext.Out.WriteLine($"lateral p2p / body length: head {head:F3}, joint {at[1]} {mid:F3}, joint {at[2]} {rear:F3}; mean speed {speed / samples:F2} m/s");
            Assert.Greater(head, 0.15f, "head sway");
            Assert.Greater(mid, 0.10f, "mid-body sway");
            Assert.Greater(rear, 0.07f, "rear sway: the wave must reach the tail");
            Assert.Greater(mid, 0.5f * head, "the wave dies out down the body");
            Assert.Greater(speed / samples, 0.4f * Mathf.Sqrt(sp.scale), "the snake must still make headway");
        }

        // The wave stays within the bend limits it was tuned for: no joint is held at its cone while cruising
        // straight (a pinned cone flattens the S into kinks).
        [Test] public void DesertSnake_CruiseBendsStayInsideTheCone()
        {
            var sp = Load(kSnakePath);
            var sim = Flat(sp);
            int n = sp.JointCount, pinned = 0, checks = 0;
            float worst = 0f;
            for (int t = 0; t < 900; t++)
            {
                sim.Tick(kDt, kFar);
                if (t < 300) continue;
                for (int i = 2; i < n - 1; i++)
                {
                    if (sp.joints[i].rigid) continue;
                    float b = Vector3.Angle(sim.Joints[i - 1] - sim.Joints[i], sim.Joints[i] - sim.Joints[i + 1]);
                    worst = Mathf.Max(worst, b);
                    if (b > sp.joints[i].bendLimitDeg - 0.5f) pinned++;
                    checks++;
                }
            }
            TestContext.Out.WriteLine($"cruise bend max {worst:F1} deg, pinned {pinned}/{checks}");
            Assert.AreEqual(0, pinned, "joints pinned at their bend limit while cruising straight");
        }

        [Test] public void DesertSnake_MaxBend_NoInvertedTriangles() => CreatureMeshTests.MaxBendNoInvertedTriangles(Load(kSnakePath));

        // ================================================================ beetle

        [Test] public void DesertBeetle_IsTripodAsAuthored()
        {
            var sp = Load(kBeetlePath);
            Assert.AreEqual(6, sp.legs.Length);
            // three pairs, front to back; tripods = front and hind of one side + middle of the other
            var joints = new System.Collections.Generic.List<int>();
            foreach (var l in sp.legs) if (!joints.Contains(l.attachJoint)) joints.Add(l.attachJoint);
            joints.Sort();
            Assert.AreEqual(3, joints.Count, "three leg pairs");
            foreach (var l in sp.legs)
            {
                int pair = joints.IndexOf(l.attachJoint);
                int want = (pair % 2 == 0) == (l.side < 0) ? 0 : 1;
                Assert.AreEqual(want, l.gaitGroup, $"leg at joint {l.attachJoint} side {l.side}");
                Assert.AreEqual(1, System.Array.FindAll(sp.legs, m => m.attachJoint == l.attachJoint && m.side == l.side).Length);
            }
        }

        [Test] public void DesertBeetle_TripodGait_NeverOverlaps()
        {
            var sp = Load(kBeetlePath);
            var sim = Flat(sp);
            int both = 0, steppingTicks = 0;
            var counts = new int[sim.Legs.Length]; var was = new bool[sim.Legs.Length];
            for (int t = 0; t < 900; t++)
            {
                sim.Tick(kDt, kFar);
                int g = -1;
                for (int l = 0; l < sim.Legs.Length; l++)
                {
                    if (sim.Legs[l].stepping && !was[l]) counts[l]++;
                    was[l] = sim.Legs[l].stepping;
                    if (!sim.Legs[l].stepping) continue;
                    int lg = sp.legs[l].gaitGroup;
                    if (g >= 0 && g != lg) both++;
                    g = lg;
                }
                if (g >= 0) steppingTicks++;
            }
            TestContext.Out.WriteLine($"ticks with a tripod in the air {steppingTicks}/900, overlapping {both}; steps per leg {string.Join(", ", counts)}; head {sim.Joints[0].z:F2} m");
            Assert.AreEqual(0, both, "both tripods in the air at once");
            Assert.Greater(steppingTicks, 100, "the beetle hardly stepped: the test does not bite");
            for (int l = 0; l < counts.Length; l++) Assert.GreaterOrEqual(counts[l], 8, "leg " + l);
            Assert.Greater(sim.Joints[0].z, 4f * sp.BodyLength * sp.scale, "the beetle must walk");
        }

        // A weaving walk: a planted foot never slides.
        [Test] public void DesertBeetle_FeetPlanted_DoNotMove()
        {
            var sp = Load(kBeetlePath);
            var sim = Flat(sp);
            int n = sim.Legs.Length, checkedCount = 0;
            var prev = new Vector3[n]; var prevStep = new bool[n];
            for (int l = 0; l < n; l++) { prev[l] = sim.Legs[l].foot; prevStep[l] = sim.Legs[l].stepping; }
            for (int t = 0; t < 900; t++)
            {
                Vector3 fwd = sim.Rotations[0] * Vector3.forward;
                sim.Tick(kDt, sim.Joints[0] + Quaternion.AngleAxis(50f * Mathf.Sin(t / 50f), Vector3.up) * fwd * (2f * sp.scale));
                for (int l = 0; l < n; l++)
                {
                    if (!prevStep[l] && !sim.Legs[l].stepping)
                    {
                        Assert.Less((sim.Legs[l].foot - prev[l]).magnitude, 1e-5f * sp.scale, "leg " + l + " t=" + sim.Time);
                        checkedCount++;
                    }
                    prev[l] = sim.Legs[l].foot; prevStep[l] = sim.Legs[l].stepping;
                }
            }
            Assert.Greater(checkedCount, 2000);
        }

        // The Task 10 hexapod read as a box on stilts: body high, lower legs near vertical. The beetle sprawls:
        // through a cruise every planted foot stays well out to the side of its shoulder, the knee rides above
        // the shoulder, the lower leg leans out, and the belly is carried low.
        [Test] public void DesertBeetle_LegsSplayed_BodyLow()
        {
            var sp = Load(kBeetlePath);
            var sim = Flat(sp);
            float minSide = float.MaxValue, sumSide = 0f, minLean = float.MaxValue, sumLean = 0f, minKnee = float.MaxValue;
            int samples = 0;
            for (int t = 0; t < 900; t++)
            {
                sim.Tick(kDt, kFar);
                if (t < 120) continue;
                for (int l = 0; l < sim.Legs.Length; l++)
                {
                    LegState st = sim.Legs[l];
                    if (st.stepping) continue;
                    LegSpec ls = sp.legs[l];
                    float len = ls.TotalLength * sim.SizeScale;
                    Vector3 right = sim.Rotations[ls.attachJoint] * Vector3.right * ls.side;
                    float side = Vector3.Dot(st.foot - st.shoulder, right) / len;
                    float lean = Vector3.Angle(st.elbow - st.ankle, Vector3.up);   // lower leg from vertical
                    float knee = (st.elbow.y - st.shoulder.y) / len;
                    minSide = Mathf.Min(minSide, side); sumSide += side;
                    minLean = Mathf.Min(minLean, lean); sumLean += lean;
                    minKnee = Mathf.Min(minKnee, knee);
                    samples++;
                }
            }
            float maxClear = 0f, maxBody = 0f;
            foreach (var j in sp.joints) { maxClear = Mathf.Max(maxClear, j.clearance - j.halfHeight); maxBody = Mathf.Max(maxBody, j.clearance + j.halfHeight); }
            float leg = sp.legs[0].TotalLength;
            TestContext.Out.WriteLine($"planted samples {samples}: foot out to the side min {minSide:F2} mean {sumSide / samples:F2} x leg; " +
                                      $"lower-leg lean from vertical min {minLean:F0} mean {sumLean / samples:F0} deg; knee above shoulder min {minKnee:F2} x leg; " +
                                      $"belly gap {maxClear / leg:F2}, back height {maxBody / leg:F2} x leg");
            Assert.Greater(samples, 1000);
            Assert.GreaterOrEqual(minSide, 0.45f, "a planted foot tucked in under the body");
            Assert.GreaterOrEqual(sumSide / samples, 0.6f, "feet splayed out to the side");
            Assert.GreaterOrEqual(sumLean / samples, 35f, "lower legs stand near vertical (stilts)");
            Assert.GreaterOrEqual(minKnee, 0f, "the knee dropped below the shoulder");
            Assert.LessOrEqual(maxClear, 0.12f * leg, "belly carried high");
        }

        [Test] public void DesertBeetle_BuildsAndWanders_Contained() => WandersContained(Load(kBeetlePath));

        [Test] public void DesertBeetle_TightTurn_JointRangesStayBounded() => CreatureLegTests.TightTurnJointRanges(Load(kBeetlePath));

        [Test] public void DesertBeetle_MaxBend_NoInvertedTriangles() => CreatureMeshTests.MaxBendNoInvertedTriangles(Load(kBeetlePath));
    }
}
