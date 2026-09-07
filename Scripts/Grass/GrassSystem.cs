using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

// GPU-resident grass. Owns the blade pool, the compute pipeline
// (GrassScatter.compute) and the indirect draws (Grass.shader); knows nothing
// about chunks or biomes beyond "here is a mesh, here are the per-channel
// densities". TerrainGrass feeds it the clipmap, GrassLab feeds it a test
// field.
//
// Data flow, per chunk mesh (once, on build):
//   mesh vertex/index buffers --CSScatter--> _blades[slot range], _slotCounts[slot]
// per frame:
//   CPU: frustum + distance test on ~100 slot bounds -> _visibleSlots
//   GPU: CSCull over those slots' blades -> _visibleBlades (3 LOD regions)
//        + instance counts in _args
//   3 x RenderPrimitivesIndexedIndirect, one per LOD (5/3/1 segments)
//
// Nothing per blade ever crosses the bus; the CPU work per frame is a few
// hundred AABB tests, one small SetData and three draw calls.
public sealed class GrassSystem : System.IDisposable
{
    // Must match GrassCommon.hlsl's Blade.
    [StructLayout(LayoutKind.Sequential)]
    struct Blade { public Vector3 pos; public uint normalOct; public uint seed; public uint colors; }
    public const int BladeStride = 24;

    [StructLayout(LayoutKind.Sequential)]
    struct SlotRef { public uint slot, bladeBase, pad0, pad1; }

    public const int LodCount = 3;
    static readonly int[] kLodSegments = { 5, 3, 1 };

    // Which biomes grow grass, how densely, and in what colours. Slot i is
    // BiomeWorld.biomes[i]; the compute shader weights the slots with the
    // biome selection at each triangle (BiomeSelect.hlsl via the shader
    // globals MCChunkManager publishes -- see BIOME_SHADING.md).
    public struct BiomeParams
    {
        public const int MaxBiomes = 8; // = BiomeDensityField.MaxBiomes = MC_MAX_BIOMES

        public Vector4[] table;      // x blades per m^2, y local height where grass stops (radius added), z fade metres
        public Vector4[] baseColors;
        public Vector4[] tipColors;
        // False: no biome world (GrassLab, plain heightfields). Slot 0 applies
        // everywhere and the biome globals are never read.
        public bool useBiomeSelect;

        public static BiomeParams Empty()
        {
            var p = new BiomeParams { table = new Vector4[MaxBiomes], baseColors = new Vector4[MaxBiomes], tipColors = new Vector4[MaxBiomes] };
            for (int i = 0; i < MaxBiomes; i++) p.table[i] = new Vector4(0f, 1e9f, 1f, 0f);
            return p;
        }

        public static BiomeParams Uniform(float density, Color baseColor, Color tipColor)
        {
            var p = Empty();
            p.table[0] = new Vector4(density, 1e9f, 1f, 0f);
            p.baseColors[0] = baseColor;
            p.tipColors[0] = tipColor;
            return p;
        }

        public void Set(int slot, float density, float heightLine, float heightBlend, Color baseColor, Color tipColor)
        {
            table[slot] = new Vector4(Mathf.Max(0f, density), heightLine, Mathf.Max(0.01f, heightBlend), 0f);
            baseColors[slot] = baseColor;
            tipColors[slot] = tipColor;
        }
    }

    // One chunk's share of the pool.
    public sealed class Slot
    {
        public int index;
        public int bladeBase;
        public Bounds bounds;      // world-space bounds of the source mesh
        public bool visible = true;
        public int version = -1;   // MarchingChunk.MeshVersion this was scattered from
        public int level;
        public int lastSeen;       // owner's frame stamp
    }

    readonly GrassSettings _settings;
    readonly ComputeShader _cs;
    readonly Material _material;
    readonly int _capacity, _maxSlots, _lodCapacity;

    GraphicsBuffer _blades, _slotCounts, _slotExpected, _visibleSlots, _visibleBlades, _counters, _args, _indices;
    readonly int _kClearSlot, _kMeasure, _kScatter, _kClearArgs, _kCull, _kFinalize;

    readonly Slot[] _slots;
    readonly Stack<int> _free = new();
    readonly SlotRef[] _slotRefs;
    readonly MaterialPropertyBlock[] _mpb = new MaterialPropertyBlock[LodCount];
    readonly Plane[] _planes = new Plane[6];
    readonly Vector4[] _planeVec = new Vector4[6];
    bool _strideWarned, _rawWarned;

    // Stats (visible counts arrive a few frames late via async readback).
    public readonly int[] VisibleCounts = new int[LodCount];
    public int ActiveSlots => _maxSlots - _free.Count;
    public int DispatchedSlots { get; private set; }
    public int CulledThreads { get; private set; }
    public long PoolBytes => (long)_maxSlots * _capacity * BladeStride;
    int _readbackCooldown;

    static readonly int
        kVerts = Shader.PropertyToID("_Verts"), kIndices = Shader.PropertyToID("_Indices"),
        kBlades = Shader.PropertyToID("_Blades"), kSlotCounts = Shader.PropertyToID("_SlotCounts"), kSlotExpected = Shader.PropertyToID("_SlotExpected"),
        kTriCount = Shader.PropertyToID("_TriCount"), kSlotIndex = Shader.PropertyToID("_SlotIndex"),
        kSlotBase = Shader.PropertyToID("_SlotBase"), kSlotCapacity = Shader.PropertyToID("_SlotCapacity"),
        kLocalToWorld = Shader.PropertyToID("_LocalToWorld"), kPlanetCenter = Shader.PropertyToID("_PlanetCenter"),
        kGrassBiome = Shader.PropertyToID("_GrassBiome"), kGrassBase = Shader.PropertyToID("_GrassBase"),
        kGrassTip = Shader.PropertyToID("_GrassTip"), kGrassUseBiomes = Shader.PropertyToID("_GrassUseBiomes"),
        kSlopeCosStart = Shader.PropertyToID("_SlopeCosStart"),
        kSlopeCosEnd = Shader.PropertyToID("_SlopeCosEnd"), kDensityScale = Shader.PropertyToID("_DensityScale"),
        kUpBlend = Shader.PropertyToID("_UpBlend"), kSeedSalt = Shader.PropertyToID("_SeedSalt"),
        kVisibleSlots = Shader.PropertyToID("_VisibleSlots"), kVisibleSlotCount = Shader.PropertyToID("_VisibleSlotCount"),
        kVisibleBlades = Shader.PropertyToID("_VisibleBlades"), kCounters = Shader.PropertyToID("_Counters"),
        kLodCapacity = Shader.PropertyToID("_LodCapacity"), kFrustumPlanes = Shader.PropertyToID("_FrustumPlanes"),
        kCamPos = Shader.PropertyToID("_CamPos"), kFadeStart = Shader.PropertyToID("_FadeStart"),
        kFadeEnd = Shader.PropertyToID("_FadeEnd"), kLod0Dist = Shader.PropertyToID("_Lod0Dist"),
        kLod1Dist = Shader.PropertyToID("_Lod1Dist"), kBoundsRadius = Shader.PropertyToID("_BoundsRadius"),
        kLodOffset = Shader.PropertyToID("_LodOffset"), kSegments = Shader.PropertyToID("_Segments"),
        kBladeSize = Shader.PropertyToID("_BladeSize"), kWindDir = Shader.PropertyToID("_WindDir"),
        kWindParams = Shader.PropertyToID("_WindParams"), kBendPos = Shader.PropertyToID("_BendPos"),
        kFadeParams = Shader.PropertyToID("_FadeParams");

    public GrassSystem(GrassSettings settings)
    {
        _settings = settings;
        _cs = settings.compute;
        _material = settings.material;
        _capacity = Mathf.Clamp(settings.slotCapacity, 64, 1 << 18);
        _maxSlots = Mathf.Clamp(settings.maxSlots, 1, 2048);
        _lodCapacity = Mathf.Clamp(settings.maxVisiblePerLod, 1024, 1 << 22);

        _kClearSlot = _cs.FindKernel("CSClearSlot");
        _kMeasure = _cs.FindKernel("CSMeasure");
        _kScatter = _cs.FindKernel("CSScatter");
        _kClearArgs = _cs.FindKernel("CSClearArgs");
        _kCull = _cs.FindKernel("CSCull");
        _kFinalize = _cs.FindKernel("CSFinalizeArgs");

        _blades = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _maxSlots * _capacity, BladeStride);
        _slotCounts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _maxSlots, 4);
        _slotCounts.SetData(new uint[_maxSlots]);
        _slotExpected = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _maxSlots, 4);
        _slotExpected.SetData(new uint[_maxSlots]);
        _visibleSlots = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _maxSlots, 16);
        _visibleBlades = new GraphicsBuffer(GraphicsBuffer.Target.Structured, LodCount * _lodCapacity, 4);
        // D3D11 refuses raw/structured compute views on an indirect-args
        // buffer, so the kernels write a plain structured mirror and Render
        // copies it across (60 bytes) once the cull has run.
        _counters = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource, LodCount * 5, 4);
        _args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.CopyDestination, LodCount * 5, 4);

        // One index buffer holding the strip pattern for every LOD; the args
        // point each draw at its own range.
        var idx = new List<ushort>();
        var args = new uint[LodCount * 5];
        for (int l = 0; l < LodCount; l++)
        {
            int segs = kLodSegments[l];
            int start = idx.Count;
            for (int s = 0; s < segs - 1; s++)
            {
                ushort a = (ushort)(2 * s), b = (ushort)(2 * s + 1), c = (ushort)(2 * s + 2), d = (ushort)(2 * s + 3);
                idx.Add(a); idx.Add(b); idx.Add(c);
                idx.Add(b); idx.Add(d); idx.Add(c);
            }
            idx.Add((ushort)(2 * (segs - 1))); idx.Add((ushort)(2 * (segs - 1) + 1)); idx.Add((ushort)(2 * segs));
            args[l * 5 + 0] = (uint)(idx.Count - start); // indexCountPerInstance
            args[l * 5 + 1] = 0;                          // instanceCount (GPU-written)
            args[l * 5 + 2] = (uint)start;                // startIndex
            args[l * 5 + 3] = 0;                          // baseVertex
            args[l * 5 + 4] = 0;                          // startInstance
        }
        _indices = new GraphicsBuffer(GraphicsBuffer.Target.Index, idx.Count, 2);
        _indices.SetData(idx.ToArray());
        _args.SetData(args);
        _counters.SetData(args);

        _slots = new Slot[_maxSlots];
        _slotRefs = new SlotRef[_maxSlots];
        for (int i = _maxSlots - 1; i >= 0; i--)
        {
            _slots[i] = new Slot { index = i, bladeBase = i * _capacity };
            _free.Push(i);
        }
        for (int l = 0; l < LodCount; l++) _mpb[l] = new MaterialPropertyBlock();
    }

    // True while the settings' budget fields still match what this instance
    // allocated. A mismatch means the owner should Dispose and rebuild.
    public bool MatchesBudget(GrassSettings s) =>
        s != null && s.compute == _cs && s.material == _material &&
        Mathf.Clamp(s.slotCapacity, 64, 1 << 18) == _capacity &&
        Mathf.Clamp(s.maxSlots, 1, 2048) == _maxSlots &&
        Mathf.Clamp(s.maxVisiblePerLod, 1024, 1 << 22) == _lodCapacity;

    public Slot Acquire()
    {
        if (_free.Count == 0) return null;
        var s = _slots[_free.Pop()];
        s.version = -1;
        s.visible = true;
        return s;
    }

    public void Release(Slot s)
    {
        if (s == null) return;
        s.version = -1;
        _free.Push(s.index);
    }

    // Scatter blades over `mesh` into `slot`. The mesh must use MarchingChunk's
    // vertex layout (pos, normal, color, 40 bytes) with Raw buffer targets and
    // a UInt32 index buffer -- which every chunk mesh does. Cheap enough to
    // call for every rebuild: one small dispatch, no readback.
    public void Scatter(Slot slot, Mesh mesh, Matrix4x4 localToWorld, int level, in BiomeParams biome, Vector4 planetCenter)
    {
        if (slot == null || mesh == null || mesh.subMeshCount == 0) return;
        int triCount = (int)(mesh.GetIndexCount(0) / 3);
        if (triCount == 0 || mesh.vertexCount == 0)
        {
            _cs.SetInt(kSlotIndex, slot.index);
            _cs.SetBuffer(_kClearSlot, kSlotCounts, _slotCounts);
            _cs.SetBuffer(_kClearSlot, kSlotExpected, _slotExpected);
            _cs.Dispatch(_kClearSlot, 1, 1, 1);
            return;
        }
        if (mesh.GetVertexBufferStride(0) != 40)
        {
            if (!_strideWarned) Debug.LogWarning($"[GrassSystem] '{mesh.name}' has a {mesh.GetVertexBufferStride(0)}-byte vertex; grass expects the 40-byte chunk layout. Skipping.");
            _strideWarned = true;
            return;
        }
        if ((mesh.vertexBufferTarget & GraphicsBuffer.Target.Raw) == 0 || (mesh.indexBufferTarget & GraphicsBuffer.Target.Raw) == 0)
        {
            if (!_rawWarned) Debug.LogWarning($"[GrassSystem] '{mesh.name}' buffers are not Raw-addressable (set vertexBufferTarget/indexBufferTarget |= Raw before filling). Skipping.");
            _rawWarned = true;
            return;
        }

        var vb = mesh.GetVertexBuffer(0);
        var ib = mesh.GetIndexBuffer();
        if (vb == null || ib == null) { vb?.Dispose(); ib?.Dispose(); return; }

        float levelScale = level < _settings.levelDensity.Length ? _settings.levelDensity[level] : _settings.levelDensity[^1];

        _cs.SetInt(kSlotIndex, slot.index);
        _cs.SetBuffer(_kClearSlot, kSlotCounts, _slotCounts);
        _cs.SetBuffer(_kClearSlot, kSlotExpected, _slotExpected);
        _cs.Dispatch(_kClearSlot, 1, 1, 1);

        _cs.SetBuffer(_kMeasure, kVerts, vb);
        _cs.SetBuffer(_kMeasure, kIndices, ib);
        _cs.SetBuffer(_kMeasure, kSlotExpected, _slotExpected);
        _cs.SetBuffer(_kScatter, kVerts, vb);
        _cs.SetBuffer(_kScatter, kIndices, ib);
        _cs.SetBuffer(_kScatter, kBlades, _blades);
        _cs.SetBuffer(_kScatter, kSlotCounts, _slotCounts);
        _cs.SetBuffer(_kScatter, kSlotExpected, _slotExpected);
        _cs.SetInt(kTriCount, triCount);
        _cs.SetInt(kSlotBase, slot.bladeBase);
        _cs.SetInt(kSlotCapacity, _capacity);
        _cs.SetMatrix(kLocalToWorld, localToWorld);
        _cs.SetVector(kPlanetCenter, planetCenter);
        _cs.SetVectorArray(kGrassBiome, biome.table);
        _cs.SetVectorArray(kGrassBase, biome.baseColors);
        _cs.SetVectorArray(kGrassTip, biome.tipColors);
        _cs.SetFloat(kGrassUseBiomes, biome.useBiomeSelect ? 1f : 0f);
        _cs.SetFloat(kSlopeCosStart, Mathf.Cos(_settings.slopeStartDeg * Mathf.Deg2Rad));
        _cs.SetFloat(kSlopeCosEnd, Mathf.Cos(_settings.slopeEndDeg * Mathf.Deg2Rad));
        _cs.SetFloat(kDensityScale, _settings.densityScale * levelScale);
        _cs.SetFloat(kUpBlend, _settings.upBlend);
        _cs.SetFloat(kSeedSalt, 0.37f);
        int groups = (triCount + 63) / 64;
        _cs.Dispatch(_kMeasure, groups, 1, 1);   // pass 1: total demand
        _cs.Dispatch(_kScatter, groups, 1, 1);   // pass 2: place, thinned to fit the slot

        vb.Dispose();
        ib.Dispose();
    }

    // Cull and draw every slot in `slots` that is visible and inside the
    // camera's frustum and the fade distance. `eye` is where distances are
    // measured from (the player, not necessarily the camera).
    public void Render(Camera cam, Vector3 eye, Vector3 bendPos, Vector3 windDir, float windScale,
                       Vector4 planetCenter, in BiomeParams biome, IReadOnlyList<Slot> slots, int layer = 0)
    {
        if (cam == null || _material == null) return;

        float fadeEnd = _settings.fadeEnd;
        float pad = _settings.bladeHeight * (1f + _settings.heightVariation) * 1.5f;
        GeometryUtility.CalculateFrustumPlanes(cam, _planes);
        for (int i = 0; i < 6; i++)
        {
            var pn = _planes[i].normal;
            _planeVec[i] = new Vector4(pn.x, pn.y, pn.z, _planes[i].distance);
        }

        // Dispatch limit: 65535 groups of 64 threads.
        int maxSlotsPerDispatch = Mathf.Max(1, (65535 * 64) / _capacity);
        int n = 0;
        for (int i = 0; i < slots.Count && n < maxSlotsPerDispatch; i++)
        {
            var s = slots[i];
            if (s == null || !s.visible || s.version < 0) continue;
            var b = s.bounds;
            b.Expand(pad * 2f);
            if (b.SqrDistance(eye) > fadeEnd * fadeEnd) continue;
            if (!GeometryUtility.TestPlanesAABB(_planes, b)) continue;
            _slotRefs[n++] = new SlotRef { slot = (uint)s.index, bladeBase = (uint)s.bladeBase };
        }
        DispatchedSlots = n;
        CulledThreads = n * _capacity;

        _cs.SetBuffer(_kClearArgs, kCounters, _counters);
        _cs.Dispatch(_kClearArgs, 1, 1, 1);

        if (n > 0)
        {
            _visibleSlots.SetData(_slotRefs, 0, 0, n);
            _cs.SetBuffer(_kCull, kVisibleSlots, _visibleSlots);
            _cs.SetBuffer(_kCull, kSlotCounts, _slotCounts);
            _cs.SetBuffer(_kCull, kBlades, _blades);
            _cs.SetBuffer(_kCull, kVisibleBlades, _visibleBlades);
            _cs.SetBuffer(_kCull, kCounters, _counters);
            _cs.SetInt(kVisibleSlotCount, n);
            _cs.SetInt(kSlotCapacity, _capacity);
            _cs.SetInt(kLodCapacity, _lodCapacity);
            _cs.SetVectorArray(kFrustumPlanes, _planeVec);
            _cs.SetVector(kCamPos, eye);
            _cs.SetFloat(kFadeStart, _settings.fadeStart);
            _cs.SetFloat(kFadeEnd, fadeEnd);
            _cs.SetFloat(kLod0Dist, _settings.lod0Distance);
            _cs.SetFloat(kLod1Dist, _settings.lod1Distance);
            _cs.SetFloat(kBoundsRadius, pad);
            _cs.Dispatch(_kCull, (n * _capacity + 63) / 64, 1, 1);

            _cs.SetBuffer(_kFinalize, kCounters, _counters);
            _cs.SetInt(kLodCapacity, _lodCapacity);
            _cs.Dispatch(_kFinalize, 1, 1, 1);
            Graphics.CopyBuffer(_counters, _args);
        }

        if (n == 0) { for (int l = 0; l < LodCount; l++) VisibleCounts[l] = 0; return; }

        var rp = new RenderParams(_material)
        {
            worldBounds = new Bounds(eye, Vector3.one * (fadeEnd * 2f + 100f)),
            shadowCastingMode = _settings.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
            receiveShadows = _settings.receiveShadows,
            layer = layer,
        };
        Vector3 wd = windDir.sqrMagnitude > 1e-6f ? windDir.normalized : Vector3.right;
        var bladeSize = new Vector4(_settings.bladeHeight, _settings.bladeWidth, _settings.heightVariation, _settings.widthVariation);
        var windV = new Vector4(wd.x, wd.y, wd.z, _settings.windStrength * windScale);
        var windP = new Vector4(_settings.windSpeed, _settings.gustScale, _settings.flutter, _settings.lean);
        var bend = new Vector4(bendPos.x, bendPos.y, bendPos.z, _settings.trampleRadius);
        var fade = new Vector4(_settings.fadeStart, fadeEnd, 0, 0);

        for (int l = 0; l < LodCount; l++)
        {
            var m = _mpb[l];
            m.SetBuffer(kBlades, _blades);
            m.SetBuffer(kVisibleBlades, _visibleBlades);
            m.SetInt(kLodOffset, l * _lodCapacity);
            m.SetInt(kSegments, kLodSegments[l]);
            m.SetVector(kBladeSize, bladeSize);
            m.SetVector(kWindDir, windV);
            m.SetVector(kWindParams, windP);
            m.SetVector(kBendPos, bend);
            m.SetVector(kFadeParams, fade);
            m.SetVector(kPlanetCenter, planetCenter);
            rp.matProps = m;
            Graphics.RenderPrimitivesIndexedIndirect(rp, MeshTopology.Triangles, _indices, _args, 1, l);
        }

        PumpStats();
    }

    void PumpStats()
    {
        if (--_readbackCooldown > 0) return;
        _readbackCooldown = 8;
        AsyncGPUReadback.Request(_counters, req =>
        {
            if (req.hasError || _counters == null || !_counters.IsValid()) return;
            var data = req.GetData<uint>();
            for (int l = 0; l < LodCount && l * 5 + 1 < data.Length; l++) VisibleCounts[l] = (int)data[l * 5 + 1];
        });
    }

    public void Dispose()
    {
        _blades?.Dispose(); _blades = null;
        _slotCounts?.Dispose(); _slotCounts = null;
        _slotExpected?.Dispose(); _slotExpected = null;
        _visibleSlots?.Dispose(); _visibleSlots = null;
        _visibleBlades?.Dispose(); _visibleBlades = null;
        _counters?.Dispose(); _counters = null;
        _args?.Dispose(); _args = null;
        _indices?.Dispose(); _indices = null;
    }
}
