using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Arm roles (user decision 5; plan 2026-10-18 T1-T6): the octopus (ArmCoordinator) decides what each arm is
    // for, each arm executes its role itself. Analytic surfaces; thresholds x SizeScale, times x TimeScale.
    public class OctopusRoleTests
    {
        const float Dt = 1f / 60f;

        static OctopusController Hanging(int kind, float s, uint seed, out OctopusBody body)
        {
            OctopusController c = OctopusCoordinationTests.Spawn(kind, s, seed, out body);
            for (int f = 0; f < 240; f++) c.Tick(Dt);
            return c;
        }

        static Vector3 Travel(OctopusController c, OctopusBody body, float s, float urgency = 0.6f)
        {
            Vector3 goal = body.HeadCentre + new Vector3(1.6f * s, 0f, 0f);
            c.Intent = new OctoIntent { mode = OctoMode.Travel, goal = goal, urgency = urgency };
            return goal;
        }

        static float Facing(OctopusController c, int a, Vector3 dir) =>
            Vector3.Dot(ArmCoordinator.Level(OctoMath.Rotate(c.Body.Orientation, OctopusShape.ArmDir(c.Body.Spec, a)), c.Up), ArmCoordinator.Level(dir, c.Up));

        // Travelling, the grip arms are the ones facing the route: on average they face it much more than the rest.
        [Test] public void Roles_FacingRoute_Grip()
        {
            foreach (float s in new[] { 1f, 4f })
                for (uint seed = 1; seed <= 3; seed++)
                {
                    OctopusController c = Hanging(1, s, seed, out OctopusBody body);
                    Travel(c, body, s);
                    double gripF = 0, otherF = 0; int gn = 0, on = 0, grips = 0, samples = 0;
                    for (int f = 0; f < 60 * 6; f++)
                    {
                        c.Tick(Dt);
                        if (f % 15 != 0) continue;
                        Vector3 R = c.Coordinator.RouteDir;
                        int g = 0;
                        for (int a = 0; a < 8; a++)
                        {
                            float fa = Facing(c, a, R);
                            if (c.Board.Role[a] == ArmRole.Grip) { gripF += fa; gn++; g++; } else { otherF += fa; on++; }
                        }
                        grips += g; samples++;
                    }
                    float gm = (float)(gripF / gn), om = (float)(otherF / on), avg = grips / (float)samples;
                    TestContext.Out.WriteLine($"scale {s} seed {seed}: grip arms face the route {gm:F2}, the others {om:F2}, grips per sample {avg:F1}");
                    Assert.Greater(gm, om + 0.4f, $"scale {s} seed {seed}: the grip arms do not face the route");
                    Assert.That(avg, Is.InRange(1.0f, 4.0f), $"scale {s} seed {seed}: grip quota");
                }
        }

        // The coordinator never leaves fewer arms committed to holding (Anchor / Grip) than the support needs:
        // over 10 000+ snapshots of travelling and resting in the branch scenes.
        [Test] public void Roles_SupportNeverBelowMin()
        {
            int snapshots = 0, bad = 0; string first = "";
            for (int kind = 0; kind <= 3; kind++)
                for (uint seed = 1; seed <= 3; seed++)
                {
                    OctopusController c = Hanging(kind, 1f, seed, out OctopusBody body);
                    Travel(c, body, 1f);
                    int lapse = 0;
                    for (int f = 0; f < 60 * 16; f++)
                    {
                        if (f == 60 * 10) c.Intent = new OctoIntent { mode = OctoMode.Rest, goal = body.HeadCentre };
                        c.Tick(Dt);
                        ArmBlackboard b = c.Board;
                        int strikers = 0;
                        for (int a = 0; a < 8; a++) if (b.Role[a] == ArmRole.Strike) strikers++;
                        int need = Mathf.Min(b.AnchoredCount, b.MinAnchors) - strikers;
                        snapshots++;
                        // (an arm that grips this tick under a role that does not keep holds is taken into support
                        // by the coordinator next tick: a one-tick lapse is the order of the tick, not a decision)
                        if (b.CommittedSupport < need) lapse++; else lapse = 0;
                        if (lapse > 1)
                        {
                            bad++;
                            if (first == "") { first = $"kind {kind} seed {seed} t {f / 60f:F2}: committed {b.CommittedSupport}, anchored {b.AnchoredCount}, min {b.MinAnchors}, roles"; for (int a = 0; a < 8; a++) first += $" {b.Role[a]}/{c.Agents[a].Mode}{(b.Anchored[a] ? "*" : "")}"; }
                        }
                    }
                }
            TestContext.Out.WriteLine($"{snapshots} snapshots, {bad} below the support minimum {first}");
            Assert.GreaterOrEqual(snapshots, 10000);
            Assert.AreEqual(0, bad, "support below the minimum: " + first);
        }

        sealed class StillPrey : IPreyTarget
        {
            public Vector3 Pos;
            public Vector3 Position => Pos;
            public Vector3 Velocity => Vector3.zero;
            public void GrabVolume(out Vector3 a, out Vector3 b, out float r) { a = Pos - Vector3.right * 0.1f; b = Pos + Vector3.right * 0.1f; r = 0.05f; }
            public bool CanBeGrabbed => true;
            public void OnGrabbed() { }
            public void OnReleased() { }
        }

        // Strike: the strike roles go, in the same tick, to the arms whose roots are nearest the intercept.
        [Test] public void Roles_StrikeNearest_PreemptWithinTick()
        {
            OctopusController c = Hanging(1, 1f, 2, out OctopusBody body);
            var prey = new StillPrey { Pos = body.HeadCentre + new Vector3(0.3f, -0.9f, 0.4f) };
            c.Intent = new OctoIntent { mode = OctoMode.Strike, prey = prey, intercept = prey.Pos, strikers = 2, goal = body.HeadCentre };
            c.Tick(Dt);
            var dist = new List<(float d, int a)>();
            for (int a = 0; a < 8; a++) dist.Add(((body.Arms[a].X[0] - prey.Pos).magnitude, a));
            dist.Sort();
            var strikers = new HashSet<int>();
            for (int a = 0; a < 8; a++) if (c.Board.Role[a] == ArmRole.Strike) strikers.Add(a);
            TestContext.Out.WriteLine($"strike roles {string.Join(",", strikers)}; nearest {dist[0].a},{dist[1].a}");
            Assert.AreEqual(2, strikers.Count, "not two strikers in the first tick");
            Assert.IsTrue(strikers.Contains(dist[0].a) && strikers.Contains(dist[1].a), "the strikers are not the nearest arms");
        }

        static long RoleHash(uint seed)
        {
            OctopusController c = Hanging(3, 1f, seed, out OctopusBody body);
            Travel(c, body, 1f);
            long h = 17;
            for (int f = 0; f < 60 * 8; f++)
            {
                c.Tick(Dt);
                for (int a = 0; a < 8; a++) h = h * 31 + (int)c.Board.Role[a];
            }
            return h;
        }

        [Test] public void Roles_Deterministic() => Assert.AreEqual(RoleHash(4), RoleHash(4));

        // Hysteresis: a minute of travelling and resting (out, rest, out, rest). Travelling hand over hand changes
        // roles by design (a grip arm done pulling becomes support, a spare hold behind is let go, a free arm is
        // sent ahead) - more than the plan's first guess of 3 per arm per minute - so what is bounded is the churn
        // overall and the flicker: a role changed again within half the minimum hold (only forced changes - a
        // support emergency, a failure, a finished pull - can do that). With the hysteresis off (no minimum hold,
        // no stickiness) the same runs flicker many times more: the test bites.
        internal static readonly Dictionary<string, int> Hist = new Dictionary<string, int>();

        static (float perArmPerMin, int flickers, int changes) Churn(float stick, float hold)
        {
            Hist.Clear();
            int changes = 0, flickers = 0; float minutes = 0f;
            for (uint seed = 1; seed <= 3; seed++)
            {
                OctopusController c = Hanging(3, 1f, seed, out OctopusBody body);
                c.Coordinator.Stickiness = stick; c.Coordinator.MinHold = hold;
                var last = new ArmRole[8]; var since = new float[8];
                for (int a = 0; a < 8; a++) { last[a] = c.Board.Role[a]; since[a] = -100f; }
                for (int f = 0; f < 60 * 60; f++)
                {
                    if (f % (60 * 15) == 0)
                    {
                        // travel out and back, rest in between
                        if ((f / (60 * 15)) % 2 == 0) Travel(c, body, 1f);
                        else c.Intent = new OctoIntent { mode = OctoMode.Rest, goal = body.HeadCentre };
                    }
                    c.Tick(Dt);
                    for (int a = 0; a < 8; a++)
                    {
                        if (c.Board.Role[a] == last[a]) continue;
                        changes++;
                        string key = last[a] + ">" + c.Board.Role[a] + (c.Time - since[a] < 0.5f * ArmCoordinator.MinHoldSeconds ? "!" : "");
                        Hist.TryGetValue(key, out int hv); Hist[key] = hv + 1;
                        if (c.Time - since[a] < 0.5f * ArmCoordinator.MinHoldSeconds) flickers++;
                        last[a] = c.Board.Role[a]; since[a] = c.Time;
                    }
                }
                minutes += 1f;
            }
            return (changes / (8f * minutes), flickers, changes);
        }

        [Test] public void Roles_NoFlipFlop()
        {
            var on = Churn(0.25f, ArmCoordinator.MinHoldSeconds);
            var hist = new List<string>();
            foreach (var kv in Hist) hist.Add($"{kv.Key} {kv.Value}");
            hist.Sort();
            TestContext.Out.WriteLine("transitions with hysteresis: " + string.Join(", ", hist));
            var off = Churn(0f, 0f);
            TestContext.Out.WriteLine($"with hysteresis: {on.perArmPerMin:F1} changes / arm / min, {on.flickers} flickers; without: {off.perArmPerMin:F1}, {off.flickers}");
            Assert.LessOrEqual(on.perArmPerMin, 18f, "roles churn");
            Assert.LessOrEqual(on.flickers, 0.12f * on.changes, "roles flicker");
            Assert.Greater(off.flickers, 3 * on.flickers, "the test does not bite: hysteresis makes no difference");
        }

        // An arm that reports Failed gets a new role in that tick and is not given the failed role again during
        // the cool-down.
        [Test] public void Roles_FailureReassigns()
        {
            OctopusController c = Hanging(1, 1f, 1, out OctopusBody body);
            Travel(c, body, 1f);
            for (int f = 0; f < 90; f++) c.Tick(Dt);
            int arm = -1;
            for (int a = 0; a < 8; a++) if (c.Board.Role[a] == ArmRole.Grip) { arm = a; break; }
            Assert.GreaterOrEqual(arm, 0, "no grip arm");
            c.Board.Report[arm] = ArmReport.Failed;
            c.Tick(Dt);
            Assert.AreNotEqual(ArmRole.Grip, c.Board.Role[arm], "the failed arm kept its role");
            for (int f = 0; f < (int)(60 * 0.9f * ArmCoordinator.FailCooldownSeconds); f++)
            {
                c.Tick(Dt);
                Assert.AreNotEqual(ArmRole.Grip, c.Board.Role[arm], $"the failed role came back during the cool-down ({f} ticks)");
            }
        }

        // A grip arm that finds nothing (travel toward empty space) reports Failed and is reassigned.
        [Test] public void Agent_GripFailsInVoid_Reports()
        {
            var s = new AnalyticSurfaces().AddCapsule(new Vector3(0f, 2.5f, -0.4f), new Vector3(0f, 2.5f, 0.4f), 0.06f).AddGround(-5f);
            var body = new OctopusBody(CephalopodSpec.Default, new Vector3(0f, 2.0f, 0f), Quaternion.identity);
            var c = new OctopusController(body, s, null, 1);
            Assert.GreaterOrEqual(c.AnchorNearby(), 2);
            for (int f = 0; f < 240; f++) c.Tick(Dt);
            c.Intent = new OctoIntent { mode = OctoMode.Travel, goal = new Vector3(4f, 2f, 0f), urgency = 0.6f };
            for (int f = 0; f < 60 * 12; f++) c.Tick(Dt);
            TestContext.Out.WriteLine($"failed reports {c.Coordinator.FailedReports}, head {body.HeadCentre}");
            Assert.Greater(c.Coordinator.FailedReports, 0, "no grip arm reported failure in the void");
            Assert.Greater(body.HeadCentre.y, 1.0f, "fell");
        }

        static OctopusController Perched(out OctopusBody body)
        {
            var s = new AnalyticSurfaces().AddCapsule(new Vector3(-0.5f, 2.5f, -3f), new Vector3(0.5f, 2.5f, 3f), 0.1f).AddGround(-5f);
            body = new OctopusBody(CephalopodSpec.Default, new Vector3(0f, 2f, 0.029f), Quaternion.identity);
            var c = new OctopusController(body, s, null, 1);
            Assert.GreaterOrEqual(c.AnchorNearby(), 2);
            for (int f = 0; f < 240; f++) c.Tick(Dt);
            c.Intent = new OctoIntent { mode = OctoMode.Rest, goal = body.HeadCentre };
            for (int f = 0; f < 120; f++) c.Tick(Dt);
            return c;
        }

        static int FreeArm(OctopusController c)
        {
            for (int a = 0; a < 8; a++) if (!c.Board.Anchored[a] && c.Agents[a].Mode == TentacleAgent.State.Curl) return a;
            return -1;
        }

        // Hang: a limp arm drapes down - its tip ends below its root.
        [Test] public void Hang_TipBelowRoot()
        {
            OctopusController c = Perched(out OctopusBody body);
            int a = FreeArm(c);
            Assert.GreaterOrEqual(a, 0, "no free arm");
            c.Coordinator.Forced[a] = (int)ArmRole.Hang;
            for (int f = 0; f < 60 * 4; f++) c.Tick(Dt);
            TentacleChain ch = body.Arms[a];
            float drop = ch.X[0].y - ch.X[ch.N - 1].y;
            TestContext.Out.WriteLine($"arm {a} hangs: tip {drop:F2} m below its root");
            Assert.AreEqual(ArmRole.Hang, c.Board.Role[a]);
            Assert.Greater(drop, 0.5f * ch.Length, "the hanging arm's tip is not below its root");
        }

        // Tuck: curled compactly under the body - the whole arm stays close to the head centre.
        [Test] public void Tuck_Compact()
        {
            OctopusController c = Perched(out OctopusBody body);
            int a = FreeArm(c);
            Assert.GreaterOrEqual(a, 0, "no free arm");
            float before = 0f;
            for (int k = 0; k < body.Arms[a].N; k++) before = Mathf.Max(before, (body.Arms[a].X[k] - body.HeadCentre).magnitude);
            c.Coordinator.Forced[a] = (int)ArmRole.Tuck;
            for (int f = 0; f < 60 * 4; f++) c.Tick(Dt);
            TentacleChain ch = body.Arms[a];
            float reach = 0f;
            for (int k = 0; k < ch.N; k++) reach = Mathf.Max(reach, (ch.X[k] - body.HeadCentre).magnitude);
            TestContext.Out.WriteLine($"arm {a}: farthest particle {before:F2} m from the head curled, {reach:F2} m tucked (arm {ch.Length:F2} m)");
            Assert.Less(reach, 0.7f * ch.Length, "the tucked arm sprawls");
        }

        // Feel: with the support met, a feeling arm explores but never commits to a hold.
        [Test] public void Feel_DoesNotGrip_WhenSupported()
        {
            OctopusController c = Perched(out OctopusBody body);
            int a = FreeArm(c);
            Assert.GreaterOrEqual(a, 0, "no free arm");
            Assert.Greater(c.Board.AnchoredCount, c.Board.MinAnchors, "support not met: test does not apply");
            int plans = c.Agents[a].GripPlans;
            c.Coordinator.Forced[a] = (int)ArmRole.Feel;
            int feeling = 0;
            for (int f = 0; f < 60 * 10; f++)
            {
                c.Tick(Dt);
                if (c.Agents[a].Mode == TentacleAgent.State.Feel) feeling++;
            }
            TestContext.Out.WriteLine($"arm {a}: feeling {feeling} of 600 ticks, grip plans {c.Agents[a].GripPlans - plans}");
            Assert.Greater(feeling, 300, "the arm did not feel about");
            Assert.AreEqual(plans, c.Agents[a].GripPlans, "a feeling arm committed to a hold although the support was met");
        }

        // Groom over the mantle: the tip strokes along the shell, standing off it (never inside the body).
        [Test] public void Groom_TipOnMantle_NoPenetration()
        {
            OctopusController c = Perched(out OctopusBody body);
            int a = FreeArm(c);
            Assert.GreaterOrEqual(a, 0, "no free arm");
            c.Coordinator.Forced[a] = (int)ArmRole.Groom;
            TentacleAgent ag = c.Agents[a];
            float nearest = float.MaxValue, inside = 0f; int grooming = 0;
            for (int f = 0; f < 60 * 6; f++)
            {
                c.Tick(Dt);
                if (ag.Mode != TentacleAgent.State.Groom) continue;
                grooming++;
                Vector3 tip = ag.Arm.X[ag.Arm.N - 1];
                // distance to the shell: the tip in the head frame vs the shell radius at its axial parameter
                float d = DistanceToShell(body, tip);
                nearest = Mathf.Min(nearest, Mathf.Abs(d));
                inside = Mathf.Min(inside, d);
            }
            TestContext.Out.WriteLine($"arm {a}: grooming {grooming} ticks, nearest the shell {nearest * 100f:F1} cm, deepest {inside * 100f:F1} cm, strokes {ag.GroomStrokes}");
            Assert.Greater(grooming, 60, "never groomed");
            Assert.Less(nearest, 0.25f * ag.Arm.Length, "the grooming tip never came near the mantle");
            Assert.Greater(inside, -0.02f, "the grooming tip went inside the body");
        }

        static float DistanceToShell(OctopusBody body, Vector3 p)
        {
            Quaternion q = body.Orientation, inv = new Quaternion(-q.x, -q.y, -q.z, q.w);   // (unit: the conjugate)
            Vector3 local = (OctoMath.Rotate(inv, p - body.HeadCentre) + body.Lattice.ClusterRestCentre[OctopusShape.ClusterHead]) / body.SizeScale;
            return OctopusShape.ShellDistance(body.Spec, local, out _) * body.SizeScale;
        }

        // Rest variety: over a long rest the spare arms take more than one idle role, and the body sleeps in the
        // quiet windows between the bursts.
        [Test] public void Roles_RestVariety_SleepsBetweenBursts()
        {
            OctopusController c = Perched(out OctopusBody body);
            var seen = new HashSet<ArmRole>();
            int asleep = 0, ticks = 0;
            for (int f = 0; f < 60 * 150; f++)
            {
                c.Tick(Dt);
                for (int a = 0; a < 8; a++) if (!c.Board.Anchored[a]) seen.Add(c.Board.Role[a]);
                if (f > 60 * 10) { ticks++; if (body.Asleep) asleep++; }
            }
            TestContext.Out.WriteLine($"idle roles seen: {string.Join(", ", seen)}; asleep {asleep * 100 / ticks}% of the rest");
            Assert.GreaterOrEqual(seen.Count, 3, "no variety at rest");
            Assert.Greater(asleep, ticks / 4, "the body hardly sleeps between bursts");
        }
    }
}
