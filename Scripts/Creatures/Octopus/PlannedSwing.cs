using System;
using UnityEngine;

// The planned body's reduced-order model (planned-body rework, 2026-10-19): the body's main motion, planned instead
// of emerging from the arms. One point - the arm crown, where the arms leave the body - hangs on up to MaxHolds
// unilateral ropes (a holding arm each: pivot = where the arm leaves the bark, length = the run to the crown):
//   one rope taut   a pendulum (spherical; its sideways part is damped, the swing stays in the plane of SwingDir)
//   two ropes       a sling: the point rests where both are at most their length
//   none / slack    ballistic flight
// Position-based: integrate with gravity and the control, project onto every rope (|x - pivot| <= length), take
// the velocity from the projected motion - a rope that goes taut stops the motion into it at once (an inelastic
// catch). Ropes reel toward RopeTarget at ReelSpeed.
// Control (one taut hold): Pumping drives the swing's energy toward the amplitude TargetAmplitude with a push along
// the swing tangent in phase with the motion (|push| <= MaxPush g; a negative error brakes); otherwise RestDamping
// (1/s) damps the whole velocity. LateralDamping always calms the part across the swing plane.
// Pure C#, deterministic, no allocation after construction, no queries. Lengths in metres, times in seconds: a
// larger body (longer ropes) swings slower by sqrt(scale) by itself.
public sealed class PlannedSwing
{
    public const int MaxHolds = 8;

    public float Gravity = 9.81f;
    public Vector3 Up = Vector3.up;
    public int SubSteps = 4;

    public Vector3 Position, Velocity;
    public Vector3 Acceleration { get; private set; }   // mean over the last Step (the soft body's drive)

    public readonly Vector3[] Pivot = new Vector3[MaxHolds];
    public readonly float[] Rope = new float[MaxHolds], RopeTarget = new float[MaxHolds];
    public readonly bool[] Active = new bool[MaxHolds];
    public int HoldCount { get; private set; }
    public float ReelSpeed = 0.4f;                       // m/s, at most
    public float ReelAccel = 1.2f;                       // m/s^2: a reel starts and stops smoothly
    public float ReelGain = 3f;                          // 1/s: the reel slows as the rope nears its target
    readonly float[] reelVel = new float[MaxHolds];

    public bool Pumping;
    public float TargetAmplitude;                        // rad
    public Vector3 SwingDir;                             // the swing plane's level direction (forward)
    public float PumpGain = 1.5f, MaxPush = 0.25f;       // push = gain x energy error (x g), |push| <= MaxPush g
    public float RestDamping;                            // 1/s amplitude decay rate, when not pumping
    public float LateralDamping = 2f;                    // 1/s
    // Circling (play): instead of the planar pump, the body is driven round the single hold in a cone of half angle
    // ConeAngle (a conical pendulum: tangential speed sqrt(g l sin tan), the angle held by a soft spring); the
    // lateral calming is off. |push| <= MaxPush g as for the pump.
    public bool Circling;
    public float ConeAngle;                              // rad
    public float CircleSense = 1f;                       // +1 counter-clockwise seen from above, -1 clockwise
    // A soft catch: a rope added slack (AddHold(..., soft: true)) does not stop the body dead when it goes taut - it
    // pays out at the body's speed along it and brakes at BrakeAccel (m/s^2) to the reel's own motion (a drop caught
    // by a stretching arm, not a snatch).
    public float BrakeAccel = 2f * 9.81f;
    readonly bool[] soft = new bool[MaxHolds], engaged = new bool[MaxHolds];

    // Braced (a many-armed body resting in its sling of holds): the arms hold it - no gravity, the motion it has
    // damped out at BraceRate (1/s), the ropes still kept. A sling of three ropes left to gravity sat at their
    // intersection and buzzed (the projections, one rope after the other, never quite agreed), so it never slept.
    public bool Braced;
    public float BraceRate = 5f;

    public float Amplitude { get; private set; }         // the energy amplitude about the single taut hold (rad)
    public float Energy { get; private set; }            // per unit mass, above the swing's lowest point (J/kg)

    public void Reset(Vector3 position, Vector3 velocity)
    {
        Position = position; Velocity = velocity; Acceleration = Vector3.zero;
        for (int i = 0; i < MaxHolds; i++) Active[i] = false;
        HoldCount = 0;
        UpdateEnergy();
    }

    // A rope from pivot, `length` long. Returns its slot (-1: all taken).
    public int AddHold(Vector3 pivot, float length, bool softCatch = false)
    {
        for (int i = 0; i < MaxHolds; i++)
        {
            if (Active[i]) continue;
            Active[i] = true; Pivot[i] = pivot; Rope[i] = length; RopeTarget[i] = length; reelVel[i] = 0f;
            soft[i] = softCatch; engaged[i] = false;
            HoldCount++;
            return i;
        }
        return -1;
    }

    public void Release(int i)
    {
        if (i < 0 || i >= MaxHolds || !Active[i]) return;
        Active[i] = false; HoldCount--;
    }

    public void ReleaseAll() { for (int i = 0; i < MaxHolds; i++) Active[i] = false; HoldCount = 0; }

    // The only active hold, or -1 (none or several).
    public int Single
    {
        get
        {
            if (HoldCount != 1) return -1;
            for (int i = 0; i < MaxHolds; i++) if (Active[i]) return i;
            return -1;
        }
    }

    readonly float[] reel = new float[MaxHolds];

    public bool Taut(int i) => Active[i] && (Position - Pivot[i]).magnitude >= Rope[i] - 1e-3f;
    public bool Braking(int i) => Active[i] && soft[i];

    public void Step(float dt)
    {
        if (!(dt > 0f)) return;
        int n = Math.Max(1, SubSteps);
        float h = dt / n;
        Vector3 v0 = Velocity;
        for (int s = 0; s < n; s++)
        {
            for (int i = 0; i < MaxHolds; i++)
            {
                if (!Active[i]) continue;
                float r0 = Rope[i];
                float want = Mathf.Clamp(ReelGain * (RopeTarget[i] - r0), -ReelSpeed, ReelSpeed);
                if (soft[i])
                {
                    // slack: the rope waits; going taut it takes the body's speed along it, then brakes
                    // (engaged the sub-step it would go taut in: one sub-step later the hard stop had already happened)
                    Vector3 d = Position - Pivot[i];
                    float len = d.magnitude, vr = len > 1e-6f ? OctoMath.Dot(Velocity, d) / len : 0f;
                    if (!engaged[i] && len + Mathf.Max(0f, vr) * h >= r0 - 1e-4f)
                    {
                        engaged[i] = true;
                        reelVel[i] = Mathf.Max(0f, vr);
                    }
                    if (engaged[i])
                    {
                        reelVel[i] = Mathf.MoveTowards(reelVel[i], Mathf.Min(want, 0f), BrakeAccel * h);
                        if (reelVel[i] <= 0f) soft[i] = false;
                    }
                    else reelVel[i] = 0f;
                }
                else reelVel[i] = Mathf.MoveTowards(reelVel[i], want, ReelAccel * h);
                Rope[i] = r0 + reelVel[i] * h;
                reel[i] = reelVel[i];   // signed: < 0 reeling in, > 0 paying out
            }
            Vector3 a = Braced ? Velocity * -BraceRate : Up * -Gravity + Control(Position, Velocity, true);
            Advance(ref Position, ref Velocity, a, h, Rope, reel);
        }
        Acceleration = (Velocity - v0) * (1f / dt);
        UpdateEnergy();
    }

    // Velocity Verlet for the free motion; a taut rope moves the point back onto its sphere and takes the velocity
    // out along it beyond the rope's own reeling speed (an inelastic rope that keeps the tangential motion: a
    // projected-position velocity lost a fifth of a free swing's energy in ten seconds).
    void Advance(ref Vector3 x, ref Vector3 v, Vector3 a, float h, float[] rope, float[] reelIn)
    {
        x += v * h + a * (0.5f * h * h);
        v += a * h;
        for (int pass = 0; pass < (HoldCount > 1 ? 3 : 1); pass++)
            for (int i = 0; i < MaxHolds; i++)
            {
                if (!Active[i]) continue;
                Vector3 d = x - Pivot[i];
                float len = d.magnitude;
                if (len <= rope[i] || len < 1e-6f) continue;
                Vector3 e = d * (1f / len);
                x = Pivot[i] + e * rope[i];
                // a taut rope lets the point move along it no faster than the rope's own reeling: pulled in at the reel
                // speed, or let down at it (paying out did not leave slack the body then fell into: no bounce)
                float vr = OctoMath.Dot(v, e), lim = reelIn != null ? reelIn[i] : 0f;
                if (vr > lim) v -= e * (vr - lim);
            }
    }

    Vector3 Control(Vector3 x, Vector3 v, bool live)
    {
        Vector3 acc = Vector3.zero;
        Vector3 f = SwingDir - Up * OctoMath.Dot(SwingDir, Up);
        float fl = f.magnitude;
        if (fl > 1e-4f && !(Circling && live))
        {
            f *= 1f / fl;
            // across the swing plane: calm
            Vector3 side = OctoMath.Cross(Up, f);
            acc -= side * (LateralDamping * OctoMath.Dot(v, side));
        }
        if (!live) return acc;
        int one = Single;
        if (Circling && one >= 0 && fl > 1e-4f) acc += Circle(x, v, one, f * (1f / fl));
        else if (Pumping && one >= 0 && fl > 1e-4f)
        {
            Vector3 r = x - Pivot[one];
            float l = r.magnitude;
            if (l > 1e-4f)
            {
                Vector3 e = r * (1f / l);
                Vector3 tau = OctoMath.Norm(f - e * OctoMath.Dot(f, e), f);
                float g = Gravity;
                float energy = 0.5f * v.sqrMagnitude + g * (l + OctoMath.Dot(r, Up));
                float want = g * l * (1f - Mathf.Cos(TargetAmplitude));
                float err = (want - energy) / (g * l);
                float vt = OctoMath.Dot(v, tau), vRef = 0.08f * Mathf.Sqrt(g * l);
                // in phase with the motion (smoothly through the apexes); from rest a gentle start forward
                float phase = vt / Mathf.Sqrt(vt * vt + vRef * vRef);
                float amp = Amplitude, kick = amp < 0.08f ? 1f - amp / 0.08f : 0f;
                if (err > 0f) phase += kick;
                float push = Mathf.Clamp(PumpGain * err, -MaxPush, MaxPush) * g;
                acc += tau * (push * Mathf.Clamp(phase, -1f, 1f));
            }
        }
        else if (RestDamping > 0f) acc -= v * (2f * RestDamping);   // amplitude ~ exp(-RestDamping t)
        return acc;
    }

    // The conical drive: tangential speed toward the cone's, the cone angle toward ConeAngle (a damped spring along
    // the meridian). From hanging still: a start along SwingDir.
    Vector3 Circle(Vector3 x, Vector3 v, int one, Vector3 f)
    {
        Vector3 r = x - Pivot[one];
        float l = r.magnitude;
        if (l < 1e-4f) return Vector3.zero;
        float g = Gravity, lim = MaxPush * g;
        float y = -OctoMath.Dot(r, Up);
        Vector3 rho = r + Up * y;
        float rl = rho.magnitude;
        if (rl < 0.03f * l) return f * lim;   // straight under the hold: lean out first
        Vector3 rh = rho * (1f / rl);
        Vector3 t = OctoMath.Cross(Up, rh) * (CircleSense < 0f ? -1f : 1f);   // round the hold, in the move's sense
        float th = Mathf.Atan2(rl, y), th0 = Mathf.Clamp(ConeAngle, 0.05f, 1.2f);
        float vWant = Mathf.Sqrt(g * l * Mathf.Sin(th0) * Mathf.Tan(th0));
        Vector3 e = rh * Mathf.Cos(th) + Up * Mathf.Sin(th);      // along the meridian, away from the bottom
        float w = Mathf.Sqrt(g / l);
        float at = Mathf.Clamp(1.2f * w * (vWant - OctoMath.Dot(v, t)), -lim, lim);
        float ae = Mathf.Clamp(w * w * l * (th0 - th) * 0.6f - 1.2f * w * OctoMath.Dot(v, e), -lim, lim);
        return t * at + e * ae;
    }

    void UpdateEnergy()
    {
        int one = Single;
        if (one < 0) { Amplitude = 0f; Energy = 0f; return; }
        Vector3 r = Position - Pivot[one];
        float l = Mathf.Max(r.magnitude, 1e-4f);
        Energy = 0.5f * Velocity.sqrMagnitude + Gravity * (l + OctoMath.Dot(r, Up));
        Amplitude = Mathf.Acos(Mathf.Clamp(1f - Energy / (Gravity * l), -1f, 1f));
    }

    // ------------------------------------------------------------------ prediction

    readonly float[] predRope = new float[MaxHolds];

    // Coasts the current state forward (ropes as they are, no pump, the sideways calming kept) for up to maxT
    // seconds and finds the forward apex: the first moment the motion along SwingDir stops while the point is
    // ahead of the single hold. arc: sampled positions (every ~maxT / arc.Length s). False: no apex in time.
    public bool PredictApex(float maxT, Vector3[] arc, out int arcCount, out Vector3 apex, out float apexTime)
    {
        const float h = 1f / 120f;
        for (int i = 0; i < MaxHolds; i++) predRope[i] = Rope[i];
        Vector3 x = Position, v = Velocity;
        Vector3 f = OctoMath.Norm(SwingDir - Up * OctoMath.Dot(SwingDir, Up), Vector3.zero);
        int one = Single;
        Vector3 piv = one >= 0 ? Pivot[one] : Position;
        int steps = Mathf.CeilToInt(maxT / h), every = arc != null && arc.Length > 0 ? Math.Max(1, steps / arc.Length + 1) : int.MaxValue;
        arcCount = 0;
        if (arc != null && arc.Length > 0) arc[arcCount++] = x;
        float prevV = OctoMath.Dot(v, f);
        apex = x; apexTime = 0f;
        for (int k = 1; k <= steps; k++)
        {
            Vector3 a = Up * -Gravity + Control(x, v, false);
            Advance(ref x, ref v, a, h, predRope, null);
            if (arc != null && k % every == 0 && arcCount < arc.Length) arc[arcCount++] = x;
            float vf = OctoMath.Dot(v, f);
            if (prevV > 0f && vf <= 0f && OctoMath.Dot(x - piv, f) > 0f)
            {
                // interpolate the zero crossing
                float u = prevV / (prevV - vf);
                apex = x - v * (h * (1f - u));
                apexTime = (k - 1 + u) * h;
                if (arc != null && arcCount < arc.Length) arc[arcCount++] = apex;
                return true;
            }
            prevV = vf;
        }
        apex = x; apexTime = maxT;
        return false;
    }
}
