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
    }
}
