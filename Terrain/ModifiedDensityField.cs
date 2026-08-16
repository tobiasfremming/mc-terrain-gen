using UnityEngine;

// Runtime wrapper that layers one or more TerrainModificationCache delta fields
// on top of a base DensityField. Chunks sample this instead of the raw field,
// so every LOD level and every transition mesh sees the same modified terrain.
// The chunk manager builds two of these: render (base + visual + physical) and
// physics (base + physical only) so visual-only edits never touch colliders.
public class ModifiedDensityField : DensityField
{
    public DensityField source;

    // NOT serializable: this array does NOT survive a domain reload (script
    // recompile / play transitions). The manager checks IsValid and rebuilds
    // stale wrappers; the null guards below keep a missed path from throwing.
    [System.NonSerialized] public TerrainModificationCache[] caches;

    // False for instances that survived a domain reload with lost state.
    public bool IsValid
    {
        get
        {
            if (source == null || caches == null) return false;
            for (int i = 0; i < caches.Length; i++)
                if (caches[i] == null) return false;
            return true;
        }
    }

    public static ModifiedDensityField Create(DensityField source, params TerrainModificationCache[] caches)
    {
        var f = CreateInstance<ModifiedDensityField>();
        f.hideFlags = HideFlags.HideAndDontSave;
        f.source = source;
        f.caches = caches;
        f.isoLevel = source ? source.isoLevel : 0f;
        return f;
    }

    public override float Sample(Vector3 p)
    {
        if (source == null) return -1f; // stale wrapper: pretend empty air
        float d = source.Sample(p);
        if (caches != null)
            for (int i = 0; i < caches.Length; i++) d += caches[i].SampleDelta(p);
        return d;
    }

    public override void SampleGrid(Vector3 origin, int countX, int countY, int countZ, float step, float[] dest)
    {
        if (source == null)
        {
            for (int i = 0; i < countX * countY * countZ; i++) dest[i] = -1f;
            return;
        }
        source.SampleGrid(origin, countX, countY, countZ, step, dest);
        if (caches != null)
            for (int i = 0; i < caches.Length; i++)
                caches[i].ApplyToGrid(origin, countX, countY, countZ, step, dest);
    }

    public override Vector3 Gradient(Vector3 p, float eps)
    {
        if (source == null) return Vector3.up;
        Vector3 g = source.Gradient(p, eps);
        if (caches != null)
            for (int i = 0; i < caches.Length; i++) g += caches[i].GradientDelta(p, eps);
        return g;
    }

    public override float GradientStep(float cellSize) =>
        source != null ? source.GradientStep(cellSize) : 0.5f * cellSize;

    public override float SurfaceHardness(Vector3 p) =>
        source != null ? source.SurfaceHardness(p) : 0f;

    public override bool HasVertexColors => source != null && source.HasVertexColors;
    public override Color GetVertexColor(Vector3 worldPos) =>
        source != null ? source.GetVertexColor(worldPos) : new Color(0, 0, 0, 1);

    // Reports the BASE terrain's bounds only. Edits are handled per chunk via
    // ChunkMeshJob.modsOverlapChunk (a local overlap test), which is both
    // exact and avoids one deep cave un-skipping every solid chunk world-wide.
    public override bool TryGetHeightBounds(out float minH, out float maxH)
    {
        if (source == null) { minH = maxH = 0f; return false; }
        return source.TryGetHeightBounds(out minH, out maxH);
    }
}
