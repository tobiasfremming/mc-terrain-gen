using UnityEngine;
using UnityEngine.Rendering;

// One baked plant from a PlantPrototypeSet standing in an enclosure (the lab's shade tree), drawn the way
// PlantScatter draws its plants: LOD0 bark and leaf submeshes through Graphics.RenderMeshInstanced with the
// set's instanced materials, casting and receiving shadows. One instance, so one draw per submesh.
// TrunkRadius is measured from the mesh so the enclosure can register the trunk as an obstacle.
public sealed class EnclosurePlant : MonoBehaviour
{
    public PlantPrototypeSet set;
    public int variant;
    public float scale = 1f;

    readonly Matrix4x4[] matrix = new Matrix4x4[1];
    Bounds worldBounds;

    // The trunk's planar radius as an obstacle (world units): the widest bark vertex between 10% and 28% of
    // the plant's height (above the root flare, below the branches), from the plant's axis, times 1.5.
    public float TrunkRadius { get; private set; }
    public float Height { get; private set; }

    public void Init(PlantPrototypeSet set, int variant, float scale)
    {
        this.set = set; this.variant = variant; this.scale = scale;
        Refresh();
    }

    void OnEnable() => Refresh();

    void Refresh()
    {
        PlantVariant pv = set != null ? set.Variant(variant) : null;
        Mesh mesh = pv != null ? pv.Lod(0) : null;
        if (mesh == null) return;
        matrix[0] = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one * scale);
        Bounds b = pv.bounds.size.sqrMagnitude > 0f ? pv.bounds : mesh.bounds;
        Height = b.max.y * scale;
        worldBounds = new Bounds(transform.position + transform.rotation * (b.center * scale), Vector3.one * (b.size.magnitude * scale));
        TrunkRadius = MeasureTrunk(mesh, b) * scale;
    }

    static float MeasureTrunk(Mesh mesh, Bounds b)
    {
        if (!mesh.isReadable) return 0.15f * Mathf.Max(b.extents.x, b.extents.z);
        Vector3[] v = mesh.vertices;
        int[] tris = mesh.GetTriangles(0); // bark
        // above the root flare (which spreads at the ground) and below the first branches
        float lo = 0.1f * b.max.y, hi = 0.28f * b.max.y, r2 = 0f;
        for (int i = 0; i < tris.Length; i++)
        {
            Vector3 p = v[tris[i]];
            if (p.y < lo || p.y > hi) continue;
            r2 = Mathf.Max(r2, p.x * p.x + p.z * p.z);
        }
        // x1.5: the obstacle also covers the root flare at the base
        return 1.5f * (r2 > 0f ? Mathf.Sqrt(r2) : 0.1f * Mathf.Max(b.extents.x, b.extents.z));
    }

    void LateUpdate()
    {
        PlantVariant pv = set != null ? set.Variant(variant) : null;
        Mesh mesh = pv != null ? pv.Lod(0) : null;
        if (mesh == null) return;
        if (transform.hasChanged) { Refresh(); transform.hasChanged = false; }
        Draw(mesh, 0, set.barkMaterial);
        if (mesh.subMeshCount > 1) Draw(mesh, 1, set.leafMaterial);
    }

    void Draw(Mesh mesh, int submesh, Material mat)
    {
        if (mat == null || !mat.enableInstancing) return; // RenderMeshInstanced throws without instancing
        var rp = new RenderParams(mat)
        {
            worldBounds = worldBounds,
            shadowCastingMode = ShadowCastingMode.On,
            receiveShadows = true,
        };
        Graphics.RenderMeshInstanced(rp, mesh, submesh, matrix, 1);
    }
}
