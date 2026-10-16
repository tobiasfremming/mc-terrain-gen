using System;
using UnityEngine;

// Procedural creature spine: a steered head drags a bend-limited chain that rides the ground.
// Port of the 2D LizardSim; legs live in CreatureLegs.
public sealed class CreatureSim
{
    const float kMaxDt = 0.1f;
    const float kGroundWindow = 0.5f;
    const float kGroundTau = 0.06f;
    const float kMaxSink = 0.005f;

    public readonly CreatureSpecies Species;
    public readonly float SizeScale;
    public readonly Vector3[] Joints;
    public readonly Quaternion[] Rotations;
    public readonly Vector3[] GroundNormals;
    public EnclosureArea? Bounds;

    public readonly LegState[] Legs;
    public int StepsTaken;     // cumulative, for tests/stats

    // Actual head speed (after the Bounds clamp), not the commanded one.
    public float Speed { get; private set; }
    public float Time { get; private set; }

    readonly IGroundProbe ground;
    readonly Func<Vector3, Vector3> upAt;
    readonly Vector3[] groundPoint;
    readonly CreatureLegs legs;
    readonly int n;
    Vector3 heading;
    float commanded;

    public CreatureSim(CreatureSpecies sp, IGroundProbe ground, Func<Vector3, Vector3> upAt,
                       Vector3 position, Vector3 forward, float sizeScale = 1f, int seed = 0)
    {
        Species = sp; this.ground = ground; this.upAt = upAt; SizeScale = sizeScale;
        n = sp.JointCount;
        Joints = new Vector3[n]; Rotations = new Quaternion[n];
        GroundNormals = new Vector3[n]; groundPoint = new Vector3[n];

        Vector3 up = Norm(upAt(position), Vector3.up);
        heading = Tangent(forward, up);
        float step = sp.spacing * sizeScale;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = position - heading * (step * i);
            Vector3 u = Norm(upAt(p), up);
            float clear = sp.joints[i].clearance * sizeScale;
            if (!ground.Probe(p, u, kGroundWindow * sizeScale, out Vector3 g, out Vector3 nrm)) { g = p - u * clear; nrm = u; }
            groundPoint[i] = g; GroundNormals[i] = Norm(nrm, u);
            Joints[i] = g + u * clear;
        }
        UpdateFrames();
        legs = new CreatureLegs(sp, ground, upAt, Joints, Rotations, sizeScale);
        Legs = legs.State;
    }

    public void Tick(float dt, Vector3 target)
    {
        if (!(dt > 0f)) dt = 0f;
        dt = Mathf.Clamp(dt, 0f, kMaxDt);
        int sub = Mathf.Max(1, Species.subSteps);
        float h = dt / sub;
        for (int s = 0; s < sub; s++) SubStep(h, target);
        UpdateFrames();
    }

    void SubStep(float dt, Vector3 target)
    {
        if (!(dt > 0f)) return;
        Time += dt;
        var sp = Species;

        // 1. head steering
        Vector3 head = Joints[0];
        Vector3 up = Norm(upAt(head), Vector3.up);
        heading = Tangent(heading, up);
        Vector3 to = target - head;
        float dist = to.magnitude;
        Vector3 desired = Vector3.ProjectOnPlane(to, up);
        float turnAngle = 0f;
        if (desired.sqrMagnitude > 1e-10f)
        {
            desired.Normalize();
            // a standing creature does not wiggle: fade the sway in with travel speed
            float sway = sp.speed > 1e-4f ? Mathf.Clamp01(commanded / (0.3f * sp.speed)) : 1f;
            float und = sway * sp.undulationDeg * Mathf.Sin(2f * Mathf.PI * sp.undulationHz * Time);
            if (und != 0f) desired = Quaternion.AngleAxis(und, up) * desired;
            float ang = Vector3.SignedAngle(heading, desired, up);
            turnAngle = Mathf.Abs(ang);
            // Turning in place swings the rigid head and drags the front of the body, so a leg at its
            // stretch or joint-range limit throttles the turn as well as the speed.
            float maxTurn = sp.turnRateDeg * dt * legs.Throttle;
            heading = Quaternion.AngleAxis(Mathf.Clamp(ang, -maxTurn, maxTurn), up) * heading;
        }
        float want = Mathf.Min(sp.speed, 2.5f * dist) * (1f - sp.turnSlowdown * turnAngle / 180f);
        // A creature cannot outrun its legs: the gait caps the speed, and slows it when a planted foot
        // nears full stretch. The cap is approached with accel-limited easing (braking 3x harder).
        float cap = Mathf.Min(sp.speed * legs.Throttle, legs.MaxSpeed);
        float goal = Mathf.Max(0f, want);
        float rate = sp.accel;
        if (cap < goal) { goal = cap; if (goal < commanded) rate = 3f * sp.accel; }
        commanded = Mathf.MoveTowards(commanded, goal, rate * dt);
        Vector3 start = head;
        head += heading * (commanded * dt);
        if (Bounds.HasValue) head = Bounds.Value.ClampToSafe(head);
        Speed = (head - start).magnitude / dt;
        Joints[0] = head;

        // 2. chain
        float seg = sp.spacing * SizeScale;
        Vector3 prevBack = -heading;
        for (int i = 1; i < n; i++)
        {
            Vector3 d = Joints[i] - Joints[i - 1];
            d = d.sqrMagnitude > 1e-12f ? d.normalized : prevBack;
            float lim = sp.joints[i].rigid ? 0f : sp.joints[i].bendLimitDeg * Mathf.Deg2Rad;
            d = Vector3.RotateTowards(prevBack, d, lim, 0f);
            Joints[i] = Joints[i - 1] + d * seg;
            prevBack = d;
        }

        // 3. ground: smooth height offset and normal; hold on probe miss
        float k = 1f - Mathf.Exp(-dt / kGroundTau);
        for (int i = 0; i < n; i++)
        {
            Vector3 u = Norm(upAt(Joints[i]), up);
            float clear = sp.joints[i].clearance * SizeScale;
            Vector3 near = Joints[i] - u * clear;
            float hPrev = Vector3.Dot(groundPoint[i] - near, u);
            float hs = hPrev;
            Vector3 nrm = GroundNormals[i];
            if (ground.Probe(near, u, kGroundWindow * SizeScale, out Vector3 gp, out Vector3 gn))
            {
                float hProbe = Vector3.Dot(gp - near, u);
                hs = Mathf.Lerp(hPrev, hProbe, k);
                // smoothing may lag a rising slope, but never let the body sink more than 5 mm into it
                if (hs < hProbe - kMaxSink) hs = hProbe - kMaxSink;
                nrm = Norm(Vector3.Lerp(nrm, gn, k), u);
            }
            groundPoint[i] = near + u * hs;
            GroundNormals[i] = nrm;
            Joints[i] = groundPoint[i] + u * (clear - legs.DroopSpread[i]);
        }

        // 4. legs (use the final joint positions; frames are refreshed first)
        UpdateFrames();
        StepsTaken += legs.Update(dt, Speed);
    }

    void UpdateFrames()
    {
        for (int i = 0; i < n; i++)
        {
            Vector3 f = i == 0 ? heading : Joints[i - 1] - Joints[i];
            f = f.sqrMagnitude > 1e-12f ? f.normalized : (i == 0 ? heading : Vector3.forward);
            Vector3 u = Vector3.ProjectOnPlane(GroundNormals[i], f);
            if (u.sqrMagnitude < 1e-8f) u = Vector3.ProjectOnPlane(Vector3.up, f);
            if (u.sqrMagnitude < 1e-8f) u = Vector3.ProjectOnPlane(Vector3.right, f);
            Rotations[i] = Quaternion.LookRotation(f, u.normalized);
        }
    }

    // Spine, leg and eye bones in world space, in the species bone order.
    public void WriteBoneWorld(Matrix4x4[] dst, int offset)
    {
        var sp = Species;
        Vector3 s = Vector3.one * SizeScale;
        for (int i = 0; i < n; i++) dst[offset + i] = Matrix4x4.TRS(Joints[i], Rotations[i], s);
        legs.WriteBones(dst, offset);

        Quaternion hr = Rotations[Mathf.Min(1, n - 1)];
        Quaternion er = Rotations[sp.eyeJoint];
        for (int e = 0; e < 2; e++)
        {
            Vector3 eo = sp.eyeOffset; eo.x *= e == 0 ? -1f : 1f; // eye 0 left (-x), 1 right
            dst[offset + sp.EyeBone(e)] = Matrix4x4.TRS(Joints[sp.eyeJoint] + er * eo * SizeScale, hr, s);
        }
    }

    static Vector3 Norm(Vector3 v, Vector3 fallback) => v.sqrMagnitude > 1e-12f ? v.normalized : fallback;

    static Vector3 Tangent(Vector3 v, Vector3 up)
    {
        Vector3 t = Vector3.ProjectOnPlane(v, up);
        if (t.sqrMagnitude < 1e-10f) t = Vector3.ProjectOnPlane(Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.forward, up);
        return t.normalized;
    }
}
