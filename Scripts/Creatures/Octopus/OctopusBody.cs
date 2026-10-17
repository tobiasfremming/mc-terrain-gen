using UnityEngine;

// The octopus body core (tree octopus design 2.3): a SoftLattice (head + mantle) and ArmCount
// TentacleChains, all one position-based system. Nothing animates the body: it hangs, swings and squashes
// because gravity and the chains' constraints act on it.
//
// Coupling: each arm root X[0] and its virtual back particle are points embedded in the web cluster,
// p = sum w_j x_j (least-norm barycentric weights over the cluster's particles, fixed at construction).
// Before each chain iteration the body writes p and the embedded inverse mass W = sum w_j^2 / m_j into the
// chain; the correction d the chain applies to p is handed back to the lattice as
// dx_j = (w_j / m_j / W) d, which moves p by exactly d (the standard PBD embedded-point rule). That is the
// root constraint impulse: a taut anchored arm pulls the body, a limp arm just hangs off it.
//
// Sub-step: integrate lattice + arms (damping relative to the whole animal's centre-of-mass velocity, so
// a free fall is a free fall); `iterations` x (arms, then shape matching); final embed + chain end step.
//
// Bones (BoneCount = ArmCount x Segments + 9; 8 x 24 + 9 = 201):
//   a x Segments + k   arm a, bone k (TentacleChain.WriteBones)
//   SoftBone(c)        c = 0 web, 1 head, 2 mantle base, 3 mantle mid, 4 mantle tip: affine
//                      [sizeScale x T_c | centroid_c] (det-1 shape-matching transform), bind = translate(-rest centroid)
//   SoftBone(5)        apex: [sizeScale x T_tip | apex particle], bind = translate(-rest apex)
//   EyeBone(e)         rigid: head transform of the eye centre, head rotation x rest eye rotation, sizeScale
//   SiphonBone         rigid like the eyes, with the siphon pulse in its scale
// No allocation after construction; no engine calls.
public sealed class OctopusBody
{
    const float kMaxDt = 0.1f;
    public const int SoftBoneCount = 6;

    public readonly CephalopodSpec Spec;
    public readonly float SizeScale, TimeScale;
    public readonly SoftLattice Lattice;
    public readonly TentacleChain[] Arms;
    public readonly int ArmCount, Segments;
    public float Time { get; private set; }

    readonly int[] web;                 // embedding particles (EmbedCluster)
    public static int EmbedCluster = OctopusShape.ClusterHead; // tuning: which cluster carries the arm roots
    readonly float[] rootW, backW;      // [arm * web.Length + j]
    readonly float[] rootWm, backWm;    // per arm: embedded inverse mass W = sum w^2 / m
    readonly Quaternion[] armRestRot;
    readonly Vector3[] eyeRest; readonly Quaternion[] eyeRot;
    readonly Vector3 siphonRest; readonly Quaternion siphonRot;
    readonly int subSteps, iterations;
    Vector3 gravity = new Vector3(0f, -9.81f, 0f);
    float extraDamping;

    // Phase A grip policy: a pin whose arm would have to stretch past its limit to hold the body (an
    // infeasible pull) lets go, instead of tearing the lattice. The arm agent (phase B) takes this over.
    public bool ReleaseOverloadedPins = true;
    public const float OverloadRelease = 1.02f;

    public OctopusBody(CephalopodSpec spec, Vector3 position, Quaternion rotation, float sizeScale = 1f)
    {
        Spec = spec;
        SizeScale = Mathf.Max(1e-3f, sizeScale);
        TimeScale = Mathf.Sqrt(SizeScale);
        ArmCount = spec.ArmCount; Segments = spec.arm.Segments;
        subSteps = spec.SubSteps; iterations = spec.Iterations;
        Lattice = new SoftLattice(spec, position, rotation, SizeScale);
        Lattice.SetIterations(iterations);

        web = OctopusShape.Clusters[EmbedCluster];
        int nw = web.Length;
        rootW = new float[ArmCount * nw]; backW = new float[ArmCount * nw];
        rootWm = new float[ArmCount]; backWm = new float[ArmCount];
        armRestRot = new Quaternion[ArmCount];
        Arms = new TentacleChain[ArmCount];
        for (int a = 0; a < ArmCount; a++)
        {
            Vector3 r = OctopusShape.ArmRoot(spec, a) * SizeScale, d = OctopusShape.ArmDir(spec, a);
            armRestRot[a] = OctopusShape.ArmRot(spec, a);
            Embed(r, rootW, a * nw);
            Embed(r - d * (spec.arm.Spacing * SizeScale), backW, a * nw);
            for (int j = 0; j < nw; j++)
            {
                float im = Lattice.InvMass[web[j]];
                rootWm[a] += rootW[a * nw + j] * rootW[a * nw + j] * im;
                backWm[a] += backW[a * nw + j] * backW[a * nw + j] * im;
            }
            Arms[a] = new TentacleChain(spec.arm, position + rotation * r, rotation * armRestRot[a], SizeScale);
            Arms[a].TwistFreeFrom = spec.WebDepth * spec.arm.Length / spec.arm.Spacing;
        }

        eyeRest = new Vector3[2]; eyeRot = new Quaternion[2];
        for (int e = 0; e < 2; e++) OctopusShape.Eye(spec, e, out eyeRest[e], out eyeRot[e]);
        OctopusShape.Siphon(spec, out siphonRest, out siphonRot);

        Lattice.FitAll();
        for (int a = 0; a < ArmCount; a++) { SyncRoot(a); Arms[a].UpdateFrames(0f); }
    }

    // Least-norm weights w over the web cluster with sum w = 1 and sum w q = p (rest, body space x scale).
    void Embed(Vector3 p, float[] w, int o)
    {
        int n = web.Length;
        Vector3 qc = Vector3.zero;
        for (int j = 0; j < n; j++) qc += Lattice.Rest[web[j]];
        qc /= n;
        var dd = new M3();
        for (int j = 0; j < n; j++) { Vector3 d = Lattice.Rest[web[j]] - qc; dd.AddOuter(d, d, 1f); }
        Vector3 y = dd.Inverse().Mul(p - qc);
        for (int j = 0; j < n; j++) w[o + j] = 1f / n + OctoMath.Dot(Lattice.Rest[web[j]] - qc, y);
    }

    // ------------------------------------------------------------------ control

    public Vector3 Gravity
    {
        get => gravity;
        set { gravity = value; Lattice.Gravity = value; for (int a = 0; a < ArmCount; a++) Arms[a].Gravity = value; }
    }

    // Absolute velocity damping on everything, 1/s (settling in tests and the lab; 0 in normal play).
    public float ExtraDamping
    {
        get => extraDamping;
        set { extraDamping = value; Lattice.ExtraDamping = value; for (int a = 0; a < ArmCount; a++) Arms[a].ExtraDamping = value; }
    }

    public void SetAllLimp(bool limp) { for (int a = 0; a < ArmCount; a++) Arms[a].SetLimp(limp); }

    // Body orientation: the head cluster's rotation (rest body space -> world).
    public Quaternion Orientation => Lattice.ClusterRotation[OctopusShape.ClusterHead];
    public Vector3 HeadCentre => Lattice.ClusterCentre[OctopusShape.ClusterHead];

    public float TotalMass
    {
        get
        {
            float m = Lattice.TotalMass;
            for (int a = 0; a < ArmCount; a++) for (int k = 1; k < Segments; k++) m += Arms[a].Mass[k];
            return m;
        }
    }

    // Centre of mass of the whole animal (lattice + arm particles past the roots).
    public Vector3 CentreOfMass()
    {
        Vector3 c = Vector3.zero; float m = 0f;
        for (int i = 0; i < SoftLattice.Count; i++) { c += Lattice.X[i] * Lattice.Mass[i]; m += Lattice.Mass[i]; }
        for (int a = 0; a < ArmCount; a++)
        {
            TentacleChain ch = Arms[a];
            for (int k = 1; k < Segments; k++) { c += ch.X[k] * ch.Mass[k]; m += ch.Mass[k]; }
        }
        return c / m;
    }

    // Mass-weighted velocity of the whole animal over the last step (scalar maths: Vector3 operators are calls on Mono).
    Vector3 Velocity(float h)
    {
        float cx = 0f, cy = 0f, cz = 0f, m = 0f;
        Vector3[] lx = Lattice.X, lp = Lattice.Prev;
        float[] lm = Lattice.Mass;
        for (int i = 0; i < SoftLattice.Count; i++)
        {
            float w = lm[i];
            cx += (lx[i].x - lp[i].x) * w; cy += (lx[i].y - lp[i].y) * w; cz += (lx[i].z - lp[i].z) * w; m += w;
        }
        for (int a = 0; a < ArmCount; a++)
        {
            TentacleChain ch = Arms[a];
            Vector3[] x = ch.X, p = ch.Prev;
            float[] am = ch.Mass;
            for (int k = 1; k < Segments; k++)
            {
                float w = am[k];
                cx += (x[k].x - p[k].x) * w; cy += (x[k].y - p[k].y) * w; cz += (x[k].z - p[k].z) * w; m += w;
            }
        }
        float inv = 1f / (m * h);
        return OctoMath.V(cx * inv, cy * inv, cz * inv);
    }

    // ------------------------------------------------------------------ tick

    public void Tick(float dt)
    {
        if (!(dt > 0f)) return;
        dt = Mathf.Min(dt, kMaxDt);
        float h = dt / subSteps;
        for (int s = 0; s < subSteps; s++) SubStep(h);
        Lattice.FitAll();
        for (int a = 0; a < ArmCount; a++) Arms[a].UpdateFrames(dt);
    }

    void SubStep(float h)
    {
        Time += h;
        Vector3 refVel = Velocity(h);
        Lattice.Integrate(h, refVel);
        Vector3 axisOrigin = HeadCentre, axisDir = OctoMath.Rotate(Orientation, 0f, 1f, 0f);
        for (int a = 0; a < ArmCount; a++)
        {
            TentacleChain ch = Arms[a];
            ch.AxisOrigin = axisOrigin; ch.AxisDir = axisDir;
            ch.Integrate(h, refVel);
        }
        for (int it = 0; it < iterations; it++)
        {
            for (int a = 0; a < ArmCount; a++)
            {
                TentacleChain ch = Arms[a];
                SyncRoot(a);
                Vector3 r0 = ch.X[0], b0 = ch.Back;
                ch.SolveIteration(h);
                Vector3 r1 = ch.X[0], b1 = ch.Back;
                Distribute(a, r1.x - r0.x, r1.y - r0.y, r1.z - r0.z, rootW, rootWm[a]);
                Distribute(a, b1.x - b0.x, b1.y - b0.y, b1.z - b0.z, backW, backWm[a]);
            }
            Lattice.ShapeMatch();
        }
        for (int a = 0; a < ArmCount; a++)
        {
            TentacleChain ch = Arms[a];
            if (ReleaseOverloadedPins && ch.Overload > OverloadRelease && ch.OverloadedPin > 0) ch.Unpin(ch.OverloadedPin);
            SyncRoot(a);
            ch.EndSubStep(h);
        }
    }

    // Writes the embedded root/back points, their inverse masses and the root frame into arm a.
    void SyncRoot(int a)
    {
        TentacleChain ch = Arms[a];
        int nw = web.Length, o = a * nw;
        float rx = 0f, ry = 0f, rz = 0f, bx = 0f, by = 0f, bz = 0f;
        Vector3[] lx = Lattice.X;
        for (int j = 0; j < nw; j++)
        {
            Vector3 x = lx[web[j]];
            float u = rootW[o + j], v = backW[o + j];
            rx += x.x * u; ry += x.y * u; rz += x.z * u;
            bx += x.x * v; by += x.y * v; bz += x.z * v;
        }
        ch.X[0].x = rx; ch.X[0].y = ry; ch.X[0].z = rz;
        ch.Prev[0] = ch.X[0];
        ch.Back.x = bx; ch.Back.y = by; ch.Back.z = bz;
        ch.InvMass[0] = rootWm[a]; ch.BackInvMass = backWm[a];
        ch.RootFrame = OctoMath.Mul(Lattice.ClusterRotation[OctopusShape.ClusterWeb], armRestRot[a]);
    }

    // Embedded-point rule: dx_j = (w_j / m_j / W) d moves the embedded point by exactly d.
    void Distribute(int a, float dx, float dy, float dz, float[] w, float W)
    {
        if ((dx == 0f && dy == 0f && dz == 0f) || !(W > 0f)) return;
        int nw = web.Length, o = a * nw;
        float iw = 1f / W;
        Vector3[] lx = Lattice.X;
        float[] im = Lattice.InvMass;
        for (int j = 0; j < nw; j++)
        {
            int p = web[j];
            float f = w[o + j] * im[p] * iw;
            lx[p].x += dx * f; lx[p].y += dy * f; lx[p].z += dz * f;
        }
    }

    // ------------------------------------------------------------------ bones

    public static int BoneCountFor(in CephalopodSpec sp) => sp.ArmCount * sp.arm.Segments + SoftBoneCount + 3;
    public int BoneCount => ArmCount * Segments + SoftBoneCount + 3;
    public int ArmBone(int arm, int k) => arm * Segments + k;
    public int SoftBone(int c) => ArmCount * Segments + c;
    public int EyeBone(int e) => ArmCount * Segments + SoftBoneCount + e;
    public int SiphonBone => ArmCount * Segments + SoftBoneCount + 2;

    // Rest-space pivot of soft bone c (its bind pose is translate(-pivot / sizeScale) at scale 1).
    public Vector3 SoftBonePivot(int c) =>
        c < SoftLattice.ClusterCount ? Lattice.ClusterRestCentre[c] : Lattice.Rest[OctopusShape.Apex];

    public void WriteBoneWorld(Matrix4x4[] dst, int offset)
    {
        for (int a = 0; a < ArmCount; a++) Arms[a].WriteBones(dst, offset + a * Segments);
        // the cluster goals act on rest offsets in sizeScale units: the mesh (scale 1) needs x sizeScale
        float s = SizeScale;
        for (int c = 0; c < SoftLattice.ClusterCount; c++)
            dst[offset + SoftBone(c)] = OctoMath.Affine(Lattice.ClusterGoal(c), s, Lattice.ClusterCentre[c]);
        dst[offset + SoftBone(5)] = OctoMath.Affine(Lattice.ClusterGoal(OctopusShape.ClusterTip), s, Lattice.X[OctopusShape.Apex]);

        int hc = OctopusShape.ClusterHead;
        M3 ht = Lattice.ClusterGoal(hc);
        Vector3 hcen = Lattice.ClusterCentre[hc], hrest = Lattice.ClusterRestCentre[hc];
        Quaternion hr = Lattice.ClusterRotation[hc];
        for (int e = 0; e < 2; e++)
            dst[offset + EyeBone(e)] = OctoMath.TRS(HeadPoint(ht, hcen, hrest, eyeRest[e] * s), OctoMath.Mul(hr, eyeRot[e]), s, s, s);
        // siphon pulse: exhale with the breath (the mantle contracts as the funnel widens)
        float pulse = 1f - 2.5f * Lattice.Breath;
        dst[offset + SiphonBone] = OctoMath.TRS(HeadPoint(ht, hcen, hrest, siphonRest * s), OctoMath.Mul(hr, siphonRot), s * pulse, s * pulse, s);
    }

    static Vector3 HeadPoint(in M3 t, Vector3 centre, Vector3 restCentre, Vector3 rest)
    {
        Vector3 d = t.Mul(OctoMath.V(rest.x - restCentre.x, rest.y - restCentre.y, rest.z - restCentre.z));
        return OctoMath.V(centre.x + d.x, centre.y + d.y, centre.z + d.z);
    }

    public void Encapsulate(ref Vector3 lo, ref Vector3 hi)
    {
        for (int i = 0; i < SoftLattice.Count; i++) { lo = Vector3.Min(lo, Lattice.X[i]); hi = Vector3.Max(hi, Lattice.X[i]); }
        for (int a = 0; a < ArmCount; a++) Arms[a].Encapsulate(ref lo, ref hi);
    }
}
