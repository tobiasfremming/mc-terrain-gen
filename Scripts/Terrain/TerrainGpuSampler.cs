using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// Owns the GPU density compute shader (Shaders/Compute/TerrainDensity.
// compute), its persistent per-world parameter buffers, and the batched
// dispatch/readback machinery. See the "GPU Compute-Shader Acceleration for
// Terrain Density Fields" plan.
//
// Deliberately decoupled from MCChunkManager/ChunkMeshJob: its public
// surface is "here are N grid-fill requests (origin/step/counts), fill this
// float[] per request" -- the same (origin,countX,countY,countZ,step,dest)
// shape DensityField.SampleGrid already uses. Callers own scheduling,
// staleness checks, and priority (that's MCChunkManager's job, not yet
// wired to this class); this class owns dispatch/buffer mechanics only.
//
// IMPORTANT, not yet verified (no way to compile/run this outside Unity):
// StructuredBuffer element packing for a struct containing a float3 (see
// PlanetGpuParams) is tightly packed by declaration order with no implicit
// alignment padding -- unlike cbuffer members, which DO pad vectors to
// 16-byte boundaries. This assumption (that C#'s Marshal.SizeOf<T>() layout
// matches HLSL's StructuredBuffer struct layout field-for-field) is the
// single most likely spot for a silent data-corruption bug if wrong; verify
// with the plan's correctness harness before trusting any visual output.
public class TerrainGpuSampler : IDisposable
{
    public const int MaxBiomes = 8;

    readonly ComputeShader _shader;
    int _kernel = -1;
    uint _threadGroupSizeX = 64;

    ComputeBuffer _leafParamsBuffer;
    ComputeBuffer _fieldTypeBuffer;
    ComputeBuffer _biasBuffer;
    ComputeBuffer _blendBuffer;
    ComputeBuffer _planetBuffer;

    // ---- grove atlas ----------------------------------------------------
    //
    // LSystemGroveField is the one leaf whose shape is not closed-form: an
    // L-system cannot be derived in HLSL, so its spires arrive as a capsule
    // atlas the CPU builds per plot and caches. These buffers carry whatever
    // plots the region currently being dispatched can reach.
    // See Shaders/Compute/DensityGrove.hlsl.
    LSystemGroveField _grove;
    ComputeBuffer _groveCapsuleBuffer;
    ComputeBuffer _grovePlotBuffer;
    ComputeBuffer _groveCellStartBuffer;
    ComputeBuffer _groveCellItemBuffer;
    ComputeBuffer _groveCellYBuffer;
    ComputeBuffer _groveAtlasBuffer;

    readonly List<GroveCapsuleGpu> _groveCapsules = new List<GroveCapsuleGpu>();
    readonly List<GrovePlotGpu> _grovePlots = new List<GrovePlotGpu>();
    readonly List<int> _groveCellStart = new List<int>();
    readonly List<int> _groveCellItems = new List<int>();
    readonly List<Vector2> _groveCellY = new List<Vector2>();

    // The plot rectangle currently uploaded. Kept separate from what is
    // written into _groveAtlasBuffer, because a region that produced no
    // capsules uploads plotsX = 0 (so the shader skips straight to ground)
    // while still counting as "already covered" for caching.
    int _atlasPxMin, _atlasPzMin, _atlasPlotsX, _atlasPlotsZ;
    int _atlasVersion = -1;
    bool _atlasValid;

    float[] _syncScratch = Array.Empty<float>(); // see SubmitBatchSync

    public bool IsWorldGpuCapable { get; private set; }
    public static bool SupportsCompute => SystemInfo.supportsComputeShaders;

    public TerrainGpuSampler(ComputeShader shader)
    {
        _shader = shader;
        if (_shader == null || !SupportsCompute) return;

        // A ComputeShader that failed to compile (most likely: the FXC
        // "Compiler timed out" this kernel is genuinely capable of hitting)
        // still loads as an asset, but exposes NO kernels -- FindKernel logs
        // an error and returns -1, and GetKernelThreadGroupSizes then THROWS
        // IndexOutOfRangeException. Letting that escape this constructor took
        // the entire generation pass down with it: EnsureField ->
        // RecomputeTargets -> GenerateAllChunks all unwound, so not one chunk
        // was built -- not even the ones that never needed the GPU. The whole
        // point of the CPU fallback is that a broken/absent compute shader is
        // survivable, so swallow it here and leave _kernel at -1.
        try
        {
            if (!_shader.HasKernel("CSMain"))
            {
                Debug.LogWarning($"[TerrainGpuSampler] '{_shader.name}' has no CSMain kernel " +
                                 "(did it fail to compile?). Falling back to CPU density sampling.");
                return;
            }
            int k = _shader.FindKernel("CSMain");
            if (k < 0) return;
            _shader.GetKernelThreadGroupSizes(k, out _threadGroupSizeX, out _, out _);
            _kernel = k;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TerrainGpuSampler] Could not bind '{_shader.name}' CSMain " +
                             $"({e.GetType().Name}: {e.Message}). Falling back to CPU density sampling.");
            _kernel = -1;
            return;
        }

        _leafParamsBuffer = new ComputeBuffer(MaxBiomes, Marshal.SizeOf<LeafGpuParams>());
        _fieldTypeBuffer = new ComputeBuffer(MaxBiomes, sizeof(int));
        _biasBuffer = new ComputeBuffer(MaxBiomes, sizeof(float));
        _blendBuffer = new ComputeBuffer(1, Marshal.SizeOf<BiomeBlendGpuParams>());
        _planetBuffer = new ComputeBuffer(1, Marshal.SizeOf<PlanetGpuParams>());

        // One-element placeholders so every kernel including DensityGrove.hlsl
        // has valid bindings even in a world with no grove in it. An unbound
        // StructuredBuffer is not merely empty on some backends -- it is
        // undefined behaviour. plotsX stays 0, so the shader reads one atlas
        // entry and falls through to ground.
        _groveCapsuleBuffer = new ComputeBuffer(1, Marshal.SizeOf<GroveCapsuleGpu>());
        _grovePlotBuffer = new ComputeBuffer(1, Marshal.SizeOf<GrovePlotGpu>());
        _groveCellStartBuffer = new ComputeBuffer(1, sizeof(int));
        _groveCellItemBuffer = new ComputeBuffer(1, sizeof(int));
        _groveCellYBuffer = new ComputeBuffer(1, sizeof(float) * 2);
        _groveAtlasBuffer = new ComputeBuffer(1, Marshal.SizeOf<GroveAtlasGpuParams>());
        _groveAtlasBuffer.SetData(new[] { default(GroveAtlasGpuParams) });
    }

    // Rebuilds the persistent per-world parameter buffers. Call whenever
    // TerrainTuning fires (same dirty-flag pattern MCChunkManager.
    // BuildBiomeMaterialProps already uses) or when the active field changes.
    // Leaves IsWorldGpuCapable false (falls back to CPU) if baseField isn't
    // a recognized shape (PlanetField-wrapping-BiomeDensityField, or a bare
    // BiomeDensityField) or any biome resolves to a non-GPU leaf type.
    public void RefreshWorldParams(DensityField baseField)
    {
        IsWorldGpuCapable = false;
        // _kernel < 0 => the shader never bound (see the constructor); every
        // buffer below is null too, so this must bail before touching them.
        if (_shader == null || !SupportsCompute || _kernel < 0) return;

        // PlanetGpuParams.center comes ONLY from TryBuildGpuParams below, so
        // the CPU and GPU can never disagree about it. This used to re-read
        // `planet.center` directly here and discard TryBuildGpuParams' own
        // output via `out _` -- harmless while the two agreed, a silent
        // divergence the moment they stopped.
        PlanetGpuParams planetParams = default; // isPlanet=0/center=zero/radius=0 for the non-planet case, matching prior defaults
        BiomeBlendGpuParams blend;
        LeafGpuParams[] leaves;
        GpuFieldType[] fieldTypes;
        float[] biases;
        bool ok;

        if (baseField is PlanetField planet)
        {
            ok = planet.TryBuildGpuParams(out planetParams, out blend, out leaves, out fieldTypes, out biases);
        }
        else if (baseField is BiomeDensityField biomeWorld)
        {
            ok = biomeWorld.TryBuildGpuLeaves(out blend, out leaves, out fieldTypes, out biases);
        }
        else
        {
            return; // unknown/unsupported shape -- stay CPU-only for this world
        }
        if (!ok) return;

        var fieldTypeInts = new int[MaxBiomes];
        for (int i = 0; i < MaxBiomes; i++) fieldTypeInts[i] = (int)fieldTypes[i];

        _leafParamsBuffer.SetData(leaves);
        _fieldTypeBuffer.SetData(fieldTypeInts);
        _biasBuffer.SetData(biases);
        _blendBuffer.SetData(new[] { blend });
        _planetBuffer.SetData(new[] { planetParams });

        _shader.SetBuffer(_kernel, "_LeafParams", _leafParamsBuffer);
        _shader.SetBuffer(_kernel, "_BiomeFieldType", _fieldTypeBuffer);
        _shader.SetBuffer(_kernel, "_BiomeBias", _biasBuffer);
        _shader.SetBuffer(_kernel, "_BiomeBlendBuf", _blendBuffer);
        _shader.SetBuffer(_kernel, "_PlanetBuf", _planetBuffer);
        BindGroveBuffers(_shader, _kernel);

        // The world's leaves may have been swapped out from under an atlas
        // built for the previous set.
        _grove = FindGroveField(baseField);
        _atlasValid = false;

        IsWorldGpuCapable = true;
    }

    // Only one grove per world is supported, which matches what a BiomeWorld
    // can meaningfully hold: two grove biomes would need two atlases and a
    // per-slot index, for no content reason anyone has asked for yet.
    static LSystemGroveField FindGroveField(DensityField baseField)
    {
        var world = baseField as BiomeDensityField;
        if (world == null && baseField is PlanetField planet) world = planet.surface as BiomeDensityField;
        if (world == null) return null;
        for (int i = 0; i < world.BiomeCount; i++)
            if (world.FieldAt(i) is LSystemGroveField g) return g;
        return null;
    }

    void BindGroveBuffers(ComputeShader shader, int kernel)
    {
        shader.SetBuffer(kernel, "_GroveCapsules", _groveCapsuleBuffer);
        shader.SetBuffer(kernel, "_GrovePlots", _grovePlotBuffer);
        shader.SetBuffer(kernel, "_GroveCellStart", _groveCellStartBuffer);
        shader.SetBuffer(kernel, "_GroveCellItems", _groveCellItemBuffer);
        shader.SetBuffer(kernel, "_GroveCellY", _groveCellYBuffer);
        shader.SetBuffer(kernel, "_GroveAtlasBuf", _groveAtlasBuffer);
    }

    // Binds the persistent world/biome parameter buffers onto ANOTHER
    // compute shader's kernel. TerrainGpuMesher includes the same
    // DensityPlanet.hlsl and so declares the same StructuredBuffers, but it
    // is a separate ComputeShader asset with its own binding table -- these
    // have to be bound there too, and from here, so there is still exactly
    // one place that decides what the world's GPU parameters are.
    public bool BindWorldBuffers(ComputeShader shader, int kernel)
    {
        if (!IsWorldGpuCapable || shader == null || kernel < 0) return false;
        shader.SetBuffer(kernel, "_LeafParams", _leafParamsBuffer);
        shader.SetBuffer(kernel, "_BiomeFieldType", _fieldTypeBuffer);
        shader.SetBuffer(kernel, "_BiomeBias", _biasBuffer);
        shader.SetBuffer(kernel, "_BiomeBlendBuf", _blendBuffer);
        shader.SetBuffer(kernel, "_PlanetBuf", _planetBuffer);
        BindGroveBuffers(shader, kernel);
        return true;
    }

    // Fills `outBuffer` with the requested grids and LEAVES IT ON THE GPU --
    // no readback, no dest arrays (Request.dest is ignored here). This is the
    // entry point for GPU meshing, where the density is only ever consumed by
    // another kernel and reading it back would be the one pointless copy in
    // the whole pipeline.
    public bool DispatchGrids(IList<Request> requests, ComputeBuffer outBuffer, out uint totalVoxels)
    {
        totalVoxels = 0;
        if (!IsWorldGpuCapable || requests == null || requests.Count == 0 || outBuffer == null) return false;
        EnsureGroveAtlasForRequests(requests);

        BuildDescriptors(requests, out GpuGridRequest[] descriptors, out totalVoxels);
        if (outBuffer.count < (int)totalVoxels) return false;

        ComputeBuffer reqBuffer = null;
        try
        {
            reqBuffer = new ComputeBuffer(descriptors.Length, Marshal.SizeOf<GpuGridRequest>());
            Dispatch(reqBuffer, outBuffer, descriptors, totalVoxels);
            return true;
        }
        catch (Exception e)
        {
            // Same reasoning as the other two dispatch paths: a GPU failure
            // must degrade to the CPU mesher, never abort the caller's loop.
            Debug.LogException(e);
            return false;
        }
        finally
        {
            reqBuffer?.Dispose();
        }
    }

    // One pending grid-fill request. `dest` must already be sized to
    // countX*countY*countZ (matches DensityField.SampleGrid's contract) --
    // results are copied straight in, using the identical (z*countY+y)*
    // countX+x flattening ChunkMesher already expects.
    // Makes the resident capsule atlas cover the world-space XZ span
    // [worldMin, worldMax], rebuilding and re-uploading only when that span
    // moves to different plots (or the field's tunables changed).
    //
    // PUBLIC because the MESHER needs it too, and dispatches a DIFFERENT chunk
    // set than the density pass. TerrainGpuMesher's vertex and transition
    // kernels evaluate density themselves -- the AO probe, the biome weights,
    // and the transition cells all call EvaluateBiomeBlend -- so an atlas
    // built only for the last density batch would leave them sampling plots
    // that are not resident. That reads as ground where a spire should be, and
    // therefore as a crack against the neighbour that did see the spire.
    public void EnsureGroveAtlas(Vector3 worldMin, Vector3 worldMax)
    {
        if (_grove == null || _kernel < 0) return;

        float size = Mathf.Max(8f, _grove.plotSize);
        // One plot of slack each way, because the shader reads the 3x3 plot
        // neighbourhood around each sample exactly as Sample does.
        int pxMin = Mathf.FloorToInt(worldMin.x / size) - 1;
        int pzMin = Mathf.FloorToInt(worldMin.z / size) - 1;
        int plotsX = Mathf.FloorToInt(worldMax.x / size) + 2 - pxMin;
        int plotsZ = Mathf.FloorToInt(worldMax.z / size) + 2 - pzMin;
        if (plotsX <= 0 || plotsZ <= 0) return;

        if (_atlasValid && _atlasVersion == _grove.Version &&
            _atlasPxMin == pxMin && _atlasPzMin == pzMin &&
            _atlasPlotsX == plotsX && _atlasPlotsZ == plotsZ)
            return; // same ground as last dispatch -- nothing to re-upload

        _grove.BuildGpuAtlas(pxMin, pzMin, plotsX, plotsZ,
                             _groveCapsules, _grovePlots, _groveCellStart, _groveCellItems, _groveCellY);

        Grow(ref _groveCapsuleBuffer, _groveCapsules.Count, Marshal.SizeOf<GroveCapsuleGpu>());
        Grow(ref _grovePlotBuffer, _grovePlots.Count, Marshal.SizeOf<GrovePlotGpu>());
        Grow(ref _groveCellStartBuffer, _groveCellStart.Count, sizeof(int));
        Grow(ref _groveCellItemBuffer, _groveCellItems.Count, sizeof(int));
        Grow(ref _groveCellYBuffer, _groveCellY.Count, sizeof(float) * 2);

        if (_groveCapsules.Count > 0) _groveCapsuleBuffer.SetData(_groveCapsules, 0, 0, _groveCapsules.Count);
        if (_grovePlots.Count > 0) _grovePlotBuffer.SetData(_grovePlots, 0, 0, _grovePlots.Count);
        if (_groveCellStart.Count > 0) _groveCellStartBuffer.SetData(_groveCellStart, 0, 0, _groveCellStart.Count);
        if (_groveCellItems.Count > 0) _groveCellItemBuffer.SetData(_groveCellItems, 0, 0, _groveCellItems.Count);
        if (_groveCellY.Count > 0) _groveCellYBuffer.SetData(_groveCellY, 0, 0, _groveCellY.Count);

        // An empty region uploads plotsX = 0, so the shader reads one atlas
        // entry and falls straight through to ground.
        _groveAtlasBuffer.SetData(new[] { new GroveAtlasGpuParams
        {
            pxMin = pxMin,
            pzMin = pzMin,
            plotsX = _groveCapsules.Count > 0 ? plotsX : 0,
            plotsZ = _groveCapsules.Count > 0 ? plotsZ : 0,
        }});

        BindGroveBuffers(_shader, _kernel); // Grow may have replaced any of them

        _atlasPxMin = pxMin;
        _atlasPzMin = pzMin;
        _atlasPlotsX = plotsX;
        _atlasPlotsZ = plotsZ;
        _atlasVersion = _grove.Version;
        _atlasValid = true;
    }

    // Overflow policy is GROW, never clamp. Dropping capsules past a cap would
    // desync the GPU from LSystemGroveField.Sample, which still sees them --
    // and a density that disagrees between the two paths is exactly how a seam
    // is made. One reallocation the first time a dense region appears, then
    // the high-water mark holds and this is a no-op.
    static void Grow(ref ComputeBuffer buf, int count, int stride)
    {
        int need = Mathf.Max(1, count);
        if (buf != null && buf.count >= need) return;
        buf?.Dispose();
        buf = new ComputeBuffer(Mathf.NextPowerOfTwo(need), stride);
    }

    // XZ span a batch of grid requests can sample.
    void EnsureGroveAtlasForRequests(IList<Request> requests)
    {
        if (_grove == null) return;

        float minX = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i < requests.Count; i++)
        {
            Request r = requests[i];
            float x1 = r.origin.x + Mathf.Max(0, r.countX - 1) * r.step;
            float z1 = r.origin.z + Mathf.Max(0, r.countZ - 1) * r.step;
            if (r.origin.x < minX) minX = r.origin.x;
            if (r.origin.z < minZ) minZ = r.origin.z;
            if (x1 > maxX) maxX = x1;
            if (z1 > maxZ) maxZ = z1;
        }
        if (minX > maxX) return;

        EnsureGroveAtlas(new Vector3(minX, 0f, minZ), new Vector3(maxX, 0f, maxZ));
    }

    public struct Request
    {
        public Vector3 origin;
        public float step;
        public int countX, countY, countZ;
        public float[] dest;
    }

    // Synchronous (blocking) batch fill -- for BuildBatchSync/ProcessAllJobsNow
    // paths that already tolerate a stall, in and out of Play mode. Returns
    // false without touching `dest` arrays if the world isn't GPU-capable
    // (caller falls back to the existing CPU SampleGrid path).
    public bool SubmitBatchSync(IList<Request> requests)
    {
        if (!IsWorldGpuCapable || requests == null || requests.Count == 0) return false;
        EnsureGroveAtlasForRequests(requests);

        BuildDescriptors(requests, out GpuGridRequest[] descriptors, out uint totalVoxels);

        try
        {
            using (var reqBuffer = new ComputeBuffer(descriptors.Length, Marshal.SizeOf<GpuGridRequest>()))
            using (var outBuffer = new ComputeBuffer((int)totalVoxels, sizeof(float)))
            {
                Dispatch(reqBuffer, outBuffer, descriptors, totalVoxels);

                // Grown and kept rather than allocated per call: a bulk
                // regen runs this repeatedly, and at 32 chunks a batch this
                // array is several megabytes each time.
                if (_syncScratch.Length < (int)totalVoxels) _syncScratch = new float[(int)totalVoxels];
                outBuffer.GetData(_syncScratch, 0, 0, (int)totalVoxels); // blocking

                CopySlices(requests, descriptors, _syncScratch);
            }
            return true;
        }
        catch (Exception e)
        {
            // A GPU-side failure here must not take the whole generation
            // pass down with it (this is exactly what happened before this
            // try/catch existed: an unhandled Dispatch exception aborted
            // ProcessAllJobsNow partway through, and NO chunk got applied at
            // all, not just the ones that would've used GPU data). Log once
            // and let the caller fall back to its own CPU SampleGrid path --
            // `dest` arrays are left untouched, which ChunkMesher already
            // treats as "no GPU data available" via the null-check at each
            // of its 3 call sites.
            Debug.LogException(e);
            return false;
        }
    }

    // Async batch fill -- for the live streaming pipeline. `onComplete`
    // fires on the main thread once the GPU readback lands, some frames
    // later; `dest` arrays are filled before it fires. Passing false means
    // either the world isn't GPU-capable or the readback itself errored --
    // caller falls back to CPU in either case.
    public void SubmitBatchAsync(IList<Request> requests, Action<bool> onComplete)
    {
        if (!IsWorldGpuCapable || requests == null || requests.Count == 0)
        {
            // no atlas needed: nothing will be dispatched
            onComplete?.Invoke(false);
            return;
        }

        EnsureGroveAtlasForRequests(requests);
        BuildDescriptors(requests, out GpuGridRequest[] descriptors, out uint totalVoxels);

        ComputeBuffer reqBuffer = null, outBuffer = null;
        try
        {
            reqBuffer = new ComputeBuffer(descriptors.Length, Marshal.SizeOf<GpuGridRequest>());
            outBuffer = new ComputeBuffer((int)totalVoxels, sizeof(float));
            Dispatch(reqBuffer, outBuffer, descriptors, totalVoxels);
        }
        catch (Exception e)
        {
            // Same reasoning as SubmitBatchSync's catch: never let a GPU
            // failure propagate into the caller's dispatch loop uncaught.
            Debug.LogException(e);
            reqBuffer?.Dispose();
            outBuffer?.Dispose();
            onComplete?.Invoke(false);
            return;
        }

        AsyncGPUReadback.Request(outBuffer, request =>
        {
            bool ok = !request.hasError;
            if (ok)
            {
                NativeArray<float> data = request.GetData<float>();
                CopySlices(requests, descriptors, data);
            }
            reqBuffer.Dispose();
            outBuffer.Dispose();
            onComplete?.Invoke(ok);
        });
    }

    // Unity/D3D caps thread group COUNT at 65535 per dispatch axis. A big
    // batch (e.g. a full "Generate All Chunks" regen touching hundreds of
    // chunks) can easily need far more than 65535 groups at 64 threads/
    // group, so this spreads the dispatch across X and Y instead of putting
    // everything on X alone -- up to 65535*65535 groups, which is far more
    // headroom than any realistic batch here needs. The kernel reconstructs
    // one flat group index from (groupID.x, groupID.y, _GroupsX).
    const int kMaxGroupsPerAxis = 65535;

    void Dispatch(ComputeBuffer reqBuffer, ComputeBuffer outBuffer, GpuGridRequest[] descriptors, uint totalVoxels)
    {
        reqBuffer.SetData(descriptors);
        _shader.SetBuffer(_kernel, "_Requests", reqBuffer);
        _shader.SetBuffer(_kernel, "_Output", outBuffer);
        _shader.SetInt("_RequestCount", descriptors.Length);
        _shader.SetInt("_TotalVoxels", (int)totalVoxels);

        int totalGroups = Mathf.Max(1, Mathf.CeilToInt(totalVoxels / (float)_threadGroupSizeX));
        int groupsX = Mathf.Min(totalGroups, kMaxGroupsPerAxis);
        int groupsY = Mathf.Min(Mathf.CeilToInt(totalGroups / (float)groupsX), kMaxGroupsPerAxis);

        _shader.SetInt("_GroupsX", groupsX);
        _shader.Dispatch(_kernel, groupsX, groupsY, 1);
    }

    // Scatter one flat readback buffer back into the per-request dest arrays.
    //
    // The length copied is the REQUEST's voxel count, never dest.Length:
    // ChunkMeshJob now owns and reuses its raw-density buffers, so a dest can
    // legitimately be longer than the request that is filling it. Using
    // dest.Length would read past this request's slice into the next one's.
    //
    // BULK copies, deliberately -- these used to be scalar `for (v...)
    // dest[v] = src[baseOffset + v]` loops, and they are NOT cheap at this
    // scale: one regular chunk grid alone is 33^3 ~= 36k floats, so a batch of
    // a few dozen chunks (each contributing a regular grid, up to 6 transition
    // faces and a collision grid) runs to millions of element reads. Worse,
    // the async one runs inside the AsyncGPUReadback callback, which fires on
    // the MAIN thread and is NOT covered by MCChunkManager.generationBudgetMs
    // -- so that cost landed as an unbudgeted frame hitch, invisible to the
    // budget that exists precisely to prevent hitches. Both overloads below
    // bottom out in a memcpy instead.
    static void CopySlices(IList<Request> requests, GpuGridRequest[] descriptors, float[] src)
    {
        for (int i = 0; i < requests.Count; i++)
            Array.Copy(src, (int)descriptors[i].outputOffset, requests[i].dest, 0, (int)descriptors[i].VoxelCount);
    }

    // NativeArray.Copy is the managed-array overload of the same memcpy; the
    // per-element NativeArray indexer it replaces additionally pays a
    // bounds/atomic-safety check per read outside Burst, which is what made
    // this the more expensive of the two paths.
    static void CopySlices(IList<Request> requests, GpuGridRequest[] descriptors, NativeArray<float> src)
    {
        for (int i = 0; i < requests.Count; i++)
            NativeArray<float>.Copy(src, (int)descriptors[i].outputOffset, requests[i].dest, 0, (int)descriptors[i].VoxelCount);
    }

    static void BuildDescriptors(IList<Request> requests, out GpuGridRequest[] descriptors, out uint totalVoxels)
    {
        descriptors = new GpuGridRequest[requests.Count];
        uint offset = 0;
        for (int i = 0; i < requests.Count; i++)
        {
            var r = requests[i];
            var d = new GpuGridRequest
            {
                origin = r.origin,
                step = r.step,
                countX = (uint)r.countX,
                countY = (uint)r.countY,
                countZ = (uint)r.countZ,
                outputOffset = offset,
                voxelOffset = offset,
            };
            descriptors[i] = d;
            offset += d.VoxelCount;
        }
        totalVoxels = offset;
    }

    public void Dispose()
    {
        _leafParamsBuffer?.Dispose();
        _fieldTypeBuffer?.Dispose();
        _biasBuffer?.Dispose();
        _blendBuffer?.Dispose();
        _planetBuffer?.Dispose();
        _groveCapsuleBuffer?.Dispose();
        _grovePlotBuffer?.Dispose();
        _groveCellStartBuffer?.Dispose();
        _groveCellItemBuffer?.Dispose();
        _groveCellYBuffer?.Dispose();
        _groveAtlasBuffer?.Dispose();
    }
}
