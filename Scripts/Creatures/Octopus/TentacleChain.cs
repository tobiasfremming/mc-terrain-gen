using System;
using UnityEngine;

// One octopus arm (tree octopus design 2.1): N particles, position-based dynamics on flat arrays.
//
//   X[0] is the root. Standalone it is kinematic (SetRoot); inside OctopusBody it is a point embedded in
//   the soft lattice: the body sets X[0], Back and their inverse masses before every solver iteration and
//   hands the corrections the chain applied to them back to the lattice (the root constraint impulse).
//   Back is a virtual particle one rest spacing behind the root, also embedded: it carries the bending
//   moment of the first joint into the body.
//
// Per sub-step: Integrate (Verlet, gravity, damping relative to a reference velocity), then
// `iterations` x SolveIteration, then EndSubStep (strict tail pass). Once per frame: UpdateFrames (frames,
// twist, stretch, squeeze; the solver reads the previous frame's frames).
// Constraints, in order:
//   distance    segment k ~ Spacing x LengthTarget[k], XPBD-lite (compliance, no lambda history), then
//               clamped to [MinStretch, MaxStretch] x Spacing
//   curvature   the muscle: the discrete curvature a + c - 2b of each joint triple is pulled toward the
//               target shape (rest curl toward ventral, a FABRIK reach shape, a travelling sinus). Its
//               gradient sums to zero, so muscles are internal forces: a limp arm (Tone 0) hangs,
//               a toned arm holds its curl against gravity and pushes back on the body.
//   bend cone   |c - a| >= the chord at bendLimitDeg (law of cosines on the current lengths)
//   pins        infinite mass (InvMass 0) at the contact point
// The free run past the last pin (or the root) is finally walked root-to-tip once and clamped exactly to
// the length and cone limits, so those invariants hold to float precision there.
//
// Frames: rotation-minimising (projection) frames transported from RootFrame, then a twist about each
// segment that turns the dorsal side (+Y) away from the pin surface (pinned) or away from the body axis
// (free, so the suckers face inward), rate-limited and clamped to MaxTwistDeg between neighbours.
// Bone k: origin X[k], rotation Frames[k] (+Z toward X[k+1]), scale (sigma, sigma, stretch) x SizeScale
// with sigma = Squeeze / sqrt(stretch) (volume-preserving stretch; 0.92 wrapped, 1.05 bulge past a wrap).
//
// No allocation after construction; no engine calls (pure C#).
public sealed class TentacleChain
{
    const float kMaxDt = 0.1f;
    const float kDistCompliance = 1e-7f;     // m/N, XPBD-lite segment compliance (slight give under load)
    const float kMaxSpeed = 30f;             // m/s per unit size: a hitch cannot explode the chain
    const float kSqueezeRate = 12f;          // 1/s, squeeze low-pass
    const float kConeSlack = 1e-4f;          // rad: the strict pass leaves joints at limit - slack

    public readonly TentacleSpec Spec;
    public readonly int N;
    public readonly float SizeScale, Spacing;
    public readonly Vector3[] X, Prev;
    public readonly float[] InvMass;         // 0 = pinned / kinematic
    public readonly float[] Mass;            // per particle (root included), kg
    public readonly Quaternion[] Frames;
    public readonly float[] Twist;           // rad about each segment, relative to the transported frame
    public readonly float[] Stretch;         // segment length / Spacing (bone k: segment k; tip: last segment)
    public readonly float[] Squeeze;         // cross-section factor (smoothed)
    public readonly float[] LengthTarget;    // per segment, x Spacing (muscle extension; 1 at rest)
    public readonly Vector3[] BendTarget;    // per joint j (0..N-2): target direction of segment j in the frame of segment j-1
    public readonly bool[] Pinned;
    public readonly Vector3[] PinPoint, PinNormal;
    public readonly int[] PinSource;

    public Vector3 Back;                     // virtual particle behind the root
    public float BackInvMass;
    public Quaternion RootFrame = Quaternion.identity;
    public Vector3 Gravity = new Vector3(0f, -9.81f, 0f);
    public float Tone = 1f;                  // muscle tone multiplier on Spec.Muscle (0 = limp)
    public float SinusAmount;                // 0..1 travelling sinus (reaching)
    public Vector3 AxisOrigin, AxisDir;      // body axis for the free twist target (AxisDir zero = no twist target)
    public float ExtraDamping;               // absolute velocity damping, 1/s (settling, tests)
    public float Time => time;
    // Overload of the most loaded pin: root distance / the arc at maximum stretch (> 1 = the pins cannot all
    // hold: an infeasible pull). OverloadedPin is that pin, or -1. The maximum over the current sub-step's iterations.
    public float Overload { get; private set; }
    public int OverloadedPin { get; private set; } = -1;

    readonly float[] restLength;             // per segment, current target length (m)
    readonly float[] arc;                    // per particle, rest arc length from the root (m)
    readonly Vector3[] restCurl;             // per joint, local rest-curl direction
    readonly Vector3[] localTarget;          // per joint, BendTarget + sinus for this sub-step
    readonly Vector3[] fab;                  // FABRIK scratch
    readonly Vector3[] frameX;               // +X of each frame (cached for the solver)
    readonly Vector3[] rmfX, rmfT;           // untwisted transported frame (x, tangent), scratch
    float time;
    readonly float cosLimit, minLen, maxLen, maxTwist;
    readonly float fabCos, fabSin;           // FABRIK joint limit (0.9 x the cone)

    public TentacleChain(TentacleSpec spec, Vector3 root, Quaternion rootFrame, float sizeScale = 1f)
    {
        Spec = spec;
        N = spec.Segments;
        SizeScale = Mathf.Max(1e-3f, sizeScale);
        Spacing = spec.Spacing * SizeScale;
        X = new Vector3[N]; Prev = new Vector3[N]; InvMass = new float[N]; Mass = new float[N];
        Frames = new Quaternion[N]; Twist = new float[N]; Stretch = new float[N]; Squeeze = new float[N];
        LengthTarget = new float[N - 1]; restLength = new float[N - 1]; arc = new float[N];
        BendTarget = new Vector3[N - 1]; restCurl = new Vector3[N - 1]; localTarget = new Vector3[N - 1];
        Pinned = new bool[N]; PinPoint = new Vector3[N]; PinNormal = new Vector3[N]; PinSource = new int[N];
        fab = new Vector3[N]; frameX = new Vector3[N]; rmfX = new Vector3[N]; rmfT = new Vector3[N];

        cosLimit = Mathf.Cos(spec.BendLimitDeg * Mathf.Deg2Rad);
        fabCos = Mathf.Cos(0.9f * spec.BendLimitDeg * Mathf.Deg2Rad); fabSin = Mathf.Sin(0.9f * spec.BendLimitDeg * Mathf.Deg2Rad);
        minLen = spec.MinStretch * Spacing; maxLen = spec.MaxStretch * Spacing;
        maxTwist = spec.MaxTwistDeg * Mathf.Deg2Rad;

        // mass ~ cross-section area along the arm, total = massPerArm x size^3
        float total = 0f;
        for (int k = 0; k < N; k++) { float r = spec.RadiusAt(spec.Spacing * k); Mass[k] = r * r; total += Mass[k]; }
        float m = spec.MassPerArm * SizeScale * SizeScale * SizeScale / total;
        for (int k = 0; k < N; k++) { Mass[k] *= m; InvMass[k] = 1f / Mass[k]; }
        InvMass[0] = 0f; // kinematic until a body embeds it

        float tipCurl = spec.TipCurlDeg * Mathf.Deg2Rad;
        for (int j = 0; j < N - 1; j++)
        {
            float f = (float)j / (N - 2);
            float a = tipCurl * f * f;
            restCurl[j] = new Vector3(0f, -Mathf.Sin(a), Mathf.Cos(a)); // toward ventral (-Y): rotation about +X
            BendTarget[j] = restCurl[j];
            LengthTarget[j] = 1f;
        }

        RootFrame = rootFrame;
        Vector3 fwd = rootFrame * Vector3.forward;
        for (int k = 0; k < N; k++) { X[k] = root + fwd * (Spacing * k); Prev[k] = X[k]; Squeeze[k] = 1f; }
        Back = root - fwd * Spacing;
        UpdateFrames(0f);
    }

    public float Length => Spacing * (N - 1);

    // ------------------------------------------------------------------ control

    // Kinematic root (standalone use): the root and its virtual back particle follow this pose.
    public void SetRoot(Vector3 root, Quaternion frame)
    {
        X[0] = root; Prev[0] = root; RootFrame = frame;
        Back = root - (frame * Vector3.forward) * Spacing;
    }

    public void Pin(int k, Vector3 point, Vector3 normal, int source = 0)
    {
        if (k <= 0 || k >= N) return; // the root belongs to the body
        Pinned[k] = true; PinPoint[k] = point; PinNormal[k] = OctoMath.Norm(normal, Vector3.up); PinSource[k] = source;
        X[k] = point; Prev[k] = point; InvMass[k] = 0f;
    }

    public void Unpin(int k)
    {
        if (k <= 0 || k >= N || !Pinned[k]) return;
        Pinned[k] = false; InvMass[k] = 1f / Mass[k];
    }

    public void UnpinAll() { for (int k = 1; k < N; k++) Unpin(k); }

    public void SetLimp(bool limp) => Tone = limp ? 0f : 1f;

    // Rest shape: curl toward ventral, rest lengths.
    public void ClearTargetShape()
    {
        for (int j = 0; j < N - 1; j++) { BendTarget[j] = restCurl[j]; LengthTarget[j] = 1f; }
    }

    // FABRIK gives the intent: a chain from the current root to `target` (4 passes on scratch), converted
    // to per-joint local directions (transported frames along the FABRIK chain) and blended with the rest
    // curl by `weight`. With `allowStretch` the lengths extend up to MaxStretch when the target is out of reach.
    public void SetTargetShape(Vector3 target, float weight, bool allowStretch)
    {
        weight = Mathf.Clamp01(weight);
        float reach = Length, dist = (target - X[0]).magnitude;
        float s = allowStretch ? Mathf.Clamp(dist / Mathf.Max(reach, 1e-6f), 1f, Spec.MaxStretch) : 1f;
        float seg = Spacing * s;
        for (int k = 0; k < N; k++) fab[k] = X[k];
        Vector3 root = X[0];
        for (int pass = 0; pass < 4; pass++)
        {
            fab[N - 1] = target;
            for (int k = N - 2; k >= 0; k--) fab[k] = fab[k + 1] + OctoMath.Norm(fab[k] - fab[k + 1], Vector3.up) * seg;
            fab[0] = root;
            // root -> tip with the joint limits (90 % of the cone, room for the sinus): the shape stays feasible
            Vector3 prev = OctoMath.Rotate(RootFrame, 0f, 0f, 1f);
            for (int k = 1; k < N; k++)
            {
                Vector3 d = ClampToCone(OctoMath.Norm(fab[k] - fab[k - 1], prev), prev, fabCos, fabSin);
                fab[k] = fab[k - 1] + d * seg;
                prev = d;
            }
        }
        // transported frames along the FABRIK chain, starting at the root frame
        Vector3 px = RootFrame * Vector3.right, pz = RootFrame * Vector3.forward;
        for (int j = 0; j < N - 1; j++)
        {
            Vector3 d = OctoMath.Norm(fab[j + 1] - fab[j], pz);
            Vector3 py = OctoMath.Cross(pz, px);
            Vector3 local = new Vector3(OctoMath.Dot(d, px), OctoMath.Dot(d, py), OctoMath.Dot(d, pz));
            Vector3 blended = Vector3.Lerp(restCurl[j], local, weight);
            BendTarget[j] = OctoMath.Norm(blended, restCurl[j]);
            LengthTarget[j] = Mathf.Lerp(1f, s, weight);
            Vector3 x = px - d * OctoMath.Dot(px, d);
            px = OctoMath.Norm(x, px); pz = d;
        }
    }

    // Unit d rotated toward unit axis until it lies within the cone of half-angle acos(cos).
    static Vector3 ClampToCone(Vector3 d, Vector3 axis, float cos, float sin)
    {
        float c = OctoMath.Dot(d, axis);
        if (c >= cos) return d;
        Vector3 perp = d - axis * c;
        float pl = perp.magnitude;
        if (pl < 1e-6f) perp = OctoMath.Norm(OctoMath.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right), Vector3.right);
        else perp /= pl;
        return axis * cos + perp * sin;
    }

    // ------------------------------------------------------------------ standalone tick

    public void Tick(float dt, int subSteps = 3, int iterations = 3)
    {
        if (!(dt > 0f)) { UpdateFrames(0f); return; }
        dt = Mathf.Min(dt, kMaxDt);
        subSteps = Mathf.Max(1, subSteps);
        float h = dt / subSteps;
        for (int s = 0; s < subSteps; s++)
        {
            Integrate(h, Vector3.zero);
            for (int it = 0; it < iterations; it++) SolveIteration(h);
            EndSubStep(h);
        }
        UpdateFrames(dt);
    }

    // ------------------------------------------------------------------ sub-step pieces (OctopusBody interleaves these)
    // The hot loops are written out in scalar float maths: Vector3/Quaternion operators are calls on Mono.

    // Verlet with gravity; velocity damped toward refVel (the body's velocity), so damping never fights a
    // free fall of the whole animal. The root is integrated by its owner.
    public void Integrate(float h, Vector3 refVel)
    {
        float damp = MathF.Exp(-Spec.Damping * h), abs = MathF.Exp(-ExtraDamping * h);
        float vmax = kMaxSpeed * MathF.Sqrt(SizeScale), vmax2 = vmax * vmax;
        float inv = 1f / h;
        float gx = Gravity.x * h, gy = Gravity.y * h, gz = Gravity.z * h;
        float rx = refVel.x, ry = refVel.y, rz = refVel.z;
        Vector3[] x = X, p = Prev;
        Overload = 0f; OverloadedPin = -1; // max over this sub-step's iterations
        for (int k = 1; k < N; k++)
        {
            if (Pinned[k]) { x[k] = PinPoint[k]; p[k] = PinPoint[k]; continue; }
            Vector3 xk = x[k], pk = p[k];
            float vx = ((xk.x - pk.x) * inv - rx) * damp + rx, vy = ((xk.y - pk.y) * inv - ry) * damp + ry, vz = ((xk.z - pk.z) * inv - rz) * damp + rz;
            vx = vx * abs + gx; vy = vy * abs + gy; vz = vz * abs + gz;
            float s2 = vx * vx + vy * vy + vz * vz;
            if (s2 > vmax2) { float f = vmax / MathF.Sqrt(s2); vx *= f; vy *= f; vz *= f; }
            p[k] = xk;
            x[k].x = xk.x + vx * h; x[k].y = xk.y + vy * h; x[k].z = xk.z + vz * h;
        }

        // this sub-step's local bend targets: BendTarget + the travelling sinus (lateral, about local +Y),
        // fading in from the root
        float amp = SinusAmount * Spec.SinusAmpDeg * Mathf.Deg2Rad;
        float w = 2f * Mathf.PI * Spec.SinusHz / MathF.Sqrt(SizeScale);
        float lo = Spec.MinStretch, hi = Spec.MaxStretch;
        for (int j = 0; j < N - 1; j++)
        {
            Vector3 t = BendTarget[j];
            if (amp != 0f)
            {
                float f = (float)j / (N - 2);
                float a = amp * f * MathF.Sin(w * time - 2f * Mathf.PI * Spec.SinusWaves * f);
                float c = MathF.Cos(a), s = MathF.Sin(a);
                t.x = t.x * c + t.z * s; t.y = t.y; t.z = -t.x * s + t.z * c;
            }
            localTarget[j] = t;
            restLength[j] = Spacing * Mathf.Clamp(LengthTarget[j], lo, hi);
            arc[j + 1] = arc[j] + restLength[j];
        }
    }

    // One Gauss-Seidel pass, root to tip: segment j's distance, then joint j's curvature target and cone
    // (joint j = segment j-1 -> j; joint 0 uses the virtual Back particle).
    public void SolveIteration(float h)
    {
        float alpha = kDistCompliance / (h * h);
        float k0 = Mathf.Clamp01(Tone * Spec.Muscle);
        float cl = cosLimit, mn = minLen, mx = maxLen;
        Vector3 rootX = OctoMath.Rotate(RootFrame, 1f, 0f, 0f);
        Vector3[] X = this.X;
        float[] W = InvMass;
        for (int j = 0; j < N - 1; j++)
        {
            // ---- distance, segment j
            {
                float wa = W[j], wb = W[j + 1], w = wa + wb;
                if (w > 0f)
                {
                    Vector3 pa = X[j], pb = X[j + 1];
                    float dx = pb.x - pa.x, dy = pb.y - pa.y, dz = pb.z - pa.z;
                    float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (len > 1e-9f)
                    {
                        float lambda = -(len - restLength[j]) / (w + alpha);
                        float nl = len + lambda * w;
                        if (nl < mn) lambda = (mn - len) / w; else if (nl > mx) lambda = (mx - len) / w;
                        float s = lambda / len, sa = s * wa, sb = s * wb;
                        X[j].x = pa.x - dx * sa; X[j].y = pa.y - dy * sa; X[j].z = pa.z - dz * sa;
                        X[j + 1].x = pb.x + dx * sb; X[j + 1].y = pb.y + dy * sb; X[j + 1].z = pb.z + dz * sb;
                    }
                }
            }

            // ---- joint j: curvature target (muscle), then the bend cone, on the triple (a, b, c)
            Vector3 A = j == 0 ? Back : X[j - 1], B = X[j], C = X[j + 1];
            float ax = A.x, ay = A.y, az = A.z, bx = B.x, by = B.y, bz = B.z, cx = C.x, cy = C.y, cz = C.z;
            float wA = j == 0 ? BackInvMass : W[j - 1], wB = W[j], wC = W[j + 1];
            bool moved = false;

            if (k0 > 0f)
            {
                float wsum = wA + wC + 4f * wB;
                if (wsum > 0f)
                {
                    float zx = bx - ax, zy = by - ay, zz = bz - az;
                    float l1 = MathF.Sqrt(zx * zx + zy * zy + zz * zz);
                    if (l1 > 1e-9f)
                    {
                        float il = 1f / l1; zx *= il; zy *= il; zz *= il;
                        Vector3 fx = j == 0 ? rootX : frameX[j - 1];
                        float dfz = fx.x * zx + fx.y * zy + fx.z * zz;
                        float xx = fx.x - zx * dfz, xy = fx.y - zy * dfz, xz = fx.z - zz * dfz;
                        float xl = MathF.Sqrt(xx * xx + xy * xy + xz * xz);
                        if (xl > 1e-6f)
                        {
                            float ix = 1f / xl; xx *= ix; xy *= ix; xz *= ix;
                            // y = z cross x
                            float yx = zy * xz - zz * xy, yy = zz * xx - zx * xz, yz = zx * xy - zy * xx;
                            Vector3 lt = localTarget[j];
                            float l2 = restLength[j], l0 = j == 0 ? Spacing : restLength[j - 1];
                            float tx = (xx * lt.x + yx * lt.y + zx * lt.z) * l2 - zx * l0;
                            float ty = (xy * lt.x + yy * lt.y + zy * lt.z) * l2 - zy * l0;
                            float tz = (xz * lt.x + yz * lt.y + zz * lt.z) * l2 - zz * l0;
                            float f = -k0 / wsum;
                            float lx = (ax + cx - 2f * bx - tx) * f, ly = (ay + cy - 2f * by - ty) * f, lz = (az + cz - 2f * bz - tz) * f;
                            ax += lx * wA; ay += ly * wA; az += lz * wA;
                            cx += lx * wC; cy += ly * wC; cz += lz * wC;
                            float w2 = 2f * wB;
                            bx -= lx * w2; by -= ly * w2; bz -= lz * w2;
                            moved = true;
                        }
                    }
                }
            }

            float wac = wA + wC;
            if (wac > 0f)
            {
                float ux = bx - ax, uy = by - ay, uz = bz - az, vx = cx - bx, vy = cy - by, vz = cz - bz;
                float la2 = ux * ux + uy * uy + uz * uz, lc2 = vx * vx + vy * vy + vz * vz;
                float dmin2 = la2 + lc2 + 2f * MathF.Sqrt(la2 * lc2) * cl;
                float ex = cx - ax, ey = cy - ay, ez = cz - az;
                float d2 = ex * ex + ey * ey + ez * ez;
                if (d2 < dmin2 && d2 > 1e-14f)
                {
                    float d = MathF.Sqrt(d2);
                    float s = (d - MathF.Sqrt(dmin2)) / (d * wac);
                    float sa = s * wA, sc = s * wC;
                    ax += ex * sa; ay += ey * sa; az += ez * sa;
                    cx -= ex * sc; cy -= ey * sc; cz -= ez * sc;
                    moved = true;
                }
            }

            if (moved)
            {
                if (j == 0) { Back.x = ax; Back.y = ay; Back.z = az; } else { X[j - 1].x = ax; X[j - 1].y = ay; X[j - 1].z = az; }
                X[j].x = bx; X[j].y = by; X[j].z = bz; X[j + 1].x = cx; X[j + 1].y = cy; X[j + 1].z = cz;
            }
        }

        Tethers();

        // pins hold exactly
        for (int k = 1; k < N; k++) if (Pinned[k]) X[k] = PinPoint[k];
    }

    // Long-range attachments (Kim et al. 2012): every free particle between a pin and the next pin toward
    // the root (or the root itself, and Back) stays within its rest arc length of that pin. Without them a
    // light chain cannot hold a heavy body in a few Gauss-Seidel passes and stretches far past its limit.
    // Only the free particle moves (the pin has infinite mass); a moved root reaches the body as impulse.
    void Tethers()
    {
        Vector3[] X = this.X;
        float[] W = InvMass;
        for (int p = N - 1; p >= 1; p--)
        {
            if (!Pinned[p]) continue;
            Vector3 q = X[p];
            int k = p - 1;
            for (; k >= 0; k--)
            {
                if (Pinned[k]) break;
                if (W[k] <= 0f) continue;
                float lim = arc[p] - arc[k];
                Vector3 x = X[k];
                float dx = x.x - q.x, dy = x.y - q.y, dz = x.z - q.z;
                float d2 = dx * dx + dy * dy + dz * dz;
                if (k == 0)
                {
                    float o = MathF.Sqrt(d2) / (maxLen * p);
                    if (o > Overload) { Overload = o; OverloadedPin = p; }
                }
                if (d2 > lim * lim)
                {
                    float f = lim / MathF.Sqrt(d2);
                    X[k].x = q.x + dx * f; X[k].y = q.y + dy * f; X[k].z = q.z + dz * f;
                }
            }
            if (k < 0 && BackInvMass > 0f)
            {
                float lim = arc[p] + Spacing;
                float dx = Back.x - q.x, dy = Back.y - q.y, dz = Back.z - q.z;
                float d2 = dx * dx + dy * dy + dz * dz;
                if (d2 > lim * lim)
                {
                    float f = lim / MathF.Sqrt(d2);
                    Back.x = q.x + dx * f; Back.y = q.y + dy * f; Back.z = q.z + dz * f;
                }
            }
        }
    }

    // Ends a sub-step: the strict tail pass. Frames are updated once per frame (UpdateFrames).
    public void EndSubStep(float h)
    {
        time += h;
        StrictTail();
    }

    // The free run past the last fixed particle, walked outward: lengths and cone exact.
    void StrictTail()
    {
        int start = 0;
        for (int k = N - 1; k > 0; k--) if (Pinned[k]) { start = k; break; }
        Vector3 pa = start == 0 ? Back : X[start - 1], pb = X[start];
        float px = pb.x - pa.x, py = pb.y - pa.y, pz = pb.z - pa.z;
        float pl = MathF.Sqrt(px * px + py * py + pz * pz);
        if (pl < 1e-9f) { Vector3 f = OctoMath.Rotate(RootFrame, 0f, 0f, 1f); px = f.x; py = f.y; pz = f.z; } else { px /= pl; py /= pl; pz /= pl; }
        float cl = cosLimit, lim = MathF.Acos(Mathf.Clamp(cosLimit, -1f, 1f)) - kConeSlack;
        float cLim = MathF.Cos(lim), sLim = MathF.Sin(lim);
        for (int k = start; k < N - 1; k++)
        {
            Vector3 a = X[k], b = X[k + 1];
            float dx = b.x - a.x, dy = b.y - a.y, dz = b.z - a.z;
            float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len > 1e-9f) { dx /= len; dy /= len; dz /= len; } else { dx = px; dy = py; dz = pz; }
            float cs = dx * px + dy * py + dz * pz;
            bool fix = false;
            if (cs < cl)
            {
                // rotate onto the cone, just inside it
                float qx = dx - px * cs, qy = dy - py * cs, qz = dz - pz * cs;
                float ql = MathF.Sqrt(qx * qx + qy * qy + qz * qz);
                if (ql < 1e-6f) { Vector3 r = OctoMath.Rotate(RootFrame, 0f, 1f, 0f); qx = r.x; qy = r.y; qz = r.z; ql = 1f; }
                qx /= ql; qy /= ql; qz /= ql;
                dx = px * cLim + qx * sLim; dy = py * cLim + qy * sLim; dz = pz * cLim + qz * sLim;
                fix = true;
            }
            if (len < minLen) { len = minLen; fix = true; } else if (len > maxLen) { len = maxLen; fix = true; }
            if (fix) { X[k + 1].x = a.x + dx * len; X[k + 1].y = a.y + dy * len; X[k + 1].z = a.z + dz * len; }
            px = dx; py = dy; pz = dz;
        }
    }

    // Rotation-minimising frames from the root frame + clamped twist; stretch and squeeze. Scalar maths.
    public void UpdateFrames(float h)
    {
        Vector3 r0 = OctoMath.Rotate(RootFrame, 1f, 0f, 0f), r2 = OctoMath.Rotate(RootFrame, 0f, 0f, 1f);
        float rxx = r0.x, rxy = r0.y, rxz = r0.z, rzx = r2.x, rzy = r2.y, rzz = r2.z;
        float rate = Spec.TwistRateDeg * Mathf.Deg2Rad * h;
        float ax = AxisDir.x, ay = AxisDir.y, az = AxisDir.z, al = MathF.Sqrt(ax * ax + ay * ay + az * az);
        bool hasAxis = al > 1e-6f;
        if (hasAxis) { ax /= al; ay /= al; az /= al; }
        float ox = AxisOrigin.x, oy = AxisOrigin.y, oz = AxisOrigin.z;
        float sqBlend = h > 0f ? 1f - MathF.Exp(-kSqueezeRate * h) : 0f;
        float invSp = 1f / Spacing;
        for (int k = 0; k < N; k++)
        {
            int i0 = k < N - 1 ? k : N - 2;
            Vector3 p0 = X[i0], p1 = X[i0 + 1];
            float tx = p1.x - p0.x, ty = p1.y - p0.y, tz = p1.z - p0.z;
            float len = MathF.Sqrt(tx * tx + ty * ty + tz * tz);
            if (len > 1e-9f) { tx /= len; ty /= len; tz /= len; } else { tx = rzx; ty = rzy; tz = rzz; }
            // x: the previous frame's x parallel-transported by the minimal rotation t_prev -> t (exact
            // rotation-minimising: zero twist between frames), v' = v c + k x v + k (k.v) / (1 + c)
            float kx = rzy * tz - rzz * ty, ky = rzz * tx - rzx * tz, kz = rzx * ty - rzy * tx;
            float cc = rzx * tx + rzy * ty + rzz * tz;
            float xx, xy, xz;
            if (cc > -0.99f)
            {
                float kv = (kx * rxx + ky * rxy + kz * rxz) / (1f + cc);
                xx = rxx * cc + (ky * rxz - kz * rxy) + kx * kv;
                xy = rxy * cc + (kz * rxx - kx * rxz) + ky * kv;
                xz = rxz * cc + (kx * rxy - ky * rxx) + kz * kv;
            }
            else { xx = rxx; xy = rxy; xz = rxz; } // folded back on itself: fall back to projection
            float d = xx * tx + xy * ty + xz * tz;
            xx -= tx * d; xy -= ty * d; xz -= tz * d;
            float xl = MathF.Sqrt(xx * xx + xy * xy + xz * xz);
            if (xl < 1e-6f) { xx = tz; xy = 0f; xz = -tx; xl = MathF.Sqrt(xx * xx + xz * xz); if (xl < 1e-6f) { xx = 1f; xz = 0f; xl = 1f; } }
            xx /= xl; xy /= xl; xz /= xl;
            // y = t cross x
            float yx = ty * xz - tz * xy, yy = tz * xx - tx * xz, yz = tx * xy - ty * xx;

            // twist target: dorsal away from the pin surface / from the body axis
            float tw = Twist[k];
            if (h > 0f)
            {
                float ux, uy, uz;
                bool want = true;
                if (Pinned[k]) { Vector3 n = PinNormal[k]; ux = n.x; uy = n.y; uz = n.z; }
                else if (hasAxis)
                {
                    Vector3 p = X[k];
                    float rx = p.x - ox, ry = p.y - oy, rz = p.z - oz, ra = rx * ax + ry * ay + rz * az;
                    ux = rx - ax * ra; uy = ry - ay * ra; uz = rz - az * ra;
                }
                else { ux = uy = uz = 0f; want = false; }
                if (want)
                {
                    float ut = ux * tx + uy * ty + uz * tz;
                    ux -= tx * ut; uy -= ty * ut; uz -= tz * ut;
                    if (ux * ux + uy * uy + uz * uz > 1e-10f)
                    {
                        float target = MathF.Atan2(-(ux * xx + uy * xy + uz * xz), ux * yx + uy * yy + uz * yz);
                        float delta = target - tw;
                        delta -= 2f * Mathf.PI * MathF.Floor((delta + Mathf.PI) / (2f * Mathf.PI));
                        tw += delta < -rate ? -rate : delta > rate ? rate : delta;
                    }
                }
                else tw += -tw < -rate ? -rate : -tw > rate ? rate : -tw;
            }
            Twist[k] = tw;
            rmfX[k].x = xx; rmfX[k].y = xy; rmfX[k].z = xz;
            rmfT[k].x = tx; rmfT[k].y = ty; rmfT[k].z = tz;
            rxx = xx; rxy = xy; rxz = xz; rzx = tx; rzy = ty; rzz = tz;

            Stretch[k] = len * invSp;
            float sq = 1f;
            if (Pinned[k]) sq = Spec.WrapSqueeze;
            else if ((k > 0 && Pinned[k - 1]) || (k + 1 < N && Pinned[k + 1])) sq = Spec.Bulge;
            Squeeze[k] += (sq - Squeeze[k]) * sqBlend;
        }

        // Pins first: every twist stays within MaxTwist x (joint distance) of each pin's twist and of the
        // root's (0), so a pin can always reach its target; then the neighbour clamp.
        for (int p = 1; p < N; p++)
        {
            if (!Pinned[p]) continue;
            float tp = Twist[p], lim = maxTwist * (p + 1);
            tp = tp < -lim ? -lim : tp > lim ? lim : tp;
            Twist[p] = tp;
            for (int k = 0; k < N; k++)
            {
                if (Pinned[k]) continue;
                float w = maxTwist * (k > p ? k - p : p - k), lo = tp - w, hi = tp + w;
                float tk = Twist[k];
                Twist[k] = tk < lo ? lo : tk > hi ? hi : tk;
            }
        }
        float prevTwist = 0f;
        for (int k = 0; k < N; k++)
        {
            float lo = prevTwist - maxTwist, hi = prevTwist + maxTwist, tw = Twist[k];
            tw = tw < lo ? lo : tw > hi ? hi : tw;
            Twist[k] = tw; prevTwist = tw;

            // rotate (x, y) about t by the twist: x' = x cos + y sin, y' = y cos - x sin
            Vector3 x = rmfX[k], t = rmfT[k];
            float yx = t.y * x.z - t.z * x.y, yy = t.z * x.x - t.x * x.z, yz = t.x * x.y - t.y * x.x;
            float c = MathF.Cos(tw), s = MathF.Sin(tw);
            float x2x = x.x * c + yx * s, x2y = x.y * c + yy * s, x2z = x.z * c + yz * s;
            float y2x = yx * c - x.x * s, y2y = yy * c - x.y * s, y2z = yz * c - x.z * s;
            Frames[k] = OctoMath.FromBasis(x2x, x2y, x2z, y2x, y2y, y2z, t.x, t.y, t.z);
            frameX[k].x = x2x; frameX[k].y = x2y; frameX[k].z = x2z;
        }
    }

    // ------------------------------------------------------------------ output

    // N bone-world matrices (see the class comment) into dst[offset..offset+N).
    public void WriteBones(Matrix4x4[] dst, int offset)
    {
        for (int k = 0; k < N; k++)
        {
            float s = Stretch[k] > 1e-3f ? Stretch[k] : 1e-3f;
            float sigma = Squeeze[k] / MathF.Sqrt(s);
            dst[offset + k] = OctoMath.TRS(X[k], Frames[k], sigma * SizeScale, sigma * SizeScale, s * SizeScale);
        }
    }

    public void Encapsulate(ref Vector3 lo, ref Vector3 hi)
    {
        for (int k = 0; k < N; k++) { lo = Vector3.Min(lo, X[k]); hi = Vector3.Max(hi, X[k]); }
    }
}
