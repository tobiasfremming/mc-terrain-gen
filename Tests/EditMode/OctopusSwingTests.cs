using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Swinging traversal (user, 2026-10-18: "traverse the branches, letting the body swing under them", "it needs a
    // goal and then check points in the branches it can go to"; plan 2026-10-18 T7-T11). Analytic surfaces; lengths
    // x scale, times x sqrt(scale); every scenario at 1x and at the lab's 4x.
    public class OctopusSwingTests
    {
        const float Dt = 1f / 60f;

        // ------------------------------------------------------------------ SwingPhase (pure)

        // An ideal pendulum (r 1 m, amplitude 30 deg) fed to the estimator: the amplitude is read within 2 deg, the
        // forward apex is flagged once per period within 3 deg of the true apex, the kinetic share is ~1 passing
        // under the pivot and ~0 at the apexes.
        [Test] public void SwingPhase_ApexOnPendulum()
        {
            var sp = new SwingPhase();
            float r = 1f, A = 30f * Mathf.Deg2Rad, w = Mathf.Sqrt(9.81f / r), dt = 1f / 240f;
            Vector3 pivot = new Vector3(0f, 2f, 0f);
            int apexes = 0; float worstApex = 0f, worstAmp = 0f, kBottom = 1f, kApex = 0f;
            for (int i = 0; i < 240 * 6; i++)
            {
                float t = i * dt, th = A * Mathf.Cos(w * t);
                Vector3 com = pivot + new Vector3(r * Mathf.Sin(th), -r * Mathf.Cos(th), 0f);
                sp.Update(com, pivot, Vector3.up, Vector3.right, 9.81f, dt, 1e-4f);
                if (i < 10) continue;
                worstAmp = Mathf.Max(worstAmp, Mathf.Abs(sp.Amplitude - A));
                if (sp.ForwardApex) { apexes++; worstApex = Mathf.Max(worstApex, Mathf.Abs(sp.Theta - A)); }
                if (Mathf.Abs(th) < 0.05f * A) kBottom = Mathf.Min(kBottom, sp.Kinetic);
                if (Mathf.Abs(th) > 0.995f * A) kApex = Mathf.Max(kApex, sp.Kinetic);
            }
            float periods = 6f * w / (2f * Mathf.PI);
            TestContext.Out.WriteLine($"apexes {apexes} over {periods:F1} periods, worst apex error {worstApex * Mathf.Rad2Deg:F2} deg, worst amplitude error {worstAmp * Mathf.Rad2Deg:F2} deg, kinetic at the bottom >= {kBottom:F2}, at the apexes <= {kApex:F2}");
            Assert.That(apexes, Is.InRange(Mathf.FloorToInt(periods) - 1, Mathf.CeilToInt(periods)), "one forward apex per period");
            Assert.Less(worstApex, 3f * Mathf.Deg2Rad, "apex flagged away from the apex");
            Assert.Less(worstAmp, 2f * Mathf.Deg2Rad, "amplitude");
            Assert.Greater(kBottom, 0.9f); Assert.Less(kApex, 0.15f);
        }

        // ------------------------------------------------------------------ helpers

        // Bars across the way (x scale s), `gap` apart along +x, at 2.5 m.
        internal static AnalyticSurfaces Bars(float s, float gap, int n)
        {
            var a = new AnalyticSurfaces();
            for (int i = 0; i < n; i++) a.AddCapsule(new Vector3(gap * i, 2.5f, -3f) * s, new Vector3(gap * i, 2.5f, 3f) * s, 0.06f * s);
            a.AddGround(-5f * s);
            return a;
        }

        internal struct Trip
        {
            public int swings, transfers, apexTransfers, catches, nearCatches, windows, stalledWindows, waypoints;
            public float arrivedAt, dx, maxAmplitude, meanAmplitude, accP99, worstSustainedStrain;
            public bool arrived;
            public override string ToString() =>
                $"arrived {(arrived ? (arrivedAt.ToString("F1") + " s/ts") : "never")}, dx {dx:F2} (/scale), swings {swings}, at the apex {apexTransfers}/{transfers}, " +
                $"catches near a waypoint {nearCatches}/{catches} (of {waypoints}), stalled 10 s windows {stalledWindows}/{windows},  swing past 75 deg {maxAmplitude:F1} % of the time, mean {meanAmplitude:F0} deg, " +
                $"acc p99 {accP99:F1} g, longest strain over the limit {worstSustainedStrain:F2} s/ts";
        }

        // Travel to `goal` for `seconds` x sqrt(s): transfers (and whether each came at the forward apex: little of
        // the swing's energy left as motion, or no swing to wait for, or a missed apex), catches against the planned
        // waypoints, 10 s windows without progress toward the goal, swing amplitude, head acceleration sampled at
        // Froude-similar intervals, and the longest time an arm's strain stayed over the limit.
        internal static Trip Travel(OctopusController c, OctopusBody body, Vector3 goal, float seconds)
        {
            float s = body.SizeScale, ts = body.TimeScale, L = body.Arms[0].Length;
            c.Intent = new OctoIntent { mode = OctoMode.Travel, goal = goal, urgency = 0.6f };
            var t = new Trip { arrivedAt = -1f };
            Vector3 start = body.HeadCentre;
            int T = (int)(60 * seconds * ts), win = (int)(600 * ts), stride = Mathf.Max(1, Mathf.RoundToInt(2f * ts));
            var hist = new Vector3[64]; var accs = new List<float>();
            float winD = Level(goal - start).magnitude; int lastSw = 0; double ampSum = 0; int ampN = 0, wide = 0;
            var over = new float[body.ArmCount];
            ArmCoordinator co = c.Coordinator;
            for (int f = 0; f < T; f++)
            {
                c.Tick(Dt);
                Vector3 h = body.HeadCentre;
                hist[f % 64] = h;
                if (f > 2 * stride + 30 && !t.arrived)
                {
                    float Ts = Dt * stride;
                    accs.Add(((hist[f % 64] - 2f * hist[(f - stride) % 64] + hist[(f - 2 * stride) % 64]) / (Ts * Ts)).magnitude / 9.81f);
                }
                if (co.Swing.Valid && !t.arrived)
                {
                    // the swing's angle itself (the energy amplitude reads high for a moment after each transfer,
                    // when the body is still moving with the old swing)
                    // (only while hanging: a body among branches with holds beside and above it - at spawn, at the
                    // trunk of the goal tree - has no swing to speak of)
                    float th = Mathf.Abs(co.Swing.Theta) * Mathf.Rad2Deg;
                    if (th < 90f)
                    {
                        ampSum += th; ampN++;
                        if (th > 75f) wide++;
                    }
                }
                for (int a = 0; a < body.ArmCount; a++)
                {
                    over[a] = c.Board.Anchored[a] && co.Strain[a] > ArmCoordinator.StrainLimit ? over[a] + Dt : 0f;
                    t.worstSustainedStrain = Mathf.Max(t.worstSustainedStrain, over[a] / ts);
                }
                if (co.Swings != lastSw)
                {
                    lastSw = co.Swings;
                    t.transfers++;
                    if (co.Swing.Kinetic <= 0.4f || co.Swing.Amplitude < 6f * Mathf.Deg2Rad || co.Swing.Theta > 0f && co.Swing.Omega <= 0.2f * co.Swing.Natural) t.apexTransfers++;
                    if (co.Route.Valid)
                    {
                        t.catches++;
                        float best = float.MaxValue;
                        for (int i = 0; i < co.Route.Count; i++) best = Mathf.Min(best, (co.Route.Waypoints[i] - c.Board.SwingCentre).magnitude);
                        if (best < 0.7f * L) t.nearCatches++;
                        t.waypoints = Mathf.Max(t.waypoints, co.Route.Count);
                    }
                }
                if (!t.arrived && co.SwingArrived) { t.arrived = true; t.arrivedAt = f * Dt / ts; }
                if (f > 0 && f % win == 0 && !t.arrived)
                {
                    float d = Level(goal - h).magnitude;
                    t.windows++;
                    if (winD - d < 0.5f * L) t.stalledWindows++;
                    winD = d;
                }
            }
            t.swings = co.Swings;
            t.dx = Vector3.Dot(body.HeadCentre - start, Level(goal - start).normalized) / s;
            t.meanAmplitude = ampN > 0 ? (float)(ampSum / ampN) : 0f;
            t.maxAmplitude = ampN > 0 ? 100f * wide / ampN : 0f;   // (reported as the share of hanging time past 75 deg)
            accs.Sort();
            t.accP99 = accs.Count > 0 ? accs[(int)(accs.Count * 0.99f)] : 0f;
            return t;
        }

        static Vector3 Level(Vector3 v) { v.y = 0f; return v; }

        internal static OctopusController InGrove(float s, int layout, bool hints, out OctopusBody body, out AnalyticSurfaces grove, int trees = 4)
        {
            grove = OctopusGroves.Row(trees, s, 2.6f, layout);
            body = new OctopusBody(CephalopodSpec.Default, new Vector3(0.6f, 1.6f, 0.3f) * s, Quaternion.identity, s);
            var c = new OctopusController(body, grove, null, (uint)layout);
            if (hints) c.Hints = new CapsuleHints(grove, 0.3f * s);
            Assert.GreaterOrEqual(c.AnchorNearby(), 2, "no hold at spawn");
            for (int f = 0; f < 240; f++) c.Tick(Dt);
            return c;
        }

        // ------------------------------------------------------------------ the swing

        // Over a row of bars an arm's length apart (no hints: the arms search ahead): the body hangs under one grip
        // group and swings on under the other; in 3 of 4 runs it crosses at least three bars in 40 s x sqrt(s), and
        // most transfers come at the forward apex of the swing (the old holds let go when the body has stopped ahead,
        // nearest the new ones).
        [Test] public void Swing_ReleaseAtApex_GapBars_Crosses()
        {
            var log = new StringBuilder(); int bad = 0;
            foreach (float s in new[] { 1f, 4f })
                for (uint seed = 1; seed <= 2; seed++)
                {
                    var surf = Bars(s, 1.6f, 8);
                    var body = new OctopusBody(CephalopodSpec.Default, new Vector3(0.03f * seed, 2.0f, 0.02f) * s, Quaternion.identity, s);
                    var c = new OctopusController(body, surf, null, seed);
                    Assert.GreaterOrEqual(c.AnchorNearby(), 2);
                    for (int f = 0; f < 240; f++) c.Tick(Dt);
                    Trip t = Travel(c, body, new Vector3(1.6f * 7f, 2f, 0f) * s, 40f);
                    bool ok = t.dx >= 3f * 1.6f && t.apexTransfers >= 0.6f * t.transfers;
                    if (!ok) bad++;
                    log.AppendLine($"scale {s} seed {seed}: {t}{(ok ? "" : "  <-- FAIL")}");
                }
            TestContext.Out.WriteLine(log.ToString());
            Assert.LessOrEqual(bad, 1, "the swing does not cross the bars at the apex (see the log)");
        }

        // Energy stays bounded: in every grove traverse the body hanging under its holds swings past 75 deg less than
        // 8 % of the time and averages below 35 deg (the pump stops at 50 deg, the swing is calmed above 58 deg; measured
        // 2026-10-18: 1x 0-1.5 %, mean 19-31 deg; 4x 3-5.4 %, mean 24-33 deg - the large octopus swings a little wider).
        [Test] public void Swing_EnergyBounded()
        {
            var log = new StringBuilder(); int bad = 0;
            foreach (float s in new[] { 1f, 4f })
                for (int layout = 1; layout <= 3; layout++)
                {
                    OctopusController c = InGrove(s, layout, true, out OctopusBody body, out _);
                    Trip t = Travel(c, body, new Vector3(3f * 2.6f, 2.4f, 0f) * s, 25f);
                    bool ok = t.maxAmplitude < 8f && t.meanAmplitude < 35f;
                    if (!ok) bad++;
                    log.AppendLine($"scale {s} layout {layout}: {t}{(ok ? "" : "  <-- FAIL")}");
                }
            TestContext.Out.WriteLine(log.ToString());
            Assert.AreEqual(0, bad, "the swing gains energy (see the log)");
        }

        // ------------------------------------------------------------------ the route

        // Purposeful: given a goal in the far tree of a row of four (three crowns to cross), the octopus plans a
        // route over the branch hints and swings it waypoint by waypoint. In 6 layouts x (1x, 4x): at least 5 of 6
        // per scale arrive within 25 s x sqrt(s); the holds it swings onto lie at the planned waypoints (>= 80 %
        // within 0.7 L); it never hangs about without progress (no 10 s window short of 0.5 L progress); no arm
        // stays strained past the strain limit longer than the release takes.
        [Test] public void Route_ReachesGoal_AcrossTrees_FollowsPlan()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float s in new[] { 1f, 4f })
            {
                int arrived = 0, near = 0, catches = 0, stalled = 0; float strain = 0f;
                for (int layout = 1; layout <= 6; layout++)
                {
                    OctopusController c = InGrove(s, layout, true, out OctopusBody body, out _);
                    Trip t = Travel(c, body, new Vector3(3f * 2.6f, 2.4f, 0f) * s, 25f);
                    if (t.arrived) arrived++;
                    near += t.nearCatches; catches += t.catches; stalled += t.stalledWindows; strain = Mathf.Max(strain, t.worstSustainedStrain);
                    log.AppendLine($"scale {s} layout {layout}: {t}");
                }
                log.AppendLine($"scale {s}: arrived {arrived}/6, catches at waypoints {near}/{catches}, stalled windows {stalled}, strain over the limit at most {strain:F2} s/ts");
                if (arrived < 5 || near < 0.8f * catches || stalled > 0 || strain > ArmCoordinator.StrainHoldSeconds + 0.35f) bad = true;
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, "the octopus does not follow its route to the goal (see the log)");
        }

        // An individual's size spread around the lab's 4x (3.1 x 0.9 / 1.1): the same traverse arrives.
        [Test] public void Route_AcrossLabSizes()
        {
            var log = new StringBuilder(); int arrived = 0, runs = 0;
            foreach (float s in new[] { 2.8f, 3.4f })
                for (int layout = 1; layout <= 3; layout++)
                {
                    OctopusController c = InGrove(s, layout, true, out OctopusBody body, out _);
                    Trip t = Travel(c, body, new Vector3(3f * 2.6f, 2.4f, 0f) * s, 25f);
                    runs++; if (t.arrived) arrived++;
                    log.AppendLine($"scale {s} layout {layout}: {t}");
                }
            TestContext.Out.WriteLine(log.ToString());
            Assert.GreaterOrEqual(arrived, runs - 1, "the octopus does not cross the grove at an individual's size (see the log)");
        }

        // ------------------------------------------------------------------ life in the grove

        internal struct Life { public int trips, arrivals, branchSwings, changes; public float minutes, path, travelShare; }

        internal static Life Live(float s, int layout, float minutes)
        {
            float ts = Mathf.Sqrt(s);
            var grove = OctopusGroves.Row(4, s, 2.6f, layout);
            var body = new OctopusBody(CephalopodSpec.Default, new Vector3(0.6f, 1.6f, 0.3f) * s, Quaternion.identity, s);
            var c = new OctopusController(body, grove, null, (uint)layout) { Hints = new CapsuleHints(grove, 0.3f * s) };
            var brain = new OctopusBrain((uint)layout); c.Brain = brain;
            for (int t = 0; t < 4; t++) brain.CanopyRegions.Add(new Vector3(t * 2.6f, 2.5f, 0f) * s);
            c.AnchorNearby();
            var l = new Life { minutes = minutes };
            int T = (int)(3600 * minutes * ts), travel = 0;
            var last = new ArmRole[body.ArmCount];
            for (int a = 0; a < body.ArmCount; a++) last[a] = c.Board.Role[a];
            Vector3 prev = body.HeadCentre;
            for (int f = 0; f < T; f++)
            {
                c.Tick(Dt);
                for (int a = 0; a < body.ArmCount; a++) if (c.Board.Role[a] != last[a]) { l.changes++; last[a] = c.Board.Role[a]; }
                if (c.Intent.mode == OctoMode.Travel) travel++;
                if (f % 30 == 0) { l.path += Level(body.HeadCentre - prev).magnitude; prev = body.HeadCentre; }
            }
            l.trips = brain.Trips; l.arrivals = brain.Arrivals; l.branchSwings = c.Coordinator.BranchSwings;
            l.path /= body.Arms[0].Length;
            l.travelShare = travel / (float)T;
            return l;
        }

        // Left to itself in a grove (brain on, a few canopy regions), the octopus keeps traversing: it picks goals,
        // swings there, rests briefly and goes on. Per minute (x sqrt(s)): at least 8 branch-to-branch swings and 10
        // arm lengths of path, at least 4 trips with 3 arrivals in the run, and roles change at most 8 times per arm
        // (the user measured 17-22 before; held through the swings, the groups stay through rests). One run of four
        // may fall short (a hard layout); over all four, at least 12 branch swings and 18 L of path per minute
        // (measured 2026-10-18: 10-21 swings/min, 20-35 L/min, 1-8 role changes/min/arm).
        [Test] public void Explore_SwingsInGrove_Traverses()
        {
            var log = new StringBuilder(); int bad = 0; float swSum = 0f, pathSum = 0f;
            foreach (float s in new[] { 1f, 4f })
                foreach (int layout in new[] { 1, 2 })
                {
                    Life l = Live(s, layout, 1.5f);
                    float sw = l.branchSwings / l.minutes, path = l.path / l.minutes, churn = l.changes / (8f * l.minutes);
                    bool ok = sw >= 8f && path >= 10f && l.trips >= 4 && l.arrivals >= 3 && churn <= 8f;
                    if (!ok) bad++;
                    swSum += sw; pathSum += path;
                    log.AppendLine($"scale {s} layout {layout}: trips {l.trips}, arrivals {l.arrivals}, branch swings {sw:F1}/min, path {path:F1} L/min, travelling {l.travelShare * 100f:F0} %, role changes {churn:F1}/min/arm{(ok ? "" : "  <-- FAIL")}");
                }
            log.AppendLine($"mean branch swings {swSum / 4f:F1}/min, path {pathSum / 4f:F1} L/min");
            TestContext.Out.WriteLine(log.ToString());
            Assert.LessOrEqual(bad, 1, "the octopus does not traverse the grove (see the log)");
            Assert.GreaterOrEqual(swSum / 4f, 12f, "too few swings"); Assert.GreaterOrEqual(pathSum / 4f, 18f, "too little travel");
        }
    }
}
