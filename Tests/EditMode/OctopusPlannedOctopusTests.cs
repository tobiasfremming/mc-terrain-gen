using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // The tree octopus on the planned body (2026-10-20, after the swinger): PlannedSwing moves the body, the SwingSequencer
    // runs the locomotion with many arms - a multi-hold sling at rest, a pendulum on one hold while brachiating, idle arms
    // display-only (curled or trailing, OctopusBody posed arms), strikes and prey holds as sequencer arm uses.
    public class OctopusPlannedOctopusTests
    {
        const float Dt = 1f / 60f;

        internal static CephalopodSpec Spec()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("TreeOctopus");
            Assert.IsNotNull(sp, "Config/Creatures/TreeOctopus.asset");
            return sp.cephalopod;
        }
        internal static float SpeciesScale => CreatureKitAssets.Load("TreeOctopus").scale;

        // The octopus test grove (OctopusGroves.Row, sized to the octopus at scale s), spawned in the first crown.
        internal static OctopusController InGrove(float s, int layout, out OctopusBody body, out AnalyticSurfaces grove, int settle = 240)
        {
            grove = OctopusGroves.Row(4, s, 2.6f, layout);
            body = new OctopusBody(Spec(), new Vector3(0.6f, 1.6f, 0.3f) * s, Quaternion.identity, s);
            var c = new OctopusController(body, grove, null, (uint)layout) { Hints = new CapsuleHints(grove, 0.3f * s) };
            Assert.GreaterOrEqual(c.AnchorNearby(), 2, "the sling at spawn");
            for (int f = 0; f < settle; f++) c.Tick(Dt);
            return c;
        }

        static readonly float[] Sizes = { 0.9f, 1.1f };

        [Test] public void Asset_IsPlanned_EightArms()
        {
            CephalopodSpec sp = Spec();
            Assert.IsTrue(sp.Planned, "TreeOctopus.asset locomotion: planned");
            Assert.AreEqual(8, sp.ArmCount);
            Assert.IsFalse(CephalopodSpec.Default.Planned, "the emergent path stays the default (locomotion 0)");
            var body = new OctopusBody(sp, Vector3.zero, Quaternion.identity, 1f);
            var c = new OctopusController(body, new AnalyticSurfaces(), null, 1);
            Assert.IsNotNull(c.Sequencer);
            Assert.IsTrue(c.Sequencer.Sling);
        }

        // Resting in a crown it takes a multi-hold sling (2-4 holds), settles and falls asleep; then nothing moves.
        [Test] public void Rest_SlingSettlesAndSleeps()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float k in Sizes)
                for (int layout = 1; layout <= 2; layout++)
                {
                    float s = SpeciesScale * k;
                    OctopusController c = InGrove(s, layout, out OctopusBody body, out _, 0);
                    c.Intent = new OctoIntent { mode = OctoMode.Rest, goal = body.HeadCentre };
                    float sleptAt = -1f, ts = body.TimeScale;
                    for (int f = 0; f < (int)(60 * 12 * ts) && sleptAt < 0f; f++) { c.Tick(Dt); if (body.Asleep) sleptAt = f * Dt / ts; }
                    float maxArm = 0f, maxBody = 0f;
                    for (int f = 0; f < 120; f++)
                    {
                        Vector3 h0 = body.HeadCentre;
                        c.Tick(Dt);
                        maxBody = Mathf.Max(maxBody, (body.HeadCentre - h0).magnitude / Dt);
                        maxArm = Mathf.Max(maxArm, body.MaxArmSpeed);
                    }
                    int holds = c.Sequencer.Swing.HoldCount;
                    bool ok = sleptAt >= 0f && sleptAt < 9f && body.Asleep && holds >= 2 && holds <= 4 && maxBody < 0.01f * ts && maxArm < 0.02f * ts;
                    if (!ok) bad = true;
                    log.AppendLine($"scale {s:F2} layout {layout}: asleep at {sleptAt:F1} s/ts, holds {holds} (supports {c.Sequencer.Supports}), then body {maxBody:E1} arms {maxArm:E1} m/s, step {c.Sequencer.Mode}{(ok ? "" : "  <-- FAIL")}");
                }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, log.ToString());
        }

        // Brachiation through the grove of four on the branch hints: clearly visible pendulum swings, no jolts, never
        // from nothing, catches that hold; at 0.9x and 1.1x the species scale, 2 of 3 layouts arrive.
        [Test] public void Brachiates_AcrossGrove()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float k in Sizes)
            {
                float s = SpeciesScale * k;
                int arrived = 0;
                for (int layout = 1; layout <= 3; layout++)
                {
                    OctopusController c = InGrove(s, layout, out OctopusBody body, out _);
                    OctopusSwingerPlannedTests.Run r = OctopusSwingerPlannedTests.Travel(c, new Vector3(3f * 2.6f, 2.4f, 0f) * s, 50f);
                    SwingSequencer q = c.Sequencer;
                    if (r.arrivedAt >= 0f) arrived++;
                    float rate = q.CatchAttempts > 0 ? (float)q.Catches / q.CatchAttempts : 1f;
                    bool ok = r.minHolds >= 1 && rate >= 0.8f && r.maxAmplitude * Mathf.Rad2Deg < 70f && r.maxDv < 0.3f * body.TimeScale && r.maxBodyTurn < 240f && q.Falls == 0;
                    if (!ok) bad = true;
                    log.AppendLine($"scale {s:F2} layout {layout}: arrived {(r.arrivedAt >= 0f ? r.arrivedAt.ToString("F1") + " s/ts" : "never")}, transfers {q.Transfers}, catches {q.Catches}/{q.CatchAttempts}, falls {q.Falls}, fewest holds {r.minHolds}, " +
                                   $"amplitude max {r.maxAmplitude * Mathf.Rad2Deg:F0} deg, max dv {r.maxDv / body.TimeScale:F3} jerk {r.maxJerk / body.TimeScale:F3}, turn {r.maxBodyTurn:F0} deg/s, x {q.Pivot.x / s:F2}, step {q.Mode}{(ok ? "" : "  <-- FAIL")}");
                }
                if (arrived < 2) bad = true;
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, log.ToString());
        }

        // Every hold's pins lie on the real (here analytic) surface: an arm radius off it, not inside, not in the air.
        [Test] public void Catches_PinOnRealSurface()
        {
            float s = SpeciesScale;
            OctopusController c = InGrove(s, 1, out OctopusBody body, out AnalyticSurfaces grove);
            c.Intent = new OctoIntent { mode = OctoMode.Travel, goal = new Vector3(3f * 2.6f, 2.4f, 0f) * s, urgency = 0.6f };
            float worst = 0f; int pins = 0;
            for (int f = 0; f < 60 * 30 * body.TimeScale; f++)
            {
                c.Tick(Dt);
                if (f == (int)(60 * 20 * body.TimeScale)) c.Intent = new OctoIntent { mode = OctoMode.Rest, goal = body.HeadCentre };   // and the rest sling
                for (int a = 0; a < body.ArmCount; a++)
                {
                    if (c.Sequencer.Use[a] == SwingSequencer.ArmUse.Prey) continue;
                    TentacleChain ch = body.Arms[a];
                    for (int k = 1; k < ch.N; k++)
                    {
                        if (!ch.Pinned[k]) continue;
                        float rk = ch.Spec.RadiusAt(ch.Spec.Spacing * k) * ch.SizeScale;
                        worst = Mathf.Max(worst, Mathf.Abs(grove.Distance(ch.PinGoal[k], out _, out _) - rk));
                        pins++;
                    }
                }
            }
            TestContext.Out.WriteLine($"{pins} pin-ticks, worst off the surface {worst * 1000f:F2} mm; catches {c.Sequencer.Catches}, supports {c.Sequencer.Supports}");
            Assert.Greater(c.Sequencer.Catches, 3);
            Assert.Less(worst, 0.002f * s);
        }

        // Two bars over open ground (the brain tests' canopy, x s): the octopus hangs in its sling.
        static AnalyticSurfaces Canopy(float s) =>
            new AnalyticSurfaces().AddCapsule(new Vector3(-0.3f, 2.5f, -3f) * s, new Vector3(-0.3f, 2.5f, 3f) * s, 0.06f * s)
                                  .AddCapsule(new Vector3(0.3f, 2.5f, -3f) * s, new Vector3(0.3f, 2.5f, 3f) * s, 0.06f * s)
                                  .AddGround(0f);

        static OctopusController HuntRig(float s, out OctopusBody body, out OctopusBrain brain, out OctopusBrainTests.TestPrey prey, Vector3 at = default)
        {
            body = new OctopusBody(Spec(), new Vector3(0f, 2.0f, 0f) * s, Quaternion.identity, s);
            var c = new OctopusController(body, Canopy(s), null, 1);
            brain = new OctopusBrain(1); c.Brain = brain;
            Assert.GreaterOrEqual(c.AnchorNearby(), 2);
            for (int f = 0; f < 120; f++) c.Tick(Dt);
            prey = new OctopusBrainTests.TestPrey { Pos = body.HeadCentre + (at == default ? new Vector3(0.2f, -1.2f, 0.5f) : at) * s, Radius = 0.12f * s, Half = 0.3f * s };
            brain.Prey.Add(prey);
            return c;
        }

        // A still prey in reach below: the brain stalks, ambushes, strikes; an arm (an eased reach) takes it, wraps it,
        // holds it HoldSeconds and lets go; the events fire; the sling never drops below one hold. At 1x, 0.9x and
        // 1.1x the species scale.
        [Test] public void Hunt_Strike_HoldsPrey()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float s in new[] { 1f, SpeciesScale * 0.9f, SpeciesScale * 1.1f })
            {
                OctopusController c = HuntRig(s, out OctopusBody body, out OctopusBrain brain, out OctopusBrainTests.TestPrey prey);
                int strikes = 0, grabbed = 0, released = 0, minHolds = int.MaxValue;
                c.Events.Strike += _ => strikes++; c.Events.Grabbed += _ => grabbed++; c.Events.Released += _ => released++;
                float grabTime = -1f, releaseTime = -1f, worstGap = 0f; int held = 0;
                for (int f = 0; f < 60 * 10 * body.TimeScale; f++)
                {
                    if (prey.Grabbed > 0 && prey.Released == 0) prey.Pos += new Vector3(0.15f, 0f, 0f) * (Mathf.Sqrt(s) * Dt);   // it drags itself away
                    c.Tick(Dt);
                    minHolds = Mathf.Min(minHolds, c.Sequencer.Swing.HoldCount);
                    if (grabTime < 0f && prey.Grabbed > 0) grabTime = c.Time;
                    if (releaseTime < 0f && prey.Released > 0) releaseTime = c.Time;
                    for (int a = 0; a < body.ArmCount; a++)
                    {
                        if (c.Sequencer.Use[a] != SwingSequencer.ArmUse.Prey) continue;
                        held++;
                        prey.GrabVolume(out Vector3 pa, out Vector3 pb, out float r);
                        TentacleChain ch = body.Arms[a];
                        Vector3 tip = ch.X[ch.N - 1], ab = pb - pa, q = pa + ab * Mathf.Clamp01(Vector3.Dot(tip - pa, ab) / ab.sqrMagnitude);
                        worstGap = Mathf.Max(worstGap, (tip - q).magnitude - r);
                    }
                }
                bool ok = strikes > 0 && prey.Grabbed == 1 && grabbed == 1 && released == 1 && releaseTime > grabTime && Mathf.Abs(releaseTime - grabTime - c.HoldSeconds) < 0.3f
                          && held > 30 && worstGap < 0.08f * s && minHolds >= 1;
                if (!ok) bad = true;
                log.AppendLine($"scale {s:F2}: strikes {strikes} (arms {c.Sequencer.Strikes}, misses {c.Sequencer.StrikeMisses}), grabbed {grabbed} at {grabTime:F2} s, released at {releaseTime:F2} s, held {held} arm-frames, worst tip gap {worstGap / s:F3} x s, fewest holds {minHolds}, brain {brain.Mode}{(ok ? "" : "  <-- FAIL")}");
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, log.ToString());
        }

        // A prey that bolts the moment the strike starts: the strike misses (the arms draw back onto their poses), the
        // octopus retreats; nothing stalls.
        [Test] public void Hunt_Miss_Retreats()
        {
            float s = SpeciesScale;
            OctopusController c = HuntRig(s, out OctopusBody body, out OctopusBrain brain, out OctopusBrainTests.TestPrey prey, new Vector3(1.0f, -0.7f, 0.6f));
            brain.CanopyRegions.Add(new Vector3(-0.3f, 2.3f, -2.5f) * s); brain.CanopyRegions.Add(new Vector3(0.3f, 2.3f, 2.5f) * s);
            int bolt = -1;
            c.Events.Strike += _ => bolt = 2;   // it bolts the moment the arms are launched at it
            bool retreated = false; int strikeFrames = 0;
            for (int f = 0; f < 60 * 8 * body.TimeScale; f++)
            {
                if (bolt > 0 && --bolt == 0) { prey.Vel = new Vector3(0f, 0f, 12f) * Mathf.Sqrt(s); prey.Grabbable = false; }   // (and dodges the arms)
                prey.Step(Dt);
                c.Tick(Dt);
                if (brain.Mode == OctopusBrain.State.Retreat) retreated = true;
                for (int a = 0; a < body.ArmCount; a++) if (c.Sequencer.Use[a] == SwingSequencer.ArmUse.Strike) strikeFrames++;
            }
            TestContext.Out.WriteLine($"missed {brain.MissedStrikes}, arm strikes {c.Sequencer.Strikes} (misses {c.Sequencer.StrikeMisses}), strike arm-frames {strikeFrames}, grabbed {prey.Grabbed}, brain {brain.Mode}, step {c.Sequencer.Mode}");
            Assert.Greater(brain.MissedStrikes, 0, "no miss");
            Assert.AreEqual(0, prey.Grabbed);
            Assert.IsTrue(retreated, "did not retreat");
            Assert.Greater(c.Sequencer.Strikes, 0, "no arm struck");
            for (int a = 0; a < body.ArmCount; a++) Assert.AreNotEqual(SwingSequencer.ArmUse.Strike, c.Sequencer.Use[a], "an arm still striking");
        }

        // Two minutes of life in the grove with its brain (rest in the sling, trips to other crowns, play): it travels
        // and arrives, sleeps between trips, never hangs from nothing, never falls, the swing stays under 65 deg, catches
        // hold, nothing turns NaN, the body stays out of the wood, an idle (posed) arm is rarely inside it. 0.9x and 1.1x.
        [Test] public void Life_Grove_TripsRestsPlays()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float k in Sizes)
            {
                float s = SpeciesScale * k;
                OctopusController c = InGrove(s, 2, out OctopusBody body, out AnalyticSurfaces grove);
                var brain = new OctopusBrain(2); c.Brain = brain;
                for (int t = 0; t < 4; t++) brain.CanopyRegions.Add(new Vector3(t * 2.6f, 2.5f, 0f) * s);
                SwingSequencer q = c.Sequencer;
                int T = (int)(60 * 120 * body.TimeScale), minHolds = int.MaxValue, asleep = 0, inside = 0, samples = 0, bodyIn = 0, bodySamples = 0, maxHolds = 0;
                float maxAmp = 0f; bool nan = false;
                for (int f = 0; f < T; f++)
                {
                    c.Tick(Dt);
                    maxAmp = Mathf.Max(maxAmp, q.Swing.Amplitude);
                    if (!c.Board.Grounded && q.Mode != SwingSequencer.Step.Fall) minHolds = Mathf.Min(minHolds, q.Swing.HoldCount);
                    maxHolds = Mathf.Max(maxHolds, q.Swing.HoldCount);
                    if (body.Asleep) asleep++;
                    for (int a = 0; a < body.ArmCount; a++) if (!OctoMath.Finite(body.Arms[a].X[body.Arms[a].N - 1])) nan = true;
                    if (!OctoMath.Finite(body.HeadCentre)) nan = true;
                    if (f % 10 != 0) continue;
                    for (int i = 0; i < SoftLattice.Count; i++) { bodySamples++; if (grove.Distance(body.Lattice.X[i], out _, out _) < 0f) bodyIn++; }
                    for (int a = 0; a < body.ArmCount; a++)
                    {
                        if (q.Use[a] != SwingSequencer.ArmUse.Free) continue;
                        TentacleChain ch = body.Arms[a];
                        for (int j = 1; j < ch.N; j++) { samples++; if (grove.Distance(ch.X[j], out _, out _) < -0.5f * ch.Spec.RadiusAt(ch.Spec.Spacing * j) * s) inside++; }
                    }
                }
                float rate = q.CatchAttempts > 0 ? (float)q.Catches / q.CatchAttempts : 1f, through = samples > 0 ? inside / (float)samples : 0f;
                bool ok = !nan && brain.Arrivals >= 2 && q.Transfers >= 6 && asleep > 0 && maxAmp * Mathf.Rad2Deg < 65f && minHolds >= 1 && rate >= 0.85f && q.Falls == 0
                          && bodyIn < 0.005f * bodySamples && through < 0.03f && maxHolds <= 4;
                if (!ok) bad = true;
                log.AppendLine($"scale {s:F2}: trips {brain.Trips}, arrived {brain.Arrivals}, play sessions {brain.PlaySessions} moves {brain.PlayMoves}, transfers {q.Transfers}, catches {q.Catches}/{q.CatchAttempts}, supports {q.Supports}, grooms {q.Grooms}, falls {q.Falls}, " +
                               $"asleep {asleep / (float)T:P0}, sleeps {q.Sleeps}, max amplitude {maxAmp * Mathf.Rad2Deg:F0} deg, holds {minHolds}..{maxHolds}, idle arm particles in wood {through:P2}, body {bodyIn / (float)Mathf.Max(1, bodySamples):P2}, step {q.Mode}{(ok ? "" : "  <-- FAIL")}");
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, log.ToString());
        }

        // Calm arms (user 2026-10-19: "just flapping their tentacles ... too much random stuff"): in a life in the grove
        // (OctopusCalmTests.Life: rest, trips, a hunt from halfway) the idle arms' tips, relative to the body, stay as
        // slow as the planned swinger's (p95 <= PlannedFreeP95 L/s x ts); no sinus, no feeling about.
        [Test] public void Calm_Life_IdleArmsBounded()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float k in Sizes)
                for (int layout = 1; layout <= 2; layout++)
                {
                    var meter = new OctopusCalmTests.Meter();
                    float s = SpeciesScale * k;
                    float swings = OctopusCalmTests.Life(Spec(), s, s, layout, 1.5f, meter);
                    float p95 = OctopusCalmTests.Meter.Pct(meter.jobless, 0.95f);
                    bool ok = p95 <= OctopusCalmTests.PlannedFreeP95 && meter.SinusShare == 0f && meter.IdleFeelShare == 0f;
                    if (!ok) bad = true;
                    log.AppendLine($"scale {s:F2} layout {layout}: {meter}; transfers {swings:F1}/min{(ok ? "" : "  <-- FAIL")}");
                }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, log.ToString());
        }

        // Same seed, same run (a life with its brain): bit-identical.
        [Test] public void Deterministic()
        {
            var end = new Vector3[2];
            for (int run = 0; run < 2; run++)
            {
                float s = SpeciesScale;
                var grove = OctopusGroves.Row(4, s, 2.6f, 3);
                var body = new OctopusBody(Spec(), new Vector3(0.6f, 1.6f, 0.3f) * s, Quaternion.identity, s);
                var c = new OctopusController(body, grove, null, 3) { Hints = new CapsuleHints(grove, 0.3f * s) };
                var brain = new OctopusBrain(3); c.Brain = brain;
                for (int t = 0; t < 4; t++) brain.CanopyRegions.Add(new Vector3(t * 2.6f, 2.5f, 0f) * s);
                c.AnchorNearby();
                for (int f = 0; f < 60 * 40; f++) c.Tick(Dt);
                end[run] = body.Arms[2].X[body.Arms[2].N - 1] + body.Lattice.X[OctopusShape.Apex] + body.Arms[5].X[9] * 0.5f;
            }
            TestContext.Out.WriteLine($"end {end[0]:F5}");
            Assert.AreEqual(end[0].x, end[1].x); Assert.AreEqual(end[0].y, end[1].y); Assert.AreEqual(end[0].z, end[1].z);
        }

        // An individual's size spread (0.9x / 1.1x the species scale): the same trip across the grove takes the same
        // number of swings and about the same sqrt-scaled time.
        [Test] public void SizeVariation_SameTrip()
        {
            var log = new StringBuilder();
            int[] transfers = new int[2]; float[] at = new float[2];
            for (int i = 0; i < 2; i++)
            {
                float s = SpeciesScale * Sizes[i];
                OctopusController c = InGrove(s, 1, out OctopusBody body, out _);
                OctopusSwingerPlannedTests.Run r = OctopusSwingerPlannedTests.Travel(c, new Vector3(3f * 2.6f, 2.4f, 0f) * s, 50f);
                transfers[i] = c.Sequencer.Transfers; at[i] = r.arrivedAt;
                log.AppendLine($"x{Sizes[i]} (scale {s:F2}): arrived {r.arrivedAt:F1} s/ts, transfers {transfers[i]}, catches {c.Sequencer.Catches}/{c.Sequencer.CatchAttempts}");
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.Greater(at[0], 0f); Assert.Greater(at[1], 0f);
            Assert.LessOrEqual(Mathf.Abs(transfers[0] - transfers[1]), 1, log.ToString());
            Assert.AreEqual(at[0], at[1], 0.15f * at[0], log.ToString());
        }

        // Zero GC per tick: resting (after the first sleep), hunting and holding prey, travelling; the bones written.
        [Test] public void NoGc()
        {
            GcAllocs.AssertCounterWorks();
            float s = SpeciesScale;
            OctopusController c = HuntRig(s, out OctopusBody body, out OctopusBrain brain, out OctopusBrainTests.TestPrey prey);
            brain.Prey.Remove(prey);
            var bones = new Matrix4x4[body.BoneCount];
            bool slept = false;
            for (int f = 0; f < 60 * 30 && !slept; f++) { c.Tick(Dt); body.WriteBoneWorld(bones, 0); slept = body.Asleep; }
            Assert.IsTrue(slept, "the warm-up rest never slept");
            brain.Prey.Add(prey);
            int allocs = GcAllocs.Count(() => { for (int f = 0; f < 60 * 12; f++) { c.Tick(Dt); body.WriteBoneWorld(bones, 0); } });
            OctopusController t = InGrove(s, 1, out OctopusBody tb, out _);
            t.Intent = new OctoIntent { mode = OctoMode.Travel, goal = new Vector3(3f * 2.6f, 2.4f, 0f) * s, urgency = 0.6f };
            var tbones = new Matrix4x4[tb.BoneCount];
            for (int f = 0; f < 60; f++) t.Tick(Dt);
            int allocsT = GcAllocs.Count(() => { for (int f = 0; f < 60 * 12; f++) { t.Tick(Dt); tb.WriteBoneWorld(tbones, 0); } });
            TestContext.Out.WriteLine($"allocations: hunt {allocs} (grabs {prey.Grabbed}), travel {allocsT} (transfers {t.Sequencer.Transfers})");
            Assert.AreEqual(0, allocs);
            Assert.AreEqual(0, allocsT);
            Assert.Greater(prey.Grabbed, 0, "the run caught nothing");
            Assert.Greater(t.Sequencer.Transfers, 0, "the run swung nowhere");
        }

        // Cost per tick (median, Stopwatch around the real tick) <= the emergent path's in the same life (travel, then
        // rest in the grove), both measured in one session.
        [Test] public void Cost_PerTick_NotAboveEmergent()
        {
            float s = SpeciesScale;
            Median(Spec(), s); Median(CephalopodSpec.Default, s);   // (warm-up: JIT)
            double planned = Median(Spec(), s), emergent = Median(CephalopodSpec.Default, s);
            TestContext.Out.WriteLine($"median tick: planned {planned * 1000:F3} ms, emergent {emergent * 1000:F3} ms ({planned / emergent:P0})");
            Assert.LessOrEqual(planned, emergent);
        }

        static double Median(CephalopodSpec spec, float s)
        {
            var grove = OctopusGroves.Row(4, s, 2.6f, 1);
            var body = new OctopusBody(spec, new Vector3(0.6f, 1.6f, 0.3f) * s, Quaternion.identity, s);
            var c = new OctopusController(body, grove, null, 1) { Hints = new CapsuleHints(grove, 0.3f * s) };
            c.AnchorNearby();
            for (int f = 0; f < 120; f++) c.Tick(Dt);
            c.Intent = new OctoIntent { mode = OctoMode.Travel, goal = new Vector3(2.6f, 2.4f, 0f) * s, urgency = 0.6f };
            var t = new double[60 * 30];
            var sw = new System.Diagnostics.Stopwatch();
            for (int f = 0; f < t.Length; f++)
            {
                if (f == t.Length / 2) c.Intent = new OctoIntent { mode = OctoMode.Rest, goal = body.HeadCentre };
                sw.Restart(); c.Tick(Dt); sw.Stop();
                t[f] = sw.Elapsed.TotalSeconds;
            }
            System.Array.Sort(t);
            return t[t.Length / 2];
        }

        // The skinned mesh in real planned poses - the sling, swings, curled and trailing idle arms, grooming, a strike
        // and a held prey: no arm tube, mantle, eye or siphon triangle inverts. The thin web (part 5) between the arm
        // bases may fold for a moment where an arm turns ~90-150 deg at its base toward a hold right over the crown (the
        // arms' rest directions are level; the web is linear-blend skinned on their oral side), now and then for a few
        // triangles. The emergent path, same grove and brain (the reference run
        // here): 98 % of the poses, up to 6 % of the web (2026-10-20). Bound: at most one folded web triangle a pose on
        // average, never more than 0.5 % of the web. The drawn holds stay on their pins (display within 1 cm of the
        // simulated pin in >= 97 % of the samples).
        [Test] public void Mesh_PlannedPoses_NoInvertedTris()
        {
            CephalopodSpec sp = Spec();
            OctopusMeshBuilder.Data d = OctopusMeshBuilder.BuildData(sp);
            Matrix4x4[] bind = OctopusRestPose.BindPoses(sp);
            int webTris = 0;
            for (int k = 0; k < d.tris.Count; k += 3) if (Mathf.RoundToInt(d.uv1[d.tris[k]].x) == OctopusMeshBuilder.PartWeb) webTris++;
            var log = new StringBuilder(); bool bad = false;
            for (int run = 0; run < 3; run++)
            {
                OctopusController c;
                OctopusBody body;
                if (run == 1) c = HuntRig(1f, out body, out _, out _);
                else
                {
                    // run 0: the planned octopus in the grove; run 2: the emergent one there (reference only)
                    var grove = OctopusGroves.Row(4, 1f, 2.6f, 2);
                    body = new OctopusBody(run == 0 ? sp : CephalopodSpec.Default, new Vector3(0.6f, 1.6f, 0.3f), Quaternion.identity, 1f);
                    c = new OctopusController(body, grove, null, 2) { Hints = new CapsuleHints(grove, 0.3f) };
                    var brain = new OctopusBrain(2); c.Brain = brain;
                    for (int t = 0; t < 4; t++) brain.CanopyRegions.Add(new Vector3(t * 2.6f, 2.5f, 0f));
                    c.AnchorNearby();
                    for (int f = 0; f < 240; f++) c.Tick(Dt);
                }
                SwingSequencer q = c.Sequencer;
                var world = new Matrix4x4[body.BoneCount]; var skin = new Matrix4x4[body.BoneCount];
                int worstWeb = 0, worstOther = 0, worstArm = 0, frames = 0, webPoses = 0, webSum = 0, pinAll = 0, pinBad = 0;
                string firstWeb = null, firstOther = null, firstArm = null;
                for (int f = 0; f < 60 * (run == 1 ? 8 : 60); f++)
                {
                    c.Tick(Dt);
                    if (f % 15 != 0) continue;
                    frames++;
                    body.WriteBoneWorld(world, 0);
                    if (q != null)
                        for (int a = 0; a < body.ArmCount; a++)
                        {
                            if (q.Use[a] != SwingSequencer.ArmUse.Hold) continue;
                            TentacleChain ch = body.Arms[a];
                            for (int k = 1; k < ch.N; k++) if (ch.Pinned[k]) { pinAll++; if ((ch.DisplayX[k] - ch.X[k]).magnitude > 0.01f) pinBad++; }
                        }
                    for (int b = 0; b < world.Length; b++) skin[b] = OctoMath.MulAffine(world[b], bind[b]);
                    OctopusMeshTests.CountInverted(d, skin, bind, out string fi, out int[] perPart);
                    int arm = perPart[OctopusMeshBuilder.PartArm], web = perPart[OctopusMeshBuilder.PartWeb], other = 0;
                    for (int i = 0; i < perPart.Length; i++) if (i != OctopusMeshBuilder.PartArm && i != OctopusMeshBuilder.PartWeb) other += perPart[i];
                    string at = q != null ? $"t {c.Time:F1} step {q.Mode} uses {string.Join(",", q.Use)}" : $"t {c.Time:F1}";
                    if (web > 0) webPoses++;
                    webSum += web;
                    if (web > worstWeb) { worstWeb = web; firstWeb = $"{at} {fi}"; }
                    if (other > worstOther) { worstOther = other; firstOther = $"{at} {fi}"; }
                    if (arm > worstArm) { worstArm = arm; firstArm = $"{at} {fi}"; }
                }
                string name = run == 0 ? "planned, grove life" : run == 1 ? "planned, hunt" : "emergent, grove life (reference)";
                bool ok = run == 2 || worstArm == 0 && worstOther == 0 && webSum <= frames && worstWeb <= 0.005f * webTris && pinBad <= 0.03f * Mathf.Max(1, pinAll);
                if (!ok) bad = true;
                log.AppendLine($"{name}: {frames} poses; web folds in {webPoses} ({webPoses / (float)frames:P0}), {webSum / (float)frames:F2} triangles a pose, worst {worstWeb} of {webTris} ({worstWeb / (float)webTris:P1}) {firstWeb}; " +
                               $"body/eyes/siphon {worstOther} {firstOther}; arm tubes {worstArm} {firstArm}; held pins drawn > 1 cm off {pinBad}/{pinAll}{(ok ? "" : "  <-- FAIL")}");
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, log.ToString());
        }

        // Retargeting (as the swinger's): a prey out on the open ground, far from every hold, is skipped (ignored for a
        // while); a prey it can strike from a hold, appearing then, is hunted and taken (the skipped one never is).
        [Test] public void Retarget_HuntsReachable_SkipsOpenGround()
        {
            float s = SpeciesScale;
            OctopusController c = HuntRig(s, out OctopusBody body, out OctopusBrain brain, out OctopusBrainTests.TestPrey near, new Vector3(2.6f, -1.6f, 0f));
            near.Pos = new Vector3(2.6f, 0.12f, 0f) * s;   // on the ground, far from the bars
            // first the open-ground one alone (it is skipped), then the reachable one appears (it is taken)
            int nearGrabs = 0;
            for (int f = 0; f < 60 * 10 * body.TimeScale && brain.HuntsSkipped + brain.GiveUps == 0; f++) { c.Tick(Dt); nearGrabs = near.Grabbed; }
            var reach = new OctopusBrainTests.TestPrey { Pos = new Vector3(0.3f, 0.12f, 1.9f) * s, Radius = 0.12f * s, Half = 0.3f * s };
            brain.Prey.Add(reach);
            for (int f = 0; f < 60 * 30 * body.TimeScale && reach.Grabbed == 0; f++) { c.Tick(Dt); nearGrabs = Mathf.Max(nearGrabs, near.Grabbed); }
            TestContext.Out.WriteLine($"skipped {brain.HuntsSkipped} (last: {brain.Unreachable}), given up {brain.GiveUps}, open-ground grabs {nearGrabs}, reachable grabs {reach.Grabbed}, brain {brain.Mode}, transfers {c.Sequencer.Transfers}");
            Assert.Greater(brain.HuntsSkipped + brain.GiveUps, 0, "the open-ground prey was not skipped");
            Assert.AreEqual(0, nearGrabs);
            Assert.Greater(reach.Grabbed, 0, "the reachable prey was not taken");
        }

        // A posed arm (display only) follows its body-space pose with a lag that grows toward the tip and is bounded,
        // and settles exactly on it when the body stops.
        [Test] public void Body_PosedArm_FollowsPose_Settles()
        {
            foreach (float s in new[] { 1f, 3.1f })
            {
                CephalopodSpec sp = CephalopodSpec.Default; sp.locomotion = 1;
                var body = new OctopusBody(sp, Vector3.zero, Quaternion.identity, s);
                body.EnablePlanned();
                TentacleChain ch = body.Arms[3];
                Vector3 r0 = OctopusShape.ArmRoot(sp, 3) * s, d = OctopusShape.ArmDir(sp, 3);
                for (int k = 0; k < ch.N; k++) body.ArmPoseLocal[3][k] = r0 + (d * 0.5f + Vector3.up * 0.8f).normalized * (k * ch.Spacing);
                body.ArmPosed[3] = true;
                Vector3 pivot = new Vector3(0f, 3f, 0f) * s;
                float worst = 0f;
                int T = (int)(240 * Mathf.Sqrt(s));
                for (int f = 0; f < T; f++)
                {
                    float t = f * Dt, a = 0.6f * Mathf.Sin(2f * Mathf.PI * t / (2f * Mathf.Sqrt(s)));
                    Vector3 pos = pivot + new Vector3(Mathf.Sin(a), -Mathf.Cos(a), 0f) * s;
                    Quaternion rot = OctoMath.Look(Vector3.forward, OctoMath.Norm(pos - pivot, Vector3.down));
                    body.SetPlannedPose(pos, rot, f == 0);
                    body.Tick(Dt);
                    if (body.ArmPose[3] < 1f) continue;
                    for (int k = 1; k < ch.N; k++)
                        worst = Mathf.Max(worst, (ch.X[k] - (pos + rot * body.ArmPoseLocal[3][k])).magnitude / ch.Length);
                }
                Vector3 still = body.PlanOrigin; Quaternion sr = body.PlanRotation;
                float tipSpeed = 0f;
                for (int f = 0; f < 180 * Mathf.Sqrt(s); f++) { Vector3 p0 = ch.X[ch.N - 1]; body.SetPlannedPose(still, sr); body.Tick(Dt); tipSpeed = (ch.X[ch.N - 1] - p0).magnitude / Dt; }
                float settle = (ch.X[ch.N - 1] - (still + sr * body.ArmPoseLocal[3][ch.N - 1])).magnitude;
                TestContext.Out.WriteLine($"scale {s}: worst lag {worst:F3} L, settled tip {settle * 1000f:F2} mm, tip speed {tipSpeed:E1}");
                Assert.AreEqual(1f, body.ArmPose[3]);
                Assert.Less(worst, OctopusBody.PoseLagMax + 1e-3f);
                Assert.Greater(worst, 0.005f, "no lag at all: rigid");
                Assert.Less(settle, 1e-3f * s);
                Assert.Less(tipSpeed, 0.01f * s);
            }
        }

    }
}
