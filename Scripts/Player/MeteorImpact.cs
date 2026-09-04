using UnityEngine;

// Carves a crater into the terrain on first physics impact. Unlike
// FootprintEmitter (a continuous walking trail gated on CharacterController.
// isGrounded), this is a one-shot event driven by Rigidbody collision --
// the right shape for "meteor slams into the ground", not a grounded check.
[RequireComponent(typeof(Rigidbody))]
public class MeteorImpact : MonoBehaviour
{
    [Header("Crater")]
    [Tooltip("Impact speed (m/s) at or below which no crater is carved -- filters out gentle rolls/settling.")]
    public float minImpactSpeed = 5f;
    [Tooltip("Crater radius at minImpactSpeed.")]
    public float baseRadius = 1.5f;
    [Tooltip("Extra radius added per m/s of impact speed above minImpactSpeed.")]
    public float radiusPerSpeed = 0.15f;
    public float maxRadius = 12f;
    [Tooltip("Pushes the carve center into the ground along the impact normal, as a fraction of the crater radius, so the crater actually bites in rather than grazing the surface.")]
    [Range(0f, 1f)] public float bite = 0.4f;

    [Tooltip("Destroy this object shortly after impact instead of leaving it sitting in the crater.")]
    public bool destroyOnImpact = true;
    public float destroyDelay = 2f;

    MCChunkManager _terrain;
    bool _hasImpacted;

    void Start()
    {
        _terrain = FindFirstObjectByType<MCChunkManager>();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (_hasImpacted || _terrain == null) return;
        if (collision.collider.GetComponentInParent<MarchingChunk>() == null) return; // only craters terrain

        float speed = collision.relativeVelocity.magnitude;
        if (speed < minImpactSpeed) return;

        _hasImpacted = true;

        float radius = Mathf.Min(maxRadius, baseRadius + (speed - minImpactSpeed) * radiusPerSpeed);
        ContactPoint contact = collision.GetContact(0);
        Vector3 center = contact.point - contact.normal * (radius * bite);

        _terrain.Modifications.CarveSphere(center, radius, affectsPhysics: true);

        if (destroyOnImpact) Destroy(gameObject, destroyDelay);
    }
}
