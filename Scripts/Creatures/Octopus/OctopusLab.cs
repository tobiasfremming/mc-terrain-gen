using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The tree octopus enclosure of the Creature Lab (tree octopus design §8 task 10, user decisions 1-3): next
// to the lab's arc, on a flat spot, a GROVE of climbable trees with overlapping canopies (each an
// EnclosurePlant: its LOD0 bark is a real collider in the creatures' surface registry, and its skeleton feeds
// the branch hints), a curb around it, the octopus spawned and anchored in the central canopy, and a prey
// creature wandering the floor. The player is prey too and the viewer the octopus freezes for. Lab default:
// a grab holds 1.5 s, releases, fires events (logged here); no damage.
// Size: the arms fit the branches - unless overridden, the scale is chosen so a mid-canopy branch takes at
// least MinWrapTurns of wrap (user decision 3).
[RequireComponent(typeof(CreatureWorld))]
public sealed class OctopusLab : MonoBehaviour
{
    public CreatureSpecies octopus;
    [Tooltip("Tree prototype sets for the grove (cycled); empty = the CreatureLab's enclosure plant.")]
    public PlantPrototypeSet[] groveSets;
    public int groveTrees = 7;
    public float treeScale = 2f;
    [Tooltip("Tree spacing as a fraction of a canopy's diameter (< 1: canopies overlap).")]
    public float canopyOverlap = 0.7f;
    [Tooltip("The prey creature on the grove floor (e.g. the desert beetle or snake); none = only the player.")]
    public CreatureSpecies preySpecies;
    public float preyScale = 0f;
    [Tooltip("Octopus size scale; 0 = fit to the grove's branches (MinWrapTurns).")]
    public float octopusScale = 0f;
    public float minWrapTurns = 1.2f;
    public int seed = 31;
    public Material borderMaterial;
    public bool logEvents = true;

    const float kWaitSeconds = 20f, kProbeWindow = 6f, kGap = 6f, kMargin = 1.5f;

    public OctopusCreature Octopus { get; private set; }
    public CreatureSim Prey { get; private set; }
    public CreatureEnclosure Enclosure { get; private set; }
    public readonly List<EnclosurePlant> Trees = new List<EnclosurePlant>();
    public BranchHints Hints { get; private set; }
    public float ChosenScale { get; private set; }

    IEnumerator Start()
    {
        CreatureWorld world = GetComponent<CreatureWorld>();
        CreatureLab lab = GetComponent<CreatureLab>();
        Transform player = null;
        float waited = 0f;
        // after the lab has placed its enclosures (so the grove can keep clear of them) and the ground answers
        while (waited <= kWaitSeconds)
        {
            MCChunkManager m = MCChunkManager.Instance;
            if (m != null && world.Ground != null)
            {
                if (player == null) player = m.target != null ? m.target : (Camera.main != null ? Camera.main.transform : null);
                bool labDone = lab == null || lab.species == null || lab.species.Length == 0 || GetComponentsInChildren<CreatureEnclosure>().Length > 0;
                if (player != null && labDone && waited > 1f) break;
            }
            yield return null;
            waited += Time.unscaledDeltaTime;
        }
        if (player == null || world.Ground == null) { Debug.LogWarning("[OctopusLab] no ground / player; no grove placed."); yield break; }
        Place(world, player);
    }

    PlantPrototypeSet SetFor(int i)
    {
        if (groveSets != null && groveSets.Length > 0 && groveSets[i % groveSets.Length] != null) return groveSets[i % groveSets.Length];
        CreatureLab lab = GetComponent<CreatureLab>();
        return lab != null ? lab.enclosurePlant : null;
    }

    internal void Place(CreatureWorld world, Transform player)
    {
        if (octopus == null) { Debug.LogWarning("[OctopusLab] no octopus species assigned."); return; }
        PlantPrototypeSet first = SetFor(0);
        if (first == null) { Debug.LogWarning("[OctopusLab] no tree prototype set for the grove."); return; }
        IGroundProbe ground = world.Ground;
        Vector3 origin = player.position;
        Vector3 up = PlanetGravity.UpAt(origin);
        Vector3 fwd = Vector3.ProjectOnPlane(player.forward, up).normalized;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;

        // grove layout: a centre tree and rings around it, canopies overlapping
        PlantVariant v0 = first.Variant(0);
        float canopy = v0 != null ? Mathf.Max(v0.bounds.size.x, v0.bounds.size.z) * treeScale : 6f * treeScale;
        float height = v0 != null ? v0.bounds.max.y * treeScale : 6f * treeScale;
        float step = Mathf.Max(1.5f, canopyOverlap * canopy);
        int n = Mathf.Max(1, groveTrees);
        float groveRadius = n == 1 ? 0.5f * canopy : step * Mathf.Ceil((n - 1) / 6f) + 0.5f * canopy;
        float radius = groveRadius + kMargin;

        // a flat spot beyond the lab's enclosures, toward the player's left of the arc
        var placed = new List<Vector4>();
        foreach (CreatureEnclosure e in GetComponentsInChildren<CreatureEnclosure>())
            placed.Add(new Vector4(e.transform.position.x, e.transform.position.y, e.transform.position.z, e.radius));
        float farthest = 0f;
        foreach (Vector4 p in placed) farthest = Mathf.Max(farthest, Vector3.ProjectOnPlane((Vector3)p - origin, up).magnitude + p.w);
        Vector3 side = Vector3.Cross(up, fwd).normalized;
        Vector3 slot = origin + (fwd * 0.6f - side * 0.8f).normalized * (Mathf.Max(farthest, 20f) * 0.6f + radius + kGap);
        Vector3 centre = CreatureLab.FlattestSpot(ground, slot, up, radius, 2f * radius, placed, out float flatness);
        Vector3 cUp = PlanetGravity.UpAt(centre);

        var encGo = new GameObject("Enclosure " + octopus.name);
        encGo.transform.SetParent(transform, false);
        encGo.transform.SetPositionAndRotation(centre, Quaternion.FromToRotation(Vector3.up, cUp));
        Enclosure = encGo.AddComponent<CreatureEnclosure>();
        Enclosure.radius = radius;
        Enclosure.borderMaterial = borderMaterial != null ? borderMaterial : GetComponent<CreatureLab>()?.borderMaterial;

        // the trees: hex rings around the centre
        Hints = new BranchHints(FindAnyObjectByType<PlantScatter>());
        Vector3 t0 = Vector3.Cross(cUp, Mathf.Abs(cUp.y) < 0.99f ? Vector3.up : Vector3.right).normalized, t1 = Vector3.Cross(cUp, t0);
        var canopyRegions = new List<Vector3>();
        float tallest = 0f;
        Enclosure.obstacles.Clear();
        for (int i = 0; i < n; i++)
        {
            Vector3 offset = Vector3.zero;
            if (i > 0)
            {
                int ring = 1 + (i - 1) / 6, slotInRing = (i - 1) % 6;
                float a = slotInRing * Mathf.PI / 3f + ring * 0.5f;
                offset = (t0 * Mathf.Cos(a) + t1 * Mathf.Sin(a)) * (step * ring);
            }
            Vector3 at = centre + offset;
            if (ground.Probe(at + cUp * 2f, cUp, kProbeWindow + 2f, out Vector3 g, out _)) at = g;
            PlantPrototypeSet set = SetFor(i);
            if (set == null) continue;
            int variant = (seed + i * 7) % Mathf.Max(1, set.variants != null ? set.variants.Length : 1);
            var go = new GameObject("Grove tree " + i);
            go.transform.SetParent(encGo.transform, true);
            Quaternion rot = Quaternion.AngleAxis(137f * i + seed, cUp) * Quaternion.FromToRotation(Vector3.up, cUp);
            go.transform.SetPositionAndRotation(at - cUp * (0.1f * treeScale), rot);
            var plant = go.AddComponent<EnclosurePlant>();
            plant.Init(set, variant, treeScale);
            Trees.Add(plant);
            Hints.Register(set, variant, Matrix4x4.TRS(go.transform.position, rot, Vector3.one * treeScale));
            Enclosure.obstacles.Add(new Vector4(at.x, at.y, at.z, plant.TrunkRadius));
            canopyRegions.Add(at + cUp * (0.55f * plant.Height));
            tallest = Mathf.Max(tallest, plant.Height);
        }
        Enclosure.Build(ground);
        Physics.SyncTransforms();

        // size: fit the branches
        ChosenScale = octopusScale > 0f ? octopusScale : FitScale(octopus.cephalopod, Hints, canopyRegions.Count > 0 ? canopyRegions[0] : centre + cUp * (0.55f * height), cUp);

        // the octopus: in the central canopy, anchored
        var area = new EnclosureArea { center = centre, up = cUp, radius = radius, safeRadius = groveRadius, maxHeight = tallest };
        Vector3 spawn = canopyRegions.Count > 0 ? canopyRegions[0] : centre + cUp * (0.5f * height);
        Octopus = world.SpawnOctopus(octopus, spawn, Quaternion.FromToRotation(Vector3.up, cUp), area, seed, ChosenScale, Hints);
        foreach (Vector3 r in canopyRegions) Octopus.Brain.CanopyRegions.Add(r);
        // from the canopy it must see the grove floor: perception reaches the ground below the tallest canopy
        float armL = Octopus.Body.Arms[0].Length;
        if (armL > 1e-3f) Octopus.Brain.RangeLengths = Mathf.Max(Octopus.Brain.RangeLengths, 0.8f * tallest / armL);
        int held;
        using (PhysicsSurfaceProbe.Backfaces()) held = Octopus.AnchorNearby();
        Octopus.Viewer = Camera.main != null ? Camera.main.transform : player;
        Octopus.Brain.Prey.Add(new PlayerPrey(player));

        // the prey creature on the floor, walking around the trunks
        if (preySpecies != null)
        {
            float pScale = preyScale > 0f ? preyScale : preySpecies.scale;
            EnclosureArea parea = EnclosureArea.For(centre, cUp, radius, preySpecies, CreatureLab.MaxScale(preySpecies, pScale));
            parea.obstacleCenters = new Vector3[Enclosure.obstacles.Count]; parea.obstacleRadii = new float[Enclosure.obstacles.Count];
            for (int i = 0; i < Enclosure.obstacles.Count; i++) { parea.obstacleCenters[i] = Enclosure.obstacles[i]; parea.obstacleRadii[i] = Enclosure.obstacles[i].w; }
            parea.keepOut = EnclosureArea.KeepOutFor(preySpecies, CreatureLab.MaxScale(preySpecies, pScale));
            var brain = new PreyBrain(new WanderBrain(parea, seed + 1), seed + 1);
            Vector3 pAt = centre + t0 * (0.5f * groveRadius);
            if (ground.Probe(pAt + cUp * 2f, cUp, kProbeWindow + 2f, out Vector3 pg, out _)) pAt = pg;
            Prey = world.Spawn(preySpecies, pAt, t1, brain, parea, seed + 1, pScale);
            OctopusCreature oct = Octopus;
            Octopus.Brain.Prey.Add(new CreaturePrey(Prey, brain) { GrabberPosition = () => oct.Body.HeadCentre });
        }

        if (logEvents)
        {
            Octopus.Controller.Events.Strike += p => Debug.Log($"[OctopusLab] strike at {Name(p)}");
            Octopus.Controller.Events.Grabbed += p => Debug.Log($"[OctopusLab] grabbed {Name(p)}");
            Octopus.Controller.Events.Released += p => Debug.Log($"[OctopusLab] released {Name(p)}");
        }
        Debug.Log($"[OctopusLab] grove of {Trees.Count} trees at {centre} (r {radius:F1} m, flatness {flatness:F3}, tallest {tallest:F1} m), " +
                  $"octopus at scale {ChosenScale:F2} holding with {held} arms, prey {(Prey != null ? preySpecies.name : "player only")}, hints {Hints.TreeCount} trees");
    }

    static string Name(IPreyTarget p) => p is PlayerPrey ? "the player" : p is CreaturePrey cp ? cp.Sim.Species.name : "prey";

    // The smallest scale (>= 0.5) at which a mid-canopy branch (the median hint radius near `at`) takes
    // minWrapTurns of wrap with the distal 70 % of the arm at WrapReach: turns x 2 pi (r + arm radius x s)
    // <= WrapReach x 0.7 L x s.
    float FitScale(in CephalopodSpec c, IBranchHints hints, Vector3 at, Vector3 up)
    {
        var res = new BranchHint[24];
        int n = hints != null ? hints.Query(at, up, -1f, 0f, 4f * c.arm.Length * treeScale, 0.01f, 2f, res) : 0;
        if (n == 0) return 1f;
        var radii = new float[n];
        for (int i = 0; i < n; i++) radii[i] = res[i].radius;
        System.Array.Sort(radii);
        float r = radii[n / 2];
        TentacleSpec a = c.arm;
        float armR = a.RadiusAt(0.65f * a.Length), budget = a.WrapReach * 0.7f * a.Length;
        float denom = budget - minWrapTurns * 2f * Mathf.PI * armR;
        float s = denom > 1e-3f ? minWrapTurns * 2f * Mathf.PI * r / denom : 4f;
        return Mathf.Clamp(s, 0.5f, 4f);
    }
}
