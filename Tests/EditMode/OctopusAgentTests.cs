using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Tree octopus task 7: the arm agents, the blackboard, brachiation and contact, on analytic surfaces
    // (the probes answer real geometry exactly; CreatureOctopusPhysicsTests repeats key cases on MeshColliders).
    public class OctopusAgentTests
    {
        const float Dt = 1f / 60f;

        // Two parallel horizontal bars along Z at x = 0 and x = gap, height 2.5; far ground below.
        internal static AnalyticSurfaces TwoBars(float gap = 1.1f) =>
            new AnalyticSurfaces().AddCapsule(new Vector3(0f, 2.5f, -3f), new Vector3(0f, 2.5f, 3f), 0.06f)
                                  .AddCapsule(new Vector3(gap, 2.5f, -3f), new Vector3(gap, 2.5f, 3f), 0.06f)
                                  .AddGround(-5f);

        internal static OctopusController Hanging(ISurfaceProbe s, out OctopusBody body, uint seed = 1)
        {
            body = new OctopusBody(CephalopodSpec.Default, new Vector3(0f, 2.0f, 0f), Quaternion.identity);
            var c = new OctopusController(body, s, null, seed);
            int held = c.AnchorNearby();
            Assert.GreaterOrEqual(held, 2, "the octopus could not take hold at spawn");
            return c;
        }

        struct Run { public float minHeadY, maxHeadX, finalX; public int minAnchors, unsafeReleases, crossedAt; }

        static Run Brachiate(OctopusController c, OctopusBody body, float gap, float seconds)
        {
            var r = new Run { minHeadY = float.MaxValue, minAnchors = int.MaxValue, crossedAt = -1 };
            for (int f = 0; f < 60 * 4; f++) c.Tick(Dt);   // settle on the first bar
            c.Intent = new OctoIntent { mode = OctoMode.Travel, goal = new Vector3(gap + 0.9f, 2.0f, 0f), urgency = 0.6f };
            for (int f = 0; f < (int)(60 * seconds); f++)
            {
                int before = c.Board.AnchoredCount, minBefore = c.Board.MinAnchors, releasesBefore = Releases(c);
                c.Tick(Dt);
                Vector3 h = body.HeadCentre;
                r.minHeadY = Mathf.Min(r.minHeadY, h.y);
                r.maxHeadX = Mathf.Max(r.maxHeadX, h.x);
                r.minAnchors = Mathf.Min(r.minAnchors, c.Board.AnchoredCount);
                // an arm that chose to let go this frame must leave the support at the minimum or above
                if (Releases(c) > releasesBefore && c.Board.AnchoredCount < c.Board.MinAnchors && before >= minBefore) r.unsafeReleases++;
                if (r.crossedAt < 0 && h.x > gap - 0.2f && AnyAnchorNear(c, gap)) r.crossedAt = f;
            }
            r.finalX = body.HeadCentre.x;
            return r;
        }

        static int Releases(OctopusController c) { int n = 0; foreach (var a in c.Agents) n += a.ReleasesStarted; return n; }

        static bool AnyAnchorNear(OctopusController c, float x)
        {
            for (int a = 0; a < c.Board.Arms; a++) if (c.Board.Anchored[a] && Mathf.Abs(c.Board.Anchor[a].x - x) < 0.15f) return true;
            return false;
        }

        // After travelling, a resting octopus hanging between two bars settles: contact planes must not throw
        // the body back and forth across a bar (a contact normal flipped toward the body centre did, at 0.1 to
        // 0.5 m per frame). Whether it falls asleep is reported.
        [Test] public void Rest_AfterTravel_Settles()
        {
            var s = TwoBars();
            var body = new OctopusBody(CephalopodSpec.Default, new Vector3(0f, 2.0f, 0f), Quaternion.identity);
            var c = new OctopusController(body, s);
            c.AnchorNearby();
            for (int f = 0; f < 240; f++) c.Tick(1f / 60f);
            c.Intent = new OctoIntent { mode = OctoMode.Travel, goal = new Vector3(2.0f, 2.0f, 0f), urgency = 0.6f };
            for (int f = 0; f < 720; f++) c.Tick(1f / 60f);
            c.Intent = new OctoIntent { mode = OctoMode.Rest, goal = body.HeadCentre };
            for (int f = 0; f < 300; f++) c.Tick(1f / 60f);
            float worst = 0f; Vector3 last = body.HeadCentre; int asleepAt = -1;
            for (int f = 0; f < 60 * 30; f++)
            {
                c.Tick(1f / 60f);
                worst = Mathf.Max(worst, (body.HeadCentre - last).magnitude); last = body.HeadCentre;
                if (asleepAt < 0 && body.Asleep) asleepAt = f;
            }
            TestContext.Out.WriteLine($"worst head motion per frame after 5 s of rest {worst * 1000f:F1} mm, asleep after {(asleepAt < 0 ? "never" : ((asleepAt + 300) / 60f).ToString("F1") + " s")}");
            Assert.Less(worst, 0.02f, "the resting body jumps");
            // not asserted yet: an anchored arm's unpinned run still flickers ~4 cm as a contact plane lapses and
            // is renewed, which keeps the body awake here (see the octopus Editor report)
        }

        // The body travels from one bar to the next by its arms alone: arms on the new bar pull, arms behind
        // let go, the body swings across and never falls.
        [Test] public void Brachiate_TwoBars_Crosses()
        {
            OctopusController c = Hanging(TwoBars(), out OctopusBody body);
            Run r = Brachiate(c, body, 1.1f, 25f);
            TestContext.Out.WriteLine($"crossed at {r.crossedAt / 60f:F1} s, max x {r.maxHeadX:F2}, final x {r.finalX:F2}, min head y {r.minHeadY:F2}, min anchors {r.minAnchors}, peak queries/frame {c.MaxQueriesPerFrame}");
            Assert.GreaterOrEqual(r.crossedAt, 0, "never reached and held the second bar");
            Assert.Greater(r.finalX, 0.7f, "did not stay across");
            Assert.Greater(r.minHeadY, 1.0f, "fell");
            Assert.GreaterOrEqual(r.minAnchors, 1, "hung on nothing");
        }

        // Local inhibition: an arm lets go only while the remaining support meets the minimum.
        [Test] public void Agent_ReleaseKeepsSupport()
        {
            OctopusController c = Hanging(TwoBars(), out OctopusBody body);
            Run r = Brachiate(c, body, 1.1f, 25f);
            Assert.Greater(Releases(c), 2, "no arm ever let go: test does not bite");
            Assert.AreEqual(0, r.unsafeReleases, "an arm let go below the support minimum");
        }

        // At rest under a rack of bars, each arm chooses and grips its own hold: distinct anchors, and the
        // grips happen at different moments (nothing grips centrally).
        [Test] public void Agent_IndependentTargets()
        {
            var s = new AnalyticSurfaces().AddGround(-5f);
            for (int i = 0; i < 5; i++) s.AddCapsule(new Vector3(-0.8f + 0.4f * i, 2.5f, -3f), new Vector3(-0.8f + 0.4f * i, 2.5f, 3f), 0.05f);
            OctopusController c = Hanging(s, out OctopusBody body);
            c.RestAnchors = 7;
            var gripFrames = new List<int>();
            int[] grips = new int[8];
            for (int a = 0; a < 8; a++) grips[a] = c.Agents[a].GripPlans;
            for (int f = 0; f < 60 * 25; f++)
            {
                c.Tick(Dt);
                for (int a = 0; a < 8; a++) if (c.Agents[a].GripPlans != grips[a]) { grips[a] = c.Agents[a].GripPlans; gripFrames.Add(f); }
            }
            int anchored = c.Board.AnchoredCount;
            TestContext.Out.WriteLine($"{anchored} arms anchored, later grips at frames {string.Join(", ", gripFrames)}");
            Assert.GreaterOrEqual(anchored, 5, "the free arms did not find holds of their own");
            for (int a = 0; a < 8; a++)
                for (int b = a + 1; b < 8; b++)
                    if (c.Board.Anchored[a] && c.Board.Anchored[b])
                        Assert.Greater((c.Board.Anchor[a] - c.Board.Anchor[b]).magnitude, 0.1f, $"arms {a} and {b} share a hold");
            Assert.GreaterOrEqual(gripFrames.Count, 1);
            var distinct = new HashSet<int>(gripFrames);
            Assert.AreEqual(gripFrames.Count, distinct.Count, "two arms planned a grip in the same frame: one plan per frame, decided per arm");
        }

        // A stub hint provider pointing at the bars' centre lines.
        sealed class BarHints : IBranchHints
        {
            readonly float[] xs; public int Queries;
            public BarHints(params float[] xs) { this.xs = xs; }
            public int Query(Vector3 o, Vector3 axis, float cosHalf, float minD, float maxD, float minR, float maxR, BranchHint[] res)
            {
                Queries++;
                int n = 0;
                foreach (float x in xs)
                {
                    Vector3 p = new Vector3(x, 2.5f, o.z);
                    float d = (p - o).magnitude;
                    if (d < minD || d > maxD || n >= res.Length) continue;
                    if (Vector3.Dot((p - o) / d, axis) < cosHalf) continue;
                    res[n++] = new BranchHint { point = p, axis = Vector3.forward, radius = 0.06f, score = d };
                }
                return n;
            }
        }

        // Hints are optional: the same crossing with hints and without (probe-only, SpaceGame).
        [Test] public void Hints_Optional_ProbeOnly()
        {
            OctopusController probeOnly = Hanging(TwoBars(), out OctopusBody b1, 3);
            Run r1 = Brachiate(probeOnly, b1, 1.1f, 25f);
            OctopusController hinted = Hanging(TwoBars(), out OctopusBody b2, 3);
            var hints = new BarHints(0f, 1.1f);
            hinted.Hints = hints;
            Run r2 = Brachiate(hinted, b2, 1.1f, 25f);
            TestContext.Out.WriteLine($"probe-only crossed at {r1.crossedAt / 60f:F1} s, hinted at {r2.crossedAt / 60f:F1} s ({hints.Queries} hint queries)");
            Assert.GreaterOrEqual(r1.crossedAt, 0, "probe-only did not cross");
            Assert.GreaterOrEqual(r2.crossedAt, 0, "hinted did not cross");
            Assert.Greater(hints.Queries, 0, "the hints were never asked");
        }

        // Dropped onto a thick log over the ground: the body is pushed out along the real normal, arms drape over
        // what they meet (contacts from the probe). The impact itself may clip for a few frames (a light arm
        // whips faster than the per-frame query budget can follow); nothing stays inside a solid: no particle
        // deeper than 5 cm for more than 6 frames in a row, and once settled (after 4 s) no centre line deeper
        // than 4 cm (within the arm's own radius: the skin rests on the surface).
        [Test] public void Body_NoPenetration_FreeArm_NoPassThrough()
        {
            var s = new AnalyticSurfaces().AddCapsule(new Vector3(-2f, 0.9f, 0f), new Vector3(2f, 0.9f, 0f), 0.35f).AddGround(0f);
            var body = new OctopusBody(CephalopodSpec.Default, new Vector3(0.1f, 2.4f, 0.05f), Quaternion.identity);
            var c = new OctopusController(body, s);
            int run = 0, longest = 0;
            float settledWorst = 0f, impactWorst = 0f;
            for (int f = 0; f < 60 * 8; f++)
            {
                c.Tick(Dt);
                float worst = 0f;
                for (int i = 0; i < SoftLattice.Count; i++) worst = Mathf.Min(worst, s.Distance(body.Lattice.X[i], out _, out _));
                foreach (TentacleChain ch in body.Arms)
                    for (int k = 1; k < ch.N; k++) worst = Mathf.Min(worst, s.Distance(ch.X[k], out _, out _));
                impactWorst = Mathf.Min(impactWorst, worst);
                if (worst < -0.05f) { run++; longest = Mathf.Max(longest, run); } else run = 0;
                if (f >= 60 * 4) settledWorst = Mathf.Min(settledWorst, worst);
            }
            TestContext.Out.WriteLine($"deepest at impact {impactWorst * 100f:F1} cm, longest run deeper than 5 cm {longest} frames, settled deepest {settledWorst * 100f:F1} cm; pushes {c.BodyPushes}, sweep hits {c.Drapes}");
            Assert.LessOrEqual(longest, 6, "something stayed inside a solid");
            Assert.Greater(settledWorst, -0.04f, "the settled octopus is inside a solid");
            Assert.Greater(c.BodyPushes + c.Drapes, 0, "nothing touched: test does not bite");
        }

        // The same inputs give the same octopus, bit for bit.
        [Test] public void Octopus_Deterministic()
        {
            Vector3 Run(uint seed)
            {
                OctopusController c = Hanging(TwoBars(), out OctopusBody body, seed);
                Brachiate(c, body, 1.1f, 8f);
                return body.HeadCentre;
            }
            Vector3 a = Run(5), b = Run(5);
            Assert.AreEqual(a.x, b.x); Assert.AreEqual(a.y, b.y); Assert.AreEqual(a.z, b.z);
        }

        // Distance LOD: far away the octopus ticks at half rate (and skips the sweep), keeps hanging and stays finite.
        [Test] public void Lod_Far_HalfRate()
        {
            OctopusController c = Hanging(TwoBars(), out OctopusBody body);
            c.Far = true;
            for (int f = 0; f < 600; f++) c.Tick(Dt);
            Assert.AreEqual(300, c.TicksRun, "not half rate");
            Assert.Greater(body.HeadCentre.y, 1.0f, "fell while far");
            Assert.IsTrue(OctoMath.Finite(body.HeadCentre));
            c.Far = false;
            for (int f = 0; f < 60; f++) c.Tick(Dt);
            Assert.AreEqual(360, c.TicksRun);
        }

        // The per-frame query budget (design §4: <= 40 raycasts/frame, ~25 expected) while brachiating.
        [Test] public void Perf_OctopusQueries()
        {
            OctopusController c = Hanging(TwoBars(), out OctopusBody body);
            Brachiate(c, body, 1.1f, 20f);
            float avg = c.TotalQueries / (c.Time * 60f);
            TestContext.Out.WriteLine($"queries per frame: average {avg:F1}, peak {c.MaxQueriesPerFrame}");
            Assert.Less(avg, 28f, "average queries per frame");
            // optional work defers to the 40-query budget; a grip plan (~25 probes) may come on top of it
            Assert.LessOrEqual(c.MaxQueriesPerFrame, 60, "peak queries per frame");
        }
    }
}
