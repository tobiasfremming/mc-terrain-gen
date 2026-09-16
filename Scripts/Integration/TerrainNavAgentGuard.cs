using System;
using UnityEngine;
using UnityEngine.AI;

// Owns a NavMeshAgent's lifecycle against terrain that streams and patches
// its NavMesh in and out (see TerrainNavMesh). A NavMeshAgent placed before
// its NavMesh exists throws on SetDestination; an agent whose patch is
// retired (globe re-plant, agent-type change) silently falls off. This
// guard keeps the agent disabled/unplaced until a NavMesh sample succeeds
// under it, re-places it after a patch removal, and hands the NPC between
// agent control and physics for knockback/ragdoll.
//
// Rigidbody + agent contract (carry this into INTEGRATION.md when Task 15
// writes it): while the agent drives, the Rigidbody is kinematic and the
// agent moves the transform (NavMeshAgent.updatePosition stays true -- do
// not disable it). When game code wants physics, it calls
// SuspendForPhysics(); this disables the agent and hands the Rigidbody back
// to TerrainRigidbodyGuard, which holds it over unstreamed ground exactly
// like any other body. ResumeFromPhysics() re-samples the NavMesh under the
// body's current position and Warps back onto it on the next Update.
//
// Kinematic ownership changes hands in a fixed order so this guard and
// TerrainRigidbodyGuard never fight over the flag: taking control disables
// the rigidbody guard FIRST (its OnDisable restores whatever kinematic
// value IT was holding) and only THEN forces isKinematic = true; parking on
// physics restores the body's ORIGINAL isKinematic (captured in Awake, not
// a hardcoded false -- a body that is kinematic by design stays kinematic)
// and only THEN re-enables the rigidbody guard.
//
// PatchVersion only bumps when a NavMesh instance is REMOVED somewhere in
// the world (see TerrainNavMesh), never when one appears -- so an agent that
// is enabled and already on a NavMesh is left alone on a version change;
// only an agent that is disabled or has fallen off gets re-sampled and
// warped. Unplaced agents still poll at checkInterval regardless of
// PatchVersion (e.g. right after spawn, before any patch was ever removed).
//
// Recommended prefab setup: the NavMeshAgent component may be left enabled
// in the prefab -- Awake disables it before Unity's own OnEnable would try
// to auto-place it on a NavMesh that may not exist yet, so an early failed
// auto-placement in the same frame is harmless.
[RequireComponent(typeof(NavMeshAgent))]
public class TerrainNavAgentGuard : MonoBehaviour
{
    public float sampleRadius = 4f;
    public float checkInterval = 0.25f;

    NavMeshAgent _agent;
    Rigidbody _rb;
    TerrainRigidbodyGuard _rbGuard;
    bool _suspended;
    bool _wasKinematic;
    int _seenVersion = -1;
    float _next;
    Vector3? _pendingDestination;

    public bool Ready => _agent.enabled && _agent.isOnNavMesh;
    public event Action BecameReady;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _rb = GetComponent<Rigidbody>();
        _rbGuard = GetComponent<TerrainRigidbodyGuard>();
        if (_rb != null) _wasKinematic = _rb.isKinematic;
        _agent.enabled = false; // never let Unity place it before a NavMesh exists
    }

    void Update()
    {
        if (_suspended) return;

        var nav = TerrainNavMesh.Instance;
        int version = nav != null ? nav.PatchVersion : 0;
        bool patchesChanged = version != _seenVersion;
        _seenVersion = version;

        // Enabled and already on a NavMesh: leave it alone unconditionally.
        // A version bump means a patch was removed SOMEWHERE, not that this
        // agent's own ground went away -- isOnNavMesh already reflects that
        // if it did. Re-warping every placed agent on every removal
        // elsewhere would hitch the whole crowd for nothing.
        if (_agent.enabled && _agent.isOnNavMesh) return;

        // Not placed (disabled, or fell off). Check the version before the
        // interval gate so a removal is acted on next frame instead of
        // waiting out checkInterval; otherwise keep polling unplaced agents
        // at the normal rate regardless of PatchVersion.
        if (!patchesChanged && Time.time < _next) return;
        _next = Time.time + checkInterval;

        if (NavMesh.SamplePosition(transform.position, out var hit, sampleRadius, _agent.areaMask))
        {
            Vector3? dest = _agent.enabled && _agent.hasPath ? _agent.destination : _pendingDestination;
            // Guard first, kinematic second -- see class comment.
            if (_rbGuard != null) _rbGuard.enabled = false;
            if (_rb != null) _rb.isKinematic = true;
            if (!_agent.enabled) _agent.enabled = true;
            _agent.Warp(hit.position);
            if (dest.HasValue) _agent.SetDestination(dest.Value);
            _pendingDestination = null;
            BecameReady?.Invoke();
        }
        else if (_agent.enabled && !_agent.isOnNavMesh)
        {
            // NavMesh vanished under us (patch retired / outside flat cap):
            // park on physics. Kinematic first, guard second -- see class comment.
            _agent.enabled = false;
            if (_rb != null) _rb.isKinematic = _wasKinematic;
            if (_rbGuard != null) _rbGuard.enabled = true;
        }
    }

    // Applies immediately while Ready; otherwise stores the destination and
    // applies it once BecameReady fires.
    public bool TrySetDestination(Vector3 worldPos)
    {
        if (Ready) return _agent.SetDestination(worldPos);
        _pendingDestination = worldPos;
        return false;
    }

    public void SuspendForPhysics()
    {
        _suspended = true;
        if (_agent.enabled)
        {
            _pendingDestination = _agent.hasPath ? _agent.destination : _pendingDestination;
            _agent.enabled = false;
        }
        // Kinematic first, guard second -- see class comment.
        if (_rb != null) _rb.isKinematic = _wasKinematic;
        if (_rbGuard != null) _rbGuard.enabled = true;
    }

    public void ResumeFromPhysics()
    {
        _suspended = false;
        _next = 0f;
        _seenVersion = -1; // force a re-sample + warp next Update
    }
}
