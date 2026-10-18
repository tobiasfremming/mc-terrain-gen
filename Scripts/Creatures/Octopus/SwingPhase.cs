using UnityEngine;

// The swing of the body under its holds (plan 2026-10-18 T7), estimated as a pendulum: the body's centre of
// mass about the pivot (the centroid of the holds it hangs from), in the vertical plane of the route.
//
//   Theta    angle forward of straight down (rad; > 0 = ahead of the pivot along the route)
//   Omega    its rate (rad/s), from the body's velocity along the swing's tangent (low-passed: the soft body
//            jiggles), so it needs no history of the pivot (which jumps at every transfer)
//   Natural  the pendulum's natural frequency sqrt(g / r)
//   Amplitude  the energy amplitude: the angle at which the swing would stop, from
//            0.5 r^2 w^2 = g r (cos theta - cos A)
//   Kinetic  the kinetic share of the swing's energy (1 passing under the pivot, 0 at an apex): pumping
//            shortens the holds when it is high and pays them out when it is low (cos 2 phi = 2 Kinetic - 1)
//   ForwardApex  set for the tick on which the body, ahead of the pivot, stops and turns back
//
// Pure C#, no allocation, no queries.
public sealed class SwingPhase
{
    public float Theta { get; private set; }
    public float Omega { get; private set; }
    public float Radius { get; private set; }
    public float Natural { get; private set; } = 1f;
    public float Amplitude { get; private set; }
    public float Kinetic { get; private set; }
    public bool ForwardApex { get; private set; }
    public bool BackApex { get; private set; }
    public int ForwardApexes { get; private set; }   // cumulative
    public bool Valid { get; private set; }

    Vector3 prevCom, vel;
    bool havePrev;
    float prevOmega;

    public void Reset() { havePrev = false; Valid = false; Omega = prevOmega = 0f; vel = Vector3.zero; ForwardApex = BackApex = false; }

    // com: the body's centre of mass; pivot: the holds' centroid; route: the level travel direction (unit);
    // smooth: the velocity low-pass time constant (s).
    public void Update(Vector3 com, Vector3 pivot, Vector3 up, Vector3 route, float gravity, float dt, float smooth)
    {
        ForwardApex = BackApex = false;
        if (!(dt > 0f)) return;
        if (havePrev)
        {
            Vector3 v = (com - prevCom) * (1f / dt);
            float k = 1f - Mathf.Exp(-dt / Mathf.Max(smooth, 1e-4f));
            vel += (v - vel) * k;
        }
        prevCom = com; havePrev = true;
        Vector3 d = com - pivot;
        float x = OctoMath.Dot(d, route), y = -OctoMath.Dot(d, up);
        float r = Mathf.Sqrt(x * x + y * y);
        if (r < 1e-4f || route.sqrMagnitude < 0.5f) { Valid = false; return; }
        Valid = true;
        Radius = r;
        Theta = Mathf.Atan2(x, y);
        // the tangent of increasing theta in the plane: d/dtheta of (r sin, -r cos) = route cos + (-up)(-sin)...
        Vector3 tangent = route * Mathf.Cos(Theta) + up * Mathf.Sin(Theta);
        prevOmega = Omega;
        Omega = OctoMath.Dot(vel, tangent) / r;
        Natural = Mathf.Sqrt(gravity / r);
        float c = Mathf.Cos(Theta) - r * Omega * Omega / (2f * gravity);
        Amplitude = Mathf.Acos(Mathf.Clamp(c, -1f, 1f));
        float w = Omega / Natural;
        float e = Theta * Theta + w * w;
        Kinetic = e > 1e-6f ? w * w / e : 0f;
        if (prevOmega > 0f && Omega <= 0f && Theta > 0f) { ForwardApex = true; ForwardApexes++; }
        if (prevOmega < 0f && Omega >= 0f && Theta < 0f) BackApex = true;
    }
}
