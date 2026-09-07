using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

// Grass test bench (Assets/Scenes/GrassLab.unity). Builds a field of rolling
// ground tiles with the exact vertex layout a MarchingChunk mesh has, feeds
// them to a GrassSystem, flies the camera, and prints what the GPU is doing.
// No terrain, no streaming: only the grass is being measured.
//
//   WASD / QE   move (Shift: faster)      right mouse drag   look
//   R           re-scatter every tile     F1                 toggle the stats
public class GrassLab : MonoBehaviour
{
    public GrassSettings settings;

    [Header("Ground")]
    [Tooltip("Tiles per side; each tile is one mesh and one grass slot, like a chunk.")]
    public int tilesPerSide = 9;
    public float tileSize = 16f;
    public int cellsPerTile = 16;
    public float hillHeight = 2.5f;
    public float hillScale = 45f;
    public Material groundMaterial;
    public Color groundColor = new Color(0.14f, 0.19f, 0.09f);

    [Header("Grass")]
    public float density = 60f;
    public Color grassBase = new Color(0.13f, 0.30f, 0.06f);
    public Color grassTip = new Color(0.58f, 0.74f, 0.27f);
    public Vector3 windDir = new Vector3(1f, 0f, 0.3f);

    [Header("Camera")]
    public bool flyCamera = true;
    public float moveSpeed = 8f;
    public float lookSpeed = 0.15f;
    public bool showStats = true;

    // Same layout as ChunkMeshJob.PackedVertex / MarchingChunk.kVertexLayout.
    [StructLayout(LayoutKind.Sequential)]
    struct Vert { public Vector3 pos; public Vector3 normal; public Vector4 color; }

    static readonly VertexAttributeDescriptor[] kLayout =
    {
        new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
        new(VertexAttribute.Normal,   VertexAttributeFormat.Float32, 3),
        new(VertexAttribute.Color,    VertexAttributeFormat.Float32, 4),
    };

    readonly List<Mesh> _meshes = new();
    readonly List<Transform> _tiles = new();
    readonly List<GrassSystem.Slot> _slots = new();
    GrassSystem _sys;
    Material _runtimeGround;
    Camera _cam;
    float _yaw, _pitch;
    // Median of the last 120 frames: immune to the one huge frame an Editor
    // stall or a domain reload leaves behind, unlike a running average.
    readonly float[] _frameMs = new float[120];
    int _frameCursor;
    bool _needScatter = true;

    public float MedianMs
    {
        get
        {
            var tmp = (float[])_frameMs.Clone();
            System.Array.Sort(tmp);
            return tmp[tmp.Length / 2];
        }
    }

    void Start()
    {
        _cam = Camera.main;
        if (_cam != null)
        {
            var e = _cam.transform.eulerAngles;
            _yaw = e.y; _pitch = e.x;
        }
        BuildGround();
    }

    void OnDisable()
    {
        _sys?.Dispose(); _sys = null;
        _slots.Clear();
        foreach (var m in _meshes) if (m != null) Destroy(m);
        _meshes.Clear();
        foreach (var t in _tiles) if (t != null) Destroy(t.gameObject);
        _tiles.Clear();
        if (_runtimeGround != null) Destroy(_runtimeGround);
    }

    float Height(float x, float z)
    {
        float s = 1f / Mathf.Max(1f, hillScale);
        float h = Mathf.PerlinNoise(x * s + 11.3f, z * s + 7.1f) * 2f - 1f;
        h += (Mathf.PerlinNoise(x * s * 2.7f + 3.3f, z * s * 2.7f + 1.9f) * 2f - 1f) * 0.35f;
        return h * hillHeight;
    }

    void BuildGround()
    {
        Material mat = groundMaterial;
        if (mat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            _runtimeGround = new Material(sh) { color = groundColor };
            _runtimeGround.SetFloat("_Smoothness", 0.05f);
            mat = _runtimeGround;
        }

        int n = Mathf.Max(1, cellsPerTile);
        float half = tilesPerSide * tileSize * 0.5f;
        var verts = new Vert[(n + 1) * (n + 1)];
        var idx = new uint[n * n * 6];
        float eps = 0.25f;

        for (int tz = 0; tz < tilesPerSide; tz++)
            for (int tx = 0; tx < tilesPerSide; tx++)
            {
                var origin = new Vector3(tx * tileSize - half, 0f, tz * tileSize - half);
                float minY = float.MaxValue, maxY = float.MinValue;
                for (int z = 0; z <= n; z++)
                    for (int x = 0; x <= n; x++)
                    {
                        float lx = x * tileSize / n, lz = z * tileSize / n;
                        float wx = origin.x + lx, wz = origin.z + lz;
                        float y = Height(wx, wz);
                        // Analytic-ish normal from central differences.
                        float dx = Height(wx + eps, wz) - Height(wx - eps, wz);
                        float dz = Height(wx, wz + eps) - Height(wx, wz - eps);
                        var nrm = new Vector3(-dx, 2f * eps, -dz).normalized;
                        verts[z * (n + 1) + x] = new Vert
                        {
                            pos = new Vector3(lx, y, lz),
                            normal = nrm,
                            color = new Vector4(0f, 0f, 0f, 1f), // channel 0, no AO
                        };
                        minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                    }
                int k = 0;
                for (int z = 0; z < n; z++)
                    for (int x = 0; x < n; x++)
                    {
                        uint a = (uint)(z * (n + 1) + x), b = a + 1, c = a + (uint)(n + 1), d = c + 1;
                        idx[k++] = a; idx[k++] = c; idx[k++] = b;
                        idx[k++] = b; idx[k++] = c; idx[k++] = d;
                    }

                var mesh = new Mesh { name = "GrassLabTile", indexFormat = IndexFormat.UInt32 };
                mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
                mesh.SetVertexBufferParams(verts.Length, kLayout);
                mesh.SetVertexBufferData(verts, 0, 0, verts.Length);
                mesh.SetIndexBufferParams(idx.Length, IndexFormat.UInt32);
                mesh.SetIndexBufferData(idx, 0, 0, idx.Length);
                mesh.subMeshCount = 1;
                var bounds = new Bounds(new Vector3(tileSize * 0.5f, (minY + maxY) * 0.5f, tileSize * 0.5f),
                                        new Vector3(tileSize, maxY - minY + 0.01f, tileSize));
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, idx.Length) { bounds = bounds, vertexCount = verts.Length });
                mesh.bounds = bounds;
                _meshes.Add(mesh);

                var go = new GameObject($"Tile_{tx}_{tz}");
                go.transform.SetParent(transform, false);
                go.transform.position = origin;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                _tiles.Add(go.transform);
            }
        _needScatter = true;
    }

    void Update()
    {
        if (settings == null || settings.compute == null || settings.material == null || _cam == null) return;
        if (_sys != null && !_sys.MatchesBudget(settings)) { _sys.Dispose(); _sys = null; _slots.Clear(); _needScatter = true; }
        _sys ??= new GrassSystem(settings);

        if (Input.GetKeyDown(KeyCode.R)) _needScatter = true;
        if (Input.GetKeyDown(KeyCode.F1)) showStats = !showStats;
        if (flyCamera) Fly();

        var biome = GrassSystem.BiomeParams.Uniform(density, grassBase, grassTip);
        if (_needScatter)
        {
            _needScatter = false;
            while (_slots.Count < _meshes.Count)
            {
                var s = _sys.Acquire();
                if (s == null) break;
                _slots.Add(s);
            }
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                var t = _tiles[i];
                var b = _meshes[i].bounds;
                b.center += t.position;
                slot.bounds = b;
                slot.visible = true;
                _sys.Scatter(slot, _meshes[i], t.localToWorldMatrix, 0, biome, Vector4.zero);
                slot.version = 1;
            }
        }

        Vector3 eye = _cam.transform.position;
        _sys.Render(_cam, eye, eye - Vector3.up * 1.6f, windDir, 1f, Vector4.zero, biome, _slots);

        _frameMs[_frameCursor++ % _frameMs.Length] = Time.unscaledDeltaTime * 1000f;
    }

    void Fly()
    {
        var t = _cam.transform;
        if (Input.GetMouseButton(1))
        {
            _yaw += Input.GetAxis("Mouse X") * lookSpeed * 10f;
            _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * lookSpeed * 10f, -89f, 89f);
        }
        t.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        float sp = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? 4f : 1f) * Time.unscaledDeltaTime;
        Vector3 d = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) d += t.forward;
        if (Input.GetKey(KeyCode.S)) d -= t.forward;
        if (Input.GetKey(KeyCode.A)) d -= t.right;
        if (Input.GetKey(KeyCode.D)) d += t.right;
        if (Input.GetKey(KeyCode.E)) d += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) d -= Vector3.up;
        var p = t.position + d * sp;
        p.y = Mathf.Max(p.y, Height(p.x, p.z) + 0.3f);
        t.position = p;
    }

    void OnGUI()
    {
        if (!showStats || _sys == null) return;
        int vis = 0;
        for (int l = 0; l < GrassSystem.LodCount; l++) vis += _sys.VisibleCounts[l];
        float ms = MedianMs;
        string s =
            $"GrassLab  {ms:F2} ms/frame median ({(1000f / Mathf.Max(0.01f, ms)):F0} fps)\n" +
            $"tiles {_slots.Count}  dispatched slots {_sys.DispatchedSlots}  cull threads {_sys.CulledThreads / 1000}k\n" +
            $"visible blades {vis:N0}   (5-seg {_sys.VisibleCounts[0]:N0} / 3-seg {_sys.VisibleCounts[1]:N0} / 1-tri {_sys.VisibleCounts[2]:N0})\n" +
            $"pool {_sys.PoolBytes / (1024 * 1024)} MB, {settings.slotCapacity:N0} blades/slot, density {density}/m^2\n" +
            "WASD/QE move (Shift fast), RMB look, R rescatter, F1 hide";
        GUI.Box(new Rect(8, 8, 520, 96), "");
        GUI.Label(new Rect(16, 12, 510, 90), s);
    }
}
