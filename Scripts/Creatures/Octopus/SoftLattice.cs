using System;
using UnityEngine;

// The octopus's squishy head and mantle (design 2.3): 14 particles under gravity, held in shape by linear
// shape matching (Mueller et al. 2005) over 5 overlapping clusters (OctopusShape). Per cluster and solver
// iteration:
//   Apq = sum m (x - c)(q - qc)^T,  R = rotation of Apq (warm-started polar extraction),
//   A = Apq Aqq^-1 normalised to det 1,  T = R + beta (A - R) normalised to det 1 again,
//   T x= breathing (radial 1 + b, axial 1 / (1 + b)^2 about the rest mantle axis: also det 1),
//   goal g = T (q - qc) + c,  x += k (g - x).
// Clusters are applied one after another (each conserves linear momentum exactly). k runs from the head
// (stiff) to the mantle tip (soft), so the mantle trails, overshoots and jiggles; det 1 keeps the volume.
// ClusterGoal/ClusterCentre are the soft bones' affine transforms (OctopusBody.WriteBoneWorld).
// No allocation after construction; no engine calls.
public sealed class SoftLattice
{
    public const int Count = OctopusShape.LatticeCount, ClusterCount = OctopusShape.ClusterCount;
    const int kPolarIterations = 2;
    const float kMaxSpeed = 30f;

    public readonly Vector3[] X, Prev;
    public readonly Vector3[] Rest;          // body space x sizeScale
    public readonly float[] Mass, InvMass;
    public readonly Quaternion[] ClusterRotation; // R per cluster (rest body space -> world)
    public readonly Vector3[] ClusterCentre;      // current centroid
    public readonly Vector3[] ClusterRestCentre;  // body space x sizeScale
    public readonly float[] ClusterStiffness;     // per sub-step
    public float Beta;
    public float BreathAmp, BreathHz;
    public float Damping;                    // relative to the reference velocity, 1/s
    public float ExtraDamping;               // absolute, 1/s
    public Vector3 Gravity = new Vector3(0f, -9.81f, 0f);
    public float Time { get; private set; }
    public float Breath { get; private set; } // current b

    readonly M3[] clusterGoal;
    readonly M3[] aqqInv;
    readonly float[] clusterMass;
    readonly int[] cStart, cCount, cIdx;
    readonly bool[] breathes;
    readonly Vector3 mantleAxis;             // rest, unit
    readonly float sizeScale;
    int iterations = 1;
    float kIterScale = 1f;
    float kFrame = 1f / 180f;                // last sub-step length
    const float kStiffnessRate = 180f;       // cluster stiffness is per 1/180 s (one default sub-step at 60 Hz)
    M3 breath = M3.Identity;
    readonly int[] hull;
    readonly Vector3[] grad;
    float restVolume;            // signed, of the rest hull

    public SoftLattice(in CephalopodSpec sp, Vector3 position, Quaternion rotation, float sizeScale)
    {
        this.sizeScale = sizeScale;
        Vector3[] rest = OctopusShape.LatticeRest(sp);
        float[] mw = OctopusShape.LatticeMassWeights();
        X = new Vector3[Count]; Prev = new Vector3[Count]; Rest = new Vector3[Count];
        Mass = new float[Count]; InvMass = new float[Count];
        float msum = 0f;
        for (int i = 0; i < Count; i++) msum += mw[i];
        float bodyMass = sp.BodyMass * sizeScale * sizeScale * sizeScale;
        for (int i = 0; i < Count; i++)
        {
            Rest[i] = rest[i] * sizeScale;
            X[i] = position + rotation * Rest[i]; Prev[i] = X[i];
            Mass[i] = bodyMass * mw[i] / msum; InvMass[i] = 1f / Mass[i];
        }

        int total = 0;
        for (int c = 0; c < ClusterCount; c++) total += OctopusShape.Clusters[c].Length;
        cStart = new int[ClusterCount]; cCount = new int[ClusterCount]; cIdx = new int[total];
        ClusterRotation = new Quaternion[ClusterCount]; ClusterCentre = new Vector3[ClusterCount];
        ClusterRestCentre = new Vector3[ClusterCount]; ClusterStiffness = new float[ClusterCount];
        clusterGoal = new M3[ClusterCount]; aqqInv = new M3[ClusterCount]; clusterMass = new float[ClusterCount];
        breathes = new bool[ClusterCount];
        int o = 0;
        for (int c = 0; c < ClusterCount; c++)
        {
            int[] mem = OctopusShape.Clusters[c];
            cStart[c] = o; cCount[c] = mem.Length;
            Vector3 qc = Vector3.zero; float m = 0f;
            for (int i = 0; i < mem.Length; i++) { cIdx[o + i] = mem[i]; qc += Rest[mem[i]] * Mass[mem[i]]; m += Mass[mem[i]]; }
            qc /= m;
            var aqq = new M3();
            for (int i = 0; i < mem.Length; i++) { Vector3 q = Rest[mem[i]] - qc; aqq.AddOuter(q, q, Mass[mem[i]]); }
            aqqInv[c] = aqq.Inverse();
            ClusterRestCentre[c] = qc; clusterMass[c] = m;
            ClusterRotation[c] = rotation;
            ClusterCentre[c] = position + rotation * qc;
            clusterGoal[c] = M3.FromQuaternion(rotation);
            ClusterStiffness[c] = Mathf.Lerp(sp.HeadStiffness, sp.TipStiffness, OctopusShape.ClusterStiffnessPos[c]);
            breathes[c] = OctopusShape.ClusterBreathes[c];
            o += mem.Length;
        }
        Beta = sp.Beta; BreathAmp = sp.BreathAmp; BreathHz = sp.BreathHz; Damping = sp.BodyDamping;
        float tilt = sp.MantleTiltDeg * Mathf.Deg2Rad;
        mantleAxis = new Vector3(0f, Mathf.Cos(tilt), -Mathf.Sin(tilt));
        hull = BuildHull(); grad = new Vector3[Count];
        restVolume = SignedVolume();
    }

    internal M3 ClusterGoal(int c) => clusterGoal[c];

    public Vector3 CentreOfMass()
    {
        Vector3 c = Vector3.zero; float m = 0f;
        for (int i = 0; i < Count; i++) { c += X[i] * Mass[i]; m += Mass[i]; }
        return c / m;
    }

    public float TotalMass { get { float m = 0f; for (int i = 0; i < Count; i++) m += Mass[i]; return m; } }

    // Sets the per-iteration stiffness so `iterations` passes give the cluster stiffness per sub-step.
    public void SetIterations(int n)
    {
        iterations = Mathf.Max(1, n);
        kIterScale = kFrame * kStiffnessRate / iterations;
    }

    // Scalar float maths in the hot paths: Vector3 operators are calls on Mono.
    public void Integrate(float h, Vector3 refVel)
    {
        Time += h;
        if (h != kFrame) { kFrame = h; kIterScale = h * kStiffnessRate / iterations; }
        Breath = BreathAmp * MathF.Sin(2f * Mathf.PI * BreathHz / MathF.Sqrt(sizeScale) * Time);
        UpdateBreath();
        float damp = MathF.Exp(-Damping * h), abs = MathF.Exp(-ExtraDamping * h);
        float inv = 1f / h, vmax = kMaxSpeed * MathF.Sqrt(sizeScale), vmax2 = vmax * vmax;
        float gx = Gravity.x * h, gy = Gravity.y * h, gz = Gravity.z * h;
        float rx = refVel.x, ry = refVel.y, rz = refVel.z;
        for (int i = 0; i < Count; i++)
        {
            Vector3 xi = X[i], pi = Prev[i];
            float vx = (((xi.x - pi.x) * inv - rx) * damp + rx) * abs + gx;
            float vy = (((xi.y - pi.y) * inv - ry) * damp + ry) * abs + gy;
            float vz = (((xi.z - pi.z) * inv - rz) * damp + rz) * abs + gz;
            float s2 = vx * vx + vy * vy + vz * vz;
            if (s2 > vmax2) { float f = vmax / MathF.Sqrt(s2); vx *= f; vy *= f; vz *= f; }
            Prev[i] = xi;
            X[i].x = xi.x + vx * h; X[i].y = xi.y + vy * h; X[i].z = xi.z + vz * h;
        }
    }

    // One shape-matching pass over all clusters (call once per solver iteration).
    public void ShapeMatch()
    {
        for (int c = 0; c < ClusterCount; c++)
        {
            float k = 1f - MathF.Pow(1f - ClusterStiffness[c], kIterScale);
            Fit(c);
            M3 t = clusterGoal[c];
            Vector3 cc = ClusterCentre[c], qc = ClusterRestCentre[c];
            int o = cStart[c];
            for (int i = 0; i < cCount[c]; i++)
            {
                int p = cIdx[o + i];
                Vector3 r = Rest[p], x = X[p];
                float qx = r.x - qc.x, qy = r.y - qc.y, qz = r.z - qc.z;
                float gx = t.m00 * qx + t.m01 * qy + t.m02 * qz + cc.x;
                float gy = t.m10 * qx + t.m11 * qy + t.m12 * qz + cc.y;
                float gz = t.m20 * qx + t.m21 * qy + t.m22 * qz + cc.z;
                X[p].x = x.x + (gx - x.x) * k; X[p].y = x.y + (gy - x.y) * k; X[p].z = x.z + (gz - x.z) * k;
            }
        }
        ProjectVolume();
    }

    // Fits cluster c to the current positions: centre, rotation, det-1 goal transform (no position change).
    public void Fit(int c)
    {
        int o = cStart[c], n = cCount[c];
        float cx = 0f, cy = 0f, cz = 0f;
        for (int i = 0; i < n; i++) { int p = cIdx[o + i]; float m = Mass[p]; Vector3 x = X[p]; cx += x.x * m; cy += x.y * m; cz += x.z * m; }
        float im = 1f / clusterMass[c];
        cx *= im; cy *= im; cz *= im;
        Vector3 qc = ClusterRestCentre[c];
        var apq = new M3();
        for (int i = 0; i < n; i++)
        {
            int p = cIdx[o + i];
            float m = Mass[p];
            Vector3 x = X[p], r = Rest[p];
            float ax = (x.x - cx) * m, ay = (x.y - cy) * m, az = (x.z - cz) * m;
            float bx = r.x - qc.x, by = r.y - qc.y, bz = r.z - qc.z;
            apq.m00 += ax * bx; apq.m01 += ax * by; apq.m02 += ax * bz;
            apq.m10 += ay * bx; apq.m11 += ay * by; apq.m12 += ay * bz;
            apq.m20 += az * bx; apq.m21 += az * by; apq.m22 += az * bz;
        }
        Quaternion q = apq.ExtractRotation(ClusterRotation[c], kPolarIterations);
        ClusterRotation[c] = q;
        M3 r3 = M3.FromQuaternion(q);
        M3 t = r3;
        if (Beta > 0f)
        {
            M3 a = apq * aqqInv[c];
            float det = a.Det;
            if (det > 1e-6f)
            {
                a = a * (1f / MathF.Pow(det, 1f / 3f));
                t = M3.Lerp(r3, a, Beta);
                float d2 = t.Det;
                t = d2 > 1e-6f ? t * (1f / MathF.Pow(d2, 1f / 3f)) : r3;
            }
        }
        if (breathes[c] && Breath != 0f) t = t * breath;
        clusterGoal[c] = t;
        ClusterCentre[c].x = cx; ClusterCentre[c].y = cy; ClusterCentre[c].z = cz;
    }

    // Breathing deformation for this sub-step: radial (1 + b), axial (1 + b)^-2 about the rest mantle axis (det 1).
    void UpdateBreath()
    {
        float rad = 1f + Breath, ax = 1f / (rad * rad);
        M3 b = M3.Scale(rad);
        M3 mm = M3.Outer(mantleAxis, mantleAxis) * (ax - rad);
        b.m00 += mm.m00; b.m01 += mm.m01; b.m02 += mm.m02;
        b.m10 += mm.m10; b.m11 += mm.m11; b.m12 += mm.m12;
        b.m20 += mm.m20; b.m21 += mm.m21; b.m22 += mm.m22;
        breath = b;
    }

    public void FitAll() { for (int c = 0; c < ClusterCount; c++) Fit(c); }

    // The closed polyhedron through rings 0..2 and the apex (the head-front particle is left out): bottom
    // cap over ring 0, bands ring 0 -> 1 -> 2 (ring 0 sits at 45 + 90k, ring 1 at 90k, so ring1[k+1] lies
    // between ring0[k] and ring0[k+1]), fan to the apex. Consistently wound.
    static int[] BuildHull()
    {
        var t = new System.Collections.Generic.List<int> { 0, 2, 1, 0, 3, 2 };
        for (int k = 0; k < 4; k++)
        {
            int k1 = (k + 1) & 3;
            t.AddRange(new[] { k, k1, 4 + k1, 4 + k, k, 4 + k1, 4 + k, 4 + k1, 8 + k1, 4 + k, 8 + k1, 8 + k, 8 + k, 8 + k1, OctopusShape.Apex });
        }
        return t.ToArray();
    }

    float SignedVolume()
    {
        float v = 0f;
        for (int i = 0; i < hull.Length; i += 3)
        {
            Vector3 a = X[hull[i]], b = X[hull[i + 1]], c = X[hull[i + 2]];
            v += a.x * (b.y * c.z - b.z * c.y) + a.y * (b.z * c.x - b.x * c.z) + a.z * (b.x * c.y - b.y * c.x);
        }
        return v / 6f;
    }

    // Volume of the lattice hull (m^3).
    public float Volume() => MathF.Abs(SignedVolume());
    public float RestVolume => MathF.Abs(restVolume);

    // Incompressibility: one PBD volume projection, C = V - V0, gradient dV/dx_i = sum over the hull
    // triangles at i of (cross of the other two corners) / 6. Shape matching makes the body soft; this
    // keeps it a water balloon instead of a sponge.
    void ProjectVolume()
    {
        for (int i = 0; i < Count; i++) grad[i] = default;
        float v = 0f;
        for (int i = 0; i < hull.Length; i += 3)
        {
            int ia = hull[i], ib = hull[i + 1], ic = hull[i + 2];
            Vector3 a = X[ia], b = X[ib], c = X[ic];
            float bcx = b.y * c.z - b.z * c.y, bcy = b.z * c.x - b.x * c.z, bcz = b.x * c.y - b.y * c.x;
            float cax = c.y * a.z - c.z * a.y, cay = c.z * a.x - c.x * a.z, caz = c.x * a.y - c.y * a.x;
            float abx = a.y * b.z - a.z * b.y, aby = a.z * b.x - a.x * b.z, abz = a.x * b.y - a.y * b.x;
            v += a.x * bcx + a.y * bcy + a.z * bcz;
            grad[ia].x += bcx; grad[ia].y += bcy; grad[ia].z += bcz;
            grad[ib].x += cax; grad[ib].y += cay; grad[ib].z += caz;
            grad[ic].x += abx; grad[ic].y += aby; grad[ic].z += abz;
        }
        float C = (v - 6f * restVolume) / 6f;
        float den = 0f;
        for (int i = 0; i < Count; i++) { Vector3 g = grad[i]; den += InvMass[i] * (g.x * g.x + g.y * g.y + g.z * g.z) / 36f; }
        if (!(den > 1e-20f)) return;
        float lambda = -C / den / 6f;
        for (int i = 0; i < Count; i++)
        {
            float f = lambda * InvMass[i];
            Vector3 g = grad[i];
            X[i].x += g.x * f; X[i].y += g.y * f; X[i].z += g.z * f;
        }
    }
}
