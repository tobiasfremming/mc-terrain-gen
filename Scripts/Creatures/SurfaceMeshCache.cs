using System.Collections.Generic;
using UnityEngine;

// Collision geometry for climbable surfaces, read once per source mesh: the triangles a MeshCollider is built
// from, with the render mesh's own vertex normals (so a probe's normal is the smooth normal the surface is
// shaded with, not the faceted triangle normal). Nothing here is a proxy: Get uses the mesh itself, BarkOnly a
// mesh that shares the render LOD0's vertices and normals exactly and keeps only submesh 0's triangles (a
// plant's bark; its leaves are not climbable). Each collision mesh is cooked once (Physics.BakeMesh) and
// shared by every collider that uses it.
public static class SurfaceMeshCache
{
    public sealed class Entry
    {
        public Mesh mesh;              // what the MeshCollider uses (the source mesh, or its bark-only copy)
        public Mesh source;
        public Vector3[] vertices, normals; // the collision mesh's, mesh-local; normals may be null (unreadable/none)
        public int[] triangles;        // triangle index k = RaycastHit.triangleIndex: vertices triangles[3k..3k+2]
        public Bounds bounds;          // mesh-local bounds of the triangles' vertices
        public bool baked;
    }

    static readonly Dictionary<Mesh, Entry> whole = new Dictionary<Mesh, Entry>();
    static readonly Dictionary<Mesh, Entry> bark = new Dictionary<Mesh, Entry>();

    public static int Bakes { get; private set; }  // cumulative Physics.BakeMesh calls (stats)

    // The mesh itself (all submeshes) as collision geometry. Null for a null mesh; an unreadable mesh gives an
    // entry without vertex data (a probe then uses the collider's faceted hit normal).
    public static Entry Get(Mesh m)
    {
        if (m == null) return null;
        if (whole.TryGetValue(m, out Entry e) && e.mesh != null) return e;
        e = new Entry { mesh = m, source = m };
        if (m.isReadable) Read(e, m, m.triangles);
        whole[m] = e;
        return e;
    }

    // A plant's bark: lod0's vertices and normals, submesh 0's triangles. Cached per lod0 mesh.
    public static Entry BarkOnly(Mesh lod0)
    {
        if (lod0 == null) return null;
        if (bark.TryGetValue(lod0, out Entry e) && e.mesh != null) return e;
        if (lod0.subMeshCount <= 1 || !lod0.isReadable) { e = Get(lod0); bark[lod0] = e; return e; }
        int[] tris = lod0.GetTriangles(0);
        var m = new Mesh { name = lod0.name + " Bark", indexFormat = lod0.indexFormat, hideFlags = HideFlags.DontSave };
        m.vertices = lod0.vertices;
        Vector3[] nrm = lod0.normals;
        if (nrm != null && nrm.Length == m.vertexCount) m.normals = nrm;
        m.triangles = tris;
        e = new Entry { mesh = m, source = lod0 };
        Read(e, m, tris);
        bark[lod0] = e;
        return e;
    }

    static void Read(Entry e, Mesh m, int[] tris)
    {
        e.vertices = m.vertices;
        Vector3[] n = m.normals;
        e.normals = n != null && n.Length == e.vertices.Length ? n : null;
        e.triangles = tris;
        bool any = false;
        var b = new Bounds();
        for (int i = 0; i < tris.Length; i++)
        {
            Vector3 v = e.vertices[tris[i]];
            if (!any) { b = new Bounds(v, Vector3.zero); any = true; } else b.Encapsulate(v);
        }
        e.bounds = any ? b : m.bounds;
    }

    // Cooks the collision mesh once (the MeshCollider then reuses the cooked data). Main thread.
    public static void Bake(Entry e)
    {
        if (e == null || e.baked || e.mesh == null) return;
        if (e.mesh.isReadable) { Physics.BakeMesh(e.mesh.GetInstanceID(), false); Bakes++; }
        e.baked = true;
    }

    public static void Clear()
    {
        foreach (var kv in bark) if (kv.Value != null && kv.Value.mesh != null && kv.Value.mesh != kv.Key) Object.DestroyImmediate(kv.Value.mesh);
        bark.Clear(); whole.Clear();
    }
}
