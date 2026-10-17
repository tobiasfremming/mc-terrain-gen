using UnityEngine;

// A WanderBrain that sometimes climbs the enclosure's climbable obstacles (a tree): after a pause it picks, with
// probability climbChance, a goal on the obstacle's real surface - a horizontal cast at a random height between
// 0.3 and 1 x the height cap, accepted where the surface is steep (60 deg .. the species' maxSurfaceAngle) and
// wide enough for the body (two parallel casts minWidthFactor body widths apart both hit) - walks to it and up,
// rests there 4-10 s, then comes down head-first toward a spot on the ground 1-1.5 body lengths from the trunk,
// and wanders on. Safeguards: no progress for 4 s while climbing, or the head above the cap, starts the
// descent; Blocked (an overhang ahead) re-picks the goal, then descends. While wandering the creature is not
// allowed onto plants (it walks around them as before); terrain walls are climbed whatever the brain does.
public sealed class ClimbingBrain : ICreatureBrain
{
    public enum State { Wander, Approach, Climb, Rest, Descend }

    const float kRestMin = 4f, kRestMax = 10f;
    const float kStuckSeconds = 4f, kDescendGiveUp = 12f;
    const float kGoalLo = 0.3f, kGoalHi = 1f;          // goal height range, fractions of the height cap
    const float kMinGoalDeg = 60f;                      // a goal's surface is at least this steep
    const float kAzimuthSpread = 60f;                   // goals face the creature's side of the trunk, +- this
    const int kGoalTries = 8;
    const float kDescendLo = 1f, kDescendHi = 1.5f;     // descent target distance from the trunk, body lengths
    const float kProgress = 0.1f;                       // metres x size: progress that resets the stuck timer
    const float kArrive = 0.12f;                        // metres x size (plus the head clearance): goal reached

    readonly EnclosureArea area;
    readonly WanderBrain wander;
    readonly float climbChance;
    uint rng;

    public State Mode { get; private set; }
    public Vector3 Goal { get; private set; }
    public int Obstacle { get; private set; } = -1;
    public int Climbs { get; private set; }             // climbs started (stats/tests)
    int pausesSeen;
    float timer, stuckTimer, bestDist;
    int repicks;

    public ClimbingBrain(EnclosureArea area, int seed, float climbChance = 0.35f)
    {
        this.area = area; this.climbChance = climbChance;
        wander = new WanderBrain(area, seed);
        rng = (uint)seed * 2246822519u + 0x165667B1u;
        if (rng == 0) rng = 3266489917u;
    }

    public WanderBrain Wanderer => wander;

    float Rand()
    {
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        return (rng & 0xFFFFFFu) / 16777216f;
    }

    // Starts a climb now if a goal can be found (tests, or a scripted behaviour).
    public bool StartClimb(CreatureSim sim)
    {
        if (!TryPickGoal(sim)) return false;
        Enter(State.Approach, sim);
        Climbs++;
        return true;
    }

    public Vector3 Target(CreatureSim sim, float dt)
    {
        Vector3 head = sim.Joints[0];
        timer += dt;
        switch (Mode)
        {
            case State.Wander:
            {
                sim.ClimbAllowed = false;
                Vector3 t = wander.Target(sim, dt);
                if (wander.PausesStarted != pausesSeen)
                {
                    pausesSeen = wander.PausesStarted;
                    if (sim.CanClimb && area.ObstacleCount > 0 && Rand() < climbChance && StartClimb(sim)) return Goal;
                }
                return t;
            }
            case State.Approach:
            {
                sim.ClimbAllowed = true;
                if (sim.Source[0] != 0 && sim.Steepness(0) > 0.5f) { Enter(State.Climb, sim); return Goal; }
                if (Stuck(sim, dt)) { Enter(State.Wander, sim); return head; }
                return Goal;
            }
            case State.Climb:
            {
                sim.ClimbAllowed = true;
                if (sim.Blocked)
                {
                    if (repicks++ < 2 && TryPickGoal(sim)) { stuckTimer = 0f; bestDist = float.MaxValue; return Goal; }
                    Enter(State.Descend, sim); return Goal;
                }
                if (AboveCap(head) || Stuck(sim, dt)) { Enter(State.Descend, sim); return Goal; }
                float arrive = (kArrive + sim.Species.joints[0].clearance) * sim.SizeScale;
                if ((Goal - head).magnitude <= arrive) { Enter(State.Rest, sim); return head; }
                return Goal;
            }
            case State.Rest:
                sim.ClimbAllowed = true;
                if (timer >= restLength) Enter(State.Descend, sim);
                return head;
            default: // Descend
            {
                sim.ClimbAllowed = true;
                bool down = !sim.Climbing || (sim.Source[0] == 0 && sim.Steepness(0) < 0.1f && OffObstacle(sim));
                float arrive = 0.5f * sim.SizeScale;
                Vector3 to = Goal - head; to -= area.up * Vector3.Dot(to, area.up);
                if ((down && (to.magnitude <= arrive || OffObstacle(sim))) || timer > kDescendGiveUp) { Enter(State.Wander, sim); return head; }
                return Goal;
            }
        }
    }

    float restLength;

    void Enter(State s, CreatureSim sim)
    {
        Mode = s; timer = 0f; stuckTimer = 0f; bestDist = float.MaxValue;
        switch (s)
        {
            case State.Wander:
                wander.IgnoreObstacle = -1;
                Obstacle = -1;
                break;
            case State.Approach:
                wander.IgnoreObstacle = Obstacle;
                repicks = 0;
                break;
            case State.Rest:
                restLength = Mathf.Lerp(kRestMin, kRestMax, Rand());
                break;
            case State.Descend:
                Goal = DescentTarget(sim);
                break;
        }
    }

    // No progress toward the goal for kStuckSeconds.
    bool Stuck(CreatureSim sim, float dt)
    {
        float d = (Goal - sim.Joints[0]).magnitude;
        if (d < bestDist - kProgress * sim.SizeScale) { bestDist = d; stuckTimer = 0f; return false; }
        stuckTimer += dt;
        return stuckTimer > kStuckSeconds;
    }

    float HeightCap(CreatureSim sim)
    {
        if (area.maxHeight > 0f) return area.maxHeight;
        return sim.Species.climb.MaxHeightBodyLengths * sim.Species.BodyLength * sim.SizeScale;
    }

    bool AboveCap(Vector3 head) => area.maxHeight > 0f && Vector3.Dot(head - area.center, area.up) >= area.maxHeight;

    // Every joint is clear of the obstacle's footprint again.
    bool OffObstacle(CreatureSim sim)
    {
        if (Obstacle < 0) return true;
        Vector3 c = area.obstacleCenters[Obstacle];
        float r = area.obstacleRadii[Obstacle];
        for (int i = 0; i < sim.Joints.Length; i++)
        {
            Vector3 d = sim.Joints[i] - c; d -= area.up * Vector3.Dot(d, area.up);
            if (d.sqrMagnitude < r * r) return false;
        }
        return true;
    }

    // A goal on the nearest climbable obstacle's real surface (see the class comment).
    bool TryPickGoal(CreatureSim sim)
    {
        ISurfaceProbe probe = sim.Surface;
        if (probe == null) return false;
        if (probe is CompositeSurfaceProbe comp && comp.Surfaces != null) probe = comp.Surfaces; // past the per-creature gate
        Vector3 head = sim.Joints[0], up = area.up;
        int best = -1; float bestD = float.MaxValue;
        for (int i = 0; i < area.ObstacleCount; i++)
        {
            if (!area.IsClimbable(i)) continue;
            Vector3 d = area.obstacleCenters[i] - head; d -= up * Vector3.Dot(d, up);
            if (d.sqrMagnitude < bestD) { bestD = d.sqrMagnitude; best = i; }
        }
        if (best < 0) return false;
        Vector3 c = area.obstacleCenters[best];
        float r = Mathf.Max(0.05f, area.obstacleRadii[best]);
        float cap = HeightCap(sim);
        Vector3 side = head - c; side -= up * Vector3.Dot(side, up);
        if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(up, Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right);
        side.Normalize();
        float halfWidth = 0f;
        for (int i = 0; i < sim.Species.JointCount; i++) halfWidth = Mathf.Max(halfWidth, sim.Species.joints[i].halfWidth);
        float span = sim.Species.climb.MinWidthFactor * 2f * halfWidth * sim.SizeScale;
        float maxAngle = sim.Species.climb.MaxSurfaceAngle;
        float reach = 2f * r + 2f * span;
        for (int k = 0; k < kGoalTries; k++)
        {
            float h = Mathf.Lerp(kGoalLo, kGoalHi, Rand()) * cap;
            Vector3 dir = Quaternion.AngleAxis((2f * Rand() - 1f) * kAzimuthSpread, up) * side;
            Vector3 o = c + up * h + dir * reach;
            if (!probe.Cast(o, -dir, 2f * reach, out SurfaceHit hit)) continue;
            float a = Vector3.Angle(hit.normal, up);
            if (a < kMinGoalDeg || a > maxAngle) continue;
            // wide enough: two parallel casts half the span either side hit at about the same depth
            Vector3 lat = Vector3.Cross(up, dir).normalized * (0.5f * span);
            if (!probe.Cast(o + lat, -dir, 2f * reach, out SurfaceHit l) || !probe.Cast(o - lat, -dir, 2f * reach, out SurfaceHit rr)) continue;
            if (Mathf.Abs(l.distance - hit.distance) > span || Mathf.Abs(rr.distance - hit.distance) > span) continue;
            // the way up from the ground on this side is closed bark: at body height the trunk must be met as a
            // front face near its radius. A plant's trunk is an open tube; where its bottom rim stands above the
            // sand a creature walking in at that side went into the hollow instead of up the bark.
            if (!ClosedAtFoot(probe, c, up, dir, reach, r, sim)) continue;
            Goal = hit.point;
            Obstacle = best;
            return true;
        }
        return false;
    }

    bool ClosedAtFoot(ISurfaceProbe probe, Vector3 c, Vector3 up, Vector3 dir, float reach, float r, CreatureSim sim)
    {
        float clear = sim.Species.joints[0].clearance * sim.SizeScale;
        for (int k = 1; k <= 2; k++)
        {
            Vector3 o = c + up * (k * clear) + dir * reach;
            if (!probe.Cast(o, -dir, 2f * reach, out SurfaceHit h)) return false;     // nothing, or a back face (inside a hollow)
            Vector3 d = h.point - c; d -= up * Vector3.Dot(d, up);
            if (d.magnitude < 0.3f * r) return false;                                // met only deep inside the trunk's outline (r = obstacle radius, 1.5 x the trunk)
        }
        return true;
    }

    // A spot on the ground 1-1.5 body lengths out from the trunk, on the creature's side, inside the safe disc.
    Vector3 DescentTarget(CreatureSim sim)
    {
        Vector3 head = sim.Joints[0], up = area.up;
        Vector3 c = Obstacle >= 0 ? area.obstacleCenters[Obstacle] : head;
        float r = Obstacle >= 0 ? area.obstacleRadii[Obstacle] : 0f;
        Vector3 side = head - c; side -= up * Vector3.Dot(side, up);
        if (side.sqrMagnitude < 1e-6f) side = sim.Heading - up * Vector3.Dot(sim.Heading, up);
        if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(up, Vector3.right);
        side.Normalize();
        float d = r + Mathf.Lerp(kDescendLo, kDescendHi, Rand()) * sim.Species.BodyLength * sim.SizeScale;
        Vector3 groundC = c - up * Vector3.Dot(c - area.center, up);
        Vector3 p = area.ClampToSafe(groundC + side * d);
        if (area.ObstacleAt(p) >= 0) p = area.RandomSafePoint(ref rng);
        return p;
    }
}
