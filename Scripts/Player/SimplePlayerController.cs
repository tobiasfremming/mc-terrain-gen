using UnityEngine;

// First-person controller for walking on the marching-cubes terrain.
// Uses the classic Input API (the project has activeInputHandler = Both).
// WASD move, mouse look, Space jump, LeftShift sprint, Escape releases the cursor.
//
// Deliberately only owns HORIZONTAL input and look -- orientation ("which
// way is up") is GravityAligner's job, and vertical velocity/ground-snap/
// jump-impulse/fall-safety is CharacterGravity's job (see its header
// comment). This class just reads Up from the former and the vertical
// displacement from the latter each frame, and does one CharacterController.
// Move() combining them with its own horizontal move vector -- gravity stays
// completely independent of whatever the movement script does.
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(GravityAligner))]
[RequireComponent(typeof(CharacterGravity))]
public class SimplePlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 6f;
    public float sprintSpeed = 12f;

    [Header("Look")]
    public Transform cameraTransform;
    public float mouseSensitivity = 2.5f;
    public float pitchLimit = 85f;

    CharacterController _cc;
    GravityAligner _gravity;
    CharacterGravity _characterGravity;
    float _pitch;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _gravity = GetComponent<GravityAligner>();
        _characterGravity = GetComponent<CharacterGravity>();
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
        _gravity.Apply(); // rebuild this frame's orientation before movement/gravity read it
        HandleMove();
        _characterGravity.CheckKillThreshold();
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
        bool jumpPressed = Input.GetKeyDown(KeyCode.Space);

        Vector3 verticalDelta = _characterGravity.Tick(jumpPressed);

        Vector3 move = _characterGravity.SuppressMovement
            ? Vector3.zero
            : (transform.right * input.x + transform.forward * input.y) * speed;

        _cc.Move(move * Time.deltaTime + verticalDelta);
    }
}
