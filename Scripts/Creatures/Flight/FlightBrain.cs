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
    const float kWaypointMin = 4f, kWaypointMax = 8f;
    const float kSoarChance = 0.35f, kSoarMin = 6f, kSoarMax = 10f;
    const float kLandingTimeout = 45f;
    const float kAvoidEvery = 0.1f;
    const int kRegionProbes = 24;     // probes down into a perch region per attempt
    const float kWallMargin = 1.4f;   // waypoints and circles keep this many turn radii inside the safe radius

    readonly EnclosureArea area;
    readonly WanderBrain ground;
    readonly PerchSpot[] perches;
    uint rng;

    public State Current { get; private set; } = State.Rest;
    public int Flights { get; private set; }
    public int Perches { get; private set; }  // landings on a perch spot (not open ground)
    public PerchSpot LastPerch { get; private set; }

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
        if (!sp.CanFly) return false;
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
                if (LaunchClear(sim) || stateLeft <= 0f) { TakeOff(sim); return head; }
                return head + Vector3.ProjectOnPlane(area.center - head, area.up).normalized * (2f * sim.SizeScale);
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
                if (stateLeft <= 0f && fl.Airborne) { fl.CancelLanding(); BeginPatrol(sim); }
                return head;
        }

        // airborne (from the launch on): patrol or soar, then choose a perch
        if (!fl.Airborne && fl.Mode != FlightMode.Launch) return head;
        flightLeft -= dt; stateLeft -= dt;
        if (flightLeft <= 0f && !(fl.Twist != null && fl.Twist.Busy) && ChoosePerch(sim)) return head;
        if (stateLeft <= 0f)
        {
            if (Current == State.Patrol && Rand() < kSoarChance) { Current = State.Soar; stateLeft = Lerp(kSoarMin, kSoarMax, Rand()); }
            else BeginPatrol(sim);
        }
        Fly(sim, dt);
        return head;
    }

    // Take off now if the launch run clears the wall, else turn toward the middle first.
    Vector3 TryTakeOff(CreatureSim sim)
    {
        if (LaunchClear(sim)) TakeOff(sim);
        else { Current = State.Face; stateLeft = 4f * sim.TimeScale; }
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
        return d + R < area.safeRadius;
    }

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
        float r = Mathf.Max(0f, area.safeRadius - kWallMargin * R) * Mathf.Sqrt(Rand());
        float a = 2f * Mathf.PI * Rand();
        Vector3 t0 = Vector3.Cross(area.up, Mathf.Abs(area.up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
        Vector3 t1 = Vector3.Cross(area.up, t0);
        float minA = sim.Species.flight.minAltitude * sim.SizeScale;
        float h = Lerp(Mathf.Max(minA * 1.5f, 0.35f * area.maxHeight), 0.85f * area.maxHeight, Rand());
        waypoint = area.center + t0 * (r * Mathf.Cos(a)) + t1 * (r * Mathf.Sin(a)) + area.up * h;
    }

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
        if (Current == State.Soar)
        {
            // a glide circle round the middle that fits the safe disc: turn rate v / r, sinking slowly; a few
            // beats when it gets low (the controller's own floor)
            float rc = Mathf.Clamp(0.6f * area.safeRadius, R * 1.2f, Mathf.Max(R * 1.2f, area.safeRadius - kWallMargin * R));
            Vector3 c = area.center;
            Vector3 rel = Vector3.ProjectOnPlane(m.Position - c, up);
            float side = Vector3.Dot(Vector3.Cross(up, rel), m.Forward) >= 0f ? 1f : -1f; // keep the sense we fly in
            float v = Mathf.Max(m.HorizontalSpeed, 1e-3f);
            turn = side * (v / rc + 0.3f / T * (rel.magnitude - rc) / rc); // the circle, plus a pull onto it
            float floorAlt = Vector3.Dot(area.center, up) + Mathf.Max(2f * sim.Species.flight.minAltitude * sim.SizeScale, 0.3f * area.maxHeight);
            alt = Mathf.Max(floorAlt, m.Altitude - 1.5f * sim.SizeScale);
            speed = 0.85f * cruise;
        }
        else
        {
            alt = Vector3.Dot(waypoint, up);
            speed = cruise;
            turn = fl.TurnToward(waypoint);
            if (Vector3.ProjectOnPlane(waypoint - m.Position, up).magnitude < R) stateLeft = Mathf.Min(stateLeft, 0.5f);
        }
        // obstacles ahead (trunks): a cast every 0.1 s; climb and turn away while one is near
        avoidIn -= dt;
        if (avoidIn <= 0f && sim.Surface != null)
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
            turn = avoidTurn * FlightModel.G * Mathf.Tan(sim.Species.flight.maxBankDeg * Mathf.Deg2Rad) / v;
            alt = Mathf.Max(alt, m.Altitude + 1f * sim.SizeScale);
        }
        // a twisting flyer: a manoeuvre every so often, flown straight and level (the wall still wins)
        if (fl.Twist != null) Manoeuvre(sim, fl.Twist, dt, ref turn, ref alt);
        // the wall last: once the tightest turn toward the middle barely fits, take it
        if (fl.WallTurn(out float wall)) turn = wall;
        float ceiling = Vector3.Dot(area.center, up) + (area.maxHeight > 0f ? 0.9f * area.maxHeight : float.MaxValue);
        fl.SetGoals(Mathf.Min(alt, ceiling), speed, turn);
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
            bool spot = count > 0 && Rand() < kSpotChance;
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
                    for (int k = 0; k < kRegionProbes && !found; k++)
                    {
                        float rr = region * Mathf.Sqrt(Rand()), aa = 2f * Mathf.PI * Rand();
                        Vector3 q = p + (u0 * Mathf.Cos(aa) + u1 * Mathf.Sin(aa)) * rr;
                        if (area.PlanarDistance(q) > area.safeRadius - R) continue;
                        if (!fl.ProbeDown(q + area.up * (0.5f * sim.SizeScale), out hp, out hn, out hsrc)) continue;
                        found = hsrc != 0 && Vector3.Dot(hn, area.up) >= 0.7f
                                && Vector3.Dot(hp - area.center, area.up) >= 0.5f * sim.Species.flight.minAltitude * sim.SizeScale
                                && Room(fl, hp, sim.SizeScale, out axis);
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
                if (area.ObstacleAt(p) >= 0) continue; // open ground, not beside a trunk or rock
            }
            if (area.PlanarDistance(p) > area.safeRadius - R) continue;
            if (region <= 0f)
            {
                // confirm from above: the first surface below must face up and be where the spot says
                Vector3 from = p + area.up * (2f * sim.SizeScale);
                if (!fl.ProbeDown(from, out hp, out hn, out hsrc)) continue;
                if (Vector3.Dot(hn, area.up) < 0.7f) continue;
                if (spot && (hp - p).magnitude > 0.5f * sim.SizeScale) continue;
            }
            if (spot && region <= 0f && src != 0) Room(fl, hp, sim.SizeScale, out axis);
            fl.Land(hp, spot ? src : hsrc, axis);
            LastPerch = new PerchSpot { point = hp, source = spot ? src : hsrc };
            Current = State.Landing; stateLeft = kLandingTimeout * sim.TimeScale;
            return true;
        }
        flightLeft = 3f; // try again in a few seconds
        return false;
    }

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
