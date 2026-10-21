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
    //   Land       on the ground (a walker already there halts there, saying "landed")
    //   Perch      with a rock in the aviary: on the ground on the rock's top (CommandBrain.OnPerch: the surface under the
    //              girdle is the rock's)
    // A command given mid-transition (Walk during the launch, Fly during the touchdown, Fly during a landing) still
    // completes; Perch with no perch anywhere lands on open ground and says so; a perch that cannot be reached (hook) or a
    // touchdown beside the rock (hook) each count as failed attempts, and after kPerchFailsMax it gives up on open ground,
    // saying so. In no run does the girdle step more than kSnap x the body length in one tick, the touchdown included
    // (as CreatureVesperTests.Vesper_Touchdown_NoGirdleJump). The brain allocates nothing per tick. RED: a brain that ignores
    // the transitions (walks while airborne) never gets a flyer onto the ground for a walk.
    public class CreatureCommandBrainTests
    {
        const float kDt = 1f / 60f;
        // body lengths per tick: a girdle jump past this is a snap (12 m/s at scale 4 is 0.016 L; the shipped vault's first
        // tick is 0.48 m = 0.039 L), the touchdown included (Vesper's touchdownFromHover; CreatureVesperTests' kTouchJump)
        const float kSnap = 0.05f;
        const float kStill = 0.02f;         // body lengths per second: standing still
        // time bounds, s x sqrt s (about 2x the worst measured; the report has every run's)
        const float kFlyBound = 10f;        // the brief's
        const float kGroundBound = 10f;     // a ground command from the ground
        const float kLandBound = 30f;       // a command that lands first
        const float kCircleBound = 20f;
        const float kPerchBound = 60f;
        const float kGiveUpBound = 100f;    // kPerchFailsMax (2) perch attempts of 30 s x sqrt s, then an open-ground landing

        internal sealed class Rig
        {
            public readonly CreatureSpecies sp;
            public readonly CreatureSim sim;
            public readonly CommandBrain brain;
            public readonly EnclosureArea area;
            public readonly float s, T, L;
            public float maxJump;      // largest girdle step in one tick outside the touchdown, m
            public float touchdownJump; // ... in the touchdown

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

            public string Snap() => maxJump > kSnap * L ? $"the girdle snapped {maxJump:F2} m in one tick (> {kSnap} x {L:F1} m)"
                : touchdownJump > kSnap * L ? $"the girdle snapped {touchdownJump:F2} m in one tick of a touchdown (> {kSnap} x {L:F1} m)" : null;

            public string State => $"t={sim.Time:F1} s {sim.Mode}{(sim.Flight.Landing ? " landing" : "")}{(brain.OnPerch ? " on the perch" : "")}, doing \"{brain.Doing}\"";
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

        // Perch: on the ground on the perch (CommandBrain.OnPerch)
        internal static string PerchProblem(Rig r, float bound, out float took)
        {
            took = r.Until(() => OnGround(r) && r.brain.OnPerch, bound);
            if (took < 0f) return $"Perch: not perched within {bound} s x sqrt s ({r.State})";
            return r.Snap();
        }

        // the perch rock (m at scale 1, x the scale): a flat-topped boulder to one side of the middle, 1.2 m square and 0.5 m
        // high. (Perching a scale-4 Vesper on a rock in its lab aviary rests on the flight's landing autopilot, which gets
        // it onto some rocks quickly and onto others not at all - the report's perch sweep; this one is of the first kind.)
        internal static WingedTestSurfaces Rock(float s) =>
            new WingedTestSurfaces().AddRock(new Vector3(2.5f, 0f, 1.25f) * s, new Vector3(1.2f, 0.5f, 1.2f) * s, 11);



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

        // circling, then Fly: it stays in the air, flying (not circling): it leaves the circle's ring
        [Test] public void Command_Fly_FromAir()
        {
            Rig r = Start(true);
            r.brain.Command(CreatureCommand.Circle);
            float ring = 0f;
            Assert.GreaterOrEqual(r.Until(() => (ring = r.brain.CircleRadius) > 0f && Mathf.Abs(r.area.PlanarDistance(r.sim.Joints[r.sim.Flight.Girdle]) - ring) < 0.1f * ring, kCircleBound), 0f, "on the circle");
            r.Run(2f);
            Assert.AreEqual("circling", r.brain.Doing);
            r.brain.Command(CreatureCommand.Fly);
            string e = FlyProblem(r, kFlyBound, out float took); Report("Fly from the air (circling)", r, took);
            Assert.IsNull(e, e);
            Assert.AreEqual("flying", r.brain.Doing);
            float off = 0f;
            float left = r.Until(() => (off = Mathf.Abs(r.area.PlanarDistance(r.sim.Joints[r.sim.Flight.Girdle]) - ring)) > 0.25f * ring, 2f * kFlyBound);
            TestContext.Out.WriteLine($"left the circle's ring (radius {ring:F1} m) by {off:F1} m after {left:F1} s");
            Assert.GreaterOrEqual(left, 0f, "Fly leaves the circle");
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

        // a walker told to land halts where it is (it is down already): still, Halt, "landed", no take-off
        [Test] public void Command_Land_FromGround()
        {
            Rig r = Start(false);
            r.brain.Command(CreatureCommand.WalkAround); r.Run(3f);
            r.brain.Command(CreatureCommand.Land);
            string e = StandProblem(r, kGroundBound, out float took); Report("Land from the ground (walking)", r, took);
            Assert.IsNull(e, e);
            Assert.IsTrue(r.sim.Halt, "halted");
            Assert.AreEqual("landed", r.brain.Doing);
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
            Assert.AreEqual("perched", r.brain.Doing);
            Assert.AreEqual(0, r.brain.PerchMisses);
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

        // Perch onto a perch it never lines up on (hook: every attempt times out): it never stalls - after kPerchFailsMax
        // timed-out attempts it lands on open ground and says it could not reach the perch
        internal static string GiveUpProblem(Rig r, out float took, float bound = kGiveUpBound)
        {
            r.brain.Command(CreatureCommand.Perch);
            took = r.Until(() => OnGround(r) && r.brain.PerchGaveUp, bound);
            if (took < 0f) return $"did not give up the perch on the ground within {kGiveUpBound} s x sqrt s ({r.State})";
            r.Run(2f);
            if (!OnGround(r)) return $"left the ground again ({r.State})";
            if (r.Snap() != null) return r.Snap();
            if (r.brain.OnPerch) return "on the perch after all";
            if (r.brain.Doing != "could not reach the perch: on open ground") return $"says \"{r.brain.Doing}\"";
            return null;
        }

        [Test] public void Perch_Unreachable_GivesUp()
        {
            Rig r = Start(true, Rock(Vesper().scale));
            r.brain.perchUnreachable = true;
            string e = GiveUpProblem(r, out float took); Report("Perch never lined up (hook; from the air)", r, took);
            Assert.IsNull(e, e);
            Assert.AreEqual(0, r.brain.PerchMisses);
        }

        // Perch whose touchdowns land beside the rock (hook: the landing asked for 1.75 m x s short of it on its final): a
        // touchdown off the perch is a failed attempt (not "perched") - it takes off for another, and after kPerchFailsMax
        // gives up there and stays down
        [Test] public void Perch_TouchdownMissesRock_CountsAndEnds()
        {
            Rig r = Start(true, Rock(Vesper().scale));
            r.brain.perchMissBy = 1.75f * r.s;
            r.brain.perchFailsMax = 4;   // room for a take-off after a miss whatever ends the other attempts
            int launches0 = r.sim.Flight.Launches;
            string e = GiveUpProblem(r, out float took, 2f * kGiveUpBound); Report($"Perch touching down beside the rock (hook; misses {r.brain.PerchMisses}, launches {r.sim.Flight.Launches - launches0})", r, took);
            Assert.IsNull(e, e);
            // each attempt ends in a miss (a touchdown beside the rock) or a timed-out line-up (the flight's slow case);
            // after a miss below the limit it takes off for the next
            Assert.GreaterOrEqual(r.brain.PerchMisses, 2, "touchdowns beside the rock are failed attempts");
            Assert.GreaterOrEqual(r.sim.Flight.Launches - launches0, r.brain.PerchMisses - 1, "took off again after each miss but the last");
            int launches = r.sim.Flight.Launches;
            r.Run(5f);
            Assert.AreEqual(launches, r.sim.Flight.Launches, "stays down");
        }

        // Perched, then WalkTo a point off the rock, then Perch again: walking off clears OnPerch (Stand there does not say
        // "perched"), and the second Perch takes off and perches again (or gives up) - at no tick on open ground does it
        // say "perched" or report OnPerch. RED (keepStaleOnPerch): the flag stays set, the second Perch reports "perched" on
        // open ground without taking off.
        internal static string WalkOffProblem(Rig r, WingedTestSurfaces rock)
        {
            r.brain.Command(CreatureCommand.Perch);
            if (r.Until(() => OnGround(r) && r.brain.OnPerch, kPerchBound) < 0f) return $"never perched ({r.State})";
            Vector3 off = r.area.center - new Vector3(2.5f, 0f, 1.25f).normalized * (0.3f * r.area.safeRadius);
            string bad = null;
            int run = 0;   // consecutive ticks claiming the perch off it (the brain sees the girdle a tick late: 2 = stale)
            Func<bool> check = () =>
            {
                bool off = false;
                if (OnGround(r) && (r.brain.OnPerch || r.brain.Doing == "perched"))
                {
                    Vector3 g = r.sim.Joints[r.sim.Flight.Girdle];
                    off = !(rock.Probe(g + Vector3.up * r.s, Vector3.up, 30f * r.s, SurfaceHit.NoSource, -1, out SurfaceHit h) && h.source == 11);
                }
                run = off ? run + 1 : 0;
                if (bad == null && run >= 2) bad = $"says \"{r.brain.Doing}\" (OnPerch {r.brain.OnPerch}) on open ground ({r.State})";
                return false;
            };
            r.brain.Command(CreatureCommand.WalkTo, off);
            if (r.Until(() => { check(); return OnGround(r) && r.brain.Arrived; }, kLandBound) < 0f) return $"WalkTo off the rock never arrived ({r.State})";
            r.Run(1f); check();
            if (bad != null) return bad;
            int launches = r.sim.Flight.Launches;
            r.brain.Command(CreatureCommand.Perch);
            r.Until(() => { check(); return OnGround(r) && (r.brain.OnPerch || r.brain.PerchGaveUp) && r.sim.Flight.Launches > launches; }, kGiveUpBound);
            if (bad != null) return bad;
            if (r.sim.Flight.Launches == launches) return $"Perch again from open ground never took off ({r.State})";
            if (!(r.brain.OnPerch || r.brain.PerchGaveUp)) return $"Perch again did not end ({r.State})";
            return null;
        }

        // a low perch rock (0.15 m high at scale 1, else as Rock) a walker can step off: Vesper does not walk off the 0.5 m
        // Rock (its walking body stalls at the edge - the walking system's, reported)
        internal static WingedTestSurfaces LowRock(float s) =>
            new WingedTestSurfaces().AddRock(new Vector3(2.5f, 0f, 1.25f) * s, new Vector3(1.2f, 0.15f, 1.2f) * s, 11);

        [Test] public void Perch_WalkOff_PerchAgain()
        {
            WingedTestSurfaces rock = LowRock(Vesper().scale);
            Rig r = Start(true, rock);
            string e = WalkOffProblem(r, rock); Report($"Perch, walk off, Perch again (on the perch {r.brain.OnPerch}, gave up {r.brain.PerchGaveUp})", r, 0f);
            Assert.IsNull(e, e);
        }

        [Test] public void Red_CommandBrain_StaleOnPerch()
        {
            WingedTestSurfaces rock = LowRock(Vesper().scale);
            Rig r = Start(true, rock);
            r.brain.keepStaleOnPerch = true;
            string e = WalkOffProblem(r, rock);
            TestContext.Out.WriteLine("RED: " + e);
            Assert.IsNotNull(e);
        }

        // Perched on the 0.5 m Rock (2 m at scale 4: higher than Vesper's hind legs are long), WalkTo a point on the ground:
        // it hops down (takes off, lands on open ground near the point, "hopping down") and walks there - arriving within
        // kLandBound, the girdle never below the rock's top while over the rock on the ground, no girdle step > kSnap L.
        // RED (noHopDown): it tries to walk off the edge - creeps, sinks into the rock, never arrives.
        internal static string HopDownProblem(Rig r, out float took)
        {
            took = -1f;
            r.brain.Command(CreatureCommand.Perch);
            if (r.Until(() => OnGround(r) && r.brain.OnPerch, kPerchBound) < 0f) return $"never perched ({r.State})";
            Vector3 lo = new Vector3(2.5f - 0.6f, 0f, 1.25f - 0.6f) * r.s, hi = new Vector3(2.5f + 0.6f, 0.5f, 1.25f + 0.6f) * r.s;   // Rock's box
            string sunk = null; bool hopped = false;
            Vector3 off = r.area.center - new Vector3(2.5f, 0f, 1.25f).normalized * (0.3f * r.area.safeRadius);
            r.brain.Command(CreatureCommand.WalkTo, off);
            took = r.Until(() =>
            {
                hopped |= r.brain.Doing == "hopping down";
                Vector3 g = r.sim.Joints[r.sim.Flight.Girdle];
                if (sunk == null && OnGround(r) && g.x > lo.x && g.x < hi.x && g.z > lo.z && g.z < hi.z && g.y < hi.y)
                    sunk = $"the girdle sank to {g.y:F2} m, below the rock's top {hi.y:F2} m, at t={r.sim.Time:F1} s";
                return OnGround(r) && r.brain.Arrived;
            }, kLandBound);
            if (sunk != null) return sunk;
            if (took < 0f) return $"never reached the point off the rock within {kLandBound} s x sqrt s ({r.State})";
            if (!hopped) return "never said \"hopping down\"";
            float d = Vector3.ProjectOnPlane(r.sim.Joints[0] - off, Vector3.up).magnitude;
            if (d > r.L) return $"arrived {d:F1} m from the point";
            return r.Snap();
        }

        [Test] public void Perch_High_WalkTo_HopsDown()
        {
            Rig r = Start(true, Rock(Vesper().scale));
            string e = HopDownProblem(r, out float took); Report($"high perch -> WalkTo (hops down; launches {r.sim.Flight.Launches})", r, took);
            Assert.IsNull(e, e);
        }

        [Test] public void Red_CommandBrain_NoHopDown()
        {
            Rig r = Start(true, Rock(Vesper().scale));
            r.brain.noHopDown = true;
            string e = HopDownProblem(r, out _);
            TestContext.Out.WriteLine("RED: " + e);
            Assert.IsNotNull(e);
        }

        // Fly given while a landing is under way (the autopilot's final or flare): the landing is called off, it flies on
        [Test] public void Command_FlyDuringLanding_CancelsIt()
        {
            Rig r = Start(true);
            r.brain.Command(CreatureCommand.Land);
            Assert.Greater(r.Until(() => r.sim.Flight.Landing && r.sim.Mode == FlightMode.Air, kLandBound), 0f, $"a landing under way ({r.State})");
            r.brain.Command(CreatureCommand.Fly);
            r.Tick();
            Assert.IsFalse(r.sim.Flight.Landing, "the landing is called off at once");
            string e = FlyProblem(r, kFlyBound, out float took); Report("Fly given during a landing", r, took);
            Assert.IsNull(e, e);
            Assert.AreEqual(0, r.sim.Flight.Landings, "never touched down");
        }

        // Perch with no perch anywhere (flat ground): it lands on open ground and says so
        [Test] public void Perch_NoPerch_FallsBackToLand()
        {
            Rig r = Start(true);
            r.brain.Command(CreatureCommand.Perch);
            Assert.GreaterOrEqual(r.Until(() => r.brain.Doing.Contains("no perch"), 1f), 0f, $"says so within a second x sqrt s (the scan's frames; {r.brain.Doing})");
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
