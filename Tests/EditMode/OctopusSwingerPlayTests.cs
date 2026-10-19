using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // The swinger's life beyond brachiation (2026-10-19, user: "The swinger needs to change target sometimes in case a
    // spider gets too far away from the trees, and it should also swing around and have fun ... body-driven, and let
    // the tentacles just be there basically ... the arms can be more for show"):
    //   retargeting   a hunt it cannot finish is given up (OctopusBrain.GiveUp says why) within a bound, without
    //                 flipping back; another spider in reach is hunted instead, else it roams and plays
    //   play          frequent when not hunting: big, high and circling swings, drops, high runs, pauses - varied,
    //                 never falling, smooth
    //   arms for show the planned body is authoritative: nothing the arms do changes its motion; a holding arm is
    //                 drawn from the body to its hold, idle mouth tentacles ride curled (no splay)
    // Groves and bars are analytic (OctopusTestSurfaces); lengths x size, times x sqrt(size).
    public class OctopusSwingerPlayTests
    {
        const float Dt = 1f / 60f;

        static float SpeciesScale => CreatureKitAssets.Load("Swinger").scale;

        // A grove of four on ground at y = 0 (the trunks stand on it), the swinger hanging in tree 0, a brain with the
        // crowns as canopy regions.
        static OctopusController Grove(float s, int layout, out OctopusBody body, out OctopusBrain brain, out AnalyticSurfaces grove, uint seed = 0)
        {
            float g = OctopusSwingerTests.GroveFit * s;
            grove = OctopusGroves.Row(4, g, 2.6f, layout);
            grove.AddGround(0f);
            body = new OctopusBody(OctopusSwingerTests.Spec(), new Vector3(0.6f, 1.6f, 0.3f) * g, Quaternion.identity, s);
            var c = new OctopusController(body, grove, null, seed == 0 ? (uint)layout : seed) { Hints = new CapsuleHints(grove, 0.3f * g) };
            Assert.GreaterOrEqual(c.AnchorNearby(), 1, "no hold at spawn");
            brain = new OctopusBrain(seed == 0 ? (uint)layout : seed); c.Brain = brain;
            for (int t = 0; t < 4; t++) brain.CanopyRegions.Add(new Vector3(t * 2.6f, 2.5f, 0f) * g);
            return c;
        }

        // A spider on the side of limb `limb` (1..6) of tree `tree`, `along` of the way out (on top, the limb hides it
        // from a swinger below).
        static OctopusBrainTests.TestPrey OnLimb(AnalyticSurfaces grove, int tree, int limb, float along, float s)
        {
            AnalyticSurfaces.Capsule cap = grove.Capsules[tree * 7 + limb];
            Vector3 ax = (cap.b - cap.a).normalized, side = Vector3.Cross(Vector3.up, ax).normalized;
            Vector3 p = Vector3.Lerp(cap.a, cap.b, along) + side * (cap.r + 0.03f * s);
            return new OctopusBrainTests.TestPrey { Pos = p, Radius = 0.03f * s, Half = 0.04f * s };
        }

        // The limb of `tree` best seen from the swinger (closest to it, level).
        static int NearestLimb(AnalyticSurfaces grove, int tree, Vector3 from)
        {
            int best = 1; float bd = float.MaxValue;
            for (int k = 1; k <= 6; k++)
            {
                AnalyticSurfaces.Capsule cap = grove.Capsules[tree * 7 + k];
                float d = (Vector3.Lerp(cap.a, cap.b, 0.6f) - from).magnitude;
                if (d < bd) { bd = d; best = k; }
            }
            return best;
        }

        // ------------------------------------------------------------------ retargeting

        // Spider A in the swinger's own tree is seen and hunted while it walks to the trunk, down it to the ground and
        // away over the ground. The hunt is given up within UnreachableSeconds + 2 checks (x ts) of A being out of reach
        // (no limb within the brain's hold window), with reason OnGround / OffTrees, and never taken up again while A
        // stays on the ground (hysteresis); spider B, which shows up on a limb in the tree after next as A leaves, is
        // hunted instead and eaten. At 0.9x and 1.1x the species scale.
        [Test] public void Retarget_SpiderLeavesTrees_HuntsTheOther()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float k in new[] { 0.9f, 1.1f })
            {
                float s = SpeciesScale * k, g = OctopusSwingerTests.GroveFit * s;
                OctopusController c = Grove(s, 2, out OctopusBody body, out OctopusBrain brain, out AnalyticSurfaces grove);
                float ts = body.TimeScale, L = body.Arms[0].Length;
                for (int f = 0; f < 120; f++) c.Tick(Dt);
                Vector3 head = body.HeadCentre;
                var A = OnLimb(grove, 0, NearestLimb(grove, 0, head), 0.6f, s);
                var B = OnLimb(grove, 2, NearestLimb(grove, 2, head), 0.75f, s);
                brain.Prey.Add(A);
                int eatenA = 0, eatenB = 0, huntsOfA = 0;
                c.Events.Eaten += p => { if (p == B) eatenB++; if (p == A) eatenA++; brain.Prey.Remove(p); };
                // A walks: to the trunk, down it, out over the ground
                AnalyticSurfaces.Capsule trunk = grove.Capsules[0];
                Vector3 foot = new Vector3(trunk.a.x + trunk.r + 0.03f * s, 0.03f * s, trunk.a.z), far = foot + new Vector3(-2.5f, 0f, 1.5f) * g;
                Vector3[] path = { new Vector3(foot.x, A.Pos.y, foot.z), foot, far };
                int leg = 0; float speed = 0.8f * Mathf.Sqrt(s);
                float huntAAt = -1f, outOfReachAt = -1f, gaveUpAt = -1f, huntBAt = -1f, eatenAt = -1f;
                OctopusBrain.GiveUp why = OctopusBrain.GiveUp.None;
                bool wasHuntingA = false;
                int F = (int)(60 * 60 * ts);
                for (int f = 0; f < F && eatenB == 0 && eatenA == 0; f++)
                {
                    if (leg < path.Length)
                    {
                        Vector3 d = path[leg] - A.Pos;
                        float step = speed * Dt;
                        A.Vel = d.normalized * speed;
                        if (d.magnitude <= step) { A.Pos = path[leg]; leg++; } else A.Pos += d.normalized * step;
                    }
                    else A.Vel = Vector3.zero;
                    if (outOfReachAt < 0f && OutOfReach(grove, A.Pos, L)) { outOfReachAt = f * Dt; brain.Prey.Add(B); }
                    c.Tick(Dt);
                    if (gaveUpAt < 0f && brain.GiveUps > 0) { gaveUpAt = f * Dt; why = brain.LastGiveUp; }
                    bool huntingA = brain.Target == A && brain.Mode == OctopusBrain.State.Stalk;
                    if (huntingA && huntAAt < 0f) huntAAt = f * Dt;
                    if (huntingA && !wasHuntingA && gaveUpAt >= 0f) huntsOfA++;
                    wasHuntingA = huntingA;
                    if (huntBAt < 0f && brain.Target == B && brain.Mode == OctopusBrain.State.Stalk) huntBAt = f * Dt;
                    if (eatenB > 0) eatenAt = f * Dt;
                }
                float bound = (OctopusBrain.UnreachableSeconds + 2f * OctopusBrain.ReachCheckSeconds) * ts;
                float delay = gaveUpAt >= 0f && outOfReachAt >= 0f ? gaveUpAt - outOfReachAt : float.PositiveInfinity;
                bool ok = huntAAt >= 0f && huntAAt < outOfReachAt && eatenA == 0 && gaveUpAt >= 0f && delay <= bound + 1e-3f && huntsOfA == 0 && huntBAt >= 0f && eatenB == 1
                          && (why == OctopusBrain.GiveUp.OnGround || why == OctopusBrain.GiveUp.OffTrees);
                if (!ok) bad = true;
                log.AppendLine($"scale {s:F2}: A hunted at {huntAAt:F1} s, out of reach at {outOfReachAt:F1} s, given up at {gaveUpAt:F1} s ({why}, {delay:F2} s later, bound {bound:F2}), eaten A {eatenA}, " +
                               $"hunts of A again {huntsOfA}, B hunted at {huntBAt:F1} s, eaten B {eatenB} at {eatenAt:F1} s, give-ups {brain.GiveUps}, skipped {brain.HuntsSkipped}, brain {brain.Mode}{(ok ? "" : "  <-- FAIL")}");
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, log.ToString());
        }

        // No limb (every capsule but the trunks) passes within the brain's hold window of p: HoldLevelLengths L level,
        // HoldBelowLengths L below .. HoldAboveLengths L above (measured at the limb's closest point).
        static bool OutOfReach(AnalyticSurfaces grove, Vector3 p, float L)
        {
            for (int i = 0; i < grove.Capsules.Count; i++)
            {
                if (i % 7 == 0) continue;
                AnalyticSurfaces.Capsule cap = grove.Capsules[i];
                Vector3 ab = cap.b - cap.a;
                float t = Mathf.Clamp01(Vector3.Dot(p - cap.a, ab) / ab.sqrMagnitude);
                Vector3 d = cap.a + ab * t - p;
                if (new Vector2(d.x, d.z).magnitude <= OctopusBrain.HoldLevelLengths * L && d.y >= -OctopusBrain.HoldBelowLengths * L && d.y <= OctopusBrain.HoldAboveLengths * L) return false;
            }
            return true;
        }

        // The only spider walks about on the ground under the grove: it is not hunted (checked at sight, ignored, kept
        // ignored by the parole checks), the swinger roams and plays instead. Stalking under 3 % of the time, at most
        // one hunt started, and play moves happen.
        [Test] public void Retarget_OnlySpiderOnGround_RoamsAndPlays()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float k in new[] { 0.9f, 1.1f })
            {
                float s = SpeciesScale * k, g = OctopusSwingerTests.GroveFit * s;
                OctopusController c = Grove(s, 1, out OctopusBody body, out OctopusBrain brain, out _);
                float ts = body.TimeScale;
                for (int f = 0; f < 120; f++) c.Tick(Dt);
                var prey = new OctopusBrainTests.TestPrey { Pos = new Vector3(1.3f, 0f, 0.6f) * g + Vector3.up * (0.03f * s), Vel = new Vector3(0.15f, 0f, 0f) * Mathf.Sqrt(s), Radius = 0.03f * s, Half = 0.04f * s };
                brain.Prey.Add(prey);
                int T = (int)(60 * 90 * ts), stalk = 0, hunts = 0;
                bool was = false;
                for (int f = 0; f < T; f++)
                {
                    prey.Step(Dt); if (prey.Pos.x > 7f * g || prey.Pos.x < -1f * g) prey.Vel = -prey.Vel;
                    c.Tick(Dt);
                    bool st = brain.Mode == OctopusBrain.State.Stalk;
                    if (st) stalk++;
                    if (st && !was) hunts++;
                    was = st;
                }
                float share = stalk / (float)T;
                bool ok = share < 0.03f && hunts <= 1 && brain.PlayMoves >= 3 && brain.HuntsSkipped + brain.GiveUps >= 1;
                if (!ok) bad = true;
                log.AppendLine($"scale {s:F2}: stalking {share:P1} of the time, hunts started {hunts}, skipped at sight {brain.HuntsSkipped}, given up {brain.GiveUps} ({brain.LastGiveUp}), last verdict {brain.Unreachable}, " +
                               $"play sessions {brain.PlaySessions}, moves {brain.PlayMoves}, trips {brain.Trips}{(ok ? "" : "  <-- FAIL")}");
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, log.ToString());
        }

        // Probe-only (no branch hints, bars over ground at 0): a spider on the ground far under the bars is not hunted;
        // one on the side of a bar is (on top, the bar hides it from below).
        [Test] public void Retarget_ProbeOnly_GroundVsBar()
        {
            float s = SpeciesScale;
            AnalyticSurfaces bars = OctopusSwingTests.Bars(s, 0.9f, 8);
            bars.AddGround(0f);
            OctopusController c = OctopusSwingerTests.Rig(OctopusSwingerTests.Spec(), bars, new Vector3(0.45f, 2.06f, 0.02f), s, 1, out OctopusBody body);
            var brain = new OctopusBrain(1); c.Brain = brain;
            c.AnchorNearby();
            for (int f = 0; f < 120; f++) c.Tick(Dt);
            var ground = new OctopusBrainTests.TestPrey { Pos = new Vector3(2.3f, 0f, 0.4f) * s + Vector3.up * (0.03f * s), Radius = 0.03f * s, Half = 0.04f * s };
            brain.Prey.Add(ground);
            int stalkGround = 0;
            for (int f = 0; f < 60 * 15; f++) { c.Tick(Dt); if (brain.Mode == OctopusBrain.State.Stalk && brain.Target == ground) stalkGround++; }
            var onBar = new OctopusBrainTests.TestPrey { Pos = new Vector3(2.7f - 0.06f - 0.03f, 2.5f, 0.3f) * s, Radius = 0.03f * s, Half = 0.04f * s };
            brain.Prey.Add(onBar);
            int eaten = 0; bool huntedBar = false;
            c.Events.Eaten += p => { if (p == onBar) eaten++; brain.Prey.Remove(p); };
            for (int f = 0; f < 60 * 40 && eaten == 0; f++) { c.Tick(Dt); if (brain.Target == onBar && brain.Mode == OctopusBrain.State.Stalk) huntedBar = true; }
            string msg = $"ground spider stalked {stalkGround} frames (skipped {brain.HuntsSkipped}, given up {brain.GiveUps}, verdict {brain.Unreachable}); bar spider hunted {huntedBar}, eaten {eaten}, last give-up {brain.LastGiveUp}, brain {brain.Mode}, step {c.Sequencer.Mode}, mouth to prey {c.Feeder.DistanceTo(onBar):F2} (reach {c.Feeder.Reach:F2})";
            TestContext.Out.WriteLine(msg);
            Assert.Less(stalkGround, 60, msg);
            Assert.IsTrue(huntedBar, msg);
            Assert.AreEqual(1, eaten, msg);
        }

        // ------------------------------------------------------------------ play

        internal struct Life
        {
            public int frames, playFrames, minHolds, kinds, falls, nan;
            public float maxDv, maxJerk, maxTurn, maxAmp, playShare;
            public SwingSequencer q; public OctopusBrain brain;
        }

        // `minutes` x ts in the grove with the brain, no prey: per-frame smoothness of the planned body (crown velocity
        // change, its change, the body's turn rate), the swing's amplitude, holds, play share.
        static Life Live(float s, int layout, float minutes, uint seed = 0)
        {
            OctopusController c = Grove(s, layout, out OctopusBody body, out OctopusBrain brain, out _, seed);
            SwingSequencer q = c.Sequencer;
            for (int f = 0; f < 120; f++) c.Tick(Dt);
            var r = new Life { minHolds = int.MaxValue, q = q, brain = brain };
            int T = (int)(3600 * minutes * body.TimeScale);
            Vector3 pv = q.Swing.Velocity, pdv = Vector3.zero; Quaternion pr = body.PlanRotation;
            for (int f = 0; f < T; f++)
            {
                c.Tick(Dt);
                Vector3 dv = q.Swing.Velocity - pv; pv = q.Swing.Velocity;
                r.maxDv = Mathf.Max(r.maxDv, dv.magnitude);
                if (f > 0) r.maxJerk = Mathf.Max(r.maxJerk, (dv - pdv).magnitude);
                pdv = dv;
                r.maxTurn = Mathf.Max(r.maxTurn, Quaternion.Angle(pr, body.PlanRotation) / Dt); pr = body.PlanRotation;
                r.maxAmp = Mathf.Max(r.maxAmp, q.Swing.Amplitude);
                if (brain.Mode == OctopusBrain.State.Play) r.playFrames++;
                if (!c.Board.Grounded && q.Mode != SwingSequencer.Step.Fall) r.minHolds = Mathf.Min(r.minHolds, c.Board.AnchoredCount);
                if (!OctoMath.Finite(body.HeadCentre) || !OctoMath.Finite(body.Arms[0].X[body.Arms[0].N - 1])) r.nan++;
            }
            r.frames = T;
            r.falls = q.Falls;
            r.playShare = r.playFrames / (float)T;
            for (int m = 1; m < brain.MoveCounts.Length; m++) if (brain.MoveCounts[m] > 0) r.kinds++;
            return r;
        }

        static string Moves(OctopusBrain b) =>
            $"big {b.MoveCounts[(int)PlayMove.BigSwing]}, high {b.MoveCounts[(int)PlayMove.HighSwing]}, circle {b.MoveCounts[(int)PlayMove.Circle]}, drop {b.MoveCounts[(int)PlayMove.Drop]}, run {b.MoveCounts[(int)PlayMove.Run]}, pause {b.MoveCounts[(int)PlayMove.Pause]}";

        // Four minutes of life (no prey) at 0.9x and 1.1x, two layouts: it plays a lot (>= 30 % of the time), with
        // variety (>= 4 kinds of move; drops tried - this grove's limbs all grow at 2.3-2.8 m, so a lower one within
        // reach is rare and a drop mostly finds none and gives way to another move: Play_Drop_CatchesSoftly lands
        // them), never falls or hangs from nothing, swings big (>= 45 deg) but never past 65, and smoothly - the crown's
        // velocity changes per frame at most 0.4 sqrt(s) m/s (~2.4 g), that change itself at most 0.4 sqrt(s), the body
        // turns < 240 deg/s.
        [Test] public void Play_Varied_NoFalls_Smooth()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float k in new[] { 0.9f, 1.1f })
                for (int layout = 1; layout <= 2; layout++)
                {
                    float s = SpeciesScale * k, ts = Mathf.Sqrt(s);
                    Life r = Live(s, layout, 4f);
                    float amp = r.maxAmp * Mathf.Rad2Deg;
                    bool ok = r.playShare >= 0.3f && r.kinds >= 4 && r.q.Drops + r.q.DropFailures >= 1 && r.falls == 0 && r.nan == 0 && r.minHolds >= 1
                              && amp >= 45f && amp < 65f && r.maxDv < 0.4f * ts && r.maxJerk < 0.4f * ts && r.maxTurn < 240f;
                    if (!ok) bad = true;
                    log.AppendLine($"scale {s:F2} layout {layout}: play {r.playShare:P0} of the time, sessions {r.brain.PlaySessions}, moves {r.brain.PlayMoves} ({Moves(r.brain)}), drops {r.q.Drops} (failed {r.q.DropFailures}), trips {r.brain.Trips} arrived {r.brain.Arrivals}, " +
                                   $"transfers {r.q.Transfers}, catches {r.q.Catches}/{r.q.CatchAttempts}, falls {r.falls}, fewest holds {r.minHolds}, amplitude max {amp:F0} deg, max dv {r.maxDv / ts:F3} jerk {r.maxJerk / ts:F3} (/sqrt s), turn {r.maxTurn:F0} deg/s{(ok ? "" : "  <-- FAIL")}");
                }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, log.ToString());
        }

        // Each play move on its own, from a still hang under one bar: big and high swings reach their amplitude
        // (within 6 deg) and stay under it + 8; circling goes round (the crown's heading round the hold turns > 2 full
        // turns in 8 s x ts) in a cone within 8 deg of its angle; no move ever leaves the body without a hold.
        [Test] public void Play_Moves_DoWhatTheySay()
        {
            var log = new StringBuilder(); bool bad = false;
            float s = SpeciesScale;
            foreach (PlayMove m in new[] { PlayMove.BigSwing, PlayMove.HighSwing, PlayMove.Circle })
            {
                OctopusController c = OctopusSwingerPlannedTests.OnBars(s, 1, out OctopusBody body, out _);
                c.AnchorNearby();
                c.Intent = new OctoIntent { mode = OctoMode.Rest, goal = body.HeadCentre };
                for (int f = 0; f < 120; f++) c.Tick(Dt);
                float want = (m == PlayMove.BigSwing ? 44f : m == PlayMove.HighSwing ? 55f : 30f) * Mathf.Deg2Rad;
                c.Intent = new OctoIntent { mode = OctoMode.Rest, goal = body.HeadCentre, play = m, playAmplitude = want, playId = 1 };
                SwingSequencer q = c.Sequencer;
                int T = (int)(60 * 8 * body.TimeScale), minHolds = int.MaxValue;
                float maxAmp = 0f, lateAmp = 0f, turned = 0f, coneMin = 180f, coneMax = 0f;
                Vector3 pivot = q.Pivot; float prevAz = float.NaN;
                for (int f = 0; f < T; f++)
                {
                    c.Tick(Dt);
                    minHolds = Mathf.Min(minHolds, c.Board.AnchoredCount);
                    maxAmp = Mathf.Max(maxAmp, q.Swing.Amplitude);
                    if (f > T / 2) lateAmp = Mathf.Max(lateAmp, q.Swing.Amplitude);
                    Vector3 r = q.Crown - q.Pivot;
                    float az = Mathf.Atan2(r.z, r.x);
                    if (!float.IsNaN(prevAz)) { float d = az - prevAz; if (d > Mathf.PI) d -= 2f * Mathf.PI; if (d < -Mathf.PI) d += 2f * Mathf.PI; turned += d; }
                    prevAz = az;
                    if (f > T / 2) { float cone = Mathf.Atan2(new Vector2(r.x, r.z).magnitude, -r.y) * Mathf.Rad2Deg; coneMin = Mathf.Min(coneMin, cone); coneMax = Mathf.Max(coneMax, cone); }
                }
                bool ok = minHolds >= 1 && q.Mode == SwingSequencer.Step.Play;
                if (m == PlayMove.Circle) ok &= Mathf.Abs(turned) > 4f * Mathf.PI && coneMin > 30f - 8f && coneMax < 30f + 8f;
                else ok &= lateAmp > want - 6f * Mathf.Deg2Rad && maxAmp < want + 8f * Mathf.Deg2Rad;
                if (!ok) bad = true;
                log.AppendLine($"{m}: want {want * Mathf.Rad2Deg:F0} deg, amplitude max {maxAmp * Mathf.Rad2Deg:F1} late {lateAmp * Mathf.Rad2Deg:F1}, turned round the hold {turned / (2f * Mathf.PI):F1} turns, cone {coneMin:F0}..{coneMax:F0} deg, fewest holds {minHolds}, step {q.Mode}{(ok ? "" : "  <-- FAIL")}");
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, log.ToString());
        }

        // A drop from a bar to a lower one beside it: the body lets go only once the lower arm holds, falls on the slack
        // arm, and is caught softly (velocity change per frame < 0.5 sqrt(s) m/s: the soft rope brakes at 2 g, on top of
        // the swing's own turn - a hard catch was 2.2 m/s in one frame, ~13 g), then hangs under the lower bar.
        [Test] public void Play_Drop_CatchesSoftly()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float k in new[] { 0.9f, 1.1f })
            {
                float s = SpeciesScale * k, ts = Mathf.Sqrt(s);
                var surf = new AnalyticSurfaces();
                surf.AddCapsule(new Vector3(0f, 2.5f, -3f) * s, new Vector3(0f, 2.5f, 3f) * s, 0.06f * s);
                surf.AddCapsule(new Vector3(0.5f, 1.9f, -3f) * s, new Vector3(0.5f, 1.9f, 3f) * s, 0.06f * s);
                surf.AddGround(-5f * s);
                OctopusController c = OctopusSwingerTests.Rig(OctopusSwingerTests.Spec(), surf, new Vector3(0.03f, 2.06f, 0.02f), s, 1, out OctopusBody body);
                c.AnchorNearby();
                c.Intent = new OctoIntent { mode = OctoMode.Rest, goal = body.HeadCentre };
                for (int f = 0; f < 180; f++) c.Tick(Dt);
                SwingSequencer q = c.Sequencer;
                float y0 = q.Pivot.y;
                c.Intent = new OctoIntent { mode = OctoMode.Rest, goal = body.HeadCentre, play = PlayMove.Drop, playId = 7 };
                int T = (int)(60 * 8 * ts), minHolds = int.MaxValue;
                float maxDv = 0f; Vector3 pv = q.Swing.Velocity;
                for (int f = 0; f < T; f++)
                {
                    c.Tick(Dt);
                    maxDv = Mathf.Max(maxDv, (q.Swing.Velocity - pv).magnitude); pv = q.Swing.Velocity;
                    minHolds = Mathf.Min(minHolds, c.Board.AnchoredCount);
                }
                bool ok = q.Drops == 1 && q.PlayDone(7) && minHolds >= 1 && q.Pivot.y < y0 - 0.4f * s && maxDv < 0.5f * ts && q.Falls == 0;
                if (!ok) bad = true;
                log.AppendLine($"scale {s:F2}: drops {q.Drops} (failed {q.DropFailures}), hold from y {y0 / s:F2} to {q.Pivot.y / s:F2} (/s), fewest holds {minHolds}, max dv {maxDv / ts:F3} (/sqrt s), step {q.Mode}{(ok ? "" : "  <-- FAIL")}");
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, log.ToString());
        }

        // A run (Travel + play Run) swings higher than the catch needs: its largest amplitude beats a plain trip's over
        // the same bars by >= 5 deg, and it still arrives with every catch.
        [Test] public void Play_Run_SwingsHigher()
        {
            float s = SpeciesScale;
            float[] amp = new float[2]; bool[] arrived = new bool[2]; float[] rate = new float[2];
            for (int run = 0; run < 2; run++)
            {
                OctopusController c = OctopusSwingerPlannedTests.OnBars(s, 1, out OctopusBody body, out _);
                c.AnchorNearby();
                for (int f = 0; f < 60; f++) c.Tick(Dt);
                SwingSequencer q = c.Sequencer;
                c.Intent = new OctoIntent { mode = OctoMode.Travel, goal = new Vector3(0.9f * 5f, 2f, 0f) * s, urgency = 0.5f, play = run == 1 ? PlayMove.Run : PlayMove.None, playAmplitude = 52f * Mathf.Deg2Rad, playId = run };
                for (int f = 0; f < (int)(60 * 40 * body.TimeScale) && !q.Arrived; f++) { c.Tick(Dt); amp[run] = Mathf.Max(amp[run], q.Swing.Amplitude * Mathf.Rad2Deg); }
                arrived[run] = q.Arrived; rate[run] = q.CatchAttempts > 0 ? (float)q.Catches / q.CatchAttempts : 0f;
            }
            string msg = $"trip: amplitude {amp[0]:F0} deg, arrived {arrived[0]}, catches {rate[0]:P0}; run: amplitude {amp[1]:F0} deg, arrived {arrived[1]}, catches {rate[1]:P0}";
            TestContext.Out.WriteLine(msg);
            Assert.IsTrue(arrived[0] && arrived[1], msg);
            Assert.GreaterOrEqual(amp[1], amp[0] + 5f, msg);
            Assert.GreaterOrEqual(rate[1], 0.9f, msg);
        }

        // Same seed, same life with brain and play: bit-identical.
        [Test] public void Play_Deterministic()
        {
            float s = SpeciesScale;
            Vector3[] end = new Vector3[2]; int[] moves = new int[2];
            for (int run = 0; run < 2; run++)
            {
                Life r = Live(s, 2, 1f, 5);
                end[run] = r.q.Crown + r.q.Swing.Velocity;
                moves[run] = r.brain.PlayMoves;
            }
            Assert.AreEqual(moves[0], moves[1]);
            Assert.AreEqual(end[0].x, end[1].x); Assert.AreEqual(end[0].y, end[1].y); Assert.AreEqual(end[0].z, end[1].z);
        }

        // Life with brain, play and a hunt allocates nothing after warm-up.
        [Test] public void Play_NoGc()
        {
            GcAllocs.AssertCounterWorks();
            float s = SpeciesScale;
            OctopusController c = Grove(s, 2, out OctopusBody body, out OctopusBrain brain, out AnalyticSurfaces grove);
            var bones = new Matrix4x4[body.BoneCount];
            for (int f = 0; f < 60 * 20; f++) { c.Tick(Dt); body.WriteBoneWorld(bones, 0); }
            var ground = new OctopusBrainTests.TestPrey { Pos = new Vector3(1f, 0.03f, 0.5f) * s, Radius = 0.03f * s, Half = 0.04f * s };
            brain.Prey.Add(ground);
            int allocs = GcAllocs.Count(() => { for (int f = 0; f < 60 * 40; f++) { c.Tick(Dt); body.WriteBoneWorld(bones, 0); } });
            TestContext.Out.WriteLine($"allocations over 40 s: {allocs}; play moves {brain.PlayMoves}, skipped {brain.HuntsSkipped}, transfers {c.Sequencer.Transfers}");
            Assert.AreEqual(0, allocs);
            Assert.Greater(brain.PlayMoves, 0, "the run played nothing");
        }

        // ------------------------------------------------------------------ arms for show

        // Nothing the arms do moves the body: two identical trips over the bars, in one every arm and mouth particle not
        // pinned is shoved 3 cm x s in a random direction every 7 frames - the planned body's path (crown, velocity,
        // holds, catches) is bit-identical.
        [Test] public void ArmsForShow_NoFeedbackIntoTheBody()
        {
            float s = SpeciesScale;
            Vector3[] crown = new Vector3[2]; int[] catches = new int[2];
            var trace = new Vector3[2][];
            for (int run = 0; run < 2; run++)
            {
                OctopusController c = OctopusSwingerPlannedTests.OnBars(s, 1, out OctopusBody body, out _);
                c.AnchorNearby();
                c.Intent = new OctoIntent { mode = OctoMode.Travel, goal = new Vector3(0.9f * 6f, 2f, 0f) * s, urgency = 0.6f };
                uint rng = 99;
                trace[run] = new Vector3[60 * 16];
                for (int f = 0; f < trace[run].Length; f++)
                {
                    if (run == 1 && f % 7 == 3)
                    {
                        System.Action<TentacleChain> shove = ch =>
                        {
                            for (int k = 1; k < ch.N; k++)
                            {
                                if (ch.Pinned[k]) continue;
                                rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                                float a = (rng & 0xFFFF) / 65536f * 6.2831f, b = ((rng >> 16) & 0xFFFF) / 65536f * 2f - 1f;
                                ch.X[k] += new Vector3(Mathf.Cos(a) * Mathf.Sqrt(1f - b * b), b, Mathf.Sin(a) * Mathf.Sqrt(1f - b * b)) * (0.03f * s);
                            }
                        };
                        for (int a = 0; a < body.ArmCount; a++) shove(body.Arms[a]);
                        for (int m = 0; m < body.MouthCount; m++) shove(body.Mouth[m]);
                    }
                    c.Tick(Dt);
                    trace[run][f] = c.Sequencer.Crown;
                }
                crown[run] = c.Sequencer.Crown + c.Sequencer.Swing.Velocity;
                catches[run] = c.Sequencer.Catches;
            }
            int firstDiff = -1;
            for (int f = 0; f < trace[0].Length && firstDiff < 0; f++) if (trace[0][f].x != trace[1][f].x || trace[0][f].y != trace[1][f].y || trace[0][f].z != trace[1][f].z) firstDiff = f;
            TestContext.Out.WriteLine($"catches {catches[0]} / {catches[1]}, first differing frame {firstDiff}");
            Assert.Greater(catches[0], 2);
            Assert.AreEqual(-1, firstDiff, "the arms moved the body");
            Assert.AreEqual(crown[0].x, crown[1].x); Assert.AreEqual(crown[0].y, crown[1].y); Assert.AreEqual(crown[0].z, crown[1].z);
        }

        // Hanging and swinging, a holding arm is drawn (no solve) from its root to its hold: root on the body, the run
        // ending at the base pin, evenly spaced (segment lengths within 5 % of each other), pins on the real surface.
        // Idle mouth tentacles do not splay: each lies within MouthLagMax of its rest curl on the body (tip direction
        // within 25 deg of the curl's, p95) while the body hangs upside down and swings.
        [Test] public void ArmsForShow_DrawnHold_CurledMouths()
        {
            float s = SpeciesScale;
            OctopusController c = OctopusSwingerPlannedTests.OnBars(s, 1, out OctopusBody body, out AnalyticSurfaces bars);
            c.AnchorNearby();
            c.Intent = new OctoIntent { mode = OctoMode.Travel, goal = new Vector3(0.9f * 6f, 2f, 0f) * s, urgency = 0.6f };
            SwingSequencer q = c.Sequencer;
            int drawnTicks = 0, holdTicks = 0; float worstSpread = 0f, worstEnd = 0f;
            var tipAngles = new System.Collections.Generic.List<float>();
            for (int f = 0; f < 60 * 20; f++)
            {
                c.Tick(Dt);
                for (int a = 0; a < body.ArmCount; a++)
                {
                    if (q.Use[a] != SwingSequencer.ArmUse.Hold) continue;
                    holdTicks++;
                    if (body.ArmDraw[a] < 1f) continue;
                    drawnTicks++;
                    TentacleChain ch = body.Arms[a];
                    int bp = ch.BasePin();
                    float lo = float.MaxValue, hi = 0f;
                    for (int k = 0; k < bp; k++) { float l = (ch.X[k + 1] - ch.X[k]).magnitude; lo = Mathf.Min(lo, l); hi = Mathf.Max(hi, l); }
                    worstSpread = Mathf.Max(worstSpread, hi / lo - 1f);
                    worstEnd = Mathf.Max(worstEnd, (ch.X[bp] - ch.PinPoint[bp]).magnitude);
                }
                if (c.Feeder.Feeding) continue;
                for (int m = 0; m < body.MouthCount; m++)
                {
                    TentacleChain ch = body.Mouth[m];
                    Vector3 tip = OctoMath.Rotate(OctoMath.Conj(ch.RootFrame), ch.X[ch.N - 1] - ch.X[0]);
                    // the curl's own tip, in the root frame (OctopusBody.mouthLocal), recomputed
                    float ang = 0f, tc = ch.Spec.TipCurlDeg * Mathf.Deg2Rad; Vector3 p = Vector3.zero;
                    for (int j = 0; j < ch.N - 1; j++) { float t = (float)j / (ch.N - 2); ang += tc * t * t; p += new Vector3(0f, -Mathf.Sin(ang), Mathf.Cos(ang)) * ch.Spacing; }
                    tipAngles.Add(Vector3.Angle(tip, p));
                }
            }
            tipAngles.Sort();
            float p95 = tipAngles.Count > 0 ? tipAngles[(int)(0.95f * (tipAngles.Count - 1))] : 0f;
            string msg = $"holding {holdTicks} arm-ticks, drawn {drawnTicks}; drawn run segment spread {worstSpread:P1}, run end off the base pin {worstEnd * 1000f:F2} mm; idle mouth tip off its curl p95 {p95:F1} deg; catches {q.Catches}";
            TestContext.Out.WriteLine(msg);
            Assert.Greater(drawnTicks, holdTicks / 2, msg);
            Assert.Less(worstSpread, 0.05f, msg);
            Assert.Less(worstEnd, 1e-4f, msg);
            Assert.Less(p95, 25f, msg);
        }
    }
}
