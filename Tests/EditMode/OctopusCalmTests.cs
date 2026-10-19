using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Calm arms (user, 2026-10-19: "it looks retarded, just flapping their tentacles ... too much random stuff
    // happening which makes them jitter"; the grip and the swinging are good). Every arm motion has a purpose, and an
    // arm without a job is calm. Measured as the arm tip's motion relative to its root in the BODY's frame (what a
    // viewer reads as the arm moving on the body), in arm lengths per second x TimeScale (Froude: one number for
    // every size):
    //   jobless   no hold, no search, no reach (Hang / Tuck / Rest / Groom, or curled while not told to seek)
    //   search    Feel - looking for a hold
    //   mouth     the swinger's mouth tentacles while they are not feeding
    // and how often a jobless arm or an idle mouth tentacle reverses its direction (flapping).
    public class OctopusCalmTests
    {
        const float Dt = 1f / 60f;

        internal sealed class Meter
        {
            public readonly List<float> jobless = new List<float>(), search = new List<float>(), mouth = new List<float>();
            public double joblessSec, mouthSec, sinusSec, armSec, idleFeelSec;
            public int jobRev, mouthRev;
            OctopusController c; OctopusBody body;
            Vector3[] r, r10, v10, mr, mr10, mv10;
            int frame;

            public void Attach(OctopusController ctl)
            {
                c = ctl; body = c.Body; frame = 0;
                int n = body.ArmCount, m = body.MouthCount;
                r = new Vector3[n]; r10 = new Vector3[n]; v10 = new Vector3[n];
                mr = new Vector3[m]; mr10 = new Vector3[m]; mv10 = new Vector3[m];
                for (int a = 0; a < n; a++) r[a] = r10[a] = Rel(body.Arms[a]);
                for (int k = 0; k < m; k++) mr[k] = mr10[k] = Rel(body.Mouth[k]);
            }

            Vector3 Rel(TentacleChain ch) => Quaternion.Inverse(body.Orientation) * (ch.X[ch.N - 1] - ch.X[0]);

            bool Jobless(int a)
            {
                // a planned body (the swinger, 2026-10-19): its sequencer says which arms are free
                if (c.Sequencer != null) return c.Sequencer.Use[a] == SwingSequencer.ArmUse.Free;
                TentacleAgent.State s = c.Agents[a].Mode;
                if (s != TentacleAgent.State.Curl && s != TentacleAgent.State.Groom) return false;
                ArmRole ro = c.Board.Role[a];
                if (ro == ArmRole.Hang || ro == ArmRole.Tuck || ro == ArmRole.Rest || ro == ArmRole.Groom) return true;
                return !c.Board.Command[a].allowGrip || !(ro == ArmRole.Grip || ro == ArmRole.Anchor || ro == ArmRole.Feel);
            }

            static bool Reversed(ref Vector3 prev, Vector3 w)
            {
                float s0 = prev.magnitude, s1 = w.magnitude;
                bool rev = s0 > 0.15f && s1 > 0.15f && Vector3.Dot(prev, w) < -0.3f * s0 * s1;
                prev = w;
                return rev;
            }

            // after every c.Tick(Dt)
            public void Sample()
            {
                float L = body.Arms[0].Length, ts = body.TimeScale, h = Dt / ts;
                bool ten = frame % 6 == 0, warm = frame > 2;
                for (int a = 0; a < body.ArmCount; a++)
                {
                    Vector3 x = Rel(body.Arms[a]);
                    float sp = (x - r[a]).magnitude / Dt * ts / L;
                    r[a] = x;
                    bool job = Jobless(a);
                    armSec += h;
                    if (warm && job) { joblessSec += h; jobless.Add(sp); if (body.Arms[a].SinusAmount > 0f) sinusSec += h; }
                    if (warm && c.Agents[a].Mode == TentacleAgent.State.Feel) { search.Add(sp); if (body.Arms[a].SinusAmount > 0f) sinusSec += h; }
                    if (c.Board.Role[a] == ArmRole.Feel) idleFeelSec += h;   // an arm sent to feel about without a hold to find
                    if (ten)
                    {
                        if (Reversed(ref v10[a], (x - r10[a]) * (ts / (6f * Dt * L))) && warm && job) jobRev++;
                        r10[a] = x;
                    }
                }
                bool idle = c.Feeder == null || !c.Feeder.Feeding;
                for (int k = 0; k < body.MouthCount; k++)
                {
                    TentacleChain ch = body.Mouth[k];
                    Vector3 x = Rel(ch);
                    float sp = (x - mr[k]).magnitude / Dt * ts / ch.Length;
                    mr[k] = x;
                    if (warm && idle) { mouth.Add(sp); mouthSec += h; }
                    if (ten)
                    {
                        if (Reversed(ref mv10[k], (x - mr10[k]) * (ts / (6f * Dt * ch.Length))) && warm && idle) mouthRev++;
                        mr10[k] = x;
                    }
                }
                frame++;
            }

            public static float Pct(List<float> l, float q)
            {
                if (l.Count == 0) return 0f;
                float[] s = l.ToArray(); Array.Sort(s);
                return s[Mathf.Clamp((int)(q * (s.Length - 1)), 0, s.Length - 1)];
            }

            public float JoblessRevPerMin => joblessSec > 0 ? (float)(jobRev / (joblessSec / 60.0)) : 0f;
            public float MouthRevPerMin => mouthSec > 0 ? (float)(mouthRev / (mouthSec / 60.0)) : 0f;
            public float SinusShare => armSec > 0 ? (float)(sinusSec / armSec) : 0f;
            public float IdleFeelShare => armSec > 0 ? (float)(idleFeelSec / armSec) : 0f;

            public override string ToString() =>
                $"jobless {100 * joblessSec / Math.Max(armSec, 1e-9):F0} % of arm time, tip p50 {Pct(jobless, 0.5f):F2} p95 {Pct(jobless, 0.95f):F2} L/s, reversals {JoblessRevPerMin:F0}/min; "
                + $"search p50 {Pct(search, 0.5f):F2} p95 {Pct(search, 0.95f):F2} L/s; sinus {100 * SinusShare:F1} %, Feel role {100 * IdleFeelShare:F1} % of arm time; "
                + (mouthSec > 0 ? $"mouth p50 {Pct(mouth, 0.5f):F2} p95 {Pct(mouth, 0.95f):F2} L/s, reversals {MouthRevPerMin:F0}/min" : "no mouth");
        }

        // A life in the grove of four (OctopusSwingTests.Live, OctopusSwingerTests.InGrove sizing): brain on, canopy
        // regions, a prey walking on the ground from halfway. Returns branch swings per minute.
        internal static float Life(CephalopodSpec spec, float s, float g, int layout, float minutes, Meter meter)
        {
            var grove = OctopusGroves.Row(4, g, 2.6f, layout);
            var body = new OctopusBody(spec, new Vector3(0.6f, 1.6f, 0.3f) * g, Quaternion.identity, s);
            var c = new OctopusController(body, grove, null, (uint)layout) { Hints = new CapsuleHints(grove, 0.3f * g) };
            var brain = new OctopusBrain((uint)layout); c.Brain = brain;
            for (int t = 0; t < 4; t++) brain.CanopyRegions.Add(new Vector3(t * 2.6f, 2.5f, 0f) * g);
            c.AnchorNearby();
            for (int f = 0; f < 120; f++) c.Tick(Dt);
            meter.Attach(c);
            int T = (int)(3600 * minutes * body.TimeScale);
            OctopusBrainTests.TestPrey prey = null;
            for (int f = 0; f < T; f++)
            {
                if (f == T / 2)
                {
                    prey = new OctopusBrainTests.TestPrey { Pos = new Vector3(1.3f, 0.12f * s, 0.6f) * g, Vel = new Vector3(0.15f, 0f, 0f) * Mathf.Sqrt(s), Radius = 0.05f * s, Half = 0.08f * s };
                    brain.Prey.Add(prey);
                }
                if (prey != null) { prey.Step(Dt); if (prey.Pos.x > 8f * g || prey.Pos.x < -1f * g) prey.Vel = -prey.Vel; }
                c.Tick(Dt);
                meter.Sample();
            }
            return (c.Sequencer != null ? c.Sequencer.Transfers : c.Coordinator.BranchSwings) / minutes;
        }

        // ------------------------------------------------------------------ a new shape is eased into

        // An arm handed a new job (feel about, tuck in, groom) moves into its new shape like a reach - it never whips
        // there. Before (2026-10-19): the positional pull went straight at the new shape's far place and the tip of a
        // feeling arm hit 20 L/s within two frames (31 m/s at 1x). At 1x and 4x, the tip stays under 3 L/s x ts.
        [Test] public void Calm_NewJob_NoWhip()
        {
            var log = new StringBuilder(); bool bad = false;
            foreach (float s in new[] { 1f, 4f })
                foreach (ArmRole job in new[] { ArmRole.Feel, ArmRole.Tuck, ArmRole.Groom })
                {
                    var surf = OctopusSwingTests.Bars(s, 1.6f, 8);
                    var body = new OctopusBody(CephalopodSpec.Default, new Vector3(0.03f, 2.0f, 0.02f) * s, Quaternion.identity, s);
                    var c = new OctopusController(body, surf, null, 1);
                    Assert.GreaterOrEqual(c.AnchorNearby(), 2);
                    for (int f = 0; f < 60 * 6 * body.TimeScale; f++) c.Tick(Dt);
                    int a = -1;
                    for (int k = 0; k < body.ArmCount && a < 0; k++) if (!c.Board.Anchored[k] && c.Agents[k].Mode == TentacleAgent.State.Curl) a = k;
                    Assert.GreaterOrEqual(a, 0, "no free arm");
                    TentacleChain ch = body.Arms[a];
                    float L = ch.Length, ts = body.TimeScale, peak = 0f;
                    Vector3 prev = Quaternion.Inverse(body.Orientation) * (ch.X[ch.N - 1] - ch.X[0]);
                    for (int f = 0; f < 60 * 3 * ts; f++)
                    {
                        c.Coordinator.Forced[a] = (int)job;
                        c.Tick(Dt);
                        Vector3 r = Quaternion.Inverse(body.Orientation) * (ch.X[ch.N - 1] - ch.X[0]);
                        peak = Mathf.Max(peak, (r - prev).magnitude / Dt * ts / L);
                        prev = r;
                    }
                    bool ok = peak < 3f;
                    if (!ok) bad = true;
                    log.AppendLine($"scale {s} arm {a} -> {job}: peak tip speed {peak:F2} L/s x ts{(ok ? "" : "  <-- FAIL")}");
                }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, "an arm whips into its new job (see the log)");
        }

        // ------------------------------------------------------------------ a life in the grove

        // Left to itself in a grove (rest, travel, a hunt from halfway), both species, two sizes each, two layouts: no
        // arm wiggles a sinus, no free arm is sent to feel about without a hold to find, a searching arm moves smoothly
        // (eased, no whip), idle mouth tentacles move with the body. Before (4c5cc89, the same runs): sinus on 13 %
        // (octopus) / 27 % (swinger) of arm time, the Feel role on free arms, search p95 19.2 / 11.6 L/s, mouth p95
        // 17.4 L/s. After (2026-10-19): 0, 0, 13.3 / 11.0, 11.9.
        // Logged, not bounded: jobless arms (hanging / curled from the swinging body: a passive pendulum, p95 ~11-12 L/s
        // before and after - damping them toward the body fed the swing, see TentacleChain.CalmDamping) and the
        // reversals of the mouth tentacles (they follow the swinger's spinning sac).
        // The planned swinger (2026-10-19: body moved by PlannedSwing, free arms passive and damped toward the body) IS
        // bounded: free arm tip p95 <= 4 L/s (measured 3.1; 11-12 on the emergent body), mouth p95 <= 4 L/s (2.7; 11.9).
        internal const float SearchP95 = 15f, MouthP95 = 13.5f, PlannedFreeP95 = 4f, PlannedMouthP95 = 4f;
        [Test] public void Calm_Life_BothSpecies()
        {
            var log = new StringBuilder(); bool bad = false;
            CephalopodSpec swinger = OctopusSwingerTests.Spec();
            foreach (bool sw in new[] { true, false })
            {
                var meter = new Meter(); float swings = 0f; int runs = 0;
                foreach (float s in sw ? new[] { 1.2f * 0.9f, 1.2f * 1.1f } : new[] { 1f, 3.1f })
                    for (int layout = 1; layout <= 2; layout++)
                    {
                        swings += Life(sw ? swinger : CephalopodSpec.Default, s, sw ? OctopusSwingerTests.GroveFit * s : s, layout, 1.5f, meter);
                        runs++;
                    }
                string name = sw ? "Swinger" : "TreeOctopus";
                bool ok = meter.SinusShare == 0f && meter.IdleFeelShare == 0f && Meter.Pct(meter.search, 0.95f) <= SearchP95
                          && (meter.mouthSec == 0 || Meter.Pct(meter.mouth, 0.95f) <= MouthP95)
                          && (!sw || Meter.Pct(meter.jobless, 0.95f) <= PlannedFreeP95 && Meter.Pct(meter.mouth, 0.95f) <= PlannedMouthP95);
                if (!ok) bad = true;
                log.AppendLine($"{name}: {meter}; branch swings {swings / runs:F1}/min{(ok ? "" : "  <-- FAIL")}");
            }
            TestContext.Out.WriteLine(log.ToString());
            Assert.IsFalse(bad, "the arms flap about (see the log)");
        }
    }
}
