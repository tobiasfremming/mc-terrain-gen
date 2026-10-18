using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Phase A of the winged serpent: FlightModel + CreatureWings, against the designer's prototype
    // (wyvern_proto/proto.py, measure.py; dt = 1/120, level flight at 5 m/s, 5 m):
    //   level: alt 4.99 +- 0.07, vx 4.89, 2.57-2.67 beats/s; climb 5 -> 8 m: 4.93 -> 6.92 m in 3 s,
    //   (the integral term, which the prototype lacks, settles the level altitude on the goal)
    //   3.0 beats/s; grip x2: no beats (held wings), alt ~6.08.
    public class WingedSerpentFlightTests
    {
        const float H = 1f / 120f;
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        // A flyer: the girdle point of a FlightModel drives a straight spine (girdle joint at the model's
        // position, frames = heading + banked up), CreatureWings on top.
        internal sealed class Rig
        {
            public WingedSerpentTestSpecies.Serpent s;
            public FlightModel model;
            public CreatureWings wings;
            public Vector3[] joints;
            public Quaternion[] rots;
            public float scale;

            public void Step(float h, bool pose = false)
            {
                model.Step(h, wings);
                if (pose) Pose();
            }

            public void Pose()
            {
                Quaternion r = Quaternion.LookRotation(model.Forward, model.BankedUp);
                Vector3 back = -model.Forward * (s.species.spacing * scale);
                for (int i = 0; i < joints.Length; i++)
                {
                    joints[i] = model.Position + back * (i - WingedSerpentTestSpecies.Girdle);
                    rots[i] = r;
                }
                wings.Pose();
            }
        }

        internal static Rig MakeRig(WingedSerpentTestSpecies.Serpent s, float scale = 1f, float alt = 5f, float speed = 5f)
        {
            float T = Mathf.Sqrt(scale);
            var r = new Rig { s = s, scale = scale };
            r.joints = new Vector3[s.species.JointCount];
            r.rots = new Quaternion[s.species.JointCount];
            for (int i = 0; i < r.rots.Length; i++) r.rots[i] = Quaternion.identity;
            r.model = new FlightModel(s.flight, scale, new Vector3(0f, alt * scale, 0f), Vector3.forward * (speed * T), Vector3.forward);
            r.model.AltitudeGoal = alt * scale;
            r.model.SpeedGoal = speed * T;
            r.wings = new CreatureWings(s.species, s.wings, r.joints, r.rots, scale);
            r.Pose();
            return r;
        }

        WingedSerpentTestSpecies.Serpent Serpent()
        {
            var s = WingedSerpentTestSpecies.Create();
            trash.Add(s.species);
            return s;
        }

        struct Sample { public float t, alt, vf, vy, amp, rate; public int beats; public bool up; }

        // mean downstroke angular rate (FlightModel.StrokeRate) over [a, b)
        static float MeanRate(List<Sample> log, float a, float b)
        {
            float sum = 0f; int n = 0;
            foreach (var s in log) if (s.t >= a && s.t < b) { sum += s.rate; n++; }
            return sum / Mathf.Max(1, n);
        }

        static List<Sample> Run(Rig r, float seconds, Func<float, float> altGoal = null, Action<float> each = null)
        {
            var log = new List<Sample>();
            int steps = Mathf.RoundToInt(seconds / H);
            for (int k = 0; k < steps; k++)
            {
                float t = k * H;
                if (altGoal != null) r.model.AltitudeGoal = altGoal(t) * r.scale;
                r.Step(H);
                each?.Invoke(t);
                log.Add(new Sample
                {
                    t = t, alt = r.model.Altitude, vf = r.model.ForwardSpeed, vy = r.model.VerticalSpeed,
                    amp = r.model.Amplitude, rate = r.model.StrokeRate, beats = r.wings.State[0].beats, up = r.wings.State[0].upstroke,
                });
            }
            return log;
        }

        static void Window(List<Sample> log, float a, float b, out float mean, out float half, out float vf, out float hz, out float amp)
        {
            float sum = 0f, lo = float.MaxValue, hi = float.MinValue, sv = 0f, sa = 0f; int n = 0, b0 = -1, b1 = 0;
            foreach (var s in log)
            {
                if (s.t < a || s.t >= b) continue;
                if (b0 < 0) b0 = s.beats;
                b1 = s.beats;
                sum += s.alt; sv += s.vf; sa += s.amp; n++;
                lo = Mathf.Min(lo, s.alt); hi = Mathf.Max(hi, s.alt);
            }
            mean = sum / n; half = 0.5f * (hi - lo); vf = sv / n; amp = sa / n;
            hz = (b1 - b0) / (b - a);
        }

        // ------------------------------------------------------------------ air plant

        [Test] public void Flight_GripFraction()
        {
            Assert.AreEqual(0f, FlightModel.GripFraction(0f, H), 1e-7f);
            Assert.AreEqual(0.5f, FlightModel.GripFraction(120f, H), 1e-6f);
            Assert.AreEqual(1f, FlightModel.GripFraction(1e9f, H), 1e-5f);

            // one integration: a wrist driven down at 1 m/s through still air, no gravity: the girdle gains
            // exactly g x 1 m/s upward along the stroke normal
            var s = Serpent();
            s.flight.kDrag = 0f; s.flight.kLift = 0f;
            var r = MakeRig(s, 1f, 5f, 0f);
            r.model.GravityScale = 0f;
            for (int w = 0; w < 2; w++) { r.wings.State[w].rise = 0f; r.wings.State[w].tilt = 0f; r.wings.State[w].area = 0f; }
            r.wings.State[0].rise = -1f; r.wings.State[0].area = 1f;
            float kappa = r.model.GripRate(1f, 0f);
            float g = FlightModel.GripFraction(kappa, H);
            r.model.Integrate(H, r.wings);
            Assert.AreEqual(g, r.model.VerticalSpeed, 1e-5f, "girdle speed = grip fraction x wrist speed");
            Assert.Greater(g, 0.01f); Assert.Less(g, 0.1f);
            // prototype: cw = kW A v0 at m = 6 (kW = kGrip m / 2, v0 = vGripMin): g = cw h / (m/2 + cw h)
            float cw = s.flight.kGrip * 3f * s.flight.vGripMin;
            float proto = cw * H / (3f + cw * H);
            Assert.AreEqual(proto, g, 1e-4f, "matches the prototype's per-wing grip");
        }

        // g = 1 (a planted foot): during the downstroke the wrist stays fixed in world space and the
        // shoulder rises by exactly the stroke.
        [Test] public void Flight_PlantedTip_LiftsShoulder()
        {
            var s = Serpent();
            // the idealised foot: no drag, lift or gravity, and the stroke normal = up (no pronation, no feather);
            // no glide either: in this gravity-free rig the planted stroke's rise trips the controller into a glide
            // on the next step (it did once the band deepened, comfortDown 1.03), which is not what this measures
            s.flight.kDrag = 0f; s.flight.kLift = 0f; s.flight.featherDeg = 0f; s.flight.glideDemand = -1f;
            foreach (var ws in s.wings) ws.pronationDeg = 0f;
            var r = MakeRig(s, 1f, 5f, 0f);
            r.model.GravityScale = 0f; r.model.GripScale = 1e7f;
            r.model.SpeedGoal = 0f;
            int downSteps = 0;
            float wrist0 = float.NaN, girdle0 = 0f, angle0 = 0f;
            for (int k = 0; k < 240; k++)
            {
                bool wasUp = r.wings.State[0].upstroke;
                r.Step(H, true);
                ref WingState st = ref r.wings.State[0];
                if (st.upstroke || wasUp || st.angleRate >= 0f) { if (downSteps > 10) break; wrist0 = float.NaN; continue; }
                if (float.IsNaN(wrist0)) { wrist0 = st.wrist.y; girdle0 = r.model.Position.y; angle0 = st.angle; downSteps = 0; continue; }
                downSteps++;
                Assert.AreEqual(wrist0, st.wrist.y, 2e-4f, $"planted wrist moved at step {k}");
                float stroke = r.wings.WristHeight(0, angle0) - r.wings.WristHeight(0, st.angle);
                Assert.AreEqual(stroke, r.model.Position.y - girdle0, 2e-4f, "shoulder rise = stroke");
            }
            Assert.Greater(downSteps, 10, "never saw a downstroke");
        }

        [Test] public void Flight_HoldsAltitude()
        {
            // the deep downstroke (comfortDown 0.95, user feedback 2026-10-18) beats slower than the prototype
            // (~2.2 Hz, was 2.67) with a bigger bob, so the window is longer (5-11 s, was 3-6 s); deeper again
            // (comfortDown 1.03, amplitudeMin 0.75, kGrip 0.5, same day): ~1.3-1.5 Hz, a ~0.44 m p-p bob
            var r = MakeRig(Serpent());
            var log = Run(r, 12f);
            Window(log, 5f, 11f, out float mean, out float half, out float vf, out float hz, out _);
            TestContext.WriteLine($"level: alt {mean:F3} +- {half:F3}, vf {vf:F2}, {hz:F2} beats/s (proto 4.99 +- 0.07, 4.89, 2.57-2.67)");
            Assert.AreEqual(5f, mean, 0.05f, "mean altitude (PID: no steady sag)");
            Assert.Less(half, 0.3f, "altitude wobble (the bob included: 0.15, then 0.2 before the deeper beats)");
            Assert.AreEqual(4.9f, vf, 0.3f, "cruise speed");
            Assert.That(hz, Is.InRange(1.1f, 2.0f), "beat frequency (1.8-2.6 before the deeper beats)");
        }

        // User feedback 2026-10-18 (twice): "the wings should go WAY further down in the wing beats". Level
        // flight, 5-11 s: every downstroke sweeps the wrist far below its glide-pose height (~0.96 m, the stroke
        // angle ~70 deg below the glide pose; 0.54 m and 33 deg before the second request) - well below the belly.
        [Test] public void Flight_DeepDownstroke()
        {
            var s = Serpent();
            var r = MakeRig(s);
            float lo = float.MaxValue, angLo = float.MaxValue, belly = float.MaxValue;
            int beats0 = -1, deep = 0, last = -1;
            float beatLo = float.MaxValue;
            for (int k = 0; k < Mathf.RoundToInt(11f / H); k++)
            {
                r.Step(H, true);
                if (k * H < 5f) continue;
                ref WingState st = ref r.wings.State[0];
                Vector3 up = r.rots[WingedSerpentTestSpecies.Girdle] * Vector3.up;
                float hgt = Vector3.Dot(st.wrist - st.shoulder, up) - r.wings.WristHeight(0, 0f);
                lo = Mathf.Min(lo, hgt); angLo = Mathf.Min(angLo, st.angle);
                belly = Mathf.Min(belly, Vector3.Dot(st.wrist - r.joints[WingedSerpentTestSpecies.Girdle], up));
                if (beats0 < 0) { beats0 = st.beats; last = st.beats; }
                beatLo = Mathf.Min(beatLo, hgt);
                if (st.beats != last) { if (beatLo < -0.75f) deep++; beatLo = float.MaxValue; last = st.beats; }
            }
            int beats = last - beats0;
            TestContext.WriteLine($"wrist down to {-lo:F2} m below its glide height (stroke {-angLo * Mathf.Rad2Deg:F0} deg), {-belly:F2} m below the girdle; {deep} of {beats} beats deeper than 0.75 m");
            Assert.Less(lo, -0.85f, "the wrist sweeps far down");
            Assert.Less(angLo, -60f * Mathf.Deg2Rad, "a deep stroke angle");
            Assert.Less(belly, -0.7f, "well below the belly");
            Assert.GreaterOrEqual(deep, beats - 1, "every beat is deep");
            Assert.Greater(beats, 4);
        }

        [Test] public void Flight_ClimbDemand_RaisesRateAndAmplitude()
        {
            var r = MakeRig(Serpent());
            var log = Run(r, 9f, t => t < 6f ? 5f : 8f);
            Window(log, 3f, 6f, out float mLevel, out _, out _, out float hzLevel, out float ampLevel);
            Window(log, 6f, 9f, out _, out _, out _, out float hzClimb, out float ampClimb);
            float alt6 = log[Mathf.RoundToInt(6f / H) - 1].alt, alt9 = log[log.Count - 1].alt;
            float rateLevel = MeanRate(log, 3f, 6f), rateClimb = MeanRate(log, 6f, 9f);
            TestContext.WriteLine($"level {hzLevel:F2} Hz amp {ampLevel:F2} stroke {rateLevel:F2} rad/s; climb {hzClimb:F2} Hz amp {ampClimb:F2} stroke {rateClimb:F2} rad/s; alt {alt6:F2} -> {alt9:F2} (proto 2.67 -> 3.0 Hz, 4.93 -> 6.92)");
            // the stroke rate rises with the demand; with the deep band (comfortDown 1.03) the beat frequency need
            // not (the longer stroke of the larger amplitude eats the faster rate: 1.67 Hz both), so it may not fall
            Assert.Greater(rateClimb, rateLevel * 1.1f, "stroke rate rises with the climb demand");
            Assert.GreaterOrEqual(hzClimb, hzLevel, "beat rate does not fall with the climb demand");
            Assert.Greater(ampClimb, ampLevel + 0.1f, "amplitude rises with the climb demand");
            Assert.Greater(alt9 - alt6, 1.5f, "climbs");
        }

        // Grip x2 (the prototype's glide case): the held wings carry it, no upstrokes.
        [Test] public void Flight_Glide_NoStrokes()
        {
            var r = MakeRig(Serpent());
            r.model.GripScale = 2f;
            var log = Run(r, 8f);
            Window(log, 3.5f, 7f, out float mean, out float half, out _, out float hz, out _);
            TestContext.WriteLine($"grip x2: alt {mean:F3} +- {half:F3}, {hz:F2} beats/s (proto 6.08 +- 0.01, 0)");
            Assert.AreEqual(0f, hz, 1e-6f, "no wingbeats");
            Assert.Less(half, 0.1f, "steady");
            Assert.Greater(mean, 5f, "does not sink below the goal");
        }

        // A turn-rate goal banks the flyer; the stroke asymmetry rolls it, and the tilted support turns the
        // velocity at g tan(bank) / v (coordinated turn).
        [Test] public void Flight_BankTurn_Coordinated()
        {
            var r = MakeRig(Serpent());
            r.model.TurnRateGoal = 0.6f;
            Vector3 f0 = Vector3.zero; float bankSum = 0f, vSum = 0f, minAlt = float.MaxValue; int n = 0;
            float yaw = 0f; Vector3 prevF = r.model.Forward;
            Run(r, 12f, null, t =>
            {
                Vector3 f = r.model.Forward;
                if (t >= 6f)
                {
                    yaw += Vector3.SignedAngle(prevF, f, Vector3.up) * Mathf.Deg2Rad;
                    bankSum += r.model.Bank; vSum += r.model.HorizontalSpeed; n++;
                    minAlt = Mathf.Min(minAlt, r.model.Altitude);
                }
                prevF = f;
            });
            float rate = yaw / (n * H), bank = bankSum / n, v = vSum / n;
            float coordinated = FlightModel.G * Mathf.Tan(bank) / v;
            float target = Mathf.Atan(0.6f * v / FlightModel.G);
            TestContext.WriteLine($"bank {bank * Mathf.Rad2Deg:F1} deg (target {target * Mathf.Rad2Deg:F1}), yaw rate {rate:F3} rad/s, g tan / v = {coordinated:F3}, min alt {minAlt:F2}");
            Assert.Greater(bank, 0.5f * target, "banks into the turn (right)");
            Assert.Less(bank, r.model.Spec.maxBankDeg * Mathf.Deg2Rad * 1.1f, "bank limit");
            Assert.AreEqual(coordinated, rate, 0.2f * Mathf.Abs(coordinated), "coordinated turn");
            Assert.Greater(rate, 0.3f, "turns right");
            Assert.Greater(minAlt, 4f, "holds altitude in the turn");
        }

        // Froude similarity: at scale 4 the beat is half as fast (2.6 -> 1.3 Hz), speed doubles, altitude
        // holds in proportion.
        [Test] public void Flight_Froude()
        {
            var r1 = MakeRig(Serpent(), 1f);
            var r4 = MakeRig(Serpent(), 4f);
            var l1 = Run(r1, 12f);
            var l4 = Run(r4, 24f);
            Window(l1, 5f, 11f, out float m1, out float h1, out float v1, out float hz1, out _);
            Window(l4, 10f, 22f, out float m4, out float h4, out float v4, out float hz4, out _);
            TestContext.WriteLine($"scale 1: {hz1:F2} Hz, {v1:F2} m/s, alt {m1:F2}+-{h1:F2}; scale 4: {hz4:F2} Hz, {v4:F2} m/s, alt {m4:F2}+-{h4:F2}");
            Assert.AreEqual(0.5f, hz4 / hz1, 0.08f, "beat frequency / sqrt(scale)");
            Assert.AreEqual(2f, v4 / v1, 0.1f, "speed x sqrt(scale)");
            Assert.AreEqual(20f, m4, 0.4f, "altitude held at scale 4");
            Assert.Less(h4, 4f * 0.3f, "wobble scales with size (as Flight_HoldsAltitude's)");
        }

        // ------------------------------------------------------------------ wings

        // The wing steps (upstroke) exactly when the wrist would leave the band at the bottom, never inside
        // it, and a downstroke never goes below the band.
        [Test] public void Wing_StepAtBandBottom()
        {
            var r = MakeRig(Serpent());
            int beats = 0, checks = 0;
            for (int k = 0; k < Mathf.RoundToInt(6f / H); k++)
            {
                r.Step(H);
                ref WingState st = ref r.wings.State[0];
                if (st.beats != beats)
                {
                    beats = st.beats;
                    float rate = r.model.StrokeRate * r.model.WingAmplitude(r.wings.Side(0)) / r.model.Amplitude;
                    Assert.GreaterOrEqual(st.upFrom, st.lo - 1e-5f, "stepped below the band");
                    Assert.Less(st.upFrom - rate * H, st.lo + 1e-5f, "stepped before the wrist reached the band bottom");
                    checks++;
                }
                else if (!st.upstroke && st.angleRate < 0f)
                    Assert.GreaterOrEqual(st.angle, st.lo - 1e-5f, "downstroke left the band");
            }
            Assert.Greater(checks, 6); // ~1.3-1.5 Hz deep beats over 6 s (was > 10 at 2.2-2.7 Hz)
        }

        // The wrist stays in the vertical comfort band along the girdle up (the upstroke may overshoot the
        // top by overshootUp).
        [Test] public void Wing_WristInBand()
        {
            var s = Serpent();
            var r = MakeRig(s);
            float lo = float.MaxValue, hi = float.MinValue;
            for (int k = 0; k < Mathf.RoundToInt(8f / H); k++)
            {
                r.model.AltitudeGoal = k * H < 4f ? 5f : 8f; // level, then a hard climb (full amplitude)
                r.Step(H, true);
                for (int w = 0; w < 2; w++)
                {
                    ref WingState st = ref r.wings.State[w];
                    Vector3 up = r.rots[WingedSerpentTestSpecies.Girdle] * Vector3.up;
                    float hgt = Vector3.Dot(st.wrist - st.shoulder, up) - r.wings.WristHeight(w, 0f);
                    lo = Mathf.Min(lo, hgt); hi = Mathf.Max(hi, hgt);
                }
            }
            WingSpec ws = s.wings[0];
            TestContext.WriteLine($"wrist height relative to rest: {lo:F3} .. {hi:F3} (band -{ws.comfortDown} .. +{ws.comfortUp})");
            Assert.GreaterOrEqual(lo, -ws.comfortDown - 1e-3f, "below the band");
            Assert.LessOrEqual(hi, ws.comfortUp * (1f + 2f * ws.overshootUp) + 1e-3f, "above the band + overshoot");
            Assert.Less(lo, -0.3f, "uses the band");
            Assert.Greater(hi, 0.3f, "uses the band");
        }

        // Both wings are one beat group: same phase always, symmetric when flying straight, still in phase
        // when banking (asymmetric amplitudes).
        [Test] public void Wing_Synchronous()
        {
            var r = MakeRig(Serpent());
            for (int k = 0; k < Mathf.RoundToInt(10f / H); k++)
            {
                r.model.TurnRateGoal = k * H < 5f ? 0f : 0.6f;
                r.Step(H);
                WingState a = r.wings.State[0], b = r.wings.State[1];
                Assert.AreEqual(a.upstroke, b.upstroke, $"phase at step {k}");
                Assert.AreEqual(a.beats, b.beats, $"beat count at step {k}");
                if (k * H < 5f) Assert.AreEqual(a.angle, b.angle, 1e-5f, $"symmetric at step {k}");
            }
            Assert.Greater(r.wings.State[0].beats, 10); // ~1.3-1.5 Hz deep beats over 10 s (was > 20)
        }

        // Fingers keep their order in the wing plane (leading -> trailing, at least fingerMinGapDeg apart)
        // over the whole stroke and fold range.
        [Test] public void Wing_FingerOrder_NoInversion()
        {
            var s = Serpent();
            var r = MakeRig(s);
            float minGap = float.MaxValue;
            for (int w = 0; w < 2; w++)
                for (int ia = 0; ia <= 10; ia++)
                    for (int iff = 0; iff <= 10; iff++)
                    {
                        float ang = Mathf.Lerp(-r.wings.BandDownAngle(w), r.wings.BandUpAngle(w) * 1.08f, ia / 10f);
                        r.wings.SetPose(w, ang, iff / 10f);
                        r.wings.Pose();
                        WingState st = r.wings.State[w];
                        Vector3 back = r.wings.Side(w) * Vector3.Cross(st.handNormal, st.handOut);
                        float prev = float.NegativeInfinity;
                        for (int k = 0; k < WingSpec.Fingers; k++)
                        {
                            Vector3 d = r.wings.FingerDir[w * WingSpec.Fingers + k];
                            Assert.AreEqual(0f, Vector3.Dot(d, st.handNormal), 1e-4f, "finger out of the wing plane");
                            float a = Mathf.Atan2(Vector3.Dot(d, back), Vector3.Dot(d, st.handOut)) * Mathf.Rad2Deg;
                            if (k > 0) minGap = Mathf.Min(minGap, a - prev);
                            prev = a;
                        }
                    }
            Assert.GreaterOrEqual(minGap, s.wings[0].fingerMinGapDeg - 1e-3f, "finger order / gap");
        }
    }
}
