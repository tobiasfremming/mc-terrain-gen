using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // The swinger on its planned body (planned-body rework, docs/superpowers/specs/2026-10-19-planned-body-tentacle-
    // rework-design.md): PlannedSwing moves the body, SwingSequencer runs the locomotion (hold -> pump -> reach -> catch
    // -> transfer, rest, creep, perch). Analytic bars and groves (OctopusTestSurfaces); lengths x scale, times x sqrt.
    public class OctopusSwingerPlannedTests
    {
        const float Dt = 1f / 60f;

        internal static OctopusController OnBars(float s, uint seed, out OctopusBody body, out AnalyticSurfaces bars, int n = 8)
        {
            bars = OctopusSwingTests.Bars(s, 0.9f, n);
            return OctopusSwingerTests.Rig(OctopusSwingerTests.Spec(), bars, new Vector3(0.45f + 0.03f * seed, 2.06f, 0.02f), s, seed, out body);
        }

        // Per-tick motion record of a run: crown speed change (jolts), fewest holds, the swing's amplitude.
        internal struct Run
        {
            public float maxDv, maxJerk, maxAmplitude, maxBodyTurn; public int minHolds; public float arrivedAt;
        }

        internal static Run Travel(OctopusController c, Vector3 goal, float seconds, bool stopAtArrival = true)
        {
            OctopusBody body = c.Body;
            SwingSequencer q = c.Sequencer;
            c.Intent = new OctoIntent { mode = OctoMode.Travel, goal = goal, urgency = 0.6f };
            var r = new Run { minHolds = int.MaxValue, arrivedAt = -1f };
            int T = (int)(60 * seconds * body.TimeScale);
            Vector3 pv = q.Swing.Velocity, pdv = Vector3.zero; Quaternion pr = body.PlanRotation;
            for (int f = 0; f < T; f++)
            {
                c.Tick(Dt);
                Vector3 dv = q.Swing.Velocity - pv; pv = q.Swing.Velocity;
                r.maxDv = Mathf.Max(r.maxDv, dv.magnitude);
                if (f > 0) r.maxJerk = Mathf.Max(r.maxJerk, (dv - pdv).magnitude);
                pdv = dv;
                r.maxBodyTurn = Mathf.Max(r.maxBodyTurn, Quaternion.Angle(pr, body.PlanRotation) / Dt); pr = body.PlanRotation;
                r.maxAmplitude = Mathf.Max(r.maxAmplitude, q.Swing.Amplitude);
                if (!c.Board.Grounded) r.minHolds = Mathf.Min(r.minHolds, c.Board.AnchoredCount);
                if (r.arrivedAt < 0f && q.Arrived) { r.arrivedAt = f * Dt / body.TimeScale; if (stopAtArrival) break; }
            }
            return r;
        }

        // Hanging at rest it settles and falls asleep: body and arms still (below a small bound), then frozen.
        [Test] public void Rest_SettlesAndSleeps()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float s in new[] { 1.2f, 1.8f })
            {
                OctopusController c = OnBars(s, 1, out OctopusBody body, out _);
                Assert.GreaterOrEqual(c.AnchorNearby(), 1, "takes hold at spawn");
                c.Intent = new OctoIntent { mode = OctoMode.Rest, goal = body.HeadCentre };
                float sleptAt = -1f, ts = body.TimeScale;
                for (int f = 0; f < (int)(60 * 10 * ts) && sleptAt < 0f; f++) { c.Tick(Dt); if (body.Asleep) sleptAt = f * Dt; }
                Vector3 head = body.HeadCentre, tip = body.Arms[1].X[body.Arms[1].N - 1];
                float maxArm = 0f, maxBody = 0f;
                for (int f = 0; f < 120; f++)
                {
                    Vector3 h0 = body.HeadCentre;
                    c.Tick(Dt);
                    maxBody = Mathf.Max(maxBody, (body.HeadCentre - h0).magnitude / Dt);
                    maxArm = Mathf.Max(maxArm, body.MaxArmSpeed);
                }
                bool ok = sleptAt >= 0f && sleptAt < 7f * ts && body.Asleep && maxBody < 0.01f * ts && maxArm < 0.02f * ts;
                if (!ok) bad = true;
                log.AppendLine($"scale {s}: asleep at {sleptAt:F1} s, then body {maxBody:E1} m/s, arms {maxArm:E1} m/s, step {c.Sequencer.Mode}{(ok ? "" : "  <-- FAIL")}");
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, log.ToString());
        }

        // Hand over hand along the bars to a goal 7 bars away: clearly visible swings (amplitude 20-70 deg), no
        // jolts (the crown's velocity changes per frame no more than the swing's own acceleration, < 0.3 sqrt(s)
        // m/s ~ 1.8 g, and its acceleration steps only at a hand switch, < 0.4 sqrt(s) m/s per frame; the body turns
        // < 240 deg/s), never hanging from nothing, every catch attempt holds (>= 90 %), at 1x and 1.5x the
        // species scale and two seeds.
        [Test] public void Brachiates_HandOverHand_Bars()
        {
            var log = new StringBuilder(); bool bad = false;
            float sp = CreatureKitAssets.Load("Swinger").scale;
            foreach (float k in new[] { 1f, 1.5f })
                for (uint seed = 1; seed <= 2; seed++)
                {
                    float s = sp * k;
                    OctopusController c = OnBars(s, seed, out OctopusBody body, out _);
                    c.AnchorNearby();
                    for (int f = 0; f < 60; f++) c.Tick(Dt);
                    Vector3 goal = new Vector3(0.9f * 7f, 2f, 0f) * s;
                    Run r = Travel(c, goal, 40f);
                    SwingSequencer q = c.Sequencer;
                    float dx = q.Pivot.x / s, rate = q.CatchAttempts > 0 ? (float)q.Catches / q.CatchAttempts : 0f;
                    bool ok = r.arrivedAt >= 0f && r.minHolds >= 1 && q.Transfers >= 5 && rate >= 0.9f
                              && r.maxAmplitude * Mathf.Rad2Deg > 20f && r.maxAmplitude * Mathf.Rad2Deg < 70f && r.maxDv < 0.3f * body.TimeScale && r.maxJerk < 0.4f * body.TimeScale && r.maxBodyTurn < 240f;
                    if (!ok) bad = true;
                    log.AppendLine($"scale {s:F2} seed {seed}: arrived {r.arrivedAt:F1} s/ts, transfers {q.Transfers}, catches {q.Catches}/{q.CatchAttempts}, misses {q.Misses}, " +
                                   $"fewest holds {r.minHolds}, amplitude max {r.maxAmplitude * Mathf.Rad2Deg:F0} deg, max dv {r.maxDv / body.TimeScale:F3} jerk {r.maxJerk / body.TimeScale:F3}, turn {r.maxBodyTurn:F0} deg/s, pivot x {dx:F2}, step {q.Mode}{(ok ? "" : "  <-- FAIL")}");
                }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, log.ToString());
        }

        // Every catch's pins lie on the real (here analytic) surface: an arm radius off it, not inside, not in the air.
        [Test] public void Catches_PinOnRealSurface()
        {
            float s = CreatureKitAssets.Load("Swinger").scale;
            OctopusController c = OnBars(s, 1, out OctopusBody body, out AnalyticSurfaces bars);
            c.AnchorNearby();
            c.Intent = new OctoIntent { mode = OctoMode.Travel, goal = new Vector3(0.9f * 7f, 2f, 0f) * s, urgency = 0.6f };
            float worst = 0f; int checkedPins = 0;
            for (int f = 0; f < 60 * 20; f++)
            {
                c.Tick(Dt);
                for (int a = 0; a < body.ArmCount; a++)
                {
                    TentacleChain ch = body.Arms[a];
                    for (int k = 1; k < ch.N; k++)
                    {
                        if (!ch.Pinned[k]) continue;
                        float rk = ch.Spec.RadiusAt(ch.Spec.Spacing * k) * ch.SizeScale;
                        worst = Mathf.Max(worst, Mathf.Abs(bars.Distance(ch.PinGoal[k], out _, out _) - rk));
                        checkedPins++;
                    }
                }
            }
            TestContext.Out.WriteLine($"{checkedPins} pin-ticks, worst off the surface {worst * 1000f:F2} mm; catches {c.Sequencer.Catches}");
            Assert.Greater(c.Sequencer.Catches, 2);
            Assert.Less(worst, 0.002f * s);
        }

        // Purposeful brachiation through the grove of four on the branch hints (route): at 1x and 1.5x, 2 of 3 layouts arrive.
        [Test] public void Brachiates_AcrossGrove()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float s in new[] { 1f, 1.5f })
            {
                int arrived = 0;
                for (int layout = 1; layout <= 3; layout++)
                {
                    OctopusController c = OctopusSwingerTests.InGrove(OctopusSwingerTests.Spec(), s, layout, out OctopusBody body, out _);
                    float g = OctopusSwingerTests.GroveFit * s;
                    Run r = Travel(c, new Vector3(3f * 2.6f, 2.4f, 0f) * g, 40f);
                    if (r.arrivedAt >= 0f) arrived++;
                    if (r.minHolds < 1) bad = true;
                    log.AppendLine($"scale {s} layout {layout}: arrived {(r.arrivedAt >= 0f ? r.arrivedAt.ToString("F1") + " s/ts" : "never")}, transfers {c.Sequencer.Transfers}, catches {c.Sequencer.Catches}/{c.Sequencer.CatchAttempts}, fewest holds {r.minHolds}, x {c.Sequencer.Pivot.x / g:F2}, step {c.Sequencer.Mode}");
                }
                if (arrived < 2) bad = true;
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, log.ToString());
        }

        // Two minutes of life in the grove with its brain (rest, explore trips to other crowns): it travels and
        // arrives, rests between trips and falls asleep there, the swing never exceeds 65 deg, it never hangs from
        // nothing, every catch attempt but a few holds, nothing turns NaN, a free arm stays out of the wood (< 1 % of its
        // particle samples deeper than half its radius). At 1x and 1.5x.
        [Test] public void Life_Grove_TripsAndRests()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float s in new[] { 1f, 1.5f })
            {
                float g = OctopusSwingerTests.GroveFit * s;
                OctopusController c = OctopusSwingerTests.InGrove(OctopusSwingerTests.Spec(), s, 2, out OctopusBody body, out AnalyticSurfaces grove);
                var brain = new OctopusBrain(2); c.Brain = brain;
                int inside = 0, samples = 0, bodyIn = 0, bodySamples = 0;
                for (int t = 0; t < 4; t++) brain.CanopyRegions.Add(new Vector3(t * 2.6f, 2.5f, 0f) * g);
                SwingSequencer q = c.Sequencer;
                int T = (int)(60 * 120 * body.TimeScale), minHolds = int.MaxValue, asleep = 0;
                float maxAmp = 0f; bool nan = false;
                for (int f = 0; f < T; f++)
                {
                    c.Tick(Dt);
                    maxAmp = Mathf.Max(maxAmp, q.Swing.Amplitude);
                    if (!c.Board.Grounded && q.Mode != SwingSequencer.Step.Fall) minHolds = Mathf.Min(minHolds, c.Board.AnchoredCount);
                    if (body.Asleep) asleep++;
                    if (!OctoMath.Finite(body.HeadCentre) || !OctoMath.Finite(body.Arms[0].X[body.Arms[0].N - 1])) nan = true;
                    if (f % 10 == 0)
                        for (int i = 0; i < SoftLattice.Count; i++) { bodySamples++; if (grove.Distance(body.Lattice.X[i], out _, out _) < 0f) bodyIn++; }
                    if (f % 10 == 0)
                        for (int a = 0; a < body.ArmCount; a++)
                        {
                            if (q.Use[a] != SwingSequencer.ArmUse.Free) continue;
                            TentacleChain ch = body.Arms[a];
                            for (int k = 1; k < ch.N; k++) { samples++; if (grove.Distance(ch.X[k], out _, out _) < -0.5f * ch.Spec.RadiusAt(ch.Spec.Spacing * k) * s) inside++; }
                        }
                }
                float rate = q.CatchAttempts > 0 ? (float)q.Catches / q.CatchAttempts : 1f;
                float through = samples > 0 ? inside / (float)samples : 0f;
                bool ok = through < 0.01f && bodyIn < 0.005f * bodySamples && !nan && brain.Arrivals >= 2 && q.Transfers >= 6 && asleep > 0 && maxAmp * Mathf.Rad2Deg < 65f && minHolds >= 1 && rate >= 0.85f && q.Falls == 0;
                if (!ok) bad = true;
                log.AppendLine($"scale {s}: trips {brain.Trips}, arrived {brain.Arrivals}, transfers {q.Transfers}, catches {q.Catches}/{q.CatchAttempts}, falls {q.Falls}, asleep {asleep / (float)T:P0} of the time, sleeps {q.Sleeps}, " +
                               $"max amplitude {maxAmp * Mathf.Rad2Deg:F0} deg, free arm particles inside wood {through:P2}, body (lattice) {bodyIn / (float)Mathf.Max(1, bodySamples):P2}, fewest holds {minHolds}, step {q.Mode}{(ok ? "" : "  <-- FAIL")}");
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, log.ToString());
        }

        // A spider on a trunk (no swing hold right over it - a trunk is no hold): the swinger swings close along the
        // bars, creeps to the nearest hold, pulls itself up there and eats it with a mouth tentacle. At 1x and 1.5x.
        [Test] public void Feeds_OnTrunkPrey()
        {
            var log = new StringBuilder(); bool bad = false;
            float sp = CreatureKitAssets.Load("Swinger").scale;
            foreach (float k in new[] { 1f, 1.5f })
            {
                float s = sp * k;
                AnalyticSurfaces surf = OctopusSwingTests.Bars(s, 0.9f, 6);
                surf.AddCapsule(new Vector3(3.2f, -5f, 0.15f) * s, new Vector3(3.2f, 3.5f, 0.15f) * s, 0.09f * s);
                OctopusController c = OctopusSwingerTests.Rig(OctopusSwingerTests.Spec(), surf, new Vector3(0.48f, 2.06f, 0.02f), s, 3, out OctopusBody body);
                var brain = new OctopusBrain(3); c.Brain = brain;
                c.AnchorNearby();
                for (int f = 0; f < 120; f++) c.Tick(Dt);
                var prey = new OctopusBrainTests.TestPrey { Pos = new Vector3(3.2f - 0.09f - 0.03f, 2.3f, 0.15f) * s, Radius = 0.03f * s, Half = 0.04f * s };
                brain.Prey.Add(prey);
                int eaten = 0, min = int.MaxValue; float at = -1f;
                c.Events.Eaten += _ => eaten++;
                int T = (int)(60 * 45f * body.TimeScale);
                for (int f = 0; f < T && eaten == 0; f++)
                {
                    c.Tick(Dt);
                    if (!c.Board.Grounded) min = Mathf.Min(min, c.Board.AnchoredCount);
                    if (eaten > 0) at = f * Dt / body.TimeScale;
                }
                bool ok = eaten == 1 && c.Feeder.Grabs == 1 && min >= 1;
                if (!ok) bad = true;
                log.AppendLine($"scale {s:F2}: eaten {eaten} at {at:F1} s/ts, brain {brain.Mode}, step {c.Sequencer.Mode}, transfers {c.Sequencer.Transfers}, hauls {c.Sequencer.Hauls}, mouth to prey {c.Feeder.DistanceTo(prey):F2} (reach {c.Feeder.Reach:F2}), fewest holds {min}{(ok ? "" : "  <-- FAIL")}");
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, log.ToString());
        }

        // Same seed, same run: bit-identical.
        [Test] public void Deterministic()
        {
            float s = CreatureKitAssets.Load("Swinger").scale;
            Vector3[] end = new Vector3[2];
            for (int run = 0; run < 2; run++)
            {
                OctopusController c = OnBars(s, 2, out OctopusBody body, out _);
                c.AnchorNearby();
                Travel(c, new Vector3(0.9f * 7f, 2f, 0f) * s, 12f, false);
                end[run] = body.Arms[0].X[body.Arms[0].N - 1] + body.Lattice.X[OctopusShape.Apex];
            }
            Assert.AreEqual(end[0].x, end[1].x); Assert.AreEqual(end[0].y, end[1].y); Assert.AreEqual(end[0].z, end[1].z);
        }
    }
}
