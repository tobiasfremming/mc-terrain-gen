using UnityEngine;

public abstract class DensityField : ScriptableObject
{
    [Tooltip("The isovalue of the surface you want to extract. Keep 0 unless you need a shift.")]
    public float isoLevel = 0f;

    // Return *signed* density. Convention used by the meshing code in this
    // project: positive = solid, negative = air (e.g. heightfields return h - y).
    //
    // `filterWidth` is the world-space spacing between adjacent samples in
    // whatever grid this evaluation belongs to. Fields band-limit themselves
    // to it: detail finer than that spacing can resolve is faded out rather
    // than point-sampled into aliasing. See TerrainNoise's header for the
    // full rationale and the watertightness argument. Pass 0 for "unfiltered"
    // -- correct for isolated point queries (gravity probes, terrain edits),
    // wrong for grid fills, which must pass their real spacing or coarse LODs
    // alias.
    public abstract float Sample(Vector3 worldPos, float filterWidth);

    // Unfiltered shorthand. Deliberately NOT the primary overload: the grid
    // paths that actually matter for aliasing all have a real spacing to pass,
    // and anything that reaches for this is by definition sampling one
    // isolated point where there is no spacing to speak of.
    public float Sample(Vector3 worldPos) => Sample(worldPos, 0f);

    // Convenience so MC can always march the zero level.
    public virtual float SampleMinusIso(Vector3 worldPos, float filterWidth) => Sample(worldPos, filterWidth) - isoLevel;
    public float SampleMinusIso(Vector3 worldPos) => SampleMinusIso(worldPos, 0f);

    // Surface hardness at a position: 0 = soft (footprints), 1 = hard rock.
    // Biome blends override this; plain fields default to soft.
    public virtual float SurfaceHardness(Vector3 worldPos) => 0f;

    protected virtual void OnValidate() => TerrainTuning.NotifyChanged();

    // Step used for gradient finite-difference (normals). Override if needed.
    public virtual float GradientStep(float cellSize) => 0.5f * cellSize;

    // Fill an axis-aligned grid of samples. dest is indexed
    // (z * countY + y) * countX + x. Subclasses can override this to batch work
    // (e.g. heightfields evaluate the 2D height once per column instead of once
    // per sample). Must produce values identical to calling Sample() per point.
    // `step` doubles as the band-limiting filter width -- it IS this grid's
    // sample spacing, which is exactly what Sample needs to know to drop
    // detail it cannot resolve.
    public virtual void SampleGrid(Vector3 origin, int countX, int countY, int countZ, float step, float[] dest)
    {
        int i = 0;
        for (int z = 0; z < countZ; z++)
            for (int y = 0; y < countY; y++)
                for (int x = 0; x < countX; x++)
                    dest[i++] = Sample(origin + new Vector3(x, y, z) * step, step);
    }

    // Per-vertex data baked at meshing time (e.g. biome weights the shader
    // blends palettes with). Must be a pure function of position so all LODs
    // agree at seams.
    public virtual bool HasVertexColors => false;
    public virtual Color GetVertexColor(Vector3 worldPos) => new Color(0, 0, 0, 1);

    // Conservative world-space bounds of the surface height, if the field can
    // provide them: the surface never goes below minH or above maxH. Lets the
    // mesher skip chunks that are entirely air or entirely solid without
    // sampling them (most chunks in a tall clipmap column are).
    public virtual bool TryGetHeightBounds(out float minH, out float maxH)
    {
        minH = maxH = 0f;
        return false;
    }

    // General empty-chunk test: can this world-space AABB be skipped because
    // it's provably entirely air or entirely solid (no possible surface
    // crossing inside it)? Default implementation reproduces the original
    // flat-world check (chunk's Y range vs TryGetHeightBounds) so every
    // existing height-based field keeps working with zero changes. Fields
    // whose "up" isn't world Y (PlanetField) override this directly instead
    // of TryGetHeightBounds, since a single flat Y range can't describe a
    // sphere's solid/air shell.
    public virtual bool TryGetEmptySkip(Vector3 boxMin, Vector3 boxMax)
    {
        if (!TryGetHeightBounds(out float minH, out float maxH)) return false;
        return boxMin.y > maxH || boxMax.y < minH;
    }

    // GPU acceleration hook (see the "GPU Compute-Shader Acceleration for
    // Terrain Density Fields" plan): a "leaf" field (Dune/Alien/Canyon)
    // overrides both to identify itself and bind its own tunables into a
    // LeafGpuParams slot. Composite fields (BiomeDensityField, PlanetField)
    // are never leaves themselves -- GpuType stays None for them, and their
    // GPU support instead comes from walking their children and checking
    // EVERY child resolves to a known leaf type (see BiomeDensityField's
    // TryBuildGpuLeaves), falling back to pure-CPU sampling for the whole
    // world if not -- never a per-chunk GPU/CPU mix (see the plan's
    // Watertightness section for why that specific mixing would be unsafe).
    public virtual GpuFieldType GpuType => GpuFieldType.None;
    public virtual LeafGpuParams ToGpuLeafParams() => default;

    // Adds weight * density into a vertical column of samples:
    // dest[destIndex + i*destStride] += weight * Sample(wx, yStart+i*yStep, wz).
    // The default just loops Sample; subclasses override to cache their per-
    // (x,z) work once per column (heightfields: the height; volume fields:
    // their 2D layout terms). This is how pure-3D SDF-style fields stay fast.
    public virtual void AddDensityColumn(float wx, float wz, float yStart, float yStep, int count,
                                         float weight, float[] dest, int destIndex, int destStride,
                                         float filterWidth)
    {
        for (int i = 0; i < count; i++)
        {
            dest[destIndex] += weight * Sample(new Vector3(wx, yStart + i * yStep, wz), filterWidth);
            destIndex += destStride;
        }
    }

    // Density gradient via central differences. Overrides must return exactly
    // the same values as this default (just computed cheaper), because vertex
    // normals feed the Transvoxel secondary-offset projection and any deviation
    // between chunks would open hairline cracks at seams.
    //
    // `filterWidth` must be the spacing of the grid whose surface this normal
    // belongs to, NOT eps -- the normal has to describe the band-limited
    // surface the mesher actually extracted, or shading disagrees with
    // geometry (and the secondary-offset projection nudges seam vertices
    // apart).
    public virtual Vector3 Gradient(Vector3 p, float eps, float filterWidth)
    {
        float dx = Sample(p + new Vector3(eps, 0, 0), filterWidth) - Sample(p - new Vector3(eps, 0, 0), filterWidth);
        float dy = Sample(p + new Vector3(0, eps, 0), filterWidth) - Sample(p - new Vector3(0, eps, 0), filterWidth);
        float dz = Sample(p + new Vector3(0, 0, eps), filterWidth) - Sample(p - new Vector3(0, 0, eps), filterWidth);
        return new Vector3(dx, dy, dz) / (2f * eps);
    }
}
