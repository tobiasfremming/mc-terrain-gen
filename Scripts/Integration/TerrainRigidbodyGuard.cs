using UnityEngine;

public enum GuardState { Free, HeldNoGround, Rescued }

// Keeps a Rigidbody from falling through terrain that has not cooked yet, and
// pulls it out if an edit or a streaming gap ever leaves it inside rock.
// Checks at a low rate (every `checkInterval` s, staggered per instance): a
// held body is not moving, and a free body over cooked ground does not need
// per-step probing.
[RequireComponent(typeof(Rigidbody))]
public class TerrainRigidbodyGuard : MonoBehaviour
{
    [Tooltip("How far below the body to look for any collider (terrain or otherwise) before deciding the ground has not streamed in yet.")]
    public float probeDistance = 60f;
    public float checkInterval = 0.25f;
    [Tooltip("Height above the found surface a rescued body is placed at (its pivot).")]
    public float rescueLift = 1f;

    static readonly RaycastHit[] s_hits = new RaycastHit[8];

    Rigidbody _rb;
    bool _heldByUs;
    bool _wasKinematic;
    float _nextCheck;

    public bool IsHeld => _heldByUs;

    public static GuardState Decide(bool colliderBelow, bool insideSolid, bool wasHeld)
    {
        if (insideSolid) return GuardState.Rescued;
        return colliderBelow ? GuardState.Free : GuardState.HeldNoGround;
    }

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _nextCheck = Time.time + Random.value * checkInterval;
    }

    void FixedUpdate()
    {
        if (Time.time < _nextCheck) return;
        _nextCheck = Time.time + checkInterval;

        var m = MCChunkManager.Instance;
        if (m == null) return;
        if (m.PhysicsField == null) return; // no world configured -- nothing to probe against

        Vector3 p = _rb.position;
        Vector3 up = PlanetGravity.UpAt(p);

        // "Ground" here means any non-trigger collider below -- a building or
        // platform holds a body just as well as terrain does. Terrain layer
        // filtering stays out of this check on purpose: that would freeze a
        // body that is legitimately resting on a non-terrain support while it
        // waits for terrain that was never going to be under it anyway.
        bool below = false;
        int hitCount = Physics.RaycastNonAlloc(p + up * 0.5f, -up, s_hits, probeDistance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            if (s_hits[i].collider != null && s_hits[i].collider.attachedRigidbody != _rb) { below = true; break; }
        }
        bool inside = TerrainQuery.IsInsideSolid(m.PhysicsField, p);

        switch (Decide(below, inside, _heldByUs))
        {
            case GuardState.Rescued:
                if (TerrainQuery.TryFindGround(p, 50f, 10f, out Vector3 g, out _))
                {
                    _rb.position = g + up * rescueLift;
                    if (!_rb.isKinematic) _rb.linearVelocity = Vector3.zero;
                }
                // Release the hold now rather than waiting out the rest of the
                // current checkInterval frozen -- the body was just moved to
                // solid ground, so there's nothing left to hold it for.
                if (_heldByUs) { _rb.isKinematic = _wasKinematic; _heldByUs = false; }
                break;
            case GuardState.HeldNoGround:
                if (!_heldByUs) { _wasKinematic = _rb.isKinematic; _rb.isKinematic = true; _heldByUs = true; }
                break;
            case GuardState.Free:
                if (_heldByUs) { _rb.isKinematic = _wasKinematic; _heldByUs = false; _rb.WakeUp(); }
                break;
        }
    }

    void OnDisable()
    {
        if (_heldByUs && _rb != null) { _rb.isKinematic = _wasKinematic; _heldByUs = false; }
    }
}
