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

    [Header("Debug")]
    public bool showDebugInfo = false;

    public DensityField densityField;

    // Transvoxel: width of a transition cell as a fraction of this chunk's cell
    // size. The paper (Eq. 4.2, w(k) = 2^(k-2)) uses a quarter cell.
    const float kTransitionWidthFraction = 0.25f;

    Mesh _mesh;
    MeshCollider _collider;
    bool _isGenerating = false; // prevent double generation

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

    public void Generate(TransitionNeeds needs)
    {
        if (_isGenerating) return;
        _isGenerating = true;

        EnsureMesh();

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris = new List<int>();

        GenerateRegularMeshData(needs, verts, norms, tris);

        if (needs.Any)
        {
            GenerateTransitionMeshData(needs, verts, norms, tris);
        }

        _mesh.Clear();
        _mesh.SetVertices(verts);
        _mesh.SetNormals(norms);
        _mesh.SetTriangles(tris, 0);
        _mesh.RecalculateBounds();

        UpdateCollider(tris.Count > 0);

        _isGenerating = false;
    }

    void UpdateCollider(bool hasGeometry)
    {
        if (generateCollider && hasGeometry)
        {
            if (_collider == null)
            {
                _collider = GetComponent<MeshCollider>();
                if (_collider == null) _collider = gameObject.AddComponent<MeshCollider>();
            }
            _collider.sharedMesh = null; // force PhysX re-cook after mesh change
            _collider.sharedMesh = _mesh;
            _collider.enabled = true;
        }
        else
        {
            if (_collider == null) _collider = GetComponent<MeshCollider>();
            if (_collider != null)
            {
                _collider.sharedMesh = null;
                _collider.enabled = false;
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
    // ========================================================================
    public void GenerateRegularMeshData(TransitionNeeds needs, List<Vector3> verts, List<Vector3> norms, List<int> tris)
    {
        GetEffectiveGrid(out int nx, out int ny, out int nz, out float step);

        Vector3 chunkSize = new Vector3(nx, ny, nz) * step;
        Vector3 origin = transform.position;

        var samples = new float[(nx + 1) * (ny + 1) * (nz + 1)];
        int Idx(int x, int y, int z) => (z * (ny + 1) + y) * (nx + 1) + x;

        for (int z = 0; z <= nz; z++)
            for (int y = 0; y <= ny; y++)
                for (int x = 0; x <= nx; x++)
                {
                    Vector3 pWorld = origin + new Vector3(x, y, z) * step;
                    samples[Idx(x, y, z)] = DensityWorld(pWorld) - isoLevel;
                }

        float gradStep = densityField ? densityField.GradientStep(step) : step * 0.1f;

        float[] c = new float[8];
        for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        var co = Corner[i];
                        c[i] = samples[Idx(x + co.x, y + co.y, z + co.z)];
                    }

                    int caseIdx = 0;
                    for (int i = 0; i < 8; i++) if (c[i] < 0f) caseIdx |= (1 << i);
                    if (caseIdx == 0 || caseIdx == 255) continue;

                    for (int t = 0; triTable[caseIdx, t] != -1; t += 3)
                    {
                        int baseIndex = verts.Count;
                        for (int k = 0; k < 3; k++)
                        {
                            Vector3 vL = InterpEdgeLocal(triTable[caseIdx, t + k], x, y, z, c, step);
                            Vector3 n = -GradientWorld(origin + vL, gradStep).normalized;
                            vL = ApplySecondaryOffset(vL, n, needs, step, chunkSize);
                            verts.Add(vL);
                            norms.Add(n);
                        }
                        tris.Add(baseIndex); tris.Add(baseIndex + 1); tris.Add(baseIndex + 2);
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
        float gradStep = densityField ? densityField.GradientStep(step) : step * 0.1f;

        var pos = new Vector3[13]; // primary positions, chunk-local, all on the boundary plane
        var d = new float[13];

        for (int j = 0; j < nV; j++)
            for (int i = 0; i < nU; i++)
            {
                // Full-resolution face: 3x3 samples at fine spacing.
                for (int c = 0; c < 9; c++)
                {
                    int a = 2 * i + (c % 3);
                    int b = 2 * j + (c / 3);
                    Vector3 p = faceOrigin + U * (a * s) + V * (b * s);
                    pos[c] = p;
                    d[c] = DensityWorld(origin + p) - isoLevel;
                }
                // Half-resolution face: same values/primary positions as corners.
                for (int hc = 0; hc < 4; hc++)
                {
                    pos[9 + hc] = pos[kHalfToFullCorner[hc]];
                    d[9 + hc] = d[kHalfToFullCorner[hc]];
                }

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

                // Build the cell's vertices (one per entry in vertexData; the
                // triangulation indexes straight into this list).
                int baseIndex = verts.Count;
                for (int v = 0; v < vertexData.Length; v++)
                {
                    ushort packed = vertexData[v];
                    int c0 = (packed >> 4) & 0x0F; // corner indices live in the LOW byte
                    int c1 = packed & 0x0F;

                    float d0 = d[c0], d1 = d[c1];
                    float t = (d0 != d1) ? Mathf.Clamp01(d0 / (d0 - d1)) : 0.5f;
                    Vector3 p = Vector3.Lerp(pos[c0], pos[c1], t);

                    Vector3 n = -GradientWorld(origin + p, gradStep).normalized;

                    // Vertices on the half-resolution face get the same secondary
                    // transform as the regular-cell boundary vertices so the two
                    // meshes stay sealed. Full-res face vertices are never moved.
                    if (c0 >= 9 && c1 >= 9)
                        p = ApplySecondaryOffset(p, n, needs, step, chunkSize);

                    verts.Add(p);
                    norms.Add(n);
                }

                byte[] indices = cellData.Indizes();
                for (int t = 0; t < triCount; t++)
                {
                    int i0 = baseIndex + indices[t * 3 + 0];
                    int i1 = baseIndex + indices[t * 3 + 1];
                    int i2 = baseIndex + indices[t * 3 + 2];
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

    Vector3 InterpEdgeLocal(int edgeId, int x, int y, int z, float[] c, float cell)
    {
        int aIdx = EdgeToCorners[edgeId, 0];
        int bIdx = EdgeToCorners[edgeId, 1];

        Vector3 paL = (new Vector3(x, y, z) + (Vector3)Corner[aIdx]) * cell;
        Vector3 pbL = (new Vector3(x, y, z) + (Vector3)Corner[bIdx]) * cell;

        float da = c[aIdx], db = c[bIdx];
        float t = (da != db) ? Mathf.Clamp01(da / (da - db)) : 0.5f;
        return Vector3.Lerp(paL, pbL, t);
    }

    Vector3 GradientWorld(Vector3 pWorld, float step)
    {
        float dx = DensityWorld(pWorld + new Vector3(step, 0, 0)) - DensityWorld(pWorld - new Vector3(step, 0, 0));
        float dy = DensityWorld(pWorld + new Vector3(0, step, 0)) - DensityWorld(pWorld - new Vector3(0, step, 0));
        float dz = DensityWorld(pWorld + new Vector3(0, 0, step)) - DensityWorld(pWorld - new Vector3(0, 0, step));
        return new Vector3(dx, dy, dz) / (2f * step);
    }

    float DensityWorld(Vector3 p)
    {
        return densityField.Sample(p);
    }

    void OnDrawGizmosSelected()
    {
        if (!gizmoBounds) return;
        Gizmos.color = new Color(1, 0.6f, 0, 0.25f);
        var size = Vector3.Scale((Vector3)cells, new Vector3(cellSize, cellSize, cellSize));
        Gizmos.DrawWireCube(transform.position + size * 0.5f, size);
    }

    void OnGUI()
    {
        if (!showDebugInfo) return;

        var style = new GUIStyle(GUI.skin.label)
        {
            normal = { textColor = Color.white },
            fontSize = 12
        };

        string configInfo = densityField ? densityField.name : "No DensityField";
        string debugText = $"Config: {configInfo}\nDensity Sampling: {densitySampling}";

        GUI.Label(new Rect(10, 10, 200, 40), debugText, style);
    }
}
