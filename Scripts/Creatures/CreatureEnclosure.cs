using UnityEngine;

// A circular enclosure: a disc of radius `radius` around this transform's position (up = transform.up),
// marked on the ground by a low sandstone curb. The curb is decoration only: no collider, so the player
// walks over it; creatures are held inside by the EnclosureArea clamp, not by the curb.
public sealed class CreatureEnclosure : MonoBehaviour
{
    public float radius = 8f, curbHeight = 0.12f, curbWidth = 0.3f;
    public int segments = 128;
    public Material borderMaterial;
    // Circular obstacles inside the enclosure (a tree): world-space centre on the ground, radius.
    public System.Collections.Generic.List<Vector4> obstacles = new System.Collections.Generic.List<Vector4>();

    const float kSink = 0.03f;          // the curb base sits this far below the probed sand
    const float kCentreWindow = 4f;     // probe window for the centreline, walking from the previous section
    const float kProfileWindow = 1f;    // probe window for each profile vertex, around its centreline point

    // Cross-section, inner bottom -> over the top -> outer bottom: (lateral offset / width, height / curbHeight).
    // Bottom width w, top 0.6 w, with the top corners rounded off.
    static readonly Vector2[] kProfile =
    {
        new Vector2(-0.50f, 0f), new Vector2(-0.43f, 0.55f), new Vector2(-0.36f, 0.88f), new Vector2(-0.24f, 1f),
        new Vector2( 0.24f, 1f), new Vector2( 0.36f, 0.88f), new Vector2( 0.43f, 0.55f), new Vector2( 0.50f, 0f),
    };

    GameObject curb;
    Mesh curbMesh; // owned: destroyed with the curb it belongs to

    public EnclosureArea AreaFor(CreatureSpecies sp, float sizeScale)
    {
        EnclosureArea a = EnclosureArea.For(transform.position, transform.up, radius, sp, sizeScale);
        int n = obstacles != null ? obstacles.Count : 0;
        if (n > 0)
        {
            a.obstacleCenters = new Vector3[n]; a.obstacleRadii = new float[n];
            for (int i = 0; i < n; i++) { a.obstacleCenters[i] = obstacles[i]; a.obstacleRadii[i] = obstacles[i].w; }
            a.keepOut = EnclosureArea.KeepOutFor(sp, sizeScale);
        }
        return a;
    }

    // Builds (or rebuilds) the curb on a child object. The mesh is in world space, so the child sits at the
    // world origin with no rotation.
    internal void Build(IGroundProbe ground)
    {
        DestroyCurb();
        Mesh m = BuildCurb(ground, transform.position, transform.up, radius, curbHeight, curbWidth, segments);
        m.name = name + " Curb";
        curbMesh = m;
        curb = new GameObject("Curb");
        curb.transform.SetParent(transform, false);
        curb.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        curb.transform.localScale = Vector3.one;
        curb.AddComponent<MeshFilter>().sharedMesh = m;
        var r = curb.AddComponent<MeshRenderer>();
        r.sharedMaterial = borderMaterial;
    }

    void OnDestroy() => DestroyCurb();

    void DestroyCurb()
    {
        if (curb != null) { if (Application.isPlaying) Destroy(curb); else DestroyImmediate(curb); }
        if (curbMesh != null) { if (Application.isPlaying) Destroy(curbMesh); else DestroyImmediate(curbMesh); }
        curb = null; curbMesh = null;
    }

    // A closed ring strip around `center` (world space). Every vertex is probed to the ground, so the curb
    // follows the sand across its width as well as around the ring; its base is sunk kSink below the
    // probed surface so slopes show no gap. segments + 1 cross-sections: the last repeats the first (UV seam).
    public static Mesh BuildCurb(IGroundProbe ground, Vector3 center, Vector3 up, float radius, float height, float width, int segments)
    {
        up = up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.up;
        segments = Mathf.Max(3, segments);
        int per = kProfile.Length, sections = segments + 1;
        var verts = new Vector3[sections * per];
        var uvs = new Vector2[verts.Length];

        Vector3 t0 = Vector3.Cross(up, Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
        Vector3 t1 = Vector3.Cross(up, t0);

        // the centre's ground, as the seed for walking the centreline
        Vector3 prev = ground.Probe(center, up, kCentreWindow, out Vector3 cg, out _) ? cg : center;
        float circumference = 2f * Mathf.PI * radius;
        for (int s = 0; s < segments; s++)
        {
            float a = s * 2f * Mathf.PI / segments;
            Vector3 radial = t0 * Mathf.Cos(a) + t1 * Mathf.Sin(a);
            Vector3 onPlane = center + radial * radius;
            Vector3 near = onPlane + up * Vector3.Dot(prev - onPlane, up);
            Vector3 line = ground.Probe(near, up, kCentreWindow, out Vector3 g, out _) ? g : near;
            prev = line;
            for (int j = 0; j < per; j++)
            {
                Vector3 q = line + radial * (kProfile[j].x * width);
                Vector3 baseP = ground.Probe(q, up, kProfileWindow, out Vector3 gq, out _) ? gq : q;
                verts[s * per + j] = baseP + up * (kProfile[j].y * (height + kSink) - kSink);
                uvs[s * per + j] = new Vector2(s * circumference / segments, j / (float)(per - 1));
            }
        }
        for (int j = 0; j < per; j++)
        {
            verts[segments * per + j] = verts[j];
            uvs[segments * per + j] = new Vector2(circumference, uvs[j].y);
        }

        // winding: the top quad of section 0 must face up
        int top = per / 2 - 1;
        Vector3 n = Vector3.Cross(verts[per + top] - verts[top], verts[top + 1] - verts[top]);
        bool flip = Vector3.Dot(n, up) < 0f;

        var tris = new int[segments * (per - 1) * 6];
        int k = 0;
        for (int s = 0; s < segments; s++)
            for (int j = 0; j < per - 1; j++)
            {
                int a = s * per + j, b = (s + 1) * per + j, c = (s + 1) * per + j + 1, d = s * per + j + 1;
                if (!flip) { tris[k++] = a; tris[k++] = b; tris[k++] = c; tris[k++] = a; tris[k++] = c; tris[k++] = d; }
                else { tris[k++] = a; tris[k++] = c; tris[k++] = b; tris[k++] = a; tris[k++] = d; tris[k++] = c; }
            }

        var mesh = new Mesh { name = "Curb" };
        if (verts.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        // the seam's two copies of the first section each saw only one side: share the average
        Vector3[] nrm = mesh.normals;
        for (int j = 0; j < per; j++)
        {
            Vector3 avg = (nrm[j] + nrm[segments * per + j]).normalized;
            nrm[j] = avg; nrm[segments * per + j] = avg;
        }
        mesh.normals = nrm;
        mesh.RecalculateBounds();
        return mesh;
    }
}
