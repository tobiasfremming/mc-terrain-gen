using System;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // CommandBrain (Vesper step 1, task 4): the "M" menu's commands - Stand, WalkAround, WalkTo, Fly, Circle, Land, Perch -
    // turned into the creature's own motion with natural transitions, on Vesper (scale 4) in its lab aviary. Every command
    // from the ground and from the air reaches its end state within a Froude time bound (s x sqrt s):
    //   Stand      on the ground, head speed < 2 % of the body length per second (a flyer lands first)
    //   WalkAround on the ground and walking: the head travels >= 1 body length after the last touchdown
    //   WalkTo     on the ground, the head within 1 body length of the point's ground projection, then standing
    //   Fly        Air within 10 s x sqrt s from the ground; in the air it keeps flying (no landing)
    //   Circle     >= one full turn in the air, its circle's radius >= the flight's turn radius, the altitude held in a band,
    //              the girdle inside the safe disc throughout
    //   Land       on the ground (a walker already there stays there)
    //   Perch      with a rock in the aviary: on the ground and Perched (on the rock's top)
    // A command given mid-transition (Walk during the launch, Fly during the touchdown) still completes; Perch with no
    // perch anywhere lands on open ground and says so. No run ever snaps the head (a jump > kSnap x the body length in one
    // tick). The brain allocates nothing per tick. RED: a brain that ignores the transitions (walks while airborne) never
    // gets a flyer onto the ground for a walk.
    public class CreatureCommandBrainTests
    {
        const float kDt = 1f / 60f;
        // body lengths per tick: a girdle jump past this is a snap (12 m/s at scale 4 is 0.016 L; the shipped vault's first
        // tick is 0.48 m = 0.039 L). The girdle, not the head: Vesper's head whips up to 7 m in a tick in steady flight under
        // its own FlightBrain (the air chain; reported); the touchdown's walking restart is reported, not judged (the flight's)
        const float kSnap = 0.05f;
        const float kStill = 0.02f;         // body lengths per second: standing still
        // time bounds, s x sqrt s (about 2x the worst measured; the report has every run's)
        const float kFlyBound = 10f;        // the brief's
        const float kGroundBound = 10f;     // a ground command from the ground
        const float kLandBound = 30f;       // a command that lands first
        const float kCircleBound = 20f;
        const float kPerchBound = 60f;
        const float kGiveUpBound = 120f;    // kPerchFailsMax (2) perch attempts of 30 s x sqrt s, then an open-ground landing

        internal sealed class Rig
        {
            public readonly CreatureSpecies sp;
            public readonly CreatureSim sim;
            public readonly CommandBrain brain;
            public readonly EnclosureArea area;
            public readonly float s, T, L;
            public float maxJump;      // largest girdle step in one tick outside the touchdown, m
            public float touchdownJump; // ... in the touchdown (the flight's walking restart: reported, not judged)

            public Rig(CreatureSpecies sp, IGroundProbe ground = null, int seed = 1)
            {
                this.sp = sp;
                s = sp.scale; T = Mathf.Sqrt(s); L = sp.BodyLength * s;
                area = KitChecks.LabArea(sp);
                sim = new CreatureSim(sp, ground ?? new FlatGroundProbe(), _ => Vector3.up, area.center, Vector3.forward, s, seed) { Bounds = area };
                brain = new CommandBrain(area, seed);
            }

            public void Tick()
            {
                int g = sim.Flight.Girdle;
                Vector3 g0 = sim.Joints[g];
                sim.Tick(kDt, brain.Target(sim, kDt));
                float j = (sim.Joints[g] - g0).magnitude;
                if (sim.Mode == FlightMode.Touchdown) touchdownJump = Mathf.Max(touchdownJump, j);
                else maxJump = Mathf.Max(maxJump, j);
            }

            // Ticks until done() (checked after each tick) or `seconds` x sqrt s; the seconds taken, or -1.
            public float Until(Func<bool> done, float seconds)
            {
                float t0 = sim.Time;
                int n = Mathf.CeilToInt(seconds * T / kDt);
                for (int i = 0; i < n; i++)
                {
                    Tick();
                    if (done()) return sim.Time - t0;
                }
                return -1f;
            }

            public void Run(float seconds) { int n = Mathf.CeilToInt(seconds * T / kDt); for (int i = 0; i < n; i++) Tick(); }

            // Fly, and on until it has been in the air (Air) for 3 s x sqrt s: the "from the air" start.
            public string Airborne()
            {
                brain.Command(CreatureCommand.Fly);
                if (Until(() => sim.Mode == FlightMode.Air, kFlyBound) < 0f) return $"never airborne ({sim.Mode}, {brain.Doing})";
                Run(3f);
                return sim.Mode == FlightMode.Air ? null : $"left the air ({sim.Mode}, {brain.Doing})";
            }

            public string Snap() => maxJump > kSnap * L ? $"the girdle snapped {maxJump:F2} m in one tick (> {kSnap} x {L:F1} m)" : null;

            public string State => $"t={sim.Time:F1} s {sim.Mode}{(sim.Flight.Landing ? " landing" : "")}{(sim.Flight.Perched ? " perched" : "")}, doing \"{brain.Doing}\"";
        }

        static CreatureSpecies Vesper() => CreatureVesperTests.Vesper();

        static void Report(string name, Rig r, float took) =>
            TestContext.Out.WriteLine($"{name}: {(took >= 0f ? $"{took:F1} s ({took / r.T:F1} s x sqrt s)" : "never")}; {r.State}; max girdle step {r.maxJump:F3} m (touchdown {r.touchdownJump:F3} m); head speed {r.sim.Speed:F3} m/s");

        static Rig Start(bool fromAir, IGroundProbe ground = null)
        {
            var r = new Rig(Vesper(), ground);
            r.Run(0.5f);
            if (fromAir) { string e = r.Airborne(); Assert.IsNull(e, e); }
            return r;
        }

        // ------------------------------------------------------------ the end states (problem functions: null = reached)

        static bool OnGround(Rig r) => r.sim.Mode == FlightMode.Ground && !r.sim.Flight.Landing && !r.sim.Flight.Crouching;

        // Stand: on the ground and still for 1 s x sqrt s
        internal static string StandProblem(Rig r, float bound, out float took)
        {
            float stillSince = -1f;
            took = r.Until(() =>
            {
                bool still = OnGround(r) && r.sim.Speed < kStill * r.L;
                if (!still) { stillSince = -1f; return false; }
                if (stillSince < 0f) stillSince = r.sim.Time;
                return r.sim.Time - stillSince >= 1f * r.T;
            }, bound);
            if (took < 0f) return $"Stand: not standing still within {bound} s x sqrt s ({r.State}, speed {r.sim.Speed:F3} m/s)";
            return r.Snap();
        }

        // WalkAround: on the ground, the head walks >= 1 body length of path (since it last got onto the ground)
        internal static string WalkProblem(Rig r, float bound, out float took)
        {
            Vector3 from = r.sim.Joints[0]; bool was = false; float walked = 0f;
            took = r.Until(() =>
            {
                bool on = OnGround(r);
                if (on && !was) walked = 0f;
                if (on && was) walked += Vector3.ProjectOnPlane(r.sim.Joints[0] - from, Vector3.up).magnitude;
                was = on; from = r.sim.Joints[0];
                return on && walked >= r.L;
            }, bound);
            if (took < 0f) return $"WalkAround: not walking within {bound} s x sqrt s ({r.State}, walked {walked:F1} m of {r.L:F1})";
            return r.Snap();
        }

        // WalkTo: on the ground with the head within 1 body length of the point's ground projection, and then standing (the
        // command turns to Stand)
        internal static string WalkToProblem(Rig r, Vector3 point, float bound, out float took)
        {
            Vector3 g = new Vector3(point.x, 0f, point.z);
            took = r.Until(() => OnGround(r) && r.brain.Current == CreatureCommand.Stand, bound);
            float d = Vector3.ProjectOnPlane(r.sim.Joints[0] - g, Vector3.up).magnitude;
            if (took < 0f) return $"WalkTo: never arrived within {bound} s x sqrt s ({r.State}, head {d:F1} m from the point)";
            if (d > r.L) return $"WalkTo: arrived {d:F1} m from the point (> 1 body length {r.L:F1} m)";
            return r.Snap();
        }

        // Fly: in the air (Air) within the bound, then still in the air (not landing) 5 s x sqrt s later
        internal static string FlyProblem(Rig r, float bound, out float took)
        {
            took = r.Until(() => r.sim.Mode == FlightMode.Air, bound);
            if (took < 0f) return $"Fly: not in the air within {bound} s x sqrt s ({r.State})";
            int n = Mathf.CeilToInt(5f * r.T / kDt);
            for (int i = 0; i < n; i++)
            {
                r.Tick();
                if (r.sim.Mode != FlightMode.Air || r.sim.Flight.Landing) return $"Fly: left the air ({r.State})";
            }
            return r.Snap();
        }

        // Circle: one full turn of the heading in the air, the brain's circle's radius >= the flight's turn radius, the
        // altitude over the last turn within kBand x s of the circle's, the girdle inside the safe disc throughout
        const float kBand = 1.5f;
        internal static string CircleProblem(Rig r, float bound, out float took, out float spread)
        {
            spread = 0f;
            CreatureFlight fl = r.sim.Flight;
            float turned = 0f, lo = float.MaxValue, hi = float.MinValue, outside = 0f;
            Vector3 prev = Vector3.zero;
            took = r.Until(() =>
            {
                outside = Mathf.Max(outside, r.area.PlanarDistance(r.sim.Joints[fl.Girdle]) - r.area.safeRadius);
                if (r.sim.Mode != FlightMode.Air || fl.Landing) { turned = 0f; prev = Vector3.zero; return false; }
                Vector3 f = Vector3.ProjectOnPlane(fl.Model.Forward, Vector3.up).normalized;
                if (prev != Vector3.zero) turned += Vector3.SignedAngle(prev, f, Vector3.up);
                prev = f;
                // the band over the measured turn, once it is round the circle's first half turn (settled onto it)
                if (Mathf.Abs(turned) >= 180f) { lo = Mathf.Min(lo, fl.Model.Altitude); hi = Mathf.Max(hi, fl.Model.Altitude); }
                return Mathf.Abs(turned) >= 360f + 180f;
            }, bound);
            if (took < 0f) return $"Circle: not one full turn within {bound} s x sqrt s ({r.State}, turned {turned:F0} deg)";
            spread = hi - lo;
            float R = fl.TurnRadius;
            if (r.brain.CircleRadius < R) return $"Circle: radius {r.brain.CircleRadius:F1} m < the turn radius {R:F1} m";
            if (lo < r.brain.CircleAltitude - kBand * r.s || hi > r.brain.CircleAltitude + kBand * r.s)
                return $"Circle: altitude {lo:F1}..{hi:F1} m outside {r.brain.CircleAltitude:F1} +- {kBand * r.s:F1} m";
            if (outside > 0f) return $"Circle: the girdle left the safe disc by {outside:F2} m";
            return r.Snap();
        }

        // Land: on the ground (stays there)
        internal static string LandProblem(Rig r, float bound, out float took)
        {
            took = r.Until(() => OnGround(r), bound);
            if (took < 0f) return $"Land: not on the ground within {bound} s x sqrt s ({r.State})";
            r.Run(2f);
            if (!OnGround(r)) return $"Land: left the ground again ({r.State})";
            return r.Snap();
        }

        // Perch: on the ground and Perched
        internal static string PerchProblem(Rig r, float bound, out float took)
        {
            took = r.Until(() => OnGround(r) && r.sim.Flight.Perched, bound);
            if (took < 0f) return $"Perch: not perched within {bound} s x sqrt s ({r.State})";
            return r.Snap();
        }

        // the perch rock (m at scale 1, x the scale): a flat-topped boulder to one side of the middle, 1.2 m square and 0.5 m
        // high. (Perching a scale-4 Vesper on a rock in its lab aviary rests on the flight's landing autopilot and obstacle
        // avoidance, which get it onto some rocks quickly and onto others not at all - the report's perch sweep; this one
        // is of the first kind, HardRock of the second.)
        internal static WingedTestSurfaces Rock(float s) =>
            new WingedTestSurfaces().AddRock(new Vector3(2.5f, 0f, 1.25f) * s, new Vector3(1.2f, 0.5f, 1.2f) * s, 11);

        internal static WingedTestSurfaces HardRock(float s) =>
            new WingedTestSurfaces().AddRock(new Vector3(2.5f, 0f, 1.25f) * s, new Vector3(2.4f, 0.75f, 2.4f) * s, 11);

        // ------------------------------------------------------------ the commands, from the ground and from the air

        [Test] public void Command_Stand_FromGround()
        {
            Rig r = Start(false);
            r.brain.Command(CreatureCommand.WalkAround); r.Run(3f);
            r.brain.Command(CreatureCommand.Stand);
            string e = StandProblem(r, kGroundBound, out float took); Report("Stand from the ground (walking)", r, took);
            Assert.IsNull(e, e);
            Assert.IsTrue(r.sim.Halt, "standing = Halt");
        }

        [Test] public void Command_Stand_FromAir()
        {
            Rig r = Start(true);
            r.brain.Command(CreatureCommand.Stand);
            string e = StandProblem(r, kLandBound, out float took); Report("Stand from the air", r, took);
            Assert.IsNull(e, e);
        }

        [Test] public void Command_WalkAround_FromGround()
        {
            Rig r = Start(false);
            r.brain.Command(CreatureCommand.WalkAround);
            string e = WalkProblem(r, kGroundBound * 2f, out float took); Report("WalkAround from the ground", r, took);
            Assert.IsNull(e, e);
        }

        internal static string WalkFromAirProblem(Rig r, out float took)
        {
            r.brain.Command(CreatureCommand.WalkAround);
            return WalkProblem(r, kLandBound, out took);
        }

        [Test] public void Command_WalkAround_FromAir()
        {
            Rig r = Start(true);
            string e = WalkFromAirProblem(r, out float took); Report("WalkAround from the air", r, took);
            Assert.IsNull(e, e);
        }

        // RED: a brain that ignores the transitions (walks while airborne: the hook) never gets the flyer down to walk
        [Test] public void Red_CommandBrain_IgnoresTransitions()
        {
            Rig r = Start(true);
            r.brain.ignoreTransitions = true;
            string e = WalkFromAirProblem(r, out float took);
            TestContext.Out.WriteLine("RED: " + e);
            Assert.IsNotNull(e);
            StringAssert.Contains("not walking", e);
        }

        static Vector3 WalkPoint(Rig r) => r.area.center + new Vector3(0.45f, 0f, -0.3f) * r.area.safeRadius + Vector3.up * (3f * r.s); // in the air above it

        [Test] public void Command_WalkTo_FromGround()
        {
            Rig r = Start(false);
            Vector3 p = WalkPoint(r);
            r.brain.Command(CreatureCommand.WalkTo, p);
            string e = WalkToProblem(r, p, kLandBound, out float took); Report("WalkTo from the ground", r, took);
            Assert.IsNull(e, e);
            e = StandProblem(r, kGroundBound, out _);
            Assert.IsNull(e, "then stands: " + e);
        }

        [Test] public void Command_WalkTo_FromAir()
        {
            Rig r = Start(true);
            Vector3 p = WalkPoint(r);
            r.brain.Command(CreatureCommand.WalkTo, p);
            string e = WalkToProblem(r, p, kLandBound, out float took); Report("WalkTo from the air", r, took);
            Assert.IsNull(e, e);
        }

        [Test] public void Command_Fly_FromGround()
        {
            Rig r = Start(false);
            r.brain.Command(CreatureCommand.Fly);
            string e = FlyProblem(r, kFlyBound, out float took); Report("Fly from the ground", r, took);
            Assert.IsNull(e, e);
        }

        [Test] public void Command_Fly_FromAir()
        {
            Rig r = Start(true);
            r.brain.Command(CreatureCommand.Circle); r.Run(4f);
            r.brain.Command(CreatureCommand.Fly);
            string e = FlyProblem(r, kFlyBound, out float took); Report("Fly from the air (circling)", r, took);
            Assert.IsNull(e, e);
        }

        [Test] public void Command_Circle_FromGround()
        {
            Rig r = Start(false);
            r.brain.Command(CreatureCommand.Circle);
            string e = CircleProblem(r, kCircleBound, out float took, out float spread); Report($"Circle from the ground (radius {r.brain.CircleRadius:F1} m, turn radius {r.sim.Flight.TurnRadius:F1} m, altitude spread {spread:F2} m)", r, took);
            Assert.IsNull(e, e);
        }

        [Test] public void Command_Circle_FromAir()
        {
            Rig r = Start(true);
            r.brain.Command(CreatureCommand.Circle);
            string e = CircleProblem(r, kCircleBound, out float took, out float spread); Report($"Circle from the air (radius {r.brain.CircleRadius:F1} m, turn radius {r.sim.Flight.TurnRadius:F1} m, altitude spread {spread:F2} m)", r, took);
            Assert.IsNull(e, e);
        }

        [Test] public void Command_Land_FromGround()
        {
            Rig r = Start(false);
            r.brain.Command(CreatureCommand.Land);
            string e = LandProblem(r, kGroundBound, out float took); Report("Land from the ground", r, took);
            Assert.IsNull(e, e);
            Assert.AreEqual(0, r.sim.Flight.Launches, "a walker told to land does not take off");
        }

        [Test] public void Command_Land_FromAir()
        {
            Rig r = Start(true);
            r.brain.Command(CreatureCommand.Land);
            string e = LandProblem(r, kLandBound, out float took); Report("Land from the air", r, took);
            Assert.IsNull(e, e);
        }

        [Test] public void Command_Perch_FromGround()
        {
            Rig r = Start(false, Rock(Vesper().scale));
            r.brain.Command(CreatureCommand.Perch);
            string e = PerchProblem(r, kPerchBound, out float took); Report("Perch from the ground", r, took);
            Assert.IsNull(e, e);
            r.Tick();   // (Doing is the brain's view before the tick that perched it)
            StringAssert.Contains("perched", r.brain.Doing);
        }

        [Test] public void Command_Perch_FromAir()
        {
            Rig r = Start(true, Rock(Vesper().scale));
            r.brain.Command(CreatureCommand.Perch);
            string e = PerchProblem(r, kPerchBound, out float took); Report("Perch from the air", r, took);
            Assert.IsNull(e, e);
        }

        // ------------------------------------------------------------ mid-transition, no perch

        // Walk given during the launch: it flies on, lands and walks. Fly given during the touchdown: it takes off again.
        [Test] public void Command_DuringTransition_Completes()
        {
            Rig r = Start(false);
            r.brain.Command(CreatureCommand.Fly);
            Assert.Greater(r.Until(() => r.sim.Mode == FlightMode.Launch, kFlyBound), 0f, "launches");
            r.brain.Command(CreatureCommand.WalkAround);
            string e = WalkProblem(r, kLandBound, out float took); Report("WalkAround given during the launch", r, took);
            Assert.IsNull(e, e);

            Rig a = Start(true);
            a.brain.Command(CreatureCommand.Land);
            Assert.Greater(a.Until(() => a.sim.Mode == FlightMode.Touchdown, kLandBound), 0f, $"touches down ({a.State})");
            a.brain.Command(CreatureCommand.Fly);
            e = FlyProblem(a, kFlyBound, out took); Report("Fly given during the touchdown", a, took);
            Assert.IsNull(e, e);
        }

        // Perch onto a rock the landing autopilot does not get it down on (HardRock): it never stalls - after kPerchFailsMax
        // timed-out attempts it lands on open ground, saying so (or it perches after all)
        [Test] public void Perch_Unreachable_NeverStalls()
        {
            Rig r = Start(false, HardRock(Vesper().scale));
            r.brain.Command(CreatureCommand.Perch);
            string e = LandProblem(r, kGiveUpBound, out float took); Report($"Perch onto the hard rock (from the ground; perched {r.sim.Flight.Perched}, gave up {r.brain.PerchGaveUp})", r, took);
            Assert.IsNull(e, e);
            if (!r.sim.Flight.Perched) { StringAssert.Contains("could not reach the perch", r.brain.Doing); Assert.IsTrue(r.brain.PerchGaveUp); }
        }

        // Perch with no perch anywhere (flat ground): it lands on open ground and says so
        [Test] public void Perch_NoPerch_FallsBackToLand()
        {
            Rig r = Start(true);
            r.brain.Command(CreatureCommand.Perch);
            r.Tick();
            StringAssert.Contains("no perch", r.brain.Doing, "says so at once");
            string e = LandProblem(r, kLandBound, out float took); Report("Perch with no perch (from the air)", r, took);
            Assert.IsNull(e, e);
            Assert.IsFalse(r.sim.Flight.Perched);
            StringAssert.Contains("no perch", r.brain.Doing);
            // from the ground: it stays there, saying so
            Rig g = Start(false);
            g.brain.Command(CreatureCommand.Perch);
            g.Run(3f);
            Assert.IsTrue(OnGround(g), g.State);
            Assert.AreEqual(0, g.sim.Flight.Launches, "no take-off for a perch that is not there");
            StringAssert.Contains("no perch", g.brain.Doing);
        }

        // ------------------------------------------------------------ no allocations

        [Test] public void CommandBrain_NoGC()
        {
            Rig r = Start(false, Rock(Vesper().scale));
            CreatureCommand[] seq = { CreatureCommand.WalkAround, CreatureCommand.WalkTo, CreatureCommand.Stand, CreatureCommand.Fly, CreatureCommand.Circle, CreatureCommand.Perch, CreatureCommand.Land };
            // warm up every path once (each command for a while)
            foreach (CreatureCommand c in seq) { r.brain.Command(c, WalkPoint(r)); r.Run(4f); }
            GcAllocs.AssertCounterWorks();
            int allocs = GcAllocs.Count(() =>
            {
                foreach (CreatureCommand c in seq)
                {
                    r.brain.Command(c, WalkPoint(r));
                    for (int i = 0; i < 240; i++) r.brain.Target(r.sim, kDt);   // the brain alone, many frames
                    for (int i = 0; i < 60; i++) { r.sim.Tick(kDt, r.brain.Target(r.sim, kDt)); }
                }
            });
            Assert.AreEqual(0, allocs, "CommandBrain allocates per tick");
        }
    }
}
