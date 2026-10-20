// Creature kit (sky manta): CephalicLobe - a manta's head fin: a flap rolled up along its length into a short horn
// (the curl tightening toward the tip) that juts forward from a corner of the head. Two bones: the base sways slowly
// (its own breathing rhythm, the pair out of step) and leans out of the body's turns; the tip FOLLOWS the base
// (a damped spring on its angles, followHz / damping) - so the lobe flicks after every change, the chain rule in
// small. Mesh: the rolled sheet with real thickness, a closed rounded tip, skinned base -> tip along its length.
// Kit-only. Runtime stage Late (once per tick); allocation-free after construction.
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("CephalicLobe", PartKind.Fin, Bones = "2 (base, tip)", Summary = "A head fin rolled up into a short horn (manta cephalic lobes): a thick sheet curled about its length, tighter toward the tip, jutting forward from the head. The base sways slowly and leans out of turns; the tip follows the base with lag (damped spring).",
          System = "CephalicLobeRuntime (part runtime, stage Late) + CephalicLobe.EmitMesh", MaterialLayers = new[] { 15 }, KitOnly = true,
          UseWhen = "manta and devil ray head fins, rolled ear flaps, curled horn-like frills, a squid's rolled arms at rest")]
    public sealed class CephalicLobe : CreaturePart
    {
        [Param(ParamUnit.Metres, "length, base to tip", Example = "manta 0.22")] public float length = 0.22f;
        [Param(ParamUnit.Metres, "unrolled width at the base")] public float width = 0.09f;
        [Param(ParamUnit.Metres, "sheet thickness at the base")] public float thickness = 0.016f;
        [Param(ParamUnit.Degrees, "curl along its length: the tip turns down (and in, half as much) by this much", Min = 0, Max = 180)] public float curlDeg = 60f;
        [Param(ParamUnit.Degrees, "how far the flap is rolled up at the tip (0 flat, 360 a round horn)", Min = 0, Max = 360)] public float tipCurlDeg = 260f;
        [Param(ParamUnit.Degrees, "points down from the head's forward", Min = -60, Max = 80)] public float pitchDeg = 20f;
        [Param(ParamUnit.Degrees, "splays out to the side", Min = -45, Max = 45)] public float splayDeg = 8f;
        [Param(ParamUnit.Degrees, "the base's slow sway amplitude", Min = 0, Max = 30)] public float swayDeg = 5f;
        [Param(ParamUnit.Hz, "the base's sway frequency", Example = "manta 0.25")] public float swayHz = 0.25f;
        [Param(ParamUnit.Fraction, "lean out of turns: degrees per deg/s of the body's yaw rate", Min = 0, Max = 1)] public float turnLean = 0.12f;
        [Param(ParamUnit.Hz, "the tip follows the base at this natural frequency", Example = "manta 2.5")] public float followHz = 2.5f;
        [Param(ParamUnit.Fraction, "damping ratio of the tip's follow", Min = 0.1f, Max = 2)] public float damping = 0.4f;

        public int Side => at.side < 0 ? -1 : 1;
        public override int BoneCount(in SpeciesCtx c) => 2;
        public override int MeshOrder => 100;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a CephalicLobe needs a spine body";
            if (at.joint < 0 || at.joint >= c.Joints) return $"{id}: joint {at.joint} outside 0..{c.Joints - 1}";
            if (!mirror && at.side != -1 && at.side != 1) return $"{id}: side must be -1 or +1 (or mirror)";
            if (!(length > 0f) || !(width > 0f) || !(thickness > 0f)) return $"{id}: length, width and thickness > 0";
            if (!(followHz > 0f)) return $"{id}: followHz > 0";
            return null;
        }

        public override IPartRuntime CreateRuntime(in PartCtx c)
            => c.Sim != null ? new CephalicLobeRuntime(this, c.Sim, c.Species.Layout.Start(c.Index), c.Size) : null;

        // The lobe's rest rotation in its joint's frame (local +z along the lobe).
        public Quaternion RestRotation(float yawDeg = 0f, float pitchAddDeg = 0f)
            => Quaternion.AngleAxis(Side * (splayDeg + yawDeg), Vector3.up) * Quaternion.AngleAxis(pitchDeg + pitchAddDeg, Vector3.right);

        const int kLen = 10, kAround = 12;

        // The lobe's centre line and its section at t (0 base .. 1 tip), in the lobe's own frame at scale 1: it curls
        // down by curlDeg along its length and in toward the midline by half that; its flat section (width x thickness)
        // narrows and thickens into a round horn toward the tip (tipCurlDeg: how far it is rolled up there, 0 flat ..
        // 360 a full horn) and closes in a rounded tip.
        public void Section(float t, out Vector3 c, out Vector3 d, out Vector3 x, out Vector3 y, out float a, out float b)
        {
            const int steps = 24;
            float curl = curlDeg * Mathf.Deg2Rad, inward = 0.5f * curl;
            c = Vector3.zero; d = Vector3.forward;
            int n = Mathf.RoundToInt(t * steps);
            for (int i = 0; i <= n; i++)
            {
                float u = i / (float)steps;
                d = Quaternion.AngleAxis(-Side * inward * u * Mathf.Rad2Deg, Vector3.up) * (Quaternion.AngleAxis(curl * u * Mathf.Rad2Deg, Vector3.right) * Vector3.forward);
                if (i < n) c += d * (length / steps);
            }
            float rest = t * steps - n;
            if (rest > 0f) c += d * (length / steps * rest);
            x = Vector3.Cross(Vector3.up, d).normalized;
            if (x.sqrMagnitude < 0.5f) x = Vector3.right;
            y = Vector3.Cross(d, x).normalized;
            float tip = Mathf.Clamp01((t - 0.7f) / 0.3f), round = Mathf.Sqrt(Mathf.Max(0f, 1f - tip * tip));
            float roll = Mathf.Lerp(curlDeg > 0f ? 0.2f : 0f, Mathf.Clamp01(tipCurlDeg / 360f), t);   // 0 flat .. 1 round
            float w = 0.5f * width * Mathf.Lerp(1f, 0.45f, t);
            float th = 0.5f * thickness;
            a = Mathf.Max(1e-4f, Mathf.Lerp(w, Mathf.Sqrt(w * th) * 1.2f, roll) * round);
            b = Mathf.Max(1e-4f, Mathf.Lerp(th, Mathf.Sqrt(w * th) * 1.2f, roll) * round);
        }

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            CreatureSim rest = m.Rest;
            if (rest == null) return;
            int start = m.Species.Layout.Start(c.Index), side = Side;
            Quaternion jr = rest.Rotations[at.joint];
            Vector3 off = at.offset; off.x *= side;
            Quaternion q = jr * RestRotation();
            Vector3 basePt = rest.Joints[at.joint] + jr * off;
            int first = m.VertexCount, M = kAround + 1;
            for (int i = 0; i <= kLen; i++)
            {
                float t = i / (float)kLen;
                Section(t, out Vector3 cl, out Vector3 dl, out Vector3 xl, out Vector3 yl, out float a, out float b);
                Vector4 skin = PartMeshEmitter.Blend(start, start + 1, t);
                for (int k = 0; k <= kAround; k++)
                {
                    float phi = 2f * Mathf.PI * (k % kAround) / kAround;
                    float cs = Mathf.Cos(phi), sn = Mathf.Sin(phi);
                    Vector3 pl = cl + xl * (a * cs) + yl * (b * sn);
                    Vector3 nl = (xl * (cs / a) + yl * (sn / b)).normalized;
                    Vector3 wn = q * nl;
                    m.Vertex(basePt + q * pl, wn, t * length, 0f, RayBody.PartRay, Mathf.Clamp((Quaternion.Inverse(jr) * wn).y, -1f, 1f), skin);
                }
            }
            for (int i = 0; i < kLen; i++)
                for (int k = 0; k < kAround; k++)
                    RayBody.Quad(m, first + i * M + k, first + (i + 1) * M + k, first + (i + 1) * M + k + 1, first + i * M + k + 1);
            // the rounded tip: a fan to a point just past the last ring
            Section(1f, out Vector3 ce, out Vector3 de, out _, out _, out _, out _);
            int pole = m.VertexCount;
            Vector3 axis = q * de;
            m.Vertex(basePt + q * (ce + de * (0.03f * length)), axis, length, 0f, RayBody.PartRay, 0f, PartMeshEmitter.Rigid(start + 1));
            int last = first + kLen * M;
            for (int k = 0; k < kAround; k++)
            {
                Vector3 fa = m.Position(last + k), fb = m.Position(last + k + 1), fp = m.Position(pole);
                bool flip = Vector3.Dot(Vector3.Cross(fb - fa, fp - fa), axis) < 0f;
                m.Tri(last + k, last + k + 1, pole, flip);
            }
        }
    }

    public sealed class CephalicLobeRuntime : IPartRuntime
    {
        readonly CephalicLobe l;
        readonly CreatureSim sim;
        readonly int start, side;
        readonly float scale, T, omega, phase0;
        float yaw, pitch, tipYaw, tipPitch, tipYawV, tipPitchV, lastHeading;
        bool started;
        float time;

        public CephalicLobe Part => l;
        public float BaseYaw => yaw;
        public float TipYaw => tipYaw;

        public CephalicLobeRuntime(CephalicLobe lobe, CreatureSim sim, int firstBone, SizeCtx size)
        {
            l = lobe; this.sim = sim; start = firstBone; side = lobe.Side;
            scale = size.L; T = size.T;
            omega = 2f * Mathf.PI * lobe.followHz / T;
            phase0 = side > 0 ? 0f : 2.1f;   // the pair out of step
        }

        public PartStage Stage => PartStage.Late;

        public void Step(in StepCtx c, float dt)
        {
            if (!(dt > 0f)) return;
            time += dt;
            Vector3 fwd = sim.Rotations[l.at.joint] * Vector3.forward;
            float heading = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            if (!started) { started = true; lastHeading = heading; }
            float yawRate = Mathf.DeltaAngle(lastHeading, heading) / dt;
            lastHeading = heading;
            float sway = l.swayDeg * Mathf.Sin(2f * Mathf.PI * l.swayHz / T * time + phase0);
            // the base: sway, and out of the turn (a turn to the right swings the lobes left, as a trailing flap does)
            yaw = sway - Mathf.Clamp(yawRate * T * l.turnLean, -25f, 25f) * side;
            pitch = 0.6f * sway;
            // the tip follows the base
            float ay = omega * omega * (yaw - tipYaw) - 2f * l.damping * omega * tipYawV;
            float ap = omega * omega * (pitch - tipPitch) - 2f * l.damping * omega * tipPitchV;
            tipYawV += ay * dt; tipPitchV += ap * dt;
            tipYaw += tipYawV * dt; tipPitch += tipPitchV * dt;
        }

        public void WriteBones(Matrix4x4[] dst, int offset)
        {
            int j = l.at.joint;
            Quaternion jr = sim.Rotations[j];
            Vector3 off = l.at.offset; off.x *= side;
            Vector3 basePt = sim.Joints[j] + jr * (off * scale);
            Quaternion rest = l.RestRotation();
            Quaternion qb = jr * l.RestRotation(yaw, pitch);
            Quaternion qt = jr * l.RestRotation(tipYaw, tipPitch);
            Vector3 s = Vector3.one * scale;
            // bind origins: the base point for both bones (the tip bone rotates about the lobe's middle)
            dst[offset + start] = Matrix4x4.TRS(basePt, qb * Quaternion.Inverse(rest), s);
            Vector3 mid = basePt + qb * new Vector3(0f, 0f, 0.5f * l.length * scale);
            Quaternion rt = qt * Quaternion.Inverse(rest);
            Vector3 restMid = rest * new Vector3(0f, 0f, 0.5f * l.length * scale);
            dst[offset + start + 1] = Matrix4x4.TRS(mid - rt * restMid, rt, s);
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi) { }   // within the disc's reach (the head's)

        public void DrawDebug(ICreatureDebugDraw d) { }
    }
}
