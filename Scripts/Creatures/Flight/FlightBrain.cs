using UnityEngine;

// A perch a flying creature may land on. radius 0: a known point on top of something (a rock top, a branch
// top) and what it is (0 = terrain, else the surface's source id). radius > 0: a region to search - a tree's
// crown (point = the crown's top centre) or a rock: the brain probes down from random points within it and
// takes the first up-facing non-terrain surface with room along it (a branch top), source as found.
public struct PerchSpot
{
    public Vector3 point;
    public int source;
    public float radius;
}

// The flying creature's day (design section 4): rest on a perch or the ground (4-15 s), sometimes a short
// ground wander (wing-walk), launch, climb to 0.4-0.8 of the aviary's height, then patrol between 3D
// waypoints or soar in glide circles (a few beats when low), and after a while choose a perch - one of the
// given perch spots or open ground, checked by a probe down - and land on it (the flight's landing
// autopilot flies the approach). Inside the aviary (EnclosureArea: safe radius, maxHeight): every waypoint and
// circle fits inside it by a turn radius, and a flyer near the wall turns back toward the middle. Ahead, a
// cast every 0.1 s finds obstacles (tree trunks): climb over and turn away.
// Allocation-free after construction; deterministic for a seed.
public sealed class FlightBrain : ICreatureBrain
{
    public enum State { Rest, Wander, Face, Patrol, Soar, Landing }

    const float kRestMin = 4f, kRestMax = 15f;
    // after a landing on open ground it usually wing-walks a while before the next take-off (user feedback
    // 2026-10-18: "I never saw it land, so I haven't seen its walk"); half the landings are on open ground
    const float kWanderChance = 0.75f, kWanderMin = 6f, kWanderMax = 12f;
    const float kFlightMin = 20f, kFlightMax = 40f;
    const float kFirstFlightMin = 10f, kFirstFlightMax = 18f; // the first flight is short: a landing soon after play starts
    const float kSpotChance = 0.5f;   // a perch spot (else open ground)
    const float kSpotChanceTrees = 0.7f;
    const float kLaneLow = 1.2f;      // m at scale 1: a racetrack lane is checked this far below the approach height too // ... with obstacle avoidance on
    const float kWaypointMin = 4f, kWaypointMax = 8f;
    const float kSoarChance = 0.35f, kSoarMin = 6f, kSoarMax = 10f;
    const float kLandingTimeout = 45f;
    const float kAvoidEvery = 0.1f;
    const int kRegionProbes = 24;     // probes down into a perch region per attempt (avoidance on: kRegionProbesWide)
    const int kRegionProbesWide = 64;
    const int kBranchTries = 6;       // avoidance on: branch tops fully checked (Branch) per attempt
    const float kRegionReach = 1.6f;  // avoidance on: a crown's region is searched out to this x its radius
    const float kWallMargin = 1.4f;   // waypoints and circles keep this many turn radii inside the safe radius

    readonly EnclosureArea area;
    readonly WanderBrain ground;
    readonly PerchSpot[] perches;
    uint rng;

    public State Current { get; private set; } = State.Rest;
    public int Flights { get; private set; }
    public int Perches { get; private set; }  // landings on a perch spot (not open ground)
    public PerchSpot LastPerch { get; private set; }
    // perch search stats (tests, debug view): branch tops probed in crowns, and those accepted by Branch()
    public int BranchTops { get; private set; }
    public int BranchesAccepted { get; private set; }
    public int LandingsAborted { get; private set; }
    public readonly int[] Rejected = new int[4]; // Branch() rejections: no axis, too thin, no room, approach blocked // landing timeouts (the approach never touched down)

    float stateLeft, flightLeft, avoidIn, avoidLeft, avoidTurn;
    // a twisting flyer (AirTwist: the dragon): time to the next manoeuvre, the altitude it is flown at
    float twistIn = -1f, twistAlt;
    Vector3 waypoint;
    float cruiseAlt;

    public FlightBrain(EnclosureArea area, int seed, PerchSpot[] perches = null)
    {
        this.area = area; this.perches = perches ?? new PerchSpot[0];
        rng = (uint)seed * 2246822519u + 0x165667B1u;
        if (rng == 0) rng = 2463534242u;
        ground = new WanderBrain(area, seed + 101);
        stateLeft = Lerp(kRestMin, kRestMax, Rand()) * 0.4f; // the first rest is short
    }

    float Rand()
    {
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        return (rng & 0xFFFFFFu) / 16777216f;
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // A flying species fits an aviary when it can turn inside it: a level turn at cruise (maxBankDeg) needs
    // radius v^2 / (g tan bank); the turn must fit in half the safe radius.
    public static bool FitsAviary(CreatureSpecies sp, EnclosureArea area, float sizeScale)
    {
        if (!sp.CanFly) return CreatureKit.Airborne.Of(sp) is CreatureKit.IAirborne air && air.FitsAviary(sp, area, sizeScale);
        float v = sp.flight.cruiseSpeed * Mathf.Sqrt(sizeScale);
        return v <= Mathf.Sqrt(FlightModel.G * Mathf.Tan(sp.flight.maxBankDeg * Mathf.Deg2Rad) * 0.5f * area.safeRadius)
               && area.maxHeight > 2f * sp.flight.minAltitude * sizeScale;
    }

    public Vector3 Target(CreatureSim sim, float dt)
    {
        CreatureFlight fl = sim.Flight;
        if (fl == null) return ground.Target(sim, dt);
        Vector3 head = sim.Joints[0];
        switch (Current)
        {
            case State.Rest:
                stateLeft -= dt;
                if (fl.Mode != FlightMode.Ground) return head;
                if (stateLeft > 0f) return head;
                if (!fl.Perched && Rand() < kWanderChance) { Current = State.Wander; stateLeft = Lerp(kWanderMin, kWanderMax, Rand()); return head; }
                return TryTakeOff(sim);
            case State.Face:
                // turn on the ground until the launch run points into the aviary (or give up after a while)
                stateLeft -= dt;
                if (LaunchClear(sim)) { TakeOff(sim); return head; }
                if (stateLeft <= 0f)
                {
                    // still blocked (bark beside the launch run with avoidance on): walk on a while first
                    if (fl.Avoidance.Enabled) { Current = State.Wander; stateLeft = Lerp(kWanderMin, kWanderMax, Rand()) * 0.5f; return head; }
                    TakeOff(sim); return head;
                }
                return head + FaceDirection(sim) * (2f * sim.SizeScale);
            case State.Wander:
                stateLeft -= dt;
                if (stateLeft <= 0f && fl.Mode == FlightMode.Ground) return TryTakeOff(sim);
                return ground.Target(sim, dt);
            case State.Landing:
                stateLeft -= dt;
                if (fl.Mode == FlightMode.Ground)
                {
                    if (fl.Perched) Perches++;
                    Current = State.Rest; stateLeft = Lerp(kRestMin, kRestMax, Rand());
                    return head;
                }
                if (stateLeft <= 0f && fl.Airborne) { fl.CancelLanding(); LandingsAborted++; BeginPatrol(sim); }
                return head;
        }

        // airborne (from the launch on): patrol or soar, then choose a perch
        if (!fl.Airborne && fl.Mode != FlightMode.Launch) return head;
        flightLeft -= dt; stateLeft -= dt;
        if (flightLeft <= 0f && !(fl.Twist != null && fl.Twist.Busy) && ChoosePerch(sim)) return head;
        if (stateLeft <= 0f)
        {
            if (Current == State.Patrol && Rand() < kSoarChance && PlanSoar(sim)) { Current = State.Soar; stateLeft = Lerp(kSoarMin, kSoarMax, Rand()); }
            else BeginPatrol(sim);
        }
        Fly(sim, dt);
        return head;
    }

    // Take off now if the launch run clears the wall, else turn toward the middle first.
    Vector3 TryTakeOff(CreatureSim sim)
    {
        if (LaunchClear(sim)) TakeOff(sim);
        else { Current = State.Face; stateLeft = 4f * sim.TimeScale; faceDirValid = false; }
        return sim.Joints[0];
    }

    // The launch's straight run (about a second at launch speed) and then the tightest turn toward the middle
    // fit inside the safe disc.
    bool LaunchClear(CreatureSim sim)
    {
        Vector3 head = sim.Joints[0];
        Vector3 fwd = Vector3.ProjectOnPlane(sim.Rotations[0] * Vector3.forward, area.up).normalized;
        float T = sim.TimeScale;
        float v = sim.Species.flight.cruiseSpeed * T;
        float R = v * v / (FlightModel.G * Mathf.Tan(sim.Species.flight.maxBankDeg * Mathf.Deg2Rad));
        Vector3 p = head + fwd * (sim.Species.flight.launchSpeed * T * 1.2f);
        Vector3 right = Vector3.Cross(area.up, fwd);
        float d = Mathf.Min(area.PlanarDistance(p + right * R), area.PlanarDistance(p - right * R));
        if (d + R >= area.safeRadius) return false;
        // avoidance on: the spread wings' sweep along the launch run is clear of bark (a launch beside a trunk
        // swept a wing through it before the avoidance could act)
        CreatureFlight fl = sim.Flight;
        if (!fl.Avoidance.Enabled) return true;
        Vector3 g = sim.Joints[fl.Girdle];
        // (along the body's heading at the girdle - the launch's - not the head's, which turns first while facing)
        fwd = Vector3.ProjectOnPlane(sim.Rotations[fl.Girdle] * Vector3.forward, area.up).normalized;
        p = head + fwd * (sim.Species.flight.launchSpeed * T * 1.2f);
        // ... and on past it for kLaunchLook (the low, slow climb-out cannot turn away in time once airborne)
        Vector3 far = p + fwd * (kLaunchLook * T);
        return fl.LegClear(g, p + area.up * (Vector3.Dot(g - p, area.up) + 1f * sim.SizeScale))
               && fl.LegClear(p + area.up * (Vector3.Dot(g - p, area.up) + 1f * sim.SizeScale), far + area.up * (Vector3.Dot(g - far, area.up) + 1.5f * sim.SizeScale));
    }
    const float kLaunchLook = 6f; // m at scale 1 (x sqrt scale) of clear air beyond the launch run

    // Face: the heading to turn to before a launch - toward the middle, or (avoidance on) the first of eight
    // headings, from the current one outward, whose launch run is clear.
    Vector3 FaceDirection(CreatureSim sim)
    {
        Vector3 head = sim.Joints[0];
        Vector3 mid = Vector3.ProjectOnPlane(area.center - head, area.up).normalized;
        CreatureFlight fl = sim.Flight;
        if (!fl.Avoidance.Enabled) return mid;
        if (faceDirValid) return faceDir;
        faceDirValid = true; faceDir = mid;
        Vector3 g = sim.Joints[fl.Girdle];
        float T = sim.TimeScale;
        for (int k = 0; k < 8; k++)
        {
            Vector3 d = Quaternion.AngleAxis(45f * ((k + 1) / 2) * ((k & 1) == 0 ? 1f : -1f), area.up) * mid;
            Vector3 a = g + area.up * (1f * sim.SizeScale), b = a + d * ((sim.Species.flight.launchSpeed * 1.2f + kLaunchLook) * T);
            if (area.PlanarDistance(b) < area.safeRadius && fl.LegClear(a, b + area.up * (0.5f * sim.SizeScale))) { faceDir = d; break; }
        }
        return faceDir;
    }
    Vector3 faceDir; bool faceDirValid;

    void TakeOff(CreatureSim sim)
    {
        cruiseAlt = Vector3.Dot(area.center, area.up) + Lerp(0.4f, 0.8f, Rand()) * area.maxHeight;
        if (!sim.Flight.Launch(cruiseAlt)) return;
        Flights++;
        flightLeft = Flights == 1 ? Lerp(kFirstFlightMin, kFirstFlightMax, Rand()) : Lerp(kFlightMin, kFlightMax, Rand());
        BeginPatrol(sim);
    }

    void BeginPatrol(CreatureSim sim)
    {
        Current = State.Patrol;
        stateLeft = Lerp(kWaypointMin, kWaypointMax, Rand());
        float R = sim.Flight.TurnRadius;
        Vector3 t0 = Vector3.Cross(area.up, Mathf.Abs(area.up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
        Vector3 t1 = Vector3.Cross(area.up, t0);
        float minA = sim.Species.flight.minAltitude * sim.SizeScale;
        // with obstacle avoidance on, a waypoint whose straight leg from here runs into a tree is redrawn (a few
        // tries; the avoidance flies round whatever is left)
        int tries = sim.Flight.Avoidance.Enabled ? kWaypointTries : 1;
        for (int k = 0; k < tries; k++)
        {
            float r = Mathf.Max(0f, area.safeRadius - kWallMargin * R) * Mathf.Sqrt(Rand());
            float a = 2f * Mathf.PI * Rand();
            float h = Lerp(Mathf.Max(Mathf.Max(minA * 1.5f, 0.35f * area.maxHeight), CrownFloor(sim)), 0.85f * area.maxHeight, Rand());
            waypoint = area.center + t0 * (r * Mathf.Cos(a)) + t1 * (r * Mathf.Sin(a)) + area.up * h;
            if (tries == 1 || sim.Flight.LegClear(sim.Flight.Model.Position, waypoint)) break;
        }
    }

    // The soar circle (round the middle, sinking to a floor): by default 0.6 of the safe radius round the
    // middle, down to max(2 minAltitude, 0.3 maxHeight). With obstacle avoidance on, the circle is the one the
    // flyer is already on (tangent to its heading, either side, a few radii) that fits the safe disc and is
    // clear of trees at the floor and at the height it starts from; then higher floors (over the crowns);
    // false = no clear circle (patrol instead).
    bool PlanSoar(CreatureSim sim)
    {
        CreatureFlight fl = sim.Flight;
        float R = fl.TurnRadius;
        Vector3 up = area.up;
        float baseH = Vector3.Dot(area.center, up);
        float floor0 = Mathf.Max(Mathf.Max(2f * sim.Species.flight.minAltitude * sim.SizeScale, 0.3f * area.maxHeight), CrownFloor(sim));
        float rLo = R * 1.2f, rHi = Mathf.Max(R * 1.2f, area.safeRadius - kWallMargin * R);
        soarRadius = Mathf.Clamp(0.6f * area.safeRadius, rLo, rHi);
        soarFloor = baseH + floor0;
        soarCentre = area.center;
        if (!fl.Avoidance.Enabled) return true;
        float now = fl.Model.Altitude;
        Vector3 p = fl.Model.Position, right = fl.Model.Right;
        float fit = area.safeRadius - kWallMargin * 0.5f * R;
        int casts0 = fl.Avoidance.Casts;
        for (int fi = 0; fi < kSoarFloors.Length && fl.Avoidance.Casts - casts0 < kSoarCastBudget; fi++)
        {
            float fh = baseH + Mathf.Max(floor0, kSoarFloors[fi] * area.maxHeight);
            if (fi > 0 && fh <= baseH + floor0) continue;
            for (int ri = 0; ri < kSoarRadii.Length; ri++)
            {
                float rc = Mathf.Clamp(kSoarRadii[ri] * area.safeRadius, rLo, rHi);
                for (int side = 1; side >= -1; side -= 2)
                {
                    Vector3 c = p + right * (side * rc);
                    c -= up * Vector3.Dot(c - area.center, up);
                    if (area.PlanarDistance(c) + rc > fit) continue;
                    // the circle at the floor and where it starts sinking from (both bound the sinking spiral)
                    if (CircleClear(fl, c, rc, fh) && CircleClear(fl, c, rc, Mathf.Max(fh, now)))
                    {
                        soarRadius = rc; soarFloor = fh; soarCentre = c;
                        return true;
                    }
                }
            }
        }
        return false;
    }

    // Avoidance on: cruise (patrol waypoints, soar floors) keeps above the crowns of the perch regions (their tops
    // plus kCrownClear: the downstroke's reach), when that is still under 0.85 of the roof; the flyer goes down
    // among the trees only to land. Over the ground's base height; 0 = no floor.
    float CrownFloor(CreatureSim sim)
    {
        if (!sim.Flight.Avoidance.Enabled) return 0f;
        float top = 0f, baseH = Vector3.Dot(area.center, area.up);
        foreach (PerchSpot ps in perches)
            if (ps.radius > 0f) top = Mathf.Max(top, Vector3.Dot(ps.point, area.up) - baseH + kCrownClear * sim.SizeScale);
        return top < 0.85f * area.maxHeight ? top : 0f;
    }
    const float kCrownClear = 1.4f; // m at scale 1

    bool CircleClear(CreatureFlight fl, Vector3 centre, float rc, float altitude)
    {
        Vector3 up = area.up;
        Vector3 t0 = Vector3.Cross(up, Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
        Vector3 t1 = Vector3.Cross(up, t0);
        Vector3 c = centre + up * (altitude - Vector3.Dot(centre, up));
        Vector3 prev = c + t0 * rc;
        for (int k = 1; k <= kSoarChords; k++)
        {
            float a = 2f * Mathf.PI * k / kSoarChords;
            Vector3 q = c + (t0 * Mathf.Cos(a) + t1 * Mathf.Sin(a)) * rc;
            if (!fl.LegClear(prev, q)) return false;
            prev = q;
        }
        return true;
    }

    float soarRadius, soarFloor;
    Vector3 soarCentre;
    const int kWaypointTries = 8, kSoarChords = 12;
    const int kSoarCastBudget = 400;  // rays one soar plan may cast (a frame's worth); over it: patrol instead
    static readonly float[] kSoarRadii = { 0.6f, 0.35f, 0.8f, 0.2f };
    static readonly float[] kSoarFloors = { 0f, 0.6f, 0.75f };

    // Steering in the air: toward the waypoint (patrol) or round a circle (soar), turned back from the wall,
    // over and away from an obstacle ahead.
    void Fly(CreatureSim sim, float dt)
    {
        CreatureFlight fl = sim.Flight;
        FlightModel m = fl.Model;
        float T = sim.TimeScale;
        float cruise = sim.Species.flight.cruiseSpeed * T;
        float R = fl.TurnRadius;
        Vector3 up = area.up;
        float alt, speed, turn;
        Vector3? toward = null;
        if (Current == State.Soar)
        {
            // a glide circle round the middle that fits the safe disc: turn rate v / r, sinking slowly; a few
            // beats when it gets low (the controller's own floor)
            float rc = fl.Avoidance.Enabled ? soarRadius : Mathf.Clamp(0.6f * area.safeRadius, R * 1.2f, Mathf.Max(R * 1.2f, area.safeRadius - kWallMargin * R));
            Vector3 c = fl.Avoidance.Enabled ? soarCentre : area.center;
            Vector3 rel = Vector3.ProjectOnPlane(m.Position - c, up);
            float side = Vector3.Dot(Vector3.Cross(up, rel), m.Forward) >= 0f ? 1f : -1f; // keep the sense we fly in
            float v = Mathf.Max(m.HorizontalSpeed, 1e-3f);
            turn = side * (v / rc + 0.3f / T * (rel.magnitude - rc) / rc); // the circle, plus a pull onto it
            float floorAlt = fl.Avoidance.Enabled ? soarFloor : Vector3.Dot(area.center, up) + Mathf.Max(2f * sim.Species.flight.minAltitude * sim.SizeScale, 0.3f * area.maxHeight);
            alt = Mathf.Max(floorAlt, m.Altitude - 1.5f * sim.SizeScale);
            speed = 0.85f * cruise;
        }
        else
        {
            alt = Vector3.Dot(waypoint, up);
            speed = cruise;
            turn = fl.TurnToward(waypoint);
            toward = waypoint;
            if (Vector3.ProjectOnPlane(waypoint - m.Position, up).magnitude < R) stateLeft = Mathf.Min(stateLeft, 0.5f);
        }
        // obstacles ahead (trunks): a cast every 0.1 s; climb and turn away while one is near
        avoidIn -= dt;
        if (avoidIn <= 0f && sim.Surface != null && !fl.Avoidance.Enabled) // FlightAvoidance replaces this single ray
        {
            avoidIn = kAvoidEvery;
            float look = 3f * R;
            if (sim.Surface.Cast(m.Position, m.Forward, look, out SurfaceHit hit) && hit.source != 0 && Vector3.Dot(hit.normal, up) < 0.7f)
            {
                avoidLeft = 0.6f * T;
                avoidTurn = Vector3.Dot(Vector3.Cross(up, m.Forward), hit.normal) >= 0f ? 1f : -1f;
            }
        }
        if (avoidLeft > 0f)
        {
            avoidLeft -= dt;
            float v = Mathf.Max(m.HorizontalSpeed, 1e-3f);
            toward = null;
            turn = avoidTurn * FlightModel.G * Mathf.Tan(sim.Species.flight.maxBankDeg * Mathf.Deg2Rad) / v;
            alt = Mathf.Max(alt, m.Altitude + 1f * sim.SizeScale);
        }
        // a twisting flyer: a manoeuvre every so often, flown straight and level (the wall still wins)
        if (fl.Twist != null) Manoeuvre(sim, fl.Twist, dt, ref turn, ref alt);
        // the wall last: once the tightest turn toward the middle barely fits, take it
        if (fl.WallTurn(out float wall)) { turn = wall; toward = null; }
        float ceiling = Vector3.Dot(area.center, up) + (area.maxHeight > 0f ? 0.9f * area.maxHeight : float.MaxValue);
        fl.SetGoals(Mathf.Min(alt, ceiling), speed, turn, toward);
    }

    // Corkscrews, barrel rolls, loops and slow rolls (AirTwist), one every intervalMin..intervalMax seconds
    // (Froude-scaled) while patrolling or soaring, when the room it needs is there (AirTwist.Fits: roof,
    // ground, the head's excursion inside the safe disc); else it tries again a second later. The altitude is
    // held through it; the turns go on (a corkscrew round a soaring circle).
    static readonly TwistManoeuvre[] kManoeuvres = { TwistManoeuvre.Corkscrew, TwistManoeuvre.BarrelRoll, TwistManoeuvre.Loop, TwistManoeuvre.SlowRoll };
    static readonly float[] kManoeuvreWeights = { 0.35f, 0.25f, 0.2f, 0.2f };

    void Manoeuvre(CreatureSim sim, AirTwist tw, float dt, ref float turn, ref float alt)
    {
        FlightModel m = sim.Flight.Model;
        float T = sim.TimeScale;
        AirTwistSpec spec = tw.Spec;
        if (twistIn < 0f) twistIn = Lerp(spec.IntervalMin, spec.IntervalMax, Rand()) * T;
        if (tw.Busy) { alt = twistAlt; return; }
        twistIn -= dt;
        if (twistIn > 0f || sim.Flight.Landing) return;
        float total = 0f;
        for (int k = 0; k < kManoeuvres.Length; k++) if (tw.Available(kManoeuvres[k])) total += kManoeuvreWeights[k];
        if (total <= 0f) { twistIn = float.MaxValue; return; }
        float r = Rand() * total;
        TwistManoeuvre pick = TwistManoeuvre.None;
        for (int k = 0; k < kManoeuvres.Length && pick == TwistManoeuvre.None; k++)
        {
            if (!tw.Available(kManoeuvres[k])) continue;
            r -= kManoeuvreWeights[k];
            if (r <= 0f || k == kManoeuvres.Length - 1) pick = kManoeuvres[k];
        }
        if (pick == TwistManoeuvre.None) pick = TwistManoeuvre.SlowRoll;
        float side = Rand() < 0.5f ? -1f : 1f;
        float speed = m.HorizontalSpeed;
        if (tw.Fits(pick, m.Position, m.Forward, speed, sim.Flight.HeightAboveGround, sim.Flight.TurnRadius)
            && tw.Begin(pick, speed, m.Forward, area.up, side))
        {
            twistAlt = m.Altitude;
            alt = twistAlt;
            twistIn = Lerp(spec.IntervalMin, spec.IntervalMax, Rand()) * T;
        }
        else twistIn = 1f * T;
    }

    // A perch spot (or open ground) that a probe down confirms: up-facing, inside the safe disc by a turn
    // radius (the final needs room). False: none found, keep flying.
    bool ChoosePerch(CreatureSim sim)
    {
        CreatureFlight fl = sim.Flight;
        float R = fl.TurnRadius;
        int count = perches.Length;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            Vector3 p; int src = 0; float region = 0f;
            // (avoidance on, the flyer can perch in trees: it prefers them)
            bool spot = count > 0 && Rand() < (fl.Avoidance.Enabled ? kSpotChanceTrees : kSpotChance);
            Vector3 hp = default, hn, axis = default; int hsrc = 0;
            if (spot)
            {
                PerchSpot s = perches[Mathf.Min(count - 1, (int)(Rand() * count))];
                p = s.point; src = s.source; region = s.radius;
                if (region > 0f)
                {
                    // a region (a crown): a few probes down from random points in it, the first good one wins
                    bool found = false;
                    Vector3 u0 = Vector3.Cross(area.up, Mathf.Abs(area.up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
                    Vector3 u1 = Vector3.Cross(area.up, u0);
                    // with avoidance on: limbs reach past the crown's region (their outer parts are where a final
                    // can come in clear), and at most kBranchTries candidates get the full check per attempt
                    float reach = fl.Avoidance.Enabled ? kRegionReach * region : region;
                    int branchTries = 0;
                    int probes = fl.Avoidance.Enabled ? kRegionProbesWide : kRegionProbes;
                    for (int k = 0; k < probes && !found && branchTries < kBranchTries; k++)
                    {
                        float rr = reach * Mathf.Sqrt(Rand()), aa = 2f * Mathf.PI * Rand();
                        Vector3 q = p + (u0 * Mathf.Cos(aa) + u1 * Mathf.Sin(aa)) * rr;
                        // (avoidance on: a branch top only has to be inside the safe disc - its planned final's
                        // racetrack is checked to fit, FinalClear; a tree by the aviary's wall has no limb a turn
                        // radius inside it)
                        if (area.PlanarDistance(q) > area.safeRadius - (fl.Avoidance.Enabled ? 0f : R)) continue;
                        if (!fl.ProbeDown(q + area.up * (0.5f * sim.SizeScale), out hp, out hn, out hsrc)) continue;
                        found = hsrc != 0 && Vector3.Dot(hn, area.up) >= 0.7f
                                && Vector3.Dot(hp - area.center, area.up) >= 0.5f * sim.Species.flight.minAltitude * sim.SizeScale;
                        if (found && fl.Avoidance.Enabled)
                        {
                            BranchTops++; branchTries++;
                            regionCentre = p;
                            found = Branch(sim, hp, hsrc, out axis);
                            if (found) { BranchesAccepted++; hp = crest; }
                        }
                        else if (found) found = Room(fl, hp, sim.SizeScale, out axis);
                    }
                    if (!found) continue;
                    src = hsrc;
                    p = hp;
                }
            }
            else
            {
                float r = Mathf.Max(0f, area.safeRadius - kWallMargin * R) * Mathf.Sqrt(Rand());
                float a = 2f * Mathf.PI * Rand();
                Vector3 t0 = Vector3.Cross(area.up, Mathf.Abs(area.up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
                p = area.center + (t0 * Mathf.Cos(a) + Vector3.Cross(area.up, t0) * Mathf.Sin(a)) * r;
                // open ground, not beside a trunk or rock (avoidance on: a wing span and two turn radii clear -
                // the approach's low lanes swept past the trunk otherwise)
                float keep = fl.Avoidance.Enabled ? (FlightAvoidance.HalfSpan(sim.Species) + 2f * fl.ApproachRadius / sim.SizeScale) * sim.SizeScale : 0f;
                if (area.ObstacleAt(p, keep) >= 0) continue;
            }
            if (area.PlanarDistance(p) > area.safeRadius - (spot && region > 0f && fl.Avoidance.Enabled ? 0f : R)) continue;
            if (region <= 0f)
            {
                // confirm from above: the first surface below must face up and be where the spot says
                Vector3 from = p + area.up * (2f * sim.SizeScale);
                if (!fl.ProbeDown(from, out hp, out hn, out hsrc)) continue;
                if (Vector3.Dot(hn, area.up) < 0.7f) continue;
                if (spot && (hp - p).magnitude > 0.5f * sim.SizeScale) continue;
            }
            if (spot && region <= 0f && src != 0) Room(fl, hp, sim.SizeScale, out axis);
            // with avoidance on, every landing not planned by Branch() gets a final whose corridor is clear (the
            // ground beside a tree, a rock top); none: another spot
            if (fl.Avoidance.Enabled && !(spot && region > 0f) && !ApproachHeading(sim, hp, out axis)) continue;
            fl.Land(hp, spot ? src : hsrc, axis);
            LastPerch = new PerchSpot { point = hp, source = spot ? src : hsrc };
            Current = State.Landing; stateLeft = kLandingTimeout * sim.TimeScale;
            return true;
        }
        flightLeft = 3f; // try again in a few seconds
        return false;
    }

    // A perch on real geometry with obstacle avoidance on (a branch top, or a rock's): `heading` is the
    // direction the final flies along it, or false.
    //   axis       of six directions through the point, the one along which probes down 0.3 m either side
    //              stay on the same surface at about the same height (a branch's run; no steeper than 35 deg);
    //   thickness  probes 0.04 m either side across it: the curvature radius >= FlightSpec.perchMinRadius;
    //   room       the torso lies behind the girdle (0.9 m along the branch on the side the final comes from)
    //              and the head 0.3 m ahead of it;
    //   corridor   the final (from 2 m above, flareDistance + 2 turn radii back) and 0.7 m of head room past
    //              the point are clear for the whole swept span, the branch itself excepted.
    // Either heading along the axis may win (the clear one).
    bool Branch(CreatureSim sim, Vector3 at, int src, out Vector3 heading)
    {
        heading = default; crest = at;
        CreatureFlight fl = sim.Flight;
        float s = sim.SizeScale;
        Vector3 up = area.up;
        Vector3 t0 = Vector3.Cross(up, Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
        Crest(fl, ref at, src, s);
        float h0 = Vector3.Dot(at, up);
        float d = kAxisProbe * s, slope = Mathf.Tan(kMaxPerchSlopeDeg * Mathf.Deg2Rad);
        int bestK = -1; float bestErr = float.MaxValue;
        for (int k = 0; k < 6; k++)
        {
            Vector3 dir = Quaternion.AngleAxis(30f * k, up) * t0;
            if (!OnPerch(fl, at, dir * d, src, h0, slope * d, s, out float e1) || !OnPerch(fl, at, -dir * d, src, h0, slope * d, s, out float e2)) continue;
            if (e1 + e2 < bestErr) { bestErr = e1 + e2; bestK = k; }
        }
        if (bestK < 0) { Rejected[0]++; return false; }
        Vector3 axis = Quaternion.AngleAxis(30f * bestK, up) * t0;
        float rise = 0f; // the branch's rise along +axis (tan of its slope)
        // refine: the crest half a metre either way along the coarse axis gives the branch's own direction
        Vector3 fa = at + axis * (kAxisRefine * s), fb = at - axis * (kAxisRefine * s);
        if (Crest(fl, ref fa, src, s) && Crest(fl, ref fb, src, s))
        {
            Vector3 run = Vector3.ProjectOnPlane(fa - fb, up);
            if (run.sqrMagnitude > 1e-6f) { axis = run.normalized; rise = Vector3.Dot(fa - fb, up) / run.magnitude; }
        }
        // a steep branch is no perch: the final comes down along it (from the side where it is higher)
        if (Mathf.Abs(rise) > Mathf.Tan(kMaxPerchRiseDeg * Mathf.Deg2Rad)) { Rejected[2]++; return false; }
        Vector3 across = Vector3.Cross(up, axis);
        // thickness: the drop a little way either side gives the curvature radius r = (w^2 + drop^2) / 2 drop
        float w = kThickProbe * s, drop = 0f;
        for (int sg = -1; sg <= 1; sg += 2)
        {
            if (!fl.ProbeDown(at + across * (sg * w) + up * (0.3f * s), out Vector3 q, out _, out int qs) || qs != src) { Rejected[1]++; return false; }
            drop = Mathf.Max(drop, h0 - Vector3.Dot(q, up));
        }
        float radius = drop > 1e-5f ? (w * w + drop * drop) / (2f * drop) : float.MaxValue;
        if (radius < sim.Species.flight.perchMinRadius * s) { Rejected[1]++; return false; }
        float R = fl.ApproachRadius, flare = sim.Species.flight.flareDistance * s;
        // outward first: the final flies along the branch away from the crown's middle (the trunk), so the flare
        // and a go-round head out into the open; then the other way
        float outSign = Vector3.Dot(Vector3.ProjectOnPlane(at - regionCentre, up), axis) >= 0f ? 1f : -1f;
        for (int k = 0; k < 2; k++)
        {
            float sg = k == 0 ? outSign : -outSign;
            Vector3 hd = axis * sg;
            if (!OnPerch(fl, at, -hd * (kRoomBehind * s), src, h0, slope * kRoomBehind * s, s, out _)) { Rejected[2]++; continue; }
            if (!OnPerch(fl, at, hd * (kRoomAhead * s), src, h0, slope * kRoomAhead * s, s, out _)) { Rejected[2]++; continue; }
            fl.Avoidance.SetPerch(at, hd);
            Vector3 top = at + up * (sim.Species.joints[fl.Girdle].clearance * s + 0.3f * s);
            bool clear = FinalClear(sim, at, hd, top) && fl.LegClear(top, top + hd * (kHeadRoom * s));
            fl.Avoidance.ClearPerch();
            if (clear) { heading = (hd + up * (rise * sg)).normalized; crest = at; return true; }
            Rejected[3]++;
        }
        return false;
    }

    // Onto the top: a point on a branch's flank steps toward its crest (against the normal's horizontal part,
    // by that part x 0.1 m: exact on a 0.1 m branch) until the surface there faces up. False: the probe left
    // the surface.
    bool Crest(CreatureFlight fl, ref Vector3 at, int src, float s)
    {
        Vector3 up = area.up;
        for (int it = 0; it < kCrestIters; it++)
        {
            if (!fl.ProbeDown(at + up * (0.3f * s), out Vector3 cp, out Vector3 cn, out int cs) || cs != src) return false;
            at = cp;
            Vector3 nh = Vector3.ProjectOnPlane(cn, up);
            if (nh.magnitude < 0.1f) return true;
            at -= nh * (0.1f * s);
        }
        return true;
    }

    // A clear final into `at` (avoidance on): of eight headings, starting from the current one and turning
    // out both ways, the first whose entry (flareDistance + 2 approach turn radii back, 2 m up) is inside the
    // safe disc with room to turn there (a clear circle of a turn radius round it) and whose corridor down to
    // the point is clear for the swept span.
    bool ApproachHeading(CreatureSim sim, Vector3 at, out Vector3 heading)
    {
        heading = default;
        CreatureFlight fl = sim.Flight;
        float s = sim.SizeScale, R = fl.ApproachRadius, flare = sim.Species.flight.flareDistance * s;
        Vector3 up = area.up;
        Vector3 h0 = Vector3.ProjectOnPlane(fl.Model.Forward, up).normalized;
        Vector3 top = at + up * (sim.Species.joints[fl.Girdle].clearance * s + 0.3f * s);
        for (int k = 0; k < 8; k++)
        {
            float a = 45f * ((k + 1) / 2) * ((k & 1) == 0 ? 1f : -1f);
            Vector3 hd = Quaternion.AngleAxis(a, up) * h0;
            if (!FinalClear(sim, at, hd, top)) continue;
            heading = hd;
            return true;
        }
        Rejected[3]++;
        return false;
    }

    // The planned final into `at` along `hd` can be flown: its holding circle (CreatureFlight.PlanHold) lies
    // inside the safe disc and is clear at the approach height, and the final from the entry (2 m up) down to
    // `top` is clear for the swept span.
    bool FinalClear(CreatureSim sim, Vector3 at, Vector3 hd, Vector3 top)
    {
        CreatureFlight fl = sim.Flight;
        Vector3 up = area.up;
        fl.PlanHold(at, hd, out Vector3 entry, out Vector3 centre, out float radius, out _);
        float h = Vector3.Dot(at, up) + kApproachH * sim.SizeScale;
        entry += up * (h - Vector3.Dot(entry, up));
        if (area.PlanarDistance(centre) + radius > area.safeRadius) return false;
        // the racetrack's lane (beside the final, two radii over, from abeam the point back to the turn) too
        Vector3 lane = (centre - entry) * 2f;
        Vector3 abeam = at + lane + up * (h - Vector3.Dot(at, up));
        if (area.PlanarDistance(abeam) > area.safeRadius) return false;
        // (the lane also a little below the approach height: a slow climber flies it still climbing)
        Vector3 low = -up * (kLaneLow * sim.SizeScale);
        return fl.LegClear(entry, top) && CircleClear(fl, centre, radius, h)
               && fl.LegClear(abeam, entry + lane) && fl.LegClear(abeam + low, entry + lane + low);
    }

    // A probe down at `at + offset` finds the same surface within `tol` (+ a little) of height h0.
    bool OnPerch(CreatureFlight fl, Vector3 at, Vector3 offset, int src, float h0, float tol, float s, out float err)
    {
        err = 0f;
        if (!fl.ProbeDown(at + offset + area.up * (0.4f * s), out Vector3 q, out Vector3 n, out int qs) || qs != src) return false;
        err = Mathf.Abs(Vector3.Dot(q, area.up) - h0);
        return err <= tol + 0.02f * s && Vector3.Dot(n, area.up) >= 0.5f;
    }

    Vector3 crest;  // Branch(): the point moved onto the branch's crest
    Vector3 regionCentre; // Branch(): the perch region's centre (a crown's: over the trunk)
    const int kCrestIters = 4;
    const float kAxisRefine = 0.5f;
    const float kAxisProbe = 0.3f, kThickProbe = 0.04f, kMaxPerchSlopeDeg = 35f, kMaxPerchRiseDeg = 20f;
    const float kRoomBehind = 0.8f, kRoomAhead = 0.3f, kApproachH = 2f;
    const float kHeadRoom = 0.8f; // m at scale 1 past the point clear for the swept span (was 0.6: the flaring wings swept a trunk 0.95 m ahead)

    // Room for the body on a found perch: along one of two directions the surface continues at about the
    // same height on both sides (a branch's length, a rock's top).
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
}
