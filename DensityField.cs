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

    // Conservative world-space bounds of the surface height, if the field can
    // provide them: the surface never goes below minH or above maxH. Lets the
    // mesher skip chunks that are entirely air or entirely solid without
    // sampling them (most chunks in a tall clipmap column are).
    public virtual bool TryGetHeightBounds(out float minH, out float maxH)
    {
        minH = maxH = 0f;
        return false;
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
