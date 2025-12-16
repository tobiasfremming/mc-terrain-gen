

using System.Collections.Generic;
using UnityEngine;
using static MarchingCubesTables;


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

    void OnEnable()
    {
        EnsureMesh();
        if (autoRegenerate) Generate();
    }

    void OnValidate()
    {
        cells = new Vector3Int(
            Mathf.Max(1, cells.x),
            Mathf.Max(1, cells.y),
            Mathf.Max(1, cells.z));
        cellSize = Mathf.Max(0.001f, cellSize);
        if (autoRegenerate && isActiveAndEnabled) Generate();
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
    public void Generate()
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

    public struct TransitionNeeds {
    public bool px, nx, py, ny, pz, nz;
    public bool Any => px||nx||py||ny||pz||nz;
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
