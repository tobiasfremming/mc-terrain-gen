// Creature kit (sky manta): WhipTail - a long, thin, tapering tail (a manta's or a stingray's whip, a rat's tail, a
// whip lizard's lash) that FOLLOWS the body: every point of it heads for the place its root passed through that far
// back along the root's own path (the tail trails the path the body flew: through turns, heaves, rolls and loops),
// easing there more and more slowly toward the tip (followSeconds: the tip lags most - the chain rule), sagging a
// little under its weight, with exact segment lengths and a bend cone. Unconditionally stable (a first-order follow
// of a bounded target): no spring to ring, whatever the body does.
// Mesh: a tapered round tube, rings along the rest chain, two bones blended between rings, a fine point at the tip.
// Kit-only. Runtime stage AfterLimbs (every sub-step); allocation-free after construction.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("WhipTail", PartKind.Chain, Bones = "= segments", Summary = "A long thin tapering tail that trails the path its root flew, easing there more slowly toward the tip (the tip lags most), sagging a little; exact lengths, bend cone; a fine tapered tube.",
          System = "WhipTailRuntime (part runtime, stage AfterLimbs) + WhipTail.EmitMesh", MaterialLayers = new[] { 15 }, KitOnly = true,
          UseWhen = "manta and stingray whips, rat and opossum tails, whip lizards: any long thin tail with no muscle of its own that lags the body")]
    public sealed class WhipTail : CreaturePart
    {
        [Param(ParamUnit.Metres, "length", Example = "manta 1.8")] public float length = 1.8f;
        [Param(ParamUnit.Count, "segments (one bone each)", Min = 2, Max = 24, Default = "14")] public int segments = 14;
        [Param(ParamUnit.Metres, "radius at the root")] public float radiusRoot = 0.028f;
        [Param(ParamUnit.Metres, "radius at the tip")] public float radiusTip = 0.0015f;
        [Param(ParamUnit.Seconds, "the tip's lag: time constant of its follow (the root's is 0, growing along the tail)", Example = "manta 0.35")] public float followSeconds = 0.35f;
        [Param(ParamUnit.Degrees, "the tail sags by this much per segment under its own weight", Min = 0, Max = 10)] public float sagDeg = 1.2f;
        [Param(ParamUnit.Degrees, "bend cone between segments", Min = 5, Max = 60)] public float maxBendDeg = 24f;
        [Param(ParamUnit.Count, "mesh: sides of the tube", Min = 4, Max = 16, Default = "8")] public int sides = 8;

        public int Segments => Mathf.Clamp(segments, 2, 24);
        public override int BoneCount(in SpeciesCtx c) => Segments;
        public override int MeshOrder => 100;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a WhipTail roots on a spine body";
            if (at.joint < 0 || at.joint >= c.Joints) return $"{id}: joint {at.joint} outside 0..{c.Joints - 1}";
            if (!(length > 0f) || !(radiusRoot > 0f) || radiusTip < 0f) return $"{id}: length and radiusRoot > 0, radiusTip >= 0";
            if (segments < 2 || segments > 24 || sides < 4 || sides > 16) return $"{id}: segments 2..24, sides 4..16";
            if (followSeconds < 0f || maxBendDeg < 5f || maxBendDeg > 60f) return $"{id}: followSeconds >= 0, maxBendDeg 5..60";
            return null;
        }

        public override IPartRuntime CreateRuntime(in PartCtx c)
            => c.Sim != null ? new WhipTailRuntime(this, c.Sim, c.Species.Layout.Start(c.Index), c.Size) : null;

        public float Radius(float t) => Mathf.Lerp(radiusRoot, radiusTip, Mathf.Pow(t, 0.8f));

        // The rest chain: straight back from the root (offset in the joint frame), points 0..Segments.
        public void RestPoints(Vector3 jointPos, Quaternion jointRot, float scale, Vector3[] into)
        {
            Vector3 root = jointPos + jointRot * (at.offset * scale);
            Vector3 back = jointRot * Vector3.back;
            float l = length / Segments * scale;
            for (int i = 0; i <= Segments; i++) into[i] = root + back * (l * i);
        }

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            CreatureSim rest = m.Rest;
            if (rest == null) return;
            int start = m.Species.Layout.Start(c.Index), N = Segments;
            var pts = new Vector3[N + 1];
            RestPoints(rest.Joints[at.joint], rest.Rotations[at.joint], 1f, pts);
            Vector3 fwd = rest.Rotations[at.joint] * Vector3.forward, up = rest.Rotations[at.joint] * Vector3.up;
            var q = new List<PartMeshEmitter.TubeSample>();
            const int sub = 2;
            float l = length / N;
            for (int i = 0; i < N; i++)
                for (int k = 0; k < sub; k++)
                {
                    float t = (i + k / (float)sub) / N;
                    Vector3 p = Vector3.Lerp(pts[i], pts[i + 1], k / (float)sub);
                    float r = Radius(t), dr = (Radius(Mathf.Min(1f, t + 0.01f)) - r) / (0.01f * length);
                    Vector4 skin = PartMeshEmitter.Blend(start + i, start + Mathf.Min(i + 1, N - 1), i + 1 < N ? k / (float)sub : 0f);
                    q.Add(PartMeshEmitter.Sample(p, -fwd, r, dr, t * length, skin));
                }
            // the fine point
            q.Add(PartMeshEmitter.Sample(pts[N], -fwd, 0f, 0f, length, PartMeshEmitter.Rigid(start + N - 1), true));
            m.Tube(q, up, Mathf.Clamp(sides, 4, 16), RayBody.PartRay);
        }
    }

    public sealed class WhipTailRuntime : IPartRuntime
    {
        const int kHistory = 2048;

        readonly WhipTail w;
        readonly CreatureSim sim;
        readonly int start, N, joint;
        readonly float scale, T, len, cosMax;
        readonly Vector3[] x, ups, hist;
        readonly float[] tau;                // follow time constant per point
        int head, count;
        bool started;

        public WhipTail Part => w;
        public Vector3[] Points => x;
        public int FirstBone => start;

        public WhipTailRuntime(WhipTail tail, CreatureSim sim, int firstBone, SizeCtx size)
        {
            w = tail; this.sim = sim; start = firstBone; N = tail.Segments; joint = tail.at.joint;
            scale = size.L; T = size.T; len = tail.length / N * scale;
            cosMax = Mathf.Cos(tail.maxBendDeg * Mathf.Deg2Rad);
            x = new Vector3[N + 1]; ups = new Vector3[N + 1]; hist = new Vector3[kHistory];
            tau = new float[N + 1];
            for (int i = 1; i <= N; i++) tau[i] = tail.followSeconds * T * i / N;
            tail.RestPoints(sim.Joints[joint], sim.Rotations[joint], scale, x);
        }

        public PartStage Stage => PartStage.AfterLimbs;

        void Push(Vector3 p)
        {
            head = (head + 1) % kHistory; hist[head] = p;
            if (count < kHistory) count++;
        }

        public void Step(in StepCtx c, float dt)
        {
            if (!(dt > 0f)) return;
            Quaternion jr = sim.Rotations[joint];
            Vector3 root = sim.Joints[joint] + jr * (w.at.offset * scale);
            Vector3 back = jr * Vector3.back, up = jr * Vector3.up;
            if (!started)
            {
                // the path so far: the tail as it lies (tip first, the root last)
                started = true;
                w.RestPoints(sim.Joints[joint], jr, scale, x);
                for (int i = N; i >= 1; i--) Push(x[i]);
            }
            // a new path point only once the root has moved on (a hovering root does not fill the history)
            if (count == 0 || (root - hist[head]).sqrMagnitude > 1e-8f * scale * scale) Push(root);
            else hist[head] = root;
            x[0] = root;
            // walk back along the path: point i heads for the path point i x len behind the root
            int idx = head, left = count - 1;
            Vector3 a = hist[idx];
            float acc = 0f;
            float sag = w.sagDeg * Mathf.Deg2Rad;
            for (int i = 1; i <= N; i++)
            {
                float want = i * len;
                Vector3 target;
                while (true)
                {
                    if (left <= 0) { target = (i >= 2 ? x[i - 1] + (x[i - 1] - x[i - 2]).normalized * len : root + back * len); break; }
                    int prev = (idx - 1 + kHistory) % kHistory;
                    Vector3 b = hist[prev];
                    float l = (b - a).magnitude;
                    if (acc + l >= want && l > 1e-9f) { target = Vector3.Lerp(a, b, (want - acc) / l); break; }
                    acc += l; a = b; idx = prev; left--;
                }
                // its own weight: a little lower the further down the tail
                target += Vector3.down * (sag * len * i * 0.5f);
                float k = tau[i] > 0f ? 1f - Mathf.Exp(-dt / tau[i]) : 1f;
                x[i] += (target - x[i]) * k;
            }
            // exact lengths and the bend cone, from the root
            for (int i = 1; i <= N; i++)
            {
                Vector3 d = x[i] - x[i - 1];
                float m = d.magnitude;
                d = m > 1e-9f ? d / m : back;
                Vector3 prevDir = i == 1 ? back : (x[i - 1] - x[i - 2]) / len;
                float c2 = Vector3.Dot(d, prevDir);
                if (c2 < cosMax)
                {
                    Vector3 perp = d - prevDir * c2;
                    float pm = perp.magnitude;
                    perp = pm > 1e-9f ? perp / pm : up;
                    d = prevDir * cosMax + perp * Mathf.Sqrt(Mathf.Max(0f, 1f - cosMax * cosMax));
                }
                x[i] = x[i - 1] + d * len;
            }
        }

        // Bone i at point i, facing the root (the spine's convention: forward = toward the head), its up carried down
        // the chain from the root joint's.
        public void WriteBones(Matrix4x4[] dst, int offset)
        {
            Vector3 s = Vector3.one * scale;
            Vector3 u = sim.Rotations[joint] * Vector3.up;
            for (int i = 0; i < N; i++)
            {
                Vector3 f = x[i] - x[i + 1];
                float m = f.magnitude;
                f = m > 1e-9f ? f / m : sim.Rotations[joint] * Vector3.forward;
                Vector3 uu = u - f * Vector3.Dot(u, f);
                float um = uu.magnitude;
                u = um > 1e-6f ? uu / um : Vector3.Cross(f, Vector3.right).normalized;
                ups[i] = u;
                dst[offset + start + i] = Matrix4x4.TRS(x[i], CreatureSim.Basis(f, u), s);
            }
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            float pad = w.radiusRoot * scale;
            for (int i = 0; i <= N; i++)
            {
                lo = Vector3.Min(lo, x[i] - new Vector3(pad, pad, pad));
                hi = Vector3.Max(hi, x[i] + new Vector3(pad, pad, pad));
            }
        }

        public void DrawDebug(ICreatureDebugDraw d)
        {
            var c = new Color(0.9f, 0.9f, 0.4f, 1f);
            for (int i = 0; i < N; i++) d.Line(x[i], x[i + 1], c);
        }
    }
}
