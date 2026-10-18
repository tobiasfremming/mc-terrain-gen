using UnityEngine;

public interface ICreatureBrain { Vector3 Target(CreatureSim sim, float dt); }

// Picks safe-disc points, walks to them, pauses (sometimes glancing aside), repeats.
public sealed class WanderBrain : ICreatureBrain
{
    const float kMinBearing = 25f, kMaxBearing = 120f;
    const float kLookChance = 0.3f, kLookDuration = 0.6f, kLookDistance = 0.3f, kLookAngle = 50f;

    readonly EnclosureArea area;
    readonly float pauseMin, pauseMax, arriveRadius;
    uint rng;

    bool walking;
    Vector3 goal;
    float pauseLeft, pauseLen;
    bool looks;
    float lookSign;

    // Pauses begun so far (a ClimbingBrain decides at a pause whether to climb).
    public int PausesStarted { get; private set; }
    // An obstacle the head may walk up to instead of around (the one being climbed); -1 = none.
    public int IgnoreObstacle = -1;

    public WanderBrain(EnclosureArea area, int seed, float pauseMin = 0.5f, float pauseMax = 3f, float arriveRadius = 0.4f)
    {
        this.area = area; this.pauseMin = pauseMin; this.pauseMax = pauseMax; this.arriveRadius = arriveRadius;
        rng = (uint)seed * 2654435761u + 0x9E3779B9u;
        if (rng == 0) rng = 2463534242u;
    }

    float Rand()
    {
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        return (rng & 0xFFFFFFu) / 16777216f;
    }

    public Vector3 Target(CreatureSim sim, float dt) => AroundObstacles(sim, Wander(sim, dt));

    // Head steering around the enclosure's obstacles: when the straight line to the target passes through an
    // obstacle's keep-out disc (grown by kSkirt), aim along the tangent to that disc on the target's side;
    // a head already inside the disc is sent straight out. The rest of the wander is unchanged.
    const float kSkirt = 1.15f;
    Vector3 AroundObstacles(CreatureSim sim, Vector3 target)
    {
        int n = area.ObstacleCount;
        if (n == 0) return target;
        Vector3 head = sim.Joints[0], up = area.up;
        Vector3 to = target - head; to -= up * Vector3.Dot(to, up);
        float dist = to.magnitude;
        if (dist < 1e-4f) return target;
        Vector3 dir = to / dist;
        float look = 2f * sim.SizeScale; // look-ahead of a steering target
        for (int i = 0; i < n; i++)
        {
            if (i == IgnoreObstacle) continue;
            Vector3 c = area.obstacleCenters[i] - head; c -= up * Vector3.Dot(c, up);
            float R = (area.obstacleRadii[i] + area.keepOut) * kSkirt;
            float D = c.magnitude;
            if (D < R)
            {
                // inside the skirt: out radially, sliding toward the target's side
                Vector3 outDir = D > 1e-4f ? -c / D : Vector3.Cross(up, dir);
                Vector3 side = Vector3.Cross(up, outDir) * Mathf.Sign(Vector3.Dot(Vector3.Cross(up, outDir), dir) + 1e-6f);
                return head + (outDir + 0.5f * side).normalized * look;
            }
            float along = Vector3.Dot(c, dir);
            if (along <= 0f || along > dist + R) continue;               // behind, or beyond the target
            float miss = (c - dir * along).magnitude;
            if (miss >= R) continue;                                      // the line clears the disc
            // tangent from the head to the disc, on the side the target lies
            float half = Mathf.Asin(Mathf.Clamp01(R / D)) * Mathf.Rad2Deg;
            Vector3 toC = c / D;
            Vector3 a = Quaternion.AngleAxis(half, up) * toC, b = Quaternion.AngleAxis(-half, up) * toC;
            Vector3 t = Vector3.Dot(a, dir) >= Vector3.Dot(b, dir) ? a : b;
            return head + t * Mathf.Max(look, Mathf.Min(dist, D));
        }
        return target;
    }

    Vector3 Wander(CreatureSim sim, float dt)
    {
        Vector3 head = sim.Joints[0];
        Vector3 heading = sim.Rotations[0] * Vector3.forward;
        if (goal == Vector3.zero && !walking && pauseLeft <= 0f) { PickGoal(head, heading); walking = true; }

        if (walking)
        {
            Vector3 d = goal - head;
            d -= area.up * Vector3.Dot(d, area.up);
            if (d.magnitude <= arriveRadius * sim.SizeScale)
            {
                walking = false;
                PausesStarted++;
                pauseLen = pauseLeft = Mathf.Lerp(pauseMin, pauseMax, Rand());
                looks = Rand() < kLookChance;
                lookSign = Rand() < 0.5f ? -1f : 1f;
            }
            else return goal;
        }

        pauseLeft -= dt;
        if (pauseLeft <= 0f)
        {
            PickGoal(head, heading);
            walking = true;
            return goal;
        }
        if (looks)
        {
            float into = pauseLen - pauseLeft; // time since pause start
            if (Mathf.Abs(into - 0.5f * pauseLen) <= 0.5f * kLookDuration)
                return head + Quaternion.AngleAxis(lookSign * kLookAngle, area.up) * Vector3.ProjectOnPlane(heading, area.up).normalized * (kLookDistance * sim.SizeScale);
        }
        return head;
    }

    void PickGoal(Vector3 head, Vector3 heading)
    {
        Vector3 h = Vector3.ProjectOnPlane(heading, area.up);
        Vector3 cand = head;
        for (int i = 0; i < 8; i++)
        {
            cand = area.RandomSafePoint(ref rng);
            Vector3 to = Vector3.ProjectOnPlane(cand - head, area.up);
            float bearing = Mathf.Abs(Vector3.Angle(h, to));
            if (bearing >= kMinBearing && bearing <= kMaxBearing) break;
        }
        goal = cand;
    }
}
