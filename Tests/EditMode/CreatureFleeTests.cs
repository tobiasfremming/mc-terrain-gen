using System;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The kit's Flee behaviour (T5.2) on the FinnedSandEel in its lab enclosure: a threat inside the alarm radius
    // makes the ModularBrain switch to Flee, the eel ends up beyond the radius and every joint stays inside the
    // enclosure, also when the threat corners it against the wall; it calms down and wanders again once the threat
    // is gone; the radius follows the size. RED: a brain that seeks the threat fails the same check.
    public class CreatureFleeTests
    {
        const float kDt = 1f / 30f;

        [TearDown] public void ClearThreats() => CreatureThreats.Clear();

        static CreatureSpecies Eel() => CreatureKitAssets.Load("FinnedSandEel");

        static Flee FleeOf(CreatureSpecies sp)
        {
            foreach (CreatureModule m in KitView.Modules(sp.Compiled.Runtime)) if (m is Flee f) return f;
            return null;
        }

        // Runs `seconds` with a fixed threat; null when the eel ends beyond the alarm radius and never leaves the
        // enclosure, else the problem.
        static string FleeProblem(CreatureSpecies sp, float s, Vector3 start, Vector3 forward, Vector3 threat, Func<CreatureSim, float, Vector3> brain, float seconds, out float endDistance)
        {
            EnclosureArea area = KitChecks.LabArea(sp);
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, start, forward, s, 3) { Bounds = area };
            int slot = CreatureThreats.Acquire();
            CreatureThreats.Set(slot, threat);
            float r = FleeOf(sp).radius * s;
            endDistance = 0f;
            for (int t = 0; t < (int)(seconds / kDt); t++)
            {
                sim.Tick(kDt, brain(sim, kDt));
                for (int i = 0; i < sim.Joints.Length; i++)
                    if (area.PlanarDistance(sim.Joints[i]) > area.radius) return $"joint {i} left the enclosure at t={sim.Time:F1}";
            }
            Vector3 d = sim.Joints[0] - threat; d.y = 0f;
            endDistance = d.magnitude;
            TestContext.WriteLine($"x{s:F2}: head {endDistance:F2} m from the threat (alarm radius {r:F2} m, enclosure {area.radius:F2} m)");
            CreatureThreats.Release(slot);
            return endDistance > r ? null : $"still {endDistance:F2} m from the threat (alarm radius {r:F2} m)";
        }

        static Func<CreatureSim, float, Vector3> BrainOf(CreatureSpecies sp, int seed)
        {
            ICreatureBrain b = BrainFactory.Create(sp, KitChecks.LabArea(sp), seed);
            Assert.IsTrue(b is ModularBrain, "Flee is not a shipped brain's behaviour: the eel gets a ModularBrain");
            return b.Target;
        }

        [TestCase(0.9f)] [TestCase(1.1f)]
        public void Flee_MovesAway_StaysContained(float jitter)
        {
            CreatureSpecies sp = Eel();
            float s = sp.scale * jitter;
            // a threat right behind the head, in the middle of the enclosure
            Vector3 threat = new Vector3(0f, 0f, -0.3f * s);
            Assert.IsNull(FleeProblem(sp, s, Vector3.zero, Vector3.forward, threat, BrainOf(sp, 1), 12f, out _));
        }

        // Cornered: the eel starts near the wall facing it, the threat between it and the centre. It slides along the
        // wall and gets away without a joint leaving the enclosure.
        [Test] public void Flee_Cornered_SlidesAlongTheWall()
        {
            CreatureSpecies sp = Eel();
            float s = sp.scale;
            EnclosureArea area = KitChecks.LabArea(sp);
            Vector3 start = new Vector3(0f, 0f, 0.8f * area.safeRadius);
            Vector3 threat = start - new Vector3(0f, 0f, 0.4f * s);
            Assert.IsNull(FleeProblem(sp, s, start, Vector3.forward, threat, BrainOf(sp, 2), 15f, out _));
        }

        // RED: a brain that heads for the threat fails the check.
        [Test] public void Red_Flee_SeekingTheThreat_Fails()
        {
            CreatureSpecies sp = Eel();
            float s = sp.scale;
            Vector3 threat = new Vector3(0f, 0f, 1.2f * s);
            string p = FleeProblem(sp, s, Vector3.zero, Vector3.forward, threat, (sim, dt) => threat, 8f, out _);
            TestContext.WriteLine(p);
            Assert.IsNotNull(p);
        }

        // Without a threat Flee has no utility and the brain wanders; a threat switches it to Flee, and after the threat
        // is gone it calms down and wanders again.
        [Test] public void Flee_SwitchesInAndCalmsDown()
        {
            CreatureSpecies sp = Eel();
            float s = sp.scale;
            EnclosureArea area = KitChecks.LabArea(sp);
            var brain = (ModularBrain)BrainFactory.Create(sp, area, 4);
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, 4) { Bounds = area };
            int flee = -1;
            for (int i = 0; i < brain.Behaviours.Count; i++) if (brain.Behaviours[i] is FleeBehaviour) flee = i;
            Assert.GreaterOrEqual(flee, 0, "the eel's brain has Flee");
            for (int t = 0; t < 60; t++) sim.Tick(kDt, brain.Target(sim, kDt));
            Assert.AreNotEqual(flee, brain.Current, "no threat: it wanders");
            int slot = CreatureThreats.Acquire();
            CreatureThreats.Set(slot, sim.Joints[0] + new Vector3(0.2f * s, 0f, 0f));
            bool fled = false;
            for (int t = 0; t < 150; t++) { sim.Tick(kDt, brain.Target(sim, kDt)); fled |= brain.Current == flee; }
            Assert.IsTrue(fled, "a threat at the head switches it to Flee (after the minimum hold)");
            CreatureThreats.Release(slot);
            bool calmed = false;
            for (int t = 0; t < 300; t++) { sim.Tick(kDt, brain.Target(sim, kDt)); if (brain.Current != flee) calmed = true; }
            Assert.IsTrue(calmed, "it calms down and wanders again");
        }

        [Test] public void Flee_Radius_FollowsSize()
        {
            CreatureSpecies sp = Eel();
            Flee f = FleeOf(sp);
            foreach (float s in new[] { 0.9f, 1.8f })
            {
                EnclosureArea area = KitChecks.LabArea(sp);
                var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, 1) { Bounds = area };
                var b = new FleeBehaviour(f);
                CreatureThreats.Clear();
                int slot = CreatureThreats.Acquire();
                CreatureThreats.Set(slot, new Vector3(0.95f * f.radius * s, 0f, 0f));
                Assert.Greater(b.Utility(new BrainCtx(sim, area, 0f)), 0f, $"x{s}: inside the radius x s");
                CreatureThreats.Set(slot, new Vector3(1.05f * f.radius * s, 0f, 0f));
                Assert.AreEqual(FleeBehaviour.Calm, new FleeBehaviour(f).Utility(new BrainCtx(sim, area, 0f)), $"x{s}: outside it");
            }
        }

        [Test] public void Flee_Validate()
        {
            var ok = new Flee();
            var walk = new SpeciesCtx(new CreaturePart[0], new CreatureModule[] { new Walk(), ok }, 10, false);
            Assert.IsNull(ok.Validate(walk));
            Assert.IsNotNull(ok.Validate(new SpeciesCtx(new CreaturePart[0], new CreatureModule[] { ok }, 10, false)), "needs Walk");
            var bad = new Flee { radius = 0f };
            Assert.IsNotNull(bad.Validate(walk), "no radius");
        }
    }
}
