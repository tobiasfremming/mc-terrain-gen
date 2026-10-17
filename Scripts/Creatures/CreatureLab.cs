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
    const float kViewGap = 4f;          // the player starts this far outside the nearest curb, facing it
    const float kEnclosureGap = 4f;     // free ground between neighbouring curbs

    // The enclosure for a species whose largest individual has size scale maxScale: 2.5 body lengths of
    // radius (room to wander and turn, small enough to see the animal from the curb), at least 5 m.
    public static float EnclosureRadius(CreatureSpecies sp, float maxScale)
        => Mathf.Max(5f, 2.5f * sp.BodyLength * maxScale);

    // The size scale of the species' largest possible individual (CreatureWorld draws it from the seed).
    public static float MaxScale(CreatureSpecies sp) => sp.scale * (1f + Mathf.Abs(sp.look.sizeJitter));

    IEnumerator Start()
    {
        CreatureWorld world = GetComponent<CreatureWorld>();
        if (GetComponent<CreatureDebugView>() == null) gameObject.AddComponent<CreatureDebugView>(); // F3 overlay
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
        // big species get big enclosures: keep neighbours apart and the arc outside the player's spot
        float maxR = 0f;
        for (int k = 0; k < n; k++) if (species[k] != null) maxR = Mathf.Max(maxR, EnclosureRadius(species[k], MaxScale(species[k])));
        float slotSpacing = Mathf.Max(spacing, 2f * maxR + kEnclosureGap);
        float slotRadius = Mathf.Max(arcRadius, maxR + kViewGap + searchRadius);
        var sb = new System.Text.StringBuilder("[CreatureLab] placed:");
        Vector3 nearestCentre = default; float nearestRadius = 0f, nearestDist = float.MaxValue;
        for (int k = 0; k < n; k++)
        {
            CreatureSpecies sp = species[k];
            if (sp == null) continue;
            // the individual's size is drawn from its seed inside CreatureWorld: size the enclosure and its
            // safe disc for the largest possible individual
            float maxScale = MaxScale(sp);
            float radius = EnclosureRadius(sp, maxScale);

            Vector3 slot = ArcSlot(origin, fwd, up, k, n, slotSpacing, slotRadius);
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
            float dist = Vector3.ProjectOnPlane(center - origin, up).magnitude - radius;
            if (dist < nearestDist) { nearestDist = dist; nearestCentre = center; nearestRadius = radius; }
        }
        if (nearestDist < float.MaxValue) FramePlayer(ground, player, origin, nearestCentre, nearestRadius, sb);
        Debug.Log(sb.ToString());
    }

    // Moves the player to kViewGap outside the nearest curb, on the line from the enclosure toward where
    // the player spawned, facing its centre: the whole enclosure and its animal are in view from the start.
    static void FramePlayer(IGroundProbe ground, Transform player, Vector3 origin, Vector3 centre, float radius, System.Text.StringBuilder sb)
    {
        Vector3 up = PlanetGravity.UpAt(centre);
        Vector3 away = Vector3.ProjectOnPlane(origin - centre, up);
        if (away.sqrMagnitude < 1e-6f) away = -Vector3.ProjectOnPlane(player.forward, up);
        away.Normalize();
        Vector3 spot = centre + away * (radius + kViewGap);
        if (!ground.Probe(spot + up * 2f, up, kProbeWindow + 2f, out Vector3 g, out _)) return;
        // the player's pivot height above the ground it stood on
        float lift = Mathf.Max(0f, Vector3.Dot(player.position - origin, PlanetGravity.UpAt(origin)));
        var cc = player.GetComponent<CharacterController>();
        bool ccOn = cc != null && cc.enabled;
        if (ccOn) cc.enabled = false;
        player.SetPositionAndRotation(g + up * (lift + 0.05f), Quaternion.LookRotation(-away, up));
        if (ccOn) cc.enabled = true;
        player.SendMessage("ResetOrientation", SendMessageOptions.DontRequireReceiver); // GravityAligner
        sb.Append($"; player framed at {player.position} facing the enclosure");
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
