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

    public Vector3 Target(CreatureSim sim, float dt)
    {
        Vector3 head = sim.Joints[0];
        Vector3 heading = sim.Rotations[0] * Vector3.forward;
        if (goal == Vector3.zero && !walking && pauseLeft <= 0f) { PickGoal(head, heading); walking = true; }

        if (walking)
        {
            Vector3 d = goal - head;
            d -= area.up * Vector3.Dot(d, area.up);
            if (d.magnitude <= arriveRadius)
            {
                walking = false;
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
                return head + Quaternion.AngleAxis(lookSign * kLookAngle, area.up) * Vector3.ProjectOnPlane(heading, area.up).normalized * kLookDistance;
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
