using System.Collections;
using UnityEngine;

// The creature lab: one enclosure per species on a gentle arc in front of the player's spawn, each with one
// wandering individual. Placement runs once the ground answers, so it adapts to wherever the dunes are,
// preferring the flattest of a few nearby spots.
[RequireComponent(typeof(CreatureWorld))]
public sealed class CreatureLab : MonoBehaviour
{
    public CreatureSpecies[] species;
    public float spacing = 25f, arcRadius = 30f;
    public int seed = 7;
    public float searchRadius = 15f;   // flat-spot search around each arc slot
    public Material borderMaterial;

    const float kWaitSeconds = 10f;
    const int kCandidates = 16, kRimProbes = 8;
    const float kProbeWindow = 6f;
    const float kGoldenAngle = 2.39996323f;

    IEnumerator Start()
    {
        CreatureWorld world = GetComponent<CreatureWorld>();
        Transform player = null;
        Vector3 groundAtPlayer = default;
        float waited = 0f;
        bool ready = false;
        while (waited <= kWaitSeconds)
        {
            MCChunkManager m = MCChunkManager.Instance;
            if (m != null && world.Ground != null)
            {
                if (player == null) player = m.target != null ? m.target : (Camera.main != null ? Camera.main.transform : null);
                if (player != null)
                {
                    Vector3 p = player.position;
                    if (world.Ground.Probe(p, PlanetGravity.UpAt(p), kProbeWindow, out groundAtPlayer, out _)) { ready = true; break; }
                }
            }
            yield return null;
            waited += Time.unscaledDeltaTime;
        }
        if (!ready)
        {
            Debug.LogWarning("[CreatureLab] no ground at the player after " + kWaitSeconds + " s; no enclosures placed.");
            yield break;
        }
        Place(world, player, groundAtPlayer);
    }

    void Place(CreatureWorld world, Transform player, Vector3 origin)
    {
        if (species == null) return;
        IGroundProbe ground = world.Ground;
        Vector3 up = PlanetGravity.UpAt(origin);
        Vector3 fwd = Vector3.ProjectOnPlane(player.forward, up);
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.ProjectOnPlane(Vector3.forward, up);
        fwd.Normalize();

        int n = species.Length;
        var sb = new System.Text.StringBuilder("[CreatureLab] placed:");
        for (int k = 0; k < n; k++)
        {
            CreatureSpecies sp = species[k];
            if (sp == null) continue;
            // the individual's size is drawn from its seed inside CreatureWorld: size the enclosure and its
            // safe disc for the largest possible individual
            float maxScale = 1f + Mathf.Abs(sp.look.sizeJitter);
            float radius = Mathf.Max(8f, 5f * sp.BodyLength * maxScale);

            Vector3 slot = ArcSlot(origin, fwd, up, k, n, spacing, arcRadius);
            Vector3 center = FlattestSpot(ground, slot, up, radius, out float flatness);
            Vector3 cUp = PlanetGravity.UpAt(center);

            var go = new GameObject("Enclosure " + sp.name);
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(center, Quaternion.FromToRotation(Vector3.up, cUp));
            var enc = go.AddComponent<CreatureEnclosure>();
            enc.radius = radius;
            enc.borderMaterial = borderMaterial;
            enc.Build(ground);

            EnclosureArea area = enc.AreaFor(sp, maxScale);
            Vector3 face = Vector3.ProjectOnPlane(origin - center, cUp);
            if (face.sqrMagnitude < 1e-6f) face = fwd;
            world.Spawn(sp, center, face.normalized, new WanderBrain(area, seed + k), area, seed + k);
            sb.Append($" {sp.name} at {center} (r {radius:F1} m, flatness {flatness:F3})");
        }
        Debug.Log(sb.ToString());
    }

    // The k-th of n slots on an arc of radius arcRadius around origin, centred on fwd; adjacent slots are
    // `spacing` apart (chord length).
    internal static Vector3 ArcSlot(Vector3 origin, Vector3 fwd, int k, int n, float spacing, float arcRadius)
        => ArcSlot(origin, fwd, Vector3.up, k, n, spacing, arcRadius);

    internal static Vector3 ArcSlot(Vector3 origin, Vector3 fwd, Vector3 up, int k, int n, float spacing, float arcRadius)
    {
        float step = 2f * Mathf.Asin(Mathf.Clamp(spacing / (2f * arcRadius), -1f, 1f));
        float angle = (k - 0.5f * (n - 1)) * step;
        Vector3 dir = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, up) * Vector3.ProjectOnPlane(fwd, up).normalized;
        return origin + dir * arcRadius;
    }

    // The flattest of kCandidates spots (sunflower pattern) within searchRadius of slot: flatness is the
    // lowest dot(normal, up) over kRimProbes probes on the enclosure rim. Returns the spot's ground point.
    Vector3 FlattestSpot(IGroundProbe ground, Vector3 slot, Vector3 up, float radius, out float best)
    {
        Vector3 t0 = Vector3.Cross(up, Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
        Vector3 t1 = Vector3.Cross(up, t0);
        best = -2f;
        Vector3 bestP = slot;
        for (int i = 0; i < kCandidates; i++)
        {
            float r = searchRadius * Mathf.Sqrt(i / (float)kCandidates), a = i * kGoldenAngle;
            Vector3 c = slot + t0 * (r * Mathf.Cos(a)) + t1 * (r * Mathf.Sin(a));
            if (!ground.Probe(c, up, kProbeWindow, out Vector3 cg, out _)) continue;
            float flat = 2f;
            for (int j = 0; j < kRimProbes; j++)
            {
                float b = j * 2f * Mathf.PI / kRimProbes;
                Vector3 q = cg + (t0 * Mathf.Cos(b) + t1 * Mathf.Sin(b)) * radius;
                flat = ground.Probe(q, up, kProbeWindow, out _, out Vector3 nrm) ? Mathf.Min(flat, Vector3.Dot(nrm, up)) : -1f;
                if (flat < best) break;
            }
            if (flat > best) { best = flat; bestP = cg; }
        }
        return bestP;
    }
}
