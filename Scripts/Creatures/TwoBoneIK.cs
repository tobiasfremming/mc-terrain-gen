using UnityEngine;

// Analytic two-bone IK (law of cosines), port of Leg.ik in video_manim/lizard.py.
// The bend plane is spanned by shoulder->target and the pole.
public static class TwoBoneIK
{
    const float kEps = 1e-4f;

    // Returns the elbow. `foot` = target if reachable, else the reach point clamped along shoulder->target.
    public static Vector3 Solve(Vector3 shoulder, Vector3 target, float l1, float l2, Vector3 pole, out Vector3 foot)
    {
        Vector3 d = target - shoulder;
        float dist = d.magnitude;
        Vector3 dir;
        if (dist > 1e-6f) dir = d / dist;
        else dir = Mathf.Abs(pole.y) < 0.9f ? Vector3.up : Vector3.right;

        float maxR = Mathf.Max(kEps, l1 + l2 - kEps);
        float minR = Mathf.Min(maxR, Mathf.Abs(l1 - l2) + kEps);
        float D = Mathf.Clamp(dist, minR, maxR);
        foot = shoulder + dir * D;

        float a = (l1 * l1 - l2 * l2 + D * D) / (2f * D);
        float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));

        Vector3 perp = pole - dir * Vector3.Dot(pole, dir);
        if (perp.sqrMagnitude < 1e-8f)
        {
            perp = Vector3.ProjectOnPlane(Vector3.up, dir);
            if (perp.sqrMagnitude < 1e-8f) perp = Vector3.ProjectOnPlane(Vector3.right, dir);
        }
        return shoulder + dir * a + perp.normalized * h;
    }
}
