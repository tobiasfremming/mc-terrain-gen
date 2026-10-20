// Creature kit (sky manta): Soar - always-airborne flight for a body without legs or legacy wings (a manta ray in the
// sky, a gliding fish): slow, powerful flap bouts alternating with long glides, coordinated banked turns, an altitude
// band over the ground and under the roof, and now and then a barrel roll or a loop. Kit-only, IAirborne (the lab
// gives it an aviary it sizes itself) and IFlapDriver (its stroke drives the RayWings' chains).
//   Point mass   speed from thrust (flap bouts: a pulse on each downstroke), drag and gravity along the path: a glide
//                sinks at 1 / glideRatio, a bout climbs at climbDeg; the path's climb angle follows the altitude error
//                (altitudeSeconds), rate-limited. Turns are coordinated: bank -> turn rate g tan(bank) / v, the bank
//                eases in (bankSeconds), the path's curvature never bends the body past its joints' bend limit.
//   Bouts        start when the body is low in its band or slow, or a glide has run its time; a whole number of beats
//                (upstroke first), the amplitude easing in and out over half a beat.
//   Body         the spine joints lie on the head's own 3D path (a history of points and ups, by arc length), so the
//                disc follows its head through turns, rolls and loops; on it the body HEAVES opposite the stroke
//                (the wings' reaction: up on the downstroke), the heave travelling head -> tail (heaveLag), lengths
//                restored from the head. The body's acceleration is published for the wings' inertia.
//   Manoeuvres   a barrel roll (a helix about the flight line, rolling once) and a loop (a vertical circle), each only
//                with room under it and above it, away from the wall, when the brain allows (ManoeuvreAllowed).
// Containment: a turn-back before the aviary's wall (its safe radius less the half span) and the roof; the ground
// floor is hard (minAltitude above the probed ground). Froude: lengths x s, times and speeds x sqrt s, g size-free.
// Allocation-free after construction; main thread.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Module("Soar", CreatureModule.Locomotion, Summary = "Always-airborne flight without legs or legacy wings (manta rays, gliding fish): flap bouts alternating with long glides, coordinated banked turns, an altitude band over the ground and under the roof, barrel rolls and loops; the body lies on the head's 3D path and heaves opposite the stroke. Drives flapping parts (RayWing) through IFlapDriver; sizes its own aviary (IAirborne).",
            Needs = "a spine chain body (RayBody); flapping parts (RayWing) read its stroke", KitOnly = true)]
    public sealed class Soar : CreatureModule, IAirborne
    {
        [Param(ParamUnit.MetresPerSecond, "cruising speed", Example = "manta 3")] public float cruiseSpeed = 3f;
        [Param(ParamUnit.MetresPerSecond, "slowest flight: below it a flap bout starts", Example = "manta 2")] public float minSpeed = 2f;
        [Param(ParamUnit.MetresPerSecond, "fastest flight (a dive)")] public float maxSpeed = 5f;
        [Param(ParamUnit.Fraction, "glide ratio: distance per height lost in a glide at cruise", Min = 2, Max = 30, Example = "manta 11")] public float glideRatio = 11f;
        [Param(ParamUnit.Degrees, "climb angle of a flap bout at cruise", Min = 0, Max = 40)] public float climbDeg = 12f;
        [Param(ParamUnit.Degrees, "steepest dive", Min = 0, Max = 60)] public float diveDeg = 25f;
        [Param(ParamUnit.Hz, "wingbeat frequency", Example = "manta 0.5")] public float flapHz = 0.5f;
        [Param(ParamUnit.Count, "fewest beats in a bout", Min = 1, Max = 12)] public int beatsMin = 2;
        [Param(ParamUnit.Count, "most beats in a bout", Min = 1, Max = 12)] public int beatsMax = 4;
        [Param(ParamUnit.Seconds, "shortest glide between bouts")] public float glideMin = 4f;
        [Param(ParamUnit.Seconds, "longest glide between bouts")] public float glideMax = 9f;
        [Param(ParamUnit.Degrees, "steepest bank of a turn", Min = 5, Max = 75)] public float maxBankDeg = 45f;
        [Param(ParamUnit.Seconds, "time constant of banking into a turn")] public float bankSeconds = 0.7f;
        [Param(ParamUnit.Seconds, "steering: the heading error is turned out in about this time")] public float steerSeconds = 1.4f;
        [Param(ParamUnit.Seconds, "altitude: the error is flown out in about this time")] public float altitudeSeconds = 2.5f;
        [Param(ParamUnit.DegPerSecond, "fastest change of the climb angle")] public float pitchRateDeg = 25f;
        [Param(ParamUnit.Metres, "cruising height above the ground when the brain's target is on the ground", Example = "manta 6")] public float cruiseAltitude = 6f;
        [Param(ParamUnit.Metres, "lowest height of the body above the ground (hard floor)", Example = "manta 2.5")] public float minAltitude = 2.5f;
        [Param(ParamUnit.Metres, "half height of the altitude band a glide may sink through before a bout")] public float altitudeBand = 1.5f;
        [Param(ParamUnit.Metres, "body heave amplitude at a full beat (up on the downstroke)", Example = "manta 0.05")] public float heave = 0.05f;
        [Param(ParamUnit.Fraction, "heave wave: phase lag head -> tail, in cycles", Min = 0, Max = 0.5f)] public float heaveLag = 0.12f;
        [Param(ParamUnit.Metres, "barrel roll: helix radius (0 = none)", Example = "manta 0.7")] public float barrelRadius = 0.7f;
        [Param(ParamUnit.Seconds, "barrel roll: duration")] public float rollSeconds = 2.6f;
        [Param(ParamUnit.Metres, "loop: radius (0 = none)", Example = "manta 1.8")] public float loopRadius = 1.8f;
        [Param(ParamUnit.Seconds, "shortest time between manoeuvres")] public float manoeuvreMin = 12f;
        [Param(ParamUnit.Seconds, "longest time between manoeuvres")] public float manoeuvreMax = 28f;

        public override ILocomotionMode CreateMode(CreatureSim sim, in ModeCtx c) => new SoarFlight(sim, this, c);

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return "Soar needs a spine chain body";
            if (!(cruiseSpeed > 0f) || !(minSpeed > 0f) || minSpeed > cruiseSpeed || maxSpeed < cruiseSpeed) return "Soar: 0 < minSpeed <= cruiseSpeed <= maxSpeed";
            if (beatsMin < 1 || beatsMax < beatsMin) return "Soar: 1 <= beatsMin <= beatsMax";
            if (glideMin < 0f || glideMax < glideMin) return "Soar: 0 <= glideMin <= glideMax";
            if (!(flapHz > 0f) || !(glideRatio >= 2f)) return "Soar: flapHz > 0, glideRatio >= 2";
            if (!(minAltitude > 0f) || cruiseAltitude < minAltitude) return "Soar: 0 < minAltitude <= cruiseAltitude";
            for (int i = 0; i < c.Modules.Count; i++) if (c.Modules[i] is Walk || c.Modules[i] is Fly) return "Soar never walks or lands: no Walk or Fly with it";
            return null;
        }

        // ------------------------------------------------------------ IAirborne

        const float kSchoolSpans = 6.5f, kHeadroom = 2.5f;

        internal static float HalfSpan(CreatureSpecies sp)
        {
            RayShape shape = RayShape.For(KitView.Parts(sp.Compiled.Runtime));
            return shape != null ? shape.HalfSpan : sp.Compiled.Runtime.MaxLateralReach;
        }

        // room for a school: kSchoolSpans spans of radius (a dozen wide circles), and the cruise turn twice over
        public float AviaryRadius(CreatureSpecies sp, float maxScale, float baseRadius)
        {
            float based = baseRadius * Mathf.Clamp(maxScale, 1f, 1.5f);
            float span = 2f * HalfSpan(sp) * maxScale;
            float v = cruiseSpeed * Mathf.Sqrt(maxScale);
            float turn = v * v / (FlightModel.G * Mathf.Tan(0.5f * maxBankDeg * Mathf.Deg2Rad));
            return Mathf.Max(based, Mathf.Max(kSchoolSpans * span, 4f * turn + span));
        }

        // the floor, a loop's height, a span of room either side and headroom
        public float AviaryHeight(CreatureSpecies sp, float maxScale, float baseHeight)
        {
            float based = baseHeight * Mathf.Clamp(maxScale, 1f, 1.5f);
            float need = (minAltitude + 2f * loopRadius + 2f * HalfSpan(sp) + kHeadroom) * maxScale;
            return Mathf.Max(based, Mathf.Max(need, (cruiseAltitude + 2f * HalfSpan(sp) + kHeadroom) * maxScale));
        }

        public bool FitsAviary(CreatureSpecies sp, EnclosureArea area, float sizeScale)
        {
            float v = cruiseSpeed * Mathf.Sqrt(sizeScale);
            float room = area.radius - HalfSpan(sp) * sizeScale;
            bool turns = v * v / (FlightModel.G * Mathf.Tan(maxBankDeg * Mathf.Deg2Rad)) <= 0.25f * room;
            bool tall = area.maxHeight >= (minAltitude + 2f * loopRadius + 2f * HalfSpan(sp)) * sizeScale;
            return turns && tall;
        }
    }

    public enum SoarManoeuvre { None = 0, Barrel = 1, Loop = 2 }

    // One individual's Soar flight (the mode CreatureSim asks first each sub-step; it always claims it).
    public sealed class SoarFlight : ILocomotionMode, IFlapDriver
    {
        const int kHistory = 1024;
        const int kProbeEvery = 4;
        const float kWindow = 40f;          // m at scale 1: ground search window below the head
        const float kRetry = 2f;            // s at scale 1: a manoeuvre not allowed now is asked again after this

        readonly CreatureSim sim;
        readonly Soar f;
        readonly IGroundProbe ground;
        readonly Func<Vector3, Vector3> upAt;
        readonly int n;
        readonly float s, T, seg, halfSpan, kappaMax, gGlide, gClimb, clearance;
        readonly Vector3[] hp, hu;          // path history: points and ups, ring
        readonly Vector3[] J, Ju;           // scratch: joints and their ups
        int head, count;
        uint rng;

        // flight state
        Vector3 P, F, U, heading;           // head point, forward, up, horizontal heading
        float V, bank, gamma;
        float groundY = float.NaN; Vector3 groundUp = Vector3.up; int probe;
        // stroke
        float phase, amp, boutPhase, boutEnd, glideLeft;
        bool bout;
        // manoeuvre
        float manoeuvreLeft, alpha, mV0;
        Vector3 mP0, mF0, mU0, mR0, lastP;

        public bool ManoeuvreAllowed = true;  // the brain says no when a neighbour is close
        public bool Sustain;                  // tests, cinematics: keep beating (a bout never ends)
        public float Urgency;                 // 0..1 from the brain (a neighbour close ahead): steer, bank and climb quicker
        public SoarManoeuvre Manoeuvre { get; private set; }
        public int Manoeuvres { get; private set; }
        public int Bouts { get; private set; }
        public bool Flapping => bout || amp > 0f;
        public float Speed => V;
        public float Altitude { get; private set; }
        public float HalfSpan => halfSpan * s;
        public Vector3 Forward => F;
        public Vector3 Up => U;
        public float WantAltitude { get; private set; }

        int ILocomotionMode.Priority => 100;

        // IFlapDriver
        public float StrokePhase => phase;
        public float StrokeAmplitude => amp;
        public float StrokeHz => f.flapHz / T;
        public float Asymmetry => f.maxBankDeg > 0f ? Mathf.Clamp(-bank / (f.maxBankDeg * Mathf.Deg2Rad), -1f, 1f) : 0f;
        public float Bank => bank;
        public Vector3 BodyAcceleration { get; private set; }

        public SoarFlight(CreatureSim sim, Soar soar, in ModeCtx c)
        {
            this.sim = sim; f = soar; ground = c.Ground; upAt = c.UpAt;
            CreatureSpecies sp = sim.Species;
            n = sp.JointCount; s = sim.SizeScale; T = sim.TimeScale;
            seg = sp.spacing * s;
            clearance = sp.joints[0].clearance * s;
            halfSpan = Soar.HalfSpan(sp);
            float bend = 0f;
            for (int i = 1; i < n - 1; i++) bend = Mathf.Max(bend, sp.joints[i].bendLimitDeg);
            kappaMax = bend > 0f ? 0.97f * bend * Mathf.Deg2Rad / seg : 1e3f;
            gGlide = Mathf.Atan(1f / f.glideRatio);
            gClimb = f.climbDeg * Mathf.Deg2Rad;
            hp = new Vector3[kHistory]; hu = new Vector3[kHistory];
            J = new Vector3[n]; Ju = new Vector3[n];
            rng = 0x9E3779B9u ^ (uint)(sim.Seed * 7919 + 17);
            if (rng == 0) rng = 1;
            // the history starts as the body lies (tail first, the head last)
            for (int i = n - 1; i >= 0; i--) Push(sim.Joints[i], sim.Rotations[i] * Vector3.up);
            P = sim.Joints[0];
            Vector3 up = Norm(upAt(P), Vector3.up);
            heading = Norm(Vector3.ProjectOnPlane(sim.Rotations[0] * Vector3.forward, up), Vector3.forward);
            F = heading; U = up;
            V = f.cruiseSpeed * T;
            glideLeft = 0f;   // start with a bout: the take-off
            manoeuvreLeft = Lerp(f.manoeuvreMin, f.manoeuvreMax, Rand()) * T;
            lastP = P;
        }

        void Push(Vector3 p, Vector3 u)
        {
            head = (head + 1) % kHistory; hp[head] = p; hu[head] = u;
            if (count < kHistory) count++;
        }

        float Rand()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFFu) / 16777216f;
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        static Vector3 Norm(Vector3 v, Vector3 fb)
        {
            float m = v.magnitude;
            return m > 1e-9f ? v / m : fb;
        }

        // ------------------------------------------------------------ the sub-step

        public bool SubStep(CreatureSim sm, float dt, Vector3 target)
        {
            if (!(dt > 0f)) return true;
            Vector3 up = Norm(upAt(P), Vector3.up);
            ProbeGround(up);
            Stroke(dt);
            if (Manoeuvre == SoarManoeuvre.None) Fly(dt, target, up);
            else Manoeuvring(dt, up);
            Push(P, U);
            LayBody(dt);
            sim.SetFlightSpeed(V);
            return true;
        }

        void ProbeGround(Vector3 up)
        {
            if (--probe > 0 && !float.IsNaN(groundY)) return;
            probe = kProbeEvery;
            if (ground.Probe(P - up * (float.IsNaN(groundY) ? 0f : Mathf.Max(0f, Altitude)), up, kWindow * s, out Vector3 g, out Vector3 gn))
            { groundY = Vector3.Dot(g, up); groundUp = up; }
            else if (float.IsNaN(groundY)) groundY = Vector3.Dot(P, up) - f.cruiseAltitude * s;
        }

        // Flap bouts and glides; the stroke's phase and eased amplitude.
        void Stroke(float dt)
        {
            float omega = 2f * Mathf.PI * f.flapHz / T;
            float lowAlt = WantAltitude - f.altitudeBand * s;
            if (!bout && Manoeuvre == SoarManoeuvre.None)
            {
                glideLeft -= dt;
                // a glide runs at least its time unless the body is near the floor or stalling; then a bout starts
                // when the glide has sunk through the band or slowed
                bool urgent = Altitude < (f.minAltitude + f.altitudeBand) * s || V < f.minSpeed * T * 0.9f || Urgency > 0.4f;   // dodging: beat (power to turn and climb)
                bool due = (glideLeft <= 0f && (Altitude < WantAltitude - 0.5f * f.altitudeBand * s || V < f.minSpeed * T * 1.05f)) || Altitude < lowAlt;
                if ((urgent || due || Sustain) && amp <= 0f)
                {
                    bout = true; boutPhase = 0f; Bouts++;
                    int beats = f.beatsMin + Mathf.Min(f.beatsMax - f.beatsMin, (int)(Rand() * (f.beatsMax - f.beatsMin + 1)));
                    boutEnd = beats * 2f * Mathf.PI;
                    phase = 0f;
                }
            }
            float wantAmp = 0f;
            if (bout)
            {
                boutPhase += omega * dt;
                if (Sustain) boutEnd = Mathf.Max(boutEnd, boutPhase + 4f * Mathf.PI);
                // still under the band's middle when the beats run out: one more (up to twice the most beats)
                else if (boutPhase >= boutEnd - Mathf.PI && boutPhase < boutEnd - Mathf.PI + omega * dt * 1.5f
                         && Altitude < WantAltitude - 0.2f * f.altitudeBand * s && boutEnd < 4f * Mathf.PI * f.beatsMax)
                    boutEnd += 2f * Mathf.PI;
                wantAmp = boutPhase < boutEnd - Mathf.PI ? 1f : 0f;
                if (boutPhase >= boutEnd) { bout = false; glideLeft = Lerp(f.glideMin, f.glideMax, Rand()) * T; }
            }
            // ease over half a beat
            float k = 1f - Mathf.Exp(-dt * omega / Mathf.PI * 2f);
            amp += (wantAmp - amp) * k;
            if (!bout && amp < 0.01f) { amp = 0f; phase = 0f; }
            else phase += omega * dt;
            if (phase > 2f * Mathf.PI) phase -= 2f * Mathf.PI;
            float hz = f.heave * s * amp * omega * omega * Mathf.Sin(phase);   // d2/dt2 of -heave sin(phase)
            BodyAcceleration = U * hz;
        }

        // Ordinary flight: steer at the target, hold the altitude band, bank into turns; manoeuvres when due.
        void Fly(float dt, Vector3 target, Vector3 up)
        {
            EnclosureArea? area = sim.Bounds;
            Altitude = Vector3.Dot(P, up) - groundY;
            // altitude wanted: the target's, unless it lies on the ground (a walking brain's point): then cruise
            float tAlt = Vector3.Dot(target, up) - groundY;
            float want = tAlt < f.minAltitude * s ? f.cruiseAltitude * s : tAlt;
            // a bout climbs through the band, the glide after it sinks back through it
            float wantCruise = want;
            if (bout) want += f.altitudeBand * s;
            float floor = (f.minAltitude + 0.2f) * s;
            float ceiling = float.PositiveInfinity;
            if (area.HasValue && area.Value.maxHeight > 0f)
                ceiling = Vector3.Dot(area.Value.center, up) + area.Value.maxHeight - groundY - (halfSpan + 0.6f) * s;
            want = Mathf.Clamp(want, floor, Mathf.Max(floor, ceiling));
            WantAltitude = Mathf.Clamp(wantCruise, floor, Mathf.Max(floor, ceiling));

            // heading: at the target, turned back before the wall
            Vector3 to = Vector3.ProjectOnPlane(target - P, up);
            Vector3 desired = to.sqrMagnitude > 1e-6f * s * s ? to.normalized : heading;
            if (area.HasValue) desired = Contain(area.Value, up, desired);
            float yawErr = Vector3.SignedAngle(heading, desired, up) * Mathf.Deg2Rad;
            float vmin = Mathf.Max(V, 0.3f * f.minSpeed * T);
            float maxBank = f.maxBankDeg * Mathf.Deg2Rad;
            float rateCap = Mathf.Min(FlightModel.G * Mathf.Tan(maxBank) / vmin, kappaMax * vmin);
            float quick = 1f - 0.6f * Mathf.Clamp01(Urgency);
            float rate = Mathf.Clamp(yawErr / (f.steerSeconds * T * quick), -rateCap, rateCap);
            float bankWant = Mathf.Atan(rate * vmin / FlightModel.G);
            bank += (bankWant - bank) * (1f - Mathf.Exp(-dt / (f.bankSeconds * T * quick)));
            float turn = FlightModel.G * Mathf.Tan(bank) / vmin;
            turn = Mathf.Clamp(turn, -kappaMax * vmin, kappaMax * vmin);
            heading = Norm(Quaternion.AngleAxis(turn * dt * Mathf.Rad2Deg, up) * heading, heading);

            // climb angle from the altitude error; a glide cannot climb (it trades speed), a bout can
            float err = want - Altitude;
            float gWant = Mathf.Atan2(err, vmin * f.altitudeSeconds * T * quick);
            // a bout climbs; a glide follows its own slope down (it keeps its speed) - it never climbs on its own
            float top = amp > 0.5f ? 1.5f * gClimb : -gGlide * (1f - amp);
            gWant = Mathf.Clamp(gWant, -f.diveDeg * Mathf.Deg2Rad, top);
            if (Altitude < floor) gWant = Mathf.Max(gWant, gClimb);   // low (the take-off): climb
            float pr = f.pitchRateDeg * Mathf.Deg2Rad / T * dt;
            gamma += Mathf.Clamp(gWant - gamma, -pr, pr);

            // speed: thrust (a pulse on each downstroke), drag, gravity along the path
            float vc = f.cruiseSpeed * T;
            float drag = FlightModel.G * Mathf.Sin(gGlide) * (V / vc) * (V / vc);
            // (the stroke pushes less the faster the body already flies: (vc / v)^2)
            float thrust = (FlightModel.G * Mathf.Sin(gGlide) + FlightModel.G * Mathf.Sin(gClimb)) * amp * (1f - 0.5f * Mathf.Cos(phase)) * Mathf.Min(1.5f, (vc / V) * (vc / V));
            V += (thrust - drag - FlightModel.G * Mathf.Sin(gamma)) * dt;
            V = Mathf.Clamp(V, 0.6f * f.minSpeed * T, f.maxSpeed * T);

            F = Norm(heading * Mathf.Cos(gamma) + up * Mathf.Sin(gamma), heading);
            Vector3 u0 = Norm(Vector3.ProjectOnPlane(up, F), up);
            Vector3 right = Vector3.Cross(u0, F);
            U = Norm(u0 * Mathf.Cos(bank) + right * Mathf.Sin(bank), u0);
            P += F * (V * dt);
            // the hard floor: the body never sinks into the ground (below minAltitude it climbs: gWant above)
            float alt = Vector3.Dot(P, up) - groundY;
            if (alt < clearance) P += up * (clearance - alt);
            if (area.HasValue) P = Clamp(area.Value, up, P);

            manoeuvreLeft -= dt;
            if (manoeuvreLeft <= 0f) TryManoeuvre(up, ceiling);
        }

        // The planar room the head has: the aviary's radius less the half span and a margin.
        float Room(in EnclosureArea a) => Mathf.Max(0f, a.radius - (halfSpan + 0.5f) * s);

        Vector3 Contain(in EnclosureArea a, Vector3 up, Vector3 desired)
        {
            float room = Room(a);
            Vector3 d = Vector3.ProjectOnPlane(P - a.center, up);
            float vmin = Mathf.Max(V, f.minSpeed * T);
            float turnR = vmin * vmin / (FlightModel.G * Mathf.Tan(0.8f * f.maxBankDeg * Mathf.Deg2Rad));
            turnR = Mathf.Max(turnR, 1f / kappaMax);
            Vector3 ahead = d + heading * (2.2f * turnR);
            float r = ahead.magnitude;
            float edge = room - 0.5f * turnR;
            if (r <= edge || r < 1e-4f) return desired;
            // past the edge ahead: turn toward the centre, harder the further past
            float w = Mathf.Clamp01((r - edge) / Mathf.Max(0.5f * turnR, 1e-3f));
            Vector3 inward = Norm(-d, desired);
            return Norm(Vector3.Lerp(desired, inward, w), inward);
        }

        Vector3 Clamp(in EnclosureArea a, Vector3 up, Vector3 p)
        {
            float room = Room(a);
            Vector3 d = p - a.center;
            float h = Vector3.Dot(d, up);
            Vector3 planar = d - up * h;
            float m = planar.magnitude;
            if (m > room && m > 1e-6f) p = a.center + planar * (room / m) + up * h;
            if (a.maxHeight > 0f)
            {
                float roof = a.maxHeight - (halfSpan + 0.3f) * s;
                if (h > roof) p -= up * (h - roof);
            }
            return p;
        }

        // ------------------------------------------------------------ manoeuvres

        void TryManoeuvre(Vector3 up, float ceiling)
        {
            manoeuvreLeft = kRetry * T;
            if (!ManoeuvreAllowed || bout || Mathf.Abs(bank) > 8f * Mathf.Deg2Rad || Mathf.Abs(gamma) > 10f * Mathf.Deg2Rad) return;
            bool barrel = f.barrelRadius > 0f, loop = f.loopRadius > 0f;
            if (!barrel && !loop) { manoeuvreLeft = float.PositiveInfinity; return; }
            SoarManoeuvre kind = barrel && loop ? (Rand() < 0.5f ? SoarManoeuvre.Barrel : SoarManoeuvre.Loop) : barrel ? SoarManoeuvre.Barrel : SoarManoeuvre.Loop;
            if (!Room(kind, up, ceiling)) return;
            Begin(kind);
        }

        bool Room(SoarManoeuvre kind, Vector3 up, float ceiling)
        {
            float span = halfSpan * s;
            float rise = kind == SoarManoeuvre.Loop ? 2f * f.loopRadius * s : 2f * f.barrelRadius * s;
            float floor = (f.minAltitude * s) + span;
            if (Altitude < floor || Altitude + rise + span > ceiling) return false;
            EnclosureArea? area = sim.Bounds;
            if (area.HasValue)
            {
                float time = kind == SoarManoeuvre.Loop ? 2f * Mathf.PI * f.loopRadius * s / Mathf.Max(V, 1e-3f) : f.rollSeconds * T;
                float travel = kind == SoarManoeuvre.Loop ? f.loopRadius * s : V * time;
                Vector3 d = Vector3.ProjectOnPlane(P - area.Value.center, up);
                if ((d + heading * (travel + 2f * span)).magnitude > Room(area.Value) - span) return false;
            }
            return true;
        }

        // Tests: start a manoeuvre now (ignores the schedule and the brain, not the room it needs).
        public bool ForceManoeuvre(SoarManoeuvre kind)
        {
            Vector3 up = Norm(upAt(P), Vector3.up);
            float ceiling = float.PositiveInfinity;
            EnclosureArea? area = sim.Bounds;
            if (area.HasValue && area.Value.maxHeight > 0f)
                ceiling = Vector3.Dot(area.Value.center, up) + area.Value.maxHeight - groundY - (halfSpan + 0.6f) * s;
            if (Manoeuvre != SoarManoeuvre.None || !Room(kind, up, ceiling)) return false;
            Begin(kind);
            return true;
        }

        void Begin(SoarManoeuvre kind)
        {
            Manoeuvre = kind; Manoeuvres++;
            alpha = 0f; mP0 = P; mF0 = F; mU0 = U; mV0 = V; lastP = P;
            mR0 = Vector3.Cross(U, F);
            bank = 0f;
        }

        void Manoeuvring(float dt, Vector3 up)
        {
            Altitude = Vector3.Dot(P, up) - groundY;
            if (Manoeuvre == SoarManoeuvre.Loop)
            {
                float r = f.loopRadius * s;
                V = mV0 * (1f - 0.12f * (1f - Mathf.Cos(alpha)));   // a little slower over the top
                float da = Mathf.Min(V * dt / r, 2f * Mathf.PI - alpha);
                alpha += da;
                float c = Mathf.Cos(da), sn = Mathf.Sin(da);
                Vector3 nf = F * c + U * sn, nu = U * c - F * sn;
                // the chord of the arc: exact circle
                P += (F + nf) * (0.5f * V * dt);
                F = Norm(nf, F); U = Norm(Vector3.ProjectOnPlane(nu, F), nu);
                if (alpha >= 2f * Mathf.PI - 1e-5f) End(up);
            }
            else
            {
                float time = f.rollSeconds * T;
                alpha = Mathf.Min(alpha + 2f * Mathf.PI * dt / time, 2f * Mathf.PI);   // the clock (as an angle)
                float t = alpha / (2f * Mathf.PI) * time;
                // the roll itself eases in and out (smoothstep): the path leaves and rejoins the flight line tangentially
                float k = alpha / (2f * Mathf.PI), roll = 2f * Mathf.PI * k * k * (3f - 2f * k);
                Vector3 ua = mU0 * Mathf.Cos(roll) + mR0 * Mathf.Sin(roll);
                Vector3 np = mP0 + mF0 * (mV0 * t) + (mU0 - ua) * (f.barrelRadius * s);
                Vector3 v = np - P;
                F = Norm(v, F);
                U = Norm(Vector3.ProjectOnPlane(ua, F), ua);
                V = v.magnitude / dt;
                P = np;
                if (alpha >= 2f * Mathf.PI) { V = mV0; End(up); }
            }
            float alt = Vector3.Dot(P, up) - groundY;
            if (alt < clearance) P += up * (clearance - alt);
        }

        void End(Vector3 up)
        {
            Manoeuvre = SoarManoeuvre.None;
            F = mF0; U = mU0;
            heading = Norm(Vector3.ProjectOnPlane(F, up), heading);
            gamma = Mathf.Asin(Mathf.Clamp(Vector3.Dot(F, up), -1f, 1f));
            bank = 0f;
            manoeuvreLeft = Lerp(f.manoeuvreMin, f.manoeuvreMax, Rand()) * T;
        }

        // ------------------------------------------------------------ the body on the path

        // Joints by arc length back along the history from the head, the heave wave on them, lengths restored.
        void LayBody(float dt)
        {
            int idx = head, left = count - 1;
            Vector3 a = hp[idx], au = hu[idx];
            float acc = 0f;
            J[0] = a; Ju[0] = au;
            for (int i = 1; i < n; i++)
            {
                float want = i * seg;
                while (true)
                {
                    if (left <= 0)
                    {
                        // past the oldest sample: straight on along the last segment
                        Vector3 back = i >= 2 ? Norm(J[i - 1] - J[i - 2], -F) : -F;
                        J[i] = J[i - 1] + back * seg; Ju[i] = Ju[i - 1];
                        break;
                    }
                    int prev = (idx - 1 + kHistory) % kHistory;
                    Vector3 b = hp[prev];
                    float l = (b - a).magnitude;
                    if (acc + l >= want && l > 1e-9f)
                    {
                        float t = (want - acc) / l;
                        J[i] = Vector3.Lerp(a, b, t);
                        Ju[i] = Norm(Vector3.Lerp(au, hu[prev], t), au);
                        break;
                    }
                    acc += l; a = b; au = hu[prev]; idx = prev; left--;
                }
            }
            // heave: up on the downstroke, travelling head -> tail
            if (amp > 0f && f.heave > 0f)
                for (int i = 0; i < n; i++)
                {
                    float lag = 2f * Mathf.PI * f.heaveLag * i / Mathf.Max(1, n - 1);
                    J[i] += Ju[i] * (-f.heave * s * amp * Mathf.Sin(phase - lag));
                }
            // exact segment lengths from the head
            for (int i = 1; i < n; i++) J[i] = J[i - 1] + Norm(J[i] - J[i - 1], -F) * seg;
            Vector3[] joints = sim.Joints, path = sim.Path, ups = sim.Up;
            Quaternion[] rot = sim.Rotations;
            for (int i = 0; i < n; i++)
            {
                Vector3 fw = i == 0 ? (n > 1 ? Norm(J[0] - J[1], F) : F) : Norm(J[i - 1] - J[i], F);
                Vector3 u = Norm(Vector3.ProjectOnPlane(Ju[i], fw), U);
                joints[i] = J[i]; path[i] = J[i]; ups[i] = u;
                rot[i] = CreatureSim.Basis(fw, u);
            }
        }

        // ------------------------------------------------------------ tests

        public float HeaveOf(int joint)
        {
            float lag = 2f * Mathf.PI * f.heaveLag * joint / Mathf.Max(1, n - 1);
            return -f.heave * s * amp * Mathf.Sin(phase - lag);
        }
    }
}
