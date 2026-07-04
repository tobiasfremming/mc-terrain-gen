using System;
using System.Collections.Generic;
using UnityEngine;
using static MarchingCubesTables;
using TransitionNeeds = MCChunkManager.TransitionNeeds;


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

    // Transvoxel: width of a transition cell as a fraction of this chunk's cell
    // size. The paper (Eq. 4.2, w(k) = 2^(k-2)) uses a quarter cell.
    const float kTransitionWidthFraction = 0.25f;

    Mesh _mesh;
    Mesh _colliderMesh;
    MeshCollider _collider;
    MeshRenderer _renderer;
    bool _isGenerating = false; // prevent double generation

    // Rendering visibility only — colliders stay active. Used by the manager's
    // staged LOD swap: freshly generated replacement chunks stay hidden until
    // the chunk they replace can be removed in the same frame.
    public void SetVisible(bool visible)
    {
        if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
        if (_renderer != null) _renderer.enabled = visible;
    }

    // Scratch buffers shared by all chunks (generation is main-thread only).
    // Reusing them avoids per-chunk allocations and GC spikes.
    static float[] s_samples;
    static float[] s_faceSamples;
    static readonly List<Vector3> s_verts = new();
    static readonly List<Vector3> s_norms = new();
    static readonly List<int> s_tris = new();
    static readonly Dictionary<long, int> s_vertexCache = new();
    static readonly int[] s_cellVerts = new int[12];

    void OnValidate()
    {
        cells = new Vector3Int(
            Mathf.Max(1, cells.x),
            Mathf.Max(1, cells.y),
            Mathf.Max(1, cells.z));
        cellSize = Mathf.Max(0.001f, cellSize);
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

    public void Generate(TransitionNeeds needs) => Generate(needs, refreshCollider: true);

    // refreshCollider: pass false when only visual-layer terrain edits changed —
    // the render mesh is rebuilt but the collider (and its PhysX cook) is left
    // untouched, so e.g. footprints never disturb the character controller.
    public void Generate(TransitionNeeds needs, bool refreshCollider)
    {
        if (_isGenerating) return;
        _isGenerating = true;

        EnsureMesh();

        s_verts.Clear();
        s_norms.Clear();
        s_tris.Clear();

        GenerateRegularMeshData(needs, s_verts, s_norms, s_tris);

        if (needs.Any)
        {
            GenerateTransitionMeshData(needs, s_verts, s_norms, s_tris);
        }

        _mesh.Clear();
        _mesh.SetVertices(s_verts);
        _mesh.SetNormals(s_norms);
        _mesh.SetTriangles(s_tris, 0);
        _mesh.RecalculateBounds();

        if (!generateCollider) DisableCollider();
        else if (refreshCollider) RebuildCollider(s_tris.Count > 0);

        _isGenerating = false;
    }

    void RebuildCollider(bool renderHasGeometry)
    {
        if (_collider == null)
        {
            _collider = GetComponent<MeshCollider>();
            if (_collider == null) _collider = gameObject.AddComponent<MeshCollider>();
        }

        Mesh colliderMesh;
        if (physicsDensityField == null || physicsDensityField == densityField)
        {
            // No separate physics field: collision shares the render mesh.
            colliderMesh = renderHasGeometry ? _mesh : null;
        }
        else
        {
            // Lean collision-only meshing over the physics field: regular cells
            // only, no normals, no transition sheets, no secondary offsets.
            s_verts.Clear();
            s_tris.Clear();
            GenerateCollisionMeshData(physicsDensityField, s_verts, s_tris);

            if (_colliderMesh == null)
            {
                _colliderMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = "MC_Collider" };
                _colliderMesh.MarkDynamic();
            }
            _colliderMesh.Clear();
            _colliderMesh.SetVertices(s_verts);
            _colliderMesh.SetTriangles(s_tris, 0);
            _colliderMesh.RecalculateBounds();
            colliderMesh = s_tris.Count > 0 ? _colliderMesh : null;
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

    // Marching cubes for collision only: no normals/gradients, no Transvoxel
    // deformation. Adjacent same-level chunks share boundary samples, so their
    // collision meshes stay sealed; colliders exist only on level-0 chunks.
    void GenerateCollisionMeshData(DensityField field, List<Vector3> verts, List<int> tris)
    {
        GetEffectiveGrid(out int nx, out int ny, out int nz, out float step);
        Vector3 origin = transform.position;

        int countX = nx + 1, countY = ny + 1, countZ = nz + 1;
        int sampleCount = countX * countY * countZ;
        if (s_samples == null || s_samples.Length < sampleCount) s_samples = new float[sampleCount];

        field.SampleGrid(origin, countX, countY, countZ, step, s_samples);
        if (isoLevel != 0f)
            for (int i = 0; i < sampleCount; i++) s_samples[i] -= isoLevel;

        int Idx(int x, int y, int z) => (z * countY + y) * countX + x;

        s_vertexCache.Clear();

        int GetOrCreateVertex(int edgeId, int x, int y, int z)
        {
            int aIdx = EdgeToCorners[edgeId, 0];
            int bIdx = EdgeToCorners[edgeId, 1];
            var ca = Corner[aIdx];
            var cb = Corner[bIdx];
            int ia = Idx(x + ca.x, y + ca.y, z + ca.z);
            int ib = Idx(x + cb.x, y + cb.y, z + cb.z);
            long key = ia < ib ? ((long)ia << 32) | (uint)ib : ((long)ib << 32) | (uint)ia;

            if (s_vertexCache.TryGetValue(key, out int vi)) return vi;

            float da = s_samples[ia], db = s_samples[ib];
            Vector3 pa = new Vector3(x + ca.x, y + ca.y, z + ca.z) * step;
            Vector3 pb = new Vector3(x + cb.x, y + cb.y, z + cb.z) * step;
            float t = (da != db) ? Mathf.Clamp01(da / (da - db)) : 0.5f;

            vi = verts.Count;
            verts.Add(Vector3.Lerp(pa, pb, t));
            s_vertexCache.Add(key, vi);
            return vi;
        }

        for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    int caseIdx = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        var co = Corner[i];
                        if (s_samples[Idx(x + co.x, y + co.y, z + co.z)] < 0f) caseIdx |= (1 << i);
                    }
                    if (caseIdx == 0 || caseIdx == 255) continue;

                    for (int t = 0; triTable[caseIdx, t] != -1; t += 3)
                    {
                        int i0 = GetOrCreateVertex(triTable[caseIdx, t + 0], x, y, z);
                        int i1 = GetOrCreateVertex(triTable[caseIdx, t + 1], x, y, z);
                        int i2 = GetOrCreateVertex(triTable[caseIdx, t + 2], x, y, z);
                        if (i0 == i1 || i1 == i2 || i0 == i2) continue;
                        tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    }
                }
    }

    // Effective grid after densitySampling, plus the step that keeps the chunk's
    // world extent constant.
    void GetEffectiveGrid(out int nx, out int ny, out int nz, out float step)
    {
        nx = Mathf.Max(1, cells.x / Mathf.Max(1, densitySampling));
        ny = Mathf.Max(1, cells.y / Mathf.Max(1, densitySampling));
        nz = Mathf.Max(1, cells.z / Mathf.Max(1, densitySampling));
        step = (cells.x * cellSize) / nx; // assumes cubic cells
    }

    // ========================================================================
    // Regular cells (modified Marching Cubes) with Transvoxel secondary vertex
    // positions: on faces that border a finer neighbor, vertices in the
    // outermost cell layer are shifted inward to make room for the transition
    // cells (Lengyel Eq. 4.2 + tangent-plane projection Eq. 4.3).
    //
    // Vertices are deduplicated with an edge-keyed cache: each surface vertex
    // lies on a unique grid edge shared by up to four cells, so caching cuts
    // both vertex count and (expensive) gradient evaluations ~4x.
    // ========================================================================
    public void GenerateRegularMeshData(TransitionNeeds needs, List<Vector3> verts, List<Vector3> norms, List<int> tris)
    {
        GetEffectiveGrid(out int nx, out int ny, out int nz, out float step);

        Vector3 chunkSize = new Vector3(nx, ny, nz) * step;
        Vector3 origin = transform.position;

        int countX = nx + 1, countY = ny + 1, countZ = nz + 1;
        int sampleCount = countX * countY * countZ;
        if (s_samples == null || s_samples.Length < sampleCount) s_samples = new float[sampleCount];

        densityField.SampleGrid(origin, countX, countY, countZ, step, s_samples);
        if (isoLevel != 0f)
            for (int i = 0; i < sampleCount; i++) s_samples[i] -= isoLevel;

        float gradStep = densityField.GradientStep(step);

        int Idx(int x, int y, int z) => (z * countY + y) * countX + x;

        s_vertexCache.Clear();

        // Creates (or reuses) the vertex on the edge of cell (x,y,z) with the
        // given Marching Cubes edge id. Cache key: the two sample-grid indices.
        int GetOrCreateVertex(int edgeId, int x, int y, int z)
        {
            int aIdx = EdgeToCorners[edgeId, 0];
            int bIdx = EdgeToCorners[edgeId, 1];
            var ca = Corner[aIdx];
            var cb = Corner[bIdx];
            int ia = Idx(x + ca.x, y + ca.y, z + ca.z);
            int ib = Idx(x + cb.x, y + cb.y, z + cb.z);
            long key = ia < ib ? ((long)ia << 32) | (uint)ib : ((long)ib << 32) | (uint)ia;

            if (s_vertexCache.TryGetValue(key, out int vi)) return vi;

            float da = s_samples[ia], db = s_samples[ib];
            Vector3 pa = new Vector3(x + ca.x, y + ca.y, z + ca.z) * step;
            Vector3 pb = new Vector3(x + cb.x, y + cb.y, z + cb.z) * step;
            float t = (da != db) ? Mathf.Clamp01(da / (da - db)) : 0.5f;
            Vector3 pL = Vector3.Lerp(pa, pb, t);

            Vector3 n = -densityField.Gradient(origin + pL, gradStep).normalized;
            pL = ApplySecondaryOffset(pL, n, needs, step, chunkSize);

            vi = verts.Count;
            verts.Add(pL);
            norms.Add(n);
            s_vertexCache.Add(key, vi);
            return vi;
        }

        for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    int caseIdx = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        var co = Corner[i];
                        if (s_samples[Idx(x + co.x, y + co.y, z + co.z)] < 0f) caseIdx |= (1 << i);
                    }
                    if (caseIdx == 0 || caseIdx == 255) continue;

                    for (int t = 0; triTable[caseIdx, t] != -1; t += 3)
                    {
                        int i0 = GetOrCreateVertex(triTable[caseIdx, t + 0], x, y, z);
                        int i1 = GetOrCreateVertex(triTable[caseIdx, t + 1], x, y, z);
                        int i2 = GetOrCreateVertex(triTable[caseIdx, t + 2], x, y, z);
                        if (i0 == i1 || i1 == i2 || i0 == i2) continue; // zero-area
                        tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    }
                }
    }

    // Lengyel Eq. 4.2/4.3. pLocal is the primary (undeformed) vertex position in
    // chunk-local space, n the vertex normal. Offsets vertices within one cell
    // of each face that has a transition; if the vertex is near ANY face without
    // a transition (same-LOD or coarser neighbor), the primary position must be
    // kept so that seams with those neighbors stay closed.
    Vector3 ApplySecondaryOffset(Vector3 pLocal, Vector3 n, in TransitionNeeds needs, float step, Vector3 chunkSize)
    {
        float h = step;
        float eps = h * 1e-4f;

        float dpx = chunkSize.x - pLocal.x, dnx = pLocal.x;
        float dpy = chunkSize.y - pLocal.y, dny = pLocal.y;
        float dpz = chunkSize.z - pLocal.z, dnz = pLocal.z;

        bool nearPX = dpx < h - eps, nearNX = dnx < h - eps;
        bool nearPY = dpy < h - eps, nearNY = dny < h - eps;
        bool nearPZ = dpz < h - eps, nearNZ = dnz < h - eps;

        if (!(nearPX || nearNX || nearPY || nearNY || nearPZ || nearNZ)) return pLocal;

        // Force primary position near faces that have no transition.
        if ((nearPX && !needs.px) || (nearNX && !needs.nx) ||
            (nearPY && !needs.py) || (nearNY && !needs.ny) ||
            (nearPZ && !needs.pz) || (nearNZ && !needs.nz))
            return pLocal;

        float w = kTransitionWidthFraction * h;
        Vector3 delta = Vector3.zero;
        if (needs.px && nearPX) delta.x -= w * (1f - dpx / h);
        if (needs.nx && nearNX) delta.x += w * (1f - dnx / h);
        if (needs.py && nearPY) delta.y -= w * (1f - dpy / h);
        if (needs.ny && nearNY) delta.y += w * (1f - dny / h);
        if (needs.pz && nearPZ) delta.z -= w * (1f - dpz / h);
        if (needs.nz && nearNZ) delta.z += w * (1f - dnz / h);

        if (delta == Vector3.zero) return pLocal;

        // Project the offset onto the tangent plane (Eq. 4.3) to avoid flattening.
        delta -= Vector3.Dot(n, delta) * n;
        return pLocal + delta;
    }

    // ========================================================================
    // Transvoxel transition cells.
    //
    // This chunk is the COARSE side. For each face that borders a finer
    // neighbor we march a sheet of transition cells: the full-resolution face
    // (9 samples at half our cell size) lies exactly on the chunk boundary and
    // matches the fine neighbor's mesh; the half-resolution face (4 samples,
    // values identical to the corner samples of the full-res face) is pushed
    // inward by the secondary-offset transform and matches our own deformed
    // regular cells.
    // ========================================================================
    public void GenerateTransitionMeshData(TransitionNeeds needs, List<Vector3> verts, List<Vector3> norms, List<int> tris)
    {
        for (int face = 0; face < 6; face++)
        {
            if (needs.Face(face))
                GenerateTransitionFace(face, needs, verts, norms, tris);
        }
    }

    // Per full-res corner 0..8 (Fig. 4.16a: row-major, bottom row first), the
    // contribution to the 9-bit case index (Fig. 4.17).
    static readonly int[] kTransitionCaseBit = { 0x01, 0x02, 0x04, 0x80, 0x100, 0x08, 0x40, 0x20, 0x10 };

    // Half-res corners 9,A,B,C map onto full-res corners 0,2,6,8 (same values,
    // same primary positions).
    static readonly int[] kHalfToFullCorner = { 0, 2, 6, 8 };

    // Face basis: local-space face origin, U and V tangents and the axis counts.
    // All bases satisfy U x V = outward normal so the table winding is
    // consistent for all six faces (verified against the regular mesh winding).
    void GetFaceBasis(int face, int nx, int ny, int nz, float step,
                      out Vector3 faceOrigin, out Vector3 U, out Vector3 V,
                      out int nU, out int nV)
    {
        float sx = nx * step, sy = ny * step, sz = nz * step;
        switch (face)
        {
            case 0: faceOrigin = new Vector3(sx, 0, 0); U = Vector3.up;      V = Vector3.forward; nU = ny; nV = nz; break; // +X
            case 1: faceOrigin = Vector3.zero;          U = Vector3.forward; V = Vector3.up;      nU = nz; nV = ny; break; // -X
            case 2: faceOrigin = new Vector3(0, sy, 0); U = Vector3.forward; V = Vector3.right;   nU = nz; nV = nx; break; // +Y
            case 3: faceOrigin = Vector3.zero;          U = Vector3.right;   V = Vector3.forward; nU = nx; nV = nz; break; // -Y
            case 4: faceOrigin = new Vector3(0, 0, sz); U = Vector3.right;   V = Vector3.up;      nU = nx; nV = ny; break; // +Z
            case 5: faceOrigin = Vector3.zero;          U = Vector3.up;      V = Vector3.right;   nU = ny; nV = nx; break; // -Z
            default: throw new ArgumentOutOfRangeException(nameof(face));
        }
    }

    void GenerateTransitionFace(int face, TransitionNeeds needs, List<Vector3> verts, List<Vector3> norms, List<int> tris)
    {
        GetEffectiveGrid(out int nx, out int ny, out int nz, out float step);
        GetFaceBasis(face, nx, ny, nz, step, out Vector3 faceOrigin, out Vector3 U, out Vector3 V, out int nU, out int nV);

        Vector3 chunkSize = new Vector3(nx, ny, nz) * step;
        Vector3 origin = transform.position;
        float s = 0.5f * step; // fine (neighbor) sample spacing
        float gradStep = densityField.GradientStep(step);

        // Pre-sample the whole fine face grid in one batch (adjacent cells share
        // sample columns; doing it per cell would sample everything ~2-4 times).
        int W = 2 * nU + 1, H = 2 * nV + 1;
        int ux = (int)U.x, uy = (int)U.y, uz = (int)U.z;
        int vx = (int)V.x, vy = (int)V.y, vz = (int)V.z;
        int countX = ux * (W - 1) + vx * (H - 1) + 1;
        int countY = uy * (W - 1) + vy * (H - 1) + 1;
        int countZ = uz * (W - 1) + vz * (H - 1) + 1;
        int total = countX * countY * countZ;
        if (s_faceSamples == null || s_faceSamples.Length < total) s_faceSamples = new float[total];
        densityField.SampleGrid(origin + faceOrigin, countX, countY, countZ, s, s_faceSamples);
        if (isoLevel != 0f)
            for (int i = 0; i < total; i++) s_faceSamples[i] -= isoLevel;

        float FaceSample(int a, int b)
        {
            int ix = ux * a + vx * b;
            int iy = uy * a + vy * b;
            int iz = uz * a + vz * b;
            return s_faceSamples[(iz * countY + iy) * countX + ix];
        }

        s_vertexCache.Clear(); // reused as (cornerA, cornerB, halfResFlag) -> vertex index

        var d = new float[13];

        for (int j = 0; j < nV; j++)
            for (int i = 0; i < nU; i++)
            {
                for (int c = 0; c < 9; c++)
                    d[c] = FaceSample(2 * i + (c % 3), 2 * j + (c / 3));
                for (int hc = 0; hc < 4; hc++)
                    d[9 + hc] = d[kHalfToFullCorner[hc]];

                int caseCode = 0;
                for (int c = 0; c < 9; c++)
                    if (d[c] < 0f) caseCode |= kTransitionCaseBit[c];
                if (caseCode == 0 || caseCode == 511) continue;

                byte cellClass = Tables.TransitionCellClass[caseCode];
                bool invert = (cellClass & 0x80) != 0;
                Tables.RegularCell cellData = Tables.TransitionRegularCellData[cellClass & 0x7F];
                ushort[] vertexData = Tables.TransitionVertexData[caseCode];

                long triCount = cellData.GetTriangleCount();
                if (triCount == 0) continue;

                // Build/reuse the cell's vertices. Vertices on shared full-res
                // face edges are deduplicated across neighboring cells.
                for (int v = 0; v < vertexData.Length; v++)
                {
                    ushort packed = vertexData[v];
                    int c0 = (packed >> 4) & 0x0F; // corner indices live in the LOW byte
                    int c1 = packed & 0x0F;
                    bool halfRes = c0 >= 9 && c1 >= 9;

                    int f0 = c0 >= 9 ? kHalfToFullCorner[c0 - 9] : c0;
                    int f1 = c1 >= 9 ? kHalfToFullCorner[c1 - 9] : c1;
                    int a0 = 2 * i + (f0 % 3), b0 = 2 * j + (f0 / 3);
                    int a1 = 2 * i + (f1 % 3), b1 = 2 * j + (f1 / 3);
                    int g0 = b0 * W + a0;
                    int g1 = b1 * W + a1;
                    long key = (halfRes ? 1L << 62 : 0L) |
                               (g0 < g1 ? ((long)g0 << 24) | (uint)g1 : ((long)g1 << 24) | (uint)g0);

                    if (!s_vertexCache.TryGetValue(key, out int vi))
                    {
                        float d0 = d[c0], d1 = d[c1];
                        Vector3 p0 = faceOrigin + U * (a0 * s) + V * (b0 * s);
                        Vector3 p1 = faceOrigin + U * (a1 * s) + V * (b1 * s);
                        float t = (d0 != d1) ? Mathf.Clamp01(d0 / (d0 - d1)) : 0.5f;
                        Vector3 p = Vector3.Lerp(p0, p1, t);

                        Vector3 n = -densityField.Gradient(origin + p, gradStep).normalized;

                        // Vertices on the half-resolution face get the same
                        // secondary transform as the regular-cell boundary
                        // vertices so the two meshes stay sealed. Full-res face
                        // vertices are never moved.
                        if (halfRes)
                            p = ApplySecondaryOffset(p, n, needs, step, chunkSize);

                        vi = verts.Count;
                        verts.Add(p);
                        norms.Add(n);
                        s_vertexCache.Add(key, vi);
                    }
                    s_cellVerts[v] = vi;
                }

                byte[] indices = cellData.Indizes();
                for (int t = 0; t < triCount; t++)
                {
                    int i0 = s_cellVerts[indices[t * 3 + 0]];
                    int i1 = s_cellVerts[indices[t * 3 + 1]];
                    int i2 = s_cellVerts[indices[t * 3 + 2]];
                    if (i0 == i1 || i1 == i2 || i0 == i2) continue; // zero-area
                    if (invert)
                    {
                        tris.Add(i2); tris.Add(i1); tris.Add(i0);
                    }
                    else
                    {
                        tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    }
                }
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
