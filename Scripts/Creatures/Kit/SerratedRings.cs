// Creature kit (2026-10-20, the Delver): SerratedRings - segmented armour round a tail (or any tapering run of the
// spine): one ring per joint from at.joint to toJoint, rigid to its joint, wrapping over the top and down the flanks
// (skirtDeg past the equator: 90 = a closed ring but for a slit underneath), shingled front over back like BandShell's
// bands (ShellBand), and on each flank a flat serrated blade - a saw tooth pointing out and back (bladeSweepDeg) from
// the ring's rear half, long by bladeLength x the ring's half-width, tapering along the run (bladeEnd). Blades shingle
// too: each blade's front edge sits bladeStep lower than its rear, so on the inside of a bend the next blade slides
// under it instead of through it. An optional dorsal tooth per ring (dorsalTooth) makes the top edge saw-toothed.
// Material layer `layer` (10, armour plate; restyle with Layers). Mesh only: every ring and blade rides its joint.
// Kit-only; off unless a species names it.
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("SerratedRings", PartKind.Rigid, Bones = "0 (each ring rigid to its joint)", Summary = "Segmented ring armour along a run of the spine (a tail): one shingled ring per joint wrapping the top and flanks, each with a flat saw-tooth blade on either flank pointing out and back (and an optional dorsal tooth), blades shingled so a bend slides them under each other.",
          System = "mesh only (SerratedRings.EmitMesh, ShellBand)", MaterialLayers = new[] { 10 }, KitOnly = true,
          UseWhen = "armoured tails: glyptodont and ankylosaur tail rings, trilobite pygidia, girdled lizards, serrated or spiked tails, segmented sea-creature tails")]
    public sealed class SerratedRings : CreaturePart, IShellBands
    {
        [Param(ParamUnit.Joint, "last joint with a ring (the first is at.joint)")] public int toJoint;
        [Param(ParamUnit.Metres, "the rings' stand-off from the body")] public float lift = 0.01f;
        [Param(ParamUnit.Degrees, "how far each ring wraps under past the equator (90 = all but a slit)", Min = -30, Max = 85, Default = "40")] public float skirtDeg = 40f;
        [Param(ParamUnit.Metres, "ring thickness")] public float thickness = 0.005f;
        [Param(ParamUnit.Metres, "shingle step (rear proud, front tucked), > thickness / 2")] public float shingle = 0.008f;
        [Param(ParamUnit.Fraction, "overlap of neighbouring rings, fraction of the joint spacing", Min = 0, Max = 1, Default = "0.4")] public float overlap = 0.4f;
        [Param(ParamUnit.Metres, "rolled lip along each rear edge")] public float lip = 0.002f;
        [Param(ParamUnit.Fraction, "section squareness: 2 round .. 4 boxy", Min = 2, Max = 4, Default = "2.4")] public float square = 2.4f;
        [Param(ParamUnit.Fraction, "blade length (out from the ring), x the ring's half-width at the first ring", Min = 0, Max = 3, Default = "0.7")] public float bladeLength = 0.7f;
        [Param(ParamUnit.Fraction, "blade length at the last ring, x the first's", Min = 0, Max = 2, Default = "0.6")] public float bladeEnd = 0.6f;
        [Param(ParamUnit.Fraction, "blade base, fraction of the ring's length (from its rear edge forward)", Min = 0.2f, Max = 1, Default = "0.6")] public float bladeBase = 0.6f;
        [Param(ParamUnit.Degrees, "blade sweep back from straight out", Min = 0, Max = 75, Default = "30")] public float bladeSweepDeg = 30f;
        [Param(ParamUnit.Degrees, "where round the ring the blade sits: 0 = the equator, + higher up the flank", Min = -40, Max = 60, Default = "0")] public float bladeAtDeg;
        [Param(ParamUnit.Metres, "blade thickness")] public float bladeThick = 0.004f;
        [Param(ParamUnit.Metres, "blade shingle: its front edge this much lower than its rear (> bladeThick / 2)")] public float bladeStep = 0.004f;
        [Param(ParamUnit.Fraction, "dorsal tooth height, x the blade length (0 = none)", Min = 0, Max = 1)] public float dorsalTooth;
        [Param(ParamUnit.Count, "mesh: vertices across each ring, 0 = 28", Min = 0, Max = 96)] public int around;
        [Param(ParamUnit.Count, "material layer: 10 armour plate (restyle with Layers)", Min = 0, Max = 15, Default = "10")] public int layer = 10;

        public override int BoneCount(in SpeciesCtx c) => 0;
        public int Rings => toJoint - at.joint + 1;
        public int BandCount => Rings;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: SerratedRings need a SpineBody";
            if (at.joint < 1 || toJoint >= c.Joints - 1 || toJoint < at.joint) return $"{id}: joints {at.joint}..{toJoint} must lie in 1..{c.Joints - 2}, first <= last";
            if (lift < 0f || lip < 0f || !(thickness > 0f)) return $"{id}: lift, lip >= 0, thickness > 0";
            if (!(shingle > 0.5f * thickness)) return $"{id}: shingle must exceed half the thickness";
            if (skirtDeg < -30f || skirtDeg > 85f || overlap < 0f || overlap > 1f || square < 2f || square > 4f) return $"{id}: skirtDeg -30..85, overlap 0..1, square 2..4";
            if (bladeLength < 0f || bladeEnd < 0f || bladeBase < 0.2f || bladeBase > 1f || bladeSweepDeg < 0f || bladeSweepDeg > 75f || bladeAtDeg < -40f || bladeAtDeg > 60f)
                return $"{id}: bladeLength, bladeEnd >= 0, bladeBase 0.2..1, bladeSweepDeg 0..75, bladeAtDeg -40..60";
            if (bladeLength > 0f && (!(bladeThick > 0f) || !(bladeStep > 0.5f * bladeThick))) return $"{id}: bladeThick > 0 and bladeStep > bladeThick / 2";
            if (dorsalTooth < 0f || dorsalTooth > 1f) return $"{id}: dorsalTooth 0..1";
            if (layer < 0 || layer >= PartLayers.MaxParts || (layer >= 4 && layer <= 7)) return $"{id}: layer must be a spine-shader part id (0..3, 8..15)";
            return null;
        }

        float W(CreatureSpecies sp, float u) => Profile(sp, u, true) + lift;
        float H(CreatureSpecies sp, float u) => Profile(sp, u, false) + lift;

        static float Profile(CreatureSpecies sp, float u, bool width)
        {
            int n = sp.JointCount;
            int i = Mathf.FloorToInt(u);
            float t = u - i;
            float V(int j) { SpineJoint q = sp.joints[Mathf.Clamp(j, 0, n - 1)]; return width ? q.halfWidth : q.halfHeight; }
            return Mathf.Max(1e-4f, PartMeshEmitter.CR(V(i - 1), V(i), V(i + 1), V(i + 2), t));
        }

        public ShellBand BandAt(CreatureSpecies sp, CreatureSim rest, int k, out int joint, out Matrix4x4 local)
            => Ring(sp, k, BandShell.Spacing(rest, at.joint + k), out joint, out local);

        public ShellBand Ring(CreatureSpecies sp, int k, float L, out int joint, out Matrix4x4 local)
        {
            joint = at.joint + k; local = Matrix4x4.identity;
            var b = new ShellBand
            {
                lo = -skirtDeg * Mathf.Deg2Rad, hi = Mathf.PI + skirtDeg * Mathf.Deg2Rad, yc = 0f,
                thick = thickness, shingle = shingle, lip = lip, square = square,
                zFront = 0.5f * L * (1f + overlap), zRear = -0.5f * L * (1f + overlap),
                A = W(sp, joint), B = H(sp, joint),
            };
            float zf = b.zFront / L, zr = b.zRear / L;
            b.aFront = W(sp, joint - zf); b.aRear = W(sp, joint - zr);
            b.bFront = H(sp, joint - zf); b.bRear = H(sp, joint - zr);
            return b;
        }

        // ---------------------------------------------------------------- blades

        // Blade of ring k on `side` (+1 right, -1 left), in the ring's joint frame: base front B0, base rear B1, apex P,
        // plane normal n (up side), the shingle offset along n at B0 (-step) and B1 (+step).
        public struct Blade { public Vector3 b0, b1, apex, n, e1; public float len, step, thick; }

        public float BladeLengthAt(CreatureSpecies sp, int k)
        {
            float f = Rings <= 1 ? 0f : (float)k / (Rings - 1);
            return bladeLength * W(sp, at.joint) * Mathf.Lerp(1f, bladeEnd, f);
        }

        public Blade BladeOf(CreatureSpecies sp, ShellBand ring, int k, int side)
        {
            float t = bladeAtDeg * Mathf.Deg2Rad;
            if (side < 0) t = Mathf.PI - t;
            float u0 = 1f - bladeBase;
            Vector3 b0 = ring.Surface(t, u0, 0.5f), b1 = ring.Surface(t, 1f, 0.5f);
            Vector3 outDir = ring.Normal(t, 0.9f, 0f); outDir.z = 0f; outDir = PartMeshEmitter.Unit(outDir, new Vector3(side, 0f, 0f));
            float sweep = bladeSweepDeg * Mathf.Deg2Rad;
            float len = BladeLengthAt(sp, k);
            Vector3 apex = b1 + (outDir * Mathf.Cos(sweep) - Vector3.forward * Mathf.Sin(sweep)) * len;
            Vector3 e1 = PartMeshEmitter.Unit(b1 - b0, Vector3.back);
            Vector3 n = Vector3.Cross(e1, apex - b0);
            if (n.y < 0f) n = -n;
            n = PartMeshEmitter.Unit(n, Vector3.up);
            return new Blade { b0 = b0, b1 = b1, apex = apex, n = n, e1 = e1, len = len, step = bladeStep, thick = bladeThick };
        }

        // The blade surface's offset along n at a point's position along the base (front -step .. rear +step).
        static float Lift(in Blade b, Vector3 p)
        {
            float s = Vector3.Dot(p - b.b0, b.e1) / Mathf.Max(1e-6f, (b.b1 - b.b0).magnitude);
            return b.step * (2f * Mathf.Clamp01(s) - 1f);
        }

        // A point on the blade's top (+1) or bottom (-1) face at barycentric (w0 b0, w1 b1, w2 apex).
        public static Vector3 BladePoint(in Blade b, float w0, float w1, float w2, int face)
        {
            Vector3 p = b.b0 * w0 + b.b1 * w1 + b.apex * w2;
            return p + b.n * (Lift(b, p) + face * 0.5f * b.thick);
        }

        // How far `p` (blade frame) lies under the blade's bottom face (> 0 under, < 0 through it); +infinity outside
        // the blade's plan triangle.
        public static float BladeClearance(in Blade b, Vector3 p)
        {
            Vector3 q = p - b.n * Vector3.Dot(p - b.b0, b.n);
            // barycentric of q in (b0, b1, apex)
            Vector3 v0 = b.b1 - b.b0, v1 = b.apex - b.b0, v2 = q - b.b0;
            float d00 = Vector3.Dot(v0, v0), d01 = Vector3.Dot(v0, v1), d11 = Vector3.Dot(v1, v1), d20 = Vector3.Dot(v2, v0), d21 = Vector3.Dot(v2, v1);
            float den = d00 * d11 - d01 * d01;
            if (Mathf.Abs(den) < 1e-14f) return float.PositiveInfinity;
            float v = (d11 * d20 - d01 * d21) / den, w = (d00 * d21 - d01 * d20) / den;
            if (v < 0f || w < 0f || v + w > 1f) return float.PositiveInfinity;
            float h = Vector3.Dot(p - b.b0, b.n);
            return (Lift(b, q) - 0.5f * b.thick) - h;
        }

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            CreatureSpecies sp = m.Species;
            int na = around > 0 ? around : 28;
            float arc = 0f;
            for (int k = 0; k < Rings; k++)
            {
                float L = BandShell.Spacing(m.Rest, at.joint + k);
                ShellBand ring = Ring(sp, k, L, out int j, out _);
                Matrix4x4 frame = Matrix4x4.TRS(m.Rest.Joints[j], m.Rest.Rotations[j], Vector3.one);
                Vector4 skin = PartMeshEmitter.Rigid(j);
                ring.Emit(m, frame, skin, layer, na, 6, arc);
                if (BladeLengthAt(sp, k) > 1e-5f)
                    for (int side = -1; side <= 1; side += 2) EmitBlade(m, frame, skin, BladeOf(sp, ring, k, side), arc);
                if (dorsalTooth > 0f) EmitDorsal(m, frame, skin, ring, BladeLengthAt(sp, k) * dorsalTooth, arc);
                arc += L;
            }
        }

        // A flat slab: top and bottom faces (one triangle each, hard normals) and the three sides.
        void EmitBlade(PartMeshEmitter m, Matrix4x4 frame, Vector4 skin, Blade b, float arc)
        {
            Vector3 T(int i) => BladePoint(b, i == 0 ? 1f : 0f, i == 1 ? 1f : 0f, i == 2 ? 1f : 0f, 1);
            Vector3 Bt(int i) => BladePoint(b, i == 0 ? 1f : 0f, i == 1 ? 1f : 0f, i == 2 ? 1f : 0f, -1);
            Face(m, frame, skin, T(0), T(1), T(2), b.n, arc);
            Face(m, frame, skin, Bt(0), Bt(2), Bt(1), -b.n, arc);
            for (int e = 0; e < 3; e++)
            {
                int a = e, d = (e + 1) % 3;
                Vector3 mid = 0.5f * (T(a) + T(d)), centre = (T(0) + T(1) + T(2)) / 3f;
                Vector3 sn = PartMeshEmitter.Unit(Vector3.ProjectOnPlane(mid - centre, b.n), Vector3.right);
                Face(m, frame, skin, T(a), Bt(a), T(d), sn, arc);
                Face(m, frame, skin, Bt(a), Bt(d), T(d), sn, arc);
            }
        }

        // A low ridge tooth on top: a three-sided pyramid leaning back over the ring's rear half.
        void EmitDorsal(PartMeshEmitter m, Matrix4x4 frame, Vector4 skin, ShellBand ring, float h, float arc)
        {
            float t = 0.5f * Mathf.PI;
            Vector3 f = ring.Surface(t, 0.45f, 0.3f), r = ring.Surface(t, 1f, 0.3f);
            Vector3 l = ring.Surface(t + 0.18f, 0.8f, 0.3f), rr = ring.Surface(t - 0.18f, 0.8f, 0.3f);
            Vector3 tip = r + Vector3.up * h + Vector3.back * (0.4f * h);
            Face(m, frame, skin, f, tip, rr, Vector3.Cross(tip - f, rr - f), arc);
            Face(m, frame, skin, f, l, tip, Vector3.Cross(l - f, tip - f), arc);
            Face(m, frame, skin, rr, tip, l, Vector3.Cross(tip - rr, l - rr), arc);
        }

        void Face(PartMeshEmitter m, Matrix4x4 frame, Vector4 skin, Vector3 a, Vector3 b, Vector3 c, Vector3 n, float arc)
        {
            Vector3 wn = PartMeshEmitter.Unit(frame.MultiplyVector(n), Vector3.up);
            int i = m.VertexCount;
            m.Vertex(frame.MultiplyPoint3x4(a), wn, arc, 0f, layer, wn.y, skin);
            m.Vertex(frame.MultiplyPoint3x4(b), wn, arc, 0.5f, layer, wn.y, skin);
            m.Vertex(frame.MultiplyPoint3x4(c), wn, arc, 1f, layer, wn.y, skin);
            Vector3 fn = Vector3.Cross(m.Position(i + 1) - m.Position(i), m.Position(i + 2) - m.Position(i));
            m.Tri(i, i + 1, i + 2, Vector3.Dot(fn, wn) < 0f);
        }
    }
}
