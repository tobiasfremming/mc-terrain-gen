// Creature kit T3.4 (design §2.3): brains from the species' behaviour modules.
//
//   BrainFactory   the brain a species gets in an enclosure. The shipped behaviours keep exactly the brains the lab
//                  gave them before the kit (CreatureLab.BrainFor): FlightExcursion -> FlightBrain (a flyer),
//                  ClimbExcursion in an area with climbable obstacles -> ClimbingBrain, otherwise WanderBrain; a soft
//                  body (Ambush) brings its own (OctopusCreature). A species with a behaviour those brains do not
//                  cover (a new behaviour module: Flee, ...) gets a ModularBrain over all its behaviours.
//   ModularBrain   runs the behaviour with the highest utility, with a minimum hold (x sqrt of the size: times
//                  are Froude-scaled) and a switch margin so it does not flicker between two near-equal ones.
//   IBehaviour     what a behaviour module adds to a ModularBrain (CreatureModule.CreateBehaviour). The shipped
//                  brains join as behaviours through BrainBehaviour (a constant utility).
// Containment stays structural: whatever a behaviour asks for, the sim clamps the head to the safe disc.
// Main thread; allocation-free per frame after construction.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    // What a behaviour sees: the sim, its enclosure, the brain's clock.
    public readonly struct BrainCtx
    {
        public readonly CreatureSim Sim;
        public readonly EnclosureArea Area;
        public readonly float Time;
        public BrainCtx(CreatureSim sim, in EnclosureArea area, float time) { Sim = sim; Area = area; Time = time; }
    }

    // Keep: leave the locomotion as it is. Walk / Fly: a behaviour's wish (flight modes are the FlightBrain's today).
    public enum LocoRequest { Keep = 0, Walk = 1, Fly = 2 }

    public struct BrainOutput
    {
        public Vector3 target;
        public LocoRequest mode;
        public float cloak;      // 0 visible .. 1 gone (the Cloak look; spine bodies: Phase 4)
        public static BrainOutput Toward(Vector3 t) => new BrainOutput { target = t };
    }

    public interface IBehaviour
    {
        float Utility(in BrainCtx c);
        void Enter(in BrainCtx c);
        BrainOutput Tick(in BrainCtx c, float dt);
    }

    // A shipped brain as a behaviour of a ModularBrain, at a constant utility.
    public sealed class BrainBehaviour : IBehaviour
    {
        public readonly ICreatureBrain Brain;
        readonly float utility;
        public BrainBehaviour(ICreatureBrain brain, float utility) { Brain = brain; this.utility = utility; }
        public float Utility(in BrainCtx c) => utility;
        public void Enter(in BrainCtx c) { }
        public BrainOutput Tick(in BrainCtx c, float dt) => BrainOutput.Toward(Brain.Target(c.Sim, dt));
    }

    public sealed class ModularBrain : ICreatureBrain
    {
        public const float DefaultMinHold = 2f, DefaultMargin = 0.1f;

        readonly IBehaviour[] behaviours;
        readonly EnclosureArea area;
        readonly float minHold, margin;
        float clock, held;

        public int Current { get; private set; } = -1;
        public int Switches { get; private set; }
        public BrainOutput Last { get; private set; }
        public IReadOnlyList<IBehaviour> Behaviours => behaviours;

        // minHold: seconds at scale 1 a behaviour runs at least (x sqrt of the size); margin: utility a challenger
        // must lead by.
        public ModularBrain(EnclosureArea area, IBehaviour[] behaviours, float minHold = DefaultMinHold, float margin = DefaultMargin)
        {
            if (behaviours == null || behaviours.Length == 0) throw new ArgumentException("a ModularBrain needs behaviours", nameof(behaviours));
            this.area = area; this.behaviours = behaviours; this.minHold = minHold; this.margin = margin;
        }

        public Vector3 Target(CreatureSim sim, float dt)
        {
            clock += dt;
            var c = new BrainCtx(sim, area, clock);
            if (Current < 0) { Current = Best(c, out _); behaviours[Current].Enter(c); held = 0f; }
            else
            {
                held += dt;
                if (held >= minHold * sim.TimeScale)
                {
                    int best = Best(c, out float u);
                    if (best != Current && u > behaviours[Current].Utility(c) + margin)
                    {
                        Current = best; behaviours[best].Enter(c); held = 0f; Switches++;
                    }
                }
            }
            BrainOutput o = behaviours[Current].Tick(c, dt);
            Last = o;
            return o.target;
        }

        int Best(in BrainCtx c, out float best)
        {
            int k = 0; best = behaviours[0].Utility(c);
            for (int i = 1; i < behaviours.Length; i++)
            {
                float u = behaviours[i].Utility(c);
                if (u > best) { best = u; k = i; }   // ties: the first listed
            }
            return k;
        }
    }

    public enum BrainKind { Wander = 0, Climbing = 1, Flight = 2, Modular = 3, OwnBody = 4 }

    public static class BrainFactory
    {
        // The shipped behaviours the legacy brains cover.
        static bool Covered(CreatureModule m) => m is Wander || m is ClimbExcursion || m is FlightExcursion || m is Ambush;

        public static BrainKind KindFor(CreatureSpecies sp, in EnclosureArea area)
        {
            sp = sp.Compiled.Runtime;
            if (sp.bodyPlan == CreatureBodyPlan.Cephalopod) return BrainKind.OwnBody;
            IReadOnlyList<CreatureModule> mods = KitView.Modules(sp);
            bool climbExcursion = false, flightExcursion = false;
            for (int i = 0; i < mods.Count; i++)
            {
                CreatureModule m = mods[i];
                if (m.Meta.Category != CreatureModule.Behaviour) continue;
                if (!Covered(m)) return BrainKind.Modular;
                if (m is ClimbExcursion) climbExcursion = true;
                if (m is FlightExcursion) flightExcursion = true;
            }
            // the pre-kit CreatureLab.BrainFor, from the modules (a legacy asset lists exactly these)
            if (sp.CanFly && flightExcursion) return BrainKind.Flight;
            if (sp.climb.canClimb && climbExcursion && area.obstacleClimbable != null) return BrainKind.Climbing;
            return BrainKind.Wander;
        }

        // The brain for an individual of sp in area (null for a body with its own brain: the octopus).
        public static ICreatureBrain Create(CreatureSpecies sp, EnclosureArea area, int seed, PerchSpot[] perches = null)
        {
            switch (KindFor(sp, area))
            {
                case BrainKind.Flight: return new FlightBrain(area, seed, perches);
                case BrainKind.Climbing: return new ClimbingBrain(area, seed);
                case BrainKind.Wander: return WanderBrain.For(sp, area, seed);
                case BrainKind.Modular: return Modular(sp, area, seed, perches);
                default: return null;
            }
        }

        // Every behaviour module's behaviour, in module order (a module with none is skipped; none at all: Wander).
        public static ModularBrain Modular(CreatureSpecies sp, EnclosureArea area, int seed, PerchSpot[] perches = null)
        {
            sp = sp.Compiled.Runtime;
            IReadOnlyList<CreatureModule> mods = KitView.Modules(sp);
            var list = new List<IBehaviour>();
            var bc = new BehaviourCtx(sp, area, seed, perches);
            for (int i = 0; i < mods.Count; i++)
            {
                if (mods[i].Meta.Category != CreatureModule.Behaviour) continue;
                IBehaviour b = mods[i].CreateBehaviour(bc);
                if (b != null) list.Add(b);
            }
            if (list.Count == 0) list.Add(new BrainBehaviour(new WanderBrain(area, seed), Wander.BaseUtility));
            return new ModularBrain(area, list.ToArray());
        }
    }

    // What a behaviour module gets to build its behaviour for one individual.
    public readonly struct BehaviourCtx
    {
        public readonly CreatureSpecies Species;
        public readonly EnclosureArea Area;
        public readonly int Seed;
        public readonly PerchSpot[] Perches;
        public BehaviourCtx(CreatureSpecies species, in EnclosureArea area, int seed, PerchSpot[] perches) { Species = species; Area = area; Seed = seed; Perches = perches; }
    }

    public sealed partial class Wander
    {
        // the idle default every other behaviour has to beat
        public const float BaseUtility = 0.1f;
        public override IBehaviour CreateBehaviour(in BehaviourCtx c) => new BrainBehaviour(WanderBrain.For(c.Species, c.Area, c.Seed), BaseUtility);
    }

    public sealed partial class ClimbExcursion
    {
        // wander with climbs of the enclosure's climbable obstacles (ClimbingBrain wraps the wander)
        public override IBehaviour CreateBehaviour(in BehaviourCtx c)
            => c.Area.obstacleClimbable != null ? new BrainBehaviour(new ClimbingBrain(c.Area, c.Seed), Wander.BaseUtility + 0.05f) : null;
    }

    public sealed partial class FlightExcursion
    {
        public override IBehaviour CreateBehaviour(in BehaviourCtx c)
            => c.Species.CanFly ? new BrainBehaviour(new FlightBrain(c.Area, c.Seed, c.Perches), Wander.BaseUtility + 0.1f) : null;
    }
}
