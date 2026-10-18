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
            public float conflict, conflictMax, lateral, lateralMax, lateralAcc, lateralAccMax, progress, restResidual, restSpeed;
            public int roleChanges;
            public override string ToString() =>
                $"conflict {conflict:F2} (max {conflictMax:F1}) lateral {lateral:F3} (max {lateralMax:F2}) lateralAcc {lateralAcc:F2} g (max {lateralAccMax:F1}) " +
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

        // Settle 4 s, travel `seconds` toward +x (1.6 x s ahead), then rest 12 s (judged over the last 4: the
        // travel stops dead at full speed, and the body may swing and an arm look for a hold for a while).
        internal static Tug Measure(int kind, float s, uint seed, float seconds = 10f)
        {
            OctopusController c = Spawn(kind, s, seed, out OctopusBody body);
            for (int f = 0; f < 240; f++) c.Tick(Dt);
            float g = OctopusController.Gravity, Mg = body.TotalMass * g;
            Vector3 start = body.HeadCentre, goal = start + new Vector3((kind == 0 ? 2.0f : 1.6f) * s, 0f, 0f);
            c.Intent = new OctoIntent { mode = OctoMode.Travel, goal = goal, urgency = 0.6f };
            var t = new Tug();
            Vector3 p1 = body.HeadCentre, p0 = p1;
            double cs = 0, ls = 0, accs = 0; int n = 0;
            int T = (int)(60 * seconds);
            for (int f = 0; f < T; f++)
            {
                c.Tick(Dt);
                Vector3 dir = goal - body.HeadCentre; dir.y = 0f;
                dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.right;
                Vector3 lat = Vector3.Cross(Vector3.up, dir);
                Vector3 sum = Vector3.zero; float mags = 0f;
                for (int a = 0; a < body.ArmCount; a++) { sum += body.ArmForce[a]; mags += body.ArmForce[a].magnitude; }
                float conflict = (mags - sum.magnitude) / Mg, lf = Mathf.Abs(Vector3.Dot(sum, lat)) / Mg;
                Vector3 p2 = body.HeadCentre;
                // acceleration in units of g, per unit scale (a 4x octopus moves 2x slower in time, 4x farther)
                float la = f >= 2 ? Mathf.Abs(Vector3.Dot((p2 - 2f * p1 + p0) / (Dt * Dt), lat)) / g : 0f;
                p0 = p1; p1 = p2;
                if (f >= 30)
                {
                    cs += conflict; ls += lf; accs += la; n++;
                    t.conflictMax = Mathf.Max(t.conflictMax, conflict); t.lateralMax = Mathf.Max(t.lateralMax, lf); t.lateralAccMax = Mathf.Max(t.lateralAccMax, la);
                }
                t.progress = Vector3.Dot(p2 - start, (goal - start).normalized) / (goal - start).magnitude;
            }
            t.conflict = (float)(cs / n); t.lateral = (float)(ls / n); t.lateralAcc = (float)(accs / n);
            c.Intent = new OctoIntent { mode = OctoMode.Rest, goal = body.HeadCentre };
            double rs = 0, sp = 0; int rn = 0; Vector3 last = body.HeadCentre;
            for (int f = 0; f < 60 * 12; f++)
            {
                c.Tick(Dt);
                if (f >= 60 * 8)
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

        // RED before the arm roles (baseline 2026-10-18, scale 1, kinds 1-3 x seeds 1-3: conflict 1.1-693 Mg
        // (peaks 8-3400), lateral 0.22-1.65 Mg, lateral acceleration 0.65-5.6 g (peaks 16-84 g); the lattice
        // scene ran pairs of arms at 200-760 Mg against each other). Bounds per run. Holds spread around a
        // hanging body always pull partly against each other (two at 45 deg: 0.41 Mg), so conflict is bounded,
        // not zero; what must not happen is a sustained fight (its peak) or the body yanked sideways.
        [Test] public void TugOfWar_Travel_ArmsPullTogether()
        {
            var log = new StringBuilder(); int bad = 0;
            for (int kind = 1; kind <= 3; kind++)
                for (uint seed = 1; seed <= 3; seed++)
                {
                    Tug t = Measure(kind, 1f, seed);
                    bool ok = t.conflict <= 1.5f && t.conflictMax <= 30f && t.lateral <= 0.25f && t.lateralAcc <= 0.6f && t.lateralAccMax <= 10f && t.progress >= 0.4f;
                    if (!ok) bad++;
                    log.AppendLine($"k{kind} seed {seed}: {t}{(ok ? "" : "  <-- FAIL")}");
                }
            TestContext.Out.WriteLine(log.ToString());
            Assert.AreEqual(0, bad, $"arms pull the body in different directions while travelling ({bad} of 9 runs, see the log)");
        }

        // The same at an individual's size spread (0.9x, 1.1x; the geometry scaled with it): at most one run of
        // nine per size outside the bounds (the branch contact is chaotic).
        [Test] public void TugOfWar_Travel_AcrossSizes()
        {
            var log = new StringBuilder(); int bad = 0;
            foreach (float s in new[] { 0.9f, 1.1f })
            {
                int badHere = 0;
                for (int kind = 1; kind <= 3; kind++)
                    for (uint seed = 1; seed <= 3; seed++)
                    {
                        Tug t = Measure(kind, s, seed);
                        bool ok = t.conflict <= 1.5f && t.conflictMax <= 30f && t.lateral <= 0.25f && t.lateralAcc <= 0.6f && t.lateralAccMax <= 12f && t.progress >= 0.4f;
                        if (!ok) badHere++;
                        log.AppendLine($"scale {s} k{kind} seed {seed}: {t}{(ok ? "" : "  <-- out")}");
                    }
                if (badHere > 1) bad++;
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.AreEqual(0, bad, "arms pull against each other at an individual's size spread (see the log)");
        }

        // Large octopuses (2x, 4x: the lab fits the scale to the branches, up to 4). Looser bounds than at 1x for
        // now (the solver's per-iteration stiffness is not size-free; tightening this is part of the 4x-size task):
        // no sustained fight (peak <= 60 Mg) and no sideways drag on average. Baseline before the arm roles at 4x:
        // conflict mean 388 Mg (peak 5009), lateral acceleration mean 3.4 g (peak 152).
        [Test] public void TugOfWar_Travel_LargeSizes()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float s in new[] { 2f, 4f })
            {
                double c = 0, acc = 0; float peak = 0f;
                for (int kind = 1; kind <= 3; kind++)
                    for (uint seed = 1; seed <= 3; seed++)
                    {
                        Tug t = Measure(kind, s, seed);
                        c += t.conflict; acc += t.lateralAcc; peak = Mathf.Max(peak, t.conflictMax);
                        log.AppendLine($"scale {s} k{kind} seed {seed}: {t}");
                    }
                c /= 9; acc /= 9;
                log.AppendLine($"scale {s}: conflict mean {c:F2}, peak {peak:F0} Mg, lateral acceleration mean {acc:F2} g");
                if (c > 2.5 || peak > 60f || acc > 0.8) bad = true;
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, "a large octopus's arms pull against each other (see the log)");
        }

        // After travelling, resting arms only support the body: the net arm force is the lattice's weight and
        // the head is still.
        [Test] public void TugOfWar_Rest_SupportOnly()
        {
            var log = new StringBuilder(); int bad = 0;
            for (int kind = 1; kind <= 3; kind++)
                for (uint seed = 1; seed <= 3; seed++)
                {
                    Tug t = Measure(kind, 1f, seed);
                    bool ok = t.restResidual <= 0.15f && t.restSpeed <= 0.03f;   // (breathing alone moves the head a few mm/s)
                    if (!ok) bad++;
                    log.AppendLine($"k{kind} seed {seed}: {t}{(ok ? "" : "  <-- FAIL")}");
                }
            TestContext.Out.WriteLine(log.ToString());
            Assert.AreEqual(0, bad, $"resting arms pull against each other ({bad} of 9 runs, see the log)");
        }
    }
}
