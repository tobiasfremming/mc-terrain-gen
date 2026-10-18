using System;
using UnityEngine;

public enum FlightMode { Ground, Launch, Air, Flare, Touchdown }

// The flight side of a CreatureSim for a species that CanFly (design section 3). Owned by the sim; the
// brain drives it through Launch / SetGoals / Land.
//   Ground     the walking code, untouched (CreatureSim.GroundSubStep); the wings fold on the walked arms
//              (wing-walk), unsupported joints of a perched body hang (Drape).
//   Launch     the walking body keeps going while the girdle vaults into the air; the air chain takes the
//              body over joint by joint, head first (AirWeight), the arms hand over from the legs to the
//              wings and unfold, the first beats lift it.
//   Air        FlightModel + CreatureWings + AirChain; the hind legs tuck. Glide is FlightModel.Gliding.
//   Flare      a landing within flareDistance: slow, high-drag braking strokes down to the landing point.
//   Touchdown  the walking state restarts from the body as it is (CreatureSim.ResetGround), the feet are put
//              down, and the hovering pose eases onto the grounded one (tail and hips first); the wings fold.
// Containment: the girdle is kept in the EnclosureArea's safe cylinder (planar safe radius, maxHeight above
// the centre) with the outward velocity removed; a turn-back is the brain's job. The ground below the girdle
// is probed a few times a second; the girdle never sinks below its clearance over it.
// Allocation-free after construction; main thread.
public sealed class CreatureFlight
{
    const float kLaunchTime = 0.45f;     // s at scale 1
    const float kTouchdownTime = 0.5f;
    const float kVault = 2.4f;           // launch: upward girdle speed, m/s at scale 1
    const float kArmHandover = 0.6f;     // fraction of the launch / touchdown over which the arms hand over
    const float kHeadLead = 0.55f;       // launch: the tail takes over this fraction of the launch later than the head
    const int kGroundProbeEvery = 4;     // sub-steps between ground probes below the girdle
    const float kAltitudeWindow = 30f;   // m at scale 1: how far below the girdle the ground is looked for
    const float kTouchMargin = 0.35f;    // m at scale 1 above clearance: the flare's target height
    const float kTouchHeight = 1f;       // m at scale 1 above clearance: touchdown starts from at most this high
    const float kFlareSpeed = 0.55f;     // fraction of minSpeed the flare brakes to
    const float kFlareTimeout = 3f;      // s at scale 1: a flare that has not touched down by then goes around
    const float kTuckReach = 0.8f;       // tucked hind legs: foot at this fraction of the leg's reach
    const float kHangRateDeg = 120f;     // perched drape: deg/s a hanging segment pitches down (at scale 1)
    const float kHangPitchDeg = 80f;     // ... toward this pitch below level (not straight down: see Drape)
    const float kHangDrop = 0.25f;       // m at scale 1: ground this much lower than the support plane leaves a joint hanging

    readonly CreatureSim sim;
    readonly CreatureSpecies sp;
    readonly FlightSpec f;
    readonly IGroundProbe ground;
    readonly Func<Vector3, Vector3> upAt;
    readonly int n, girdle;
    readonly float scale, T;
    readonly bool[] wingLeg;             // per leg: driven by a wing
    readonly Vector3[] air;              // the air chain's joints
    readonly Vector3[] snapshot;         // touchdown: the body when it touched down
    readonly Vector3[] offset;           // touchdown: hovering pose - grounded pose, eased out
    readonly float[] tuck;               // per leg: 0 walked .. 1 tucked
    readonly float[] wristLift;          // per wing: the wing-walk's wrist lift (groundWristLift), held for the blends
    bool offsetValid;

    public readonly FlightModel Model;
    public readonly CreatureWings Wings;
    public readonly AirChain Chain;
    public readonly float[] AirWeight;   // per joint: 0 the walking body .. 1 the air chain
    public FlightMode Mode { get; private set; }
    public float ModeTime { get; private set; }
    public float HeightAboveGround { get; private set; } = float.PositiveInfinity; // girdle, along up
    public Vector3 GroundBelow { get; private set; }
    public bool Landing { get; private set; }
    public Vector3 LandPoint { get; private set; }
    public bool Perched { get; private set; } // on the ground after landing on a non-terrain surface
    public int Launches { get; private set; }
    public int Landings { get; private set; }

    float armBlend;        // 0: arms are legs (wing-walk) .. 1: arms are wings
    float foldFloor;       // the wings fold at least this far (launch unfolding, touchdown folding)
    int landSource;
    int probeCountdown;
    float touchSpeed;

    public CreatureFlight(CreatureSim sim, IGroundProbe ground, Func<Vector3, Vector3> upAt)
    {
        this.sim = sim; this.ground = ground; this.upAt = upAt;
        sp = sim.Species; f = sp.flight;
        n = sp.JointCount; scale = sim.SizeScale; T = sim.TimeScale;
        girdle = sp.legs[sp.wings[0].leg].attachJoint;
        wingLeg = new bool[sp.legs.Length];
        for (int w = 0; w < sp.WingCount; w++) wingLeg[sp.wings[w].leg] = true;
        air = new Vector3[n]; snapshot = new Vector3[n]; offset = new Vector3[n];
        tuck = new float[sp.legs.Length];
        wristLift = new float[sp.WingCount];
        AirWeight = new float[n];
        Vector3 fwd = sim.Rotations[0] * Vector3.forward;
        Model = new FlightModel(f, scale, sim.Joints[girdle], Vector3.zero, fwd);
        Wings = new CreatureWings(sp, sp.wings, sim.Joints, sim.Rotations, scale);
        Wings.ResetStroke(1f);
        Chain = new AirChain(sp, f, girdle, scale);
        Mode = FlightMode.Ground;
        foldFloor = 1f;
    }

    public bool Airborne => Mode == FlightMode.Air || Mode == FlightMode.Flare;
    public int Girdle => girdle;

    // ------------------------------------------------------------------ commands (the brain)

    // From the ground: vault into the air and fly toward the goals. False when not on the ground.
    public bool Launch(float altitudeGoal)
    {
        if (Mode != FlightMode.Ground) return false;
        Vector3 up = Up(sim.Joints[girdle]);
        Vector3 heading = Vector3.ProjectOnPlane(sim.Rotations[0] * Vector3.forward, up);
        heading = heading.sqrMagnitude > 1e-8f ? heading.normalized : Vector3.forward;
        float v = Mathf.Max(sim.Speed, f.launchSpeed * T);
        Model.Reset(sim.Joints[girdle], heading * v + up * (kVault * T), heading, up);
        Model.AltitudeGoal = altitudeGoal;
        Model.SpeedGoal = f.cruiseSpeed * T;
        Model.TurnRateGoal = 0f;
        Chain.Reset(Model.Position, Model.Velocity);
        Array.Copy(sim.Joints, air, n);
        for (int i = 0; i < n; i++) AirWeight[i] = 0f;
        Wings.ResetStroke(1f);
        armBlend = 0f; foldFloor = 1f;
        Landing = false; Perched = false;
        probeCountdown = 0;
        Mode = FlightMode.Launch; ModeTime = 0f;
        Launches++;
        return true;
    }

    // Altitude (world, along up), speed (m/s) and turn rate (rad/s, + = right) to fly at. Ignored while
    // landing (the landing autopilot owns the goals).
    public void SetGoals(float altitude, float speed, float turnRate)
    {
        if (Landing) return;
        Model.AltitudeGoal = altitude; Model.SpeedGoal = speed; Model.TurnRateGoal = turnRate;
    }

    // Steer toward a point (pure pursuit within the coordinated turn's limit) at an altitude and speed.
    public void SteerTo(Vector3 point, float altitude, float speed) => SetGoals(altitude, speed, TurnToward(point));

    // The tightest level turn's radius at the current speed (bank at maxBankDeg).
    public float TurnRadius
    {
        get
        {
            float v = Mathf.Max(Model.HorizontalSpeed, f.minSpeed * T);
            return v * v / (FlightModel.G * Mathf.Tan(f.maxBankDeg * Mathf.Deg2Rad));
        }
    }

    // Containment by prediction: the tightest turn toward the enclosure's middle, from here, must still fit
    // inside the safe disc (its circle's far side within safeRadius - kWallSlack). Once it barely does, turn
    // that way at the coordinated limit (true, turnRate set).
    public bool WallTurn(out float turnRate)
    {
        turnRate = 0f;
        if (!sim.Bounds.HasValue) return false;
        EnclosureArea a = sim.Bounds.Value;
        Vector3 up = Model.Up;
        float R = 1.7f * TurnRadius; // the physical turn (strokes, roll lag) is wider than the ideal one
        Vector3 right = Vector3.Cross(up, Model.Forward);
        float v = Mathf.Max(Model.HorizontalSpeed, f.minSpeed * T);
        float maxBank = f.maxBankDeg * Mathf.Deg2Rad;
        // the side whose circle lies nearer the middle, and the straight run before the bank is in: rolling
        // from the current bank to the full bank on that side takes time
        Vector3 p0 = Model.Position;
        float side = a.PlanarDistance(p0 + right * R) <= a.PlanarDistance(p0 - right * R) ? 1f : -1f;
        float rollIn = kRollLead * T + Mathf.Abs(side * maxBank - Model.Bank) / (kRollRate * Mathf.Deg2Rad / T);
        Vector3 p = p0 + Model.Forward * (v * rollIn);
        float d = a.PlanarDistance(p + right * (side * R));
        if (d + R < a.safeRadius - kWallSlack * scale) return false;
        turnRate = side * FlightModel.G * Mathf.Tan(maxBank) / v;
        return true;
    }

    public float TurnToward(Vector3 point)
    {
        Vector3 up = Model.Up;
        Vector3 to = Vector3.ProjectOnPlane(point - Model.Position, up);
        if (to.sqrMagnitude < 1e-8f) return 0f;
        float yaw = Vector3.SignedAngle(Model.Forward, to.normalized, up) * Mathf.Deg2Rad;
        float v = Mathf.Max(Model.HorizontalSpeed, f.minSpeed * T);
        float wMax = FlightModel.G * Mathf.Tan(f.maxBankDeg * Mathf.Deg2Rad) / v;
        return Mathf.Clamp(kPursuitGain / T * yaw, -wMax, wMax);
    }

    // Land at point (source: what it is - 0 terrain, else a collider: a perch). From the air the landing
    // autopilot flies to an entry point (a straight final of flareDistance + 2.5 turn radii, kept inside the
    // enclosure), the final on a glide slope, the flare and the touchdown.
    // axis: on a perch (source != 0), the direction its top runs along (a branch): the touchdown lays the
    // body along it with the girdle on the point (the eased touchdown hides the shift). Zero: none.
    public void Land(Vector3 point, int source = 0, Vector3 axis = default)
    {
        Landing = true; LandPoint = point; landSource = source; landAxis = axis;
        approach = Approach.Entry;
        PlanEntry();
    }

    Vector3 landAxis;
    enum Approach { Entry, Final }
    Approach approach;
    Vector3 entryPoint, finalDir;
    public Vector3 EntryPoint => entryPoint;
    public bool OnFinal => Landing && approach == Approach.Final;
    const float kPursuitGain = 0.9f;       // turn rate per radian of heading error (/ sqrt(scale))
    const float kGlideSlope = 0.3f;        // final: altitude above the flare per metre of distance
    const float kApproachHeight = 2f;      // m at scale 1 above the landing point at the entry
    const float kApproachSpeed = 0.85f;    // of cruise on the final
    const float kWallSlack = 0.5f;         // m at scale 1: the predicted turn keeps this far inside the safe radius
    const float kRollLead = 0.2f;          // s at scale 1: before a roll starts
    const float kRollRate = 30f;           // deg/s at scale 1: a typical roll rate (FlightModel's stroke asymmetry; ~30 with the deep, slower beat, was 45)

    // The final's direction: of 12 directions the one closest to the current heading whose entry point lies
    // inside the enclosure's safe disc by a turn radius.
    void PlanEntry()
    {
        Vector3 up = Model.Up;
        float d = f.flareDistance * scale + 2.5f * TurnRadius;
        Vector3 h = Model.Forward;
        Vector3 toL = Vector3.ProjectOnPlane(LandPoint - Model.Position, up);
        toL = toL.sqrMagnitude > 1e-8f ? toL.normalized : h;
        float best = float.MinValue;
        finalDir = h; entryPoint = LandPoint - h * d;
        for (int k = 0; k < 12; k++)
        {
            Vector3 dir = Quaternion.AngleAxis(30f * k, up) * h;
            Vector3 e = LandPoint - dir * d;
            float score = Vector3.Dot(dir, h) + Vector3.Dot(dir, toL);
            if (sim.Bounds.HasValue)
            {
                EnclosureArea a = sim.Bounds.Value;
                if (a.PlanarDistance(e) > a.safeRadius - TurnRadius) score -= 10f;
            }
            if (score > best) { best = score; finalDir = dir; entryPoint = e; }
        }
        entryPoint += up * (Vector3.Dot(LandPoint - entryPoint, up) + kApproachHeight * scale);
    }


    public void CancelLanding()
    {
        Landing = false;
        if (Mode == FlightMode.Flare) Mode = FlightMode.Air;
    }

    // The first surface below `from` along up (any surface the creature can stand on: terrain, a rock, a
    // branch), with its source when the ground knows sources.
    public bool ProbeDown(Vector3 from, out Vector3 point, out Vector3 normal, out int source)
    {
        Vector3 up = Up(from);
        source = 0;
        if (ground is ISurfaceProbe sp2 && sp2.Probe(from, up, kAltitudeWindow * scale, SurfaceHit.NoSource, -1, out SurfaceHit h))
        {
            point = h.point; normal = h.normal; source = h.source;
            return true;
        }
        return ground.Probe(from, up, kAltitudeWindow * scale, out point, out normal);
    }

    // ------------------------------------------------------------------ the sub-step

    // True when this sub-step was handled here (every mode but Ground).
    internal bool SubStep(float dt, Vector3 target)
    {
        GateSurfaces();
        switch (Mode)
        {
            case FlightMode.Ground:
                return false;
            case FlightMode.Launch:
                ModeTime += dt;
                AirStep(dt);
                sim.GroundSubStep(dt, target); // the walking body under the vault; BeforeLegs blends it into the air chain
                if (ModeTime >= kLaunchTime * T * (1f + kHeadLead))
                {
                    Mode = FlightMode.Air; ModeTime = 0f;
                    for (int i = 0; i < n; i++) AirWeight[i] = 1f;
                    armBlend = 1f; foldFloor = 0f;
                }
                return true;
            case FlightMode.Air:
            case FlightMode.Flare:
                ModeTime += dt;
                if (Landing) SteerLanding();
                AirStep(dt);
                Array.Copy(air, sim.Joints, n);
                Chain.WriteRotations(sim.Rotations);
                Array.Copy(Chain.Up, sim.Up, n);
                sim.SetFlightSpeed(Model.Velocity.magnitude);
                if (Landing && ReadyToTouchDown()) BeginTouchdown();
                return true;
            case FlightMode.Touchdown:
                ModeTime += dt;
                sim.GroundSubStep(dt, target);
                if (ModeTime >= kTouchdownTime * T)
                {
                    Mode = FlightMode.Ground; ModeTime = 0f;
                    for (int i = 0; i < n; i++) AirWeight[i] = 0f;
                    armBlend = 0f; foldFloor = 1f;
                    Perched = landSource != 0;
                    Landing = false;
                    Landings++;
                }
                return true;
        }
        return false;
    }

    // On the composite ground (terrain + surfaces) the surface part is asked only while the body is near a
    // registered surface (CompositeSurfaceProbe.SetActiveBounds; a climbing creature's CreatureClimb does this
    // for itself): the joints' box grown by the wing's reach and the ground below, every few sub-steps.
    void GateSurfaces()
    {
        if (!(ground is CompositeSurfaceProbe comp) || --gateCountdown > 0) return;
        gateCountdown = kGateEvery;
        Vector3 lo = sim.Joints[0], hi = lo;
        for (int i = 1; i < n; i++) { lo = Vector3.Min(lo, sim.Joints[i]); hi = Vector3.Max(hi, sim.Joints[i]); }
        var b = new Bounds();
        b.SetMinMax(lo, hi);
        b.Expand(2f * (sp.MaxLateralReach + 0.5f) * scale);
        // the ground below the girdle (the altitude probe and a landing look down)
        Vector3 up = Up(sim.Joints[girdle]);
        b.Encapsulate(sim.Joints[girdle] - up * Mathf.Min(kAltitudeWindow * scale, float.IsInfinity(HeightAboveGround) ? kAltitudeWindow * scale : HeightAboveGround + 2f * scale));
        comp.SetActiveBounds(b);
    }
    int gateCountdown;
    const int kGateEvery = 8;

    // Model, wings and chain one sub-step (into `air`), with containment and the ground floor.
    void AirStep(float dt)
    {
        Model.Up = Up(Model.Position);
        Model.Control(dt);
        Wings.Advance(dt, Model);
        if (foldFloor > 0f)
            for (int w = 0; w < Wings.Count; w++)
            {
                ref WingState st = ref Wings.State[w];
                st.fold = Mathf.Max(st.fold, foldFloor);
                st.area *= 1f - 0.8f * foldFloor; // a folded wing barely grips the air
            }
        Model.Integrate(dt, Wings);
        Contain();
        ProbeGround();
        Chain.Step(dt, air, Model.Position, Model.Velocity, Model.BankedUp, Model.Up);
    }

    void Contain()
    {
        if (!sim.Bounds.HasValue) return;
        EnclosureArea a = sim.Bounds.Value;
        Vector3 p = Model.Position, up = a.up;
        Vector3 d = p - a.center;
        float h = Vector3.Dot(d, up);
        Vector3 planar = d - up * h;
        float m = planar.magnitude;
        if (m > a.safeRadius && m > 1e-6f)
        {
            Vector3 radial = planar / m;
            p = a.center + radial * a.safeRadius + up * h;
            float vr = Vector3.Dot(Model.Velocity, radial);
            if (vr > 0f) Model.Velocity -= radial * vr;
        }
        if (a.maxHeight > 0f && h > a.maxHeight)
        {
            p -= up * (h - a.maxHeight);
            float vu = Vector3.Dot(Model.Velocity, up);
            if (vu > 0f) Model.Velocity -= up * vu;
        }
        Model.Position = p;
    }

    // The ground under the girdle, a few times a second (every sub-step when low), and the floor: the girdle
    // stays at least its clearance above it. The probe starts at the girdle (the first surface below it: a
    // rock or a branch it flies over, not just the terrain) and reaches kAltitudeWindow down.
    void ProbeGround()
    {
        Vector3 p = Model.Position, up = Model.Up;
        float clear = sp.joints[girdle].clearance * scale;
        if (--probeCountdown <= 0 || HeightAboveGround < 2f * clear + kTouchMargin * scale)
        {
            probeCountdown = kGroundProbeEvery;
            if (ground.Probe(p, up, kAltitudeWindow * scale, out Vector3 g, out _) && Vector3.Dot(p - g, up) > -0.5f * clear) GroundBelow = g;
            else if (float.IsInfinity(HeightAboveGround)) GroundBelow = p - up * (kAltitudeWindow * scale);
        }
        HeightAboveGround = Vector3.Dot(p - GroundBelow, up);
        if (HeightAboveGround < clear)
        {
            Model.Position = p + up * (clear - HeightAboveGround);
            float vu = Vector3.Dot(Model.Velocity, up);
            if (vu < 0f) Model.Velocity -= up * vu;
            HeightAboveGround = clear;
        }
    }

    // The landing autopilot: entry point, final (glide slope, approach speed), flare (within flareDistance:
    // braking strokes down to just above the point, turning onto it). Passing the point goes around.
    void SteerLanding()
    {
        Vector3 up = Model.Up;
        Vector3 to = Vector3.ProjectOnPlane(LandPoint - Model.Position, up);
        float dist = to.magnitude;
        float flare = f.flareDistance * scale;
        float clear = sp.joints[girdle].clearance * scale;
        float landH = Vector3.Dot(LandPoint, up);
        if (Mode == FlightMode.Air)
        {
            if (approach == Approach.Entry)
            {
                // Lined up and far enough out: the final starts here, along the line to the point. Too close
                // or not facing it: fly on (away) until there is room for a final, then turn back toward it
                // (a teardrop); near the enclosure's wall turn back at once.
                float yawToL = dist > 1e-3f ? Mathf.Abs(Vector3.SignedAngle(Model.Forward, to / dist, up)) : 180f;
                float R = TurnRadius;
                if (yawToL < 25f && dist > flare + R)
                {
                    approach = Approach.Final; finalDir = to / dist; entryPoint = Model.Position;
                }
                else
                {
                    bool room = dist > flare + 2f * R;
                    Model.AltitudeGoal = landH + kApproachHeight * scale;
                    Model.SpeedGoal = f.cruiseSpeed * T;
                    Model.TurnRateGoal = WallTurn(out float wt) ? wt : room || yawToL < 90f ? TurnToward(LandPoint) : 0f;
                    return;
                }
            }
            Model.AltitudeGoal = landH + clear + kTouchMargin * scale + kGlideSlope * Mathf.Max(0f, dist - flare);
            Model.SpeedGoal = kApproachSpeed * f.cruiseSpeed * T;
            Model.TurnRateGoal = TurnToward(LandPoint);
            // flare once the point is within flareDistance ahead and close to the track; passed it (or wide of
            // it) without flaring: go around
            float aheadD = Vector3.Dot(to, Model.Forward);
            float lateral = (to - Model.Forward * aheadD).magnitude;
            if (aheadD < flare && aheadD > -0.25f * flare && lateral < 0.6f * flare) { Mode = FlightMode.Flare; ModeTime = 0f; }
            else
            {
                if (aheadD < 0.5f * flare) { approach = Approach.Entry; PlanEntry(); }
                return;
            }
        }
        Model.AltitudeGoal = landH + clear + 0.5f * kTouchMargin * scale;
        Model.SpeedGoal = kFlareSpeed * f.minSpeed * T * Mathf.Clamp01(dist / flare);
        Model.TurnRateGoal = TurnToward(LandPoint);
        // flown past the point in the flare without touching down: go around
        // past the point, below it (missed it, e.g. beside a branch), or too long in the flare: go around
        bool below = Vector3.Dot(Model.Position, up) < landH - 0.25f * scale;
        if ((Vector3.Dot(to, Model.Forward) < -flare && ModeTime > 0.5f * T) || (below && ModeTime > 0.5f * T) || ModeTime > kFlareTimeout * T)
        {
            Mode = FlightMode.Air; ModeTime = 0f; approach = Approach.Entry; PlanEntry();
        }
    }

    bool ReadyToTouchDown()
    {
        if (Mode != FlightMode.Flare) return false;
        float clear = sp.joints[girdle].clearance * scale;
        Vector3 to = Vector3.ProjectOnPlane(LandPoint - Model.Position, Model.Up);
        // up to kTouchHeight above its clearance: the touchdown eases the rest of the way down
        bool low = HeightAboveGround <= clear + kTouchHeight * scale;
        bool there = to.magnitude < 0.5f * f.flareDistance * scale || ModeTime > 1.5f * T;
        return low && there;
    }

    void BeginTouchdown()
    {
        Array.Copy(sim.Joints, snapshot, n);
        touchSpeed = Vector3.ProjectOnPlane(Model.Velocity, Model.Up).magnitude;
        if (landSource != 0)
        {
            // a perch (a branch, a rock top): the ground body starts laid along the perch through the point,
            // girdle on it, at the clearances (the joints past its end hang: Drape); the eased touchdown carries
            // the hovering body there
            Vector3 up = Model.Up;
            Vector3 axis = Vector3.ProjectOnPlane(landAxis, up);
            if (axis.sqrMagnitude < 1e-8f) axis = Model.Forward;
            axis.Normalize();
            if (Vector3.Dot(axis, Model.Forward) < 0f) axis = -axis;
            float seg = sp.spacing * scale;
            for (int i = 0; i < n; i++)
                sim.Joints[i] = LandPoint + up * (sp.joints[i].clearance * scale) - axis * (seg * (i - girdle));
            touchSpeed = 0f;
        }
        sim.ResetGround(0.5f * touchSpeed, (kTouchHeight + 1f) * scale);
        offsetValid = false;
        Mode = FlightMode.Touchdown; ModeTime = 0f;
    }

    // ------------------------------------------------------------------ hooks from the walking sub-step

    // After the walking sway, before the legs: launch blends the walking body into the air chain (head first);
    // touchdown eases the hovering pose onto the grounded body (tail and hips first).
    internal void BeforeLegs(float dt)
    {
        Vector3[] J = sim.Joints;
        if (Mode == FlightMode.Launch)
        {
            float t = ModeTime / (kLaunchTime * T);
            for (int i = 0; i < n; i++)
            {
                float lag = kHeadLead * i / Mathf.Max(1, n - 1);
                float w = Smooth01(t - lag);
                AirWeight[i] = w;
                J[i] = Vector3.Lerp(J[i], air[i], w);
            }
            armBlend = Smooth01(t / kArmHandover);
            foldFloor = 1f - Smooth01(t / (1f + kHeadLead));
            Relength(J);
            UpFrames(J);
        }
        else if (Mode == FlightMode.Touchdown)
        {
            if (!offsetValid)
            {
                for (int i = 0; i < n; i++) offset[i] = snapshot[i] - J[i];
                offsetValid = true;
            }
            float t = ModeTime / (kTouchdownTime * T);
            for (int i = 0; i < n; i++)
            {
                // the hips and tail settle first, the head last
                float lag = 0.4f * (1f - i / Mathf.Max(1f, n - 1f));
                float g = Smooth01((t - lag) / 0.6f);
                AirWeight[i] = 1f - g;
                J[i] += offset[i] * (1f - g);
            }
            armBlend = 1f - Smooth01(t / kArmHandover);
            foldFloor = Smooth01(t);
            Relength(J);
            sim.RefreshFrames();
        }
    }

    // Perched (or any flyer on the ground): joints past the edge of what they stand on hang - a tail over a
    // branch or a rock's edge - instead of being pulled straight down onto the ground far below (a fold at
    // the edge). A joint hangs when its ground lies more than kHangDrop below the last supported joint's
    // ground (or its probe found none). The hanging run stays in the vertical plane of the last supported
    // segment (a nearly vertical segment has no reliable direction along the ground; the walking chain step
    // would kink it) and pitches down toward kHangPitchDeg, each segment at most its joint's bend limit
    // steeper than the one before; where it reaches the ground below it bends back to level within the same
    // limits and the rest lies on the ground. True when it moved a joint.
    internal bool Drape(float dt, Vector3[] path, Vector3[] pathUp, Vector3[] groundPoint, float seg)
    {
        if (Mode != FlightMode.Ground && Mode != FlightMode.Touchdown) return false;
        Vector3[] probed = sim.ProbedGround;
        bool[] sup = sim.Supported;
        if (probed == null) return false;
        float drop = kHangDrop * scale, rate = kHangRateDeg / T * dt;
        // the first joint (behind the girdle) whose ground falls away: the support's edge
        int a = -1;
        for (int i = Mathf.Max(2, girdle); i < n; i++)
        {
            Vector3 u = pathUp[i];
            float support = Vector3.Dot(groundPoint[i - 1], u);
            if (!sup[i] || Vector3.Dot(probed[i], u) < support - drop) { a = i; break; }
        }
        if (a < 0) return false;
        Vector3 upF = pathUp[a];
        Vector3 hb = Vector3.ProjectOnPlane(path[a - 1] - path[a - 2], upF);
        if (hb.sqrMagnitude < 1e-10f) hb = Vector3.ProjectOnPlane(path[a] - path[a - 1], upF);
        if (hb.sqrMagnitude < 1e-10f) return false;
        hb.Normalize();
        Vector3 down = -upF;
        float pitch0 = Mathf.Asin(Mathf.Clamp(Vector3.Dot((path[a - 1] - path[a - 2]).normalized, down), -1f, 1f)) * Mathf.Rad2Deg;
        // pass 1: where the hanging run reaches the ground below (b), pass 2: lay it, easing back to level there
        int b = n;
        for (int pass = 0; pass < 2; pass++)
        {
            float prevPitch = pitch0;
            Vector3 prev = path[a - 1];
            for (int i = a; i < (pass == 0 ? n : b); i++)
            {
                Vector3 d = path[i] - path[i - 1];
                float pitch = d.sqrMagnitude > 1e-12f ? Mathf.Asin(Mathf.Clamp(Vector3.Dot(d.normalized, down), -1f, 1f)) * Mathf.Rad2Deg : prevPitch;
                pitch = Mathf.MoveTowards(pitch, kHangPitchDeg, rate);
                float lim = sp.joints[i - 1].rigid ? 0f : sp.joints[i - 1].bendLimitDeg;
                pitch = Mathf.Clamp(pitch, prevPitch - lim, prevPitch + lim);
                if (pass == 1 && b < n) pitch = Mathf.Min(pitch, lim * (b - i));
                float pr = pitch * Mathf.Deg2Rad;
                Vector3 p = prev + (hb * Mathf.Cos(pr) + down * Mathf.Sin(pr)) * seg;
                float clear = sp.joints[i].clearance * scale;
                if (pass == 0)
                {
                    // reached its own ground: from here on the body lies on the ground (the walking code)
                    if (sup[i] && Vector3.Dot(p - probed[i], upF) <= clear) { b = i; break; }
                }
                else
                {
                    path[i] = p;
                    groundPoint[i] = p - pathUp[i] * clear;
                }
                prev = p; prevPitch = pitch;
            }
        }
        return true;
    }

    // ------------------------------------------------------------------ bones

    // After the legs' bones: the wings (arms handed between legs and wings), tucked hind legs in the air.
    internal void WriteBones(Matrix4x4[] dst, int offsetIndex)
    {
        LegState[] legs = sim.Legs;
        LegSpec[] specs = sp.legs;
        // wings
        for (int w = 0; w < Wings.Count; w++)
        {
            int l = sp.wings[w].leg;
            ref LegState ls = ref legs[l];
            Vector3 target = Wings.FlightWristTarget(w, out Vector3 shoulder);
            Vector3 legFoot = ls.foot;
            Vector3 legWrist = legFoot;
            if (armBlend > 0f && wristLift[w] > 0f) // launching / touching down: from the raised wing-walk wrist
                legWrist += (ls.footNormal.sqrMagnitude > 0.5f ? ls.footNormal : sim.Rotations[specs[l].attachJoint] * Vector3.up) * wristLift[w];
            Vector3 wristTarget = armBlend >= 1f ? target : armBlend <= 0f ? legFoot : Vector3.Lerp(legWrist, target, armBlend);
            Vector3 sh = armBlend <= 0f ? ls.shoulder : shoulder;
            LegSpec arm = specs[l];
            Vector3 elbow = TwoBoneIK.Solve(sh, wristTarget, arm.upperLength * scale, arm.lowerLength * scale, Wings.ArmPole(w), out Vector3 wrist);
            float fold = Mathf.Max(foldFloor, Wings.State[w].fold);
            if (armBlend <= 0f) { elbow = ls.elbow; wrist = ls.foot; fold = 1f; }
            Wings.PoseBetween(w, sh, elbow, wrist, fold, armBlend);
            if (armBlend <= 0f && sp.wings[w].groundWristLift > 0f)
            {
                // standing on the thumb claws (WingSpec.groundWristLift): the wrist raised off the foot point until
                // the thumb's tip is on it, at most groundWristLift (two passes: the hand frame follows the arm)
                Vector3 fu = ls.footNormal.sqrMagnitude > 0.5f ? ls.footNormal : sim.Rotations[arm.attachJoint] * Vector3.up;
                float hMax = sp.wings[w].groundWristLift * scale, h = hMax;
                for (int pass = 0; pass < 2; pass++)
                {
                    elbow = TwoBoneIK.Solve(sh, ls.foot + fu * h, arm.upperLength * scale, arm.lowerLength * scale, Wings.ArmPole(w), out wrist);
                    Wings.PoseBetween(w, sh, elbow, wrist, fold, 0f);
                    h = Mathf.Clamp(h - Vector3.Dot(Wings.ThumbTip(w) - ls.foot, fu), 0f, hMax);
                }
                elbow = TwoBoneIK.Solve(sh, ls.foot + fu * h, arm.upperLength * scale, arm.lowerLength * scale, Wings.ArmPole(w), out wrist);
                Wings.PoseBetween(w, sh, elbow, wrist, fold, 0f);
                wristLift[w] = h;
            }
            // the arm as the renderer's bounds and the debug view see it (on the ground the foot stays the gait's)
            ls.shoulder = sh; ls.elbow = elbow;
            if (armBlend > 0f) ls.foot = wrist;
        }
        Wings.WriteBones(dst, offsetIndex);

        // hind legs: tucked back under the belly in the air, blended by the air weight at their joint
        Vector3 sc = Vector3.one * scale;
        for (int l = 0; l < specs.Length; l++)
        {
            if (wingLeg[l]) continue;
            float w = Mode == FlightMode.Ground ? 0f : AirWeight[specs[l].attachJoint];
            if (w <= 0f) continue;
            LegSpec s = specs[l];
            ref LegState ls = ref legs[l];
            Quaternion ar = sim.Rotations[s.attachJoint];
            Vector3 so = s.shoulderOffset; so.x *= s.side;
            Vector3 shoulder = sim.Joints[s.attachJoint] + ar * (so * scale);
            float reach = (s.upperLength + s.lowerLength) * scale * kTuckReach;
            Vector3 tuckDir = ar * new Vector3(0.25f * s.side, -0.35f, -1f).normalized;
            Vector3 foot = Vector3.Lerp(ls.foot, shoulder + tuckDir * reach, w);
            Vector3 pole = s.pole; pole.x *= s.side;
            pole = ar * pole;
            Vector3 elbow = TwoBoneIK.Solve(shoulder, foot, s.upperLength * scale, s.lowerLength * scale, pole, out Vector3 ft);
            Quaternion upper = CreatureWings.BoneRot(elbow - shoulder, pole, ar);
            dst[offsetIndex + sp.LegBone(l, 0)] = Matrix4x4.TRS(shoulder, upper, sc);
            dst[offsetIndex + sp.LegBone(l, 1)] = Matrix4x4.TRS(elbow, CreatureWings.LowerRot(upper, ft - elbow), sc);
            Vector3 toe = ft - elbow;
            Vector3 fn = Vector3.Slerp(ls.footNormal.sqrMagnitude > 0.5f ? ls.footNormal : ar * Vector3.up, ar * Vector3.back, w);
            Vector3 ff = Vector3.ProjectOnPlane(toe.sqrMagnitude > 1e-12f ? toe : ar * Vector3.back, fn);
            if (ff.sqrMagnitude < 1e-8f) ff = Vector3.ProjectOnPlane(ar * Vector3.forward, fn);
            dst[offsetIndex + sp.LegBone(l, 2)] = Matrix4x4.TRS(ft, Quaternion.LookRotation(ff.normalized, fn.normalized), sc);
            if (Mode != FlightMode.Touchdown) { ls.shoulder = shoulder; ls.elbow = elbow; ls.foot = ft; }
        }
    }

    // ------------------------------------------------------------------ helpers

    Vector3 Up(Vector3 p)
    {
        Vector3 u = upAt(p);
        return u.sqrMagnitude > 1e-12f ? u.normalized : Vector3.up;
    }

    static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    // Exact segment lengths from the girdle out both ways (the blend of two chains is not exact).
    void Relength(Vector3[] J)
    {
        float seg = sp.spacing * scale;
        for (int i = girdle + 1; i < n; i++)
        {
            Vector3 d = J[i] - J[i - 1];
            float m = d.magnitude;
            J[i] = J[i - 1] + (m > 1e-9f ? d / m : -Model.Forward) * seg;
        }
        for (int i = girdle - 1; i >= 0; i--)
        {
            Vector3 d = J[i] - J[i + 1];
            float m = d.magnitude;
            J[i] = J[i + 1] + (m > 1e-9f ? d / m : Model.Forward) * seg;
        }
    }

    // Launch frames: the chain's frame rule on the blended body, up between the ground's and the banked one.
    void UpFrames(Vector3[] J)
    {
        float w = AirWeight[girdle];
        Vector3 gUp = Up(J[girdle]);
        Vector3 u = Vector3.Slerp(gUp, Model.BankedUp, w);
        Chain.UpdateFrames(J, u);
        Chain.WriteRotations(sim.Rotations);
    }
}
