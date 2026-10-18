using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class CreatureContainmentTests
    {
        const float Radius = 8f;
        const float Dt = 1f / 30f;

        CreatureSpecies sp;
        DensityField field;

        [TearDown]
        public void TearDown()
        {
            if (sp != null) Object.DestroyImmediate(sp);
            if (field != null) Object.DestroyImmediate(field);
        }

        CreatureSim Make(bool sphere, int seed, out EnclosureArea area)
        {
            sp = CreatureTestSpecies.Lizardish();
            System.Func<Vector3, Vector3> up = _ => Vector3.up;
            if (sphere)
            {
                var s = ScriptableObject.CreateInstance<SphereField>();
                s.center = new Vector3(0f, -500f, 0f); s.radius = 500f;
                field = s;
                up = p => (p - s.center).normalized;
            }
            else field = ScriptableObject.CreateInstance<PlaneField>();
            area = EnclosureArea.For(Vector3.zero, Vector3.up, Radius, sp, 1f);
            var sim = new CreatureSim(sp, new FieldGroundProbe(field), up, Vector3.zero, Vector3.forward, 1f, seed);
            sim.Bounds = area;
            return sim;
        }

        static void AssertInside(CreatureSim sim, EnclosureArea area, string tag)
        {
            for (int i = 0; i < sim.Joints.Length; i++)
                Assert.LessOrEqual(area.PlanarDistance(sim.Joints[i]), area.radius, tag + " joint " + i + " t=" + sim.Time);
            for (int l = 0; l < sim.Legs.Length; l++)
                Assert.LessOrEqual(area.PlanarDistance(sim.Legs[l].foot), area.radius, tag + " foot " + l + " t=" + sim.Time);
        }

        [Test] public void Containment_LongWander_AllJointsAndFeetInside()
        {
            for (int pass = 0; pass < 2; pass++)
                for (int seed = 1; seed <= 5; seed++)
                {
                    Object.DestroyImmediate(sp); Object.DestroyImmediate(field);
                    var sim = Make(pass == 1, seed, out var area);
                    var brain = new WanderBrain(area, seed);
                    int ticks = (int)(20 * 60 / Dt);
                    for (int t = 0; t < ticks; t++)
                    {
                        sim.Tick(Dt, brain.Target(sim, Dt));
                        AssertInside(sim, area, "seed " + seed + " sphere " + pass);
                    }
                }
        }

        [Test] public void Containment_TargetOutside_HeadStaysInside()
        {
            var sim = Make(false, 1, out var area);
            var target = new Vector3(30f, 0f, 0f);
            int ticks = (int)(60f / Dt);
            for (int t = 0; t < ticks; t++)
            {
                sim.Tick(Dt, target);
                AssertInside(sim, area, "outside");
            }
        }

        static Vector3 RunHead(int seed, float seconds)
        {
            var sp = CreatureTestSpecies.Lizardish();
            var field = ScriptableObject.CreateInstance<PlaneField>();
            try
            {
                var area = EnclosureArea.For(Vector3.zero, Vector3.up, Radius, sp, 1f);
                var sim = new CreatureSim(sp, new FieldGroundProbe(field), _ => Vector3.up, Vector3.zero, Vector3.forward, 1f, seed);
                sim.Bounds = area;
                var brain = new WanderBrain(area, seed);
                int ticks = (int)(seconds / Dt);
                for (int t = 0; t < ticks; t++) sim.Tick(Dt, brain.Target(sim, Dt));
                return sim.Joints[0];
            }
            finally { Object.DestroyImmediate(sp); Object.DestroyImmediate(field); }
        }

        [Test] public void Wander_IsDeterministicPerSeed()
        {
            Assert.AreEqual(RunHead(3, 120f), RunHead(3, 120f));
        }

        [Test] public void Wander_CoversEnclosure()
        {
            var sim = Make(false, 7, out var area);
            var brain = new WanderBrain(area, 7);
            var seen = new bool[8];
            int ticks = (int)(600f / Dt);
            for (int t = 0; t < ticks; t++)
            {
                sim.Tick(Dt, brain.Target(sim, Dt));
                Vector3 p = sim.Joints[0];
                float a = Mathf.Atan2(p.z, p.x);
                seen[Mathf.Clamp((int)((a + Mathf.PI) / (2f * Mathf.PI) * 8f), 0, 7)] = true;
            }
            int count = 0; foreach (bool b in seen) if (b) count++;
            Assert.GreaterOrEqual(count, 6, "sectors visited");
        }

        // R21: the shipped monitor at its species scale (4x) in an enclosure sized by the lab's rule, on the
        // flat plane: 10 minutes of wander, every joint and foot inside the curb.
        [Test] public void Containment_ScaledMonitor_LabRadius_AllInside()
        {
            var monitor = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureSpecies>(CreatureWorldTests.kMonitorPath);
            Assert.IsNotNull(monitor);
            Assert.Greater(monitor.scale, 1f, "the monitor ships scaled up (R21)");
            float maxScale = CreatureLab.MaxScale(monitor);
            float radius = CreatureLab.EnclosureRadius(monitor, maxScale);
            Assert.GreaterOrEqual(radius, 2f * monitor.BodyLength * maxScale - 1e-4f);
            for (int seed = 1; seed <= 3; seed++)
            {
                var area = EnclosureArea.For(Vector3.zero, Vector3.up, radius, monitor, maxScale);
                var sim = new CreatureSim(monitor, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, maxScale, seed) { Bounds = area };
                var brain = new WanderBrain(area, seed);
                float far = 0f;
                for (int t = 0; t < (int)(10 * 60 / Dt); t++)
                {
                    sim.Tick(Dt, brain.Target(sim, Dt));
                    AssertInside(sim, area, "scaled monitor seed " + seed);
                    far = Mathf.Max(far, area.PlanarDistance(sim.Joints[0]));
                }
                Assert.Greater(far, 0.5f * area.safeRadius, "the wander never went far: test does not bite");
            }
        }

        // R24: a tree trunk inside the enclosure. The wander never picks a goal in its keep-out disc and the head
        // steers around it, so over 10 minutes no joint or foot of the 4x monitor enters the trunk.
        [Test] public void Wander_AvoidsObstacle_NoJointOrFootInside()
        {
            var monitor = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureSpecies>(CreatureWorldTests.kMonitorPath);
            Assert.IsNotNull(monitor);
            float maxScale = CreatureLab.MaxScale(monitor);
            float radius = CreatureLab.EnclosureRadius(monitor, maxScale);
            var trunk = new Vector3(0.45f * radius, 0f, 0f);
            const float trunkR = 1.2f;
            int passes = 0;
            for (int seed = 1; seed <= 3; seed++)
            {
                var area = EnclosureArea.For(Vector3.zero, Vector3.up, radius, monitor, maxScale);
                area.obstacleCenters = new[] { trunk }; area.obstacleRadii = new[] { trunkR };
                area.keepOut = EnclosureArea.KeepOutFor(monitor, maxScale);
                var sim = new CreatureSim(monitor, new FlatGroundProbe(), _ => Vector3.up, new Vector3(-0.3f * radius, 0f, 0f), Vector3.right, maxScale, seed) { Bounds = area };
                var brain = new WanderBrain(area, seed);
                float closest = float.MaxValue;
                for (int t = 0; t < (int)(10 * 60 / Dt); t++)
                {
                    sim.Tick(Dt, brain.Target(sim, Dt));
                    AssertInside(sim, area, "obstacle seed " + seed);
                    for (int i = 0; i < sim.Joints.Length; i++)
                    {
                        float d = Planar(sim.Joints[i] - trunk);
                        closest = Mathf.Min(closest, d);
                        Assert.Greater(d, trunkR, $"seed {seed} joint {i} inside the trunk t={sim.Time}");
                    }
                    for (int l = 0; l < sim.Legs.Length; l++)
                        Assert.Greater(Planar(sim.Legs[l].foot - trunk), trunkR, $"seed {seed} foot {l} inside the trunk t={sim.Time}");
                    if (Planar(sim.Joints[0] - trunk) > 0f && sim.Joints[0].x > trunk.x + trunkR) passes |= 1 << seed; // went past it
                }
                TestContext.Out.WriteLine($"seed {seed}: closest joint to trunk axis {closest:F2} m (trunk {trunkR} m, keep-out {area.keepOut:F2} m)");
            }
            Assert.AreNotEqual(0, passes, "the monitor never got past the tree: test does not bite");
        }

        [Test] public void RandomSafePoint_NeverInsideObstacle()
        {
            var monitor = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureSpecies>(CreatureWorldTests.kMonitorPath);
            float maxScale = CreatureLab.MaxScale(monitor);
            var area = EnclosureArea.For(Vector3.zero, Vector3.up, CreatureLab.EnclosureRadius(monitor, maxScale), monitor, maxScale);
            area.obstacleCenters = new[] { new Vector3(4f, 0f, 1f) }; area.obstacleRadii = new[] { 1.2f };
            area.keepOut = EnclosureArea.KeepOutFor(monitor, maxScale);
            uint rng = 12345u;
            for (int i = 0; i < 10000; i++)
            {
                Vector3 p = area.RandomSafePoint(ref rng);
                Assert.Less(area.ObstacleAt(p), 0, "point " + p + " inside the keep-out disc");
                Assert.LessOrEqual(area.PlanarDistance(p), area.safeRadius + 1e-4f);
            }
        }

        static float Planar(Vector3 v) => new Vector2(v.x, v.z).magnitude;
    }
}
