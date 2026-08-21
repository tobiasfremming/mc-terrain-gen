using UnityEngine;

// Leaves a smooth visual trail in the voxel terrain while the character walks.
// Decoupled from the movement controller: any object with a CharacterController
// can carry this, and it only talks to TerrainModificationSystem.
//
// Stamps are VISUAL-ONLY by default (affectPhysics = false): the render mesh
// shows the trail but colliders ignore it, so movement stays smooth.
// Consecutive stamps are connected with capsule (line) stamps, carving one
// continuous groove instead of a bumpy chain of dents.
[RequireComponent(typeof(CharacterController))]
public class FootprintEmitter : MonoBehaviour
{
    [Tooltip("Horizontal distance walked between stamps, meters.")]
    public float stride = 0.75f;
    public float radius = 0.7f;
    [Tooltip("How much each pass lowers the terrain, meters. Keep small; passes accumulate up to the system's maxDepth.")]
    public float depth = 0.15f;
    [Tooltip("If true the trail also deforms collision (colliders re-cook on every stamp).")]
    public bool affectPhysics = false;

    CharacterController _cc;
    MCChunkManager _terrain;
    Vector3 _lastPos;
    Vector3 _lastStamp;
    bool _hasLastStamp;
    float _accum;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
    }

    void Start()
    {
        _terrain = FindFirstObjectByType<MCChunkManager>();
        _lastPos = transform.position;
    }

    void LateUpdate() // after the controller has moved
    {
        if (_terrain == null) return;

        Vector3 pos = transform.position;
        Vector3 delta = pos - _lastPos;
        _lastPos = pos;

        if (!_cc.isGrounded)
        {
            _hasLastStamp = false; // break the trail across jumps/falls
            return;
        }

        // Measure horizontal distance walked: strip out whatever component of
        // delta points along "up" (world Y normally, planet-radial when the
        // player is aligned to a globe -- see GravityAligner).
        Vector3 up = transform.up;
        delta -= Vector3.Dot(delta, up) * up;
        _accum += delta.magnitude;
        if (_accum < stride) return;
        _accum = 0f;

        Vector3 foot = pos - up * (_cc.height * 0.5f);

        // Soft ground takes footprints; hard rock doesn't. Hardness blends
        // smoothly across biome transitions, so prints fade out gradually.
        float soft = 1f - _terrain.HardnessAt(foot);
        if (soft <= 0.05f)
        {
            _hasLastStamp = false;
            return;
        }
        float d = depth * soft;

        // Connect to the previous stamp if it's plausibly one step away, so the
        // trail is a continuous groove rather than separate dents.
        if (_hasLastStamp && Vector3.Distance(_lastStamp, foot) <= stride * 2.5f)
            _terrain.Modifications.StampLine(_lastStamp, foot, radius, d, affectPhysics);
        else
            _terrain.Modifications.StampSphere(foot, radius, d, affectPhysics);

        _lastStamp = foot;
        _hasLastStamp = true;
    }
}
