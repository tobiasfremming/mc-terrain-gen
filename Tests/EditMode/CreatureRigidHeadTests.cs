using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // A spine body's head is one rigid body (user, 2026-10-19: "the head can be deformed, and the head shape follows
    // the terrain when there are large changes, for instance when crawling out of a hole"). The first
    // rigidHeadJoints joints keep a fixed layout relative to each other - positions and frames - whatever the
    // ground, the climb or the flight does; the bend happens in the neck behind them.
    public class CreatureRigidHeadTests
    {
        const float kDt = 1f / 60f;
        static readonly Func<Vector3, Vector3> Up = _ => Vector3.up;
        const float kRelTol = 1e-4f, kAngTol = 0.5f;

        readonly List<Object> trash = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        // The head joints per shipped species (the joints up to where the body narrows into the neck).
        internal static readonly Dictionary<string, int> kHead = new Dictionary<string, int>
        {
            { "DesertMonitor", 3 }, { "KitMonitor", 3 }, { "DesertSnake", 2 }, { "DesertBeetle", 2 }, { "Tarantula", 3 },
            { "DaddyLongLegs", 2 }, { "WingedSerpent", 2 }, { "SkyDragon", 2 },
        };
        // spine bodies without a neck (auto finds none): every joint drapes on its own as before
        static readonly string[] kHeadless = { "TestHexapod", "TestSnake", "TreeOctopus", "FinnedSandEel" };

        // The head's layout in joint 0's frame (positions / sizeScale, frames relative to joint 0's), against the
        // first sample: worst relative position change and worst frame change (deg).
        internal sealed class HeadTracker
        {
            readonly int k;
            Vector3[] refPos; Quaternion[] refRot;
            public float worstRel, worstAng; public int samples; public string at = "-";
            public HeadTracker(int k) { this.k = k; }

            public void Sample(CreatureSim sim, int t)
            {
                Quaternion inv = Quaternion.Inverse(sim.Rotations[0]);
                float s = sim.SizeScale;
                if (refPos == null)
                {
                    refPos = new Vector3[k]; refRot = new Quaternion[k];
                    for (int i = 0; i < k; i++) { refPos[i] = inv * (sim.Joints[i] - sim.Joints[0]) / s; refRot[i] = inv * sim.Rotations[i]; }
                    return;
                }
                samples++;
                for (int i = 1; i < k; i++)
                {
                    Vector3 p = inv * (sim.Joints[i] - sim.Joints[0]) / s;
                    float rel = (p - refPos[i]).magnitude / refPos[i].magnitude;
                    // every pair's distance as well (the layout check above implies it; reported separately)
                    for (int j = 0; j < i; j++)
                    {
                        float d0 = (refPos[i] - refPos[j]).magnitude, d = (sim.Joints[i] - sim.Joints[j]).magnitude / s;
                        rel = Mathf.Max(rel, Mathf.Abs(d - d0) / d0);
                    }
                    if (rel > worstRel) { worstRel = rel; at = $"joint {i} t={t}"; }
                }
                for (int i = 0; i < k; i++)
                {
                    float a = Quaternion.Angle(refRot[i], inv * sim.Rotations[i]);
                    if (i > 0 && a > worstAng) worstAng = a;
                }
            }

            public override string ToString() => $"head ({k} joints): worst layout change {worstRel:E2} (at {at}), worst frame change {worstAng:F3} deg over {samples} samples";

            public void AssertRigid(string tag)
            {
                Assert.Greater(samples, 10, tag + ": sampled");
                Assert.LessOrEqual(worstRel, kRelTol, $"{tag}: the head deforms | {this}");
                Assert.LessOrEqual(worstAng, kAngTol, $"{tag}: the head's frames twist | {this}");
            }
        }

        static CreatureSpecies Monitor()
        {
            var sp = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(CreatureWorldTests.kMonitorPath);
            Assert.IsNotNull(sp);
            return sp;
        }

        static int HeadOf(CreatureSpecies sp) => kHead[sp.name];

        // ---- the data: legacy field, kit SpineBody default (auto from the profile), soft bodies never ----

        [Test] public void Data_ShippedSpecies_HeadJoints()
        {
            foreach (var kv in kHead)
            {
                CreatureSpecies sp = CreatureKitAssets.Load(kv.Key);
                Assert.IsNotNull(sp, kv.Key);
                Assert.AreEqual(kv.Value, sp.rigidHeadJoints, kv.Key);
                Assert.AreEqual(kv.Value, sp.RigidHead, kv.Key + " (as the sim reads it)");
            }
            // the legacy fixtures keep the old behaviour (LegacyLegsGoldenTests bit-identical), the octopus is soft, the eel
            // has no neck
            foreach (string name in kHeadless)
                Assert.AreEqual(0, CreatureKitAssets.Load(name).RigidHead, name);
            Assert.AreEqual(0, CreatureTestSpecies.Lizardish().RigidHead, "Lizardish");
            Assert.AreEqual(0, CreatureTestSpecies.Hexapod().RigidHead, "Hexapod");
        }

        [Test] public void Data_SpineBody_AutoHead()
        {
            // a new SpineBody's head is rigid by default (auto), from where the body first narrows
            var body = new CreatureKit.SpineBody();
            Assert.AreEqual(-1, body.rigidHeadJoints, "default auto");
            CreatureKit.SpineBody FromWidths(params float[] w)
            {
                var b = new CreatureKit.SpineBody { joints = new CreatureKit.JointDef[w.Length] };
                for (int i = 0; i < w.Length; i++) b.joints[i] = new CreatureKit.JointDef { halfWidth = w[i], halfHeight = w[i], clearance = 0.05f, bendLimitDeg = 20f };
                return b;
            }
            Assert.AreEqual(3, FromWidths(0.02f, 0.03f, 0.04f, 0.035f, 0.035f, 0.05f, 0.06f, 0.05f).RigidHeadJoints, "monitor-like: snout, skull, jaw");
            Assert.AreEqual(2, FromWidths(0.02f, 0.03f, 0.02f, 0.02f, 0.025f, 0.03f).RigidHeadJoints, "snake-like");
            Assert.AreEqual(0, FromWidths(0.02f, 0.02f, 0.021f, 0.022f, 0.023f, 0.024f).RigidHeadJoints, "a worm that only widens: no head");
            Assert.AreEqual(0, FromWidths(0.03f, 0.02f, 0.02f, 0.025f).RigidHeadJoints, "a head of one joint: nothing to hold together");
            Assert.AreEqual(0, FromWidths(0.02f, 0.02f, 0.03f, 0.04f, 0.05f, 0.04f).RigidHeadJoints, "narrowing only in the back half: no head");
            Assert.AreEqual(0, FromWidths(0.011f, 0.015f, 0.0164f, 0.0168f, 0.0169f, 0.017f, 0.01699f, 0.0168f, 0.0165f, 0.016f).RigidHeadJoints, "an eel tapering gently: no neck, no head");
            var off = FromWidths(0.02f, 0.03f, 0.04f, 0.035f); off.rigidHeadJoints = 0;
            Assert.AreEqual(0, off.RigidHeadJoints, "0 = off");
            var two = FromWidths(0.02f, 0.03f, 0.04f, 0.035f); two.rigidHeadJoints = 2;
            Assert.AreEqual(2, two.RigidHeadJoints, "explicit");
        }

        // ---- the monitor crawling into and out of a dug hole (R25's fixture), both grounds, 0.9 / 1 / 1.1 ----

        static HeadTracker AcrossHole(float centreY, float size, bool climbingGround, out CreatureSim sim, List<Object> trash)
        {
            var sp = Monitor();
            var f = ScriptableObject.CreateInstance<PlaneWithSphereHoleField>(); trash.Add(f);
            f.radius = 1.6f;
            f.center = new Vector3(0.4f, centreY * f.radius, 0f);
            float s = sp.scale * size;
            var fieldProbe = new FieldGroundProbe(f);
            var ground = CachedGroundProbe.ForCreature(fieldProbe, sp, s);
            IGroundProbe g = climbingGround ? new CompositeSurfaceProbe(ground, null, fieldProbe) : (IGroundProbe)ground;
            sim = new CreatureSim(sp, g, Up, new Vector3(0f, 0f, -12f), Vector3.forward, s);
            var head = new HeadTracker(HeadOf(sp));
            for (int t = 0; t < 2400; t++)
            {
                sim.Tick(kDt, new Vector3(0f, 0f, 1000f));
                head.Sample(sim, t);
            }
            TestContext.Out.WriteLine($"hole {centreY} size {size} climbing ground {climbingGround}: {head}; tail z {sim.Joints[sim.Joints.Length - 1].z:F1}");
            Assert.Greater(sim.Joints[sim.Joints.Length - 1].z, f.center.z + f.radius, "crossed the hole");
            return head;
        }

        [TestCase(0.9f), TestCase(1f), TestCase(1.1f)]
        public void Monitor_OutOfOverhangingHole_HeadRigid(float size) => AcrossHole(-0.3f, size, false, out _, trash).AssertRigid("overhanging hole");

        [TestCase(0.9f), TestCase(1f), TestCase(1.1f)]
        public void Monitor_OutOfBowl_HeadRigid(float size) => AcrossHole(0.3f, size, false, out _, trash).AssertRigid("bowl");

        [TestCase(0.9f), TestCase(1f), TestCase(1.1f)]
        public void Monitor_OutOfHole_ClimbingGround_HeadRigid(float size) => AcrossHole(-0.3f, size, true, out _, trash).AssertRigid("overhanging hole, climbing ground");

        // ---- a sharp rim: up a 0.5 m step (walked, not climbed: relief < half the body) and back down ----

        [TestCase(0.9f), TestCase(1f), TestCase(1.1f)]
        public void Monitor_SharpRim_UpAndDown_HeadRigid(float size)
        {
            var sp = Monitor();
            var f = ScriptableObject.CreateInstance<CliffField>(); trash.Add(f);
            f.wallZ = 0f; f.height = 0.5f;
            float s = sp.scale * size;
            var field = new FieldGroundProbe(f);
            var cache = CachedGroundProbe.ForCreature(field, sp, s);
            var sim = new CreatureSim(sp, new CompositeSurfaceProbe(cache, null, field), Up, new Vector3(0f, 0f, -8f), Vector3.forward, s);
            var head = new HeadTracker(HeadOf(sp));
            bool up = false, back = false;
            for (int t = 0; t < 3600; t++)
            {
                if (!up && sim.Joints[sim.Joints.Length - 1].z > 1f) up = true;
                Vector3 goal = up ? new Vector3(0.5f, 0f, -12f) : new Vector3(0f, 0.5f, 12f);
                if (up && sim.Joints[sim.Joints.Length - 1].z < -1f) back = true;
                sim.Tick(kDt, goal);
                head.Sample(sim, t);
            }
            TestContext.Out.WriteLine($"rim size {size}: up {up}, back down {back}; {head}");
            Assert.IsTrue(up, "walked up the step");
            Assert.IsTrue(back, "walked back down");
            head.AssertRigid("sharp rim");
        }

        // ---- onto a trunk, up, and back down to the ground (an analytic trunk: runs offline and in Unity) ----

        [TestCase(0.9f), TestCase(1f), TestCase(1.1f)]
        public void Monitor_OntoAndOffTrunk_HeadRigid(float size)
        {
            var sp = Monitor();
            float s = sp.scale * size;
            var trunk = new AnalyticSurfaces().AddCapsule(new Vector3(0f, -1f, 0f), new Vector3(0f, 13f, 0f), 1.4f);
            var sim = new CreatureSim(sp, new CompositeSurfaceProbe(new FlatGroundProbe(), trunk), Up, new Vector3(0f, 0f, -9f), Vector3.forward, s);
            Vector3 upGoal = new Vector3(0f, 4f, -1.4f), down = new Vector3(3f, 0f, -8f);
            var head = new HeadTracker(HeadOf(sp));
            bool goingDown = false, climbed = false;
            for (int t = 0; t < 5400; t++)
            {
                if (!goingDown && (sim.Joints[0] - upGoal).magnitude < 1f) goingDown = true;
                sim.Tick(kDt, goingDown ? down : upGoal);
                climbed |= sim.Climbing && sim.Steepness(0) > 0.8f;
                head.Sample(sim, t);
            }
            TestContext.Out.WriteLine($"trunk size {size}: climbed {climbed}, came down {goingDown}, head at {sim.Joints[0]}; {head}");
            Assert.IsTrue(climbed, "climbed the trunk");
            Assert.IsTrue(goingDown, "reached the goal on the trunk");
            head.AssertRigid("trunk");
        }

        // ---- a terrain cliff: up the wall and over the top edge ----

        [Test] public void Monitor_TerrainCliff_UpAndOver_HeadRigid()
        {
            var sp = Monitor();
            var f = ScriptableObject.CreateInstance<CliffField>(); trash.Add(f);
            var field = new FieldGroundProbe(f);
            var cache = CachedGroundProbe.ForCreature(field, sp, sp.scale);
            var sim = new CreatureSim(sp, new CompositeSurfaceProbe(cache, null, field), Up, new Vector3(0f, 0f, -9f), Vector3.forward, sp.scale);
            Vector3 goal = new Vector3(0f, 6f, 8f);
            var head = new HeadTracker(HeadOf(sp));
            bool arrived = false;
            for (int t = 0; t < 3600; t++)
            {
                if (!arrived && (sim.Joints[0] - goal).magnitude < 0.8f) arrived = true;
                sim.Tick(kDt, arrived ? sim.Joints[0] : goal);
                head.Sample(sim, t);
            }
            TestContext.Out.WriteLine($"terrain cliff: arrived {arrived}; {head}");
            Assert.IsTrue(arrived, "over the top");
            head.AssertRigid("terrain cliff");
        }

        // ---- the skinned head: vertices skinned only to head bones keep their layout ----

        [Test] public void Monitor_SkinnedHead_NoDeformation_OutOfHole()
        {
            var sp = Monitor();
            int k = HeadOf(sp);
            Mesh mesh = CreatureMeshBuilder.Build(sp); trash.Add(mesh);
            Vector3[] v = mesh.vertices;
            var uv2 = new List<Vector4>(); mesh.GetUVs(2, uv2);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            var headVerts = new List<int>();
            for (int i = 0; i < v.Length; i++)
            {
                Vector4 w = uv2[i];
                bool b0 = w.z <= 0f || w.x < k, b1 = w.w <= 0f || w.y < k;
                if (b0 && b1) headVerts.Add(i);
            }
            Assert.Greater(headVerts.Count, 50, "head vertices found");

            var f = ScriptableObject.CreateInstance<PlaneWithSphereHoleField>(); trash.Add(f);
            f.radius = 1.6f; f.center = new Vector3(0.4f, -0.3f * f.radius, 0f);
            var ground = CachedGroundProbe.ForCreature(new FieldGroundProbe(f), sp, sp.scale);
            var sim = new CreatureSim(sp, ground, Up, new Vector3(0f, 0f, -12f), Vector3.forward, sp.scale);
            var bones = new Matrix4x4[sp.BoneCount];
            Vector3[] first = null;
            float worst = 0f, size = 0f; string at = "-";
            for (int t = 0; t < 2400; t++)
            {
                sim.Tick(kDt, new Vector3(0f, 0f, 1000f));
                if (t % 5 != 0) continue;
                sim.WriteBoneWorld(bones, 0);
                Matrix4x4 toHead = Matrix4x4.TRS(sim.Joints[0], sim.Rotations[0], Vector3.one * sim.SizeScale).inverse;
                var cur = new Vector3[headVerts.Count];
                for (int q = 0; q < headVerts.Count; q++)
                {
                    int i = headVerts[q]; Vector4 w = uv2[i];
                    Vector3 p = w.z * (bones[(int)w.x] * bind[(int)w.x]).MultiplyPoint3x4(v[i]) + w.w * (bones[(int)w.y] * bind[(int)w.y]).MultiplyPoint3x4(v[i]);
                    cur[q] = toHead.MultiplyPoint3x4(p);
                }
                if (first == null) { first = cur; foreach (Vector3 p in cur) size = Mathf.Max(size, p.magnitude); continue; }
                for (int q = 0; q < cur.Length; q++)
                {
                    float d = (cur[q] - first[q]).magnitude / size;
                    if (d > worst) { worst = d; at = $"vertex {headVerts[q]} t={t}"; }
                }
            }
            TestContext.Out.WriteLine($"skinned head: {headVerts.Count} vertices, worst change {worst:E2} of the head size ({at})");
            Assert.LessOrEqual(worst, kRelTol, "the skinned head deforms");
        }

        // ---- every shipped species with a head: walking a weaving path over rolling ground, and the flyers in the air ----

        [TestCase("DesertSnake"), TestCase("DesertBeetle"), TestCase("Tarantula"), TestCase("DaddyLongLegs"), TestCase("WingedSerpent"), TestCase("SkyDragon"), TestCase("KitMonitor")]
        public void Species_RollingGround_HeadRigid(string name)
        {
            CreatureSpecies sp = CreatureKitAssets.Load(name);
            Assert.IsNotNull(sp, name);
            float s = sp.scale;
            var f = ScriptableObject.CreateInstance<RollingField>(); trash.Add(f);
            f.amplitude = 0.15f * s; f.k = 3f / s;
            var sim = new CreatureSim(sp, CachedGroundProbe.ForCreature(new FieldGroundProbe(f), sp, s), Up, Vector3.zero, Vector3.forward, s);
            var head = new HeadTracker(kHead[name]);
            for (int t = 0; t < 1200; t++)
            {
                float tt = t / 60f;
                sim.Tick(kDt, new Vector3(2f * Mathf.Sin(0.6f * tt), 0f, 2f * Mathf.Cos(0.4f * tt) + 1f) * s);
                head.Sample(sim, t);
            }
            TestContext.Out.WriteLine($"{name} rolling ground: {head}");
            head.AssertRigid(name);
        }

        [TestCase("WingedSerpent"), TestCase("SkyDragon")]
        public void Flyer_LaunchFlyLand_HeadRigid(string name)
        {
            CreatureSpecies sp = CreatureKitAssets.Load(name);
            float s = sp.scale;
            var sim = new CreatureSim(sp, new FlatGroundProbe(), Up, Vector3.zero, Vector3.forward, s);
            var head = new HeadTracker(kHead[name]);
            Vector3 ahead = new Vector3(0f, 0f, 100f) * s;
            int tick = 0;
            for (int t = 0; t < 60; t++, tick++) { sim.Tick(kDt, ahead); head.Sample(sim, tick); }
            Assert.IsTrue(sim.Flight.Launch(5f * s), name + " launches");
            bool flew = false;
            for (int t = 0; t < 900; t++, tick++)
            {
                if (t == 300 && sim.Flight.Twist != null)
                    sim.Flight.Twist.Begin(TwistManoeuvre.BarrelRoll, sim.Flight.Model.HorizontalSpeed, sim.Flight.Model.Forward, Vector3.up);
                Vector3 dir = Quaternion.AngleAxis(t < 200 ? 0f : 0.3f * (t - 200), Vector3.up) * Vector3.forward;
                sim.Tick(kDt, sim.Joints[0] + dir * (5f * s));
                flew |= sim.Mode == FlightMode.Air;
                head.Sample(sim, tick);
            }
            TestContext.Out.WriteLine($"{name} flight: flew {flew}, mode {sim.Mode}; {head}");
            Assert.IsTrue(flew, "reached the air");
            head.AssertRigid(name + " flight");
        }
    }
}
