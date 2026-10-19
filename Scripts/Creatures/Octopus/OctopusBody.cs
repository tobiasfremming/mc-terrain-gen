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
    // Mouth tentacles (spec.mouthCount; none on the octopus): short chains rooted in the web around the beak that
    // only feed (MouthFeeder). One-way coupled: their roots ride the web cluster, they put nothing back on the body
    // (a few grams against the body's weight). Bones: after the 6 soft bones, before the eyes.
    public readonly TentacleChain[] Mouth;
    public readonly int MouthCount, MouthSegments;
    readonly float[] mouthW;
    readonly Quaternion[] mouthRestRot;

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
    public int OverloadReleases;   // cumulative: grips the body tore loose

    // The force each arm exerted on the body over the last tick (N, averaged): the sum of its root and back
    // constraint corrections handed to the lattice, as impulses (correction x embedded mass / h) over dt. A
    // pure readout (tests, the lab's role box): how hard the arms pull and whether they pull together.
    public readonly Vector3[] ArmForce;
    readonly Vector3[] armImpulse;

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
        ArmForce = new Vector3[ArmCount]; armImpulse = new Vector3[ArmCount];
        ArmAim = new Quaternion[ArmCount];
        for (int a = 0; a < ArmCount; a++) ArmAim[a] = Quaternion.identity;
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
            Arms[a].Iterations = iterations;
        }

        MouthCount = spec.MouthCount; MouthSegments = MouthCount > 0 ? spec.mouth.Segments : 0;
        Mouth = new TentacleChain[MouthCount];
        mouthW = new float[MouthCount * nw]; mouthRestRot = new Quaternion[MouthCount];
        for (int m = 0; m < MouthCount; m++)
        {
            Vector3 r = OctopusShape.MouthRoot(spec, m) * SizeScale;
            mouthRestRot[m] = OctopusShape.MouthRot(spec, m);
            Embed(r, mouthW, m * nw);
            Mouth[m] = new TentacleChain(spec.mouth, position + rotation * r, rotation * mouthRestRot[m], SizeScale) { Iterations = iterations };
        }

        eyeRest = new Vector3[2]; eyeRot = new Quaternion[2];
        for (int e = 0; e < 2; e++) OctopusShape.Eye(spec, e, out eyeRest[e], out eyeRot[e]);
        OctopusShape.Siphon(spec, out siphonRest, out siphonRot);

        Lattice.FitAll();
        for (int a = 0; a < ArmCount; a++) { SyncRoot(a); Arms[a].UpdateFrames(0f); }
        for (int m = 0; m < MouthCount; m++) { SyncMouth(m); Mouth[m].UpdateFrames(0f); }
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
        set { gravity = value; Lattice.Gravity = value; for (int a = 0; a < ArmCount; a++) Arms[a].Gravity = value; for (int m = 0; m < MouthCount; m++) Mouth[m].Gravity = value; }
    }

    // Absolute velocity damping on everything, 1/s (settling in tests and the lab; 0 in normal play).
    public float ExtraDamping
    {
        get => extraDamping;
        set { extraDamping = value; Lattice.ExtraDamping = value; for (int a = 0; a < ArmCount; a++) Arms[a].ExtraDamping = value; for (int m = 0; m < MouthCount; m++) Mouth[m].ExtraDamping = value; }
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
        if (Planned) { TickPlanned(dt); return; }
        if (Asleep)
        {
            if (!SleepStillValid()) Wake();
            else
            {
                // asleep: nothing moves, the soft bones still breathe (5 cluster fits, a few microseconds)
                Time += dt;
                Lattice.AdvanceBreath(dt);
                Lattice.FitAll();
                return;
            }
        }
        float h = dt / subSteps;
        for (int a = 0; a < ArmCount; a++) armImpulse[a] = Vector3.zero;
        for (int s = 0; s < subSteps; s++) SubStep(h);
        float idt = 1f / dt;
        for (int a = 0; a < ArmCount; a++) ArmForce[a] = OctoMath.V(armImpulse[a].x * idt, armImpulse[a].y * idt, armImpulse[a].z * idt);
        Lattice.FitAll();
        for (int a = 0; a < ArmCount; a++) Arms[a].UpdateFrames(dt);
        for (int m = 0; m < MouthCount; m++) Mouth[m].UpdateFrames(dt);
        UpdateSleep(dt);
    }

    // ------------------------------------------------------------------ planned body (the swinger)
    // Planned mode (CephalopodSpec.locomotion 1, planned-body rework 2026-10-19): a reduced-order model outside the
    // body (PlannedSwing, driven by SwingSequencer) decides the body's rigid pose - origin = the arm crown (body-space
    // origin), rotation = body space -> world - and hands it over every tick (SetPlannedPose). Then:
    //   lattice   each particle is a damped spring on its rigid goal (pose x rest): the crown ring is exactly on it (it
    //             carries the arms), the head front, neck, mantle and apex follow ever more softly (SecondaryOmega,
    //             SecondaryZeta) - they lag a swing's acceleration (stretch at the bottom, squash and jiggle at a catch)
    //             and settle when it stops. It carries no load and takes nothing back from the arms.
    //   arms      roots kinematic on the pose (inverse mass 0), root frames = pose x ArmAim (a shoulder turn the
    //             sequencer sets) x rest; the chains solve as usual (pins, tethers, muscles, gravity), damped
    //             relative to the pose's velocity
    //   mouths    as before (kinematic on the web cluster)
    // The sub-steps interpolate the pose from the last tick's to this one's. SleepPlanned freezes everything but the
    // breathing until Wake (the sequencer decides both). MaxArmSpeed: the fastest arm or mouth particle over the tick.
    public bool Planned { get; private set; }
    // rad/s at scale 1 (Froude: / sqrt(size)) per lattice slot: crown ring (rigid), neck ring, mantle ring, head front, apex
    public static readonly float[] SecondaryOmega = { 0f, 18f, 11f, 28f, 9f };
    public const float SecondaryZeta = 0.35f;
    public const float SecondaryMaxOffset = 0.25f;   // x mantle length x size
    public readonly Quaternion[] ArmAim;
    public Vector3 PlanOrigin => planPos;
    public Quaternion PlanRotation => planRot;
    public Vector3 PlanVelocity => planVel;
    public float MaxArmSpeed { get; private set; }

    Vector3 planPos, planPrevPos, planVel;
    Quaternion planRot = Quaternion.identity, planPrevRot = Quaternion.identity;
    Vector3[] secVel, secOff, secGoalPrev, secGoalVel, tickArm;   // per lattice particle: offset velocity, offset, goal, goal velocity
    float[] secW;
    bool planFresh = true;

    public void EnablePlanned()
    {
        if (Planned) return;
        Planned = true;
        planRot = Lattice.ClusterRotation[OctopusShape.ClusterWeb];
        planPos = Lattice.X[0] - OctoMath.Rotate(planRot, Lattice.Rest[0]);
        secVel = new Vector3[SoftLattice.Count]; secOff = new Vector3[SoftLattice.Count]; secGoalVel = new Vector3[SoftLattice.Count]; secGoalPrev = new Vector3[SoftLattice.Count]; secW = new float[SoftLattice.Count];
        for (int k = 0; k < 4; k++) { secW[k] = SecondaryOmega[0]; secW[4 + k] = SecondaryOmega[1]; secW[8 + k] = SecondaryOmega[2]; }
        secW[OctopusShape.HeadFront] = SecondaryOmega[3]; secW[OctopusShape.Apex] = SecondaryOmega[4];
        for (int i = 0; i < SoftLattice.Count; i++) secW[i] /= TimeScale;
        int n = 0;
        for (int a = 0; a < ArmCount; a++) n += Arms[a].N;
        for (int m = 0; m < MouthCount; m++) n += Mouth[m].N;
        tickArm = new Vector3[n];
        planFresh = true;
    }

    // The pose at the end of the coming tick. snap: a jump (spawn, teleport) - no interpolation, no secondary kick.
    public void SetPlannedPose(Vector3 origin, Quaternion rotation, bool snap = false)
    {
        if (snap || planFresh)
        {
            // a jump: the arms and mouths go along rigidly (from the last pose, or the constructed one)
            Quaternion d = OctoMath.Mul(rotation, OctoMath.Conj(planRot));
            for (int a = 0; a < ArmCount; a++) Carry(Arms[a], d, planPos, origin);
            for (int m = 0; m < MouthCount; m++) Carry(Mouth[m], d, planPos, origin);
            planPrevPos = origin; planPrevRot = rotation; planPos = origin; planRot = rotation; planVel = Vector3.zero;
            for (int i = 0; i < SoftLattice.Count; i++)
            {
                Vector3 g = origin + OctoMath.Rotate(rotation, Lattice.Rest[i]);
                Lattice.X[i] = g; Lattice.Prev[i] = g; secGoalPrev[i] = g; secVel[i] = Vector3.zero; secOff[i] = Vector3.zero; secGoalVel[i] = Vector3.zero;
            }
            // the clusters' rotation is warm-started from the last one: a jump (a flip to hanging) starts it at the new one
            for (int c = 0; c < SoftLattice.ClusterCount; c++) Lattice.ClusterRotation[c] = rotation;
            Lattice.FitAll();
            planFresh = false;
            for (int a = 0; a < ArmCount; a++) SyncRootPlanned(a, origin, rotation);
            return;
        }
        planPos = origin; planRot = rotation;
    }

    static void Carry(TentacleChain ch, Quaternion d, Vector3 from, Vector3 to)
    {
        for (int k = 0; k < ch.N; k++)
        {
            if (ch.Pinned[k]) continue;
            ch.X[k] = to + OctoMath.Rotate(d, ch.X[k] - from);
            ch.Prev[k] = ch.X[k];
        }
        ch.Back = to + OctoMath.Rotate(d, ch.Back - from);
        ch.RootFrame = OctoMath.Mul(d, ch.RootFrame);
        ch.UpdateFrames(0f);
    }

    public void SleepPlanned()
    {
        if (!Planned) return;
        for (int i = 0; i < SoftLattice.Count; i++) { Lattice.Prev[i] = Lattice.X[i]; secVel[i] = Vector3.zero; secGoalVel[i] = Vector3.zero; }
        for (int a = 0; a < ArmCount; a++) for (int k = 0; k < Arms[a].N; k++) Arms[a].Prev[k] = Arms[a].X[k];
        for (int m = 0; m < MouthCount; m++) for (int k = 0; k < Mouth[m].N; k++) Mouth[m].Prev[k] = Mouth[m].X[k];
        planPrevPos = planPos; planPrevRot = planRot; planVel = Vector3.zero;
        MaxArmSpeed = 0f;
        Asleep = true;
    }

    void TickPlanned(float dt)
    {
        if (Asleep)
        {
            Time += dt;
            Lattice.AdvanceBreath(dt);
            Lattice.FitAll();
            planPrevPos = planPos; planPrevRot = planRot;
            return;
        }
        int o = 0;
        for (int a = 0; a < ArmCount; a++) for (int k = 0; k < Arms[a].N; k++) tickArm[o++] = Arms[a].X[k];
        for (int m = 0; m < MouthCount; m++) for (int k = 0; k < Mouth[m].N; k++) tickArm[o++] = Mouth[m].X[k];
        float h = dt / subSteps;
        planVel = (planPos - planPrevPos) * (1f / dt);
        for (int s = 0; s < subSteps; s++)
        {
            float t = (s + 1f) / subSteps;
            Vector3 pos = planPrevPos + (planPos - planPrevPos) * t;
            Quaternion rot = NLerp(planPrevRot, planRot, t);
            PlannedSubStep(h, pos, rot);
        }
        planPrevPos = planPos; planPrevRot = planRot;
        for (int a = 0; a < ArmCount; a++) ArmForce[a] = Vector3.zero;
        Lattice.FitAll();
        for (int a = 0; a < ArmCount; a++) Arms[a].UpdateFrames(dt);
        for (int m = 0; m < MouthCount; m++) Mouth[m].UpdateFrames(dt);
        float worst = 0f;
        o = 0;
        for (int a = 0; a < ArmCount; a++) for (int k = 0; k < Arms[a].N; k++) worst = Mathf.Max(worst, (Arms[a].X[k] - tickArm[o++]).sqrMagnitude);
        for (int m = 0; m < MouthCount; m++) for (int k = 0; k < Mouth[m].N; k++) worst = Mathf.Max(worst, (Mouth[m].X[k] - tickArm[o++]).sqrMagnitude);
        MaxArmSpeed = Mathf.Sqrt(worst) / dt;
    }

    void PlannedSubStep(float h, Vector3 pos, Quaternion rot)
    {
        Time += h;
        lastSubStep = h;
        Lattice.AdvanceBreath(h);
        // the lattice: damped springs on the rigid goals
        float maxOff = SecondaryMaxOffset * Spec.MantleLength * SizeScale, ih = 1f / h;
        Vector3[] lx = Lattice.X, lp = Lattice.Prev;
        for (int i = 0; i < SoftLattice.Count; i++)
        {
            // in the goal's frame: offset o = x - goal, o'' = -goal'' - w^2 o - 2 zeta w o'
            Vector3 g = pos + OctoMath.Rotate(rot, Lattice.Rest[i]);
            Vector3 gv = (g - secGoalPrev[i]) * ih, ga = (gv - secGoalVel[i]) * ih;
            secGoalPrev[i] = g; secGoalVel[i] = gv;
            lp[i] = lx[i];
            float w = secW[i];
            if (!(w > 0f)) { lx[i] = g; continue; }
            Vector3 q = secVel[i], off = secOff[i];
            q += (ga * -1f - off * (w * w) - q * (2f * SecondaryZeta * w)) * h;
            off += q * h;
            float ol = off.magnitude;
            if (ol > maxOff) { off *= maxOff / ol; q -= off * (OctoMath.Dot(q, off) / (maxOff * maxOff)); }
            secOff[i] = off; secVel[i] = q;
            lx[i] = g + off;
        }
        for (int a = 0; a < ArmCount; a++)
        {
            TentacleChain ch = Arms[a];
            SyncRootPlanned(a, pos, rot);
            ch.Integrate(h, planVel);
            for (int it = 0; it < iterations; it++) ch.SolveIteration(h);
            SyncRootPlanned(a, pos, rot);
            ch.EndSubStep(h);
        }
        if (MouthCount > 0) BodySpin(h);
        for (int m = 0; m < MouthCount; m++)
        {
            TentacleChain ch = Mouth[m];
            SyncMouth(m);
            ch.Integrate(h, planVel);
            for (int it = 0; it < iterations; it++) ch.SolveIteration(h);
            SyncMouth(m);
            ch.EndSubStep(h);
        }
    }

    static Quaternion NLerp(Quaternion a, Quaternion b, float t)
    {
        float d = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w, sb = d < 0f ? -t : t, sa = 1f - t;
        return OctoMath.Normalize(OctoMath.Q(a.x * sa + b.x * sb, a.y * sa + b.y * sb, a.z * sa + b.z * sb, a.w * sa + b.w * sb));
    }

    void SyncRootPlanned(int a, Vector3 pos, Quaternion rot)
    {
        TentacleChain ch = Arms[a];
        Vector3 r = pos + OctoMath.Rotate(rot, OctopusShape.ArmRoot(Spec, a) * SizeScale);
        ch.X[0] = r; ch.Prev[0] = r;
        ch.InvMass[0] = 0f; ch.BackInvMass = 0f;
        ch.RootFrame = OctoMath.Mul(rot, OctoMath.Mul(ArmAim[a], armRestRot[a]));
        Vector3 f = OctoMath.Rotate(ch.RootFrame, 0f, 0f, 1f);
        ch.Back = r - f * ch.Spacing;
    }

    // ------------------------------------------------------------------ sleep
    // A whole-body sleep, not per arm: an anchored arm's constraint impulses are what hold the body up, so a
    // load-bearing arm cannot sleep on its own. The body falls asleep when every particle has moved less than
    // SleepSpeed x sizeScale (m/s) for SleepFrames frames with no travelling sinus running, and wakes on any
    // change that could move it: a particle moved from outside, a pin added, removed or moved, a muscle,
    // tone, sinus or shape target changed, gravity changed. The next tick after waking simulates as usual.
    public bool AllowSleep = true;
    public const float SleepSpeed = 0.002f;
    // A settled rest (set by the controller: every arm holds on or lies curled, the brain wants rest) sleeps at
    // a looser bound: the last sub-millimetre solver wobble of a loaded hold is invisible, and sleeping
    // freezes it out entirely. Anything still moving faster than this keeps the body awake.
    public const float SettledSleepSpeed = 0.15f;
    public bool Settled;
    public const int SleepFrames = 30;
    public bool Asleep { get; private set; }
    int calmFrames;
    Vector3[] sleepX;            // lattice then arm particles at sleep (allocated on the first sleep only)
    float[] sleepSig;            // per arm control signature at sleep
    Vector3 sleepGravity;

    public void Wake() { Asleep = false; calmFrames = 0; }

    // Calm = every particle moved less than SleepSpeed x sizeScale between the ends of two ticks. Measured tick
    // end to tick end (before the controller's contact planes), not by the last sub-step's X - Prev: a particle
    // held between a constraint and a contact plane keeps a constant X - Prev while standing perfectly still.
    Vector3[] tickX, tickX2;

    void UpdateSleep(float dt)
    {
        if (!AllowSleep) { calmFrames = 0; return; }
        int n = SoftLattice.Count;
        for (int a = 0; a < ArmCount; a++) n += Arms[a].N;
        bool first = tickX == null;
        if (first) { tickX = new Vector3[n]; tickX2 = new Vector3[n]; }
        // a settled rest is judged over two ticks: a part caught between a contact and the solver can flip
        // between two poses a few mm apart every tick for ever (going nowhere); sleeping freezes one of them
        bool two = Settled;
        float lim = (Settled ? 2f * SettledSleepSpeed : SleepSpeed) * SizeScale * dt;
        float maxD2 = 0f;
        int o = 0;
        Vector3[] lx = Lattice.X;
        for (int i = 0; i < SoftLattice.Count; i++, o++)
        {
            Vector3 q = two ? tickX2[o] : tickX[o], x = lx[i];
            maxD2 = Mathf.Max(maxD2, (x.x - q.x) * (x.x - q.x) + (x.y - q.y) * (x.y - q.y) + (x.z - q.z) * (x.z - q.z));
            tickX2[o] = tickX[o]; tickX[o] = x;
        }
        bool sinus = false;
        for (int a = 0; a < ArmCount; a++)
        {
            TentacleChain ch = Arms[a];
            if (ch.SinusAmount != 0f) sinus = true;
            Vector3[] x = ch.X;
            for (int k = 0; k < ch.N; k++, o++)
            {
                Vector3 q = two ? tickX2[o] : tickX[o], xk = x[k];
                if (k > 0) maxD2 = Mathf.Max(maxD2, (xk.x - q.x) * (xk.x - q.x) + (xk.y - q.y) * (xk.y - q.y) + (xk.z - q.z) * (xk.z - q.z));
                tickX2[o] = tickX[o]; tickX[o] = xk;
            }
        }
        if (first || sinus || maxD2 > lim * lim) { calmFrames = 0; return; }
        if (++calmFrames < SleepFrames) return;
        FallAsleep();
    }

    void FallAsleep()
    {
        int n = SoftLattice.Count;
        for (int a = 0; a < ArmCount; a++) n += Arms[a].N;
        if (sleepX == null) { sleepX = new Vector3[n]; sleepSig = new float[ArmCount]; }
        int o = 0;
        for (int i = 0; i < SoftLattice.Count; i++) { sleepX[o++] = Lattice.X[i]; Lattice.Prev[i] = Lattice.X[i]; }
        for (int a = 0; a < ArmCount; a++)
        {
            TentacleChain ch = Arms[a];
            for (int k = 0; k < ch.N; k++) { sleepX[o++] = ch.X[k]; ch.Prev[k] = ch.X[k]; }
            sleepSig[a] = Signature(ch);
        }
        sleepGravity = gravity;
        Asleep = true;
    }

    // Re-take the sleep snapshot from the particles as they are now (the controller's contact moved some after
    // the body fell asleep in this tick).
    public void ResnapSleep()
    {
        if (!Asleep) return;
        int o = 0;
        for (int i = 0; i < SoftLattice.Count; i++) { sleepX[o++] = Lattice.X[i]; Lattice.Prev[i] = Lattice.X[i]; }
        for (int a = 0; a < ArmCount; a++)
        {
            TentacleChain ch = Arms[a];
            for (int k = 0; k < ch.N; k++) { sleepX[o++] = ch.X[k]; ch.Prev[k] = ch.X[k]; }
        }
        if (tickX != null)
        {
            o = 0;
            for (int i = 0; i < SoftLattice.Count; i++) tickX[o++] = Lattice.X[i];
            for (int a = 0; a < ArmCount; a++) for (int k = 0; k < Arms[a].N; k++) tickX[o++] = Arms[a].X[k];
        }
    }

    bool SleepStillValid()
    {
        if (!AllowSleep || !Same(gravity, sleepGravity)) return false;
        int o = 0;
        for (int i = 0; i < SoftLattice.Count; i++) if (!Same(Lattice.X[i], sleepX[o++])) return false;
        for (int a = 0; a < ArmCount; a++)
        {
            TentacleChain ch = Arms[a];
            for (int k = 0; k < ch.N; k++) if (!Same(ch.X[k], sleepX[o++])) return false;
            if (Signature(ch) != sleepSig[a] || ch.SinusAmount != 0f) return false;
        }
        return true;
    }

    // exact (Vector3 == is approximate, ~1e-5 m)
    static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

    // Everything an arm is told to do, folded into one number (exact comparison: any change wakes).
    static float Signature(TentacleChain ch)
    {
        float s = ch.Tone * 1.618f + ch.SinusAmount * 2.718f + ch.ExtraDamping * 0.577f;
        for (int k = 0; k < ch.N - 1; k++)
        {
            Vector3 b = ch.BendTarget[k];
            s += (ch.LengthTarget[k] + b.x * 0.31f + b.y * 0.47f + b.z * 0.73f) * (k + 1);
        }
        for (int k = 1; k < ch.N; k++)
            if (ch.Pinned[k]) { Vector3 p = ch.PinPoint[k], nn = ch.PinNormal[k]; s += (k + 0.5f) * (p.x + 3.1f * p.y + 7.3f * p.z + nn.x + 1.7f * nn.y + 2.9f * nn.z + 11f); }
        return s;
    }

    float lastSubStep;

    // The swing's energy governor (ArmCoordinator.SwingGovernor, 1/s): absolute velocity damping on the whole
    // animal - lattice and every arm particle - while it is above zero. (Damping the lattice alone, half the
    // animal's mass, let a wide swing ring on in the arms.)
    public float SwingGovernor;
    // The pump's push (ArmCoordinator.SwingPush, m/s^2): a uniform acceleration of every free particle.
    public Vector3 SwingPush;

    void SubStep(float h)
    {
        Time += h;
        lastSubStep = h;
        if (SwingGovernor > 0f) Govern(Mathf.Exp(-SwingGovernor * h));
        if (SwingPush.x != 0f || SwingPush.y != 0f || SwingPush.z != 0f) Push(SwingPush * (h * h));
        Vector3 refVel = Velocity(h);
        if (MouthCount > 0) BodySpin(h);
        Lattice.Integrate(h, refVel);
        // Free arm segments relax their twist to the transported frame (the oral side stays oral, so an arm
        // curls toward its suckers); only pins turn the suckers. A twist target that follows the body axis fed
        // back into the curl direction and kept toned arms oscillating forever (0.2 m/s at the tips).
        for (int a = 0; a < ArmCount; a++) Arms[a].Integrate(h, refVel);
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
                float ir = rootWm[a] > 0f ? 1f / (rootWm[a] * h) : 0f, ib = backWm[a] > 0f ? 1f / (backWm[a] * h) : 0f;
                Vector3 J = armImpulse[a];
                J.x += (r1.x - r0.x) * ir + (b1.x - b0.x) * ib; J.y += (r1.y - r0.y) * ir + (b1.y - b0.y) * ib; J.z += (r1.z - r0.z) * ir + (b1.z - b0.z) * ib;
                armImpulse[a] = J;
            }
            Lattice.ShapeMatch();
        }
        for (int a = 0; a < ArmCount; a++)
        {
            TentacleChain ch = Arms[a];
            if (ReleaseOverloadedPins && ch.Overload > OverloadRelease && ch.OverloadedPin > 0) { ch.Unpin(ch.OverloadedPin); OverloadReleases++; }
            SyncRoot(a);
            ch.EndSubStep(h);
        }
        // the mouth tentacles: kinematic roots on the web, their own solve (nothing handed back to the lattice)
        for (int m = 0; m < MouthCount; m++)
        {
            TentacleChain ch = Mouth[m];
            SyncMouth(m);
            ch.Integrate(h, refVel);
            for (int it = 0; it < iterations; it++) ch.SolveIteration(h);
            SyncMouth(m);
            ch.EndSubStep(h);
        }
    }

    // Mouth tentacle m's root: the embedded point on the web cluster, kinematic (inverse mass 0, its back point
    // along the root frame).
    void SyncMouth(int m)
    {
        TentacleChain ch = Mouth[m];
        int nw = web.Length, o = m * nw;
        float rx = 0f, ry = 0f, rz = 0f;
        Vector3[] lx = Lattice.X;
        for (int j = 0; j < nw; j++) { Vector3 x = lx[web[j]]; float u = mouthW[o + j]; rx += x.x * u; ry += x.y * u; rz += x.z * u; }
        ch.X[0].x = rx; ch.X[0].y = ry; ch.X[0].z = rz;
        ch.Prev[0] = ch.X[0];
        ch.InvMass[0] = 0f; ch.BackInvMass = 0f;
        ch.RootFrame = OctoMath.Mul(Lattice.ClusterRotation[OctopusShape.ClusterWeb], mouthRestRot[m]);
        Vector3 f = OctoMath.Rotate(ch.RootFrame, 0f, 0f, 1f);
        ch.Back.x = rx - f.x * ch.Spacing; ch.Back.y = ry - f.y * ch.Spacing; ch.Back.z = rz - f.z * ch.Spacing;
    }

    // Every free particle's velocity + d / h (d = a h^2).
    void Push(Vector3 d)
    {
        Vector3[] lp = Lattice.Prev;
        for (int i = 0; i < SoftLattice.Count; i++) { lp[i].x -= d.x; lp[i].y -= d.y; lp[i].z -= d.z; }
        for (int a = 0; a < ArmCount; a++)
        {
            Vector3[] p = Arms[a].Prev; float[] im = Arms[a].InvMass;
            for (int k = 1; k < Segments; k++) if (im[k] > 0f) { p[k].x -= d.x; p[k].y -= d.y; p[k].z -= d.z; }
        }
    }

    // The head's angular velocity over the last sub-step, handed to every chain with the head centre: the reference
    // a calm chain is damped toward (TentacleChain.CalmDamping; the idle mouth tentacles). Scalar maths.
    Quaternion spinPrev; bool spinValid;
    void BodySpin(float h)
    {
        Quaternion q = Orientation;
        float wx = 0f, wy = 0f, wz = 0f;
        if (spinValid)
        {
            // d = q * conj(prev); omega = 2 d.xyz / h (d.w >= 0)
            float px = -spinPrev.x, py = -spinPrev.y, pz = -spinPrev.z, pw = spinPrev.w;
            float dx = q.w * px + q.x * pw + q.y * pz - q.z * py;
            float dy = q.w * py - q.x * pz + q.y * pw + q.z * px;
            float dz = q.w * pz + q.x * py - q.y * px + q.z * pw;
            float dw = q.w * pw - q.x * px - q.y * py - q.z * pz;
            float k = (dw < 0f ? -2f : 2f) / h;
            wx = dx * k; wy = dy * k; wz = dz * k;
        }
        spinPrev = q; spinValid = true;
        Vector3 w = new Vector3(wx, wy, wz), c = HeadCentre;
        for (int m = 0; m < MouthCount; m++) { Mouth[m].RefOmega = w; Mouth[m].RefCentre = c; }
    }

    // Every particle's velocity (X - Prev) x f.
    void Govern(float f)
    {
        Vector3[] lx = Lattice.X, lp = Lattice.Prev;
        for (int i = 0; i < SoftLattice.Count; i++)
        {
            lp[i].x = lx[i].x - (lx[i].x - lp[i].x) * f; lp[i].y = lx[i].y - (lx[i].y - lp[i].y) * f; lp[i].z = lx[i].z - (lx[i].z - lp[i].z) * f;
        }
        for (int a = 0; a < ArmCount; a++)
        {
            Vector3[] x = Arms[a].X, p = Arms[a].Prev;
            for (int k = 1; k < Segments; k++)
            {
                p[k].x = x[k].x - (x[k].x - p[k].x) * f; p[k].y = x[k].y - (x[k].y - p[k].y) * f; p[k].z = x[k].z - (x[k].z - p[k].z) * f;
            }
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

    public static int MouthBonesFor(in CephalopodSpec sp) => sp.MouthCount > 0 ? sp.MouthCount * sp.mouth.Segments : 0;
    public static int BoneCountFor(in CephalopodSpec sp) => sp.ArmCount * sp.arm.Segments + SoftBoneCount + MouthBonesFor(sp) + 3;
    public int BoneCount => ArmCount * Segments + SoftBoneCount + MouthCount * MouthSegments + 3;
    public int ArmBone(int arm, int k) => arm * Segments + k;
    public int SoftBone(int c) => ArmCount * Segments + c;
    public int MouthBone(int m, int k) => ArmCount * Segments + SoftBoneCount + m * MouthSegments + k;
    public int EyeBone(int e) => ArmCount * Segments + SoftBoneCount + MouthCount * MouthSegments + e;
    public int SiphonBone => ArmCount * Segments + SoftBoneCount + MouthCount * MouthSegments + 2;

    // Rest-space pivot of soft bone c (its bind pose is translate(-pivot / sizeScale) at scale 1).
    public Vector3 SoftBonePivot(int c) =>
        c < SoftLattice.ClusterCount ? Lattice.ClusterRestCentre[c] : Lattice.Rest[OctopusShape.Apex];

    // Frames and stretch re-taken from the particles as they are now, twist kept (no rate step): for a pass outside
    // the solve that moved particles after Tick took the frames (the controller's contacts).
    public void RefreshArmFrames()
    {
        for (int a = 0; a < ArmCount; a++) Arms[a].UpdateFrames(0f);
        for (int m = 0; m < MouthCount; m++) Mouth[m].UpdateFrames(0f);
    }

    public void WriteBoneWorld(Matrix4x4[] dst, int offset)
    {
        for (int a = 0; a < ArmCount; a++) Arms[a].WriteBones(dst, offset + a * Segments);
        for (int m = 0; m < MouthCount; m++) Mouth[m].WriteBones(dst, offset + MouthBone(m, 0));
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
        for (int m = 0; m < MouthCount; m++) Mouth[m].Encapsulate(ref lo, ref hi);
    }
}
