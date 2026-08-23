using UnityEngine;

// Gravity for CharacterController-driven movers: vertical velocity
// integration, ground-snap, jump impulse, and fall-safety (hold-if-no-
// ground-yet, kill-depth respawn) -- completely independent of whatever
// movement script drives horizontal input. Mirrors the role PlanetGravityBody
// plays for Rigidbody-driven objects: a self-contained "how does this thing
// fall" component that any CharacterController mover can attach, instead of
// that logic being duplicated/embedded inside a specific controller.
//
// Orientation ("which way is up") is still GravityAligner's job, same as
// always -- this only owns the vertical AXIS's velocity and safety behavior,
// reading GravityAligner.Up rather than duplicating it.
//
// Usage: call Tick(jumpPressed) once per frame, AFTER GravityAligner.Apply()
// has run for that frame, and add the returned displacement to whatever
// horizontal move vector the caller builds before passing the sum to
// CharacterController.Move(). Call CheckKillThreshold() once per frame too
// (after the Move() call, matching where the fall-safety net always ran).
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(GravityAligner))]
public class CharacterGravity : MonoBehaviour
{
    [Header("Gravity")]
    public float gravity = -25f;
    public float jumpHeight = 1.4f;

    [Header("Safety")]
    public DensityField densityField; // used to respawn on the surface if we fall out of the world
    [Tooltip("Flat world: world Y below this respawns on the surface. Planet mode: distance from the planet center below this fraction of the radius respawns instead (same idea -- caught falling through terrain that hasn't generated yet).")]
    public float killDepth = -80f;
    [Range(0f, 1f)] public float planetKillRadiusFraction = 0.5f;
    [Tooltip("While airborne, the fall is held (not applied) if no collider is found within this distance below -- avoids falling through terrain that hasn't streamed in yet. Needs to comfortably exceed the biggest realistic spawn-to-ground gap (see PlanetField.SafeSpawnRadius), or a legitimately far-but-generated drop gets mistaken for 'nothing there yet'.")]
    public float groundProbeDistance = 2000f;
    [Tooltip("If the fall stays held (groundProbeDistance never finds a collider) for longer than this, stop waiting and snap to the surface instead of hanging there indefinitely -- a self-healing backstop for slow/stuck chunk streaming.")]
    public float maxHoldSeconds = 6f;
    [Tooltip("Short downward probe just below the capsule's feet, supplementing CharacterController.isGrounded -- that flag alone is a known-flaky Unity signal (can read false for a stray frame on perfectly ordinary flat ground), which lets vertical velocity accumulate an extra frame of gravity before snapping back: a small periodic correction that shows up as sliding/stuttering while walking.")]
    public float groundedRayMargin = 0.3f;

    CharacterController _cc;
    GravityAligner _gravity;
    float _yVelocity;
    float _heldSince = -1f;

    public bool IsGrounded { get; private set; }
    public float VerticalVelocity => _yVelocity;
    // True while holding position because no ground was found nearby --
    // callers should suppress horizontal movement too while this is set,
    // same as the fall-safety net always did.
    public bool SuppressMovement { get; private set; }

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _gravity = GetComponent<GravityAligner>();
    }

    // Integrates one frame of vertical velocity and returns the displacement
    // (already scaled by Time.deltaTime) to fold into the caller's
    // CharacterController.Move() call. Must run after GravityAligner.Apply()
    // this frame, so _gravity.Up is current.
    public Vector3 Tick(bool jumpPressed)
    {
        Vector3 up = _gravity.Up;
        IsGrounded = _cc.isGrounded ||
            Physics.Raycast(transform.position - up * (_cc.height * 0.5f), -up, groundedRayMargin);

        if (IsGrounded && _yVelocity < 0f) _yVelocity = -2f; // keep pressed to the ground
        if (IsGrounded && jumpPressed) _yVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        _yVelocity += gravity * Time.deltaTime;

        // If there's no collider below us at all (terrain still generating),
        // hold position instead of falling through the world. Bounded by
        // maxHoldSeconds so a probe that never finds ground (streaming stuck,
        // or a spawn gap wider than groundProbeDistance) self-heals via
        // SnapToSurface instead of holding forever.
        if (!IsGrounded && !Physics.Raycast(transform.position, -up, groundProbeDistance))
        {
            _yVelocity = 0f;
            SuppressMovement = true;

            if (_heldSince < 0f) _heldSince = Time.time;
            else if (Time.time - _heldSince > maxHoldSeconds)
            {
                _heldSince = -1f;
                SnapToSurface();
                return Vector3.zero;
            }
            return Vector3.zero;
        }

        _heldSince = -1f;
        SuppressMovement = false;
        return up * _yVelocity * Time.deltaTime;
    }

    // Call once per frame after the movement script's CharacterController.
    // Move() -- respawns on the surface if we've fallen through the world.
    public void CheckKillThreshold()
    {
        if (IsBelowKillThreshold()) SnapToSurface();
    }

    bool IsBelowKillThreshold()
    {
        if (_gravity.IsPlanet)
            return Vector3.Distance(transform.position, _gravity.Center) < _gravity.Radius * planetKillRadiusFraction;
        return transform.position.y < killDepth;
    }

    public void SnapToSurface()
    {
        _yVelocity = 0f;
        _cc.enabled = false; // CharacterController ignores direct transform writes while enabled

        if (_gravity.IsPlanet)
        {
            Vector3 rel = transform.position - _gravity.Center;
            Vector3 dir = rel.sqrMagnitude > 1e-6f ? rel.normalized : Vector3.up;
            float r = SampleSurfaceRadius(_gravity.Center, dir, densityField, _gravity.Radius);
            transform.position = _gravity.Center + dir * (r + 2f);
        }
        else
        {
            Vector3 p = transform.position;
            float y = SampleSurfaceHeight(p.x, p.z, densityField);
            transform.position = new Vector3(p.x, y + 2f, p.z);
        }

        _gravity.ResetOrientation(); // transform.forward just changed under it (position moved)
        _cc.enabled = true;
    }

    // Finds the terrain surface by scanning the density field downward.
    // Convention in this project: Sample > 0 is solid, < 0 is air.
    public static float SampleSurfaceHeight(float x, float z, DensityField field)
    {
        if (!field) return 30f;

        const float top = 150f, bottom = -50f, step = 1f;
        float airY = top;
        for (float y = top; y >= bottom; y -= step)
        {
            if (field.Sample(new Vector3(x, y, z)) > 0f)
            {
                // refine the crossing between airY (air) and y (solid)
                float lo = y, hi = airY;
                for (int i = 0; i < 12; i++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (field.Sample(new Vector3(x, mid, z)) > 0f) lo = mid; else hi = mid;
                }
                return 0.5f * (lo + hi);
            }
            airY = y;
        }
        return 0f;
    }

    // Planet-mode equivalent of SampleSurfaceHeight: scans radius outward-to-
    // inward along `dir` from `center` (space -> ground, mirroring the flat
    // scan's sky -> ground direction) and returns the radius where density
    // crosses from air to solid. approxRadius comes from PlanetField.radius,
    // just to bound the search around where the surface actually should be.
    public static float SampleSurfaceRadius(Vector3 center, Vector3 dir, DensityField field, float approxRadius)
    {
        if (!field) return approxRadius;

        float span = Mathf.Max(50f, approxRadius * 0.5f);
        float rTop = approxRadius + span;
        float rBottom = Mathf.Max(0.1f, approxRadius - span);
        const float step = 1f;

        float airR = rTop;
        for (float r = rTop; r >= rBottom; r -= step)
        {
            if (field.Sample(center + dir * r) > 0f)
            {
                float lo = r, hi = airR;
                for (int i = 0; i < 12; i++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (field.Sample(center + dir * mid) > 0f) lo = mid; else hi = mid;
                }
                return 0.5f * (lo + hi);
            }
            airR = r;
        }
        return approxRadius;
    }
}
