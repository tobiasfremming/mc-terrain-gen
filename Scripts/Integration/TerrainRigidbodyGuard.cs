using UnityEngine;

public enum GuardState { Free, HeldNoGround, Rescued }

// Keeps a Rigidbody from falling through terrain that has not cooked yet, and
// pulls it out if an edit or a streaming gap ever leaves it inside rock.
// Checks at a low rate (every `checkInterval` s, staggered per instance): a
// held body is not moving, and a free body over cooked ground does not need
// per-step probing. The very first check runs on the first FixedUpdate, so a
// body spawned over uncooked ground is caught before it has fallen for a
// quarter second; only the checks after it are staggered.
//
// "Held" means: the density field says there is ground within probeDistance
// below, but no collider has streamed in there yet. A body with nothing below
// it in the field either is simply falling (dropped from high up, off a
// cliff, over a canyon deeper than probeDistance) and is left alone --
// freezing it would hold it in mid-air forever, since no collider is ever
// going to appear within range.
[RequireComponent(typeof(Rigidbody))]
public class TerrainRigidbodyGuard : MonoBehaviour
{
    [Tooltip("How far below the body to look, both for any collider (terrain or otherwise) and for ground in the density field. The body is held only when the field has ground within this distance but no collider does; with no field ground in range it is falling and stays free.")]
    public float probeDistance = 60f;
    public float checkInterval = 0.25f;
    [Tooltip("Height above the found surface a rescued body is placed at (its pivot).")]
    public float rescueLift = 1f;
    [Tooltip("Density the body's centre of mass must be buried past before it counts as inside rock. The collider is a 0.5 m-cell approximation of the field, so a body resting on it normally sits a few cm inside the field's surface; a margin keeps ordinary contact from triggering a rescue.")]
    public float insideMargin = 0.25f;

    // How far above the buried point to look for the way out.
    const float kRescueSearchUp = 50f;

    static readonly RaycastHit[] s_hits = new RaycastHit[8];

    Rigidbody _rb;
    bool _heldByUs;
    bool _wasKinematic;
    float _nextCheck;
    bool _checkedOnce;

    public bool IsHeld => _heldByUs;

    public static GuardState Decide(bool colliderBelow, bool fieldGroundBelow, bool insideSolid, bool wasHeld)
    {
        if (insideSolid) return GuardState.Rescued;
        if (colliderBelow) return GuardState.Free;
        // No collider: hold only if there is ground in the field for one to
        // stream in on. Otherwise the body is falling, not waiting.
        return fieldGroundBelow ? GuardState.HeldNoGround : GuardState.Free;
    }

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        if (_checkedOnce && Time.time < _nextCheck) return;
        // First check now; the second lands a random fraction of an interval
        // later, which spreads many guards spawned on the same frame across
        // the interval from then on.
        _nextCheck = Time.time + (_checkedOnce ? checkInterval : Random.value * checkInterval);
        _checkedOnce = true;

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
        // Sampled at the centre of mass, not the pivot: a pivot at the feet
        // of a body standing on the collider is routinely just inside the
        // field's surface (see insideMargin).
        Vector3 com = _rb.worldCenterOfMass;
        bool inside = m.PhysicsField.Sample(com) > insideMargin;
        // Only sampled when it can change the outcome: a collider below
        // already frees the body, whatever the field says.
        bool fieldGround = !below && !inside &&
                           TerrainQuery.TryFindGround(m.PhysicsField, p, up, 0.5f, probeDistance, out _, out _);

        switch (Decide(below, fieldGround, inside, _heldByUs))
        {
            case GuardState.Rescued:
                // Out through the rock directly above the body, not onto the
                // topmost surface: under an overhang or a cave roof that would
                // teleport it on top of the roof.
                if (TerrainQuery.TryFindExitAbove(m.PhysicsField, com, up, kRescueSearchUp, out Vector3 g))
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
