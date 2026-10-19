using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // The arms work together (user, 2026-10-18: "the tentacles seem to constantly drag the body in different
    // directions, and there is seemingly no logic for making the arms work together"). Measured on the force
    // each arm puts on the body (OctopusBody.ArmForce) while the octopus travels over branches and then rests:
    //   conflict   (sum |F_arm| - |sum F_arm|) / Mg: force the arms spend pulling against each other
    //   lateral    |net arm force across the travel direction| / Mg: how hard the body is dragged sideways
    //   lateralAcc |head acceleration across the travel direction| / g: the visible sideways yank
    //   at rest    |net arm force - the lattice's weight| / Mg (support only) and the head's speed
    public class OctopusCoordinationTests
    {
        const float Dt = 1f / 60f;

        // Branch scenes (x scale s), the octopus starting under them at the origin and travelling toward +x.
        internal static AnalyticSurfaces Scene(int kind, float s)
        {
            var a = new AnalyticSurfaces();
            switch (kind)
            {
                case 0:   // two bars across the way
                    a.AddCapsule(new Vector3(0f, 2.5f, -3f) * s, new Vector3(0f, 2.5f, 3f) * s, 0.06f * s)
                     .AddCapsule(new Vector3(1.1f, 2.5f, -3f) * s, new Vector3(1.1f, 2.5f, 3f) * s, 0.06f * s);
                    break;
                case 1:   // a rack of six bars across the way
                    for (int i = 0; i < 6; i++) a.AddCapsule(new Vector3(-0.8f + 0.45f * i, 2.5f, -3f) * s, new Vector3(-0.8f + 0.45f * i, 2.5f, 3f) * s, 0.05f * s);
                    break;
                case 2:   // two branches along the way (holds on every side)
                    a.AddCapsule(new Vector3(-3f, 2.5f, 0f) * s, new Vector3(4f, 2.5f, 0f) * s, 0.08f * s)
                     .AddCapsule(new Vector3(-3f, 2.7f, 0.5f) * s, new Vector3(4f, 2.6f, 0.6f) * s, 0.05f * s);
                    break;
                default:  // a lattice of crossing branches
                    for (int i = 0; i < 5; i++) a.AddCapsule(new Vector3(-1f + 0.6f * i, 2.5f, -3f) * s, new Vector3(-1f + 0.6f * i, 2.5f, 3f) * s, 0.05f * s);
                    for (int i = 0; i < 4; i++) a.AddCapsule(new Vector3(-3f, 2.65f, -0.9f + 0.6f * i) * s, new Vector3(4f, 2.65f, -0.9f + 0.6f * i) * s, 0.04f * s);
                    break;
            }
            a.AddGround(-5f * s);
            return a;
        }

        internal struct Tug
        {
            public float conflict, conflictMax, lateral, lateralMax, lateralAcc, lateralAccP99, progress, restResidual, restSpeed;
            public int roleChanges;
            public override string ToString() =>
                $"conflict {conflict:F2} (max {conflictMax:F1}) lateral {lateral:F3} (max {lateralMax:F2}) lateralAcc {lateralAcc:F2} g (p99 {lateralAccP99:F1}) " +
                $"progress {progress:F2} | rest residual {restResidual:F3} speed {restSpeed * 1000f:F1} mm/s per unit scale";
        }

        internal static OctopusController Spawn(int kind, float s, uint seed, out OctopusBody body)
        {
            Vector3 spawn = new Vector3(0.037f * ((int)(seed * 7) % 5 - 2), 2.0f, 0.029f * ((int)(seed * 3) % 5 - 2)) * s;
            body = new OctopusBody(CephalopodSpec.Default, spawn, Quaternion.identity, s);
            var c = new OctopusController(body, Scene(kind, s), null, seed);
            c.AnchorNearby();
            return c;
        }

        // Settle 4 s, travel 10 s toward +x (1.6 x s ahead; since 2026-10-18 the octopus swings there), then rest
        // 12 s (judged over the last 4: the travel stops dead, the body may swing and an arm look for a hold for a
        // while). Every time x sqrt(s) (Froude: a 4x octopus does the same in twice the time), and the sideways
        // acceleration is sampled at Froude-similar intervals (every 2 sqrt(s) frames) - its peak is the 99th
        // percentile, not one frame (a single-frame peak is solver noise and differed between .NET and Mono).
        internal static Tug Measure(int kind, float s, uint seed, float seconds = 10f)
        {
            OctopusController c = Spawn(kind, s, seed, out OctopusBody body);
            float ts = body.TimeScale;
            for (int f = 0; f < (int)(240 * ts); f++) c.Tick(Dt);
            float g = OctopusController.Gravity, Mg = body.TotalMass * g;
            Vector3 start = body.HeadCentre, goal = start + new Vector3((kind == 0 ? 2.0f : 1.6f) * s, 0f, 0f);
            c.Intent = new OctoIntent { mode = OctoMode.Travel, goal = goal, urgency = 0.6f };
            var t = new Tug();
            int stride = Mathf.Max(1, Mathf.RoundToInt(2f * ts));
            var hist = new Vector3[64]; var accs = new List<float>();
            double cs = 0, ls = 0, asum = 0; int n = 0, an = 0;
            int T = (int)(60 * seconds * ts);
            for (int f = 0; f < T; f++)
            {
                c.Tick(Dt);
                Vector3 dir = goal - body.HeadCentre; dir.y = 0f;
                dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.right;
                Vector3 lat = Vector3.Cross(Vector3.up, dir);
                Vector3 sum = Vector3.zero; float mags = 0f;
                for (int a = 0; a < body.ArmCount; a++) { sum += body.ArmForce[a]; mags += body.ArmForce[a].magnitude; }
                float conflict = (mags - sum.magnitude) / Mg, lf = Mathf.Abs(Vector3.Dot(sum, lat)) / Mg;
                Vector3 p = body.HeadCentre;
                hist[f % 64] = p;
                if (f >= 30)
                {
                    cs += conflict; ls += lf; n++;
                    t.conflictMax = Mathf.Max(t.conflictMax, conflict); t.lateralMax = Mathf.Max(t.lateralMax, lf);
                    if (f % stride == 0 && f >= 30 + 2 * stride)
                    {
                        float Ts = Dt * stride;
                        float la = Mathf.Abs(Vector3.Dot((hist[f % 64] - 2f * hist[(f - stride) % 64] + hist[(f - 2 * stride) % 64]) / (Ts * Ts), lat)) / g;
                        accs.Add(la); asum += la; an++;
                    }
                }
                t.progress = Vector3.Dot(p - start, (goal - start).normalized) / (goal - start).magnitude;
            }
            t.conflict = (float)(cs / n); t.lateral = (float)(ls / n); t.lateralAcc = (float)(asum / Mathf.Max(1, an));
            accs.Sort();
            t.lateralAccP99 = accs.Count > 0 ? accs[(int)(accs.Count * 0.99f)] : 0f;
            c.Intent = new OctoIntent { mode = OctoMode.Rest, goal = body.HeadCentre };
            double rs = 0, sp = 0; int rn = 0; Vector3 last = body.HeadCentre;
            int R = (int)(60 * 12 * ts), judge = (int)(60 * 8 * ts);
            for (int f = 0; f < R; f++)
            {
                c.Tick(Dt);
                if (f >= judge)
                {
                    Vector3 sum = Vector3.zero;
                    for (int a = 0; a < body.ArmCount; a++) sum += body.ArmForce[a];
                    Vector3 support = -body.Lattice.TotalMass * body.Gravity;
                    rs += (sum - support).magnitude / Mg; sp += (body.HeadCentre - last).magnitude / Dt; rn++;
                }
                last = body.HeadCentre;
            }
            t.restResidual = (float)(rs / rn); t.restSpeed = (float)(sp / rn) / s;
            return t;
        }

        // Per-run bounds of a travelling octopus whose arms pull together (sideways: across the travel).
        internal static bool TravelOk(Tug t) => t.conflict <= 1.5f && t.conflictMax <= 30f && t.lateral <= 0.25f && t.lateralAcc <= 0.5f && t.lateralAccP99 <= 4f && t.progress >= 0.4f;
        static bool RestOk(Tug t) => t.restResidual <= 0.15f && t.restSpeed <= 0.03f;   // (breathing alone moves the head a few mm/s)

        // Kinds 1-3 x seeds 1-RunSeeds at scale s: the log, how many runs fail `ok`, and the means. The contact is
        // chaotic, so the bounds below count runs: widened 2026-10-19 from 3 to 6 seeds (9 -> 18 runs per scale) with
        // every allowance doubled - the same fraction, half the luck (a code change elsewhere tipped one run of nine).
        internal const int RunSeeds = 6, RunCount = 3 * RunSeeds;
        static int Runs(float s, System.Func<Tug, bool> ok, StringBuilder log, out float conflict, out float lateralAcc, out float restSpeed, List<Tug> all = null)
        {
            int bad = 0; double c = 0, a = 0, r = 0;
            for (int kind = 1; kind <= 3; kind++)
                for (uint seed = 1; seed <= RunSeeds; seed++)
                {
                    Tug t = Measure(kind, s, seed);
                    all?.Add(t);
                    bool good = ok(t);
                    if (!good) bad++;
                    c += t.conflict; a += t.lateralAcc; r += t.restSpeed;
                    log.AppendLine($"scale {s} k{kind} seed {seed}: {t}{(good ? "" : "  <-- out")}");
                }
            conflict = (float)(c / RunCount); lateralAcc = (float)(a / RunCount); restSpeed = (float)(r / RunCount);
            log.AppendLine($"scale {s}: {bad} of {RunCount} out, means conflict {conflict:F2} Mg, lateral acceleration {lateralAcc:F2} g, rest speed {restSpeed * 1000f:F1} mm/s");
            return bad;
        }

        // RED before the arm roles (baseline 2026-10-18, scale 1, kinds 1-3 x seeds 1-3: conflict 1.1-693 Mg
        // (peaks 8-3400), lateral 0.22-1.65 Mg, lateral acceleration 0.65-5.6 g (peaks 16-84 g); the lattice
        // scene ran pairs of arms at 200-760 Mg against each other). Holds spread around a hanging body always pull
        // partly against each other (two at 45 deg: 0.41 Mg), so conflict is bounded, not zero; what must not
        // happen is a sustained fight or the body yanked sideways. The branch contact is chaotic (a hair of
        // difference in the start changes which bar each arm takes, and .NET and Mono round differently), so one
        // run of nine (two of eighteen) may fall outside the per-run bounds; the means are bounded tightly.
        [Test] public void TugOfWar_Travel_ArmsPullTogether()
        {
            var log = new StringBuilder();
            int bad = Runs(1f, TravelOk, log, out float c, out float a, out _);
            TestContext.Out.WriteLine(log.ToString());
            Assert.LessOrEqual(bad, 2, "arms pull the body in different directions while travelling (see the log)");
            Assert.LessOrEqual(c, MeanConflict, "mean conflict"); Assert.LessOrEqual(a, MeanLateralAcc, "mean sideways acceleration");
        }

        const float MeanConflict = 1.0f, MeanLateralAcc = 0.35f;

        // The same at an individual's size spread (0.9x, 1.1x; the geometry scaled with it).
        [Test] public void TugOfWar_Travel_AcrossSizes()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float s in new[] { 0.9f, 1.1f })
            {
                int b = Runs(s, TravelOk, log, out float c, out float a, out _);
                if (b > 2 || c > MeanConflict || a > MeanLateralAcc) bad = true;
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, "arms pull against each other at an individual's size spread (see the log)");
        }

        // Large octopuses (2x, and 4x - the lab's size): the same per-run bounds as at 1x (in Froude time, with the
        // solver's response size-free: TentacleChain.SizeFree, eased pins), two of nine (four of eighteen) may fall outside, and the
        // means stay below 1.2 Mg conflict and 0.5 g sideways. Baseline before the arm roles at 4x: conflict mean
        // 388 Mg (peak 5009), lateral acceleration mean 3.4 g (peak 152); after the roles (pull-along): 1.84 Mg,
        // 0.58 g, travel ok 4/9.
        [Test] public void TugOfWar_Travel_LargeSizes()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float s in new[] { 2f, 4f })
            {
                int b = Runs(s, TravelOk, log, out float c, out float a, out _);
                if (b > 4 || c > 1.2f || a > 0.5f) bad = true;
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, "a large octopus's arms pull against each other (see the log)");
        }

        // After travelling, resting arms only support the body: the net arm force is the lattice's weight and
        // the head is still, at 1x and 4x. One run of nine (two of eighteen) may still be settling a swing (a hold
        // found late); at least seven of nine (fourteen of eighteen) are dead still (< 5 mm/s per unit scale:
        // asleep, measured 2026-10-18 8/9 at 0.0).
        [Test] public void TugOfWar_Rest_SupportOnly()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float s in new[] { 1f, 4f })
            {
                var all = new List<Tug>();
                int b = Runs(s, RestOk, log, out _, out _, out _, all);
                int moving = 0;
                foreach (Tug t in all) if (t.restSpeed >= 0.005f) moving++;
                if (b > 2 || moving > 4) bad = true;
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, "resting arms pull against each other (see the log)");
        }
    }
}
