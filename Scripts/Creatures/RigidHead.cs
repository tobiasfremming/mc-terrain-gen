using UnityEngine;

// A spine body's rigid head (CreatureSpecies.rigidHeadJoints, user 2026-10-19: "the head can be deformed, and the head
// shape follows the terrain when there are large changes, for instance when crawling out of a hole"). Joints 0..K-1
// keep one fixed layout and one shared frame - the walking rest shape: on flat ground, at the clearances, segments at
// their exact length - placed as one body from two points (the snout joint and the neck end, the chord between them
// is the head's forward) and an up. CreatureSim places the head after the chain step and after grounding (with the
// lift that keeps every head joint at or above its own grounded height), keeps the sway out of it, and writes the
// head's frames from it; the flight's air chain gets the same frames. The neck behind the head bends as before.
// Pure C#, allocation-free after construction.
internal sealed class RigidHead
{
    public readonly int K;
    readonly Vector3[] local;       // joint i in the head frame, scale 1
    readonly Quaternion[] frame;    // joint i's frame in the head frame

    public RigidHead(CreatureSpecies sp, int k)
    {
        K = k;
        local = new Vector3[k]; frame = new Quaternion[k];
        // the rest shape (CreatureRestPose: flat ground, up +Y, facing +Z): joint i at its clearance, each segment along
        // (0, c_i - c_i-1, -spacing) - shortened to the spacing, as the walking chain keeps it
        var w = new Vector3[k]; var rot = new Quaternion[k];
        w[0] = new Vector3(0f, sp.joints[0].clearance, 0f);
        rot[0] = Quaternion.identity;
        for (int i = 1; i < k; i++)
        {
            Vector3 d = new Vector3(0f, sp.joints[i].clearance - sp.joints[i - 1].clearance, -sp.spacing).normalized;
            w[i] = w[i - 1] + d * sp.spacing;
            rot[i] = CreatureSim.Basis(-d, Ortho(Vector3.up, -d));
        }
        Vector3 f = (w[0] - w[k - 1]).normalized;
        Quaternion inv = Quaternion.Inverse(CreatureSim.Basis(f, Ortho(Vector3.up, f)));
        for (int i = 0; i < k; i++) { local[i] = inv * (w[i] - w[0]); frame[i] = inv * rot[i]; }
    }

    // The head frame from the snout joint, the neck end and an up (made perpendicular to the chord), rolled about the
    // chord by rollDeg.
    public Quaternion Frame(Vector3 front, Vector3 back, Vector3 upRef, float rollDeg)
    {
        Vector3 f = front - back;
        float m = f.magnitude;
        f = m > 1e-9f ? f / m : Vector3.forward;
        Vector3 u = Ortho(upRef, f);
        if (rollDeg != 0f) u = Quaternion.AngleAxis(rollDeg, f) * u;
        return CreatureSim.Basis(f, u);
    }

    // Joints 0..K-1 laid out from pts[anchor] in frame q (scale s); the anchor stays where it is.
    public void Place(Vector3[] pts, Quaternion q, int anchor, float s)
    {
        Vector3 a = pts[anchor], la = local[anchor];
        for (int i = 0; i < K; i++)
            if (i != anchor) pts[i] = a + q * ((local[i] - la) * s);
    }

    public void WriteFrames(Quaternion q, Quaternion[] rotations)
    {
        for (int i = 0; i < K; i++) rotations[i] = q * frame[i];
    }

    // The head's own up (the frame of joint 0..K-1 averaged) - for frames another system wrote (the air chain).
    public Vector3 UpOf(Quaternion[] rotations)
    {
        Vector3 u = Vector3.zero;
        for (int i = 0; i < K; i++) u += rotations[i] * Vector3.up;
        return u;
    }

    static Vector3 Ortho(Vector3 u, Vector3 f)
    {
        Vector3 o = u - f * Vector3.Dot(u, f);
        if (o.sqrMagnitude < 1e-10f) o = Vector3.up - f * Vector3.Dot(Vector3.up, f);
        if (o.sqrMagnitude < 1e-10f) o = Vector3.Cross(f, Vector3.right);
        return o.normalized;
    }
}
