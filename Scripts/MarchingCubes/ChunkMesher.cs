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
    public Vector3 origin;         // chunk's true world position -- used ONLY for chunk transform placement and the empty-skip AABB test
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
    // One buffer PER FACE, not one shared scratch: the transition-face grids
    // are now sampled up front (before the regular mesh is polygonized) so the
    // regular grid's boundary planes can be overwritten from them -- see
    // ChunkMesher.Build. They therefore all have to be alive at once.
    internal readonly float[][] faceSamples = new float[6][];
    internal readonly Dictionary<long, int> vertexCache = new();
    internal readonly int[] cellVerts = new int[12];

    // GPU-prefetched RAW (pre-edit) base density, keyed by which request they
    // satisfy -- set by the manager's GPU staging step before Build() runs on
    // a worker thread; null (the common case when GPU isn't available/used)
    // means fall back to the normal CPU field.SampleGrid path. See
    // ModifiedDensityField.SampleGridWithRawBase for why edits are still
    // always applied fresh here, never frozen at GPU-dispatch time.
    internal float[] gpuRawRegular;
    internal readonly float[][] gpuRawFaces = new float[6][];
    internal float[] gpuRawCollision;

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

            // Empty-chunk skip: if the surface can't possibly cross this
            // chunk's AABB, there is nothing to mesh (or collide with).
            // Disabled when terrain edits overlap the chunk: a carved cave
            // creates surface deep inside otherwise-solid rock.
            if (!job.modsOverlapChunk)
            {
                Vector3 boxMin = job.origin;
                Vector3 boxMax = job.origin + new Vector3(nx, ny, nz) * step;
                if (job.renderField.TryGetEmptySkip(boxMin, boxMax))
                {
                    job.colliderSharesRenderMesh = true; // both empty
                    return;
                }
            }

            // Order matters. The transition-face grids must exist BEFORE the
            // regular mesh is polygonized, because PatchRegularBoundaryPlanes
            // rewrites the regular grid's boundary planes from them -- that is
            // what keeps LOD seams closed under band-limiting (see its
            // comment). Polygonizing first would bake the un-patched values
            // into the vertices.
            // NeedsFaceGrid, not Face: a chunk that merely touches a finer
            // chunk along an EDGE or CORNER generates no transition cell there,
            // but still shares those grid points with it and so must
            // band-limit them the same way. See PatchRegularBoundaryPlanes.
            for (int face = 0; face < 6; face++)
                if (job.needs.NeedsFaceGrid(face))
                    SampleTransitionFaceGrid(job, face, nx, ny, nz, step);

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

    // Public: shared with MCChunkManager's hoisted empty-chunk skip check
    // (used to run only inside Build(); evaluating it right after
    // PrepareBuild means a provably-empty chunk never costs a GPU batch
    // slot at all -- see PlanGridRequests's identical reuse for descriptor
    // shapes).
    public static void GetEffectiveGrid(ChunkMeshJob job, out int nx, out int ny, out int nz, out float step)
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

        if (field is ModifiedDensityField mdf)
            mdf.SampleGridWithRawBase(origin, countX, countY, countZ, step, samples, job.gpuRawRegular);
        else
            field.SampleGrid(origin, countX, countY, countZ, step, samples);
        job.gpuRawRegular = null; // one-shot: don't let a pooled job reuse stale GPU data next time
        if (job.isoLevel != 0f)
            for (int i = 0; i < sampleCount; i++) samples[i] -= job.isoLevel;

        // Both grids are iso-adjusted by now, so the two are directly
        // comparable -- patch before anything reads `samples`.
        PatchRegularBoundaryPlanes(job, nx, ny, nz, step);

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

            Vector3 n = -field.Gradient(origin + pL, gradStep, step).normalized;
            Vector3 pPrimary = pL;
            pL = ApplySecondaryOffset(pL, n, needs, step, chunkSize);

            vi = verts.Count;
            verts.Add(pL);
            norms.Add(n);
            job.colors.Add(VertexColorWithAO(field, origin + pPrimary, n, step, step));
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
    // `step` sets the probe DISTANCE; `fw` is the band-limiting filter width
    // of the grid this vertex came from. They differ on transition faces,
    // where the probe still reaches out by the coarse step but the density
    // must be filtered at the fine face spacing so both sides of the seam
    // agree.
    static Color VertexColorWithAO(DensityField field, Vector3 worldPos, Vector3 n, float step, float fw)
    {
        Color c = field.HasVertexColors ? field.GetVertexColor(worldPos) : new Color(0, 0, 0, 1);
        float d = 2.5f * step;
        float above = field.Sample(worldPos + n * d, fw);
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

    // One grid-fill request this job will need -- the regular mesh grid,
    // one entry per needed Transvoxel transition face, and (if a fresh
    // collider is needed) the collision grid. Used by the GPU staging code
    // to build dispatch descriptors; reuses the exact same GetEffectiveGrid/
    // GetFaceBasis math Build() itself uses, so the two can never drift.
    public struct PlannedRequest
    {
        public enum Kind { Regular, Face, Collision }
        public Kind kind;
        public int face; // valid only when kind == Face
        public Vector3 origin;
        public float step;
        public int countX, countY, countZ;
    }

    public static List<PlannedRequest> PlanGridRequests(ChunkMeshJob job)
    {
        var result = new List<PlannedRequest>();
        GetEffectiveGrid(job, out int nx, out int ny, out int nz, out float step);

        result.Add(new PlannedRequest
        {
            kind = PlannedRequest.Kind.Regular,
            origin = job.origin,
            step = step,
            countX = nx + 1, countY = ny + 1, countZ = nz + 1,
        });

        if (job.needs.AnyFaceGrid)
        {
            for (int face = 0; face < 6; face++)
            {
                // NeedsFaceGrid, not Face -- edge/corner-only contacts need the
                // grid for PatchRegularBoundaryPlanes even though they produce
                // no transition cell.
                if (!job.needs.NeedsFaceGrid(face)) continue;
                GetFaceBasis(face, nx, ny, nz, step, out Vector3 faceOrigin, out Vector3 U, out Vector3 V, out int nU, out int nV);
                float s = 0.5f * step;
                int ux = (int)U.x, uy = (int)U.y, uz = (int)U.z;
                int vx = (int)V.x, vy = (int)V.y, vz = (int)V.z;
                int W = 2 * nU + 1, H = 2 * nV + 1;
                result.Add(new PlannedRequest
                {
                    kind = PlannedRequest.Kind.Face,
                    face = face,
                    origin = job.origin + faceOrigin,
                    step = s,
                    countX = ux * (W - 1) + vx * (H - 1) + 1,
                    countY = uy * (W - 1) + vy * (H - 1) + 1,
                    countZ = uz * (W - 1) + vz * (H - 1) + 1,
                });
            }
        }

        // Mirrors Build()'s own collision trigger condition exactly.
        if (job.buildCollider && job.refreshCollider &&
            job.physicsField != null && job.physicsField != job.renderField && job.physicsDiffersNearby)
        {
            result.Add(new PlannedRequest
            {
                kind = PlannedRequest.Kind.Collision,
                origin = job.origin,
                step = step,
                countX = nx + 1, countY = ny + 1, countZ = nz + 1,
            });
        }

        return result;
    }

    // THE fix for LOD-seam cracks under band-limiting. Read this with
    // TerrainNoise.cs's header.
    //
    // Transvoxel assumes ONE density field sampled at two rates: Lengyel
    // defines the transition cell's four low-res corner values to simply BE
    // the coincident high-res samples, which is trivially true when the field
    // does not depend on sample spacing. Band-limiting made it false -- the
    // coarse chunk's own grid (spacing `step`) and the fine face grid
    // (spacing `s`) became genuinely different functions at the same points --
    // and a zipper handed two different pieces of cloth leaves a gap.
    //
    // Rather than teach Transvoxel about two fields (impossible: one case code
    // drives both sides of the cell), restore its premise. The rule that makes
    // the field single-valued again is:
    //
    //     a grid point is band-limited at the step of the FINEST chunk that
    //     touches it
    //
    // which every chunk sharing that point evaluates identically, so they
    // cannot disagree. It is deliberately stated per POINT rather than per
    // face, because the face-only version leaves rims: a chunk diagonally
    // outside the corner of a finer region shares only an EDGE with it,
    // generates no transition cell there, and would otherwise leave those
    // points coarse while its neighbour made them fine -- a thin crack along
    // the 12 edges of every clipmap box. FinerTouches answers the rule for
    // faces, edges and corners uniformly.
    //
    // Implementation: the fine-filtered values already exist in the face
    // grids (their even indices are exactly the coarse grid points), so the
    // patch is a copy, not extra sampling. On a transitioning face this also
    // restores Lengyel's identity exactly --
    //
    //   coarse grid on the plane == face grid even samples == d[0,2,6,8]
    //                            == d[9..12]
    //
    // -- so the coarse cell's boundary polygon and the transition cell's
    // low-res polygon agree in both value and sign, position and topology.
    // The cost is a one-cell-thick band where a chunk carries slightly finer
    // detail than its interior, which is exactly the region the transition
    // cell exists to blend.
    static void PatchRegularBoundaryPlanes(ChunkMeshJob job, int nx, int ny, int nz, float step)
    {
        var needs = job.needs;
        if (!needs.AnyFaceGrid) return;

        int cX = nx + 1, cY = ny + 1;
        float[] regular = job.samples;

        // FinerTouches only depends on which SIDE of the chunk a point is on
        // per axis, so there are 27 possible answers -- resolve them once
        // instead of walking 26 neighbours per grid point.
        Span<bool> fine = stackalloc bool[27]; // per-build, on a worker thread: keep it off the heap
        for (int sz = -1; sz <= 1; sz++)
            for (int sy = -1; sy <= 1; sy++)
                for (int sx = -1; sx <= 1; sx++)
                    fine[(sz + 1) * 9 + (sy + 1) * 3 + (sx + 1)] = needs.FinerTouches(sx, sy, sz);

        int Side(int i, int n) => i == 0 ? -1 : (i == n ? 1 : 0);

        for (int face = 0; face < 6; face++)
        {
            if (!needs.NeedsFaceGrid(face)) continue;
            float[] faceSamples = job.faceSamples[face];
            if (faceSamples == null) continue;

            GetFaceBasis(face, nx, ny, nz, step, out Vector3 faceOrigin, out Vector3 U, out Vector3 V, out int nU, out int nV);
            int ux = (int)U.x, uy = (int)U.y, uz = (int)U.z;
            int vx = (int)V.x, vy = (int)V.y, vz = (int)V.z;
            int W = 2 * nU + 1, H = 2 * nV + 1;
            int fcX = ux * (W - 1) + vx * (H - 1) + 1;
            int fcY = uy * (W - 1) + vy * (H - 1) + 1;

            int baseX = Mathf.RoundToInt(faceOrigin.x / step);
            int baseY = Mathf.RoundToInt(faceOrigin.y / step);
            int baseZ = Mathf.RoundToInt(faceOrigin.z / step);

            for (int b = 0; b <= nV; b++)
                for (int a = 0; a <= nU; a++)
                {
                    int rx = baseX + ux * a + vx * b;
                    int ry = baseY + uy * a + vy * b;
                    int rz = baseZ + uz * a + vz * b;
                    if (!fine[(Side(rz, nz) + 1) * 9 + (Side(ry, ny) + 1) * 3 + (Side(rx, nx) + 1)]) continue;

                    // face grid is at spacing s = step/2, so coarse point
                    // (a, b) sits at full-res index (2a, 2b).
                    int fx = ux * (2 * a) + vx * (2 * b);
                    int fy = uy * (2 * a) + vy * (2 * b);
                    int fz = uz * (2 * a) + vz * (2 * b);
                    regular[(rz * cY + ry) * cX + rx] = faceSamples[(fz * fcY + fy) * fcX + fx];
                }
        }
    }

    // Sampling half of the transition face, split out of GenerateTransitionFace
    // so every face grid exists BEFORE the regular mesh is polygonized (see
    // Build and PatchRegularBoundaryPlanes for why that ordering is what keeps
    // LOD seams closed).
    static void SampleTransitionFaceGrid(ChunkMeshJob job, int face, int nx, int ny, int nz, float step)
    {
        GetFaceBasis(face, nx, ny, nz, step, out Vector3 faceOrigin, out Vector3 U, out Vector3 V, out int nU, out int nV);
        var field = job.renderField;
        float s = 0.5f * step; // fine (neighbor) sample spacing

        int W = 2 * nU + 1, H = 2 * nV + 1;
        int ux = (int)U.x, uy = (int)U.y, uz = (int)U.z;
        int vx = (int)V.x, vy = (int)V.y, vz = (int)V.z;
        int countX = ux * (W - 1) + vx * (H - 1) + 1;
        int countY = uy * (W - 1) + vy * (H - 1) + 1;
        int countZ = uz * (W - 1) + vz * (H - 1) + 1;
        int total = countX * countY * countZ;

        var buf = job.faceSamples[face];
        if (buf == null || buf.Length < total) job.faceSamples[face] = buf = new float[total];

        float[] gpuRaw = job.gpuRawFaces[face];
        if (field is ModifiedDensityField mdf)
            mdf.SampleGridWithRawBase(job.origin + faceOrigin, countX, countY, countZ, s, buf, gpuRaw);
        else
            field.SampleGrid(job.origin + faceOrigin, countX, countY, countZ, s, buf);
        job.gpuRawFaces[face] = null; // one-shot, see GenerateRegularMeshData's identical note
        if (job.isoLevel != 0f)
            for (int i = 0; i < total; i++) buf[i] -= job.isoLevel;
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

        int W = 2 * nU + 1, H = 2 * nV + 1;
        int ux = (int)U.x, uy = (int)U.y, uz = (int)U.z;
        int vx = (int)V.x, vy = (int)V.y, vz = (int)V.z;
        int countX = ux * (W - 1) + vx * (H - 1) + 1;
        int countY = uy * (W - 1) + vy * (H - 1) + 1;
        float[] faceSamples = job.faceSamples[face]; // filled by SampleTransitionFaceGrid

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
                // Lengyel's identity: the low-res corners ARE the coincident
                // high-res samples. Valid again because
                // PatchRegularBoundaryPlanes made the coarse grid agree with
                // this face grid on this plane -- see its comment.
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

                        // Each vertex is band-limited at the resolution of
                        // the side it belongs to and will be welded against:
                        // half-res vertices meet OUR regular mesh (step),
                        // full-res ones meet the fine neighbour's (s). Using
                        // one width for both pulls one of the two seams open.
                        // gradStep is already GradientStep(step) on both sides
                        // here, matching GenerateRegularMeshData exactly, so
                        // the half-res normals -- and therefore the secondary
                        // offset below, which is driven by n -- come out
                        // identical to the regular mesh's.
                        float vfw = halfRes ? step : s;
                        Vector3 n = -field.Gradient(origin + p, gradStep, vfw).normalized;

                        // Half-res face vertices get the same secondary
                        // transform as the regular boundary vertices; full-res
                        // face vertices are never moved.
                        Vector3 pPrimary = p;
                        if (halfRes)
                            p = ApplySecondaryOffset(p, n, needs, step, chunkSize);

                        vi = verts.Count;
                        verts.Add(p);
                        norms.Add(n);
                        job.colors.Add(VertexColorWithAO(field, origin + pPrimary, n, step, vfw));
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

        if (field is ModifiedDensityField mdf)
            mdf.SampleGridWithRawBase(origin, countX, countY, countZ, step, samples, job.gpuRawCollision);
        else
            field.SampleGrid(origin, countX, countY, countZ, step, samples);
        job.gpuRawCollision = null; // one-shot, see GenerateRegularMeshData's identical note
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
