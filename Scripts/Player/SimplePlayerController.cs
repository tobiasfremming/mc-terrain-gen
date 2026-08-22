using UnityEngine;

// First-person controller for walking on the marching-cubes terrain.
// Uses the classic Input API (the project has activeInputHandler = Both).
// WASD move, mouse look, Space jump, LeftShift sprint, Escape releases the cursor.
//
// Gravity/orientation ("which way is up") is delegated entirely to
// GravityAligner -- this class only ever reads its Up/Center/Radius/IsPlanet
// and calls Yaw() for mouse turning. See GravityAligner's header comment for
// why that split exists (rotating this transform incrementally in-place was
// the source of the "movement feels off" bug on planets).
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(GravityAligner))]
public class SimplePlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 6f;
    public float sprintSpeed = 12f;
    public float jumpHeight = 1.4f;
    public float gravity = -25f;

    [Header("Look")]
    public Transform cameraTransform;
    public float mouseSensitivity = 2.5f;
    public float pitchLimit = 85f;

    [Header("Safety")]
    public DensityField densityField; // used to respawn on the surface if we fall out of the world
    [Tooltip("Flat world: world Y below this respawns on the surface. Planet mode: distance from the planet center below this fraction of the radius respawns instead (same idea -- caught falling through terrain that hasn't generated yet).")]
    public float killDepth = -80f;
    [Range(0f, 1f)] public float planetKillRadiusFraction = 0.5f;
    [Tooltip("While airborne, the fall is held (not applied) if no collider is found within this distance below -- avoids falling through terrain that hasn't streamed in yet. Needs to comfortably exceed the biggest realistic spawn-to-ground gap (see PlanetField.SafeSpawnRadius), or a legitimately far-but-generated drop gets mistaken for 'nothing there yet'.")]
    public float groundProbeDistance = 2000f;
    [Tooltip("If the fall stays held (groundProbeDistance never finds a collider) for longer than this, stop waiting and snap to the surface instead of hanging there indefinitely -- a self-healing backstop for slow/stuck chunk streaming.")]
    public float maxHoldSeconds = 6f;

    CharacterController _cc;
    GravityAligner _gravity;
    float _pitch;
    float _yVelocity;
    float _heldSince = -1f;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _gravity = GetComponent<GravityAligner>();
    }

    void Start()
    {
        if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
        LockCursor(true);
    }

    void Update()
    {
        HandleCursor();
        if (Cursor.lockState == CursorLockMode.Locked) HandleLook();
        _gravity.Apply(); // rebuild this frame's orientation before movement reads it
        HandleMove();

        if (IsBelowKillThreshold()) SnapToSurface();
    }

    void HandleCursor()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) LockCursor(false);
        else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);
    }

    static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void HandleLook()
    {
        float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
        float my = Input.GetAxis("Mouse Y") * mouseSensitivity;

        _gravity.Yaw(mx); // turning is a yaw around Up, applied on the next Apply()
        _pitch = Mathf.Clamp(_pitch - my, -pitchLimit, pitchLimit);
        if (cameraTransform) cameraTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    void HandleMove()
    {
        Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (input.sqrMagnitude > 1f) input.Normalize();

        float speed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;
        Vector3 move = (transform.right * input.x + transform.forward * input.y) * speed;

        Vector3 up = _gravity.Up;
        bool grounded = _cc.isGrounded;
        if (grounded && _yVelocity < 0f) _yVelocity = -2f; // keep pressed to the ground

        if (grounded && Input.GetKeyDown(KeyCode.Space))
            _yVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        _yVelocity += gravity * Time.deltaTime;

        // If there's no collider below us at all (terrain still generating),
        // hold position instead of falling through the world. Bounded by
        // maxHoldSeconds so a probe that never finds ground (streaming stuck,
        // or a spawn gap wider than groundProbeDistance) self-heals via
        // SnapToSurface instead of holding forever.
        if (!grounded && !Physics.Raycast(transform.position, -up, groundProbeDistance))
        {
            _yVelocity = 0f;
            move = Vector3.zero;

            if (_heldSince < 0f) _heldSince = Time.time;
            else if (Time.time - _heldSince > maxHoldSeconds)
            {
                _heldSince = -1f;
                SnapToSurface();
                return;
            }
        }
        else
        {
            _heldSince = -1f;
        }

        _cc.Move((move + up * _yVelocity) * Time.deltaTime);
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
