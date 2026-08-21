using UnityEngine;

// Owns "which way is up" for a transform: world Y normally, radial from the
// planet center when the terrain is in globe mode (WorldConfig.useGlobe).
// Everything about how the object turns/moves is unaffected -- this only
// changes what gravity means.
//
// Deliberately NOT implemented as "rotate transform.rotation a little every
// frame toward the target up". That incremental approach (composing
// Quaternion.FromToRotation(oldUp, newUp) onto the current rotation, frame
// after frame) has two real problems: it accumulates drift/roll over many
// frames (FromToRotation doesn't consistently pick the same rotation axis),
// and smoothing the catch-up means move/look code reads axes that are still
// lagging behind the correct orientation -- which is exactly what made
// movement feel "off". Instead, this keeps an explicit persisted `forward`
// reference, re-projects it onto the current tangent plane each frame (a
// planet's radial "up" changes perfectly smoothly with position, so this
// needs no smoothing to look smooth), and rebuilds the transform's rotation
// from scratch via Quaternion.LookRotation(forward, up) every frame. No
// incremental composition, so no drift; no lag, since it's exact every frame.
public class GravityAligner : MonoBehaviour
{
    [Tooltip("Left empty, the first MCChunkManager found in the scene is used.")]
    public MCChunkManager terrain;

    public Vector3 Up { get; private set; } = Vector3.up;
    public Vector3 Center { get; private set; }
    public float Radius { get; private set; }
    public bool IsPlanet { get; private set; }

    Vector3 _forward;

    void Awake()
    {
        _forward = transform.forward;
    }

    void Start()
    {
        if (!terrain) terrain = FindFirstObjectByType<MCChunkManager>();
    }

    // Turn around Up by `degrees` (mouse yaw). Call any number of times per
    // frame before Apply(); it just accumulates into the persisted forward.
    public void Yaw(float degrees)
    {
        _forward = Quaternion.AngleAxis(degrees, Up) * _forward;
    }

    // Resyncs the persisted forward reference to the transform's CURRENT
    // forward. Call after directly setting transform position/rotation (e.g.
    // spawn placement), so the next Apply() doesn't snap back to whatever
    // Awake happened to capture.
    public void ResetOrientation()
    {
        _forward = transform.forward;
    }

    // Recomputes Up (and Center/Radius/IsPlanet) from the current position,
    // then rebuilds transform.rotation from the persisted forward reprojected
    // onto the new tangent plane. Call once per frame, after any Yaw() calls.
    public void Apply()
    {
        Vector3 center = default;
        float radius = 0f;
        IsPlanet = terrain != null && terrain.TryGetPlanetCenter(out center, out radius);
        if (IsPlanet)
        {
            Center = center;
            Radius = radius;
            Vector3 rel = transform.position - center;
            Up = rel.sqrMagnitude > 1e-6f ? rel.normalized : Vector3.up;
        }
        else
        {
            Up = Vector3.up;
        }

        Vector3 fwd = Vector3.ProjectOnPlane(_forward, Up);
        if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.ProjectOnPlane(transform.forward, Up); // _forward went ~parallel to Up
        if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.ProjectOnPlane(Vector3.forward, Up);   // still degenerate: pick anything
        _forward = fwd.sqrMagnitude > 1e-8f ? fwd.normalized : Vector3.forward;

        transform.rotation = Quaternion.LookRotation(_forward, Up);
    }
}
