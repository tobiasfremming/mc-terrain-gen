using System;
using CreatureKit;
using UnityEngine;

// The twisting manoeuvres a dragon-like flyer performs (AirTwist.Begin; FlightBrain picks them).
public enum TwistManoeuvre { None = 0, Corkscrew = 1, BarrelRoll = 2, Loop = 3, SlowRoll = 4 }

// Twisting flight data (the kit's Fly.twist; FlightSpec.twist at runtime). All zero = off: the air chain is
// the winged serpent's, bit for bit. Authored at scale 1: lengths x s, times x sqrt(s), rates / sqrt(s).
[Serializable]
public struct AirTwistSpec
{
    [Param(ParamUnit.Metres, "always-on swimming: the head's 3D S-curve amplitude (0 = no swimming)", Example = "dragon 0.12")]
    public float swimAmplitude;
    [Param(ParamUnit.Hz, "swimming wave frequency; wavelength along the body = speed / Hz", Example = "dragon 0.9")]
    public float swimHz;
    [Param(ParamUnit.Fraction, "vertical share of the swim (0 = a flat S, 1 = as tall as wide); its own slower phase turns the S-plane", Min = 0, Max = 1)]
    public float swimVertical;
    [Param(ParamUnit.Degrees, "roll into each swim curve (banking with the S), travels down the body")]
    public float swimRollDeg;
    [Param(ParamUnit.Metres, "corkscrew: helix radius of the head's path (0 = no corkscrews)", Example = "dragon 0.3")]
    public float corkscrewAmplitude;
    [Param(ParamUnit.Hz, "corkscrew: helix turns per second", Example = "dragon 0.8")]
    public float corkscrewHz;
    [Param(ParamUnit.Count, "corkscrew: helix turns per manoeuvre (0 = 3)", Min = 0, Max = 12)]
    public int corkscrewTurns;
    [Param(ParamUnit.Count, "corkscrew: full body rolls over the manoeuvre (the twist travels head to tail)", Min = 0, Max = 12)]
    public int corkscrewRolls;
    [Param(ParamUnit.Metres, "barrel roll: helix radius (0 = no barrel rolls); the body rolls once, belly out", Example = "dragon 0.5")]
    public float barrelRadius;
    [Param(ParamUnit.Seconds, "barrel roll duration (0 = 1.6)")]
    public float barrelTime;
    [Param(ParamUnit.Metres, "loop: radius of the vertical loop (0 = no loops)", Example = "dragon 0.9")]
    public float loopRadius;
    [Param(ParamUnit.Fraction, "loop: speed round the loop / flight speed (0 = 1); the head then surges to catch the flight line up again", Min = 0, Max = 3)]
    public float loopSpin;
    [Param(ParamUnit.Seconds, "slow roll: one full roll in place along the flight line (0 = no slow rolls)", Example = "dragon 2.4")]
    public float slowRollTime;
    [Param(ParamUnit.Seconds, "shortest pause between manoeuvres (0 = 4)")]
    public float intervalMin;
    [Param(ParamUnit.Seconds, "longest pause between manoeuvres (0 = 10)")]
    public float intervalMax;
    [Param(ParamUnit.DegPerSecond, "how fast the body rights itself onto the banked up (0 = 120)")]
    public float uprightRateDeg;
    [Param(ParamUnit.Fraction, "the wingbeat's shoulder bob on the twisting body: the share of the flight model's bob the girdle joint rides (0 = none: the body follows only the head's path, which never bobs; above 1 the body heaves more than the point mass, a stylised beat)", Min = 0, Max = 4, Example = "dragon 2.6")]
    public float girdleBob;
    [Param(ParamUnit.Fraction, "share of the girdle's bob at the head (the head is stabilised; the neck blends)", Min = 0, Max = 1, Example = "dragon 0.3")]
    public float bobHead;
    [Param(ParamUnit.Fraction, "share at the tail: behind the girdle the bob travels down the body along the path (each joint bobs as the girdle did where it is now), fading to this", Min = 0, Max = 1, Example = "dragon 0.4")]
    public float bobTail;
    [Param(ParamUnit.Fraction, "share of the bob behind the girdle that the body rides in phase with the girdle (the whole trunk heaves with the shoulders) instead of as the wave travelling down the path: a big bob at a fast beat is a short steep wave on its own (wavelength = speed / beat), which the bend cones cannot follow (0 = all travelling)", Min = 0, Max = 1, Example = "dragon 0.5")]
    public float bobInPhase;
    [Param(ParamUnit.Seconds, "the body rides only the beat in the shoulder bob: its slow part (a low-pass of this time constant: the stabilised path lagging a climb or a level-off) is taken out first, so a girdleBob above 1 heaves with the beat and never amplifies an altitude change (0 = the raw bob)", Min = 0, Max = 2, Example = "dragon 0.6")]
    public float bobHighPass;

    public bool CanCorkscrew => corkscrewAmplitude > 0f && corkscrewHz > 0f;
    public bool CanBarrel => barrelRadius > 0f;
    public bool CanLoop => loopRadius > 0f;
    public bool CanSlowRoll => slowRollTime > 0f;
    public bool Swims => swimAmplitude > 0f && swimHz > 0f;
    public bool Enabled => Swims || CanCorkscrew || CanBarrel || CanLoop || CanSlowRoll;

    public int CorkscrewTurns => corkscrewTurns > 0 ? corkscrewTurns : 3;
    public float BarrelTime => barrelTime > 0f ? barrelTime : 1.6f;
    public float LoopSpin => loopSpin > 0f ? loopSpin : 1f;
    public float IntervalMin => intervalMin > 0f ? intervalMin : 4f;
    public float IntervalMax => Mathf.Max(IntervalMin, intervalMax > 0f ? intervalMax : 10f);
    public float UprightRateDeg => uprightRateDeg > 0f ? uprightRateDeg : 120f;
    public bool Bobs => girdleBob > 0f;
}

// The twisting air chain (design: the dragon's whole body twists like a Chinese dragon). Instead of a pull
// chain trailing the girdle, the head leads and every joint follows the head's own path through the air
// (follow-the-leader on a recorded path): a 3D S-curve or a corkscrew of the head becomes a travelling wave
// down the body at exactly the flight speed (wavelength = speed / frequency), a loop is traced joint by joint.
// The path stores the head's up as well, so a roll (barrel roll, slow roll, the corkscrew's twist) travels
// from head to tail the same way. The up is parallel-transported along the head's path (rotation-minimising,
// continuous through loops and vertical stretches) and only then rotated by the roll, so frames never flip.
// The manoeuvre is a kinematic overlay on FlightModel's point (the physics flies on undisturbed); the head's
// path is kept inside the aviary and above the ground. Segment lengths are exact; bend cones hold.
// The wingbeat's bob (girdleBob): the head's path is the stabilised flight line, so on its own the body would not
// bob at all. The girdle joint rides the flight model's bob (AirChain sets ShoulderBob), the neck blends it down to
// bobHead at the head, and behind the girdle the bob is stamped into the path where the girdle passes: every joint
// behind bobs as the girdle did at that place (the bob travels down the body at the path speed, the wyvern's
// lagging wave), fading to bobTail. Along the world up (as the flight model bobs). A manoeuvre takes the body over: the
// bob fades out while it runs (with it the cone-limited joints snapped, frames turning 20-38 deg in a tick, and a
// banked bob pushed joints out of the aviary) and back in after.
// Allocation-free after construction; main thread; deterministic.
public sealed class AirTwist
{
    const float kSampleSpacing = 0.5f;   // a path sample every this many segment lengths of head travel
    const float kRampIn = 0.18f;         // corkscrew amplitude envelope: ramp fraction at each end
    const float kSwimFade = 1.5f;        // s at scale 1: the swim fades in after launch / out on landing
    const float kVerticalPhase = 0.618f; // the swim's vertical phase rate: the S-plane turns slowly
    const float kCatchUp = 1.8f;         // loop: the catch-up after the circle takes this many circle times
    const float kLoopSettle = 0.6f;      // s at scale 1: the swim fades out before the loop's circle starts
    const float kLoopSwimFade = 0.5f;    // s at scale 1: ... at this rate
    const float kMaxPathSpeed = 3.5f;    // the head's path speed is at most this many times the flight speed
    const float kJointFloor = 0.5f;      // joints stay this fraction of the girdle's clearance above the ground

    readonly AirTwistSpec t;
    readonly int n, cap;
    readonly float seg, scale, T;
    readonly float[] cosLimit, sinLimit;
    readonly float turnPerSeg;           // rad: the head's sharpest path turn per segment length travelled
    readonly Vector3[] pathPos, pathUp;
    readonly float[] pathBob;            // the girdle's bob where it passed the sample (stamped once)
    readonly bool[] pathStamped;
    readonly int girdle;
    float bobLevel;
    float bobSlow;                       // ShoulderBob's slow part (bobHighPass)
    Vector3 worldUp = Vector3.up;
    int newest = -1, count;
    bool seeded;

    Vector3 lead, tangent, up;          // the head: position, path direction, transported (unrolled) up
    Vector3 leadUp;                     // the head's up with the roll
    float swimPhase, swimPhase2, swimLevel;
    float mTime, mDuration, mSign, mSpeed;
    // loop: the circle's start point and plane, the base's arc travelled since it started, the catch-up's offset
    Vector3 loopF, loopU, loopC, loopDelta;
    float loopArc, loopLag;
    int loopPhase;                      // 0 settle, 1 circle, 2 catch-up
    // the base point's recent path (loops only): the head catches up along it
    readonly Vector3[] basePos;
    readonly int capH;
    int newestH = -1, countH;
    Vector3 lastBase;

    public TwistManoeuvre Current { get; private set; }
    public int Manoeuvres { get; private set; }
    public float Progress => Current != TwistManoeuvre.None && mDuration > 0f ? mTime / mDuration : 0f;
    public float Roll { get; private set; }          // rad, the head's roll about its path
    public Vector3 Lead => lead;
    public Vector3 LeadUp => leadUp;
    public Vector3 Tangent => tangent;
    public bool Busy => Current != TwistManoeuvre.None;
    public AirTwistSpec Spec => t;
    // The flight model's shoulder bob this sub-step (m along up: the girdle minus its stabilised path), set by
    // AirChain before Step; used when the spec bobs.
    public float ShoulderBob;

    // Set by CreatureFlight each sub-step: landing / launching (the swim fades out, no manoeuvre starts),
    // the aviary, the ground under the flyer (its height along up and the clearance the head keeps over it).
    public bool Calm;
    // Set by CreatureFlight with Calm: the wingbeat's bob fades out (the flare and touchdown, the landing's final).
    // Separate from Calm: the whole landing approach is calm (no swim, no manoeuvre), but the wings beat all the
    // way round it, and a body that stopped heaving there read as no bob at all (user, 2026-10-19).
    public bool BobCalm;
    public EnclosureArea? Bounds;
    bool floorSet;
    Vector3 floorPoint, floorUp;
    float floorClear;

    public AirTwist(AirTwistSpec spec, int joints, float segment, float sizeScale, float[] cosLimit, float[] sinLimit, int girdleJoint = 0)
    {
        t = spec; n = joints; seg = segment;
        girdle = Mathf.Clamp(girdleJoint, 0, Mathf.Max(0, joints - 1));
        scale = Mathf.Max(1e-3f, sizeScale); T = Mathf.Sqrt(scale);
        this.cosLimit = cosLimit; this.sinLimit = sinLimit;
        // the head's sharpest turn per segment of travel: the neck's tightest bend cone (joints 1..3), 80% of it
        float lim = float.MaxValue;
        for (int i = 1; i < Mathf.Min(4, n); i++) if (cosLimit[i] < 1f) lim = Mathf.Min(lim, Mathf.Acos(Mathf.Clamp(cosLimit[i], -1f, 1f)));
        turnPerSeg = 0.8f * (lim < float.MaxValue ? lim : 0.25f);
        cap = 2 * n + 16;
        pathPos = new Vector3[cap]; pathUp = new Vector3[cap];
        pathBob = new float[cap]; pathStamped = new bool[cap];
        // the base travels about one loop circumference while the head circles (circle speed = LoopSpin x flight)
        capH = t.CanLoop ? Mathf.CeilToInt(1.5f * 2f * Mathf.PI * t.loopRadius * scale / (Mathf.Min(1f, t.LoopSpin) * kSampleSpacing * seg)) + 16 : 0;
        basePos = new Vector3[capH];
    }

    public void Reset()
    {
        seeded = false; newest = -1; count = 0; newestH = -1; countH = 0;
        Current = TwistManoeuvre.None; mTime = 0f; Roll = 0f;
        swimLevel = 0f; bobLevel = 0f; ShoulderBob = 0f; bobSlow = 0f;
    }

    public void SetFloor(Vector3 point, Vector3 upDir, float clearance)
    {
        floorSet = true; floorPoint = point; floorUp = upDir; floorClear = clearance;
    }

    // ------------------------------------------------------------------ manoeuvres

    // How long a manoeuvre takes at this flight speed (s).
    public float Duration(TwistManoeuvre m, float speed)
    {
        switch (m)
        {
            case TwistManoeuvre.Corkscrew: return t.CorkscrewTurns / (t.corkscrewHz / T);
            case TwistManoeuvre.BarrelRoll: return t.BarrelTime * T;
            case TwistManoeuvre.SlowRoll: return t.slowRollTime * T;
            case TwistManoeuvre.Loop: return kLoopSettle * T + (1f + kCatchUp) * LoopCircleTime(speed);
        }
        return 0f;
    }

    // The loop's circle: round at LoopSpin x the flight speed.
    float LoopCircleTime(float speed) => 2f * Mathf.PI * t.loopRadius * scale / (t.LoopSpin * Mathf.Max(speed, 0.5f * T));

    // How far the head's path leaves the flight line upward (m): the room a manoeuvre needs above.
    public float Rise(TwistManoeuvre m)
    {
        switch (m)
        {
            case TwistManoeuvre.Corkscrew: return 2f * t.corkscrewAmplitude * scale;
            case TwistManoeuvre.BarrelRoll: return 2f * t.barrelRadius * scale;
            case TwistManoeuvre.Loop: return 2f * t.loopRadius * scale;
        }
        return 0f;
    }

    public bool Available(TwistManoeuvre m)
    {
        switch (m)
        {
            case TwistManoeuvre.Corkscrew: return t.CanCorkscrew;
            case TwistManoeuvre.BarrelRoll: return t.CanBarrel;
            case TwistManoeuvre.Loop: return t.CanLoop;
            case TwistManoeuvre.SlowRoll: return t.CanSlowRoll;
        }
        return false;
    }

    // Room for a manoeuvre from pos (heading fwd): its rise under the roof and a margin over the ground; the
    // head's excursion (the loop's circle ahead, a helix to the side) inside the safe disc. The flight itself goes
    // on turning (the brain's wall turn keeps the flyer inside); the head's path is clamped as well.
    public bool Fits(TwistManoeuvre m, Vector3 pos, Vector3 fwd, float speed, float heightAboveGround, float turnRadius)
    {
        if (!Available(m) || Busy || Calm) return false;
        float sway = t.swimAmplitude * scale;
        float margin = 0.5f * scale + sway;
        if (heightAboveGround < margin + floorClear) return false;
        if (Bounds.HasValue)
        {
            EnclosureArea a = Bounds.Value;
            float h = Vector3.Dot(pos - a.center, a.up);
            if (a.maxHeight > 0f && h + Rise(m) + margin > a.maxHeight) return false;
            Vector3 f = Vector3.ProjectOnPlane(fwd, a.up);
            f = f.sqrMagnitude > 1e-8f ? f.normalized : Vector3.zero;
            float reach = m == TwistManoeuvre.Loop ? speed * kLoopSettle * T + 0.5f * Rise(m) : 0.5f * Rise(m);
            if (a.PlanarDistance(pos + f * reach) + 0.5f * Rise(m) + margin > a.safeRadius) return false;
        }
        return true;
    }

    // Start a manoeuvre now (false: not available or one is running). side: +1 / -1 picks the corkscrew's and
    // the rolls' direction; fwd and up freeze the loop's plane.
    public bool Begin(TwistManoeuvre m, float speed, Vector3 fwd, Vector3 upDir, float side = 1f)
    {
        if (!Available(m) || Busy) return false;
        Current = m; mTime = 0f; mDuration = Mathf.Max(1e-3f, Duration(m, speed));
        mSign = side < 0f ? -1f : 1f;
        mSpeed = Mathf.Max(speed, 0.5f * T);
        loopU = upDir.sqrMagnitude > 1e-8f ? upDir.normalized : Vector3.up;
        loopF = Vector3.ProjectOnPlane(fwd, loopU);
        loopF = loopF.sqrMagnitude > 1e-8f ? loopF.normalized : Vector3.forward;
        loopPhase = 0; loopArc = 0f;
        Manoeuvres++;
        return true;
    }

    // ------------------------------------------------------------------ the sub-step

    // One sub-step of the chain. basePoint: where the head would be on the plain flight line (the stabilised
    // girdle plus the neck); fwd / upDir: the flight's heading and world up; bankedUp: the up the body rights
    // itself onto. Writes the joints and the chain's frames (forward toward the head, up).
    public void Step(float h, Vector3[] joints, Vector3 basePoint, Vector3 fwd, Vector3 upDir, Vector3 bankedUp, float speed,
                     Vector3[] forward, Vector3[] ups)
    {
        if (!(h > 0f)) return;
        Vector3 U = Norm(upDir, Vector3.up);
        worldUp = U;
        if (!seeded) Seed(joints, U, bankedUp);
        Vector3 F = Norm(fwd - U * Vector3.Dot(fwd, U), tangent);
        Vector3 R = Norm(Vector3.Cross(U, F), Vector3.right);

        // the base's recent path (the loop's catch-up runs along it)
        if (capH > 0)
        {
            if (countH == 0 || (basePoint - basePos[newestH]).sqrMagnitude >= kSampleSpacing * kSampleSpacing * seg * seg)
            {
                newestH = (newestH + 1) % capH; basePos[newestH] = basePoint;
                if (countH < capH) countH++;
            }
            if (Current == TwistManoeuvre.Loop && loopPhase == 1) loopArc += (basePoint - lastBase).magnitude;
            lastBase = basePoint;
        }

        // the always-on swim: a 3D S-curve whose plane turns slowly, banking into each curve (faded out for a loop)
        bool looping = Current == TwistManoeuvre.Loop && loopPhase < 2;
        swimLevel = Mathf.MoveTowards(swimLevel, Calm || !t.Swims || looping ? 0f : 1f, h / ((looping ? kLoopSwimFade : kSwimFade) * T));
        float roll = 0f;
        Vector3 off = Vector3.zero;
        if (t.Swims)
        {
            float dphi = 2f * Mathf.PI * t.swimHz / T * h;
            swimPhase = Wrap(swimPhase + dphi);
            swimPhase2 = Wrap(swimPhase2 + kVerticalPhase * dphi);
            float a = swimLevel * t.swimAmplitude * scale;
            float s1 = Mathf.Sin(swimPhase);
            off = R * (a * s1) + U * (a * t.swimVertical * Mathf.Sin(swimPhase2));
            roll = swimLevel * t.swimRollDeg * Mathf.Deg2Rad * s1;
        }

        // the manoeuvre
        bool free = false;
        if (Current != TwistManoeuvre.None)
        {
            mTime += h;
            float p = mTime / mDuration;
            if (p >= 1f) { Current = TwistManoeuvre.None; mTime = 0f; }
            else
            {
                switch (Current)
                {
                    case TwistManoeuvre.Corkscrew:
                    {
                        float phi = 2f * Mathf.PI * t.CorkscrewTurns * p;
                        float a = t.corkscrewAmplitude * scale * Envelope(p);
                        off += R * (mSign * a * Mathf.Sin(phi)) + U * (a * (1f - Mathf.Cos(phi)));
                        roll += mSign * 2f * Mathf.PI * t.corkscrewRolls * Smoother(p);
                        break;
                    }
                    case TwistManoeuvre.BarrelRoll:
                    {
                        // one helix turn round an axis above the flight line, the back always toward the axis
                        float phi = 2f * Mathf.PI * Smoother(p);
                        float a = t.barrelRadius * scale;
                        off += R * (mSign * a * Mathf.Sin(phi)) + U * (a * (1f - Mathf.Cos(phi)));
                        roll += mSign * phi;
                        break;
                    }
                    case TwistManoeuvre.SlowRoll:
                        roll += mSign * 2f * Mathf.PI * Smoother(p);
                        break;
                    case TwistManoeuvre.Loop:
                        free = LoopStep(basePoint, ref off, out bool fixedPoint);
                        if (fixedPoint) { basePoint = off; off = Vector3.zero; }
                        break;
                }
            }
        }

        Vector3 next = Limit(basePoint + off);
        Vector3 d = next - lead;
        // The head turns no sharper than the body can follow (the neck's bend cone per segment of travel): a
        // sudden goal (the launch's climb, a swim at full amplitude) becomes a curve the joints can trace, never a
        // corner. It pursues its goal (which it rejoins on the next steps), and the aviary still holds it.
        float dl = d.magnitude;
        if (dl > 1e-6f * seg)
        {
            Vector3 want = d / dl;
            // and no faster than kMaxPathSpeed x the flight (a far goal is caught up with, not jumped to)
            float maxStep = kMaxPathSpeed * Mathf.Max(speed, 0.5f * T) * h;
            if (dl > maxStep) dl = maxStep;
            float maxTurn = turnPerSeg * dl / seg, cosT = Vector3.Dot(tangent, want);
            next = Limit(lead + (cosT < Mathf.Cos(maxTurn) ? Vector3.RotateTowards(tangent, want, maxTurn, 0f) : want) * dl);
            d = next - lead;
        }
        Vector3 tn = d.sqrMagnitude > 1e-12f * seg * seg ? d.normalized : tangent;
        // parallel transport of the up onto the new direction, then (not round a loop) right it toward the banked up
        up = Ortho(up, tn, U);
        if (!free)
        {
            Vector3 want = Ortho(bankedUp, tn, up);
            float ang = Mathf.Atan2(Vector3.Dot(tn, Vector3.Cross(up, want)), Vector3.Dot(up, want));
            float lim = t.UprightRateDeg * Mathf.Deg2Rad / T * h;
            up = Ortho(Rotate(up, tn, Mathf.Clamp(ang, -lim, lim)), tn, up);
        }
        Roll = roll;
        // the bob fades in after the launch and out for the landing, like the swim, and quickly out for a manoeuvre
        // (a slow roll keeps it: the path stays the straight flight line, only the up turns, and the bob is along world up)
        bool busy = Current != TwistManoeuvre.None && Current != TwistManoeuvre.SlowRoll;
        bobLevel = Mathf.MoveTowards(bobLevel, BobCalm || !t.Bobs || busy ? 0f : 1f, h / ((busy ? kLoopSwimFade : kSwimFade) * T));
        if (t.bobHighPass > 0f) bobSlow += (ShoulderBob - bobSlow) * Mathf.Min(1f, h / (t.bobHighPass * T));
        leadUp = Ortho(Rotate(up, tn, roll), tn, up);
        lead = next; tangent = tn;
        if ((lead - pathPos[newest]).sqrMagnitude >= kSampleSpacing * kSampleSpacing * seg * seg) Push(lead, leadUp);
        Place(joints, forward, ups);
    }

    // A round vertical loop standing still in the air. Settle: the swim fades out (a straight run in). Circle: the
    // head goes round a fixed vertical circle in the plane of its heading at LoopSpin x the flight speed, upside
    // down over the top (the up is transported freely round it and comes back level) while the flight model's
    // point flies on (and may turn). Catch-up: the head runs along the base's own recent path, its lag shrinking
    // smoothly to zero (a surge), so it rejoins the flight line without cutting a corner. True while the up is
    // free (the circle). fixedPoint: off holds the head's position itself, not an offset from the base.
    bool LoopStep(Vector3 basePoint, ref Vector3 off, out bool fixedPoint)
    {
        fixedPoint = false;
        float settle = kLoopSettle * T, d1 = LoopCircleTime(mSpeed);
        float r = t.loopRadius * scale;
        if (loopPhase == 0)
        {
            if (mTime < settle) return false;
            loopPhase = 1; loopArc = 0f;
            // the circle leaves along the head's own path (no corner), in the vertical plane through it
            loopC = lead;
            Vector3 side = Vector3.Cross(loopU, tangent);
            if (side.sqrMagnitude > 1e-6f)
            {
                loopF = tangent;
                loopU = Vector3.Cross(tangent, side.normalized).normalized;
            }
        }
        float tc = mTime - settle;
        if (loopPhase == 1 && tc < d1)
        {
            float th = 2f * Mathf.PI * tc / d1;
            off = loopC + loopF * (r * Mathf.Sin(th)) + loopU * (r * (1f - Mathf.Cos(th)));
            fixedPoint = true;
            return true;
        }
        if (loopPhase == 1)
        {
            loopPhase = 2;
            loopLag = loopArc;
            loopDelta = loopC - BaseBehind(basePoint, loopLag);
            off = Vector3.zero;   // the swim is out until here
        }
        float q = Mathf.Clamp01((tc - d1) / (kCatchUp * d1));
        float k = 1f - Smoother(q);
        off = BaseBehind(basePoint, loopLag * k) + loopDelta * k + off;   // the swim fades back in
        fixedPoint = true;
        return false;
    }

    // The point `back` metres behind the base along its recent path (straight on past the oldest sample).
    Vector3 BaseBehind(Vector3 basePoint, float back)
    {
        Vector3 a = basePoint;
        float acc = 0f;
        for (int k = 0; k < countH; k++)
        {
            Vector3 b = basePos[(newestH - k + capH) % capH];
            float len = (b - a).magnitude;
            if (len > 1e-9f && acc + len >= back) return a + (b - a) * ((back - acc) / len);
            acc += len; a = b;
        }
        Vector3 dir = countH > 1 ? Norm(basePos[(newestH - countH + 1 + capH) % capH] - basePos[(newestH - countH + 2 + capH) % capH], -tangent) : -tangent;
        return a + dir * Mathf.Max(0f, back - acc);
    }

    // The path starts as the body as it is (the ground body at launch), tail first.
    void Seed(Vector3[] joints, Vector3 U, Vector3 bankedUp)
    {
        newest = -1; count = 0;
        for (int i = n - 1; i >= 1; i--)
        {
            Vector3 f = Norm(joints[i - 1] - joints[i], Vector3.forward);
            Push(joints[i], Ortho(U, f, Vector3.up));
            pathStamped[newest] = true;   // the seeded body has not bobbed
        }
        lead = joints[0];
        tangent = Norm(joints[0] - joints[n > 1 ? 1 : 0], Vector3.forward);
        up = Ortho(bankedUp, tangent, U);
        leadUp = up;
        seeded = true;
    }

    void Push(Vector3 p, Vector3 u)
    {
        newest = (newest + 1) % cap;
        pathPos[newest] = p; pathUp[newest] = u;
        pathBob[newest] = 0f; pathStamped[newest] = false;
        if (count < cap) count++;
    }

    // Every joint at its arc length behind the head along the path (exact segment lengths, bend cones),
    // its up the path's up there made perpendicular to the joint's forward.
    void Place(Vector3[] joints, Vector3[] forward, Vector3[] ups)
    {
        // (a bobbing spec keeps placing the bob after it has faded to 0: the bob stamped into the path behind the girdle
        // then runs out along the body instead of vanishing in one tick - a frame jump of ~20 deg at a big bob)
        bool bobs = bobLevel > 0f || t.Bobs;
        float bob = bobs ? bobLevel * t.girdleBob * (t.bobHighPass > 0f ? ShoulderBob - bobSlow : ShoulderBob) : 0f;
        joints[0] = bobs ? lead + worldUp * (bob * BobShare(0)) : lead;
        Vector3 a = lead, ua = leadUp;
        float ba = bob;                              // the bob a sample carries (ahead of the girdle: its bob now)
        int k = 0;                                   // path samples used so far (0 = the newest is next)
        Vector3 b = count > 0 ? pathPos[newest] : lead - tangent * seg, ub = count > 0 ? pathUp[newest] : leadUp;
        float bb = count > 0 ? SampleBob(newest, bob) : bob;
        float acc = 0f, len = (b - a).magnitude;
        Vector3 prevDir = -tangent;
        for (int i = 1; i < n; i++)
        {
            float want = i * seg;
            while (acc + len < want && k + 1 < count)
            {
                acc += len; a = b; ua = ub; ba = bb; k++;
                int idx = (newest - k + cap) % cap;
                b = pathPos[idx]; ub = pathUp[idx]; bb = SampleBob(idx, bob);
                len = (b - a).magnitude;
            }
            Vector3 q, uq;
            float bq;
            if (len > 1e-9f && acc + len >= want)
            {
                float f = (want - acc) / len;
                q = a + (b - a) * f; uq = ua + (ub - ua) * f; bq = ba + (bb - ba) * f;
            }
            else
            {
                // past the oldest sample (just after a seed): straight on along the last stretch
                Vector3 dir = len > 1e-9f ? (b - a) / len : prevDir;
                q = b + dir * (want - acc - len); uq = ub; bq = bb;
            }
            if (bobs)
            {
                // ahead of the girdle: its bob now, blended to the head's share; behind it: the bob stamped into the
                // path where the girdle passed (the girdle stamps the samples it has just passed)
                if (i == girdle && count > 0) { Stamp((newest - k + cap) % cap, bob); bb = SampleBob((newest - k + cap) % cap, bob); }
                float bi = i <= girdle ? bob : t.bobInPhase > 0f ? bq + (bob - bq) * t.bobInPhase : bq;

                q += worldUp * (bi * BobShare(i));
            }
            Vector3 d = Norm(q - joints[i - 1], prevDir);
            d = Cone(prevDir, d, cosLimit[i - 1], sinLimit[i - 1]);
            joints[i] = joints[i - 1] + d * seg;
            if (floorSet)
            {
                // never below the ground (a cone-limited joint can swing past a sharp turn of the path, as at the
                // launch where the climbing head leaves the grounded body): lifted, the segment length kept
                float hh = Vector3.Dot(joints[i] - floorPoint, floorUp), lo = kJointFloor * floorClear;
                if (hh < lo)
                {
                    d = Norm(joints[i] + floorUp * (lo - hh) - joints[i - 1], d);
                    joints[i] = joints[i - 1] + d * seg;
                }
            }
            prevDir = d;
            forward[i] = -d;
            ups[i] = Ortho(uq, forward[i], ups[i - 1]);
            if (i == 1)
            {
                forward[0] = -d;
                ups[0] = Ortho(leadUp, forward[0], ups[1]);
            }
        }
        if (n == 1) { forward[0] = tangent; ups[0] = leadUp; }
    }

    // Share of the girdle's bob at joint i: bobHead at the head .. 1 at the girdle .. bobTail at the tail.
    float BobShare(int i)
    {
        if (i <= girdle) return girdle > 0 ? Mathf.Lerp(t.bobHead, 1f, i / (float)girdle) : 1f;
        return n - 1 > girdle ? Mathf.Lerp(1f, t.bobTail, (i - girdle) / (float)(n - 1 - girdle)) : 1f;
    }

    // A sample's bob: stamped when the girdle passed it, else (still ahead of the girdle) the girdle's now.
    float SampleBob(int idx, float now) => pathStamped[idx] ? pathBob[idx] : now;

    // The girdle has passed sample idx and everything older that is not stamped yet (a fast stretch may pass several
    // in one sub-step): they carry the bob it has now.
    void Stamp(int idx, float bob)
    {
        for (int c = 0; c < count && !pathStamped[idx]; c++)
        {
            pathBob[idx] = bob; pathStamped[idx] = true;
            idx = (idx - 1 + cap) % cap;
        }
    }

    // The head's path kept inside the aviary's safe cylinder and its roof, and above the ground.
    Vector3 Limit(Vector3 p)
    {
        if (Bounds.HasValue)
        {
            EnclosureArea a = Bounds.Value;
            Vector3 d = p - a.center;
            float h = Vector3.Dot(d, a.up);
            Vector3 planar = d - a.up * h;
            float m = planar.magnitude;
            if (m > a.safeRadius && m > 1e-6f) p = a.center + planar * (a.safeRadius / m) + a.up * h;
            if (a.maxHeight > 0f && h > a.maxHeight) p -= a.up * (h - a.maxHeight);
        }
        if (floorSet)
        {
            float hh = Vector3.Dot(p - floorPoint, floorUp);
            if (hh < floorClear) p += floorUp * (floorClear - hh);
        }
        return p;
    }

    // ------------------------------------------------------------------ helpers

    static float Envelope(float p) => SmoothStep(0f, kRampIn, p) * SmoothStep(0f, kRampIn, 1f - p);
    static float SmoothStep(float a, float b, float x) { float u = Mathf.Clamp01((x - a) / (b - a)); return u * u * (3f - 2f * u); }
    static float Smoother(float p) { p = Mathf.Clamp01(p); return p * p * p * (p * (6f * p - 15f) + 10f); }
    static float Wrap(float a) => a > 2f * Mathf.PI ? a - 2f * Mathf.PI : a;

    static Vector3 Rotate(Vector3 v, Vector3 a, float rad) => CreatureWings.Rotate(v, a, rad);

    // d limited to the cone of half-angle acos(c) around the unit axis a (AirChain's rule, with its fix). A path
    // folded back on itself (the head pressed along the aviary wall) gives a nearly antiparallel d: perp is then
    // rounding noise, not perpendicular to a, and a * c + perp * (s / m) was no unit vector - the segment came out
    // longer, the next joint's cone axis with it, and the tail was flung metres out of the aviary for a tick
    // (SkyDragon Wander, Unity seed 1 t=244.4; Flight_Twist_SegmentLengthsExact). Pick a true perpendicular
    // below 1e-4 and renormalise a result that is not unit (a normal one keeps its bits: the flight goldens).
    static Vector3 Cone(Vector3 a, Vector3 d, float c, float s)
    {
        float ca = Vector3.Dot(a, d);
        if (ca >= c) return d;
        Vector3 perp = d - a * ca;
        float m = perp.magnitude;
        if (m < 1e-4f)
        {
            perp = Vector3.Cross(a, Mathf.Abs(a.y) < 0.9f ? Vector3.up : Vector3.right);
            m = perp.magnitude;
        }
        Vector3 r = a * c + perp * (s / m);
        float r2 = r.sqrMagnitude;
        if (Mathf.Abs(r2 - 1f) > 1e-5f) r = r2 > 1e-12f ? r / Mathf.Sqrt(r2) : a;
        return r;
    }

    static Vector3 Ortho(Vector3 u, Vector3 f, Vector3 fallback)
    {
        Vector3 o = u - f * Vector3.Dot(u, f);
        if (o.sqrMagnitude < 1e-10f) o = fallback - f * Vector3.Dot(fallback, f);
        if (o.sqrMagnitude < 1e-10f) o = Vector3.Cross(f, Vector3.right);
        if (o.sqrMagnitude < 1e-10f) o = Vector3.Cross(f, Vector3.forward);
        return o.normalized;
    }

    static Vector3 Norm(Vector3 v, Vector3 fallback)
    {
        float m = v.x * v.x + v.y * v.y + v.z * v.z;
        if (!(m > 1e-12f)) return fallback;
        float inv = 1f / Mathf.Sqrt(m);
        return new Vector3(v.x * inv, v.y * inv, v.z * inv);
    }
}
