using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The kit's Jaw part (a hinged jaw with an idle gape) on the Crocodile: validation; standing still it gapes (opens,
    // holds, closes) with the hinge angle always inside 0 .. gapeDeg and eased (no snap); walking on it closes at once;
    // the jaw never self-intersects - opening only moves every jaw point ahead of the hinge away from the opposite jaw
    // (its closing side), and no jaw vertex goes into the ground, whole cycle, posed meshes; Froude (0.9x / 1.1x: the same
    // angle curve in time / sqrt s); no allocation while it gapes. RED: a lower jaw dropping 60 deg from a head held as
    // low as the crocodile's goes into the ground (the ground check bites).
    public class CreatureJawTests
    {
        const float kDt = 1f / 60f;

        static CreatureSpecies Croc() => CreatureKitAssets.Load("Crocodile").Compiled.Runtime;

        static CreatureSim Sim(CreatureSpecies sp, float s, int seed = 1) =>
            new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, seed);

        static JawRuntime Gaper(CreatureSim sim)
        {
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is JawRuntime j && j.Part.gapeDeg > 0f) return j;
            return null;
        }

        static Jaw SnoutPart()
        {
            foreach (CreaturePart p in CreatureKitAssets.Load("Crocodile").Compiled.Parts) if (p is Jaw j && j.gapeDeg > 0f) return j;
            return null;
        }

        static SpeciesCtx Ctx(int joints, bool soft = false) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, soft);

        static Jaw Valid() => new Jaw
        {
            id = "j", at = new PartSocket(0, 0, Vector3.zero), length = 0.2f, gapeDeg = 30f,
            keys = new[] { new LoftKey { t = 0f, halfWidth = 0.03f, halfHeight = 0.01f, squareTop = 2f, squareBottom = 2f },
                           new LoftKey { t = 1f, halfWidth = 0.02f, halfHeight = 0.008f, squareTop = 2f, squareBottom = 2f } },
        };

        [Test] public void Jaw_Validate_RejectsBadData()
        {
            Jaw ok = Valid();
            Assert.IsNull(ok.Validate(Ctx(6)));
            Assert.AreEqual(1, ok.BoneCount(Ctx(6)), "one bone: the hinge");
            Assert.IsNotNull(ok.Validate(Ctx(6, true)), "needs a spine body");
            var bad = (Jaw)ok.Clone(); bad.gapeDeg = 100f; Assert.IsNotNull(bad.Validate(Ctx(6)), "gape past 80 deg");
            bad = (Jaw)ok.Clone(); bad.openSeconds = 0f; Assert.IsNotNull(bad.Validate(Ctx(6)), "an instant snap open");
            bad = (Jaw)ok.Clone(); bad.back = 0.3f; Assert.IsNotNull(bad.Validate(Ctx(6)), "starts behind its own end");
            bad = (Jaw)ok.Clone(); bad.teethPerSide = 4; bad.teethFrom = 0.9f; bad.teethTo = 0.5f; Assert.IsNotNull(bad.Validate(Ctx(6)), "teeth range reversed");
            bad = (Jaw)ok.Clone(); bad.keys = new LoftKey[1]; Assert.IsNotNull(bad.Validate(Ctx(6)), "one key");
            bad = (Jaw)ok.Clone(); bad.at.joint = 6; Assert.IsNotNull(bad.Validate(Ctx(6)), "joint out of range");
        }

        // Stand still for a minute, then walk on: the angle trace (time normalised by sqrt k), gapes, the fastest
        // angle change (deg per normalised s), and how long after walking on it was shut.
        static List<float> Cycle(float k, out int gapes, out float maxRate, out float shutAfter, out float maxAngle)
        {
            CreatureSpecies sp = Croc();
            float s = sp.scale * k, dt = kDt * Mathf.Sqrt(k);
            var sim = Sim(sp, s);
            JawRuntime jaw = Gaper(sim);
            Assert.IsNotNull(jaw, "the crocodile's snout is a gaping Jaw");
            var trace = new List<float>();
            maxRate = 0f; maxAngle = 0f; shutAfter = float.PositiveInfinity;
            // it walks on 1 s (normalised) after its second gape is fully open: the jaw must shut on walking
            float prev = 0f, goAt = float.PositiveInfinity;
            for (int t = 0; t < (int)(96f / kDt); t++)
            {
                float time = t * kDt;
                Vector3 goal = time < goAt ? sim.Joints[0] : new Vector3(0f, 0f, 40f * s);
                if (goAt == float.PositiveInfinity && jaw.Gapes >= 2 && jaw.State == JawRuntime.Phase.Open) goAt = time + 1f;
                sim.Tick(dt, goal);
                float a = jaw.Angle;
                Assert.IsTrue(a >= 0f && a <= jaw.Part.gapeDeg, $"t={time:F2}: hinge angle {a} outside 0..{jaw.Part.gapeDeg}");
                maxRate = Mathf.Max(maxRate, Mathf.Abs(a - prev) / kDt);
                maxAngle = Mathf.Max(maxAngle, a);
                if (time >= goAt && a == 0f && shutAfter == float.PositiveInfinity) shutAfter = time - goAt;
                trace.Add(a); prev = a;
            }
            gapes = jaw.Gapes;
            Assert.Less(goAt, 90f, "a second gape within the window");
            return trace;
        }

        [TestCase(1f)] [TestCase(0.9f)] [TestCase(1.1f)]
        public void Jaw_StandingStill_Gapes_InRange_Eased_ClosesOnWalking(float k)
        {
            CreatureSpecies sp = Croc();
            Cycle(k, out int gapes, out float rate, out float shut, out float widest);
            Jaw j = SnoutPart();
            // smoothstep peaks at 1.5 x the mean rate; in time normalised by sqrt k the times are x sqrt(species scale)
            float T = Mathf.Sqrt(sp.scale);
            float bound = 1.5f * j.gapeDeg / (Mathf.Min(j.openSeconds, j.closeSeconds) * T) * 1.05f;
            TestContext.Out.WriteLine($"x{k:F1}: {gapes} gapes while resting, widest {widest:F1} deg, fastest {rate:F1} deg/s (bound {bound:F1}), shut {shut:F2} s after walking on");
            Assert.GreaterOrEqual(gapes, 2, "it gapes again and again while it rests");
            Assert.AreEqual(j.gapeDeg, widest, 1e-3f, "it opens fully");
            Assert.LessOrEqual(rate, bound, "eased open and shut (no snap)");
            Assert.LessOrEqual(shut, j.closeSeconds * T + 0.1f, "walking on, it shuts within its closing time");
        }

        [Test] public void Jaw_Size_FroudeSimilar()
        {
            List<float> a = Cycle(0.9f, out _, out _, out _, out _), b = Cycle(1.1f, out _, out _, out _, out _);
            float worst = 0f;
            for (int i = 0; i < Mathf.Min(a.Count, b.Count); i++) worst = Mathf.Max(worst, Mathf.Abs(a[i] - b[i]));
            TestContext.Out.WriteLine($"angle traces x0.9 / x1.1 in normalised time: worst difference {worst:F4} deg");
            Assert.Less(worst, 0.5f, "the same gape in Froude time");
        }

        // The jaw's posed vertices over a gape: none below the ground; each one ahead of the hinge moves only away from
        // the opposite jaw (along the closing side's normal it never comes closer than when closed). Returns the
        // deepest vertex below the ground (m, < 0 = below) and the worst approach toward the closing side (m).
        static void JawSweep(CreatureSpecies sp, out float deepest, out float approach)
        {
            float s = sp.scale;
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            Vector3[] v = mesh.vertices; var uv2 = new List<Vector4>(); mesh.GetUVs(2, uv2);
            Object.DestroyImmediate(mesh);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            var world = new Matrix4x4[sp.BoneCount];
            var sim = Sim(sp, s);
            JawRuntime jaw = Gaper(sim);
            Jaw j = jaw.Part;
            int bone = jaw.Bone, jt = j.at.joint;
            deepest = float.PositiveInfinity; approach = 0f;
            var closed = new Dictionary<int, float>();
            for (int t = 0; t < (int)(40f / kDt); t++)
            {
                sim.Tick(kDt, sim.Joints[0]);
                if (t % 6 != 0 && jaw.State != JawRuntime.Phase.Open) continue;
                sim.WriteBoneWorld(world, 0);
                Matrix4x4 skin = world[bone] * bind[bone];
                Quaternion r = sim.Rotations[jt];
                Vector3 hinge = sim.Joints[jt] + r * (j.Hinge * s), closing = r * (j.opensUp ? Vector3.down : Vector3.up), fwd = r * Vector3.forward;
                for (int i = 0; i < v.Length; i++)
                {
                    if ((int)uv2[i].x != bone) continue;
                    Vector3 p = skin.MultiplyPoint3x4(v[i]);
                    deepest = Mathf.Min(deepest, p.y);
                    if (Vector3.Dot(p - hinge, fwd) < 0.02f * s) continue;   // the root at the hinge turns in place
                    float c = Vector3.Dot(p - hinge, closing);
                    if (jaw.Angle == 0f) closed[i] = c;
                    else if (closed.TryGetValue(i, out float c0)) approach = Mathf.Max(approach, c - c0);
                }
            }
        }

        [Test] public void Jaw_Gape_NoSelfIntersection_NoGround()
        {
            CreatureSpecies sp = Croc();
            JawSweep(sp, out float deepest, out float approach);
            TestContext.Out.WriteLine($"deepest jaw vertex {deepest * 1000f:F1} mm above the ground; worst approach to the closing side {approach * 1000f:F3} mm");
            Assert.Greater(deepest, -1e-3f * sp.scale, "no jaw vertex in the ground");
            Assert.Less(approach, 1e-4f * sp.scale, "opening never moves the jaw into the opposite jaw");
        }

        // The deepest point of a jaw's loft (m) at a gape angle on a head standing on flat ground at its rest clearance.
        static float DeepestAt(Jaw j, CreatureSpecies sp, float deg)
        {
            float s = sp.scale, y0 = sp.joints[j.at.joint].clearance * s, deepest = float.PositiveInfinity;
            RigidLoft L = j.Shape();
            for (int a = 0; a <= 20; a++)
                for (int b = 0; b < 24; b++)
                    deepest = Mathf.Min(deepest, y0 + j.JointPoint(L.Surface(a / 20f, 2f * Mathf.PI * b / 24f), deg).y * s);
            return deepest;
        }

        // RED: the ground check bites - the same snout hinged as a lower jaw dropping 60 deg digs in, while the
        // crocodile's own lifting snout at its full gape stays clear.
        [Test] public void Red_LowerJaw_Dropping60_GoesIntoTheGround()
        {
            CreatureSpecies sp = Croc();
            Jaw red = (Jaw)SnoutPart().Clone();
            red.opensUp = false; red.gapeDeg = 60f;
            float bad = DeepestAt(red, sp, red.gapeDeg), good = DeepestAt(SnoutPart(), sp, SnoutPart().gapeDeg);
            TestContext.Out.WriteLine($"RED: a dropping jaw reaches {bad * 1000f:F1} mm; the lifting snout {good * 1000f:F1} mm");
            Assert.Less(bad, -1e-3f * sp.scale, "the ground check bites");
            Assert.Greater(good, -1e-3f * sp.scale, "the real gape stays clear");
        }

        [Test] public void Jaw_Gaping_AllocatesNothing()
        {
            GcAllocs.AssertCounterWorks();
            CreatureSpecies sp = Croc();
            var sim = Sim(sp, sp.scale);
            var bones = new Matrix4x4[sp.BoneCount];
            JawRuntime jaw = Gaper(sim);
            for (int t = 0; t < 120; t++) { sim.Tick(kDt, sim.Joints[0]); sim.WriteBoneWorld(bones, 0); }
            int n = GcAllocs.Count(() =>
            {
                for (int t = 0; t < 2400; t++)
                {
                    sim.Tick(kDt, t < 2000 ? sim.Joints[0] : new Vector3(0f, 0f, 50f));
                    sim.WriteBoneWorld(bones, 0);
                }
            });
            TestContext.Out.WriteLine($"{jaw.Gapes} gapes, {n} allocations");
            Assert.GreaterOrEqual(jaw.Gapes, 1, "the window covers a gape");
            Assert.AreEqual(0, n, "no allocation per frame");
        }
    }
}
