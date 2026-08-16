using UnityEngine;

// First-person controller for walking on the marching-cubes terrain.
// Uses the classic Input API (the project has activeInputHandler = Both).
// WASD move, mouse look, Space jump, LeftShift sprint, Escape releases the cursor.
[RequireComponent(typeof(CharacterController))]
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
    public float killDepth = -80f;

    CharacterController _cc;
    float _pitch;
    float _yVelocity;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
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
        HandleMove();

        if (transform.position.y < killDepth) SnapToSurface();
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

        transform.Rotate(0f, mx, 0f);
        _pitch = Mathf.Clamp(_pitch - my, -pitchLimit, pitchLimit);
        if (cameraTransform) cameraTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    void HandleMove()
    {
        Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (input.sqrMagnitude > 1f) input.Normalize();

        float speed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;
        Vector3 move = (transform.right * input.x + transform.forward * input.y) * speed;

        bool grounded = _cc.isGrounded;
        if (grounded && _yVelocity < 0f) _yVelocity = -2f; // keep pressed to the ground

        if (grounded && Input.GetKeyDown(KeyCode.Space))
            _yVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        _yVelocity += gravity * Time.deltaTime;

        // If there's no collider below us at all (terrain still generating),
        // hold position instead of falling through the world.
        if (!grounded && !Physics.Raycast(transform.position, Vector3.down, 500f))
        {
            _yVelocity = 0f;
            move = Vector3.zero;
        }

        _cc.Move((move + Vector3.up * _yVelocity) * Time.deltaTime);
    }

    public void SnapToSurface()
    {
        Vector3 p = transform.position;
        float y = SampleSurfaceHeight(p.x, p.z, densityField);
        _yVelocity = 0f;
        _cc.enabled = false; // CharacterController ignores direct transform writes while enabled
        transform.position = new Vector3(p.x, y + 2f, p.z);
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
}
