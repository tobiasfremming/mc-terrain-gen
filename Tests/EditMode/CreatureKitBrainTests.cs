using System;
using System.Collections.Generic;
using CreatureKit;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // A behaviour module only the tests have (outside namespace CreatureKit: never in the catalogue): run away
    // from the enclosure centre, at a utility the test sets.
    [Serializable]
    [Module("TestFlee", CreatureModule.Behaviour)]
    public sealed class TestFleeModule : CreatureModule
    {
        public override IBehaviour CreateBehaviour(in BehaviourCtx c) => new SetUtility(0.05f, (in BrainCtx b) => b.Area.center + (b.Sim.Joints[0] - b.Area.center).normalized * 100f);
    }

    public delegate Vector3 TargetFn(in BrainCtx c);

    // A behaviour whose utility the test sets; counts its entries.
    public sealed class SetUtility : IBehaviour
    {
        public float utility;
        public int entered;
        readonly TargetFn target;
        public SetUtility(float u, TargetFn target) { utility = u; this.target = target; }
        public float Utility(in BrainCtx c) => utility;
        public void Enter(in BrainCtx c) => entered++;
        public BrainOutput Tick(in BrainCtx c, float dt) => BrainOutput.Toward(target(c));
    }

    // Creature kit T3.4: BrainFactory keeps the shipped brains; ModularBrain switches with hysteresis and never
    // lets a behaviour take the body out of its enclosure.
    public class CreatureKitBrainTests
    {
        const float kDt = 1f / 60f;
        readonly List<Object> trash = new List<Object>();
        [TearDown] public void TearDown() { foreach (Object o in trash) if (o != null) Object.DestroyImmediate(o); trash.Clear(); }

        // the pre-kit CreatureLab.BrainFor, frozen
        static Type LegacyBrainType(CreatureSpecies sp, EnclosureArea area)
        {
            if (sp.CanFly) return typeof(FlightBrain);
            return sp.climb.canClimb && area.obstacleClimbable != null ? typeof(ClimbingBrain) : typeof(WanderBrain);
        }

        static EnclosureArea Plain(CreatureSpecies sp) => EnclosureArea.For(Vector3.zero, Vector3.up, 6f * sp.scale, sp, sp.scale);

        static EnclosureArea WithTree(CreatureSpecies sp)
        {
            EnclosureArea a = Plain(sp);
            a.obstacleCenters = new[] { new Vector3(1f, 0f, 1f) * sp.scale }; a.obstacleRadii = new[] { 0.3f * sp.scale };
            a.obstacleClimbable = new[] { true }; a.maxHeight = 2f * sp.scale;
            return a;
        }

        [TestCaseSource(typeof(CreatureKitAssets), nameof(CreatureKitAssets.Species))]
        public void BrainFactory_Legacy_SameTypes(string name)
        {
            CreatureSpecies sp = CreatureKitAssets.Load(name);
            if (sp.bodyPlan == CreatureBodyPlan.Cephalopod)
            {
                Assert.AreEqual(BrainKind.OwnBody, BrainFactory.KindFor(sp, Plain(sp)));
                Assert.IsNull(BrainFactory.Create(sp, Plain(sp), 1), "the octopus brings its own brain");
                return;
            }
            foreach (EnclosureArea area in new[] { Plain(sp), WithTree(sp) })
            {
                ICreatureBrain b = BrainFactory.Create(sp, area, 1);
                Assert.AreEqual(LegacyBrainType(sp, area), b.GetType(), $"{name} (climbable tree: {area.obstacleClimbable != null})");
            }
            // the same from the asset itself (a kit asset is compiled to its runtime view)
            CreatureSpecies asset = CreatureKitAssets.LoadAsset(name);
            Assert.AreEqual(LegacyBrainType(sp, WithTree(sp)), BrainFactory.Create(asset, WithTree(sp), 1).GetType());
        }

        CreatureSim Monitor(out CreatureSpecies sp, out EnclosureArea area, float radius)
        {
            sp = CreatureKitAssets.Load("DesertMonitor");
            area = EnclosureArea.For(Vector3.zero, Vector3.up, radius * sp.scale, sp, sp.scale);
            return new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, sp.scale) { Bounds = area };
        }

        [Test] public void ModularBrain_Hysteresis_MinHold()
        {
            CreatureSim sim = Monitor(out CreatureSpecies sp, out EnclosureArea area, 6f);
            var a = new SetUtility(0.5f, (in BrainCtx c) => c.Area.center + Vector3.forward);
            var b = new SetUtility(0.2f, (in BrainCtx c) => c.Area.center - Vector3.forward);
            var brain = new ModularBrain(area, new IBehaviour[] { a, b }, minHold: 1f, margin: 0.1f);
            float hold = 1f * sim.TimeScale;
            void Run(float seconds) { for (float t = 0f; t < seconds; t += kDt) sim.Tick(kDt, brain.Target(sim, kDt)); }

            Run(0.2f);
            Assert.AreEqual(0, brain.Current, "the higher utility starts");
            b.utility = 0.9f;
            Run(0.5f * hold);
            Assert.AreEqual(0, brain.Current, "no switch inside the minimum hold");
            Run(0.6f * hold);
            Assert.AreEqual(1, brain.Current, "switches once the hold is over");
            Assert.AreEqual(1, brain.Switches);
            // a challenger inside the margin never takes over
            a.utility = 0.95f;   // leads b (0.9) by 0.05 < 0.1
            Run(3f * hold);
            Assert.AreEqual(1, brain.Current, "within the switch margin: no flicker");
            a.utility = 1.2f;
            Run(1.1f * hold);
            Assert.AreEqual(0, brain.Current, "past the margin: it switches back");
            Assert.AreEqual(2, brain.Switches);
            Assert.AreEqual(1 + 1, a.entered, "entered on start and on the switch back");
        }

        // Whatever the behaviours want (one keeps aiming 100 m out of the enclosure), the body stays inside.
        [Test] public void ModularBrain_Containment()
        {
            CreatureSim sim = Monitor(out CreatureSpecies sp, out EnclosureArea area, 4f);
            var wander = new BrainBehaviour(new WanderBrain(area, 3), Wander.BaseUtility);
            var flee = new SetUtility(0f, (in BrainCtx c) => c.Area.center + (c.Sim.Joints[0] - c.Area.center + Vector3.right * 0.01f).normalized * 100f);
            var brain = new ModularBrain(area, new IBehaviour[] { wander, flee }, 1f, 0.05f);
            float worst = 0f;
            for (int t = 0; t < 60 * 120; t++)
            {
                flee.utility = (t / 180) % 2 == 0 ? 0f : 1f;   // flee 3 s of every 6
                sim.Tick(kDt, brain.Target(sim, kDt));
                Vector3 d = sim.Joints[0] - area.center; d.y = 0f;
                worst = Mathf.Max(worst, d.magnitude - area.safeRadius);
            }
            TestContext.WriteLine($"head past the safe disc by at most {worst * 1000f:F2} mm; switches {brain.Switches}");
            Assert.Greater(brain.Switches, 10, "it did switch");
            Assert.LessOrEqual(worst, 1e-3f, "the head never leaves the safe disc");
        }

        // A behaviour module the shipped brains do not cover makes the species modular: every behaviour joins.
        [Test] public void BrainFactory_NewBehaviour_IsModular()
        {
            CreatureSpecies a = CreatureKitAssets.LoadAsset("KitMonitor");
            var k = ScriptableObject.CreateInstance<CreatureSpecies>();
            trash.Add(k);
            k.name = "KitMonitor+flee"; k.scale = a.scale; k.kitVersion = 1;
            k.parts = new List<CreaturePart>(a.parts);
            k.modules = new List<CreatureModule>(a.modules) { new TestFleeModule() };
            Assert.IsNull(k.Validate(), k.name);
            CreatureSpecies sp = k.Compiled.Runtime;
            EnclosureArea area = Plain(sp);
            Assert.AreEqual(BrainKind.Modular, BrainFactory.KindFor(sp, area));
            var brain = BrainFactory.Create(sp, area, 1) as ModularBrain;
            Assert.IsNotNull(brain);
            // Wander + TestFlee (ClimbExcursion needs a climbable tree; there is none)
            Assert.AreEqual(2, brain.Behaviours.Count);
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, sp.scale) { Bounds = area };
            for (int t = 0; t < 120; t++) sim.Tick(kDt, brain.Target(sim, kDt));
            Assert.AreEqual(0, brain.Current, "wander (0.1) beats the flee's 0.05");
        }
    }
}
