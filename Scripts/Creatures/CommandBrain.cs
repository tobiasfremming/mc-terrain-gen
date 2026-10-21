using UnityEngine;

// The commands a player gives a creature (the lab's "M" menu).
public enum CreatureCommand { Stand, WalkAround, WalkTo, Fly, Circle, Land, Perch }

// A brain driven by commands (Vesper step 1, the lab's "M" menu): each command is turned into the creature's own
// motion through the shared walking and flight systems, with the natural transitions in between. The brain is
// reactive - every tick it looks at the flight's mode and does the next thing the command needs - so a command given
// at any moment (mid-launch, mid-touchdown, mid-landing) redirects what is going on instead of stalling or snapping:
//   Stand       on the ground: Halt. In the air: land on open ground first, then Halt.
//   WalkAround  on the ground: the WanderBrain's walk inside the area. In the air: land first.
//   WalkTo      walk to the point's ground projection (clamped into the safe disc), then Stand (Current turns to
//               Stand). In the air: land near the point first.
//   Fly         on the ground: face a clear launch run, Launch, then patrol between waypoints (SteerTo-style pursuit).
//               In the air: a landing in progress is called off.
//   Circle      a steady banked circle round the area's middle, of radius >= 1.2 x the cruise turn radius and inside
//               the safe disc (by kWallMargin turn radii), at a fixed altitude (0.6 of the roof). From the ground:
//               launch first.
//   Land        land on open ground; on the ground: Halt.
//   Perch       land on a perch: an up-facing non-terrain surface (a rock's top) with room along it, found by probing
//               down over the area (as FlightBrain.ChoosePerch confirms its spots), aimed at its top's middle. On the
//               ground and not perched: take off first. None in reach: land on open ground (or stay on the ground) and
//               Doing says so; kPerchFailsMax attempts that time out (kPerchTimeout each): open ground, Doing says so.
// Landings: the brain flies the approach itself and asks the flight's landing autopilot (Land, no axis) for the final,
// flare and touchdown only from where its final starts at once - the spot ahead and far enough (KeepLanding).
// Launch is only possible from the ground, and the crouch, vault and touchdown run to their end, so a command given
// during them waits for the mode it needs (the queue is the command itself). Uses only CreatureFlight's public surface
// (Launch, SetGoals, Land, CancelLanding, Mode, OnFinal, Model, TurnRadius, ProbeDown, TurnToward, WallTurn, LegClear,
// PlanHold - as FlightBrain does) and sim.Halt; FlightBrain is untouched. A touchdown meant for the perch that ends off it
// counts as a failed attempt too. Flyers that perch on rocks want FlightSpec.perchTubeOnApproach (Vesper has it).
// Allocation-free after construction; deterministic for a seed; main thread.
public sealed class CommandBrain : ICreatureBrain
{
    // Doing: what it is up to, for the menu (constant strings: no allocation)
    const string kStanding = "standing", kPerchedStanding = "perched", kArrived = "arrived: standing";
    const string kWalking = "walking around", kWalkingTo = "walking to the point";
    const string kLandToStand = "landing to stand", kLandToWalk = "landing to walk", kLandToWalkTo = "landing to walk to the point";
    const string kUpThenStand = "taking off, then landing to stand", kUpThenWalk = "taking off, then landing to walk", kUpThenWalkTo = "taking off, then landing to walk to the point";
    const string kDownToStand = "touching down to stand", kDownToWalk = "touching down to walk", kDownToWalkTo = "touching down to walk to the point";
    const string kFacing = "turning to take off", kTakingOff = "taking off", kDownThenUp = "touching down, then taking off";
    const string kFlying = "flying", kCircling = "circling";
    const string kLanding = "landing", kLanded = "landed", kUpThenLand = "taking off, then landing", kTouchingDown = "touching down";
    const string kToPerch = "flying to the perch", kUpToPerch = "taking off to perch", kDownToPerch = "touching down on the perch";
    const string kNoPerchLanding = "no perch in reach: landing on open ground", kNoPerchLanded = "no perch in reach: on open ground";
    const string kNoPerchUp = "no perch in reach: taking off, then landing on open ground", kNoPerchDown = "no perch in reach: touching down on open ground";
    const string kGaveUpLanding = "could not reach the perch: landing on open ground", kGaveUpDown = "could not reach the perch: touching down on open ground";
    const string kGaveUpLanded = "could not reach the perch: on open ground";
    const string kCannotFly = "cannot fly: standing", kLooking = "looking for a perch", kHopping = "hopping down";

    const float kWallMargin = 1.4f;    // waypoints and the circle keep this many turn radii inside the safe radius (FlightBrain's)
    const float kWaypointMin = 4f, kWaypointMax = 8f;   // s at scale 1 (x sqrt s) on one waypoint at most
    const float kLandingTimeout = 45f; // s at scale 1 (x sqrt s): a landing that has not touched down is flown again
    const float kFaceTime = 4f;        // s at scale 1 (x sqrt s): turning toward a clear launch run, at most
    const float kLaunchLook = 6f;      // m at scale 1 (x sqrt s) of clear air beyond the launch run (FlightBrain's)
    const float kCircleAlt = 0.6f;     // of the roof: the circle's height
    const float kCircleSpeed = 0.85f;  // of cruise
    const float kArrive = 0.1f;        // of the body length: WalkTo's arrival radius (at least kArriveMin x s)
    const float kArriveMin = 0.4f;
    const int kPerchFailsMax = 2;      // perch attempts that time out before it settles for open ground
    const float kPerchTimeout = 30f;   // s at scale 1 (x sqrt s): one perch attempt (line-up and final) at most
    const float kHopHeight = 0.75f;    // a perch higher than this x the longest walking leg is hopped down from, not walked off
    const int kMaxCandidates = 64;     // perch candidates kept per scan
    const int kScanProbes = 40;        // probes down a frame the perch scan makes at most
    const int kApproachTries = 8;      // the nearest perch surfaces given the full approach check
    const float kLineUpDeg = 20f;      // the landing is asked for with the spot this well ahead ...
    const float kFinalRadii = 1.2f;    // ... and more than flareDistance + this many turn radii away (the autopilot's final: 25 deg, 1)
    const float kAheadDeg = 12f;       // open ground: spots this far either side of straight ahead too
    const int kStageDirs = 16;         // perch: final lines tried for the staging point
    const float kApproachH = 2f;       // m at scale 1: the approach height over the spot (the autopilot's and FlightBrain's)
    const float kLaneLow = 1.2f;
    const int kChords = 12;
    const float kLoiterStraight = 2f;  // cruise turn radii: loitering inside this of the middle it flies straight on

    readonly EnclosureArea area;
    readonly WanderBrain wander;
    readonly Vector3[] candPoint = new Vector3[kMaxCandidates];
    readonly int[] candSource = new int[kMaxCandidates];
    readonly bool[] candTried = new bool[kMaxCandidates];
    uint rng;

    enum Landing { None, Open, Perch }

    public CreatureCommand Current { get; private set; } = CreatureCommand.Stand;
    public string Doing { get; private set; } = kStanding;
    // WalkTo's point as given
    public Vector3 Point { get; private set; }
    // the circle (Circle: valid once it is being flown)
    public Vector3 CircleCentre { get; private set; }
    public float CircleRadius { get; private set; }
    public float CircleAltitude { get; private set; }
    // Perch: it lands (or stays) on open ground instead - no perch in reach, or (PerchGaveUp) kPerchFailsMax attempts at
    // the perch timed out (the landing autopilot did not get it down there)
    public bool NoPerch { get; private set; }
    public bool PerchGaveUp { get; private set; }
    // Perch landings that touched down off the perch since the last command (each a failed attempt; tests, debug)
    public int PerchMisses { get; private set; }
    // On the ground on the perch it was landed on (checked under the girdle at the touchdown and every ground tick after;
    // cleared by a take-off or by walking off it)
    public bool OnPerch { get; private set; }
    // WalkTo reached its point (Current has turned to Stand); cleared by the next command
    public bool Arrived => arrived;

    // RED hook (tests): ignore the transitions - walk commands return the walk's target even while airborne
    internal bool ignoreTransitions;
    // test hooks: a perch landing asked for this far (m) short of the perch along its final (a touchdown beside the rock);
    // a perch that is never lined up on (unreachable: every attempt times out)
    internal float perchMissBy;
    internal bool perchUnreachable;
    internal int perchFailsMax = kPerchFailsMax;
    internal bool ignoreMisses;   // RED hook: a touchdown beside the perch is not counted as a failed attempt
    internal int PerchTimeouts { get; private set; }   // (tests) perch attempts that timed out on the line-up
    internal bool keepStaleOnPerch;
    internal bool orbitLoiter;   // RED hook: loitering turns for the middle from anywhere (it orbited it, never landing)
    internal bool noHopDown;   // RED hook: a walk command on a high perch walks off the edge   // RED hook: OnPerch not re-checked on the ground (stale after walking off)   // (tests: more attempts)

    Landing landing;         // what this brain's own landing in progress is for
    float landingLeft;       // its timeout
    int perchFails;
    bool scanBegun, scanDone;   // Perch: the scan for perches (once per command, spread over frames)
    int scanCells, scanNext, scanStart, scanStride, scanHalf, candCount;
    float scanReach, scanTop, scanStep;
    bool arrived;            // WalkTo reached its point (now Stand)
    float faceLeft = -1f;    // taking off: time left turning toward a clear launch run (< 0: not started)
    Vector3 waypoint;
    float waypointLeft;
    float circleSide;        // +1 / -1: the circle's sense, kept once chosen
    bool circlePlanned;
    bool approachPlanned, approachStaged;   // Perch: a perch chosen; flying out to its staging point
    Vector3 approachPoint, stagePoint;
    bool looking;
    bool hopping;            // hopping down from a high perch to walk (take-off, landing on open ground)
    float perchTopH;         // the perch it is on: its top's height over the area's ground
    float hindLeg = -1f;     // the longest walking (non-wing) leg, m (this individual's)            // Perch in the air: the scan is still running this frame
    int approachSource;

    public CommandBrain(EnclosureArea area, int seed)
    {
        this.area = area;
        rng = (uint)seed * 2654435761u + 0x7F4A7C15u;
        if (rng == 0) rng = 2463534242u;
        wander = new WanderBrain(area, seed + 211);
    }

    // Give a command (point: WalkTo's target; ignored by the others). It takes effect on the next Target.
    public void Command(CreatureCommand c, Vector3 point = default)
    {
        Current = c; Point = point;
        hopping = false;
        perchFails = 0; PerchMisses = 0; PerchTimeouts = 0; scanBegun = false; scanDone = false; candCount = 0; NoPerch = false; PerchGaveUp = false; arrived = false;
        faceLeft = -1f; circlePlanned = false; waypointLeft = 0f; approachPlanned = false;
    }

    float Rand()
    {
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        return (rng & 0xFFFFFFu) / 16777216f;
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    public Vector3 Target(CreatureSim sim, float dt)
    {
        sim.Halt = false;
        CreatureFlight fl = sim.Flight;
        if (fl == null)
        {
            // a walker: the air commands stand
            if (Current == CreatureCommand.WalkAround || Current == CreatureCommand.WalkTo) return Walk(sim, null, dt);
            sim.Halt = true;
            Doing = Current == CreatureCommand.Stand || Current == CreatureCommand.Land ? (arrived ? kArrived : kStanding) : kCannotFly;
            return sim.Joints[0];
        }
        if (fl.Mode == FlightMode.Ground && !fl.Crouching)
        {
            // whatever landing it was is over; one meant for the perch that ended off it (beside the rock) is a failed
            // attempt: it takes off for another, up to kPerchFailsMax, then stays on the open ground it is on
            // (the flight's Perched only says the landing was on a perch source; whether it came down on the perch's top or
            // beside the rock is a probe down under the girdle)
            if (landing == Landing.Perch)
            {
                OnPerch = OnPerchSurface(sim, fl);
                perchTopH = Vector3.Dot(approachPoint - area.center, area.up);   // the perch's height over the area's ground
            }
            else if (landing == Landing.Open) OnPerch = false;
            // walked off the perch (a walk command, the Wander, a WalkTo): no longer on it - one probe a tick while it is
            else if (OnPerch && !keepStaleOnPerch) OnPerch = OnPerchSurface(sim, fl);
            hopping = false;   // (a hop down ends on the ground)
            if (landing == Landing.Perch && !OnPerch && !ignoreMisses)
            {
                PerchMisses++;
                if (++perchFails >= perchFailsMax) { NoPerch = true; PerchGaveUp = true; }
            }
            landing = Landing.None; approachPlanned = false;
        }
        switch (Current)
        {
            case CreatureCommand.Fly:
            case CreatureCommand.Circle: return AirCommand(sim, fl, dt);
            case CreatureCommand.Land:
            case CreatureCommand.Perch: return LandCommand(sim, fl, dt);
            default: return GroundCommand(sim, fl, dt);
        }
    }

    // ------------------------------------------------------------------ Stand, WalkAround, WalkTo

    Vector3 GroundCommand(CreatureSim sim, CreatureFlight fl, float dt)
    {
        Vector3 head = sim.Joints[0];
        if (ignoreTransitions) return Walk(sim, fl, dt);
        switch (fl.Mode)
        {
            case FlightMode.Ground:
                if (fl.Crouching) { Doing = hopping ? kHopping : Pick(kUpThenStand, kUpThenWalk, kUpThenWalkTo); return head; } // the vault comes: land after it
                // on a perch higher than its hind legs are long, a walk does not step off the edge (the walking body crept
                // and sank into a 2 m rock): it hops down - takes off and lands on open ground (near WalkTo's point), then
                // walks. Standing, it stays up there.
                // (at once: no turning on the perch toward a clear launch run - it cannot walk up there)
                if (OnPerch && Current != CreatureCommand.Stand && !noHopDown && perchTopH > kHopHeight * HindLegLength(sim))
                {
                    if (fl.Launch(Vector3.Dot(area.center, area.up) + 0.5f * Roof(sim))) { hopping = true; OnPerch = false; faceLeft = -1f; waypointLeft = 0f; }
                    Doing = kHopping;
                    return head;
                }
                return Walk(sim, fl, dt);
            case FlightMode.Launch:
                Doing = hopping ? kHopping : Pick(kUpThenStand, kUpThenWalk, kUpThenWalkTo);
                return head;
            case FlightMode.Touchdown:
                Doing = hopping ? kHopping : Pick(kDownToStand, kDownToWalk, kDownToWalkTo);
                return head;
            default:
                // airborne: land on open ground first (near the point for WalkTo)
                Doing = hopping ? kHopping : Pick(kLandToStand, kLandToWalk, kLandToWalkTo);
                KeepLanding(sim, fl, dt, Landing.Open);
                return head;
        }
    }

    string Pick(string stand, string walk, string walkTo) =>
        Current == CreatureCommand.Stand ? stand : Current == CreatureCommand.WalkAround ? walk : walkTo;

    Vector3 Walk(CreatureSim sim, CreatureFlight fl, float dt)
    {
        Vector3 head = sim.Joints[0];
        switch (Current)
        {
            case CreatureCommand.WalkAround:
                Doing = kWalking;
                return wander.Target(sim, dt);
            case CreatureCommand.WalkTo:
            {
                Vector3 goal = GroundPoint(fl, Point, sim.SizeScale);
                float arrive = Mathf.Max(kArriveMin * sim.SizeScale, kArrive * sim.Species.BodyLength * sim.SizeScale);
                Vector3 d = goal - head; d -= area.up * Vector3.Dot(d, area.up);
                if (d.magnitude > arrive) { Doing = kWalkingTo; return goal; }
                Current = CreatureCommand.Stand; arrived = true;   // arrived: stand there
                sim.Halt = true; Doing = kArrived;
                return head;
            }
            default:
                sim.Halt = true;
                Doing = arrived ? kArrived : OnPerch ? kPerchedStanding : kStanding;
                return head;
        }
    }

    // The point's ground projection: inside the safe disc, the first surface below it from the roof (the area's plane
    // without a flight's probe).
    Vector3 GroundPoint(CreatureFlight fl, Vector3 p, float s)
    {
        p = area.ClampToSafe(p);
        float top = area.maxHeight > 0f ? area.maxHeight : 10f * s;
        Vector3 from = p + area.up * (Vector3.Dot(area.center - p, area.up) + top);
        if (fl != null && fl.ProbeDown(from, out Vector3 hp, out _, out _)) return hp;
        return p - area.up * Vector3.Dot(p - area.center, area.up);
    }

    // ------------------------------------------------------------------ Fly, Circle

    Vector3 AirCommand(CreatureSim sim, CreatureFlight fl, float dt)
    {
        Vector3 head = sim.Joints[0];
        switch (fl.Mode)
        {
            case FlightMode.Ground:
                return TakeOff(sim, fl, dt, kTakingOff);
            case FlightMode.Launch:
                Doing = kTakingOff;
                return head;
            case FlightMode.Touchdown:
                Doing = kDownThenUp;   // Launch works from the ground only: up again as soon as it stands
                return head;
            default:
                if (fl.Landing) { fl.CancelLanding(); landing = Landing.None; }   // a landing in progress is called off
                if (Current == CreatureCommand.Circle) FlyCircle(sim, fl);
                else Patrol(sim, fl, dt);
                return head;
        }
    }

    // From the ground: turn toward the middle until the launch run is clear (at most kFaceTime), then Launch.
    Vector3 TakeOff(CreatureSim sim, CreatureFlight fl, float dt, string doing)
    {
        Vector3 head = sim.Joints[0];
        if (fl.Crouching) { Doing = doing; return head; }
        if (faceLeft < 0f) faceLeft = kFaceTime * sim.TimeScale;
        if (faceLeft > 0f && !LaunchClear(sim, fl))
        {
            faceLeft -= dt;
            Doing = kFacing;
            Vector3 mid = Vector3.ProjectOnPlane(area.center - head, area.up);
            mid = mid.sqrMagnitude > 1e-6f ? mid.normalized : Vector3.ProjectOnPlane(sim.Rotations[0] * Vector3.forward, area.up).normalized;
            return head + mid * (2f * sim.SizeScale);
        }
        float alt = Vector3.Dot(area.center, area.up) + Lerp(0.4f, 0.8f, Rand()) * Roof(sim);
        if (fl.Launch(alt)) { Doing = doing; faceLeft = -1f; waypointLeft = 0f; OnPerch = false; }
        return head;
    }

    float Roof(CreatureSim sim) => area.maxHeight > 0f ? area.maxHeight : 10f * sim.SizeScale;

    float Ceiling(CreatureSim sim) => Vector3.Dot(area.center, area.up) + (area.maxHeight > 0f ? 0.9f * area.maxHeight : float.MaxValue);

    // The tightest level turn at cruise (bank at maxBankDeg): the circle's and the waypoints' margin.
    static float CruiseTurnRadius(CreatureSim sim)
    {
        float v = sim.Species.flight.cruiseSpeed * sim.TimeScale;
        return v * v / (FlightModel.G * Mathf.Tan(sim.Species.flight.maxBankDeg * Mathf.Deg2Rad));
    }

    // The launch's straight run (about a second at launch speed) and then the tightest turn toward the middle fit
    // inside the safe disc; with obstacle avoidance on, the run and the air beyond it are clear (FlightBrain.LaunchClear).
    bool LaunchClear(CreatureSim sim, CreatureFlight fl)
    {
        Vector3 head = sim.Joints[0];
        Vector3 fwd = Vector3.ProjectOnPlane(sim.Rotations[0] * Vector3.forward, area.up).normalized;
        float T = sim.TimeScale;
        float R = CruiseTurnRadius(sim);
        Vector3 p = head + fwd * (sim.Species.flight.launchSpeed * T * 1.2f);
        Vector3 right = Vector3.Cross(area.up, fwd);
        float d = Mathf.Min(area.PlanarDistance(p + right * R), area.PlanarDistance(p - right * R));
        if (d + R >= area.safeRadius) return false;
        if (!fl.Avoidance.Enabled) return true;
        Vector3 g = sim.Joints[fl.Girdle];
        fwd = Vector3.ProjectOnPlane(sim.Rotations[fl.Girdle] * Vector3.forward, area.up).normalized;
        p = head + fwd * (sim.Species.flight.launchSpeed * T * 1.2f);
        Vector3 far = p + fwd * (kLaunchLook * T);
        return fl.LegClear(g, p + area.up * (Vector3.Dot(g - p, area.up) + 1f * sim.SizeScale))
               && fl.LegClear(p + area.up * (Vector3.Dot(g - p, area.up) + 1f * sim.SizeScale), far + area.up * (Vector3.Dot(g - far, area.up) + 1.5f * sim.SizeScale));
    }

    // Fly: between random waypoints inside the safe disc (by kWallMargin turn radii), at their heights; the wall turn
    // last (FlightBrain's patrol, without its manoeuvres).
    void Patrol(CreatureSim sim, CreatureFlight fl, float dt)
    {
        Doing = kFlying;
        FlightModel m = fl.Model;
        float s = sim.SizeScale, T = sim.TimeScale;
        float R = CruiseTurnRadius(sim);
        waypointLeft -= dt;
        if (waypointLeft <= 0f || Vector3.ProjectOnPlane(waypoint - m.Position, area.up).magnitude < R)
        {
            waypointLeft = Lerp(kWaypointMin, kWaypointMax, Rand()) * T;
            Vector3 t0 = Vector3.Cross(area.up, Mathf.Abs(area.up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
            Vector3 t1 = Vector3.Cross(area.up, t0);
            float minA = sim.Species.flight.minAltitude * s, roof = Roof(sim);
            float r = Mathf.Max(0f, area.safeRadius - kWallMargin * R) * Mathf.Sqrt(Rand());
            float a = 2f * Mathf.PI * Rand();
            float h = Lerp(Mathf.Max(minA * 1.5f, 0.35f * roof), 0.85f * roof, Rand());
            waypoint = area.center + t0 * (r * Mathf.Cos(a)) + t1 * (r * Mathf.Sin(a)) + area.up * h;
        }
        float cruise = sim.Species.flight.cruiseSpeed * T;
        float alt = Vector3.Dot(waypoint, area.up), turn = fl.TurnToward(waypoint);
        Vector3? toward = waypoint;
        if (fl.WallTurn(out float wall)) { turn = wall; toward = null; }
        fl.SetGoals(Mathf.Min(alt, Ceiling(sim)), cruise, turn, toward);
    }

    // Circle: round the area's middle at a fixed altitude, turn rate v / r plus a pull onto the circle (FlightBrain's
    // soar, without the sink); the sense is the one it is already turning in.
    void FlyCircle(CreatureSim sim, CreatureFlight fl)
    {
        Doing = kCircling;
        FlightModel m = fl.Model;
        float s = sim.SizeScale, T = sim.TimeScale;
        Vector3 up = area.up;
        float R = CruiseTurnRadius(sim);
        if (!circlePlanned)
        {
            circlePlanned = true;
            CircleCentre = area.center;
            CircleRadius = Mathf.Clamp(0.6f * area.safeRadius, 1.2f * R, Mathf.Max(1.2f * R, area.safeRadius - kWallMargin * R));
            CircleAltitude = Vector3.Dot(area.center, up) + Mathf.Max(kCircleAlt * Roof(sim), 2f * sim.Species.flight.minAltitude * s);
            CircleAltitude = Mathf.Min(CircleAltitude, Ceiling(sim));
            Vector3 rel0 = Vector3.ProjectOnPlane(m.Position - CircleCentre, up);
            circleSide = rel0.sqrMagnitude < 1e-6f || Vector3.Dot(Vector3.Cross(up, rel0), m.Forward) >= 0f ? 1f : -1f;
        }
        Vector3 rel = Vector3.ProjectOnPlane(m.Position - CircleCentre, up);
        float v = Mathf.Max(m.HorizontalSpeed, 1e-3f);
        float wMax = FlightModel.G * Mathf.Tan(sim.Species.flight.maxBankDeg * Mathf.Deg2Rad) / v;
        float turn = Mathf.Clamp(circleSide * (v / CircleRadius + 0.3f / T * (rel.magnitude - CircleRadius) / CircleRadius), -wMax, wMax);
        if (fl.WallTurn(out float wall)) turn = wall;
        fl.SetGoals(CircleAltitude, kCircleSpeed * sim.Species.flight.cruiseSpeed * T, turn);
    }

    // ------------------------------------------------------------------ Land, Perch

    Vector3 LandCommand(CreatureSim sim, CreatureFlight fl, float dt)
    {
        Vector3 head = sim.Joints[0];
        bool perch = Current == CreatureCommand.Perch;
        switch (fl.Mode)
        {
            case FlightMode.Ground:
                if (fl.Crouching) { Doing = perch ? (NoPerch ? kNoPerchUp : kUpToPerch) : kUpThenLand; return head; }
                if (perch && !OnPerch && !NoPerch)
                {
                    // on the ground, not on a perch: is there one to fly to? (scanned once per command, over a few frames)
                    if (!scanBegun) { scanBegun = true; BeginScan(sim); }
                    if (!StepScan(sim, fl)) { sim.Halt = true; Doing = kLooking; return head; }
                    if (candCount == 0) NoPerch = true;
                    if (!NoPerch) return TakeOff(sim, fl, dt, kUpToPerch);
                }
                sim.Halt = true;
                Doing = OnPerch ? kPerchedStanding : perch ? (PerchGaveUp ? kGaveUpLanded : kNoPerchLanded) : kLanded;
                return head;
            case FlightMode.Launch:
                Doing = perch ? (NoPerch ? kNoPerchUp : kUpToPerch) : kUpThenLand;
                return head;
            case FlightMode.Touchdown:
                Doing = perch ? (landing == Landing.Perch ? kDownToPerch : PerchGaveUp ? kGaveUpDown : kNoPerchDown) : kTouchingDown;
                return head;
            default:
                KeepLanding(sim, fl, dt, perch && !NoPerch ? Landing.Perch : Landing.Open);
                Doing = perch ? (looking ? kLooking : !NoPerch ? kToPerch : PerchGaveUp ? kGaveUpLanding : kNoPerchLanding) : kLanding;
                return head;
        }
    }

    // Airborne: make sure this brain's landing of the wanted kind is under way. The brain flies the approach itself up to
    // the final and asks for the landing (Land, no axis: the autopilot's glide slope, flare and touchdown) only where the
    // autopilot's final starts at once - the spot ahead (within kLineUpDeg) and more than flareDistance + kFinalRadii turn
    // radii away; when the autopilot falls back to its own entry (a final that drifted off, a go-around) the brain takes
    // over again. Open ground: a spot straight ahead is chosen as it comes (OpenAhead). A perch is a fixed spot, lined
    // up on (LineUp); one not touched down on within kPerchTimeout is given up for another, after kPerchFailsMax for open
    // ground (NoPerch set); no perch in reach: open ground, NoPerch set.
    // (Why not the autopilot's own approach: its teardrop entry rarely lined up in an aviary a few turn radii across (the
    // flown turn is ~1.7 x the ideal radius), and a planned final - Land with an axis - turns in only once the flyer is
    // above the approach height, which a flyer whose altitude hold sags below its goal never is: Vesper sags 2-3 m at
    // scale 4 and flew the racetrack's lane for minutes.)
    void KeepLanding(CreatureSim sim, CreatureFlight fl, float dt, Landing want)
    {
        landingLeft -= dt;
        looking = false;
        if (fl.Landing)
        {
            if (landing == want && (fl.OnFinal || fl.Mode == FlightMode.Flare))
            {
                if (landingLeft > 0f || fl.Mode == FlightMode.Flare) return;   // on the final (a flare runs to its end)
            }
            fl.CancelLanding();   // another kind, the autopilot's own entry, or timed out on the final: the brain's again
            landing = Landing.None;
        }
        if (want == Landing.Perch)
        {
            if (approachPlanned && landingLeft <= 0f)
            {
                // this perch took too long: another (kPerchFailsMax tries, then open ground)
                approachPlanned = false;
                PerchTimeouts++;
                if (++perchFails >= perchFailsMax) { NoPerch = true; PerchGaveUp = true; }
            }
            if (!NoPerch && !approachPlanned)
            {
                if (!scanBegun) { scanBegun = true; BeginScan(sim); }
                if (!StepScan(sim, fl)) { Loiter(sim, fl); looking = true; return; }   // still looking (a few frames)
                if (!PlanPerch(sim, fl)) NoPerch = true;
            }
            if (!NoPerch) { LineUp(sim, fl); return; }
        }
        OpenAhead(sim, fl);
    }

    // Open ground ahead: of points straight ahead and a little to either side (kAheadDeg), between the final's run and
    // two cruise turn radii beyond it, the first (WalkTo: the one nearest its point) inside the safe disc by kWallMargin
    // turn radii, off the obstacles, on up-facing terrain (a probe down; not a rock top) with the leg to it clear: the
    // landing is asked for there. None (heading for the wall): steer for the middle until there is.
    void OpenAhead(CreatureSim sim, CreatureFlight fl)
    {
        FlightModel m = fl.Model;
        Vector3 up = area.up;
        float s = sim.SizeScale, T = sim.TimeScale, R = CruiseTurnRadius(sim);
        float run = sim.Species.flight.flareDistance * s + kFinalRadii * fl.TurnRadius;
        float inner = Mathf.Max(0f, area.safeRadius - kWallMargin * R);
        Vector3 fwd = Vector3.ProjectOnPlane(m.Forward, up).normalized;
        bool walkTo = Current == CreatureCommand.WalkTo;
        Vector3 goal = walkTo ? GroundPoint(fl, Point, s) : default;
        Vector3 best = default; float bestScore = float.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            float d = run + (0.25f + 0.85f * i) * R;
            for (int j = -1; j <= 1; j++)
            {
                Vector3 p = m.Position + Quaternion.AngleAxis(j * kAheadDeg, up) * fwd * d;
                p -= up * Vector3.Dot(p - area.center, up);
                if (area.PlanarDistance(p) > inner || area.ObstacleAt(p) >= 0) continue;
                float score = walkTo ? Vector3.ProjectOnPlane(p - goal, up).sqrMagnitude : i * 3 + Mathf.Abs(j);
                if (score >= bestScore) continue;
                if (!fl.ProbeDown(p + up * (2f * s), out Vector3 hp, out Vector3 hn, out int src)) continue;
                if (src != 0 || Vector3.Dot(hn, up) < 0.7f) continue;
                if (!fl.LegClear(m.Position, hp + up * (1f * s))) continue;
                best = hp; bestScore = score;
            }
            if (!walkTo && bestScore < float.MaxValue) break;
        }
        if (bestScore < float.MaxValue)
        {
            fl.Land(best, 0);
            landing = Landing.Open;
            landingLeft = kLandingTimeout * T;
            return;
        }
        Loiter(sim, fl);   // none ahead: for the middle until there is
    }

    // Flying on for the middle at the height it has (a little over the approach height at least; within kLoiterStraight
    // cruise turn radii of the middle straight on), the wall turn last.
    void Loiter(CreatureSim sim, CreatureFlight fl)
    {
        FlightModel m = fl.Model;
        Vector3 up = area.up;
        float alt = Mathf.Max(m.Altitude, Vector3.Dot(area.center, up) + kApproachH * sim.SizeScale);
        float turn = fl.TurnToward(area.center);
        Vector3? toward = area.center;
        // (near the middle it flies straight on: turning for the middle from there at its flown turn radius (~1.6 cruise
        // radii) it orbited the middle, and no open-ground spot was ever straight ahead inside the inner disc - from the
        // air, Land never landed on 20 of 24 seeds; on a straight chord through the middle one comes up)
        if (!orbitLoiter && area.PlanarDistance(m.Position) < kLoiterStraight * CruiseTurnRadius(sim)) { turn = 0f; toward = null; }
        if (fl.WallTurn(out float wall)) { turn = wall; toward = null; }
        fl.SetGoals(Mathf.Min(alt, Ceiling(sim)), sim.Species.flight.cruiseSpeed * sim.TimeScale, turn, toward);
    }

    // The perch: the top of the nearest reachable perch surface (the scan, BeginScan / StepScan), aimed at its middle - the mean of the
    // scan's points on that surface, when a probe there confirms it (a landing aimed at the edge nearest the flyer
    // overshot or fell short of the top); with avoidance on, one a clear final into exists (FlightBrain's check). False:
    // none.
    bool PlanPerch(CreatureSim sim, CreatureFlight fl)
    {
        int n = candCount;
        for (int i = 0; i < n; i++) candTried[i] = false;
        float s = sim.SizeScale;
        Vector3 at = fl.Model.Position;
        for (int k = 0; k < kApproachTries; k++)
        {
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (candTried[i]) continue;
                float d = Vector3.ProjectOnPlane(candPoint[i] - at, area.up).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0) return false;
            int src = candSource[best];
            Vector3 mean = Vector3.zero; int m = 0;
            for (int i = 0; i < n; i++) if (candSource[i] == src) { mean += candPoint[i]; m++; candTried[i] = true; }
            mean /= m;
            Vector3 p = candPoint[best];
            if (fl.ProbeDown(mean + area.up * (Vector3.Dot(p - mean, area.up) + 0.5f * s), out Vector3 hp, out Vector3 hn, out int hs)
                && hs == src && Vector3.Dot(hn, area.up) >= 0.7f && Room(fl, hp, s, out _)) p = hp;
            if (fl.Avoidance.Enabled && !ApproachHeading(sim, fl, p, out _)) continue;
            approachPoint = p; approachSource = src; approachPlanned = true; approachStaged = false;
            landingLeft = kPerchTimeout * sim.TimeScale;
            return true;
        }
        return false;
    }

    // Lining up on the perch: the landing is asked for (Land, no axis) only from where the autopilot's final starts at
    // once (above). Until then the flyer is steered at the approach height: straight for the perch while it is far
    // enough, else via a staging point on a line into the perch, flareDistance + kFinalRadii + 1 turn radii out: of
    // kStageDirs lines, the one whose staging point lies inside the safe disc by kWallMargin turn radii and that the flyer
    // reaches flying most nearly along the line (it then turns in with little to turn).
    void LineUp(CreatureSim sim, CreatureFlight fl)
    {
        FlightModel m = fl.Model;
        Vector3 up = area.up;
        float s = sim.SizeScale, T = sim.TimeScale, R = CruiseTurnRadius(sim);
        float run = sim.Species.flight.flareDistance * s + kFinalRadii * fl.TurnRadius;
        Vector3 to = Vector3.ProjectOnPlane(approachPoint - m.Position, up);
        float dist = to.magnitude;
        float yaw = dist > 1e-3f ? Vector3.Angle(m.Forward, to / dist) : 180f;
        if (yaw < kLineUpDeg && dist > run && !perchUnreachable)
        {
            // (no axis: an axis makes it a planned final, which Vesper's sagging altitude hold never turned in on; the
            // perch keeps its avoidance tube along this approach heading with FlightSpec.perchTubeOnApproach)
            Vector3 at = approachPoint;
            if (perchMissBy > 0f) at = GroundPoint(fl, approachPoint - to / dist * perchMissBy, s);
            fl.Land(at, approachSource);
            landing = Landing.Perch;
            return;
        }
        if (approachStaged)
        {
            if (Vector3.ProjectOnPlane(stagePoint - m.Position, up).magnitude < R) approachStaged = false;   // there: turn in
        }
        else if (dist < run || yaw > 90f)
        {
            approachStaged = true;
            float inner = Mathf.Max(0f, area.safeRadius - kWallMargin * R);
            Vector3 fwd = Vector3.ProjectOnPlane(m.Forward, up).normalized;
            Vector3 t0 = Vector3.Cross(up, Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
            Vector3 t1 = Vector3.Cross(up, t0);
            float bestScore = float.MinValue;
            stagePoint = area.center;
            for (int k = 0; k < kStageDirs; k++)
            {
                float a = 2f * Mathf.PI * k / kStageDirs;
                Vector3 h = t0 * Mathf.Cos(a) + t1 * Mathf.Sin(a);   // the final's heading
                Vector3 st = approachPoint - h * (run + R);
                st -= up * Vector3.Dot(st - area.center, up);
                if (area.PlanarDistance(st) > inner) continue;
                Vector3 go = Vector3.ProjectOnPlane(st - m.Position, up);
                float gl = go.magnitude;
                if (gl < 1e-3f) continue;
                go /= gl;
                // along the line on arrival, and no sharp turn now; a staging point too near to turn for costs
                float score = Vector3.Dot(go, h) + 0.5f * Vector3.Dot(go, fwd) - (gl < 2f * R ? 1f : 0f);
                if (score > bestScore) { bestScore = score; stagePoint = st; }
            }
        }
        Vector3 aim = approachStaged ? stagePoint : approachPoint;
        float alt = Vector3.Dot(approachPoint, up) + kApproachH * s;
        float turn = fl.TurnToward(aim);
        Vector3? toward = aim;
        if (fl.WallTurn(out float wall)) { turn = wall; toward = null; }
        fl.SetGoals(Mathf.Min(alt, Ceiling(sim)), sim.Species.flight.cruiseSpeed * T, turn, toward);
    }

    // Perch candidates: probes down from the roof over the area - each obstacle's centre, then a grid over the safe disc
    // less a cruise turn radius (the final needs room) - keeping up-facing non-terrain tops with room for the body along
    // them (Room, FlightBrain's), into cand* (at most kMaxCandidates). The scan is spread over frames (kScanProbes probes
    // down a frame at most: on real colliders each is a raycast) and walks the grid's cells in a scrambled order (a
    // random start, a stride coprime with the count), so a full candidate list is not all from one corner.
    // BeginScan, then StepScan each frame until it returns true.
    void BeginScan(CreatureSim sim)
    {
        float s = sim.SizeScale, R = CruiseTurnRadius(sim);
        scanReach = Mathf.Max(0f, area.safeRadius - R);
        scanTop = Vector3.Dot(area.center, area.up) + Roof(sim);
        scanStep = Mathf.Max(0.5f * s, scanReach / 12f);
        scanHalf = Mathf.CeilToInt(scanReach / scanStep);
        int side = 2 * scanHalf + 1;
        scanCells = area.ObstacleCount + side * side;
        scanStart = (int)(Rand() * scanCells);
        scanStride = 1;
        for (int k = 0; k < kStrides.Length; k++)
            if (Gcd(kStrides[k] % scanCells, scanCells) == 1) { scanStride = kStrides[k] % scanCells; break; }
        scanNext = 0; candCount = 0; scanDone = false;
    }
    static readonly int[] kStrides = { 7919, 104729, 1299709, 15485863 };
    static int Gcd(int a, int b) { while (b != 0) { int t = a % b; a = b; b = t; } return a; }

    bool StepScan(CreatureSim sim, CreatureFlight fl)
    {
        if (scanDone) return true;
        Vector3 t0 = Vector3.Cross(area.up, Mathf.Abs(area.up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
        Vector3 t1 = Vector3.Cross(area.up, t0);
        int side = 2 * scanHalf + 1, budget = kScanProbes;
        while (budget > 0 && scanNext < scanCells && candCount < kMaxCandidates)
        {
            int c = (int)((scanStart + (long)scanNext * scanStride) % scanCells);
            scanNext++;
            Vector3 q;
            if (c < area.ObstacleCount) q = area.obstacleCenters[c];
            else
            {
                int g = c - area.ObstacleCount, i = g / side - scanHalf, j = g % side - scanHalf;
                q = area.center + t0 * (i * scanStep) + t1 * (j * scanStep);
            }
            budget -= Consider(sim, fl, q, scanTop, scanReach, ref candCount);
        }
        scanDone = scanNext >= scanCells || candCount >= kMaxCandidates;
        return scanDone;
    }

    // One cell: the probes down it cost (0 outside the reach; 1, or 5 with Room's).
    int Consider(CreatureSim sim, CreatureFlight fl, Vector3 q, float top, float reach, ref int n)
    {
        if (area.PlanarDistance(q) > reach) return 0;
        float s = sim.SizeScale;
        Vector3 from = q + area.up * (top - Vector3.Dot(q, area.up));
        if (!fl.ProbeDown(from, out Vector3 hp, out Vector3 hn, out int src)) return 1;
        if (src == 0 || Vector3.Dot(hn, area.up) < 0.7f) return 1;
        if (Room(fl, hp, s, out _)) { candPoint[n] = hp; candSource[n] = src; n++; }
        return 5;
    }

    // The longest walking leg (not a wing arm), m at the individual's size (a perch higher than kHopHeight x this is hopped
    // down from).
    float HindLegLength(CreatureSim sim)
    {
        if (hindLeg >= 0f) return hindLeg;
        CreatureSpecies sp = sim.Species;
        float best = 0f;
        for (int l = 0; l < sp.legs.Length; l++)
        {
            bool wing = false;
            for (int w = 0; w < sp.WingCount && !wing; w++) wing = sp.wings[w].leg == l;
            if (!wing) best = Mathf.Max(best, sp.legs[l].TotalLength);
        }
        return hindLeg = best * sim.SizeScale;
    }

    // Down on the perch: the first surface under the girdle is the perch's (its source).
    bool OnPerchSurface(CreatureSim sim, CreatureFlight fl)
    {
        Vector3 g = sim.Joints[fl.Girdle];
        return fl.ProbeDown(g + area.up * (0.5f * sim.SizeScale), out _, out _, out int src) && src == approachSource && src != 0;
    }

    // Room for the body on a found perch: along one of two directions the surface continues at about the same height on
    // both sides (FlightBrain.Room).
    bool Room(CreatureFlight fl, Vector3 at, float s, out Vector3 axis)
    {
        axis = default;
        Vector3 t0 = Vector3.Cross(area.up, Mathf.Abs(area.up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
        Vector3 t1 = Vector3.Cross(area.up, t0);
        float step = 0.3f * s, tol = 0.15f * s;
        for (int k = 0; k < 2; k++)
        {
            Vector3 d = k == 0 ? t0 : t1;
            bool ok = true;
            for (int sgn = -1; sgn <= 1 && ok; sgn += 2)
            {
                Vector3 q = at + d * (sgn * step) + area.up * (0.4f * s);
                ok = fl.ProbeDown(q, out Vector3 qp, out _, out int qs) && qs != 0 && Mathf.Abs(Vector3.Dot(qp - at, area.up)) < tol;
            }
            if (ok) { axis = d; return true; }
        }
        return false;
    }

    // A clear final into `at` (avoidance on): of eight headings, from the current one turning out both ways, the first
    // whose planned final can be flown (FlightBrain.ApproachHeading).
    bool ApproachHeading(CreatureSim sim, CreatureFlight fl, Vector3 at, out Vector3 heading)
    {
        heading = default;
        float s = sim.SizeScale;
        Vector3 up = area.up;
        Vector3 h0 = Vector3.ProjectOnPlane(fl.Model.Forward, up).normalized;
        Vector3 top = at + up * (sim.Species.joints[fl.Girdle].clearance * s + 0.3f * s);
        for (int k = 0; k < 8; k++)
        {
            float a = 45f * ((k + 1) / 2) * ((k & 1) == 0 ? 1f : -1f);
            Vector3 hd = Quaternion.AngleAxis(a, up) * h0;
            if (!FinalClear(sim, fl, at, hd, top)) continue;
            heading = hd;
            return true;
        }
        return false;
    }

    // The planned final into `at` along `hd`: its holding circle inside the safe disc and clear at the approach height,
    // the lane and the final clear for the swept span (FlightBrain.FinalClear).
    bool FinalClear(CreatureSim sim, CreatureFlight fl, Vector3 at, Vector3 hd, Vector3 top)
    {
        Vector3 up = area.up;
        fl.PlanHold(at, hd, out Vector3 entry, out Vector3 centre, out float radius, out _);
        float h = Vector3.Dot(at, up) + kApproachH * sim.SizeScale;
        entry += up * (h - Vector3.Dot(entry, up));
        if (area.PlanarDistance(centre) + radius > area.safeRadius) return false;
        Vector3 lane = (centre - entry) * 2f;
        Vector3 abeam = at + lane + up * (h - Vector3.Dot(at, up));
        if (area.PlanarDistance(abeam) > area.safeRadius) return false;
        Vector3 low = -up * (kLaneLow * sim.SizeScale);
        return fl.LegClear(entry, top) && CircleClear(fl, centre, radius, h)
               && fl.LegClear(abeam, entry + lane) && fl.LegClear(abeam + low, entry + lane + low);
    }

    bool CircleClear(CreatureFlight fl, Vector3 centre, float rc, float altitude)
    {
        Vector3 up = area.up;
        Vector3 t0 = Vector3.Cross(up, Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
        Vector3 t1 = Vector3.Cross(up, t0);
        Vector3 c = centre + up * (altitude - Vector3.Dot(centre, up));
        Vector3 prev = c + t0 * rc;
        for (int k = 1; k <= kChords; k++)
        {
            float a = 2f * Mathf.PI * k / kChords;
            Vector3 q = c + (t0 * Mathf.Cos(a) + t1 * Mathf.Sin(a)) * rc;
            if (!fl.LegClear(prev, q)) return false;
            prev = q;
        }
        return true;
    }
}
