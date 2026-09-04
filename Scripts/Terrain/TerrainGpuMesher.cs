using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// Drives Shaders/Compute/TerrainMesh.compute: density grid in, finished
// vertex/index buffers out, with no CPU marching in between.
//
// Owns only the GPU-side machinery. WHICH chunks are eligible is CanMesh's
// business and nothing else's, and the answer is conservative on purpose --
// see its comment. A chunk that fails, or that overflows the per-chunk
// capacity, is simply left for ChunkMesher; there is no partial GPU/CPU mix
// within a single chunk, for the same reason TerrainGpuSampler refuses one
// (see DensityField.GpuType's comment).
//
// Density comes from TerrainGpuSampler.DispatchGrids and STAYS on the GPU --
// the whole point is that the only thing crossing the bus is the finished
// mesh. It is the same TerrainDensity.compute the CPU path uses, reading the
// same parameter buffers, so the density values are bit-identical to what
// ChunkMesher would have marched.
public class TerrainGpuMesher : IDisposable
{
    // Per-chunk output capacity. A 32^3 chunk holding a typical surface sheet
    // lands around 3-8k vertices; caves multiply that but not by ten. Sized
    // for ~10x the common case and backed by a hard overflow check, because
    // the failure mode of guessing low is a chunk quietly meshing to the CPU
    // path, not a corrupt mesh.
    public const int VertexCapacity = 32768;
    public const int IndexCapacity = 98304;

    // Output buffers are per-slot and proportional to this, so it is a memory
    // knob: one chunk slot costs ~1.7 MB of vertex + index space, times kSlots.
    public const int MaxChunksPerBatch = 4;

    // Async batches in flight at once.
    //
    // Three rather than two because of direct upload: a slot is no longer free
    // once its readback lands, it is held until the LAST of its chunks has
    // copied itself into its Mesh -- and those applies are spread over frames
    // by MCChunkManager.generationBudgetMs. Smaller batches and one more slot
    // keeps chunks flowing while an older batch drains, at the same total
    // memory as 2 x 6.
    const int kSlots = 3;

    // Counters per chunk, mirroring MC_COUNTS_PER_CHUNK in the shader:
    // vertexCount, indexCount, overflow, boundsMin xyz, boundsMax xyz.
    const int kCountsPerChunk = 9;

    readonly ComputeShader _shader;
    readonly TerrainGpuSampler _sampler;
    int _kClear = -1, _kPatch = -1, _kVertices = -1, _kTriangles = -1, _kTransition = -1, _kCopyOut = -1;

    // OUTPUT buffers are per-slot because their contents have to survive until
    // the readback lands. The INPUT side (density, edge table, descriptors) is
    // shared: GPU commands execute in order, so a later batch's CSClear cannot
    // begin before the earlier batch's CSTriangles has finished reading them.
    class Slot
    {
        public int id;
        public ComputeBuffer verts, indices, counts;
        public bool busy;
        // Direct upload only. The batch's chunks are applied over the next few
        // frames (MCChunkManager budgets them), and this slot's output has to
        // survive until the last of them has copied it into its Mesh.
        // `generation` invalidates any reference that outlives a forced reset.
        public int outstanding;
        public uint generation;
    }

    // Persistent, world-independent lookup tables.
    ComputeBuffer _triTable, _transCellClass, _transCellInfo, _transCellIdx,
                  _transVertInfo, _transVertData;
    ComputeBuffer _desc, _density, _edgeIdx;
    Slot[] _slots;
    int _shapeCells = -1;      // cells-per-axis the buffers above are sized for

    uint _clearGroupSize = 64, _patchGroupSize = 64, _vertexGroupSize = 64,
         _triangleGroupSize = 64, _transitionGroupSize = 64, _copyGroupSize = 64;

    // Mirrors MeshChunkDesc in TerrainMesh.compute field-for-field.
    [StructLayout(LayoutKind.Sequential)]
    struct MeshChunkDesc
    {
        public Vector3 origin;
        public float step;
        public float isoLevel;
        public uint densityBase;
        public uint faceGridMask;   // bit f: face f has a half-res grid in _Density
        public uint transitionMask; // bit f: face f gets actual transition cells (a subset of the above)
        public uint finerMask;      // 27 bits of TransitionNeeds.FinerTouches
        // Six named fields rather than an array, matching the HLSL struct --
        // see its comment. Written through SetFaceBase.
        public uint faceBase0, faceBase1, faceBase2, faceBase3, faceBase4, faceBase5;
        public uint pad0; // keep in step with the HLSL struct: 64 bytes total

        public void SetFaceBase(int face, uint value)
        {
            switch (face)
            {
                case 0: faceBase0 = value; break;
                case 1: faceBase1 = value; break;
                case 2: faceBase2 = value; break;
                case 3: faceBase3 = value; break;
                case 4: faceBase4 = value; break;
                default: faceBase5 = value; break;
            }
        }
    }

    readonly List<TerrainGpuSampler.Request> _gridRequests = new();
    readonly List<ChunkMesher.PlannedRequest> _planScratch = new();
    readonly MeshChunkDesc[] _descScratch = new MeshChunkDesc[MaxChunksPerBatch];
    readonly uint[] _countsScratch = new uint[MaxChunksPerBatch * kCountsPerChunk];

    public bool IsAvailable => _kClear >= 0 && _sampler != null && _sampler.IsWorldGpuCapable;

    public TerrainGpuMesher(ComputeShader shader, TerrainGpuSampler sampler)
    {
        _shader = shader;
        _sampler = sampler;
        if (_shader == null || sampler == null || !TerrainGpuSampler.SupportsCompute) return;

        // Same defensive binding as TerrainGpuSampler's constructor: a shader
        // whose kernels failed to compile still loads as an asset, and letting
        // that throw would take the caller's whole generation pass down
        // instead of falling back to the CPU mesher.
        if (!TryBindKernel("CSClear", out _kClear, out _clearGroupSize) ||
            !TryBindKernel("CSPatch", out _kPatch, out _patchGroupSize) ||
            !TryBindKernel("CSVertices", out _kVertices, out _vertexGroupSize) ||
            !TryBindKernel("CSTriangles", out _kTriangles, out _triangleGroupSize) ||
            !TryBindKernel("CSTransition", out _kTransition, out _transitionGroupSize) ||
            !TryBindKernel("CSCopyOut", out _kCopyOut, out _copyGroupSize))
        {
            // All or nothing: IsAvailable keys off _kClear, so make sure a
            // partial bind can't look usable.
            _kClear = _kPatch = _kVertices = _kTriangles = _kTransition = _kCopyOut = -1;
            return;
        }

        BuildTables();
    }

    // Binds one kernel, naming it if it fails.
    //
    // Worth the extra method purely for the diagnostic: Unity reports a kernel
    // that FAILED TO COMPILE as "IndexOutOfRangeException: Invalid kernelIndex
    // (N) passed, must be non-negative less than M" -- with N < M, which reads
    // like a nonsense bounds error and says nothing about which kernel or why.
    // Binding one at a time turns that into the kernel's name, and the real
    // compile error is then in the console just above.
    bool TryBindKernel(string name, out int kernel, out uint groupSize)
    {
        kernel = -1;
        groupSize = 64;
        try
        {
            if (!_shader.HasKernel(name))
            {
                Debug.LogWarning($"[TerrainGpuMesher] '{_shader.name}' has no kernel '{name}'. " +
                                 "GPU meshing disabled; the CPU mesher is unaffected.");
                return false;
            }
            int k = _shader.FindKernel(name);
            if (k < 0) return false;
            _shader.GetKernelThreadGroupSizes(k, out groupSize, out _, out _);
            kernel = k;
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TerrainGpuMesher] kernel '{name}' in '{_shader.name}' did not compile " +
                             $"({e.GetType().Name}: {e.Message}). Look for the shader compile error above this " +
                             "line -- a timeout usually means that kernel inlines too much field evaluation. " +
                             "GPU meshing disabled; the CPU mesher is unaffected.");
            return false;
        }
    }

    // Uploads the Marching Cubes and Transvoxel lookup tables.
    //
    // The Transvoxel tables have variable-length rows (each case code's vertex
    // list, each cell class's triangle list), which a StructuredBuffer cannot
    // express. Each is flattened into one array plus a per-entry
    // (count | offset << 8) descriptor -- the same trick the kernel decodes.
    void BuildTables()
    {
        var tri = new int[256 * 16];
        for (int c = 0; c < 256; c++)
            for (int k = 0; k < 16; k++)
                tri[c * 16 + k] = MarchingCubesTables.triTable[c, k];
        _triTable = Upload(tri);

        var cellClass = new int[Tables.TransitionCellClass.Length];
        for (int i = 0; i < cellClass.Length; i++) cellClass[i] = Tables.TransitionCellClass[i];
        _transCellClass = Upload(cellClass);

        var cellInfo = new int[Tables.TransitionRegularCellData.Length];
        var cellIdx = new List<int>();
        for (int i = 0; i < cellInfo.Length; i++)
        {
            var cell = Tables.TransitionRegularCellData[i];
            int triCount = (int)cell.GetTriangleCount();
            cellInfo[i] = triCount | (cellIdx.Count << 8);
            byte[] idx = cell.Indizes();
            for (int k = 0; k < triCount * 3; k++) cellIdx.Add(idx[k]);
        }
        _transCellInfo = Upload(cellInfo);
        _transCellIdx = Upload(cellIdx.ToArray());

        var vertInfo = new int[Tables.TransitionVertexData.Length];
        var vertData = new List<int>();
        for (int i = 0; i < vertInfo.Length; i++)
        {
            ushort[] row = Tables.TransitionVertexData[i];
            vertInfo[i] = row.Length | (vertData.Count << 8);
            foreach (ushort v in row) vertData.Add(v);
        }
        _transVertInfo = Upload(vertInfo);
        _transVertData = Upload(vertData.ToArray());
    }

    static ComputeBuffer Upload(int[] data)
    {
        // A zero-length ComputeBuffer is invalid, and an empty table would
        // mean the shader reads nothing anyway.
        var buf = new ComputeBuffer(Mathf.Max(1, data.Length), sizeof(int));
        if (data.Length > 0) buf.SetData(data);
        return buf;
    }

    // Can this chunk be meshed entirely on the GPU?
    //
    // Deliberately narrow. Each exclusion is a thing the kernel does not do,
    // not a thing it does badly:
    //
    //  * terrain edits -- footprints and digging are applied on the CPU by
    //    ModifiedDensityField on top of the raw base density. This path never
    //    sees that, so a GPU-meshed edited chunk would silently render the
    //    unedited terrain.
    //  * densitySampling > 1 -- a reduced-resolution grid changes the lattice
    //    the kernel assumes.
    //
    // COLLIDER chunks (LOD0) ARE allowed, but they must take the readback path
    // rather than direct upload: PhysX cooks from CPU-side mesh data, and a
    // mesh whose buffers were written by a compute shader has none. They still
    // skip the marching and the per-vertex field evaluations, which is the
    // expensive half. Batches are kept homogeneous in buildCollider by the
    // caller so one batch is never half direct and half readback.
    //
    // Such a chunk can never need its OWN collision mesh, which is what makes
    // this safe: a separate physics mesh is only built when
    // physicsDiffersNearby, that implies a visual edit overlaps the chunk, and
    // that implies modsOverlapChunk -- excluded above. So the collider always
    // shares the render mesh, and ChunkMesher.Build's job of setting
    // colliderSharesRenderMesh falls to DecodeCounts instead.
    public static bool CanMesh(ChunkMeshJob job)
    {
        if (job == null || job.renderField == null) return false;
        if (job.modsOverlapChunk || job.physicsDiffersNearby) return false;
        if (job.densitySampling > 1) return false;
        return true;
    }

    public bool HasFreeSlot
    {
        get
        {
            if (_slots == null) return true; // not built yet: the first submit builds them
            for (int i = 0; i < _slots.Length; i++) if (!_slots[i].busy) return true;
            return false;
        }
    }

    // ---------------------------------------------------------------- sync --

    // Meshes a batch of already-admitted jobs, blocking on the result. Used by
    // MCChunkManager.BuildBatchSync -- the initial ground under the player and
    // the editor's Generate/Refresh All Chunks, both of which already stall and
    // both of which land hundreds of chunks at once.
    //
    // Returns false if the batch could not run at all. Either way, meshed[i]
    // is the authority per chunk: false means jobs[i] still has to go through
    // ChunkMesher (it overflowed capacity, or the batch failed outright).
    public bool MeshBatchSync(IReadOnlyList<ChunkMeshJob> jobs, bool[] meshed, bool directUpload)
    {
        Slot slot = BeginBatch(jobs);
        if (slot == null) return false;
        bool holdSlot = false;
        try
        {
            slot.counts.GetData(_countsScratch, 0, 0, jobs.Count * kCountsPerChunk); // blocking
            int refs = 0;
            for (int i = 0; i < jobs.Count; i++)
            {
                if (!DecodeCounts(jobs[i], i, !directUpload, out int vcount, out int icount)) { meshed[i] = false; continue; }
                meshed[i] = true;
                if (vcount <= 0 || icount <= 0) continue; // legitimately empty

                if (directUpload)
                {
                    // The caller applies every chunk before returning, so these
                    // references are all paid back inside this same call.
                    var job = jobs[i];
                    job.gpuMeshOwner = this;
                    job.gpuMeshSlot = slot.id;
                    job.gpuMeshChunk = i;
                    job.gpuMeshGeneration = slot.generation;
                    refs++;
                }
                else
                {
                    slot.verts.GetData(jobs[i].packedVerts, 0, i * VertexCapacity, vcount);
                    slot.indices.GetData(jobs[i].packedIndices, 0, i * IndexCapacity, icount);
                }
            }
            if (refs > 0) { slot.outstanding = refs; holdSlot = true; }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return false;
        }
        finally
        {
            // Held only when chunks took references; they release it instead.
            if (!holdSlot) ReleaseSlot(slot);
        }
    }

    // --------------------------------------------------------------- async --

    // One in-flight async batch. Lives from dispatch until the last readback
    // callback fires; its slot cannot be reused before then.
    class AsyncBatch
    {
        public Slot slot;
        public ChunkMeshJob[] jobs;
        public int count;
        public bool[] meshed;
        public int pendingReads;
        public bool directUpload;
        public Action<bool[]> onComplete;
    }

    // Dispatches a batch and reads it back over two hops: the counters first
    // (a couple of hundred bytes), then only the vertex/index ranges those
    // counters say are actually used.
    //
    // The second hop is unavoidable -- the sizes do not exist until the
    // kernels have run, and reading back the full per-chunk capacity instead
    // would be ~1.7 MB per chunk of mostly nothing. It costs roughly one extra
    // frame of latency on a pipeline that already spans several.
    //
    // `onComplete` fires on the main thread with the per-chunk verdict; jobs
    // are filled in place. Returns false if the batch never started, in which
    // case onComplete is NOT called.
    public bool SubmitBatchAsync(IReadOnlyList<ChunkMeshJob> jobs, bool directUpload, Action<bool[]> onComplete)
    {
        Slot slot = BeginBatch(jobs);
        if (slot == null) return false;

        var batch = new AsyncBatch
        {
            slot = slot,
            jobs = new ChunkMeshJob[jobs.Count],
            count = jobs.Count,
            meshed = new bool[jobs.Count],
            directUpload = directUpload,
            onComplete = onComplete,
        };
        for (int i = 0; i < jobs.Count; i++) batch.jobs[i] = jobs[i];

        try
        {
            AsyncGPUReadback.Request(slot.counts, r => OnCountsReady(batch, r));
            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            slot.busy = false;
            return false;
        }
    }

    void OnCountsReady(AsyncBatch batch, AsyncGPUReadbackRequest req)
    {
        if (req.hasError)
        {
            FinishBatch(batch); // every meshed[] entry is still false: the whole batch falls back to CPU
            return;
        }

        NativeArray<uint> data = req.GetData<uint>();
        int need = batch.count * kCountsPerChunk;
        if (data.Length < need) { FinishBatch(batch); return; }
        NativeArray<uint>.Copy(data, 0, _countsScratch, 0, need);

        // Decide every chunk's fate and COUNT the reads before issuing any.
        // Issuing as we go would let a same-frame completion drive
        // pendingReads to zero mid-loop and finish the batch while later
        // chunks were still being set up.
        var vcounts = new int[batch.count];
        var icounts = new int[batch.count];
        int reads = 0;
        for (int i = 0; i < batch.count; i++)
        {
            if (!DecodeCounts(batch.jobs[i], i, !batch.directUpload, out vcounts[i], out icounts[i])) continue;
            if (vcounts[i] > 0 && icounts[i] > 0) reads += 2;
            else batch.meshed[i] = true; // legitimately empty chunk: nothing to read back
        }

        if (batch.directUpload)
        {
            // Nothing else comes back. Each surviving chunk takes a reference
            // to this slot and copies its own geometry into its Mesh when
            // MCChunkManager gets around to applying it; the slot is freed by
            // the last release, not here.
            // vcounts/icounts are the authority here, not meshed[]: the decode
            // loop above already set meshed[i] for chunks that are legitimately
            // EMPTY (nothing to copy, no reference needed) and left it false
            // both for those and for chunks falling back to the CPU mesher.
            // Only a chunk with actual geometry takes a slot reference.
            int refs = 0;
            for (int i = 0; i < batch.count; i++)
            {
                if (vcounts[i] <= 0 || icounts[i] <= 0) continue;
                var job = batch.jobs[i];
                job.gpuMeshOwner = this;
                job.gpuMeshSlot = batch.slot.id;
                job.gpuMeshChunk = i;
                job.gpuMeshGeneration = batch.slot.generation;
                batch.meshed[i] = true;
                refs++;
            }
            batch.slot.outstanding = refs;
            batch.onComplete?.Invoke(batch.meshed);
            // An all-empty batch has nothing to release the slot, so do it now.
            if (refs == 0) ReleaseSlot(batch.slot);
            return;
        }

        batch.pendingReads = reads;
        if (reads == 0) { FinishBatch(batch); return; }

        int vstride = Marshal.SizeOf<ChunkMeshJob.PackedVertex>();
        for (int i = 0; i < batch.count; i++)
        {
            int vcount = vcounts[i], icount = icounts[i];
            if (vcount <= 0 || icount <= 0) continue;
            int idx = i;
            var job = batch.jobs[i];

            AsyncGPUReadback.Request(batch.slot.verts, vcount * vstride, idx * VertexCapacity * vstride,
                r => OnDataReady(batch, idx, job, vcount, r, true));
            AsyncGPUReadback.Request(batch.slot.indices, icount * sizeof(int), idx * IndexCapacity * sizeof(int),
                r => OnDataReady(batch, idx, job, icount, r, false));
        }
    }

    void OnDataReady(AsyncBatch batch, int idx, ChunkMeshJob job, int count, AsyncGPUReadbackRequest req, bool isVerts)
    {
        if (req.hasError)
        {
            batch.meshed[idx] = false; // only this chunk is lost; the rest of the batch is fine
        }
        else if (isVerts)
        {
            NativeArray<ChunkMeshJob.PackedVertex> src = req.GetData<ChunkMeshJob.PackedVertex>();
            NativeArray<ChunkMeshJob.PackedVertex>.Copy(src, 0, job.packedVerts, 0, Mathf.Min(count, src.Length));
            batch.meshed[idx] = true;
        }
        else
        {
            NativeArray<int> src = req.GetData<int>();
            NativeArray<int>.Copy(src, 0, job.packedIndices, 0, Mathf.Min(count, src.Length));
            batch.meshed[idx] = true;
        }

        if (--batch.pendingReads <= 0) FinishBatch(batch);
    }

    void FinishBatch(AsyncBatch batch)
    {
        ReleaseSlot(batch.slot);
        batch.onComplete?.Invoke(batch.meshed);
    }

    void ReleaseSlot(Slot slot)
    {
        slot.outstanding = 0;
        slot.generation++;
        slot.busy = false;
    }

    // ------------------------------------------------------- direct upload --

    // Copies one chunk's finished geometry from GPU scratch straight into its
    // Mesh's own buffers. `vertexBuffer`/`indexBuffer` come from
    // Mesh.GetVertexBuffer/GetIndexBuffer after MarchingChunk has sized them.
    //
    // False means the slot was recycled before this chunk got applied (a world
    // refresh mid-flight); the caller leaves the mesh empty rather than render
    // whatever the buffers now hold.
    public bool CopyInto(ChunkMeshJob job, GraphicsBuffer vertexBuffer, GraphicsBuffer indexBuffer)
    {
        if (_kCopyOut < 0 || vertexBuffer == null || indexBuffer == null) return false;
        if (job.gpuMeshSlot < 0 || _slots == null || job.gpuMeshSlot >= _slots.Length) return false;
        Slot slot = _slots[job.gpuMeshSlot];
        if (slot.generation != job.gpuMeshGeneration) return false;

        try
        {
            BindAll(_kCopyOut, slot);
            _shader.SetBuffer(_kCopyOut, "_MeshVerts", vertexBuffer);
            _shader.SetBuffer(_kCopyOut, "_MeshIndices", indexBuffer);
            _shader.SetInt("_CopyChunk", job.gpuMeshChunk);
            _shader.SetInt("_CopyVertexCount", job.vertexCount);
            _shader.SetInt("_CopyIndexCount", job.indexCount);
            // One dispatch covers both: threads below the vertex count copy
            // vertices, the rest copy indices.
            DispatchFlat(_kCopyOut, job.vertexCount + job.indexCount, _copyGroupSize);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return false;
        }
    }

    // Drops one chunk's claim on an output slot. Called exactly once per job
    // that took one, from MCChunkManager.ClearGpuRaw -- which every completion
    // path (applied, stale, requeued, failed) routes through.
    public void ReleaseChunkSlot(int slotId, uint generation)
    {
        if (_slots == null || slotId < 0 || slotId >= _slots.Length) return;
        Slot slot = _slots[slotId];
        if (slot.generation != generation) return; // already force-released
        if (--slot.outstanding <= 0) ReleaseSlot(slot);
    }

    // Force-frees every slot, invalidating outstanding references. For
    // teardown paths where the jobs holding them are being dropped wholesale
    // and their reference counts will never be paid back.
    public void ResetSlots()
    {
        if (_slots == null) return;
        foreach (var slot in _slots) ReleaseSlot(slot);
    }

    // ----------------------------------------------------- shared plumbing --

    // Everything both paths do: validate the batch shape, fill the density
    // grid, and run the three kernels. Returns the claimed slot, or null if the
    // batch cannot run -- in which case the caller falls back to ChunkMesher.
    Slot BeginBatch(IReadOnlyList<ChunkMeshJob> jobs)
    {
        if (!IsAvailable || jobs == null || jobs.Count == 0) return null;
        if (jobs.Count > MaxChunksPerBatch) return null;

        ChunkMesher.GetEffectiveGrid(jobs[0], out int nx, out int ny, out int nz, out _);
        if (nx != ny || ny != nz) return null; // the kernel assumes a cubic chunk
        int cells = nx;

        Slot slot;
        try
        {
            if (!EnsureBuffers(cells)) return null;
            slot = ClaimSlot();
            if (slot == null) return null;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return null;
        }

        _gridRequests.Clear();
        uint offset = 0; // running prefix sum; must match TerrainGpuSampler.BuildDescriptors exactly
        for (int i = 0; i < jobs.Count; i++)
        {
            var job = jobs[i];
            ChunkMesher.GetEffectiveGrid(job, out int jx, out int jy, out int jz, out float step);
            if (jx != cells || jy != cells || jz != cells) { slot.busy = false; return null; }

            var desc = new MeshChunkDesc
            {
                origin = job.origin,
                step = step,
                isoLevel = job.isoLevel,
            };

            // Which faces need what, precomputed here rather than re-derived
            // per grid point in the kernel. transitionMask is a SUBSET of
            // faceGridMask: a chunk touching a finer chunk only along an edge
            // or corner still needs that face's grid for CSPatch while
            // generating no transition cells there.
            for (int f = 0; f < 6; f++)
            {
                if (job.needs.NeedsFaceGrid(f)) desc.faceGridMask |= 1u << f;
                if (job.needs.Face(f)) desc.transitionMask |= 1u << f;
            }
            for (int sz = -1; sz <= 1; sz++)
                for (int sy = -1; sy <= 1; sy++)
                    for (int sx = -1; sx <= 1; sx++)
                        if (job.needs.FinerTouches(sx, sy, sz))
                            desc.finerMask |= 1u << ((sz + 1) * 9 + (sy + 1) * 3 + (sx + 1));

            // Straight from the planner the CPU path uses, so the halo origin,
            // the face-grid shapes and the counts can never drift from
            // ChunkMesher's own grids.
            ChunkMesher.PlanGridRequests(job, _planScratch);
            bool sawRegular = false;
            foreach (var pr in _planScratch)
            {
                // A collision grid means a collider, which CanMesh already
                // excludes; seeing one here means the two disagree.
                if (pr.kind == ChunkMesher.PlannedRequest.Kind.Collision) { slot.busy = false; return null; }

                if (pr.kind == ChunkMesher.PlannedRequest.Kind.Regular)
                {
                    desc.densityBase = offset;
                    sawRegular = true;
                }
                else
                {
                    desc.SetFaceBase(pr.face, offset);
                }

                _gridRequests.Add(new TerrainGpuSampler.Request
                {
                    origin = pr.origin,
                    step = pr.step,
                    countX = pr.countX,
                    countY = pr.countY,
                    countZ = pr.countZ,
                    dest = null, // DispatchGrids never reads this back
                });
                offset += (uint)(pr.countX * pr.countY * pr.countZ);
            }
            if (!sawRegular) { slot.busy = false; return null; }
            _descScratch[i] = desc;
        }
        if (offset > (uint)_density.count) { slot.busy = false; return null; }

        try
        {
            if (!_sampler.DispatchGrids(_gridRequests, _density, out _)) { slot.busy = false; return null; }

            _desc.SetData(_descScratch, 0, 0, jobs.Count);

            _shader.SetInt("_Cells", cells);
            _shader.SetInt("_ChunkCount", jobs.Count);
            _shader.SetInt("_VertexCapacity", VertexCapacity);
            _shader.SetInt("_IndexCapacity", IndexCapacity);

            int pointsPerChunk = (cells + 1) * (cells + 1) * (cells + 1);
            int cellsPerChunk = cells * cells * cells;

            int facePointsPerChunk = 6 * (cells + 1) * (cells + 1);
            int faceCellsPerChunk = 6 * cells * cells;

            BindAll(_kClear, slot);
            DispatchFlat(_kClear, jobs.Count * 3 * pointsPerChunk, _clearGroupSize);

            // Strictly before CSVertices: it rewrites the very grid values
            // they are about to march. See CSPatch's comment.
            BindAll(_kPatch, slot);
            DispatchFlat(_kPatch, jobs.Count * facePointsPerChunk, _patchGroupSize);

            BindAll(_kVertices, slot);
            _sampler.BindWorldBuffers(_shader, _kVertices); // the AO probe and biome weights need the world params
            DispatchFlat(_kVertices, jobs.Count * pointsPerChunk, _vertexGroupSize);

            BindAll(_kTriangles, slot);
            DispatchFlat(_kTriangles, jobs.Count * cellsPerChunk, _triangleGroupSize);

            // Last: it appends to the same per-chunk vertex and index
            // counters the regular mesh just filled, so its triangles index
            // straight into the same buffer.
            BindAll(_kTransition, slot);
            _sampler.BindWorldBuffers(_shader, _kTransition);
            DispatchFlat(_kTransition, jobs.Count * faceCellsPerChunk, _transitionGroupSize);
            return slot;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            slot.busy = false;
            return null;
        }
    }

    // Reads one chunk's counters out of _countsScratch and prepares the job to
    // receive its data. False means "leave this one to ChunkMesher".
    bool DecodeCounts(ChunkMeshJob job, int i, bool needPackedArrays, out int vcount, out int icount)
    {
        int b = i * kCountsPerChunk;
        vcount = icount = 0;
        if (_countsScratch[b + 2] != 0) return false; // overflowed capacity
        int v = (int)_countsScratch[b + 0];
        int n = (int)_countsScratch[b + 1];
        // The kernel leaves the counter past capacity on overflow rather than
        // clamping it, so these are a real check, not paranoia.
        if (v > VertexCapacity || n > IndexCapacity || v < 0 || n < 0) return false;
        if (n % 3 != 0) return false;

        job.ResetOutputs();
        // ChunkMesher.Build normally decides this; we skip Build entirely, and
        // CanMesh has already guaranteed the only possible answer (see its
        // comment).
        job.colliderSharesRenderMesh = job.buildCollider;
        job.vertexCount = v;
        job.indexCount = n;
        if (v == 0 || n == 0) return true; // empty chunk; vcount/icount stay 0

        // Skipped for direct upload: nothing ever writes into these there, and
        // at up to 32k vertices a job they are not small.
        if (needPackedArrays) ChunkMesher.EnsurePackedCapacity(job, v, n);
        vcount = v;
        icount = n;

        // Bounds were accumulated as InterlockedMin/Max over the float bit
        // patterns; see the _Counts comment in TerrainMesh.compute.
        Vector3 min = new(BitConverter.Int32BitsToSingle((int)_countsScratch[b + 3]),
                          BitConverter.Int32BitsToSingle((int)_countsScratch[b + 4]),
                          BitConverter.Int32BitsToSingle((int)_countsScratch[b + 5]));
        Vector3 max = new(BitConverter.Int32BitsToSingle((int)_countsScratch[b + 6]),
                          BitConverter.Int32BitsToSingle((int)_countsScratch[b + 7]),
                          BitConverter.Int32BitsToSingle((int)_countsScratch[b + 8]));
        job.bounds = new Bounds((min + max) * 0.5f, max - min);
        return true;
    }

    Slot ClaimSlot()
    {
        for (int i = 0; i < _slots.Length; i++)
            if (!_slots[i].busy) { _slots[i].busy = true; return _slots[i]; }
        return null;
    }

    void BindAll(int kernel, Slot slot)
    {
        _shader.SetBuffer(kernel, "_Desc", _desc);
        _shader.SetBuffer(kernel, "_Density", _density);
        _shader.SetBuffer(kernel, "_TriTable", _triTable);
        _shader.SetBuffer(kernel, "_TransCellClass", _transCellClass);
        _shader.SetBuffer(kernel, "_TransCellInfo", _transCellInfo);
        _shader.SetBuffer(kernel, "_TransCellIdx", _transCellIdx);
        _shader.SetBuffer(kernel, "_TransVertInfo", _transVertInfo);
        _shader.SetBuffer(kernel, "_TransVertData", _transVertData);
        _shader.SetBuffer(kernel, "_EdgeIdx", _edgeIdx);
        _shader.SetBuffer(kernel, "_Verts", slot.verts);
        _shader.SetBuffer(kernel, "_Indices", slot.indices);
        _shader.SetBuffer(kernel, "_Counts", slot.counts);
    }

    // Same 65535-groups-per-axis spreading TerrainDensity.compute needs; the
    // kernels rebuild one flat thread index from (groupId.x, groupId.y).
    const int kMaxGroupsPerAxis = 65535;

    void DispatchFlat(int kernel, int threads, uint groupSize)
    {
        _shader.SetInt("_TotalThreads", threads);
        int totalGroups = Mathf.Max(1, Mathf.CeilToInt(threads / (float)groupSize));
        int groupsX = Mathf.Min(totalGroups, kMaxGroupsPerAxis);
        int groupsY = Mathf.Min(Mathf.CeilToInt(totalGroups / (float)groupsX), kMaxGroupsPerAxis);
        _shader.SetInt("_GroupsX", groupsX);
        _shader.Dispatch(kernel, groupsX, groupsY, 1);
    }

    bool EnsureBuffers(int cells)
    {
        if (_shapeCells == cells && _slots != null) return true;
        // Reallocating under a pending readback would pull the buffer out from
        // under it. cellsPerChunk does not change at runtime, so this only
        // guards the pathological case.
        if (_slots != null)
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].busy) return false;

        ReleaseShapeBuffers();

        int gridPoints = ChunkMesher.RegularGridCount(cells);
        int gridPerChunk = gridPoints * gridPoints * gridPoints;
        int pointsPerChunk = (cells + 1) * (cells + 1) * (cells + 1);
        // Worst case per chunk: the halo grid plus all six half-resolution
        // face grids, each (2*cells+1)^2 samples. Only the faces a chunk
        // actually needs are dispatched, so this is a ceiling, not the norm.
        int facePerChunk = (2 * cells + 1) * (2 * cells + 1);
        int densityPerChunk = gridPerChunk + 6 * facePerChunk;

        _desc = new ComputeBuffer(MaxChunksPerBatch, Marshal.SizeOf<MeshChunkDesc>());
        _density = new ComputeBuffer(MaxChunksPerBatch * densityPerChunk, sizeof(float));
        _edgeIdx = new ComputeBuffer(MaxChunksPerBatch * 3 * pointsPerChunk, sizeof(int));

        _slots = new Slot[kSlots];
        for (int i = 0; i < kSlots; i++)
        {
            _slots[i] = new Slot
            {
                id = i,
                verts = new ComputeBuffer(MaxChunksPerBatch * VertexCapacity, Marshal.SizeOf<ChunkMeshJob.PackedVertex>()),
                indices = new ComputeBuffer(MaxChunksPerBatch * IndexCapacity, sizeof(int)),
                counts = new ComputeBuffer(MaxChunksPerBatch * kCountsPerChunk, sizeof(uint)),
            };
        }

        _shapeCells = cells;
        return true;
    }

    void ReleaseShapeBuffers()
    {
        _desc?.Dispose(); _desc = null;
        _density?.Dispose(); _density = null;
        _edgeIdx?.Dispose(); _edgeIdx = null;
        if (_slots != null)
        {
            foreach (var s in _slots)
            {
                s.verts?.Dispose();
                s.indices?.Dispose();
                s.counts?.Dispose();
            }
            _slots = null;
        }
        _shapeCells = -1;
    }

    public void Dispose()
    {
        // A pending readback holds a reference to buffers we are about to
        // free, and its callback would then run against freed memory.
        AsyncGPUReadback.WaitAllRequests();
        ReleaseShapeBuffers();
        _triTable?.Dispose(); _triTable = null;
        _transCellClass?.Dispose(); _transCellClass = null;
        _transCellInfo?.Dispose(); _transCellInfo = null;
        _transCellIdx?.Dispose(); _transCellIdx = null;
        _transVertInfo?.Dispose(); _transVertInfo = null;
        _transVertData?.Dispose(); _transVertData = null;
    }
}
