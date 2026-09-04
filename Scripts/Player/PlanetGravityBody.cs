using UnityEngine;

// Drop-in Rigidbody gravity: world Y normally, radial from the planet
// center in globe mode -- same "which way is up" as GravityAligner (see
// PlanetGravity), but for physics-driven objects instead of a scripted
// controller. Add this to ANY Rigidbody that should fall toward the planet
// and (optionally) stand upright on its surface; nothing else about how the
// object is built needs to change between a flat world and a globe --
// that's the point.
//
// Unity's own Physics.gravity is a single uniform vector for the whole
// scene, so it can't be made position-dependent; this replaces it per-body
// with a custom force computed from that body's own position each step.
[RequireComponent(typeof(Rigidbody))]
public class PlanetGravityBody : MonoBehaviour
{
    [Tooltip("Acceleration applied along -Up each FixedUpdate, replacing Unity's built-in Physics.gravity for this body.")]
    public float gravity = 9.81f;
    [Tooltip("Also rotate the body so its local up tracks the surface (radial direction) -- like GravityAligner, but via Rigidbody.MoveRotation so it plays nicely with physics/collision response. Turn off for props that should tumble freely under normal physics (rocks, debris) rather than stay upright.")]
    public bool alignToSurface = true;

    Rigidbody _rb;
    Vector3 _forward; // persisted, reprojected onto the tangent plane each step -- see GravityAligner's header comment for why not incremental rotation (drift/lag)

    public Vector3 Up { get; private set; } = Vector3.up;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _forward = transform.forward;
    }

    void FixedUpdate()
    {
        Up = PlanetGravity.UpAt(_rb.position);
        _rb.AddForce(-Up * gravity, ForceMode.Acceleration);

        if (!alignToSurface) return;

        Vector3 fwd = Vector3.ProjectOnPlane(_forward, Up);
        if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.ProjectOnPlane(transform.forward, Up); // _forward went ~parallel to Up
        if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.ProjectOnPlane(Vector3.forward, Up);   // still degenerate: pick anything
        _forward = fwd.sqrMagnitude > 1e-8f ? fwd.normalized : Vector3.forward;

        // No smoothing, same reasoning as GravityAligner: Up varies smoothly
        // with position already, so snapping exactly to it every step is
        // itself smooth -- and it means an impact can't permanently topple
        // this body, gravity always rights it back up next step (which is
        // exactly the desired behavior for anything meant to stand upright;
        // turn alignToSurface off for anything that shouldn't have that).
        _rb.MoveRotation(Quaternion.LookRotation(_forward, Up));
    }

    // Resyncs the persisted forward reference to the transform's CURRENT
    // forward -- call after directly teleporting/spawning this body so the
    // next FixedUpdate doesn't snap back to whatever Awake captured. Mirrors
    // GravityAligner.ResetOrientation.
    public void ResetOrientation() => _forward = transform.forward;
}
