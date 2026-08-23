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
    readonly int _kernel = -1;
    readonly uint _threadGroupSizeX = 64;

    ComputeBuffer _leafParamsBuffer;
    ComputeBuffer _fieldTypeBuffer;
    ComputeBuffer _biasBuffer;
    ComputeBuffer _blendBuffer;
    ComputeBuffer _planetBuffer;

    public bool IsWorldGpuCapable { get; private set; }
    public static bool SupportsCompute => SystemInfo.supportsComputeShaders;

    public TerrainGpuSampler(ComputeShader shader)
    {
        _shader = shader;
        if (_shader == null || !SupportsCompute) return;

        _kernel = _shader.FindKernel("CSMain");
        _shader.GetKernelThreadGroupSizes(_kernel, out _threadGroupSizeX, out _, out _);

        _leafParamsBuffer = new ComputeBuffer(MaxBiomes, Marshal.SizeOf<LeafGpuParams>());
        _fieldTypeBuffer = new ComputeBuffer(MaxBiomes, sizeof(int));
        _biasBuffer = new ComputeBuffer(MaxBiomes, sizeof(float));
        _blendBuffer = new ComputeBuffer(1, Marshal.SizeOf<BiomeBlendGpuParams>());
        _planetBuffer = new ComputeBuffer(1, Marshal.SizeOf<PlanetGpuParams>());
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
        if (_shader == null || !SupportsCompute) return;

        // PlanetGpuParams.center comes ONLY from TryBuildGpuParams below --
        // it must be PlanetField's own CenterRebased (floating-origin
        // precision fix), not a raw re-read of planet.center here. This used
        // to read `planet.center` directly and discard TryBuildGpuParams'
        // own output via `out _`, silently bypassing the rebase.
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

        IsWorldGpuCapable = true;
    }

    // One pending grid-fill request. `dest` must already be sized to
    // countX*countY*countZ (matches DensityField.SampleGrid's contract) --
    // results are copied straight in, using the identical (z*countY+y)*
    // countX+x flattening ChunkMesher already expects.
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

        BuildDescriptors(requests, out GpuGridRequest[] descriptors, out uint totalVoxels);

        try
        {
            using (var reqBuffer = new ComputeBuffer(descriptors.Length, Marshal.SizeOf<GpuGridRequest>()))
            using (var outBuffer = new ComputeBuffer((int)totalVoxels, sizeof(float)))
            {
                Dispatch(reqBuffer, outBuffer, descriptors, totalVoxels);

                var result = new float[totalVoxels];
                outBuffer.GetData(result); // blocking

                for (int i = 0; i < requests.Count; i++)
                {
                    var dest = requests[i].dest;
                    uint baseOffset = descriptors[i].outputOffset;
                    for (int v = 0; v < dest.Length; v++)
                        dest[v] = result[baseOffset + v];
                }
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
            onComplete?.Invoke(false);
            return;
        }

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
                for (int i = 0; i < requests.Count; i++)
                {
                    var dest = requests[i].dest;
                    uint baseOffset = descriptors[i].outputOffset;
                    for (int v = 0; v < dest.Length; v++)
                        dest[v] = data[(int)baseOffset + v];
                }
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
    }
}
