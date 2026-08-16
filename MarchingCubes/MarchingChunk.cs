using UnityEngine;

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
            _mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }
    }

    // Main-thread half: upload the built mesh data and update the collider.
    public void ApplyBuild(ChunkMeshJob job)
    {
        EnsureMesh();

        _mesh.Clear();
        _mesh.SetVertices(job.verts);
        _mesh.SetNormals(job.norms);
        if (job.colors.Count == job.verts.Count && job.colors.Count > 0)
            _mesh.SetColors(job.colors); // biome weights for the shader
        _mesh.SetTriangles(job.tris, 0);
        _mesh.RecalculateBounds();

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
            colliderMesh = job.tris.Count > 0 ? _mesh : null;
        }
        else
        {
            if (_colliderMesh == null)
            {
                _colliderMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = "MC_Collider" };
                _colliderMesh.MarkDynamic();
            }
            _colliderMesh.Clear();
            _colliderMesh.SetVertices(job.colVerts);
            _colliderMesh.SetTriangles(job.colTris, 0);
            _colliderMesh.RecalculateBounds();
            colliderMesh = job.colTris.Count > 0 ? _colliderMesh : null;
        }

        _collider.sharedMesh = null; // force PhysX re-cook after mesh change
        _collider.sharedMesh = colliderMesh;
        _collider.enabled = colliderMesh != null;
    }

    void DisableCollider()
    {
        if (_collider == null) _collider = GetComponent<MeshCollider>();
        if (_collider != null)
        {
            _collider.sharedMesh = null;
            _collider.enabled = false;
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
