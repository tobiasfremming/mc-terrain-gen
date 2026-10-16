using UnityEngine;

// Ground queries for creatures. Field-based, never colliders.
public interface IGroundProbe
{
    // near: a point close to the ground; window: half-extent searched along up.
    bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal);
}

// Flat world at y = height, up = +Y.
public sealed class FlatGroundProbe : IGroundProbe
{
    readonly float height;
    public FlatGroundProbe(float height = 0f) { this.height = height; }

    public bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal)
    {
        point = new Vector3(near.x, height, near.z);
        normal = Vector3.up;
        return true;
    }
}

public sealed class FieldGroundProbe : IGroundProbe
{
    const int kBisect = 12;
    const float kFallbackUp = 8f, kFallbackDown = 16f;
    const float kStep = 0.5f; // TerrainQuery's coarse step, for sample accounting

    readonly DensityField field;
    public int SampleCount; // cumulative field samples, for perf stats

    public FieldGroundProbe(DensityField field) { this.field = field; }

    public bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal)
    {
        up = up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.up;
        if (field == null) { point = near; normal = up; return false; }

        Vector3 hi = near + up * window, lo = near - up * window;
        SampleCount += 2;
        if (field.Sample(hi) <= 0f && field.Sample(lo) > 0f)
        {
            float a = 0f, b = 2f * window; // along hi -> lo: a is air, b is solid
            for (int i = 0; i < kBisect; i++)
            {
                float m = 0.5f * (a + b);
                SampleCount++;
                if (field.Sample(hi - up * m) > 0f) b = m; else a = m;
            }
            point = hi - up * (0.5f * (a + b));
            Vector3 g = field.Gradient(point, 0.05f, 0f);
            SampleCount += 6; // central differences
            normal = g.sqrMagnitude > 1e-12f ? -g.normalized : up;
            return true;
        }

        SampleCount += Mathf.CeilToInt((kFallbackUp + kFallbackDown) / kStep) + 1;
        if (TerrainQuery.TryFindGround(field, near, up, kFallbackUp, kFallbackDown, out point, out normal))
            return true;
        point = near; normal = up;
        return false;
    }
}
