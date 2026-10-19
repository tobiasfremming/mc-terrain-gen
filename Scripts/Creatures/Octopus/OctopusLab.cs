using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The tree octopus enclosure of the Creature Lab (tree octopus design §8 task 10, user decisions 1-3): next
// to the lab's arc, on a flat spot, a GROVE of climbable trees with overlapping canopies (each an
// EnclosurePlant: its LOD0 bark is a real collider in the creatures' surface registry, and its skeleton feeds
// the branch hints), a curb around it, the octopus spawned and anchored in the central canopy, and a prey
// creature wandering the floor. The player is prey too and the viewer the octopus freezes for. Lab default:
// a grab holds 1.5 s, releases, fires events (logged here); no damage.
// Size: the species pipeline (species.scale x the individual's look.sizeJitter = sizeScale; user 2026-10-18:
// 4x the old lab size, TreeOctopus.scale 3.1). octopusScale > 0 overrides the species scale; fitScaleToBranches
// (opt-in) instead chooses it so a mid-canopy branch takes at least MinWrapTurns of wrap (user decision 3, the
// old default, 0.77 in the lab's grove). The curb is sized from the octopus: its goals and holds stay inside.
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
    [Tooltip("How many prey creatures (the swinger's grove: several small spiders).")]
    public int preyCount = 1;
    [Tooltip("The prey climbs the grove's trees (ClimbingBrain on their real bark; the species needs climb.canClimb).")]
    public bool preyClimbs = false;
    [Tooltip("A prey that was eaten (mouth feeders: the swinger) comes back after this many seconds, at a tree's foot.")]
    public float respawnSeconds = 20f;
    [Tooltip("Octopus size scale; 0 = the species' scale (x the individual's size jitter).")]
    public float octopusScale = 0f;
    [Tooltip("Opt-in: ignore the species' scale and fit the size to the grove's branches (MinWrapTurns).")]
    public bool fitScaleToBranches = false;
    public float minWrapTurns = 1.2f;
    public int seed = 31;
    public Material borderMaterial;
    public bool logEvents = true;
    [Tooltip("Cloak (dither-dissolve) on: off by default until the look is reworked; the species' cephalopod.cloakEnabled also turns it on.")]
    public bool cloakEnabled = false;
    [Tooltip("Role readout (top left): what the octopus has each arm do, its state, the force it puts on the body, and role changes per minute.")]
    public bool showRoles = true;
    [Tooltip("Also log every role change to the console.")]
    public bool logRoles = false;

    const float kWaitSeconds = 20f, kProbeWindow = 6f, kGap = 6f, kMargin = 1.5f;

    public OctopusCreature Octopus { get; private set; }
    public CreatureSim Prey { get; private set; }
    public readonly List<CreatureSim> PreySims = new List<CreatureSim>();
    // placed (or given up): the next OctopusLab on this object waits for it, so two groves never take one spot
    public bool Done { get; private set; }
    public int Meals { get; private set; }
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
        // groves one after another (each keeps clear of the ones placed before it)
        while (!EarlierDone() && waited <= 3f * kWaitSeconds) { yield return null; waited += Time.unscaledDeltaTime; }
        if (player == null || world.Ground == null) { Debug.LogWarning("[OctopusLab] no ground / player; no grove placed."); Done = true; yield break; }
        try { Place(world, player); }
        finally { Done = true; }
    }

    bool EarlierDone()
    {
        foreach (OctopusLab l in GetComponents<OctopusLab>())
        {
            if (l == this) return true;
            if (l.enabled && l.octopus != null && !l.Done) return false;
        }
        return true;
    }

    int Index()
    {
        OctopusLab[] all = GetComponents<OctopusLab>();
        for (int i = 0; i < all.Length; i++) if (all[i] == this) return i;
        return 0;
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
        // the octopus's goals stay EnclosureRadius - 1.6 L from the curb and its holds 1.3 L (OctopusBrain.Clip,
        // OctopusController.HoldInside): the curb lies that far beyond the grove, for the largest individual, so
        // the whole grove is its to cross (a 4x octopus in a curb sized for 1x could not leave the centre tree)
        float plannedScale = octopusScale > 0f ? octopusScale : fitScaleToBranches ? 4f : octopus.scale;
        float armReach = 1.6f * octopus.cephalopod.arm.Length * CreatureLab.MaxScale(octopus, plannedScale);
        float radius = groveRadius + Mathf.Max(kMargin, armReach);

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
        ChosenScale = octopusScale > 0f ? octopusScale : !fitScaleToBranches ? octopus.scale : FitScale(octopus.cephalopod, Hints, canopyRegions.Count > 0 ? canopyRegions[0] : centre + cUp * (0.55f * height), cUp);

        // the octopus: in the central canopy, anchored
        var area = new EnclosureArea { center = centre, up = cUp, radius = radius, safeRadius = groveRadius, maxHeight = tallest };
        Vector3 spawn = canopyRegions.Count > 0 ? canopyRegions[0] : centre + cUp * (0.5f * height);
        Octopus = world.SpawnOctopus(octopus, spawn, Quaternion.FromToRotation(Vector3.up, cUp), area, seed, ChosenScale, Hints);
        if (cloakEnabled) Octopus.Brain.CloakEnabled = true;
        foreach (Vector3 r in canopyRegions) Octopus.Brain.CanopyRegions.Add(r);
        // from the canopy it must see the grove floor: perception reaches the ground below the tallest canopy
        float armL = Octopus.Body.Arms[0].Length;
        if (armL > 1e-3f) Octopus.Brain.RangeLengths = Mathf.Max(Octopus.Brain.RangeLengths, 0.8f * tallest / armL);
        int held;
        using (PhysicsSurfaceProbe.Backfaces()) held = Octopus.AnchorNearby();
        Octopus.Viewer = Camera.main != null ? Camera.main.transform : player;
        Octopus.Brain.Prey.Add(new PlayerPrey(player));

        // the prey creatures: on the floor walking around the trunks, or (preyClimbs: the swinger's spiders) climbing
        // the trees' real bark
        if (preySpecies != null)
        {
            float pScale = preyScale > 0f ? preyScale : preySpecies.scale;
            preyArea = EnclosureArea.For(centre, cUp, radius, preySpecies, CreatureLab.MaxScale(preySpecies, pScale));
            int no = Enclosure.obstacles.Count;
            preyArea.obstacleCenters = new Vector3[no]; preyArea.obstacleRadii = new float[no];
            for (int i = 0; i < no; i++) { preyArea.obstacleCenters[i] = Enclosure.obstacles[i]; preyArea.obstacleRadii[i] = Enclosure.obstacles[i].w; }
            preyArea.keepOut = EnclosureArea.KeepOutFor(preySpecies, CreatureLab.MaxScale(preySpecies, pScale));
            if (preyClimbs && preySpecies.climb.canClimb)
            {
                preyArea.obstacleClimbable = new bool[no];
                for (int i = 0; i < no; i++) preyArea.obstacleClimbable[i] = true;
                preyArea.maxHeight = 0.8f * tallest;
            }
            preyWorld = world; preyScaleUsed = pScale; preyCentre = centre; preyUp = cUp; preyT0 = t0; preyT1 = t1; preyRadius = groveRadius;
            for (int k = 0; k < Mathf.Max(1, preyCount); k++) SpawnPrey(k);
            Prey = PreySims.Count > 0 ? PreySims[0] : null;
        }
        Octopus.Controller.Events.Eaten += OnEaten;

        if (logEvents)
        {
            Octopus.Controller.Events.Eaten += p => Debug.Log($"[OctopusLab] {octopus.name} ate {Name(p)}");
            Octopus.Controller.Events.Strike += p => Debug.Log($"[OctopusLab] strike at {Name(p)}");
            Octopus.Controller.Events.Grabbed += p => Debug.Log($"[OctopusLab] grabbed {Name(p)}");
            Octopus.Controller.Events.Released += p => Debug.Log($"[OctopusLab] released {Name(p)}");
        }
        Debug.Log($"[OctopusLab] grove of {Trees.Count} trees at {centre} (r {radius:F1} m, flatness {flatness:F3}, tallest {tallest:F1} m), " +
                  $"octopus at scale {ChosenScale:F2} holding with {held} arms, prey {(Prey != null ? preySpecies.name : "player only")}, hints {Hints.TreeCount} trees");
    }

    // ------------------------------------------------------------------ prey (spawn, eaten, respawn)

    EnclosureArea preyArea;
    CreatureWorld preyWorld;
    float preyScaleUsed, preyRadius;
    Vector3 preyCentre, preyUp, preyT0, preyT1;
    int preySpawned;
    readonly List<float> respawnAt = new List<float>();

    // Prey k: at a tree's foot (climbers: a little out from the trunk) or on the floor between the trees.
    void SpawnPrey(int k)
    {
        int seedK = seed + 1 + 17 * preySpawned++;
        float ring = preyClimbs ? 0f : 0.5f * preyRadius;
        Vector3 at = preyCentre + preyT0 * ring;
        if (preyClimbs && preyArea.obstacleCenters != null && preyArea.obstacleCenters.Length > 0)
        {
            int o = (k + preySpawned) % preyArea.obstacleCenters.Length;
            float a = 2.4f * preySpawned;
            Vector3 off = (preyT0 * Mathf.Cos(a) + preyT1 * Mathf.Sin(a)) * (preyArea.obstacleRadii[o] + 0.6f);
            at = preyArea.obstacleCenters[o] + off;
        }
        if (preyWorld.Ground.Probe(at + preyUp * 2f, preyUp, kProbeWindow + 2f, out Vector3 pg, out _)) at = pg;
        ICreatureBrain inner = preyClimbs ? (ICreatureBrain)new ClimbingBrain(preyArea, seedK, 0.9f) : new WanderBrain(preyArea, seedK);
        var brain = new PreyBrain(inner, seedK);
        CreatureSim sim = preyWorld.Spawn(preySpecies, at, preyT1, brain, preyArea, seedK, preyScaleUsed);
        PreySims.Add(sim);
        OctopusCreature oct = Octopus;
        Octopus.Brain.Prey.Add(new CreaturePrey(sim, brain) { GrabberPosition = () => oct.Body.HeadCentre });
    }

    // Eaten (a mouth feeder's grab-and-eat): the prey is gone - no gore, it simply leaves the world - and another
    // comes back at a tree's foot after respawnSeconds.
    void OnEaten(IPreyTarget p)
    {
        Meals++;
        if (!(p is CreaturePrey cp)) return;
        Octopus.Brain.Prey.Remove(p);
        PreySims.Remove(cp.Sim);
        if (preyWorld != null) preyWorld.Despawn(cp.Sim);
        respawnAt.Add(Time.time + respawnSeconds);
    }

    void Update()
    {
        for (int i = respawnAt.Count - 1; i >= 0; i--)
            if (Time.time >= respawnAt[i] && preySpecies != null && preyWorld != null) { respawnAt.RemoveAt(i); SpawnPrey(PreySims.Count); }
    }

    // ------------------------------------------------------------------ role readout (plan T5)
    // Rebuilt 4 times a second into one cached string (no per-frame allocation from it; GUI.Box itself draws the
    // cached text). Per arm: role / state / force on the body in body weights; then the planned speed, the
    // pullers, holds, and role changes per minute over the last minute.
    readonly System.Text.StringBuilder roleText = new System.Text.StringBuilder(512);
    string roleCache = "";
    float roleNext;
    readonly ArmRole[] lastRoles = new ArmRole[16];
    readonly float[] changeTimes = new float[256];
    int changeCount, changeHead;
    bool rolesInit;

    void LateUpdate()
    {
        if (Octopus == null || (!showRoles && !logRoles)) return;
        OctopusController c = Octopus.Controller;
        ArmBlackboard b = c.Board;
        int n = Mathf.Min(b.Arms, lastRoles.Length);
        for (int a = 0; a < n; a++)
        {
            if (!rolesInit) { lastRoles[a] = b.Role[a]; continue; }
            if (b.Role[a] == lastRoles[a]) continue;
            if (logRoles) Debug.Log($"[OctopusLab] t {c.Time:F1} arm {a}: {lastRoles[a]} -> {b.Role[a]} ({c.Agents[a].Mode})");
            lastRoles[a] = b.Role[a];
            changeTimes[changeHead] = c.Time; changeHead = (changeHead + 1) % changeTimes.Length;
            if (changeCount < changeTimes.Length) changeCount++;
        }
        rolesInit = true;
        if (!showRoles || Time.unscaledTime < roleNext) return;
        roleNext = Time.unscaledTime + 0.25f;
        int recent = 0;
        for (int i = 0; i < changeCount; i++) if (c.Time - changeTimes[i] <= 60f) recent++;
        float minutes = Mathf.Clamp(c.Time / 60f, 1f / 60f, 1f);
        float weight = Octopus.Body.TotalMass * OctopusController.Gravity;
        roleText.Length = 0;
        roleText.Append(octopus.name).Append("  ").Append(Octopus.Brain.Mode).Append("  intent ").Append(c.Intent.mode)
                .Append("  speed ").Append(c.Board.DesiredVelocity.magnitude.ToString("F2")).Append(" m/s  holds ").Append(b.AnchoredCount)
                .Append("  role changes/min/arm ").Append((recent / (minutes * n)).ToString("F1")).Append('\n');
        // the intent: where it is going, its route over the branches (waypoint / count), the swing
        ArmCoordinator co = c.Coordinator;
        BranchRoute route = c.Route;
        SwingSequencer q = c.Sequencer;
        if (q != null)
        {
            // the planned body: the sequencer's step, the swing, the plan and the arms' uses
            roleText.Append("goal ").Append(Octopus.Brain.Goal.ToString("F1")).Append("  trips ").Append(Octopus.Brain.Trips).Append(" arrived ").Append(Octopus.Brain.Arrivals).Append("  route ");
            if (route.Valid) { roleText.Append(route.Current).Append('/').Append(route.Count); if (!route.ReachesGoal) roleText.Append(" (partial)"); }
            else roleText.Append('-');
            roleText.Append('\n').Append("step ").Append(q.Mode).Append("  swing ").Append((q.Swing.Amplitude * Mathf.Rad2Deg).ToString("F0"))
                    .Append(" / ").Append((q.TargetAmplitude * Mathf.Rad2Deg).ToString("F0")).Append(" deg  next hold ").Append(q.HasNext ? q.NextHold.ToString("F1") : "-");
            if (q.Mode == SwingSequencer.Step.Pump || q.Mode == SwingSequencer.Step.Reach) roleText.Append("  apex in ").Append(q.PredictedApexTime.ToString("F2")).Append(" s");
            roleText.Append('\n').Append("transfers ").Append(q.Transfers).Append("  catches ").Append(q.Catches).Append('/').Append(q.CatchAttempts)
                    .Append("  hauls ").Append(q.Hauls).Append("  falls ").Append(q.Falls).Append("  sleeps ").Append(q.Sleeps).Append(Octopus.Body.Asleep ? "  ASLEEP" : "").Append('\n');
            for (int a = 0; a < n; a++) roleText.Append(a).Append("  ").Append(q.Use[a]).Append('\n');
        }
        else
        {
        roleText.Append("goal ").Append(Octopus.Brain.Goal.ToString("F1")).Append("  trips ").Append(Octopus.Brain.Trips).Append(" arrived ").Append(Octopus.Brain.Arrivals).Append("  route ");
        if (route.Valid) { roleText.Append(route.Current).Append('/').Append(route.Count); if (!route.ReachesGoal) roleText.Append(" (partial)"); }
        else roleText.Append('-');
        roleText.Append("  swings ").Append(co.Swings).Append(" (branch to branch ").Append(co.BranchSwings).Append(")  swing ");
        if (co.Swing.Valid) roleText.Append((co.Swing.Theta * Mathf.Rad2Deg).ToString("F0")).Append(" deg"); else roleText.Append('-');
        if (co.Pumping) roleText.Append("  pumping");
        roleText.Append('\n');
        for (int a = 0; a < n; a++)
        {
            // P: the grip group the body hangs from, C: the group catching ahead
            roleText.Append(a).Append("  ").Append(b.Role[a].ToString().PadRight(7)).Append(c.Agents[a].Mode.ToString().PadRight(9))
                    .Append(co.GroupOf(a) < 0 ? "   " : co.GroupOf(a) == b.PivotGroup ? "P  " : "C  ")
                    .Append((Octopus.Body.ArmForce[a].magnitude / weight).ToString("F1")).Append(" W");
            if (b.Pulling[a]) roleText.Append("  pulls");
            if (b.Anchored[a]) roleText.Append("  holds");
            roleText.Append('\n');
        }
        }
        // the mouth tentacles (mouth feeders: the swinger): role, what they do, meals
        MouthFeeder fd = c.Feeder;
        if (fd != null)
        {
            roleText.Append("mouth x").Append(fd.Count).Append("  ").Append(fd.RoleOf(0).ToString().PadRight(7)).Append(fd.Mode.ToString().PadRight(9));
            if (fd.Feeding && fd.Prey != null) roleText.Append(" prey ").Append(fd.DistanceTo(fd.Prey).ToString("F2")).Append(" m (reach ").Append(fd.Reach.ToString("F2")).Append(')');
            roleText.Append("  grabs ").Append(fd.Grabs).Append(" eaten ").Append(fd.Eats).Append('\n');
        }
        roleCache = roleText.ToString();
    }

    void OnGUI()
    {
        if (!showRoles || Octopus == null || roleCache.Length == 0) return;
        // one box per grove, stacked (the octopus's, then the swinger's)
        int lines = 1;
        for (int i = 0; i < roleCache.Length; i++) if (roleCache[i] == '\n') lines++;
        GUI.Box(new Rect(10f, 10f + Index() * 200f, 560f, 6f + 16f * lines), roleCache);
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
