using System;
using System.Collections.Generic;
using UnityEngine;

// Grass over the streaming terrain. Watches every live MarchingChunk (they
// register themselves in MarchingChunk.Active), gives each near-ring chunk a
// slot in the GrassSystem pool, re-scatters a slot whenever its chunk's mesh
// is rebuilt (MeshVersion), and drops it when the chunk is pooled. Which
// biomes grow grass comes from Biome.grassDensity:
// the compute shader evaluates the same biome selection (BiomeSelect.hlsl,
// through the globals MCChunkManager publishes) that the density blend and
// the terrain shader use, so grass stops exactly where the meadow does. See
// BIOME_SHADING.md.
//
// Sits next to PlantScatter in the scene. Costs nothing when
// WorldConfig.enablePlants is off.
[DisallowMultipleComponent]
public class TerrainGrass : MonoBehaviour
{
    public GrassSettings settings;
    [Tooltip("Read for enablePlants, the density field (biomes, planet) and wind. Falls back to the MCChunkManager's.")]
    public WorldConfig worldConfig;
    [Tooltip("Distances and trampling are measured from here. Left empty: the MCChunkManager's target (the player), then the main camera.")]
    public Transform viewer;
    [Tooltip("Frustum used for culling. Left empty: the main camera.")]
    public Camera cullCamera;
    [Tooltip("Blades per m^2 when the world has no BiomeDensityField to ask (plain heightfield worlds).")]
    public float fallbackDensity = 0f;
    public Color fallbackBase = new Color(0.16f, 0.32f, 0.08f);
    public Color fallbackTip = new Color(0.55f, 0.72f, 0.28f);

    GrassSystem _sys;
    MCChunkManager _manager;
    readonly Dictionary<MarchingChunk, GrassSystem.Slot> _slots = new();
    readonly List<GrassSystem.Slot> _slotList = new();
    readonly List<MarchingChunk> _stale = new();
    GrassSystem.BiomeParams _biome = GrassSystem.BiomeParams.Empty();
    int _biomeHash;
    int _stamp;
    bool _rescatterAll;
    string _status = "";

    public string Status => _status;
    public GrassSystem System => _sys;
    public int Starved { get; private set; }  // chunks that wanted a slot this frame and found the pool full

    void OnEnable() { TerrainTuning.Changed += OnTuningChanged; }

    void OnDisable()
    {
        TerrainTuning.Changed -= OnTuningChanged;
        _sys?.Dispose();
        _sys = null;
        _slots.Clear();
        _slotList.Clear();
    }

    void OnTuningChanged() => _rescatterAll = true;

    void Update()
    {
        if (!Application.isPlaying) { _status = "play mode only"; return; }

        WorldConfig cfg = ResolveConfig();
        if (cfg == null) { _status = "no WorldConfig"; return; }
        if (!cfg.enablePlants) { _status = "disabled in WorldConfig (enablePlants)"; return; }
        if (settings == null) { _status = "no GrassSettings"; return; }
        if (settings.compute == null || settings.material == null) { _status = "GrassSettings needs its compute shader and material"; return; }

        if (_sys != null && !_sys.MatchesBudget(settings))
        {
            _sys.Dispose(); _sys = null;
            _slots.Clear(); _slotList.Clear();
        }
        _sys ??= new GrassSystem(settings);

        DensityField field = cfg.EffectiveDensity;
        PlanetField planet = field as PlanetField;
        Vector4 planetCenter = planet != null ? new Vector4(planet.center.x, planet.center.y, planet.center.z, 1f) : Vector4.zero;
        BuildBiomeParams(field, planet);

        Camera cam = cullCamera != null ? cullCamera : Camera.main;
        Transform v = ResolveViewer(cam);
        if (cam == null || v == null) { _status = "no camera"; return; }

        SyncChunks(planetCenter);

        Vector3 wind = cfg.windDir;
        _sys.Render(cam, v.position, v.position, wind, cfg.windStrength, planetCenter, _biome, _slotList, gameObject.layer);
        _status = "OK";
    }

    WorldConfig ResolveConfig()
    {
        if (worldConfig != null) return worldConfig;
        if (_manager == null) _manager = FindFirstObjectByType<MCChunkManager>();
        return _manager != null ? _manager.worldConfig : null;
    }

    Transform ResolveViewer(Camera cam)
    {
        if (viewer != null) return viewer;
        if (_manager == null) _manager = FindFirstObjectByType<MCChunkManager>();
        if (_manager != null && _manager.target != null) return _manager.target;
        return cam != null ? cam.transform : null;
    }

    static BiomeDensityField BiomeFieldOf(DensityField field)
    {
        if (field is BiomeDensityField b) return b;
        if (field is PlanetField p) return p.surface as BiomeDensityField;
        return null;
    }

    // Per-biome grass table from the Biome assets. Slot i is biomes[i], the
    // same slot order the biome selection and MCChunkManager's shading globals
    // use. Where grass stops with height mirrors the terrain shader's own
    // ground shading: the Dolomite meadow line and the Mountain grass line
    // (radius added, as the shader's localHeight is the distance from the
    // planet centre in globe mode).
    void BuildBiomeParams(DensityField field, PlanetField planet)
    {
        var world = BiomeFieldOf(field);
        var p = GrassSystem.BiomeParams.Empty();
        int hash = 17;
        if (world == null || world.biomes == null || world.BiomeCount == 0)
        {
            p = GrassSystem.BiomeParams.Uniform(fallbackDensity, fallbackBase, fallbackTip);
            hash = HashCode.Combine(fallbackDensity, fallbackBase, fallbackTip);
        }
        else
        {
            p.useBiomeSelect = true;
            float radius = planet != null ? planet.radius : 0f;
            int n = Mathf.Min(world.BiomeCount, GrassSystem.BiomeParams.MaxBiomes);
            for (int i = 0; i < n; i++)
            {
                var b = world.biomes[i];
                if (b == null) continue;
                float line = 1e9f, blend = 1f;
                switch (b.terrain)
                {
                    case DolomiteVolumeField dolo:
                        line = radius + dolo.baseHeight + dolo.meadowLine;
                        blend = Mathf.Max(1f, dolo.meadowLineBlend);
                        break;
                    case ErodedHeightField ero:
                        line = radius + ero.baseHeight + ero.grassLine;
                        blend = Mathf.Max(1f, ero.grassLineBlend);
                        break;
                }
                p.Set(i, b.grassDensity, line, blend, b.grassColorBase, b.grassColorTip);
                hash = HashCode.Combine(hash, b.grassDensity, b.grassColorBase, b.grassColorTip, line, blend, i);
            }
            hash = HashCode.Combine(hash, world.seed, world.regionScale, world.sharpness, world.BiomeCount, radius);
        }
        hash = HashCode.Combine(hash, settings.densityScale, settings.slopeStartDeg, settings.slopeEndDeg, settings.upBlend, settings.maxLevel);
        for (int i = 0; i < settings.levelDensity.Length; i++) hash = HashCode.Combine(hash, settings.levelDensity[i]);
        if (hash != _biomeHash)
        {
            _biomeHash = hash;
            _rescatterAll = true;
            // The scatter reads the biome selection from shader globals. The
            // manager publishes them on TerrainTuning, which may not have
            // fired yet this session; publishing again is idempotent.
            if (world != null) MCChunkManager.PublishBiomeShadingGlobals(world, planet);
        }
        _biome = p;
    }

    void SyncChunks(Vector4 planetCenter)
    {
        _stamp++;
        Starved = 0;
        int budget = settings.scattersPerFrame;
        if (_rescatterAll)
        {
            foreach (var s in _slots.Values) s.version = -1;
            _rescatterAll = false;
        }

        var active = MarchingChunk.Active;
        for (int i = 0; i < active.Count; i++)
        {
            var ch = active[i];
            if (ch == null) continue;
            Mesh mesh = ch.RenderMesh;
            bool wanted = ch.lodLevel <= settings.maxLevel && mesh != null && mesh.vertexCount > 0 && mesh.subMeshCount > 0 && mesh.GetIndexCount(0) > 0;
            if (!wanted)
            {
                if (_slots.TryGetValue(ch, out var old)) { _sys.Release(old); _slots.Remove(ch); }
                continue;
            }
            if (!_slots.TryGetValue(ch, out var slot))
            {
                slot = _sys.Acquire();
                if (slot == null) { Starved++; continue; }
                _slots[ch] = slot;
            }
            slot.lastSeen = _stamp;
            slot.visible = ch.IsVisible;
            slot.level = ch.lodLevel;
            var b = mesh.bounds;
            b.center += ch.transform.position;
            slot.bounds = b;
            if (slot.version != ch.MeshVersion && budget > 0)
            {
                _sys.Scatter(slot, mesh, ch.transform.localToWorldMatrix, ch.lodLevel, _biome, planetCenter);
                slot.version = ch.MeshVersion;
                budget--;
            }
        }

        // Chunks that left the registry (pooled or destroyed) since last frame.
        _stale.Clear();
        foreach (var kv in _slots)
            if (kv.Value.lastSeen != _stamp) _stale.Add(kv.Key);
        foreach (var ch in _stale) { _sys.Release(_slots[ch]); _slots.Remove(ch); }

        _slotList.Clear();
        foreach (var s in _slots.Values) _slotList.Add(s);
    }
}
