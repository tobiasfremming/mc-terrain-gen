using UnityEngine;

// Base class for density fields of the form  density = HeightAt(x, z) - y
// (positive = solid below the surface). Implements the batched grid sampling
// (height evaluated once per column) and the cheap exact gradient, so concrete
// heightfields only provide HeightAt.
public abstract class HeightDensityField : DensityField
{
    // The (expensive) 2D terrain height at world (x, z).
    public abstract float HeightAt(float wx, float wz, float filterWidth);

    // ---- optional 3D features (overhangs, bridges, caves) ----
    // Density = HeightAt(x,z) - y + Extra3D(x,y,z). The extra term enables
    // non-heightfield geometry while keeping the fast per-column sampling:
    // implementations cache their (x,z) work per column in AddExtra3DColumn.
    public virtual bool Has3D => false;

    // World-y range outside which Extra3D is guaranteed zero (used to pick the
    // cheap 2D gradient away from 3D features).
    public virtual void Get3DBand(out float yMin, out float yMax)
    {
        yMin = float.MinValue; yMax = float.MaxValue;
    }

    // Point evaluation (Sample / Gradient paths).
    public virtual float Extra3DAt(float wx, float y, float wz, float filterWidth) => 0f;

    // Column evaluation: dest[destIndex + i*destStride] += weight * Extra3D at
    // y = yStart + i*yStep, for i in [0, count).
    public virtual void AddExtra3DColumn(float wx, float wz, float yStart, float yStep, int count,
                                         float weight, float[] dest, int destIndex, int destStride,
                                         float filterWidth) {}

    public override float Sample(Vector3 w, float fw)
    {
        float d = HeightAt(w.x, w.z, fw) - w.y;
        if (Has3D) d += Extra3DAt(w.x, w.y, w.z, fw);
        return d;
    }

    // Fast column path: the expensive HeightAt runs once per column.
    public override void AddDensityColumn(float wx, float wz, float yStart, float yStep, int count,
                                          float weight, float[] dest, int destIndex, int destStride,
                                          float fw)
    {
        float h = HeightAt(wx, wz, fw);
        int idx = destIndex;
        for (int i = 0; i < count; i++)
        {
            dest[idx] += weight * (h - (yStart + i * yStep));
            idx += destStride;
        }
        if (Has3D)
            AddExtra3DColumn(wx, wz, yStart, yStep, count, weight, dest, destIndex, destStride, fw);
    }

    // Height only depends on (x, z), so evaluate it once per column instead of
    // once per sample; 3D extras are then added per column with cached context.
    public override void SampleGrid(Vector3 origin, int countX, int countY, int countZ, float step, float[] dest)
    {
        float[] row = new float[countX];
        for (int z = 0; z < countZ; z++)
        {
            float wz = origin.z + z * step;
            for (int x = 0; x < countX; x++)
                row[x] = HeightAt(origin.x + x * step, wz, step);

            for (int y = 0; y < countY; y++)
            {
                float wy = origin.y + y * step;
                int baseIdx = (z * countY + y) * countX;
                for (int x = 0; x < countX; x++)
                    dest[baseIdx + x] = row[x] - wy;
            }
        }

        if (Has3D)
        {
            for (int z = 0; z < countZ; z++)
            {
                float wz = origin.z + z * step;
                for (int x = 0; x < countX; x++)
                    AddExtra3DColumn(origin.x + x * step, wz, origin.y, step, countY,
                                     1f, dest, z * countY * countX + x, countX, step);
            }
        }
    }

    // 2D fast gradient (d/dy of h - y is exactly -1) away from 3D features;
    // full central differences inside the 3D band. The switch is a pure
    // function of position and the two formulas agree exactly where Extra3D
    // vanishes within +-eps, so no seams.
    public override Vector3 Gradient(Vector3 p, float eps, float fw)
    {
        if (Has3D)
        {
            Get3DBand(out float y0, out float y1);
            if (p.y > y0 - eps && p.y < y1 + eps)
                return base.Gradient(p, eps, fw); // full 3D central differences
        }
        float dx = HeightAt(p.x + eps, p.z, fw) - HeightAt(p.x - eps, p.z, fw);
        float dz = HeightAt(p.x, p.z + eps, fw) - HeightAt(p.x, p.z - eps, fw);
        return new Vector3(dx / (2f * eps), -1f, dz / (2f * eps));
    }
}
