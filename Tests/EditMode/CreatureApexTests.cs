using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The Apex (Apex redesign, task 10; Config/Creatures/Apex.asset from Tools~/SpeciesGen/Apex.py): the species asset
    // built from the redesign's own kit parts and tuned to the reference (docs/superpowers/specs/assets/
    // 2026-10-20-apex-reference-hires.jpg). Its layout: a 26-joint FlowBody (head joint 0, the shoulder - wing arms - at
    // joint 7, the hips - hind legs - at 11, the tail from 12), CrestBeakHead on joint 0, Dewlap, PitRow, mirrored
    // FleshWing blades on Leg2Bone wing arms, DrakeLeg hind legs, Eyes, TailFlourish last. Invariants: it composes from
    // its own parts only (no pterosaur part); its walking stance is the picture's (the head highest, the neck raised
    // steeply, the folded blades' tips high above the hump and behind the shoulder, steep, side by side and broad, the
    // clubs ahead of the hind feet, no stance segment steeper than 65 deg); in steady flight the girdle bobs >= 6 % of the
    // body length with each beat, the bob travels down the tail and the head rides steadier (<= 1/3); under its own brain
    // in the aviary it walks, takes off, beats, glides, flares, lands and walks again; the gates: no body skin seen inside
    // the open mouth in a walking pose, the standing tail loop turns >= 120 deg; the arm buried in the folded blade's
    // flesh (task 10c). The REDs: a beating flyer whose girdle barely heaves; a tail wave that leads or jumps half a beat.
    public class CreatureApexTests
    {
        const float kDt = 1f / 60f;
        internal const int kShoulder = 7, kHips = 11, kTailRoot = 12;

        // the asset (its runtime view)
        internal static CreatureSpecies Apex() => CreatureKitAssets.Load("Apex");

        // A copy of the asset's data, edited, compiled afresh (the asset itself is untouched).
        internal static CreatureSpecies Apex(System.Action<List<CreaturePart>> parts, System.Action<FlightSpec> fly = null)
        {
            CreatureSpecies a = CreatureKitAssets.LoadAsset("Apex");
            var ps = new List<CreaturePart>();
            foreach (CreaturePart p in a.parts) ps.Add(p.Clone());
            var ms = new List<CreatureModule>();
            foreach (CreatureModule m in a.modules)
            {
                if (m is Fly f)
                {
                    var c = new Fly { spec = CopyOf(f.spec), twist = f.twist };
                    fly?.Invoke(c.spec);
                    ms.Add(c);
                }
                else ms.Add(m.Clone());
            }
            parts?.Invoke(ps);
            return FlowKit.Build("ApexVariant", a.scale, ps, ms);
        }

        static FlightSpec CopyOf(FlightSpec s)
        {
            var c = new FlightSpec();
            foreach (FieldInfo f in typeof(FlightSpec).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) f.SetValue(c, f.GetValue(s));
            return c;
        }

        internal static T Find<T>(CreatureSpecies sp) where T : CreaturePart
        {
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is T t) return t;
            return null;
        }

        static int Count<T>(CreatureSpecies sp)
        {
            int n = 0;
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is T) n++;
            return n;
        }

        // ------------------------------------------------------------ composition

        [Test] public void Apex_ComposesFromItsOwnParts()
        {
            CreatureSpecies sp = Apex();
            Assert.IsNotNull(sp, "Config/Creatures/Apex.asset");
            Assert.IsNull(sp.Validate());
            Assert.AreEqual(1, Count<FlowBody>(sp)); Assert.AreEqual(1, Count<CrestBeakHead>(sp));
            Assert.AreEqual(1, Count<Dewlap>(sp)); Assert.AreEqual(1, Count<TailFlourish>(sp));
            Assert.AreEqual(2, Count<FleshWing>(sp)); Assert.AreEqual(2, Count<DrakeLeg>(sp)); Assert.AreEqual(2, Count<PitRow>(sp));
            Assert.AreEqual(0, Count<PteroWing>(sp), "no pterosaur wing"); Assert.AreEqual(0, Count<LongNeck>(sp), "the neck is the body's chain");
            Assert.AreEqual(0, Count<BeakHead>(sp)); Assert.AreEqual(0, Count<SpineBody>(sp)); Assert.AreEqual(0, Count<Wing>(sp));
            Assert.IsTrue(KitView.Parts(sp)[KitView.Parts(sp).Count - 1] is TailFlourish, "the tail overlay is the last part");
            Assert.IsTrue(sp.CanFly);
            Assert.IsNull(MaterialSlots.For(sp).Problem, "its kinds fit the material table");
        }

        // ------------------------------------------------------------ the stance (the picture)

        internal const float kNeckRiseDeg = 35f, kMaxStanceSegmentDeg = 65f;

        // Walking a weave for `seconds` (every 0.5 s after the first second): the head joint is the highest body joint, the
        // chord from the chest (the shoulder joint) to the head joint rises >= kNeckRiseDeg, and each wing arm's foot (the
        // club's contact) is ahead of the hind foot on its side along the girdle's levelled forward while both are planted.
        internal static string StanceProblem(CreatureSpecies sp, float seconds, out float minRise, out float minAhead) => StanceProblem(sp, seconds, out minRise, out minAhead, out _);

        // aheadSamples: the planted club / hind-foot pairs measured (0: the clause never ran - a problem)
        internal static string StanceProblem(CreatureSpecies sp, float seconds, out float minRise, out float minAhead, out int aheadSamples)
        {
            minRise = 90f; minAhead = float.MaxValue; aheadSamples = 0;
            float s = sp.scale;
            var sim = FlowKit.Sim(sp, s);
            int n = Mathf.RoundToInt(seconds / kDt), every = Mathf.RoundToInt(0.5f / kDt);
            var arms = new List<int>(); var hinds = new List<int>();
            for (int l = 0; l < sp.legs.Length; l++) (sp.legs[l].attachJoint == kShoulder ? arms : hinds).Add(l);
            for (int t = 0; t < n; t++)
            {
                Vector3 fwd = sim.Rotations[0] * Vector3.forward;
                sim.Tick(kDt, sim.Joints[0] + Quaternion.AngleAxis(40f * Mathf.Sin(t / 70f), Vector3.up) * fwd * (2f * s));
                if (t < 60 || t % every != 0) continue;
                Vector3[] J = sim.Joints;
                for (int j = 1; j < J.Length; j++)
                    if (J[j].y >= J[0].y) return $"t={sim.Time:F1} s: body joint {j} ({J[j].y:F3} m) is not below the head joint ({J[0].y:F3} m)";
                Vector3 chord = J[0] - J[kShoulder];
                float rise = Mathf.Atan2(chord.y, new Vector2(chord.x, chord.z).magnitude) * Mathf.Rad2Deg;
                minRise = Mathf.Min(minRise, rise);
                if (rise < kNeckRiseDeg) return $"t={sim.Time:F1} s: the neck rises only {rise:F1} deg from the chest to the head (< {kNeckRiseDeg})";
                Vector3 gf = Vector3.ProjectOnPlane(sim.Rotations[kShoulder] * Vector3.forward, Vector3.up).normalized;
                for (int k = 0; k < arms.Count && k < hinds.Count; k++)   // each side's club and hind foot (mirror: .L, .R)
                {
                    int a = arms[k], h = hinds[k];
                    if (Stepping(sim.Legs[a]) || Stepping(sim.Legs[h])) continue;   // planted (a swinging foot passes the other)
                    float ahead = Vector3.Dot(sim.Legs[a].foot - sim.Legs[h].foot, gf);
                    aheadSamples++;
                    minAhead = Mathf.Min(minAhead, ahead);
                    if (ahead <= 0f) return $"t={sim.Time:F1} s: wing arm {a}'s club is {-ahead:F3} m behind its side's hind foot {h}";
                }
            }
            if (aheadSamples == 0) return "no sample with a club and its hind foot both planted: the clubs-ahead clause never ran";
            return null;
        }

        static bool Stepping(in LegState l) => l.stepping && l.stepT >= 0f;

        // The steepest segment of the bound stance (the rest the mesh is drawn on: restAtStance)
        internal static float SteepestStanceSegment(CreatureSpecies sp, out int at)
        {
            CreatureSim rest = CreatureRestPose.BindSim(sp, sp.wings);
            float worst = 0f; at = -1;
            for (int i = 1; i < rest.Joints.Length; i++)
            {
                Vector3 d = rest.Joints[i - 1] - rest.Joints[i];
                float p = Mathf.Abs(Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg);
                if (p > worst) { worst = p; at = i - 1; }
            }
            return worst;
        }

        [Test] public void Apex_Stance_MatchesTheReference()
        {
            CreatureSpecies sp = Apex();
            string e = StanceProblem(sp, 20f, out float rise, out float ahead, out int samples);
            TestContext.Out.WriteLine($"walking 20 s: the head highest; neck rise >= {rise:F1} deg; clubs ahead of the hind feet by >= {ahead:F3} m ({samples} planted samples)");
            Assert.IsNull(e, e);
            Assert.GreaterOrEqual(samples, 8, "the clubs-ahead clause measured (planted pairs every 0.5 s)");
            // the folded blades: high above the hump and behind the shoulder, steep, side by side (no crossing over the
            // back), broad leaves (CreatureFleshWingTests' blade checks on the Apex itself)
            string b = CreatureFleshWingTests.BladeProblem(sp, 20f, out float margin, out float behind);
            Assert.IsNull(b, b);
            string st = CreatureFleshWingTests.SteepProblem(sp, out float elev, out _, out float own, out float gap, out _);
            Assert.IsNull(st, st);
            string br = CreatureFleshWingTests.BroadProblem(sp, out float broad);
            TestContext.Out.WriteLine($"blades: least steep {elev:F0} deg, tips out on their own side >= {own / sp.scale:F3} x s, apart >= {gap / sp.scale:F3} x s; broad {broad:F3}");
            Assert.IsNull(br, br);
            float steep = SteepestStanceSegment(sp, out int seg);
            TestContext.Out.WriteLine($"steepest stance segment {steep:F1} deg ({seg}-{seg + 1})");
            Assert.LessOrEqual(steep, kMaxStanceSegmentDeg, "a stance segment near vertical tips its walking frame over (task 8b)");
            float walk = CreatureFleshWingTests.MaxWalkPitch(sp, out string where);
            TestContext.Out.WriteLine($"steepest walking segment {walk:F1} deg ({where})");
            Assert.Less(walk, CreatureFleshWingTests.kMaxWalkPitch, where);
        }

        // The arm buried in the blade (task 10c, CreatureFleshWingTests' checks on the Apex itself): folded, the arm's own
        // flesh along the forearm from the club up reaches >= kLeafWidth x its radius across the blade (a leaf lying in
        // it, not a tube), its flesh is one closed surface through the elbow (no ball joint), and its shoulder end stays put
        // through a touchdown.
        [Test] public void Apex_ArmBuriedInTheBlade()
        {
            CreatureSpecies sp = Apex();
            string e = CreatureFleshWingTests.ArmLeafProblem(sp, out _, out _);
            Assert.IsNull(e, e);
            string f = CreatureFleshWingTests.ArmSurfaceProblem(sp, out _, out _, out _);
            Assert.IsNull(f, f);
            string g = CreatureFleshWingTests.FleshShoulderProblem(sp, out _, out _);
            Assert.IsNull(g, g);
        }

        // ------------------------------------------------------------ the bob

        internal const float kBob = 0.06f, kTailLag = 0.15f, kHeadShare = 1f / 3f, kMaxStepLag = 0.45f;
        // the wave's stations: the girdle (the flight's), then down the tail - each one's lag behind the one before
        internal static readonly int[] kWave = { 15, 19, 23 };
        internal const int kMidTail = 19;

        internal struct Bob { public float girdle, head, tail, lagBeats, period, beats, climbSeconds; public float[] steps; }

        // Steady cruise (a launch from a short walk, the goals held - altitude 6 x s, the cruise speed, straight - and the
        // climb-out until the flight has held its altitude goal within 0.75 x s for 2 s x sqrt s; at most 60 s x sqrt s):
        // 20 s x sqrt s of the girdle's, the head's and the tail stations' (kWave) heights, each less its running mean over
        // +-1 s x sqrt s (the path's drift); peak to peak; the beats (the upstrokes begun) and their period; the wave's
        // steps (WaveSteps) and the mid tail's lag behind the girdle (their sum up to kMidTail). climbSeconds < 0: it never
        // reached its cruise.
        internal static Bob MeasureBob(CreatureSpecies sp)
        {
            float s = sp.scale, T = Mathf.Sqrt(s);
            var sim = FlowKit.Sim(sp, s);
            for (int t = 0; t < 30; t++) sim.Tick(kDt, sim.Joints[0] + sim.Rotations[0] * Vector3.forward * (2f * s));
            sim.Flight.Launch(6f * s);
            int N = Mathf.RoundToInt(20f * T / kDt), W = Mathf.RoundToInt(1f * T / kDt), hold = Mathf.RoundToInt(2f * T / kDt), maxClimb = Mathf.RoundToInt(60f * T / kDt);
            int held = 0, climbed = 0;
            for (; climbed < maxClimb && held < hold; climbed++)
            {
                sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]);
                held = sim.Mode == FlightMode.Air && Mathf.Abs(sim.Flight.Model.Altitude - 6f * s) < 0.75f * s ? held + 1 : 0;
            }
            float climbSeconds = held >= hold ? climbed * kDt : -1f;
            int g = sim.Flight.Girdle, starts = 0;
            var y = new float[2 + kWave.Length][];   // 0 girdle, 1 head, 2.. the wave's stations
            for (int k = 0; k < y.Length; k++) y[k] = new float[N];
            bool up = sim.Flight.Wings.State[0].upstroke;
            for (int t = 0; t < N; t++)
            {
                sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]);
                y[0][t] = sim.Joints[g].y; y[1][t] = sim.Joints[0].y;
                for (int k = 0; k < kWave.Length; k++) y[2 + k][t] = sim.Joints[kWave[k]].y;
                bool u = sim.Flight.Wings.State[0].upstroke;
                if (u && !up) starts++;
                up = u;
            }
            var d = new float[y.Length][];
            for (int k = 0; k < y.Length; k++) d[k] = Detrend(y[k], W);
            var b = new Bob { girdle = PP(d[0]), head = PP(d[1]), tail = PP(d[2 + System.Array.IndexOf(kWave, kMidTail)]), climbSeconds = climbSeconds, beats = starts };
            b.period = starts > 0 ? N * kDt / starts : 0f;
            int per = Mathf.Max(2, Mathf.RoundToInt(b.period / kDt));
            var wave = new float[kWave.Length + 1][];
            wave[0] = d[0];
            for (int k = 0; k < kWave.Length; k++) wave[k + 1] = d[2 + k];
            b.steps = WaveSteps(wave, per);
            b.lagBeats = 0f;
            for (int k = 0; k < kWave.Length && kWave[k] <= kMidTail; k++) b.lagBeats += b.steps[k];
            return b;
        }

        // The wave down the body, signal by signal (girdle, then the stations toward the tail): each station's lag behind
        // the one before it, in beats, the cross-correlation's best lag in the signed window (-1/2, 1/2] beat (per =
        // samples per beat) - a station that leads its predecessor reads negative, not as most of a beat late.
        internal static float[] WaveSteps(float[][] wave, int per)
        {
            var steps = new float[wave.Length - 1];
            for (int k = 1; k < wave.Length; k++)
            {
                int best = 0; float bc = float.MinValue;
                for (int lag = -(per - 1) / 2; lag <= per / 2; lag++)
                {
                    float c = lag >= 0 ? Corr(wave[k - 1], wave[k], lag) : Corr(wave[k], wave[k - 1], -lag);
                    if (c > bc) { bc = c; best = lag; }
                }
                steps[k - 1] = (float)best / per;
            }
            return steps;
        }

        // The bob travels down the body: every step of the wave lags (> 0) and stays clear of the half-beat ambiguity
        // (< kMaxStepLag: a step near 1/2 beat could be anti-phase either way), and the mid tail lags the girdle by
        // >= kTailLag beat in all.
        internal static string WaveProblem(float[] steps, float lagToMid)
        {
            for (int k = 0; k < steps.Length; k++)
            {
                string from = k == 0 ? "the girdle" : "joint " + kWave[k - 1];
                if (!(steps[k] > 0f)) return $"joint {kWave[k]} leads {from} by {-steps[k]:F2} beat (the bob does not travel down the tail)";
                if (steps[k] >= kMaxStepLag) return $"joint {kWave[k]} lags {from} by {steps[k]:F2} beat (>= {kMaxStepLag}: anti-phase, ambiguous)";
            }
            if (lagToMid < kTailLag) return $"the mid tail lags the girdle by {lagToMid:F2} beat (< {kTailLag})";
            return null;
        }

        static float[] Detrend(float[] y, int W)
        {
            var o = new float[y.Length - 2 * W];
            for (int i = W; i < y.Length - W; i++)
            {
                float m = 0f;
                for (int j = -W; j <= W; j++) m += y[i + j];
                o[i - W] = y[i] - m / (2 * W + 1);
            }
            return o;
        }

        static float PP(float[] y)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (float v in y) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
            return hi - lo;
        }

        // correlation of a[i] with b[i + lag] (b later by lag), normalised
        static float Corr(float[] a, float[] b, int lag)
        {
            double ab = 0, aa = 0, bb = 0;
            for (int i = 0; i + lag < b.Length; i++) { ab += a[i] * b[i + lag]; aa += a[i] * a[i]; bb += b[i + lag] * b[i + lag]; }
            return (float)(ab / System.Math.Sqrt(System.Math.Max(1e-12, aa * bb)));
        }

        internal static string BobProblem(CreatureSpecies sp, out Bob b)
        {
            b = MeasureBob(sp);
            float L = Find<FlowBody>(sp).BodyLength * sp.scale;
            if (b.climbSeconds < 0f) return "it never held its cruise altitude (60 s x sqrt s)";
            TestContext.Out.WriteLine($"cruise after {b.climbSeconds:F1} s of climb: girdle bob {b.girdle:F3} m p-p = {b.girdle / L * 100f:F1} % of the body ({L:F2} m); head {b.head:F3} m ({b.head / Mathf.Max(1e-6f, b.girdle):F2} x girdle); mid tail {b.tail:F3} m lagging {b.lagBeats:F2} beat (steps girdle -> {string.Join(" -> ", kWave)}: {string.Join(", ", System.Array.ConvertAll(b.steps, x => x.ToString("F2")))}); period {b.period:F2} s, {b.beats:F1} beats");
            if (b.beats < 5f) return $"only {b.beats:F1} beats measured";
            if (b.girdle < kBob * L) return $"the girdle bobs {b.girdle:F3} m p-p, < {kBob} x the body length ({kBob * L:F3} m)";
            string w = WaveProblem(b.steps, b.lagBeats);
            if (w != null) return w;
            if (b.head > kHeadShare * b.girdle) return $"the head bobs {b.head:F3} m p-p, > 1/3 of the girdle's {b.girdle:F3} m";
            return null;
        }

        [Test] public void Apex_Bob_TravelsDownTheBody()
        {
            string e = BobProblem(Apex(), out _);
            Assert.IsNull(e, e);
        }

        // RED: a flyer that beats all the time but on a strong glide lift with a weak, shallow stroke (kLift 0.75 - the
        // old Apex's -, kGrip 0.25, amplitude from 0.2, never gliding) - plenty of beats, the girdle barely heaves.
        [Test] public void Red_Apex_FlatBob()
        {
            string e = BobProblem(Apex(null, f => { f.kLift = 0.75f; f.airLevel = 0.8f; f.kGrip = 0.25f; f.amplitudeMin = 0.2f; f.glideDemand = 0f; f.descendCutoff = -1f; }), out Bob b);
            TestContext.Out.WriteLine("RED: " + e);
            Assert.GreaterOrEqual(b.beats, 5f, "beats enough: the amplitude clause is what bites");
            StringAssert.Contains("the girdle bobs", e);
        }

        // The wave clause on a made-up travelling wave (a sine per station, 60 samples a beat): a tail lagging 0.1 beat a
        // station passes; REDs - a tail that leads the girdle, and a step at more than the half beat (anti-phase), fail.
        static float[][] Wave(params float[] lagsInBeats)
        {
            var w = new float[lagsInBeats.Length][];
            for (int k = 0; k < w.Length; k++)
            {
                w[k] = new float[1200];
                for (int i = 0; i < 1200; i++) w[k][i] = Mathf.Sin(2f * Mathf.PI * (i / 60f - lagsInBeats[k]));
            }
            return w;
        }

        static string WaveOf(params float[] lags)
        {
            float[] st = WaveSteps(Wave(lags), 60);
            return WaveProblem(st, st[0] + st[1]);
        }

        [Test] public void Wave_Lagging_Passes() => Assert.IsNull(WaveOf(0f, 0.1f, 0.2f, 0.3f));

        [Test] public void Red_Wave_TailLeads() => StringAssert.Contains("leads", WaveOf(0f, -0.1f, -0.2f, -0.3f));

        [Test] public void Red_Wave_AntiPhase() => StringAssert.Contains("anti-phase", WaveOf(0f, 0.1f, 0.6f, 0.7f));

        // ------------------------------------------------------------ flies and lands under its own brain

        internal const float kFlySeconds = 120f;

        // From the ground in its aviary (the lab's area, its own brain), within kFlySeconds x sqrt s: the flight channel's
        // phases in order of need - wing-walk, launch, beat, glide, flare, touchdown, and wing-walk again after the touchdown.
        internal static string FlyAndLandProblem(CreatureSpecies sp, int seed, out string seen, out float landedAt)
        {
            landedAt = -1f;
            EnclosureArea area = KitChecks.LabArea(sp);
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, area.center, Vector3.forward, sp.scale, seed) { Bounds = area };
            ICreatureBrain brain = KitPoseSweep.BrainOf(sp, area, seed);
            IPosePhases flight = null;
            foreach (IPosePhases c in PosePhases.Collect(sim, brain)) if (c.PhaseChannel == "flight") flight = c;
            var got = new HashSet<string>();
            bool touched = false, again = false;
            int ticks = Mathf.CeilToInt(kFlySeconds * sim.TimeScale / kDt);
            for (int t = 0; t < ticks && !again; t++)
            {
                sim.Tick(kDt, brain.Target(sim, kDt));
                int p = flight.CurrentPhase;
                if (p < 0) continue;
                string name = flight.PhaseName(p);
                got.Add(name);
                if (name == "touchdown") touched = true;
                if (touched && name == "wing-walk" && got.Count == 6) { again = true; landedAt = sim.Time; }
            }
            seen = string.Join(", ", got);
            if (!again)
            {
                var missing = new List<string>();
                foreach (string ph in PosePhases.FlightNames) if (!got.Contains(ph)) missing.Add(ph);
                return $"seed {seed}: in {kFlySeconds} s x sqrt s it reached {seen}; never {(missing.Count > 0 ? string.Join(", ", missing) : "wing-walk after a touchdown")}";
            }
            return null;
        }

        [Test] public void Apex_FlyAndLand()
        {
            CreatureSpecies sp = Apex();
            string e = FlyAndLandProblem(sp, 1, out string seen, out float at);
            TestContext.Out.WriteLine($"seed 1: {seen}; walking again at t = {at:F1} s");
            Assert.IsNull(e, e);
        }

        // ------------------------------------------------------------ the gates

        // Gate 1: in a walking pose (1 s of walk), the mouth at its full gape shows only the mouth and the teeth inside -
        // never the neck's front end (the FlowBody's head cap) or the dewlap (CrestBeakHeadTests.InsideMouthProblem).
        [Test] public void Apex_OpenMouth_Walking_OnlyMouthInside()
        {
            string e = CreatureCrestBeakHeadTests.InsideMouthProblem(Apex(), 1f, out int wrong, out int rays);
            Assert.IsNull(e, e);
        }

        // Gate 2: standing, the tail leaves the hips low and rises in a loop - its posed segments turn >= kLoopTurnDeg in the
        // body's side plane from the root to the tip (accumulated, signed: up and over), and the loop's top stands above the
        // hips (CreatureTailFlourishTests.LoopProblem: >= 0.3 tail lengths; nothing beyond the base near the ground).
        internal const float kLoopTurnDeg = 120f;

        internal static float LoopTurning(CreatureSpecies sp, out float least)
        {
            float s = sp.scale, T = Mathf.Sqrt(s);
            var sim = FlowKit.Sim(sp, s);
            TailFlourishRuntime rt = CreatureTailFlourishTests.Rt(sim);
            int n = sim.Joints.Length;
            Vector3 stand = sim.Joints[0];
            least = float.MaxValue; float most = 0f;
            for (int k = 0; k < 600; k++)
            {
                sim.Tick(kDt * T, stand);
                if (k < 120 || k % 10 != 0) continue;
                // the side plane: the hips' levelled forward and up
                Vector3 fw = Vector3.ProjectOnPlane(sim.Rotations[kHips] * Vector3.forward, Vector3.up).normalized;
                float total = 0f, prevA = 0f;
                for (int j = kTailRoot; j < n; j++)
                {
                    Vector3 a = rt.PosedJoint(j), b = j + 1 < n ? rt.PosedJoint(j + 1) : rt.PosedTip;
                    Vector3 d = b - a;
                    float ang = Mathf.Atan2(d.y, -Vector3.Dot(d, fw)) * Mathf.Rad2Deg;   // 0 = straight back, +90 = up
                    if (j > kTailRoot) total += Mathf.DeltaAngle(prevA, ang);
                    prevA = ang;
                }
                least = Mathf.Min(least, total); most = Mathf.Max(most, total);
            }
            return most;
        }

        [Test] public void Apex_TailLoop_TurnsAndRises()
        {
            CreatureSpecies sp = Apex();
            LoopTurning(sp, out float least);
            TestContext.Out.WriteLine($"standing 10 s: the tail turns at least {least:F0} deg from its root to its tip");
            Assert.GreaterOrEqual(least, kLoopTurnDeg);
            string e = CreatureTailFlourishTests.LoopProblem(sp, sp.scale, out float rise, out float lowest);
            TestContext.Out.WriteLine($"the loop's top >= {rise:F2} tail lengths above the hips; lowest posed point beyond the base {lowest * 100f:F1} cm x s");
            Assert.IsNull(e, e);
        }
    }
}
