using UnityEngine;

// A species, baked: a handful of finished plants at a handful of detail levels.
//
// This is the hinge between authoring and the world. Everything upstream of it
// -- grammars, profiles, the turtle -- runs at BAKE time. Everything downstream
// (scatter, instancing) only ever sees meshes and materials, and never derives
// a grammar again.
//
// Why a fixed set of variants rather than a unique plant per instance: GPU
// instancing draws N copies of ONE mesh in one call. A world of unique meshes
// is a world of single-instance draw calls, which is the thing instancing
// exists to avoid. Eight to sixteen variants plus per-instance yaw, scale and
// tint reads as endless variety at a glance, and is what shipped games do.
[System.Serializable]
public class PlantVariant
{
    public uint seed;
    [Tooltip("Index is the LOD level. Same plant, less of it.")]
    public Mesh[] lods = new Mesh[0];
    public Bounds bounds;

    public Mesh Lod(int level) => lods == null || lods.Length == 0
        ? null
        : lods[Mathf.Clamp(level, 0, lods.Length - 1)];
}

[CreateAssetMenu(fileName = "PlantPrototypes", menuName = "Marching Cubes/Plant Prototype Set")]
public class PlantPrototypeSet : ScriptableObject
{
    [Tooltip("What these were baked from. Kept so a rebake is one button, and so it is never a mystery which grammar produced a mesh.")]
    public PlantProfile source;

    public Material barkMaterial;
    public Material leafMaterial;

    public PlantVariant[] variants = new PlantVariant[0];

    [Header("Baked stats")]
    [Tooltip("Tallest variant. The scatter uses this for LOD distances and for how far apart to space instances.")]
    public float maxHeight;
    [Tooltip("Triangles per LOD, summed across variants -- the budget number.")]
    public int[] lodTriangles = new int[0];
    public string bakedAt = "";
    [Tooltip("LOD0 triangles for ONE variant -- the number that decides whether this is scatterable or a hero asset.")]
    public int lod0TrianglesPerVariant;
    [Tooltip("Size on disk. Assets serialize as text in this project, so meshes are expensive to store and to commit.")]
    public long assetBytes;

    public int LodCount => variants != null && variants.Length > 0 && variants[0].lods != null
        ? variants[0].lods.Length : 0;

    // Deterministic pick, so the same instance always gets the same variant.
    public PlantVariant Variant(int index)
    {
        if (variants == null || variants.Length == 0) return null;
        return variants[(int)((uint)index % (uint)variants.Length)];
    }
}
