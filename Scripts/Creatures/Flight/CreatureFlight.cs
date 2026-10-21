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
public sealed class CreatureFlight : CreatureKit.ILocomotionMode
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
    const float kHangRateDeg = 120f;     // perched drape: deg/s a hanging segment pitches down (at scale 1)
    const float kHangPitchDeg = 80f;     // ... toward this pitch below level (not straight down: see Drape)
    const float kDrapeMinPlanar = 0.2f;  // FlightSpec.drapeOwnHeading: the segment before's heading weighs as this share of the spacing against a hanging segment's own run across up
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
    public readonly FlightAvoidance Avoidance; // obstacles ahead (FlightSpec.avoidLookahead > 0; else inert)
    readonly ISurfaceProbe obstacles;         // what the avoidance casts against: the surfaces (bark, rocks), not the terrain
    public readonly float[] AirWeight;   // per joint: 0 the walking body .. 1 the air chain
    public FlightMode Mode { get; private set; }
    public float ModeTime { get; private set; }
    public float HeightAboveGround { get; private set; } = float.PositiveInfinity; // girdle, along up
    public Vector3 GroundBelow { get; private set; }
    public bool Landing { get; private set; }
    public Vector3 LandPoint { get; private set; }
    public bool Perched { get; private set; } // on the ground after landing on a non-terrain surface
    public int Launches { get; private set; }
    // A behaviour's fold of the wings in the air, 0..1 (a stoop: folded wings lose their lift and grip and the body falls
    // along its path); 0 = none. Cleared on landing and launch.
    public float DiveFold;
    // FlightSpec.launchCrouch: a quad launch's crouch before the vault is running (the body lowered on its four limbs)
    public bool Crouching => crouchLeft > 0f;
    float crouchLeft, crouchAlt;
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
        Avoidance = new FlightAvoidance(f, scale, FlightAvoidance.HalfSpan(sp));
        obstacles = ground is CompositeSurfaceProbe comp ? comp.Surfaces : ground as ISurfaceProbe;
        Mode = FlightMode.Ground;
        foldFloor = 1f;
    }

    public bool Airborne => Mode == FlightMode.Air || Mode == FlightMode.Flare;
    // A twisting flyer's manoeuvres (FlightSpec.twist, the dragon); null for every other flyer.
    public AirTwist Twist => Chain.Twist;
    public int Girdle => girdle;

    // ------------------------------------------------------------------ commands (the brain)

    // From the ground: vault into the air and fly toward the goals. False when not on the ground.
    public bool Launch(float altitudeGoal)
    {
        if (Mode != FlightMode.Ground) return false;
        // a quad launch (FlightSpec.launchCrouch > 0): crouch on the four limbs first, the vault follows (SubStep)
        if (f.launchCrouch > 0f)
        {
            if (crouchLeft > 0f) return true;
            crouchLeft = f.launchCrouch * T; crouchAlt = altitudeGoal;
            return true;
        }
        return Vault(altitudeGoal);
    }

    bool Vault(float altitudeGoal)
    {
        Vector3 up = Up(sim.Joints[girdle]);
        Vector3 heading = Vector3.ProjectOnPlane(sim.Rotations[0] * Vector3.forward, up);
        heading = heading.sqrMagnitude > 1e-8f ? heading.normalized : Vector3.forward;
        float v = Mathf.Max(sim.Speed, f.launchSpeed * T);
        Model.Reset(sim.Joints[girdle], heading * v + up * ((f.vaultSpeed > 0f ? f.vaultSpeed : kVault) * T), heading, up);
        DiveFold = 0f; Model.DemandCap = float.MaxValue; sim.Crouch = 0f;
        Model.AltitudeGoal = altitudeGoal;
        Model.SpeedGoal = f.cruiseSpeed * T;
        Model.TurnRateGoal = 0f;
        goalAlt = Model.AltitudeGoal; goalSpeed = Model.SpeedGoal; goalTurn = 0f;
        Chain.Reset(Model.Position, Model.Velocity);
        Array.Copy(sim.Joints, air, n);
        for (int i = 0; i < n; i++) AirWeight[i] = 0f;
        Wings.ResetStroke(1f);
        armBlend = 0f; foldFloor = 1f;
        Landing = false; Perched = false;
        probeCountdown = 0;
        Avoidance.Reset();
        sim.SetGrip(false);
        Mode = FlightMode.Launch; ModeTime = 0f;
        Launches++;
        return true;
    }

    // Altitude (world, along up), speed (m/s) and turn rate (rad/s, + = right) to fly at. Ignored while
    // landing (the landing autopilot owns the goals).
    // toward: the point the turn rate pursues (TurnToward), when it does - the avoidance then predicts the
    // pursuit (the turn eases off as the heading comes round) instead of a constant turn.
    public void SetGoals(float altitude, float speed, float turnRate, Vector3? toward = null)
    {
        if (Landing) return;
        Model.AltitudeGoal = altitude; Model.SpeedGoal = speed; Model.TurnRateGoal = turnRate;
        goalAlt = altitude; goalSpeed = speed; goalTurn = turnRate; goalToward = toward;
    }
    Vector3? goalToward, landToward;

    // The brain's goals as it set them (the avoidance overrides the model's copy for a while; every sub-step
    // starts again from these).
    float goalAlt, goalSpeed, goalTurn;

    // Steer toward a point (pure pursuit within the coordinated turn's limit) at an altitude and speed.
    public void SteerTo(Vector3 point, float altitude, float speed) => SetGoals(altitude, speed, TurnToward(point), point);

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
    // A planned final (avoidance on) is flown a little slower than cruise (kSlowApproach; slower than ~0.85
    // cruise the serpent cannot hold its height), for a tighter turn (R ~ v^2) among trees.
    public float ApproachSpeed => kSlowApproach * f.cruiseSpeed * T;
    public float ApproachRadius
    {
        get { float v = ApproachSpeed; return v * v / (FlightModel.G * Mathf.Tan(f.maxBankDeg * Mathf.Deg2Rad)); }
    }
    const float kSlowApproach = 0.88f;

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

    // A straight leg clear of obstacles (bark, rocks) for the whole span (FlightAvoidance.SegmentClear); always
    // true while the avoidance is off.
    public bool LegClear(Vector3 from, Vector3 to) => !Avoidance.Enabled || Avoidance.SegmentClear(obstacles, from, to, Model.Up);

    public float TurnToward(Vector3 point)
    {
        Vector3 up = Model.Up;
        Vector3 to = Vector3.ProjectOnPlane(point - Model.Position, up);
        if (to.sqrMagnitude < 1e-8f) return 0f;
        float yaw = Vector3.SignedAngle(Model.Forward, to.normalized, up) * Mathf.Deg2Rad;
        float v = Mathf.Max(Model.HorizontalSpeed, f.minSpeed * T);
        float wMax = FlightModel.G * Mathf.Tan(f.maxBankDeg * Mathf.Deg2Rad) / v;
        return Mathf.Clamp(f.PursuitGain / T * yaw, -wMax, wMax);
    }

    // Land at point (source: what it is - 0 terrain, else a collider: a perch). From the air the landing
    // autopilot flies to an entry point (a straight final of flareDistance + 2.5 turn radii, kept inside the
    // enclosure), the final on a glide slope, the flare and the touchdown.
    // axis: on a perch (source != 0), the direction its top runs along (a branch): the touchdown lays the
    // body along it with the girdle on the point (the eased touchdown hides the shift). Zero: none.
    public void Land(Vector3 point, int source = 0, Vector3 axis = default)
    {
        Landing = true; LandPoint = point; landSource = source; landAxis = axis;
        if (f.perchTubeOnApproach)
        {
            perchApproach = Vector3.ProjectOnPlane(point - Model.Position, Model.Up);
            perchApproach = perchApproach.sqrMagnitude > 1e-8f ? perchApproach.normalized : Model.Forward;
        }
        approach = Approach.Entry;
        // a branch perch with an approach heading: hits on the branch are the perch, the final flies along it
        // avoidance on, a planned final (a heading given): flown along it; on a perch (source != 0) along the
        // branch's slope, hits on the branch being the perch
        perchHeading = axis.sqrMagnitude > 1e-8f && Avoidance.Enabled ? Vector3.ProjectOnPlane(axis, Model.Up) : Vector3.zero;
        perchRise = 0f;
        Avoidance.ClearPerch();
        if (perchHeading.sqrMagnitude > 1e-8f)
        {
            perchRise = source != 0 ? Vector3.Dot(axis, Model.Up) / perchHeading.magnitude : 0f;
            perchHeading.Normalize();
            if (source != 0) Avoidance.SetPerch(point, axis);
        }
        else perchHeading = Vector3.zero;
        if (perchHeading != Vector3.zero)
        {
            PlanHold(point, perchHeading, out holdEntry, out holdCentre, out holdRadius, out holdSense);
            // already behind the runway and heading in: straight onto the final line; else out along the lane first
            Vector3 rel = Vector3.ProjectOnPlane(Model.Position - point, Model.Up);
            outbound = !(Vector3.Dot(rel, perchHeading) < -FinalRun && Vector3.Dot(Model.Forward, perchHeading) > 0f);
            laneAlt = Model.Altitude; laps = 0;
        }
        PlanEntry();
    }

    Vector3 landAxis;
    Vector3 perchApproach;   // FlightSpec.perchTubeOnApproach: the planar direction to the perch when the landing was asked for
    // A planned final is a racetrack flown by L1 guidance (L1Turn): out along a lane beside the final line
    // (holdSense side, two approach turn radii over, against the heading) until there is a runway of FinalRun
    // behind the point, then the turn in (round holdCentre) onto the line and the final along it.
    Vector3 holdEntry, holdCentre; // planned final: where its runway starts, and the turn-in circle there
    float holdRadius, holdSense;   // ... its radius and side (+1: the lane and the turn on the right)
    bool outbound;                 // on the lane, flying away from the point
    const float kRunwayRadii = 1.5f;   // approach turn radii of straight final before the flare
    const float kL1Time = 1.3f;        // s at scale 1: L1 guidance's look-ahead time (its distance = speed x this)
    const float kL1Min = 0.8f;     // m at scale 1: ... at least (the flare's low speed)
    const float kPerchStroke = 0.5f;  // of the comfort band: a branch flare's strokes at most
    const float kFinalYaw = 30f, kFinalCross = 0.4f; // a planned final starts this well lined up (deg) and this near its line (x the turn radius)
    const float kPerchFlare = 0.7f;
    const float kPerchFootArc = 0.15f; // m at scale 1: a hind foot's rise over its touchdown blend onto a perch   // m at scale 1 above the clearance a branch flare holds (kTouchMargin over ground)
    const int kThumbPlantPasses = 4;   // WingSpec.thumbPlant: wrist corrections per frame (the miss shrinks ~10x a pass)
    const float kLowFinal = 0.8f;
    const float kLaneHigh = 2.5f;     // m at scale 1: the racetrack's lane at most this far over the approach height
    float laneAlt;
    int laps;                         // racetrack laps flown without a final (they relax its gates)                    // the height the landing started at (the lane keeps it)     // m at scale 1 below the approach height a planned final may still start
    const float kFinalAlignDeg = 25f; // the flare starts only this well lined up (else round again)
    // the straight final's length before the point (the flare distance and kRunwayRadii approach turn radii)
    float FinalRun => f.flareDistance * scale + kRunwayRadii * ApproachRadius;
    float perchRise;        // the branch's rise along perchHeading (tan of its slope)
    Vector3 perchHeading;   // avoidance on, landing on a branch: the final's heading (along the branch), else zero
    enum Approach { Entry, Final }
    Approach approach;
    Vector3 entryPoint, finalDir;
    public Vector3 EntryPoint => entryPoint;
    public bool OnFinal => Landing && approach == Approach.Final;
    public int GoArounds { get; private set; }     // flares that went round (debug), and why the last did:
    public int LastGoAround { get; private set; }  // 1 off its line, 2 past the point, 3 below it, 4 too long
    public Vector3 HoldCentre => holdCentre;      // planned final: the turn-in circle (debug)
    public bool Outbound => Landing && perchHeading != Vector3.zero && approach == Approach.Entry && outbound; // on the lane (debug)
    public Vector3 ApproachHeading => perchHeading; // planned final's heading, zero = none (debug)
    // turn rate per radian of heading error (/ sqrt(scale)): FlightSpec.PursuitGain (0 = 0.9)
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
        if (perchHeading != Vector3.zero)
        {
            // a branch: the final comes in along it, from the side the brain checked
            finalDir = perchHeading; entryPoint = LandPoint - perchHeading * d;
            entryPoint += up * (Vector3.Dot(LandPoint - entryPoint, up) + kApproachHeight * scale);
            return;
        }
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
        Avoidance.ClearPerch();
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
    // The creature kit's locomotion mode (CreatureSim asks it before walking each sub-step).
    int CreatureKit.ILocomotionMode.Priority => 100;
    bool CreatureKit.ILocomotionMode.SubStep(CreatureSim s, float dt, Vector3 target) => SubStep(dt, target);

    internal bool SubStep(float dt, Vector3 target)
    {
        GateSurfaces();
        switch (Mode)
        {
            case FlightMode.Ground:
                if (crouchLeft > 0f)
                {
                    // the quad launch's crouch: the body sinks on its limbs (eased), then vaults
                    crouchLeft -= dt;
                    float c = 1f - Mathf.Clamp01(crouchLeft / (f.launchCrouch * T));
                    sim.Crouch = f.launchCrouchDepth * scale * c * c * (3f - 2f * c);
                    if (crouchLeft <= 0f) { crouchLeft = 0f; Vault(crouchAlt); }
                }
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
                Model.AmplitudeCap = float.MaxValue;
                Model.GlideEnergyToGoal = Landing && f.approachBleedsSpeed;
                if (Landing) SteerLanding();
                AirStep(dt);
                Array.Copy(air, sim.Joints, n);
                Chain.WriteRotations(sim.Rotations);
                sim.RigidHeadFromRotations();   // the rigid head (rigidHeadJoints) in the air too
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
                    sim.LegsInternal.HoldDroop = false;
                    for (int i = 0; i < n; i++) AirWeight[i] = 0f;
                    armBlend = 0f; foldFloor = 1f;
                    Perched = landSource != 0;
                    Landing = false;
                    Avoidance.ClearPerch();
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
        if (Avoidance.Enabled && !Landing) { Model.AltitudeGoal = goalAlt; Model.SpeedGoal = goalSpeed; Model.TurnRateGoal = goalTurn; }
        // (out on a racetrack's lane the perch is an obstacle like any other: the flyer is going round, not down)
        bool goingDown = Landing && !Outbound;
        if (Avoidance.Enabled && Landing && landSource != 0)
        {
            // (FlightSpec.perchTubeOnApproach: a perch landed on without an axis - a rock top - keeps the perch tube along
            // the heading it was approached on; else none was set and the rock turned the final away)
            if (goingDown) Avoidance.SetPerch(LandPoint, f.perchTubeOnApproach && landAxis.sqrMagnitude < 1e-8f ? perchApproach : landAxis);
            else Avoidance.ClearPerch();
        }
        if (Avoidance.Enabled) Avoidance.Apply(dt, Model, obstacles, sim.Bounds, goingDown ? LandPoint : (Vector3?)null, Mode == FlightMode.Flare,
                                              Landing ? landToward : goalToward, f.PursuitGain);
        Model.Control(dt);
        Wings.Advance(dt, Model);
        // (DiveFold: a behaviour folds the wings in the air - a stoop; 0 = the flight's own floor exactly)
        float ff = DiveFold > foldFloor ? DiveFold : foldFloor;
        if (ff > 0f)
            for (int w = 0; w < Wings.Count; w++)
            {
                ref WingState st = ref Wings.State[w];
                st.fold = Mathf.Max(st.fold, ff);
                st.area *= 1f - 0.8f * ff; // a folded wing barely grips the air
            }
        Model.Integrate(dt, Wings);
        Contain();
        ProbeGround();
        if (Chain.Twist != null)
        {
            // the twisting head's path stays in the aviary and above the ground; calm while launching or landing
            Chain.Twist.Bounds = sim.Bounds;
            Chain.Twist.SetFloor(GroundBelow, Model.Up, sp.joints[girdle].clearance * scale);
            Chain.Twist.Calm = Landing || Mode != FlightMode.Air;
            // the bob goes on round the landing approach (the wings beat all the way) and fades only on the final
            Chain.Twist.BobCalm = OnFinal || Mode != FlightMode.Air;
        }
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
                if (perchHeading != Vector3.zero)
                {
                    // a planned final (a branch, or a clear line into open ground): out along the lane until there
                    // is a runway behind the point (or the enclosure's wall comes up), then in onto the final line
                    Vector3 rel = Vector3.ProjectOnPlane(Model.Position - LandPoint, up);
                    float along = Vector3.Dot(rel, perchHeading);
                    // (and high enough: a slow climber turned in from low reached the point below it and went
                    // round right at the tree; the wall ends the lane whatever the height)
                    // (hysteresis: out on the lane it must climb half way further before turning in)
                    bool high = Vector3.Dot(Model.Position, up) > landH + (kApproachHeight - (outbound ? 0.5f : 1f) * kLowFinal) * scale;
                    // the turn in (a semicircle round PlanHold's centre: tangent to the final line at its runway's
                    // start) begins abeam that centre; earlier only where the turn from here would leave the safe disc
                    bool wallNear = false;
                    if (outbound && sim.Bounds.HasValue)
                    {
                        Vector3 turnC = Model.Position - Vector3.Cross(up, perchHeading) * (holdSense * holdRadius);
                        wallNear = sim.Bounds.Value.PlanarDistance(turnC) + holdRadius > sim.Bounds.Value.safeRadius - kWallSlack * scale;
                    }
                    if (outbound && ((along < -FinalRun && high) || wallNear)) outbound = false;
                    // out on the lane it keeps the height it came in at (cruise is over the crowns: coming down to
                    // the approach height at once the lane's turns swept the trees), up to kLaneHigh over the
                    // approach height; turned in it comes down to the approach height before the final starts
                    float approachH = landH + kApproachHeight * scale;
                    Model.AltitudeGoal = outbound ? Mathf.Clamp(laneAlt, approachH, approachH + kLaneHigh * scale) : approachH;
                    // each lap round the racetrack without a final widens the gates a little (relax, up to 3)
                    float relax = Mathf.Min(laps, 3);
                    bool notHigh = Vector3.Dot(Model.Position, up) < approachH + kLowFinal * (1f + 0.5f * relax) * scale;
                    Model.SpeedGoal = ApproachSpeed;
                    landToward = null;
                    if (outbound)
                    {
                        Vector3 lane = LandPoint + Vector3.Cross(up, perchHeading) * (holdSense * 2f * holdRadius);
                        Model.TurnRateGoal = L1Turn(lane, -perchHeading);
                        return;
                    }
                    Model.TurnRateGoal = L1Turn(LandPoint, perchHeading);
                    // on the line behind the runway's start, lined up: the final (glide slope) starts
                    float yawH = Vector3.Angle(Model.Forward, perchHeading);
                    float cross = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(up, perchHeading)));
                    if (yawH < kFinalYaw + 5f * relax && cross < kFinalCross * (1f + 0.5f * relax) * holdRadius && high && notHigh)
                    {
                        approach = Approach.Final; finalDir = perchHeading; entryPoint = Model.Position;
                    }
                    else
                    {
                        // turned in too late (near the point, not on the line), or still too low by the runway's
                        // start (it would reach the point below it): out again, climbing on the lane
                        if (along > -flare || (!high && along > -FinalRun - holdRadius)) { outbound = true; laps++; }
                        return;
                    }
                }
                else if (yawToL < 25f && dist > flare + R)
                {
                    approach = Approach.Final; finalDir = to / dist; entryPoint = Model.Position;
                }
                else
                {
                    bool room = dist > flare + 2f * R;
                    Model.AltitudeGoal = landH + kApproachHeight * scale;
                    Model.SpeedGoal = f.cruiseSpeed * T;
                    bool wall = WallTurn(out float wt), pursue = !wall && (room || yawToL < 90f);
                    Model.TurnRateGoal = wall ? wt : pursue ? TurnToward(LandPoint) : 0f;
                    landToward = pursue ? LandPoint : (Vector3?)null;
                    return;
                }
            }
            Model.AltitudeGoal = PerchFloor(landH + clear + kTouchMargin * scale + kGlideSlope * Mathf.Max(0f, dist - flare), clear);
            Model.SpeedGoal = perchHeading != Vector3.zero ? ApproachSpeed : kApproachSpeed * f.cruiseSpeed * T;
            // a planned final holds its line (the guidance field, steeper near it); else pursue the point
            if (perchHeading != Vector3.zero) { Model.TurnRateGoal = L1Turn(LandPoint, perchHeading); landToward = null; }
            else { Model.TurnRateGoal = TurnToward(LandPoint); landToward = LandPoint; }
            // flare once the point is within flareDistance ahead and close to the track; passed it (or wide of
            // it) without flaring: go around
            float aheadD = Vector3.Dot(to, Model.Forward);
            float lateral = (to - Model.Forward * aheadD).magnitude;
            bool lined = perchHeading == Vector3.zero || Vector3.Angle(Model.Forward, perchHeading) < kFinalAlignDeg;
            if (aheadD < flare && aheadD > -0.25f * flare && lateral < 0.6f * flare && lined) { Mode = FlightMode.Flare; ModeTime = 0f; }
            else
            {
                if (aheadD < 0.5f * flare) { approach = Approach.Entry; outbound = true; PlanEntry(); }
                return;
            }
        }
        // (over a branch the flare stays higher, kPerchFlare: the braking downstrokes reach a metre below the
        // girdle and swept the branch behind the point; the touchdown eases the rest of the way)
        Model.AltitudeGoal = PerchFloor(landH + clear + (landSource != 0 ? kPerchFlare : 0.5f * kTouchMargin) * scale, clear);
        // ... and beats shallow (kPerchStroke of the band): a full downstroke reaches past the branch
        Model.AmplitudeCap = landSource != 0 ? kPerchStroke : float.MaxValue;
        Model.SpeedGoal = kFlareSpeed * f.minSpeed * T * Mathf.Clamp01(dist / flare);
        Model.TurnRateGoal = perchHeading != Vector3.zero ? L1Turn(LandPoint, perchHeading) : TurnToward(LandPoint);
        // flown past the point in the flare without touching down: go around
        // past the point, below it (missed it, e.g. beside a branch), or too long in the flare: go around
        bool below = Vector3.Dot(Model.Position, up) < landH - 0.25f * scale;
        // a planned final's flare that has turned off its line (a lateral miss at low speed pulled it round): go
        // round at once - turning in the flare swept the wings through the tree
        bool offLine = perchHeading != Vector3.zero && Vector3.Angle(Model.Forward, perchHeading) > kFinalAlignDeg + 15f;
        bool passed = Vector3.Dot(to, Model.Forward) < -flare && ModeTime > 0.5f * T;
        if (offLine || passed || (below && ModeTime > 0.5f * T) || ModeTime > kFlareTimeout * T)
        {
            GoArounds++; LastGoAround = offLine ? 1 : passed ? 2 : below && ModeTime > 0.5f * T ? 3 : 4;
            Mode = FlightMode.Air; ModeTime = 0f; approach = Approach.Entry; outbound = true; PlanEntry();
        }
    }

    // The racetrack of a planned final into `point` along `heading` (see holdEntry): the runway's start FinalRun
    // back along the line, and the turn-in circle (an approach turn radius) tangent to the line there on the side
    // that fits the enclosure better (its far edge nearer the middle; no enclosure: the flyer's side). The lane
    // runs on that side, two radii over.
    public void PlanHold(Vector3 point, Vector3 heading, out Vector3 entry, out Vector3 centre, out float radius, out float sense)
    {
        Vector3 up = Model.Up;
        Vector3 hd = Vector3.ProjectOnPlane(heading, up).normalized;
        radius = ApproachRadius;
        entry = point - hd * FinalRun;
        Vector3 side = Vector3.Cross(up, hd);
        Vector3 cR = entry + side * radius, cL = entry - side * radius;
        bool right;
        if (sim.Bounds.HasValue)
        {
            EnclosureArea a = sim.Bounds.Value;
            right = a.PlanarDistance(cR) <= a.PlanarDistance(cL);
        }
        else right = Vector3.Dot(Model.Position - entry, side) >= 0f;
        centre = right ? cR : cL;
        sense = right ? 1f : -1f;
    }

    // L1 guidance onto the line through p0 along dir (Park, Deyst and How 2004): the reference point is where a
    // circle of L1 round the flyer meets the line ahead (the line's nearest point when it is farther than L1); the
    // turn rate is 2 v sin(eta) / L1, eta the angle from the ground track to it (a full turn when it is behind).
    // L1 = speed x kL1Time (x sqrt scale): it shrinks as the flare brakes, so the line is held to the end. Stable
    // against the bank's lag where a heading pursuit of the line overshot by 20-30 deg.
    float L1Turn(Vector3 p0, Vector3 dir)
    {
        Vector3 up = Model.Up;
        Vector3 rel = Vector3.ProjectOnPlane(Model.Position - p0, up);
        Vector3 side = Vector3.Cross(up, dir);
        float e = Vector3.Dot(rel, side), along = Vector3.Dot(rel, dir);
        float v = Mathf.Max(Model.HorizontalSpeed, f.minSpeed * T);
        float L1 = Mathf.Max(kL1Time * T * Model.HorizontalSpeed, kL1Min * scale);
        float ahead = Mathf.Abs(e) < L1 ? Mathf.Sqrt(L1 * L1 - e * e) : 0f;
        Vector3 to = Vector3.ProjectOnPlane(p0 + dir * (along + ahead) - Model.Position, up);
        Vector3 track = Vector3.ProjectOnPlane(Model.Velocity, up);
        if (track.sqrMagnitude < 1e-6f) track = Model.Forward;
        float eta = Vector3.SignedAngle(track, to, up) * Mathf.Deg2Rad;
        float sinEta = Mathf.Abs(eta) > 0.5f * Mathf.PI ? Mathf.Sign(eta) : Mathf.Sin(eta);
        float wMax = FlightModel.G * Mathf.Tan(f.maxBankDeg * Mathf.Deg2Rad) / v;
        return Mathf.Clamp(2f * v * sinEta / Mathf.Max(L1, 1e-3f), -wMax, wMax);
    }

    // Over a sloping branch on the final: never lower than the branch under the flyer (its height along the
    // line, from the point's height and the branch's rise) plus the clearance and the touch margin.
    float PerchFloor(float goal, float clear)
    {
        if (perchHeading == Vector3.zero || perchRise == 0f) return goal;
        Vector3 up = Model.Up;
        float along = Vector3.Dot(Vector3.ProjectOnPlane(Model.Position - LandPoint, up), perchHeading);
        if (along >= 0f || along < -kPerchFloorBack * scale) return goal;
        return Mathf.Max(goal, Vector3.Dot(LandPoint, up) + perchRise * along + clear + (landSource != 0 ? kPerchFlare : kTouchMargin) * scale);
    }
    const float kPerchFloorBack = 3f; // m at scale 1 behind the point the branch floor applies
    const float kPerchCapture = 0.4f; // m at scale 1: a perch touchdown starts this near the branch's line

    bool ReadyToTouchDown()
    {
        if (Mode != FlightMode.Flare) return false;
        float clear = sp.joints[girdle].clearance * scale;
        Vector3 to = Vector3.ProjectOnPlane(LandPoint - Model.Position, Model.Up);
        // up to kTouchHeight above its clearance: the touchdown eases the rest of the way down
        bool low = HeightAboveGround <= clear + kTouchHeight * scale;
        bool there = to.magnitude < 0.5f * f.flareDistance * scale || ModeTime > 1.5f * T;
        if (landSource != 0)
        {
            // a perch (a branch): low over the point itself, not the ground under the girdle (a probe straight
            // down misses a 10 cm branch a hand's width to the side), and close to its line (or the point); the
            // eased touchdown lays the body along it (BeginTouchdown)
            float above = Vector3.Dot(Model.Position - LandPoint, Model.Up);
            float off = perchHeading != Vector3.zero ? Mathf.Abs(Vector3.Dot(to, Vector3.Cross(Model.Up, perchHeading))) : to.magnitude;
            low = above <= clear + kTouchHeight * scale;
            there = to.magnitude < 0.5f * f.flareDistance * scale && off < kPerchCapture * scale;
        }
        return low && there;
    }

    void BeginTouchdown()
    {
        Array.Copy(sim.Joints, snapshot, n);
        DiveFold = 0f; Model.DemandCap = float.MaxValue;
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
            // each segment its own length: the clearances may vary along the spine (a tall stance's), so the run
            // along the perch per segment is what is left of it after the height step (was seg flat: +9 %)
            sim.Joints[girdle] = LandPoint + up * (sp.joints[girdle].clearance * scale);
            for (int i = girdle + 1; i < n; i++)
            {
                float dh = (sp.joints[i].clearance - sp.joints[i - 1].clearance) * scale;
                sim.Joints[i] = sim.Joints[i - 1] - axis * Mathf.Sqrt(Mathf.Max(0f, seg * seg - dh * dh)) + up * dh;
            }
            for (int i = girdle - 1; i >= 0; i--)
            {
                float dh = (sp.joints[i].clearance - sp.joints[i + 1].clearance) * scale;
                sim.Joints[i] = sim.Joints[i + 1] + axis * Mathf.Sqrt(Mathf.Max(0f, seg * seg - dh * dh)) + up * dh;
            }
            touchSpeed = 0f;
        }
        else if (f.touchdownAtStance)
        {
            // onto the ground (FlightSpec.touchdownAtStance): the walking body starts at its stance - the girdle where
            // it is, the other joints along the heading at their clearances, each segment spacing long (the bind's
            // layout, CreatureSim.RestRun) - and the eased touchdown carries the hovering body there
            Vector3 up = Model.Up;
            Vector3 axis = Vector3.ProjectOnPlane(sim.Joints[0] - sim.Joints[girdle], up);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.ProjectOnPlane(Model.Forward, up);
            if (axis.sqrMagnitude < 1e-8f) axis = Model.Forward;
            axis.Normalize();
            float seg = sp.spacing * scale;
            for (int i = girdle + 1; i < n; i++)
                sim.Joints[i] = sim.Joints[i - 1] - axis * CreatureSim.RestRun(sp, i, seg, scale) + up * ((sp.joints[i].clearance - sp.joints[i - 1].clearance) * scale);
            for (int i = girdle - 1; i >= 0; i--)
                sim.Joints[i] = sim.Joints[i + 1] + axis * CreatureSim.RestRun(sp, i + 1, seg, scale) + up * ((sp.joints[i].clearance - sp.joints[i + 1].clearance) * scale);
            touchSpeed = 0f;
        }
        sim.SetGrip(f.perchGrip && landSource != 0);   // perched: the feet close round the branch
        // onto a perch the arms come down to the raised wrist (standing on the thumb claws) from the start: blended
        // straight to the foot point, the wrist swept the branch's flank (it is set again once on the ground)
        if (landSource != 0)
            for (int w = 0; w < Wings.Count; w++) wristLift[w] = Mathf.Max(wristLift[w], sp.wings[w].groundWristLift * scale);
        sim.ResetGround(0.5f * touchSpeed, (kTouchHeight + 1f) * scale);
        // (touchdownAtStance onto the ground: the walking body stands at its stance until the touchdown ends - its legs,
        // measured from the hovering body still above, would lower it and tip a steep neck over)
        sim.LegsInternal.HoldDroop = f.touchdownAtStance && landSource == 0;
        offsetValid = false;
        Mode = FlightMode.Touchdown; ModeTime = 0f;
        if (f.touchdownFromHover)
        {
            // FlightSpec.touchdownFromHover: the walking state restarts from the laid body (Path, above), the displayed
            // joints stay the hovering body - the touchdown blend's start (BeforeLegs takes its offset in the next
            // walking sub-step). Begun on a tick's last sub-step, the laid body was shown for one tick instead.
            Array.Copy(snapshot, sim.Joints, n);
            sim.RefreshFrames();
        }
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
            // a twisting chain (AirTwist) starts as the grounded body and follows its own head up: it is the body at
            // once (the weights still hand the legs and wings over); a trailing chain is blended in head first
            bool follow = Chain.Twist != null;
            // FlightSpec.launchTailBySegment: the walking trunk + tail's segment directions, kept before the blend below
            // (offset is touchdown scratch, unused while launching)
            bool bySegment = f.launchTailBySegment && !follow;
            if (bySegment)
                for (int i = girdle + 1; i < n; i++) offset[i] = J[i] - J[i - 1];
            for (int i = 0; i < n; i++)
            {
                float lag = kHeadLead * i / Mathf.Max(1, n - 1);
                float w = Smooth01(t - lag);
                AirWeight[i] = w;
                J[i] = follow ? air[i] : Vector3.Lerp(J[i], air[i], w);
            }
            armBlend = Smooth01(t / kArmHandover);
            foldFloor = 1f - Smooth01(t / (1f + kHeadLead));
            if (!follow)
            {
                Relength(J);
                if (bySegment)
                {
                    // from the blended girdle back, each segment turned from the walking body's direction toward the air
                    // chain's by its joint's air weight (exact lengths): the lerp of two far-apart tails (one curled from
                    // a circling walk, one trailing the vault) shrank segments to nothing and Relength folded them back.
                    // Joints whose weights differ turn by different shares, so each bend behind the girdle is also kept
                    // inside its joint's cone (the air chain's limits).
                    float seg = sp.spacing * scale;
                    Vector3 prev = Vector3.zero;
                    for (int i = girdle + 1; i < n; i++)
                    {
                        Vector3 d = SlerpDir(offset[i], air[i] - air[i - 1], AirWeight[i]);
                        if (i > girdle + 1 && !NoLaunchTailConeForTest) d = Chain.ConeAt(i - 1, prev, d);
                        J[i] = J[i - 1] + d * seg;
                        prev = d;
                    }
                }
                UpFrames(J);
            }
        }
        else if (Mode == FlightMode.Touchdown)
        {
            if (!offsetValid)
            {
                for (int i = 0; i < n; i++) offset[i] = snapshot[i] - J[i];
                offsetValid = true;
            }
            float t = ModeTime / (kTouchdownTime * T);
            if (f.touchdownTogether) TogetherBlend(J, Smooth01(t / 0.6f));
            else
            {
                for (int i = 0; i < n; i++)
                {
                    // the hips and tail settle first, the head last
                    float lag = 0.4f * (1f - i / Mathf.Max(1f, n - 1f));
                    float g = Smooth01((t - lag) / 0.6f);
                    AirWeight[i] = 1f - g;
                    J[i] += offset[i] * (1f - g);
                }
            }
            armBlend = 1f - Smooth01(t / kArmHandover);
            foldFloor = Smooth01(t);
            if (!f.touchdownTogether) Relength(J);
            sim.RefreshFrames();
        }
    }

    // Test-only (REDs of FlightSpec.launchTailBySegment): the segment blend without its bend cones / the tail's walking frames.
    internal static bool NoLaunchTailConeForTest, WalkingLaunchTailFramesForTest;

    // After the walking sub-step's frames: a twisting chain's launch body is the air chain itself (BeforeLegs), so
    // its frames are the chain's (the walking frames' head follows the ground heading, not the rising head).
    internal void AfterFrames()
    {
        if (Mode == FlightMode.Launch && Chain.Twist != null) { Chain.WriteRotations(sim.Rotations); sim.RigidHeadFromRotations(); }
        else if (Mode == FlightMode.Launch && f.launchTailBySegment && !WalkingLaunchTailFramesForTest)
        {
            // FlightSpec.launchTailBySegment: behind the girdle the launch's own frames (BeforeLegs' UpFrames: up carried
            // down the blended body), not the walking frames' - their up is the ground's projected across each segment
            // and rolled a tail segment over as the rising tail swept it through vertical
            for (int i = girdle + 1; i < n; i++) sim.Rotations[i] = CreatureSim.Basis(Chain.Forward[i], Chain.Up[i]);
        }
        if (Mode == FlightMode.Launch && Chain.Twist == null && f.launchHeadOnNeck && sp.RigidHead < 2 && n > 1 && AirWeight[0] > 0f)
        {
            // FlightSpec.launchHeadOnNeck: the walking frames keep the head on the ground heading; it turns with its
            // rising neck as the air takes it (the air chain's head frame: along its front segment), so the steep
            // launch neck no longer folds over a level head. (A rigid head's frame already follows its neck chord.)
            Vector3[] J = sim.Joints;
            Vector3 fw = J[0] - J[1];
            if (fw.sqrMagnitude > 1e-6f)
            {
                fw.Normalize();
                Vector3 u = Vector3.ProjectOnPlane(sim.Rotations[1] * Vector3.up, fw);
                if (u.sqrMagnitude > 1e-6f)
                    sim.Rotations[0] = Quaternion.Slerp(sim.Rotations[0], CreatureSim.Basis(fw, u.normalized), AirWeight[0]);
            }
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
        bool moved = f.perchFrontLevel && HoldFrontOnPerch(path, pathUp, groundPoint, probed, sup, drop);
        // the first joint (behind the girdle) whose ground falls away: the support's edge
        int a = -1;
        for (int i = Mathf.Max(2, girdle); i < n; i++)
        {
            Vector3 u = pathUp[i];
            float support = Vector3.Dot(groundPoint[i - 1], u);
            if (!sup[i] || Vector3.Dot(probed[i], u) < support - drop) { a = i; break; }
        }
        if (a < 0) return moved;
        Vector3 upF = pathUp[a];
        Vector3 hb = Vector3.ProjectOnPlane(path[a - 1] - path[a - 2], upF);
        if (hb.sqrMagnitude < 1e-10f) hb = Vector3.ProjectOnPlane(path[a] - path[a - 1], upF);
        if (hb.sqrMagnitude < 1e-10f) return false;
        hb.Normalize();
        Vector3 down = -upF;
        float pitch0 = Mathf.Asin(Mathf.Clamp(Vector3.Dot((path[a - 1] - path[a - 2]).normalized, down), -1f, 1f)) * Mathf.Rad2Deg;
        // pass 1: where the hanging run reaches the ground below (b), pass 2: lay it, easing back to level there
        int b = n;
        bool own = f.drapeOwnHeading;
        float minPlanar = kDrapeMinPlanar * seg;
        for (int pass = 0; pass < 2; pass++)
        {
            float prevPitch = pitch0;
            Vector3 prev = path[a - 1];
            Vector3 h = hb;
            for (int i = a; i < (pass == 0 ? n : b); i++)
            {
                Vector3 d = path[i] - path[i - 1];
                if (own)
                {
                    // FlightSpec.drapeOwnHeading: each hanging segment pitches down in its own vertical plane (the walked
                    // segment's heading, blended with the one before's: a nearly vertical one keeps it), so a change of the edge
                    // joint or of the reference segment's heading no longer swings the whole hanging run about the edge
                    Vector3 hp = Vector3.ProjectOnPlane(d, upF) + h * minPlanar;
                    if (hp.sqrMagnitude > 1e-12f) h = hp.normalized;
                }
                float pitch = d.sqrMagnitude > 1e-12f ? Mathf.Asin(Mathf.Clamp(Vector3.Dot(d.normalized, down), -1f, 1f)) * Mathf.Rad2Deg : prevPitch;
                pitch = Mathf.MoveTowards(pitch, kHangPitchDeg, rate);
                float lim = sp.joints[i - 1].rigid ? 0f : sp.joints[i - 1].bendLimitDeg;
                pitch = Mathf.Clamp(pitch, prevPitch - lim, prevPitch + lim);
                if (pass == 1 && b < n) pitch = Mathf.Min(pitch, lim * (b - i));
                float pr = pitch * Mathf.Deg2Rad;
                Vector3 p = prev + (h * Mathf.Cos(pr) + down * Mathf.Sin(pr)) * seg;
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

    // FlightSpec.perchFrontLevel: perched and standing (a perch touchdown, or on the ground after one with Halt), the joints
    // ahead of the girdle whose ground falls away past the support's edge (the head and neck held out over a rock's edge)
    // keep the support's ground: each is raised to the ground of the joint behind it. Without it the head was grounded on
    // the floor below, the length restore (from the head) dragged the neck down after it, the neck's probes - their clearance
    // point now nearer the floor than the rock's top - took the floor, and the whole body sank into the rock's footprint.
    // True when it moved a joint.
    bool HoldFrontOnPerch(Vector3[] path, Vector3[] pathUp, Vector3[] groundPoint, Vector3[] probed, bool[] sup, float drop)
    {
        if (Mode == FlightMode.Touchdown ? landSource == 0 : !(Perched && sim.Halt)) return false;
        bool moved = false;
        for (int i = girdle - 1; i >= 0; i--)
        {
            Vector3 u = pathUp[i];
            float support = Vector3.Dot(groundPoint[i + 1], u), own = Vector3.Dot(groundPoint[i], u);
            if (sup[i] && Vector3.Dot(probed[i], u) >= support - drop) continue;
            float lift = support - own;
            if (lift <= 0f) continue;
            path[i] += u * lift;
            groundPoint[i] += u * lift;
            moved = true;
        }
        return moved;
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
                if (sp.wings[w].thumbPlant)
                {
                    // the claw's tip onto the foot point in 3D (WingSpec.thumbPlant): the wrist target moved by the claw's
                    // miss, a few passes (the hand frame follows the arm a little each time)
                    Vector3 wt = ls.foot + fu * h;
                    for (int pass = 0; pass < kThumbPlantPasses; pass++)
                    {
                        wt -= Wings.ThumbTip(w) - ls.foot;
                        elbow = TwoBoneIK.Solve(sh, wt, arm.upperLength * scale, arm.lowerLength * scale, Wings.ArmPole(w), out wrist);
                        Wings.PoseBetween(w, sh, elbow, wrist, fold, 0f);
                    }
                    h = Mathf.Clamp(Vector3.Dot(wrist - ls.foot, fu), 0f, hMax);
                }
                wristLift[w] = h;
            }
            // the arm as the renderer's bounds and the debug view see it (on the ground the foot stays the gait's)
            ls.shoulder = sh; ls.elbow = elbow;
            if (armBlend > 0f) ls.foot = ls.ankle = wrist;
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
            float reach = (s.upperLength + s.lowerLength) * scale * f.LegTuckReach;
            Vector3 tuckDir = ar * new Vector3(f.LegTuckOut * s.side, -f.LegTuckDown, -1f).normalized;
            // the IK end is the ankle (LegState.ankle; the foot hangs below it by the ankle height)
            float ah = sim.LegsInternal.AnkleHeight(l) * scale;
            Vector3 foot = Vector3.Lerp(ls.ankle, shoulder + tuckDir * reach, w);
            // touching down on a perch the foot comes down onto the branch from above (a straight blend from the
            // tuck swept the shin through it)
            if (Mode == FlightMode.Touchdown && landSource != 0) foot += (ar * Vector3.up) * (4f * w * (1f - w) * kPerchFootArc * scale);
            Vector3 pole = s.pole; pole.x *= s.side;
            pole = ar * pole;
            Vector3 elbow = TwoBoneIK.Solve(shoulder, foot, s.upperLength * scale, s.lowerLength * scale, pole, out Vector3 ft);
            Quaternion upper = sim.LegsInternal.UpperRot(l, elbow - shoulder, ar);
            dst[offsetIndex + sp.LegBone(l, 0)] = Matrix4x4.TRS(shoulder, upper, sc);
            dst[offsetIndex + sp.LegBone(l, 1)] = Matrix4x4.TRS(elbow, CreatureWings.LowerRot(upper, ft - elbow), sc);
            Vector3 toe = ft - elbow;
            Vector3 fn = Vector3.Slerp(ls.footNormal.sqrMagnitude > 0.5f ? ls.footNormal : ar * Vector3.up, ar * Vector3.back, w);
            Vector3 ff = Vector3.ProjectOnPlane(toe.sqrMagnitude > 1e-12f ? toe : ar * Vector3.back, fn);
            if (ff.sqrMagnitude < 1e-8f) ff = Vector3.ProjectOnPlane(ar * Vector3.forward, fn);
            Vector3 sole = ft - fn.normalized * ah;
            dst[offsetIndex + sp.LegBone(l, 2)] = Matrix4x4.TRS(sole, Quaternion.LookRotation(ff.normalized, fn.normalized), sc);
            if (Mode != FlightMode.Touchdown) { ls.shoulder = shoulder; ls.elbow = elbow; ls.ankle = ls.foot = ft; }
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
    // FlightSpec.touchdownTogether: the whole body from the frozen hovering pose onto the grounded body as it is now, the
    // girdle's position lerped and every segment's direction slerped out from it (exact lengths; a short deep trunk). A
    // blend of positions shortened the segments where the two bodies disagree and the re-lengthening folded them.
    void TogetherBlend(Vector3[] J, float g)
    {
        float seg = sp.spacing * scale;
        Vector3[] P = offset;   // scratch (the offsets are not used by this blend)
        P[girdle] = Vector3.Lerp(snapshot[girdle], J[girdle], g);
        for (int i = girdle + 1; i < n; i++)
            P[i] = P[i - 1] + SlerpDir(snapshot[i] - snapshot[i - 1], J[i] - J[i - 1], g) * seg;
        for (int i = girdle - 1; i >= 0; i--)
            P[i] = P[i + 1] + SlerpDir(snapshot[i] - snapshot[i + 1], J[i] - J[i + 1], g) * seg;
        for (int i = 0; i < n; i++) { J[i] = P[i]; AirWeight[i] = 1f - g; }
    }

    static Vector3 SlerpDir(Vector3 a, Vector3 b, float t)
    {
        float la = a.magnitude, lb = b.magnitude;
        if (!(la > 1e-9f)) return lb > 1e-9f ? b / lb : Vector3.forward;
        if (!(lb > 1e-9f)) return a / la;
        return Vector3.Slerp(a / la, b / lb, t).normalized;
    }

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
