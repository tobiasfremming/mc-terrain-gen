using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

// One terrain chunk: holds the Unity-side objects (mesh, collider, renderer)
// and applies mesh data built by ChunkMesher (usually on a worker thread).
// All configuration lives on MCChunkManager; the fields here are set by it.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class MarchingChunk : MonoBehaviour
{
    [Header("Grid")]
    public Vector3Int cells = new(32, 32, 32);  // cells per axis
    public float cellSize = 0.5f;               // world units per cell
    public float isoLevel = 0f;

    public int densitySampling = 1;             // 1=full resolution, higher=lower poly

    [Header("Generation")]
    public bool autoRegenerate = false;
    public bool gizmoBounds = true;

    [Header("Physics")]
    public bool generateCollider = false; // set by MCChunkManager for LOD0 chunks

    public DensityField densityField;

    // Optional separate field for collision. When set, the collider is meshed
    // from THIS field instead of sharing the render mesh — used to keep
    // visual-only terrain edits (footprints) out of physics.
    public DensityField physicsDensityField;

    Mesh _mesh;
    Mesh _colliderMesh;
    MeshCollider _collider;
    MeshRenderer _renderer;

    // Deferred PhysX cook. Assigning MeshCollider.sharedMesh cooks the
    // collision data INLINE on the main thread, and for a 32^3 marching-cubes
    // mesh that is one of the largest single main-thread costs in the whole
    // streaming pipeline -- paid for every LOD0 chunk, and (unlike meshing)
    // not covered by MCChunkManager.generationBudgetMs. Physics.BakeMesh
    // performs the identical cook, is explicitly safe to call off the main
    // thread, and writes the result into PhysX's cache keyed on the mesh; the
    // later sharedMesh assignment then finds it warm and returns almost
    // immediately.
    //
    // The cache entry reflects the mesh's CONTENTS at bake time, and
    // _mesh/_colliderMesh are reused across rebuilds. If this chunk is
    // remeshed before its bake lands, the assignment simply cooks
    // synchronously the way it always did -- slower for that one chunk, never
    // wrong.
    Task _bakeTask;
    Mesh _pendingColliderMesh;

    // Must match ChunkMeshJob.PackedVertex field-for-field. Colors stay
    // Float32x4 rather than the packed UNorm8x4 a mesh often defaults to:
    // channel .a carries baked AO and .rgb carries biome blend weights that
    // the shader feeds into a sharpened blend, so 8-bit quantisation there is
    // visible as banding, not a rounding detail.
    static readonly VertexAttributeDescriptor[] kVertexLayout =
    {
        new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
        new(VertexAttribute.Normal,   VertexAttributeFormat.Float32, 3),
        new(VertexAttribute.Color,    VertexAttributeFormat.Float32, 4),
    };

    // The collider never needs normals or colors.
    static readonly VertexAttributeDescriptor[] kColliderLayout =
    {
        new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
    };

    // DontValidateIndices: the mesher emits indices it just created and
    // bounds-checked itself; Unity re-checking every one of them against the
    // vertex count is pure duplicated work on the main thread.
    // DontRecalculateBounds: PackForUpload already computed them on a worker.
    const MeshUpdateFlags kUploadFlags = MeshUpdateFlags.DontValidateIndices |
                                         MeshUpdateFlags.DontRecalculateBounds |
                                         MeshUpdateFlags.DontResetBoneBounds;

    // Set by MCChunkManager so one chunk can't be queued twice in its pump
    // list (a pooled chunk can be rebuilt while a previous bake is still
    // outstanding). Owned by the manager; meaningless here.
    [System.NonSerialized] public bool bakeTracked;

    public bool ColliderBakePending => _bakeTask != null;

    // Rendering visibility only — colliders stay active. Used by the manager's
    // staged LOD swap: freshly generated replacement chunks stay hidden until
    // the chunk they replace can be removed in the same frame.
    public void SetVisible(bool visible)
    {
        if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
        if (_renderer != null) _renderer.enabled = visible;
    }

    void EnsureMesh()
    {
        if (_mesh == null)
        {
            _mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = "MC_Chunk" };
            // Deliberately NOT MarkDynamic. It puts the mesh in a CPU-writable
            // heap that Unity may re-upload from its own system-memory copy,
            // which for a compute-written mesh means silently wiping what
            // CSCopyOut just wrote -- uninitialized buffer memory drawn as
            // terrain. It also bought nothing here any more: the hint is for
            // repeated CPU writes into an existing buffer, and every rebuild
            // reallocates via SetVertexBufferParams regardless.
            // (_colliderMesh below keeps it -- nothing ever computes into it.)
            //
            // The Raw targets are what let a compute shader address these
            // buffers at all, and must be set before SetVertexBufferParams
            // creates them. They cost nothing for meshes that never take that
            // path. See TerrainGpuMesher.CopyInto.
            _mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            _mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }
    }

    // Main-thread half: upload the built mesh data and update the collider.
    public void ApplyBuild(ChunkMeshJob job)
    {
        EnsureMesh();

        // Two memcpys into buffers whose sizes we already know, instead of
        // SetVertices/SetNormals/SetColors/SetTriangles (four walks, three
        // format conversions, one full index validation) plus a
        // RecalculateBounds pass. The interleaving and the bounds were done on
        // the worker thread -- see ChunkMesher.PackForUpload.
        if (job.vertexCount > 0 && job.indexCount > 0)
        {
            _mesh.SetVertexBufferParams(job.vertexCount, kVertexLayout);
            _mesh.SetIndexBufferParams(job.indexCount, IndexFormat.UInt32);

            // Shape first, bytes second. SetSubMesh is not obviously inert on a
            // mesh that still carries an empty CPU-side copy, and if it ever
            // re-uploads from that copy it would land after a compute write and
            // wipe it. Declaring the shape up front removes the question
            // entirely; the CPU path below does not care about the order.
            _mesh.subMeshCount = 1;
            _mesh.SetSubMesh(0, new SubMeshDescriptor(0, job.indexCount, MeshTopology.Triangles)
            {
                bounds = job.bounds,
                firstVertex = 0,
                vertexCount = job.vertexCount,
            }, kUploadFlags);
            _mesh.bounds = job.bounds;

            bool filled;
            if (job.gpuMeshOwner != null)
            {
                // The geometry is already in video memory; hand the mesh's own
                // buffers to a compute shader rather than pulling it down and
                // pushing it straight back up.
                var vb = _mesh.GetVertexBuffer(0);
                var ib = _mesh.GetIndexBuffer();
                filled = vb != null && ib != null && job.gpuMeshOwner.CopyInto(job, vb, ib);
                vb?.Dispose();
                ib?.Dispose();
            }
            else if (job.packedVerts != null && job.packedIndices != null &&
                     job.packedVerts.Length >= job.vertexCount && job.packedIndices.Length >= job.indexCount)
            {
                _mesh.SetVertexBufferData(job.packedVerts, 0, 0, job.vertexCount, 0, kUploadFlags);
                _mesh.SetIndexBufferData(job.packedIndices, 0, 0, job.indexCount, kUploadFlags);
                filled = true;
            }
            else
            {
                // Neither source has data. Only reachable if a job reached here
                // with counts set but nothing behind them; render nothing
                // rather than whatever the freshly allocated buffers hold.
                filled = false;
            }

            if (!filled)
            {
                // Buffers sized but never written -- uninitialized GPU memory
                // drawn as triangles is the worst possible failure here, so
                // clear rather than leave it.
                _mesh.Clear();
            }
        }
        else
        {
            _mesh.Clear(); // empty chunk: no buffers to size
        }

        if (!generateCollider)
        {
            DisableCollider();
            return;
        }
        if (!job.refreshCollider) return; // visual-only edit: keep the cooked collider

        if (_collider == null)
        {
            _collider = GetComponent<MeshCollider>();
            if (_collider == null) _collider = gameObject.AddComponent<MeshCollider>();
        }

        Mesh colliderMesh;
        if (job.colliderSharesRenderMesh)
        {
            colliderMesh = job.indexCount > 0 ? _mesh : null;
        }
        else
        {
            if (_colliderMesh == null)
            {
                _colliderMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = "MC_Collider" };
                _colliderMesh.MarkDynamic();
            }
            if (job.colVertexCount > 0 && job.colIndexCount > 0)
            {
                _colliderMesh.SetVertexBufferParams(job.colVertexCount, kColliderLayout);
                _colliderMesh.SetVertexBufferData(job.packedColVerts, 0, 0, job.colVertexCount, 0, kUploadFlags);
                _colliderMesh.SetIndexBufferParams(job.colIndexCount, IndexFormat.UInt32);
                _colliderMesh.SetIndexBufferData(job.packedColIndices, 0, 0, job.colIndexCount, kUploadFlags);
                _colliderMesh.subMeshCount = 1;
                _colliderMesh.SetSubMesh(0, new SubMeshDescriptor(0, job.colIndexCount, MeshTopology.Triangles)
                {
                    bounds = job.colBounds,
                    firstVertex = 0,
                    vertexCount = job.colVertexCount,
                }, kUploadFlags);
                _colliderMesh.bounds = job.colBounds;
                colliderMesh = _colliderMesh;
            }
            else
            {
                _colliderMesh.Clear();
                colliderMesh = null;
            }
        }

        if (colliderMesh == null)
        {
            _collider.sharedMesh = null;
            _collider.enabled = false;
            _pendingColliderMesh = null;
            return;
        }

        if (!job.deferColliderBake)
        {
            _collider.sharedMesh = null; // force PhysX re-cook after mesh change
            _collider.sharedMesh = colliderMesh;
            _collider.enabled = true;
            _pendingColliderMesh = null;
            return;
        }

        // Deliberately NOT clearing sharedMesh here. Clearing is what forces
        // the re-cook, so doing it now would leave this chunk with no
        // collision at all for the frames the bake takes -- and a full
        // regenerate (TerrainTuning / Inspector change) can hit the chunk the
        // player is standing on, dropping them through the world. Leaving the
        // previous cooked data bound means the worst case is collision that
        // lags the visual mesh by a frame or two, which is what a streaming
        // world does everywhere else anyway. FinishColliderBake does the
        // null-then-assign once the cook is actually ready.
        int meshId = colliderMesh.GetInstanceID(); // Unity object access: must happen here, on the main thread
        _pendingColliderMesh = colliderMesh;

        // CHAINED, not just overwritten: a rebuild can land while an earlier
        // bake for this chunk is still running, and both read the same reused
        // Mesh. Chaining keeps them serialized (they target the same PhysX
        // cache entry anyway) and, more importantly, makes "_bakeTask has
        // completed" imply "every bake this chunk ever started has completed"
        // -- which is what lets OnDestroy free the mesh safely after a single
        // Wait().
        Task prev = _bakeTask;
        _bakeTask = (prev == null || prev.IsCompleted)
            ? Task.Run(() => Physics.BakeMesh(meshId, false))
            : prev.ContinueWith(_ => Physics.BakeMesh(meshId, false));
    }

    // Main thread, pumped by MCChunkManager. Returns true once no bake is
    // outstanding (i.e. this chunk can be dropped from the pump list).
    public bool FinishColliderBake()
    {
        if (_bakeTask == null) return true;
        if (!_bakeTask.IsCompleted) return false;

        if (_bakeTask.IsFaulted) Debug.LogException(_bakeTask.Exception);
        _bakeTask = null;

        // _pendingColliderMesh is nulled by DisableCollider/ReleaseChunk when
        // the result stopped being wanted mid-bake.
        if (_collider != null && _pendingColliderMesh != null && generateCollider)
        {
            _collider.sharedMesh = null; // force the lookup; the cook is cached now, so this pair is cheap
            _collider.sharedMesh = _pendingColliderMesh;
            _collider.enabled = true;
        }
        _pendingColliderMesh = null;
        return true;
    }

    // The bake's result is no longer wanted (chunk released to the pool /
    // collider turned off). The Task itself is left to run out on its own --
    // it only touches PhysX's cache, and OnDestroy still waits on it before
    // freeing the mesh underneath it.
    public void CancelPendingColliderBake() => _pendingColliderMesh = null;

    void DisableCollider()
    {
        _pendingColliderMesh = null; // don't let an in-flight bake re-enable it
        if (_collider == null) _collider = GetComponent<MeshCollider>();
        if (_collider != null)
        {
            _collider.sharedMesh = null;
            _collider.enabled = false;
        }
    }

    // Procedural Mesh objects (_mesh/_colliderMesh) aren't owned by the scene
    // graph and aren't freed automatically when this GameObject is destroyed
    // -- without this they leak for the life of the pooled/destroyed chunk.
    // Hit by every ReleaseChunk/ClearAllChunks/CleanupStaleChildren path.
    void OnDestroy()
    {
        // A background Physics.BakeMesh is reading the mesh we are about to
        // free. It captured only an instance ID, so it cannot be cancelled --
        // block until it is done rather than pull the mesh out from under it.
        // Rare (chunks are normally pooled, not destroyed) and short.
        if (_bakeTask != null)
        {
            try { _bakeTask.Wait(); } catch { /* faults are reported in FinishColliderBake */ }
            _bakeTask = null;
            _pendingColliderMesh = null;
        }

        if (Application.isPlaying)
        {
            if (_mesh != null) Destroy(_mesh);
            if (_colliderMesh != null) Destroy(_colliderMesh);
        }
        else
        {
            if (_mesh != null) DestroyImmediate(_mesh);
            if (_colliderMesh != null) DestroyImmediate(_colliderMesh);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!gizmoBounds) return;
        Gizmos.color = new Color(1, 0.6f, 0, 0.25f);
        var size = Vector3.Scale((Vector3)cells, new Vector3(cellSize, cellSize, cellSize));
        Gizmos.DrawWireCube(transform.position + size * 0.5f, size);
    }
}
