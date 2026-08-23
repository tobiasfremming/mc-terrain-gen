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
        ApplyCaches(origin, countX, countY, countZ, step, dest);
    }

    // GPU acceleration seam (see the "GPU Compute-Shader Acceleration for
    // Terrain Density Fields" plan): identical to SampleGrid, except the
    // expensive base-terrain evaluation is supplied pre-computed (from a GPU
    // dispatch) instead of calling source.SampleGrid. Edits are ALWAYS
    // applied fresh here, at actual meshing time -- never frozen at
    // GPU-dispatch time, which could be several frames earlier -- so a
    // landed dig/footprint is never missed by a job that already has GPU
    // results in hand. `rawBase` must already contain source's raw output
    // for this exact (origin,countX,countY,countZ,step) request, laid out
    // with the same (z*countY+y)*countX+x indexing SampleGrid produces.
    //
    // NOT an override of SampleGrid itself: this instance (_renderField/
    // _physicsField) is a SINGLE SHARED object called concurrently from
    // every worker thread building a different chunk, so the "is GPU data
    // available" flag can never be mutable state on this object -- it has to
    // be an explicit per-call parameter instead, which is exactly what a new
    // method (rather than a virtual override, which can't add parameters)
    // lets us do without touching the shared SampleGrid contract at all.
    public void SampleGridWithRawBase(Vector3 origin, int countX, int countY, int countZ, float step,
                                       float[] dest, float[] rawBase)
    {
        int count = countX * countY * countZ;
        if (source == null || rawBase == null)
        {
            SampleGrid(origin, countX, countY, countZ, step, dest);
            return;
        }
        System.Array.Copy(rawBase, dest, count);
        ApplyCaches(origin, countX, countY, countZ, step, dest);
    }

    void ApplyCaches(Vector3 origin, int countX, int countY, int countZ, float step, float[] dest)
    {
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

    // Delegate directly rather than relying on the base default (which would
    // call THIS class's own TryGetHeightBounds) -- fields like PlanetField
    // override TryGetEmptySkip directly instead of TryGetHeightBounds, and
    // that override must be reached.
    public override bool TryGetEmptySkip(Vector3 boxMin, Vector3 boxMax) =>
        source != null && source.TryGetEmptySkip(boxMin, boxMax);
}
