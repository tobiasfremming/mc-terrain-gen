using System;
using System.Collections.Generic;
using UnityEngine;
using static MarchingCubesTables;
using TransitionNeeds = MCChunkManager.TransitionNeeds;

// All inputs, outputs and scratch space for building one chunk's mesh data.
// Self-contained so builds can run on worker threads (no Unity object access);
// instances are pooled by the manager.
public class ChunkMeshJob
{
    // inputs (set by the manager before dispatch)
    public Vector3 origin;
    public Vector3Int cells;
    public float cellSize;
    public float isoLevel;
    public int densitySampling = 1;
    public DensityField renderField;
    public DensityField physicsField;    // for the collider; null = share render mesh
    public TransitionNeeds needs;
    public bool buildCollider;           // LOD0 only
    public bool refreshCollider;         // false: leave the existing collider untouched
    public bool physicsDiffersNearby;    // visual edits overlap this chunk -> collider needs its own mesh
    public bool modsOverlapChunk;        // any edits overlap -> the all-air/all-solid skip must not apply

    // outputs
    public readonly List<Vector3> verts = new();
    public readonly List<Vector3> norms = new();
    public readonly List<Color> colors = new();   // vertex colors (biome weights), empty if unused
    public readonly List<int> tris = new();
    public readonly List<Vector3> colVerts = new();
    public readonly List<int> colTris = new();
    public bool colliderSharesRenderMesh;

    // scratch (owned by this job -> thread safe)
    internal float[] samples;
    internal float[] faceSamples;
    internal readonly Dictionary<long, int> vertexCache = new();
    internal readonly int[] cellVerts = new int[12];

    public Exception error;

    public void ResetOutputs()
    {
        verts.Clear(); norms.Clear(); colors.Clear(); tris.Clear();
        colVerts.Clear(); colTris.Clear();
        colliderSharesRenderMesh = false;
        error = null;
    }
}

// Builds chunk mesh data from a density field: modified Marching Cubes for the
// regular cells plus Transvoxel transition sheets on faces bordering finer
// neighbors (see MarchingChunk/TransVoxelTables for the conventions). Pure
// computation — safe to run on worker threads.
public static class ChunkMesher
{
    // Transvoxel: width of a transition cell as a fraction of the chunk's cell
    // size. The paper (Eq. 4.2, w(k) = 2^(k-2)) uses a quarter cell.
    const float kTransitionWidthFraction = 0.25f;

    public static void Build(ChunkMeshJob job)
    {
        try
        {
            job.ResetOutputs();

            GetEffectiveGrid(job, out int nx, out int ny, out int nz, out float step);

            // Empty-chunk skip: if the surface can't intersect this chunk's
            // vertical range, there is nothing to mesh (or collide with).
            // Disabled when terrain edits overlap the chunk: a carved cave
            // creates surface deep inside otherwise-solid rock.
            if (!job.modsOverlapChunk && job.renderField.TryGetHeightBounds(out float minH, out float maxH))
            {
                float y0 = job.origin.y, y1 = job.origin.y + ny * step;
                if (y0 > maxH || y1 < minH)
                {
                    job.colliderSharesRenderMesh = true; // both empty
                    return;
                }
            }

            GenerateRegularMeshData(job, nx, ny, nz, step);

            if (job.needs.Any)
                for (int face = 0; face < 6; face++)
                    if (job.needs.Face(face))
                        GenerateTransitionFace(job, face, nx, ny, nz, step);

            if (job.buildCollider && job.refreshCollider)
            {
                if (job.physicsField == null || job.physicsField == job.renderField || !job.physicsDiffersNearby)
                {
                    // No visual-only edits near this chunk: physics == render.
                    job.colliderSharesRenderMesh = true;
                }
                else
                {
                    GenerateCollisionMeshData(job, nx, ny, nz, step);
                }
            }
        }
        catch (Exception e)
        {
            job.error = e;
        }
    }

    static void GetEffectiveGrid(ChunkMeshJob job, out int nx, out int ny, out int nz, out float step)
    {
        int ds = Mathf.Max(1, job.densitySampling);
        nx = Mathf.Max(1, job.cells.x / ds);
        ny = Mathf.Max(1, job.cells.y / ds);
        nz = Mathf.Max(1, job.cells.z / ds);
        step = (job.cells.x * job.cellSize) / nx; // assumes cubic cells
    }

    static void EnsureSamples(ChunkMeshJob job, int count)
    {
        if (job.samples == null || job.samples.Length < count) job.samples = new float[count];
    }

    // ========================================================================
    // Regular cells with Transvoxel secondary vertex positions; vertices are
    // deduplicated with an edge-keyed cache (cuts vertex count and expensive
    // gradient evaluations ~4x).
    // ========================================================================
    static void GenerateRegularMeshData(ChunkMeshJob job, int nx, int ny, int nz, float step)
    {
        Vector3 chunkSize = new Vector3(nx, ny, nz) * step;
        Vector3 origin = job.origin;
        var field = job.renderField;
        var needs = job.needs;
        var verts = job.verts;
        var norms = job.norms;
        var tris = job.tris;

        int countX = nx + 1, countY = ny + 1, countZ = nz + 1;
        int sampleCount = countX * countY * countZ;
        EnsureSamples(job, sampleCount);
        float[] samples = job.samples;

        field.SampleGrid(origin, countX, countY, countZ, step, samples);
        if (job.isoLevel != 0f)
            for (int i = 0; i < sampleCount; i++) samples[i] -= job.isoLevel;

        float gradStep = field.GradientStep(step);

        int Idx(int x, int y, int z) => (z * countY + y) * countX + x;

        var cache = job.vertexCache;
        cache.Clear();

        int GetOrCreateVertex(int edgeId, int x, int y, int z)
        {
            int aIdx = EdgeToCorners[edgeId, 0];
            int bIdx = EdgeToCorners[edgeId, 1];
            var ca = Corner[aIdx];
            var cb = Corner[bIdx];
            int ia = Idx(x + ca.x, y + ca.y, z + ca.z);
            int ib = Idx(x + cb.x, y + cb.y, z + cb.z);
            long key = ia < ib ? ((long)ia << 32) | (uint)ib : ((long)ib << 32) | (uint)ia;

            if (cache.TryGetValue(key, out int vi)) return vi;

            float da = samples[ia], db = samples[ib];
            Vector3 pa = new Vector3(x + ca.x, y + ca.y, z + ca.z) * step;
            Vector3 pb = new Vector3(x + cb.x, y + cb.y, z + cb.z) * step;
            float t = (da != db) ? Mathf.Clamp01(da / (da - db)) : 0.5f;
            Vector3 pL = Vector3.Lerp(pa, pb, t);

            Vector3 n = -field.Gradient(origin + pL, gradStep).normalized;
            Vector3 pPrimary = pL;
            pL = ApplySecondaryOffset(pL, n, needs, step, chunkSize);

            vi = verts.Count;
            verts.Add(pL);
            norms.Add(n);
            job.colors.Add(VertexColorWithAO(field, origin + pPrimary, n, step));
            cache.Add(key, vi);
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
                        if (samples[Idx(x + co.x, y + co.y, z + co.z)] < 0f) caseIdx |= (1 << i);
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

    // Vertex color: RGB = biome weights (for palette/texture blending),
    // A = cheap baked ambient occlusion — one density sample along the normal;
    // open sky above gives 1, crevices/overhang undersides approach 0. This is
    // the rasterizer's stand-in for the raymarchers' curvature/AO darkening.
    static Color VertexColorWithAO(DensityField field, Vector3 worldPos, Vector3 n, float step)
    {
        Color c = field.HasVertexColors ? field.GetVertexColor(worldPos) : new Color(0, 0, 0, 1);
        float d = 2.5f * step;
        float above = field.Sample(worldPos + n * d);
        c.a = 0.25f + 0.75f * Mathf.Clamp01(-above / d);
        return c;
    }

    // Lengyel Eq. 4.2/4.3. Offsets vertices within one cell of each face that
    // has a transition; kept at the primary position near faces without one so
    // seams with same-LOD/coarser neighbors stay closed.
    static Vector3 ApplySecondaryOffset(Vector3 pLocal, Vector3 n, in TransitionNeeds needs, float step, Vector3 chunkSize)
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

        delta -= Vector3.Dot(n, delta) * n; // tangent-plane projection (Eq. 4.3)
        return pLocal + delta;
    }

    // ========================================================================
    // Transvoxel transition cells (this chunk is the COARSE side; see the
    // conventions documented in TransVoxelTables/older revisions).
    // ========================================================================
    static readonly int[] kTransitionCaseBit = { 0x01, 0x02, 0x04, 0x80, 0x100, 0x08, 0x40, 0x20, 0x10 };
    static readonly int[] kHalfToFullCorner = { 0, 2, 6, 8 };

    static void GetFaceBasis(int face, int nx, int ny, int nz, float step,
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

    static void GenerateTransitionFace(ChunkMeshJob job, int face, int nx, int ny, int nz, float step)
    {
        GetFaceBasis(face, nx, ny, nz, step, out Vector3 faceOrigin, out Vector3 U, out Vector3 V, out int nU, out int nV);

        Vector3 chunkSize = new Vector3(nx, ny, nz) * step;
        Vector3 origin = job.origin;
        var field = job.renderField;
        var needs = job.needs;
        var verts = job.verts;
        var norms = job.norms;
        var tris = job.tris;

        float s = 0.5f * step; // fine (neighbor) sample spacing
        float gradStep = field.GradientStep(step);

        // Pre-sample the whole fine face grid in one batch.
        int W = 2 * nU + 1, H = 2 * nV + 1;
        int ux = (int)U.x, uy = (int)U.y, uz = (int)U.z;
        int vx = (int)V.x, vy = (int)V.y, vz = (int)V.z;
        int countX = ux * (W - 1) + vx * (H - 1) + 1;
        int countY = uy * (W - 1) + vy * (H - 1) + 1;
        int countZ = uz * (W - 1) + vz * (H - 1) + 1;
        int total = countX * countY * countZ;
        if (job.faceSamples == null || job.faceSamples.Length < total) job.faceSamples = new float[total];
        float[] faceSamples = job.faceSamples;
        field.SampleGrid(origin + faceOrigin, countX, countY, countZ, s, faceSamples);
        if (job.isoLevel != 0f)
            for (int i = 0; i < total; i++) faceSamples[i] -= job.isoLevel;

        float FaceSample(int a, int b)
        {
            int ix = ux * a + vx * b;
            int iy = uy * a + vy * b;
            int iz = uz * a + vz * b;
            return faceSamples[(iz * countY + iy) * countX + ix];
        }

        var cache = job.vertexCache;
        cache.Clear(); // reused as (cornerA, cornerB, halfResFlag) -> vertex index

        var d = new float[13];
        var cellVerts = job.cellVerts;

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

                    if (!cache.TryGetValue(key, out int vi))
                    {
                        float d0 = d[c0], d1 = d[c1];
                        Vector3 p0 = faceOrigin + U * (a0 * s) + V * (b0 * s);
                        Vector3 p1 = faceOrigin + U * (a1 * s) + V * (b1 * s);
                        float t = (d0 != d1) ? Mathf.Clamp01(d0 / (d0 - d1)) : 0.5f;
                        Vector3 p = Vector3.Lerp(p0, p1, t);

                        Vector3 n = -field.Gradient(origin + p, gradStep).normalized;

                        // Half-res face vertices get the same secondary
                        // transform as the regular boundary vertices; full-res
                        // face vertices are never moved.
                        Vector3 pPrimary = p;
                        if (halfRes)
                            p = ApplySecondaryOffset(p, n, needs, step, chunkSize);

                        vi = verts.Count;
                        verts.Add(p);
                        norms.Add(n);
                        job.colors.Add(VertexColorWithAO(field, origin + pPrimary, n, step));
                        cache.Add(key, vi);
                    }
                    cellVerts[v] = vi;
                }

                byte[] indices = cellData.Indizes();
                for (int t = 0; t < triCount; t++)
                {
                    int i0 = cellVerts[indices[t * 3 + 0]];
                    int i1 = cellVerts[indices[t * 3 + 1]];
                    int i2 = cellVerts[indices[t * 3 + 2]];
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

    // Marching cubes for collision only: no normals/gradients, no Transvoxel
    // deformation. Adjacent same-level chunks share boundary samples, so their
    // collision meshes stay sealed; colliders exist only on level-0 chunks.
    static void GenerateCollisionMeshData(ChunkMeshJob job, int nx, int ny, int nz, float step)
    {
        Vector3 origin = job.origin;
        var field = job.physicsField;
        var verts = job.colVerts;
        var tris = job.colTris;

        int countX = nx + 1, countY = ny + 1, countZ = nz + 1;
        int sampleCount = countX * countY * countZ;
        EnsureSamples(job, sampleCount);
        float[] samples = job.samples;

        field.SampleGrid(origin, countX, countY, countZ, step, samples);
        if (job.isoLevel != 0f)
            for (int i = 0; i < sampleCount; i++) samples[i] -= job.isoLevel;

        int Idx(int x, int y, int z) => (z * countY + y) * countX + x;

        var cache = job.vertexCache;
        cache.Clear();

        int GetOrCreateVertex(int edgeId, int x, int y, int z)
        {
            int aIdx = EdgeToCorners[edgeId, 0];
            int bIdx = EdgeToCorners[edgeId, 1];
            var ca = Corner[aIdx];
            var cb = Corner[bIdx];
            int ia = Idx(x + ca.x, y + ca.y, z + ca.z);
            int ib = Idx(x + cb.x, y + cb.y, z + cb.z);
            long key = ia < ib ? ((long)ia << 32) | (uint)ib : ((long)ib << 32) | (uint)ia;

            if (cache.TryGetValue(key, out int vi)) return vi;

            float da = samples[ia], db = samples[ib];
            Vector3 pa = new Vector3(x + ca.x, y + ca.y, z + ca.z) * step;
            Vector3 pb = new Vector3(x + cb.x, y + cb.y, z + cb.z) * step;
            float t = (da != db) ? Mathf.Clamp01(da / (da - db)) : 0.5f;

            vi = verts.Count;
            verts.Add(Vector3.Lerp(pa, pb, t));
            cache.Add(key, vi);
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
                        if (samples[Idx(x + co.x, y + co.y, z + co.z)] < 0f) caseIdx |= (1 << i);
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
}
