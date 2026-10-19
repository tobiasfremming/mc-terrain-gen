using System.Collections.Generic;
using UnityEngine;

// The octopus's brain (tree octopus design §3): it publishes an intent and a look, never places an arm.
//
// Perception: prey (registered IPreyTargets) within 4 L in a 270 deg cone ahead of the head, one line-of-sight
// cast per candidate every 0.2 s (the canopy occludes); last known position and velocity are kept.
// States:
//   CanopyRest  anchored, breathing, colour drifting; after RestMin-RestMax s -> Explore (user 2026-10-18: it
//               should visibly traverse the canopy - short rests, frequent trips)
//   Explore     Travel (swinging along a planned route, ArmCoordinator / BranchRoute) to a GOAL: a branch point
//               (a skeleton hint) in another tree's crown - or, now and then, a short hop to a branch nearby;
//               arrival (level distance) -> CanopyRest; prey seen -> Stalk
//   Stalk       Travel to 0.5-1.0 L above the prey's predicted path (low urgency), mottled camouflage with
//               passing clouds; while the viewer faces it (within 15 deg, 12 m): freeze + partial cloak 0.6;
//               prey within strike range -> Ambush
//   Ambush      0.25 s coil wind-up (arms tense), then Strike: 2-4 arms qualify themselves
//   Strike      until an arm grabs (-> Hold) or 0.6 s pass (miss -> Retreat)
//   Hold        HoldSeconds, then release (the arms let go) -> Retreat
//   Retreat     deimatic flash 0.3 s, cloak 1, Travel to the farthest high region at urgency 1; uncloak after
//               4-8 s of calm -> CanopyRest
//   Fallen      grounded and low: Travel to the nearest climbable region; arms anchor on the ground (crawl)
// With mouth tentacles (OctopusController.Feeder: the two-armed swinger) the hunt ends differently - the arms never
// strike:
//   Stalk       far (no hold within 1.2 CreepLengths L of the prey, level): Travel - swing closer; near: Stalk at
//               low urgency - creep, pulling itself up on the hold near the prey to just below it (CreepBelow L);
//               back to swinging only past 2 CreepLengths (hysteresis)
//   Feed        the prey is within the mouth's reach: Hold (the arms keep hanging on), intent.feed - the mouth
//               tentacles reach for it slowly; it gets away -> Stalk
//   Eat         a mouth tentacle has it: Hold until the feeder has eaten it (Eaten event) -> CanopyRest, sated for
//               SatedSeconds (no new hunt)
// Goals are clipped to the enclosure (centre, radius, height cap).
public sealed class OctopusBrain
{
    public enum State { CanopyRest, Explore, Stalk, Ambush, Strike, Hold, Retreat, Fallen, Feed, Eat }
    public const float CreepLengths = 0.5f, CreepBelow = 0.25f, FeedGiveUpSeconds = 8f, SatedSeconds = 20f;   // x arm length; x TimeScale
    public int Meals;                            // cumulative (mouth feeders)
    float satedUntil = -1f;
    bool creeping;

    public const float PerceptionSeconds = 0.2f;
    public const float ConeDeg = 270f;
    public const float WatchedAngleDeg = 15f, WatchedRange = 12f;
    // Watched has hysteresis and a minimum hold: it starts inside 15 deg / 12 m, ends only outside 25 deg /
    // 14 m, and neither state changes within WatchedHoldSeconds of the last change - a gaze wandering over the
    // cone's edge flipped it (and the freeze and the cloak with it) every few frames: the "glitching" cloak.
    public const float WatchedExitAngleDeg = 25f, WatchedExitRange = 14f, WatchedHoldSeconds = 2f;
    // The cloak: its target is held CloakHoldSeconds before it may change (a retreat's cloak is exempt: it is
    // the answer to a threat), and the value ramps at CloakRate per second (full range in ~0.5 s), never steps.
    public const float CloakHoldSeconds = 2f, CloakRate = 2f;

    public State Mode { get; private set; }
    public float StateTime { get; private set; }
    public float Cloak { get; private set; }
    // Off: Cloak stays exactly 0 whatever the state (the species' cephalopod.cloakEnabled, default off).
    public bool CloakEnabled;
    public float Chroma { get; private set; }
    public float Flash { get; private set; }
    public float CloudPhase { get; private set; }
    public bool Watched { get; private set; }
    public IPreyTarget Target { get; private set; }
    public Vector3 LastSeen { get; private set; }
    public Vector3 Goal => goal;                 // the current travel goal (debug view, lab readout)
    public int Trips, Arrivals;                  // cumulative
    public const float RestMin = 3f, RestMax = 8f, HopChance = 0.3f;
    public const float ArriveLevel = 0.6f, ArriveRise = 1.6f;   // x arm length: arrived at a goal on a branch above
    public int Sightings, StrikesStarted, GrabsMade, MissedStrikes; // cumulative

    // world knowledge (set by the owner)
    public readonly List<IPreyTarget> Prey = new List<IPreyTarget>();
    public readonly List<Vector3> CanopyRegions = new List<Vector3>();   // points high in climbable plants
    public Vector3 EnclosureCentre;
    public float EnclosureRadius = float.PositiveInfinity, HeightCap = float.PositiveInfinity;
    public bool ViewerValid;
    public Vector3 ViewerPosition, ViewerForward;
    public float RangeLengths = 4f;
    public float StrikeRangeLengths = 1.15f;

    uint rng;
    float nextPerception, nextExplore, calmUntil, lastSeenTime = -100f, strikeAt;
    float watchedChanged = -100f, cloakGoal, cloakGoalChanged = -100f, time;
    Vector3 lastVel, goal;
    bool seen;

    public OctopusBrain(uint seed = 1)
    {
        rng = seed * 747796405u + 2891336453u; if (rng == 0) rng = 7;
        Mode = State.CanopyRest;
        Chroma = 0.35f;
        nextExplore = RestMin + (RestMax - RestMin) * Rand();
    }

    float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }

    void Enter(State s) { Mode = s; StateTime = 0f; }

    public void Tick(float dt, OctopusController c)
    {
        StateTime += dt;
        time = c.Time;
        c.EnclosureCentre = EnclosureCentre; c.EnclosureRadius = EnclosureRadius;
        float L = c.Body.Arms[0].Length;
        Vector3 head = c.Body.HeadCentre;
        Perceive(c, head, L);
        UpdateWatched(head);

        OctoIntent it = c.Intent;
        it.prey = null; it.strikers = 0; it.climb = false;
        switch (Mode)
        {
            case State.CanopyRest:
                it.mode = OctoMode.Rest; it.goal = head; it.urgency = 0f;
                if (seen && Target != null && c.Time >= satedUntil) { Enter(State.Stalk); break; }
                if (IsFallen(c, head, L)) { Enter(State.Fallen); break; }
                if (c.Time > nextExplore && CanopyRegions.Count > 0)
                {
                    goal = PickGoal(c, head, L);
                    Trips++;
                    Enter(State.Explore);
                }
                break;
            case State.Explore:
                it.mode = OctoMode.Travel; it.goal = goal; it.urgency = 0.4f;
                if (seen && Target != null && c.Time >= satedUntil) { Enter(State.Stalk); break; }
                // (no route to it after NoRouteSeconds - the hints show no way - or the trip takes too long: rest,
                // then a new goal)
                bool noRoute = c.Hints != null && StateTime > NoRouteSeconds * c.Body.TimeScale && !c.Route.Valid;
                bool late = StateTime > ExploreSeconds * c.Body.TimeScale;
                if (Arrived(c, head, L) || noRoute || late)
                {
                    if (!noRoute && !late) Arrivals++;
                    nextExplore = c.Time + RestMin + (RestMax - RestMin) * Rand();
                    Enter(State.CanopyRest);
                }
                break;
            case State.Stalk when c.Feeder != null:
                StalkToFeed(c, ref it, head, L);
                break;
            case State.Feed:
                it.mode = OctoMode.Hold; it.goal = head; it.prey = Target; it.feed = true;
                if (Target == null || c.Feeder == null) { Target = null; Enter(State.CanopyRest); break; }
                // it got away (out of reach, or nothing caught in time): stalk again
                if (!c.Feeder.Feeding && StateTime > 0.3f && !c.Feeder.InReach(Target) || StateTime > FeedGiveUpSeconds * c.Body.TimeScale) Enter(State.Stalk);
                break;
            case State.Eat:
                it.mode = OctoMode.Hold; it.goal = head; it.prey = Target; it.feed = true;
                if (c.Feeder == null || !c.Feeder.Feeding && StateTime > 0.5f) { Target = null; Enter(State.CanopyRest); }
                break;
            case State.Stalk:
            {
                if (Target == null || c.Time - lastSeenTime > 8f) { Target = null; Enter(State.CanopyRest); break; }
                Vector3 predicted = LastSeen + lastVel * 1.5f;
                goal = Clip(predicted + c.Up * (0.75f * L), L);
                it.mode = Watched ? OctoMode.Rest : OctoMode.Stalk; it.goal = goal; it.urgency = 0.3f;
                if (!Watched && Target.CanBeGrabbed && (Target.Position - head).magnitude < StrikeRangeLengths * L) { Enter(State.Ambush); }
                break;
            }
            case State.Ambush:
                it.mode = OctoMode.Rest; it.goal = head;
                if (StateTime >= 0.25f && Target != null)
                {
                    StrikesStarted++;
                    strikeAt = c.Time;
                    c.Events.RaiseStrike(Target);
                    Enter(State.Strike);
                }
                break;
            case State.Strike:
            {
                if (Target == null) { Enter(State.Retreat); break; }
                // aim where the prey will be when the arm arrives (~0.15 s)
                it.mode = OctoMode.Strike; it.prey = Target;
                it.intercept = Target.Position + Target.Velocity * 0.15f;
                it.strikers = 3;
                if (StateTime > 0.6f) { MissedStrikes++; BeginRetreat(c, head, L); }
                break;
            }
            case State.Hold:
                it.mode = OctoMode.Hold; it.goal = head; it.prey = Target;
                if (!AnyArmHolding(c) && StateTime > 0.1f) BeginRetreat(c, head, L);
                break;
            case State.Retreat:
                it.mode = OctoMode.Retreat; it.goal = goal; it.urgency = 1f;
                if (seen) calmUntil = c.Time + 4f + 4f * Rand();
                if (c.Time > calmUntil && (head - goal).magnitude < 0.6f * L) { Target = null; Enter(State.CanopyRest); }
                if (StateTime > 40f) { Target = null; Enter(State.CanopyRest); }
                break;
            case State.Fallen:
            {
                // back up from the ground: hand over hand along the trunk (swinging needs something to hang from).
                // A region high in a crown is no goal to crawl to (the arms reached up into the air under it and
                // the body wandered about the ground until one happened onto the trunk): first the foot of the
                // nearest surface that rises (a trunk, a wall), crawling; then up it to the region's height.
                if (StateTime < dt * 1.5f) { region = NearestRegion(head, L); haveFoot = atFoot = false; nextFootSearch = 0f; }
                if (!haveFoot && c.Time >= nextFootSearch)
                {
                    haveFoot = FindRisingSurface(c, head, region, L, out footPoint, out footNormal);
                    nextFootSearch = c.Time + FootSearchSeconds * c.Body.TimeScale;
                }
                goal = region;
                if (haveFoot)
                {
                    Vector3 up = c.Up, off = footNormal - up * OctoMath.Dot(footNormal, up);
                    off = OctoMath.Norm(off, Vector3.zero) * (ClimbOffLengths * L);
                    Vector3 rel = footPoint - head;
                    float level = (rel - up * OctoMath.Dot(rel, up)).magnitude;
                    // at the foot (an arm's reach from the surface): up it, beside it, to the region's height
                    if (level < FootReachLengths * L) atFoot = true;
                    goal = atFoot ? footPoint + off + up * OctoMath.Dot(region - footPoint, up) : footPoint + off + up * (0.3f * L);
                }
                it.mode = OctoMode.Travel; it.goal = goal; it.urgency = 0.7f; it.climb = true;
                if (!c.Board.Grounded && c.Board.AnchoredCount >= 2 && (head - goal).magnitude < 0.6f * L && (!haveFoot || atFoot)) { Enter(State.CanopyRest); atFoot = false; }
                if (StateTime > 60f) { Enter(State.CanopyRest); atFoot = false; }
                break;
            }
        }
        c.Intent = it;
        UpdateLook(dt);
    }

    internal void OnGrab(IPreyTarget p) { GrabsMade++; Target = p; if (Mode == State.Strike) Enter(State.Hold); else if (Mode == State.Feed) Enter(State.Eat); }

    // The feeder has eaten it: rest, sated for a while.
    internal void OnEaten(IPreyTarget p)
    {
        Meals++;
        if (Target == p) Target = null;
        seen = false;
        satedUntil = time + SatedSeconds;
        nextExplore = time + RestMin + (RestMax - RestMin) * Rand();
        Enter(State.CanopyRest);
    }

    // Hunting with the mouth: swing close, creep the last bit to just below the prey, feed when it is in reach.
    void StalkToFeed(OctopusController c, ref OctoIntent it, Vector3 head, float L)
    {
        if (Target == null || c.Time - lastSeenTime > 8f) { Target = null; it.mode = OctoMode.Rest; it.goal = head; Enter(State.CanopyRest); return; }
        if (c.Feeder.InReach(Target))
        {
            it.mode = OctoMode.Hold; it.goal = head; it.prey = Target; it.feed = true;
            Enter(State.Feed);
            return;
        }
        Vector3 predicted = LastSeen + lastVel * 1f, up = c.Up;
        goal = Clip(predicted - up * (CreepBelow * L), L);
        // creep once a hold lies near below the prey (level distance): the body pulls itself up on it. The swinging
        // head passing close is not enough - from a hold a bar behind, a pull goes nowhere. (Hysteresis: swinging
        // close and creeping do not flip at the threshold.)
        float near = float.PositiveInfinity;
        for (int a = 0; a < c.Board.Arms; a++)
        {
            if (!c.Board.Anchored[a]) continue;
            Vector3 d = predicted - c.Board.Anchor[a];
            d -= up * OctoMath.Dot(d, up);
            near = Mathf.Min(near, d.magnitude);
        }
        bool far = near > (creeping ? 2f : 1.2f) * CreepLengths * L;
        creeping = !far;
        it.mode = far ? OctoMode.Travel : Watched ? OctoMode.Rest : OctoMode.Stalk;
        it.goal = goal;
        it.urgency = far ? 0.4f : 0.15f;
    }
    internal void OnRelease(IPreyTarget p) { }

    static bool AnyArmHolding(OctopusController c)
    {
        for (int a = 0; a < c.Agents.Length; a++) if (c.Agents[a].Holding) return true;
        return false;
    }

    void BeginRetreat(OctopusController c, Vector3 head, float L)
    {
        Flash = 1f;
        calmUntil = c.Time + 4f + 4f * Rand();
        goal = FarthestRegion(head, L);
        Enter(State.Retreat);
    }

    // On the ground below the canopy - or hanging far below it (a swing that caught holds low on a trunk left the
    // body hanging at full stretch under the crowns, out of reach of every branch and of every route: it gave up
    // trip after trip and never got back up). Either way it climbs back.
    public const float LowHangLengths = 1.2f;   // x arm length below the nearest canopy region
    bool IsFallen(OctopusController c, Vector3 head, float L)
    {
        if (CanopyRegions.Count == 0) return false;
        Vector3 n = NearestRegion(head, L);
        float below = Vector3.Dot(n - head, c.Up);
        // (Grounded is any upward-facing contact - an arm lying over a branch top counts: an octopus hanging in a
        // crown from holds above it is not on the ground. It climbed back from there, hand over hand, a few times a
        // minute: most of its role changes)
        return c.Board.Grounded && !HoldsAbove(c, head, L) ? below > 0.8f * L : below > LowHangLengths * L;
    }

    static bool HoldsAbove(OctopusController c, Vector3 head, float L)
    {
        int n = 0;
        for (int a = 0; a < c.Board.Arms; a++) if (c.Board.Anchored[a] && Vector3.Dot(c.Board.Anchor[a] - head, c.Up) > 0.3f * L) n++;
        return n >= c.Board.MinAnchorsHanging;
    }

    // ------------------------------------------------------------------ perception

    void Perceive(OctopusController c, Vector3 head, float L)
    {
        if (c.Time < nextPerception || !c.Spare(Prey.Count)) return;
        nextPerception = c.Time + PerceptionSeconds;
        Vector3 fwd = OctoMath.Rotate(c.Body.Orientation, Vector3.forward);
        float range = RangeLengths * L, cosHalf = Mathf.Cos(0.5f * ConeDeg * Mathf.Deg2Rad);
        IPreyTarget bestP = null; float bestD = float.MaxValue;
        for (int i = 0; i < Prey.Count; i++)
        {
            IPreyTarget p = Prey[i];
            if (p == null) continue;
            Vector3 d = p.Position - head;
            float dist = d.magnitude;
            if (dist > range || dist < 1e-4f) continue;
            // (the cone is for finding prey; the one it is already watching it keeps in view all round - the eyes
            // turn: a body rocking on its holds turned its blind spot onto the prey it stalked and lost it)
            if (p != Target && Vector3.Dot(d, fwd) < cosHalf * dist) continue;
            // one line of sight cast: the canopy (or anything) in between hides the prey
            if (c.Cast(head, d, dist - 0.05f * L, out SurfaceHit h)) continue;
            if (dist < bestD) { bestD = dist; bestP = p; }
        }
        seen = bestP != null;
        if (seen)
        {
            if (Target != bestP) Sightings++;
            Target = bestP;
            lastVel = bestP.Velocity;
            LastSeen = bestP.Position;
            lastSeenTime = c.Time;
        }
    }

    void UpdateWatched(Vector3 head)
    {
        bool raw;
        if (!ViewerValid) raw = false;
        else
        {
            Vector3 d = head - ViewerPosition;
            float dist = d.magnitude;
            float ang = dist < 1e-3f ? 180f : Vector3.Angle(ViewerForward, d);
            raw = Watched ? dist <= WatchedExitRange && ang <= WatchedExitAngleDeg
                          : dist <= WatchedRange && ang < WatchedAngleDeg;
        }
        if (raw == Watched || time - watchedChanged < WatchedHoldSeconds) return;
        Watched = raw;
        watchedChanged = time;
    }

    // ------------------------------------------------------------------ look

    void UpdateLook(float dt)
    {
        float cloakTarget = 0f, chromaTarget = 0.35f;
        switch (Mode)
        {
            case State.Stalk: chromaTarget = 0.85f; cloakTarget = Watched ? 0.6f : 0f; break;
            case State.Ambush: chromaTarget = 0.9f; break;
            case State.Strike: case State.Hold: chromaTarget = 0.6f; break;
            case State.Retreat: chromaTarget = 0.2f; cloakTarget = Flash > 0.05f ? 0f : 1f; break;
            case State.CanopyRest: chromaTarget = 0.3f + 0.25f * Mathf.Sin(0.13f * StateTime); break;
        }
        if (!CloakEnabled) cloakTarget = 0f;
        bool threat = Mode == State.Retreat;
        if (cloakTarget != cloakGoal && (threat || time - cloakGoalChanged >= CloakHoldSeconds)) { cloakGoal = cloakTarget; cloakGoalChanged = time; }
        Cloak = Mathf.MoveTowards(Cloak, cloakGoal, dt * CloakRate);
        Chroma = Mathf.MoveTowards(Chroma, chromaTarget, dt * 1.2f);
        Flash = Mathf.MoveTowards(Flash, 0f, dt / 0.3f);
        // passing clouds run faster when stalking
        CloudPhase += dt * (Mode == State.Stalk || Mode == State.Ambush ? 0.9f : 0.25f);
    }

    // ------------------------------------------------------------------ goals

    // The body hangs under its holds: a goal on a branch is reached when the body is under it (level distance),
    // not at it.
    bool Arrived(OctopusController c, Vector3 head, float L)
    {
        Vector3 d = goal - head, up = c.Up;
        float rise = OctoMath.Dot(d, up);
        d -= up * rise;
        return d.magnitude < ArriveLevel * L && Mathf.Abs(rise) < ArriveRise * L;
    }

    readonly BranchHint[] goalHints = new BranchHint[12];

    // A purposeful goal: another tree's crown (not the one it is in), or now and then a hop to a branch nearby;
    // snapped to a level branch point of the plant skeleton when there are hints (probe-only: the region).
    Vector3 PickGoal(OctopusController c, Vector3 head, float L)
    {
        // a hop that lands on no branch (open air beside the grove) is not a goal: try again, then a crown
        for (int attempt = 0; attempt < 4; attempt++)
            if (TryGoal(c, head, L, attempt < 3 && Rand() < HopChance, out Vector3 g)) return g;
        TryGoal(c, head, L, false, out Vector3 crown);
        return crown;
    }

    bool TryGoal(OctopusController c, Vector3 head, float L, bool hop, out Vector3 goalOut)
    {
        Vector3 up = c.Up;
        Vector3 pick = head;
        if (hop)
        {
            float a = Rand() * 2f * Mathf.PI, r = (1.5f + 1.5f * Rand()) * L;
            Vector3 t0 = OctoMath.Norm(OctoMath.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right), Vector3.right), t1 = OctoMath.Cross(up, t0);
            pick = head + (t0 * Mathf.Cos(a) + t1 * Mathf.Sin(a)) * r + up * (0.8f * L);
        }
        else
        {
            // a canopy region away from where it is (the nearest is the tree it is in)
            int bi = -1; float bs = float.NegativeInfinity;
            for (int k = 0; k < 4; k++)
            {
                int i = (int)(Rand() * CanopyRegions.Count) % CanopyRegions.Count;
                Vector3 d = CanopyRegions[i] - head; d -= up * OctoMath.Dot(d, up);
                float s = Mathf.Min(d.magnitude, 6f * L) + Rand() * L;
                if (s > bs) { bs = s; bi = i; }
            }
            pick = CanopyRegions[bi];
        }
        pick = Clip(pick, L);
        goalOut = pick;
        if (c.Hints == null) return true;
        // the level branch point nearest the pick (a stem is no place to hang under)
        int n = c.Hints.Query(pick, up, -1f, 0f, (hop ? 0.8f : 1.5f) * L, c.Body.Spec.arm.TipRadius * c.Body.SizeScale * 1.5f, 2f * L, goalHints);
        for (int i = 0; i < n; i++)
            if (Mathf.Abs(OctoMath.Dot(goalHints[i].axis, up)) <= BranchRoute.MaxAxisRise) { goalOut = Clip(goalHints[i].point, L); return true; }
        return false;
    }

    public const float ExploreSeconds = 30f, NoRouteSeconds = 5f;   // x TimeScale: give a trip up

    // ------------------------------------------------------------------ regions

    Vector3 Clip(Vector3 p, float L)
    {
        Vector3 d = p - EnclosureCentre;
        Vector2 h = new Vector2(d.x, d.z);
        float lim = EnclosureRadius - 1.6f * L;   // the body's goal: its arms reach ~L further and swing
        if (!float.IsInfinity(EnclosureRadius) && h.magnitude > lim && lim > 0f) { h = h.normalized * lim; p.x = EnclosureCentre.x + h.x; p.z = EnclosureCentre.z + h.y; }
        if (!float.IsInfinity(HeightCap) && p.y > EnclosureCentre.y + HeightCap) p.y = EnclosureCentre.y + HeightCap;
        return p;
    }

    // Fallen: the foot of the climb back (FindRisingSurface), and whether the body has reached it.
    public const float FootSearchSeconds = 2f;         // x TimeScale: no rising surface seen yet -> look again
    public const float FootSearchLengths = 4f;         // x arm length: how far it looks
    public const float FootReachLengths = 0.7f;        // x arm length, level: at the foot, the climb starts
    public const float ClimbOffLengths = 0.2f;         // x arm length: the climb's goal stands this far off the surface
    const int FootRays = 16;
    Vector3 region, footPoint, footNormal;
    bool haveFoot, atFoot;
    float nextFootSearch;

    // The nearest surface that rises (steeper than ~53 deg: a trunk or a wall, not the ground) around the body, a
    // little above the ground, weighted toward the region the octopus wants to get back to. One ring of casts.
    bool FindRisingSurface(OctopusController c, Vector3 head, Vector3 toward, float L, out Vector3 point, out Vector3 normal)
    {
        Vector3 up = c.Up;
        Vector3 t0 = OctoMath.Norm(OctoMath.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right), Vector3.right), t1 = OctoMath.Cross(up, t0);
        Vector3 origin = head + up * (0.5f * L);
        point = normal = Vector3.zero;
        float best = float.MaxValue; bool found = false;
        for (int i = 0; i < FootRays; i++)
        {
            float a = i * (2f * Mathf.PI / FootRays);
            Vector3 dir = t0 * Mathf.Cos(a) + t1 * Mathf.Sin(a);
            if (!c.Cast(origin, dir, FootSearchLengths * L, out SurfaceHit h)) continue;
            if (Mathf.Abs(OctoMath.Dot(h.normal, up)) > 0.6f) continue;
            if (!c.HoldInside(h.point)) continue;
            Vector3 r = toward - h.point;
            float s = h.distance + 0.5f * (r - up * OctoMath.Dot(r, up)).magnitude;
            if (s < best) { best = s; point = h.point; normal = h.normal; found = true; }
        }
        return found;
    }

    Vector3 NearestRegion(Vector3 from, float L)
    {
        if (CanopyRegions.Count == 0) return from;
        int bi = 0; float bd = float.MaxValue;
        for (int i = 0; i < CanopyRegions.Count; i++) { float d = (CanopyRegions[i] - from).sqrMagnitude; if (d < bd) { bd = d; bi = i; } }
        return Clip(CanopyRegions[bi], L);
    }

    Vector3 FarthestRegion(Vector3 from, float L)
    {
        if (CanopyRegions.Count == 0) return from;
        Vector3 threat = Target != null ? Target.Position : from;
        int bi = 0; float bd = -1f;
        for (int i = 0; i < CanopyRegions.Count; i++) { float d = (CanopyRegions[i] - threat).sqrMagnitude; if (d > bd) { bd = d; bi = i; } }
        return Clip(CanopyRegions[bi], L);
    }
}
