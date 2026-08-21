using UnityEngine;

public abstract class DensityField : ScriptableObject
{
    [Tooltip("The isovalue of the surface you want to extract. Keep 0 unless you need a shift.")]
    public float isoLevel = 0f;

    // Return *signed* density. Convention used by the meshing code in this
    // project: positive = solid, negative = air (e.g. heightfields return h - y).
    public abstract float Sample(Vector3 worldPos);

    // Convenience so MC can always march the zero level.
    public virtual float SampleMinusIso(Vector3 worldPos) => Sample(worldPos) - isoLevel;

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
    public virtual void SampleGrid(Vector3 origin, int countX, int countY, int countZ, float step, float[] dest)
    {
        int i = 0;
        for (int z = 0; z < countZ; z++)
            for (int y = 0; y < countY; y++)
                for (int x = 0; x < countX; x++)
                    dest[i++] = Sample(origin + new Vector3(x, y, z) * step);
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

    // Adds weight * density into a vertical column of samples:
    // dest[destIndex + i*destStride] += weight * Sample(wx, yStart+i*yStep, wz).
    // The default just loops Sample; subclasses override to cache their per-
    // (x,z) work once per column (heightfields: the height; volume fields:
    // their 2D layout terms). This is how pure-3D SDF-style fields stay fast.
    public virtual void AddDensityColumn(float wx, float wz, float yStart, float yStep, int count,
                                         float weight, float[] dest, int destIndex, int destStride)
    {
        for (int i = 0; i < count; i++)
        {
            dest[destIndex] += weight * Sample(new Vector3(wx, yStart + i * yStep, wz));
            destIndex += destStride;
        }
    }

    // Density gradient via central differences. Overrides must return exactly
    // the same values as this default (just computed cheaper), because vertex
    // normals feed the Transvoxel secondary-offset projection and any deviation
    // between chunks would open hairline cracks at seams.
    public virtual Vector3 Gradient(Vector3 p, float eps)
    {
        float dx = Sample(p + new Vector3(eps, 0, 0)) - Sample(p - new Vector3(eps, 0, 0));
        float dy = Sample(p + new Vector3(0, eps, 0)) - Sample(p - new Vector3(0, eps, 0));
        float dz = Sample(p + new Vector3(0, 0, eps)) - Sample(p - new Vector3(0, 0, eps));
        return new Vector3(dx, dy, dz) / (2f * eps);
    }
}
