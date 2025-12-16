

using System;
using System.Collections.Generic;
using UnityEngine;
using static MarchingCubesTables;
using TransitionNeeds = MCChunkManager.TransitionNeeds;
using static TransVoxelTables;


[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class MarchingChunk : MonoBehaviour
{
    [Header("Grid")]
    public Vector3Int cells = new(32, 32, 32);  // cells per axis
    public float cellSize = 0.5f;               // world units per cell
    public float isoLevel = 0f;
   
    public int densitySampling = 1;             // 1=full resolution, higher=lower poly

    [Header("Test shape")]
    public Vector3 worldCenter = Vector3.zero;  // could be (chunkOrigin + chunkSize * 0.5);
    public float sphereRadius = 6f;             // sphere radius

    [Header("Generation")]
    public bool autoRegenerate = false;
    public bool gizmoBounds = true;

    [Header("Debug")]
    public bool showDebugInfo = false;

    public DensityField densityField;

    Mesh _mesh;
    bool _isGenerating = false; // prevent double generation

    // void OnEnable()
    // {
    //     EnsureMesh();
    //     if (autoRegenerate) Generate();
    // }

    void OnValidate()
    {
        cells = new Vector3Int(
            Mathf.Max(1, cells.x),
            Mathf.Max(1, cells.y),
            Mathf.Max(1, cells.z));
        cellSize = Mathf.Max(0.001f, cellSize);
        // if (autoRegenerate && isActiveAndEnabled) Generate();
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

    [ContextMenu("Generate")]
    public void Generate(TransitionNeeds needs){
        GenerateRegularMesh();


        if (needs.Any)
        {
            GenerateTransitionMesh(needs);
        }
    }

    public void GenerateTransitionMesh(TransitionNeeds needs)
    {
        if (!needs.Any) return;

        // === grid info ===
        int nx = Mathf.Max(1, Mathf.FloorToInt(cells.x / (float)densitySampling));
        int ny = Mathf.Max(1, Mathf.FloorToInt(cells.y / (float)densitySampling));
        int nz = Mathf.Max(1, Mathf.FloorToInt(cells.z / (float)densitySampling));

        float fineStep = cellSize * densitySampling;

        Vector3 origin = transform.position;

        // Get existing mesh data to APPEND to
        var verts = new List<Vector3>(_mesh.vertices);
        var norms = new List<Vector3>(_mesh.normals);
        var tris = new List<int>(_mesh.triangles);

        float gradStep = densityField
            ? densityField.GradientStep(fineStep)
            : fineStep * 0.1f;

        // Generate transition mesh for each face that needs it
        if (needs.px) GenerateTransitionFace(ref verts, ref norms, ref tris, origin, nx, ny, nz, fineStep, gradStep, 0);
        if (needs.nx) GenerateTransitionFace(ref verts, ref norms, ref tris, origin, nx, ny, nz, fineStep, gradStep, 1);
        if (needs.py) GenerateTransitionFace(ref verts, ref norms, ref tris, origin, nx, ny, nz, fineStep, gradStep, 2);
        if (needs.ny) GenerateTransitionFace(ref verts, ref norms, ref tris, origin, nx, ny, nz, fineStep, gradStep, 3);
        if (needs.pz) GenerateTransitionFace(ref verts, ref norms, ref tris, origin, nx, ny, nz, fineStep, gradStep, 4);
        if (needs.nz) GenerateTransitionFace(ref verts, ref norms, ref tris, origin, nx, ny, nz, fineStep, gradStep, 5);

        // Update mesh with combined regular + transition geometry
        _mesh.Clear();
        _mesh.SetVertices(verts);
        _mesh.SetNormals(norms);
        _mesh.SetTriangles(tris, 0);
        _mesh.RecalculateBounds();
    }

    void GenerateTransitionFace(
        ref List<Vector3> verts,
        ref List<Vector3> norms,
        ref List<int> tris,
        Vector3 origin,
        int nx, int ny, int nz,
        float fineStep,
        float gradStep,
        int face)
    {
        // face: 0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z
        
        // Determine iteration bounds based on face
        int u1, u2, v1, v2;
        switch (face)
        {
            case 0: case 1: u1 = ny; u2 = nz; v1 = ny - 1; v2 = nz - 1; break; // X faces: iterate over Y,Z
            case 2: case 3: u1 = nx; u2 = nz; v1 = nx - 1; v2 = nz - 1; break; // Y faces: iterate over X,Z
            case 4: case 5: u1 = nx; u2 = ny; v1 = nx - 1; v2 = ny - 1; break; // Z faces: iterate over X,Y
            default: return;
        }

        // Iterate in coarse-sized steps (2x2 blocks)
        for (int u = 0; u < v1; u += 2)
        for (int v = 0; v < v2; v += 2)
        {
            // Get cell base positions for fine and coarse faces
            Vector3 cellBaseFine = GetTransitionCellBase(face, u, v, origin, fineStep, true, nx, ny, nz);
            Vector3 cellBaseCoarse = GetTransitionCellBase(face, u, v, origin, fineStep, false, nx, ny, nz);
            
            // Sample all 13 positions: 9 fine + 4 coarse
            float[] cellSamples = new float[13];
            for (int i = 0; i < 13; i++)
            {
                Vector3 samplePos = GetTransitionSamplePos(i, cellBaseFine, cellBaseCoarse, fineStep, face);
                cellSamples[i] = DensityWorld(samplePos) - isoLevel;
            }
            
            // Build 9-bit case index from fine face samples
            int caseIndex = 0;
            for (int i = 0; i < 9; i++)
            {
                if (cellSamples[i] < 0f) caseIndex |= (1 << i);
            }
            
            // Get per-case vertex definitions
            ushort[] vtxData = TransVoxelTables.TransitionVertexData[caseIndex];
            
            // Map to equivalence class for triangulation
            int transClass = TransVoxelTables.TransitionCellClass[caseIndex];
            bool invertWinding = (transClass & 0x80) != 0;
            int cellDataIndex = transClass & 0x7F;
            
            var cellData = TransVoxelTables.TransitionCellDataTable[cellDataIndex];
            int triCount = cellData.TriangleCount;
            
            // Generate triangles
            for (int t = 0; t < triCount; t++)
            {
                int[] triVerts = new int[3];
                for (int v_idx = 0; v_idx < 3; v_idx++)
                {
                    // Get index into the per-case vertex list
                    byte vertexIndex = cellData.VertexIndex[t * 3 + v_idx];
                    ushort packed = vtxData[vertexIndex];
                    
                    // Extract endpoint indices from low byte
                    int edge0 = packed & 0x0F;
                    int edge1 = (packed >> 4) & 0x0F;
                    
                    float da = cellSamples[edge0];
                    float db = cellSamples[edge1];
                    
                    Vector3 pa = GetTransitionSamplePos(edge0, cellBaseFine, cellBaseCoarse, fineStep, face);
                    Vector3 pb = GetTransitionSamplePos(edge1, cellBaseFine, cellBaseCoarse, fineStep, face);
                    
                    float tt = da / (da - db + 1e-8f);
                    Vector3 vertPos = Vector3.Lerp(pa, pb, Mathf.Clamp01(tt));
                    
                    int vi = verts.Count;
                    verts.Add(vertPos - origin);
                    
                    Vector3 n = -GradientWorld(vertPos, gradStep).normalized;
                    norms.Add(n);
                    
                    triVerts[v_idx] = vi;
                }
                
                // Add triangle (reverse winding if needed)
                if (invertWinding)
                {
                    tris.Add(triVerts[2]);
                    tris.Add(triVerts[1]);
                    tris.Add(triVerts[0]);
                }
                else
                {
                    tris.Add(triVerts[0]);
                    tris.Add(triVerts[1]);
                    tris.Add(triVerts[2]);
                }
            }
        }
    }
    
    Vector3 GetTransitionCellBase(int face, int u, int v, Vector3 origin, float fineStep, bool isFine, int nx, int ny, int nz)
    {
        // face: 0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z
        // u, v are the 2D coordinates on the face
        // Returns the base position for a transition cell
        // Fine base: last layer inside chunk
        // Coarse base: one step outside chunk (on neighbor's side)
        
        switch (face)
        {
            case 0: // +X face
                if (isFine)
                    return origin + new Vector3((nx - 1) * fineStep, u * fineStep, v * fineStep);
                else
                    return origin + new Vector3(nx * fineStep, u * fineStep, v * fineStep);
            case 1: // -X face
                if (isFine)
                    return origin + new Vector3(0, u * fineStep, v * fineStep);
                else
                    return origin + new Vector3(-fineStep, u * fineStep, v * fineStep);
            case 2: // +Y face
                if (isFine)
                    return origin + new Vector3(u * fineStep, (ny - 1) * fineStep, v * fineStep);
                else
                    return origin + new Vector3(u * fineStep, ny * fineStep, v * fineStep);
            case 3: // -Y face
                if (isFine)
                    return origin + new Vector3(u * fineStep, 0, v * fineStep);
                else
                    return origin + new Vector3(u * fineStep, -fineStep, v * fineStep);
            case 4: // +Z face
                if (isFine)
                    return origin + new Vector3(u * fineStep, v * fineStep, (nz - 1) * fineStep);
                else
                    return origin + new Vector3(u * fineStep, v * fineStep, nz * fineStep);
            case 5: // -Z face
                if (isFine)
                    return origin + new Vector3(u * fineStep, v * fineStep, 0);
                else
                    return origin + new Vector3(u * fineStep, v * fineStep, -fineStep);
            default:
                return origin;
        }
    }
    
    Vector3 GetTransitionSamplePos(int i, Vector3 baseFine, Vector3 baseCoarse, float fineStep, int face)
    {
        // i: 0-8 are fine face (3x3), 9-12 are coarse face (2x2)
        // Returns world position for sample i
        
        if (i < 9)
        {
            // Fine face: 3x3 grid
            int fy = i / 3;
            int fz = i % 3;
            Vector3 offset = GetFaceOffset(face, fy, fz, fineStep);
            return baseFine + offset;
        }
        else
        {
            // Coarse face: 2x2 grid
            int ci = i - 9;
            int cy = ci / 2;
            int cz = ci % 2;
            Vector3 offset = GetFaceOffset(face, cy * 2, cz * 2, fineStep);
            return baseCoarse + offset;
        }
    }
    
    Vector3 GetFaceOffset(int face, int u, int v, float step)
    {
        // Maps 2D coordinates (u,v) to 3D offset based on face orientation
        switch (face)
        {
            case 0: // +X face (YZ plane)
                return new Vector3(0, u * step, v * step);
            case 1: // -X face (YZ plane)
                return new Vector3(0, u * step, v * step);
            case 2: // +Y face (XZ plane)
                return new Vector3(u * step, 0, v * step);
            case 3: // -Y face (XZ plane)
                return new Vector3(u * step, 0, v * step);
            case 4: // +Z face (XY plane)
                return new Vector3(u * step, v * step, 0);
            case 5: // -Z face (XY plane)
                return new Vector3(u * step, v * step, 0);
            default:
                return Vector3.zero;
        }
    }



    public void GenerateRegularMesh()
    {
        if (_isGenerating) return; // prevent double generation
        _isGenerating = true;
        
        EnsureMesh();

        // Keep mesh extent equal to the chunk's world size no matter the combo.
        float worldSizeX = cells.x * cellSize;   // == chunk world size on X
        float worldSizeY = cells.y * cellSize;
        float worldSizeZ = cells.z * cellSize;

        int nx = Mathf.Max(1, Mathf.FloorToInt(cells.x / (float)densitySampling));
        int ny = Mathf.Max(1, Mathf.FloorToInt(cells.y / (float)densitySampling));
        int nz = Mathf.Max(1, Mathf.FloorToInt(cells.z / (float)densitySampling));

        // derive step so that nx * step == worldSizeX, etc.
        float stepX = worldSizeX / nx;
        float stepY = worldSizeY / ny;
        float stepZ = worldSizeZ / nz;
        // if you require cubic cells, pick one step (e.g., step = Mathf.Max(stepX, stepY, stepZ)) and recompute n*.
        float effectiveCellSize = stepX; // assuming cubic and cells are same per axis


        Debug.Log($"[{name}] Generate: cells={cells}, densitySampling={densitySampling}, effective grid=({nx},{ny},{nz}), effectiveCellSize={effectiveCellSize}");

        var samples = new float[(nx + 1) * (ny + 1) * (nz + 1)];

        Vector3 origin = transform.position;
        int Idx(int x, int y, int z) => (z * (ny + 1) + y) * (nx + 1) + x;

        // SAMPLE IN WORLD SPACE (correct)
        for (int z = 0; z <= nz; z++)
            for (int y = 0; y <= ny; y++)
                for (int x = 0; x <= nx; x++)
                {
                    Vector3 pWorld = origin + new Vector3(x, y, z) * effectiveCellSize;
                    samples[Idx(x, y, z)] = DensityWorld(pWorld) - isoLevel;
                }

        var verts = new List<Vector3>(nx * ny * nz * 12);
        var norms = new List<Vector3>(nx * ny * nz * 12);
        var tris  = new List<int>(nx * ny * nz * 12);

        float gradStep = densityField ? densityField.GradientStep(effectiveCellSize)
                                    : (effectiveCellSize * 0.1f); // reduced from 0.5f for better accuracy
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
                        Vector3 v0L = InterpEdgeLocal(triTable[caseIdx, t + 0], x, y, z, c, effectiveCellSize);
                        Vector3 v1L = InterpEdgeLocal(triTable[caseIdx, t + 1], x, y, z, c, effectiveCellSize);
                        Vector3 v2L = InterpEdgeLocal(triTable[caseIdx, t + 2], x, y, z, c, effectiveCellSize);

                        int i0 = verts.Count; verts.Add(v0L);
                        int i1 = verts.Count; verts.Add(v1L);
                        int i2 = verts.Count; verts.Add(v2L);

                        // normals from WORLD positions of those verts
                        Vector3 w0 = origin + v0L;
                        Vector3 w1 = origin + v1L;
                        Vector3 w2 = origin + v2L;
                        norms.Add(-GradientWorld(w0, gradStep).normalized);
                        norms.Add(-GradientWorld(w1, gradStep).normalized);
                        norms.Add(-GradientWorld(w2, gradStep).normalized);

                        tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    }
                }

        _mesh.Clear();
        _mesh.SetVertices(verts);     // local-space verts
        _mesh.SetNormals(norms);
        _mesh.SetTriangles(tris, 0);
        _mesh.RecalculateBounds();
        
        _isGenerating = false; // allow future generation
    }

    





    Vector3 InterpEdgeLocal(int edgeId, int x, int y, int z, float[] c, float cell)
    {
        int aIdx = EdgeToCorners[edgeId, 0];
        int bIdx = EdgeToCorners[edgeId, 1];

        Vector3 paL = (new Vector3(x, y, z) + (Vector3)Corner[aIdx]) * cell; // LOCAL
        Vector3 pbL = (new Vector3(x, y, z) + (Vector3)Corner[bIdx]) * cell; // LOCAL

        float da = c[aIdx], db = c[bIdx];
        float t = da / (da - db + 1e-8f);
        return Vector3.Lerp(paL, pbL, Mathf.Clamp01(t));
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
        // if (densityField != null) return densityField.SampleMinusIso(p);
        return densityField.Sample(p);
        // Fallback: sphere test (so old scenes still work)
        //return Vector3.Distance(p, worldCenter) - sphereRadius - isoLevel;
    }

    

    Vector3 InterpEdge(int edgeId, int x, int y, int z, float[] c, Vector3 origin, float cellSizeToUse)
    {
        int aIdx = EdgeToCorners[edgeId, 0];
        int bIdx = EdgeToCorners[edgeId, 1];

        Vector3 pa = origin + (new Vector3(x, y, z) + (Vector3)Corner[aIdx]) * cellSizeToUse;
        Vector3 pb = origin + (new Vector3(x, y, z) + (Vector3)Corner[bIdx]) * cellSizeToUse;

        float da = c[aIdx];
        float db = c[bIdx];
        float t = da / (da - db + 1e-8f);
        return Vector3.Lerp(pa, pb, Mathf.Clamp01(t));
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
