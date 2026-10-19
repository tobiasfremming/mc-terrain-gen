// Creature kit: SkinFolds - loose skin over a stretch of the spine: transverse folds (ridges running round the body,
// `folds` of them between at.joint and toJoint, drawn sharper by `sharpness`) on an angular window round the body
// (aroundDeg from the back: 180 = the throat / belly, 90 = the flank; mirror for a pair at +- the angle), and a
// sagging dewlap below (sag: loose skin hanging under the throat). A shell lofted over the body's own cross-sections
// like Swell (PartMeshEmitter.SpineAt), grown outward along the ellipse normal by the folds, fading into the body at
// both ends (inset), skinned to the spine as the body rings are (it bends and twists with the neck), shaded as the
// body (part 0: the pattern runs on across it). Kit-only part. Runs once per species mesh (may allocate).
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("SkinFolds", PartKind.Rigid, Bones = "0 (skinned to the spine)", Summary = "Loose skin over a stretch of the spine: transverse folds round the body on an angular window (throat, flanks) and a sagging dewlap below, fading into the body at both ends. Skinned to the spine like the body rings, shaded as the body.",
          System = "mesh only (SkinFolds.EmitMesh), skinned to the spine bones", MaterialLayers = new[] { 0 }, KitOnly = true,
          UseWhen = "neck and throat folds of monitor lizards, a dewlap, wrinkled elbows of skin, the gular folds of iguanas")]
    public sealed class SkinFolds : CreaturePart
    {
        [Param(ParamUnit.Joint, "last spine joint of the stretch (the first is at.joint)")] public int toJoint;
        [Param(ParamUnit.Count, "folds (ridges round the body) over the stretch", Min = 1, Max = 40)] public int folds = 5;
        [Param(ParamUnit.Metres, "fold height at the middle of the stretch", Example = "komodo throat 0.006")] public float height = 0.005f;
        [Param(ParamUnit.Fraction, "how sharp each fold is: 1 = a gentle wave, 4 = narrow creases between flat skin", Min = 1, Max = 8, Default = "2")] public float sharpness = 2f;
        [Param(ParamUnit.Degrees, "centre of the window round the body from the back: 180 the throat, 90 the flank (mirror: also at minus this)", Min = 0, Max = 180, Default = "180")] public float aroundDeg = 180f;
        [Param(ParamUnit.Degrees, "angular half width of the window", Min = 10, Max = 180, Default = "70")] public float widthDeg = 70f;
        [Param(ParamUnit.Metres, "loose skin sagging straight down at the middle of the stretch (a dewlap)")] public float sag;
        [Param(ParamUnit.Fraction, "where along the stretch the sag is deepest (0 = at.joint .. 1 = toJoint)", Min = 0.05f, Max = 0.95f, Default = "0.5")] public float sagAt = 0.5f;
        [Param(ParamUnit.Fraction, "both ends sink this fraction of the body's radius inside the body", Min = 0, Max = 0.5f, Default = "0.06")] public float inset = 0.06f;
        [Param(ParamUnit.Count, "mesh: rings per spine segment (0 = 8)", Min = 0, Max = 16)] public int ringsPerJoint;
        [Param(ParamUnit.Count, "mesh: vertices round each ring (0 = 32)", Min = 0, Max = 64)] public int around;

        const float kClear = 0.02f;
        const float kStep = 1e-3f;

        public override int BoneCount(in SpeciesCtx c) => 0;
        public override int MeshOrder => 100;
        public int Rings => Mathf.Max(1, toJoint - at.joint) * (ringsPerJoint > 0 ? ringsPerJoint : 8);
        public int Around => around > 0 ? Mathf.Max(8, around) : 32;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: SkinFolds need a SpineBody";
            if (at.joint < 0 || toJoint >= c.Joints || toJoint <= at.joint) return $"{id}: joints {at.joint}..{toJoint} must lie in 0..{c.Joints - 1}, first < last";
            if (folds < 1 || folds > 40) return $"{id}: folds must be in 1..40";
            if (height < 0f || sag < 0f || height + sag <= 0f) return $"{id}: height and sag must be >= 0 and not both 0";
            if (!(sharpness >= 1f && sharpness <= 8f)) return $"{id}: sharpness must be in 1..8";
            if (aroundDeg < 0f || aroundDeg > 180f || widthDeg < 10f || widthDeg > 180f) return $"{id}: aroundDeg 0..180, widthDeg 10..180";
            if (!(sagAt >= 0.05f && sagAt <= 0.95f) || inset < 0f || inset > 0.5f) return $"{id}: sagAt 0.05..0.95, inset 0..0.5";
            return null;
        }

        float T01(float u) => Mathf.Clamp01((u - at.joint) / Mathf.Max(1e-4f, toJoint - at.joint));

        // 0 at both ends, 1 inside (a smooth window over the first and last eighth)
        static float Fade(float t)
        {
            float q = Mathf.Clamp01(Mathf.Min(t, 1f - t) / 0.18f);
            return q * q * q * (q * (6f * q - 15f) + 10f);
        }

        // The extra round the body is separable: along the stretch (folds x fade, sag profile) times round the body
        // (the window, the belly's sag weight), so a mesh evaluates each factor once per ring / per angle.
        public struct Along { public float folds, sag, fade; }

        public Along AlongAt(float u)
        {
            float t = T01(u);
            float ridge = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(2f * Mathf.PI * folds * t), sharpness);
            float q = Mathf.Clamp01(t < sagAt ? t / sagAt : (1f - t) / (1f - sagAt));
            float f = Fade(t);
            return new Along { folds = f * height * ridge, sag = sag * q * q * (3f - 2f * q), fade = f };
        }

        // The window round the body at ring angle phi (-pi/2 the belly, +pi/2 the back): the signed angle from the back
        // (+ toward +x, the right; +-180 the belly); a sided part (mirror) sits on its own side at aroundDeg, a midline
        // one at +- aroundDeg.
        public float WindowAt(float phi, float side)
        {
            float ang = Mathf.Atan2(Mathf.Cos(phi), Mathf.Sin(phi)) * Mathf.Rad2Deg;
            float a = at.side != 0 ? ang * side : Mathf.Abs(ang);
            float d = Mathf.DeltaAngle(a, aroundDeg) / widthDeg;
            return Mathf.Exp(-2f * d * d);
        }

        public static float SagWeightAt(float phi) => Mathf.Pow(Mathf.Max(0f, -Mathf.Sin(phi)), 3f);

        // The extra round the body at spine coordinate u and ring angle phi.
        public float Extra(float u, float phi, float side)
        {
            Along al = AlongAt(u);
            return al.folds * WindowAt(phi, side) + al.sag * SagWeightAt(phi);
        }

        Vector3 Point(in SpineSection s, in Along al, float cs, float sn, float win, float sagW)
        {
            Vector3 onBody = s.c + s.x * (s.a * cs) + s.y * (s.b * sn);
            Vector3 n = PartMeshEmitter.Unit(s.x * (cs / s.a) + s.y * (sn / s.b), s.y);
            float r = Mathf.Sqrt(s.a * s.b);
            float off = al.folds * win + al.sag * sagW + al.fade * (inset + kClear) * r - inset * r;
            return onBody + n * off;
        }

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            CreatureSpecies sp = m.Species;
            int rings = Rings, segs = Around, joints = m.Joints;
            float side = at.side < 0 ? -1f : 1f;
            float headLen = Mathf.Max(sp.headCapLength, 1e-3f);
            int first = m.VertexCount;
            // per angle (k, and k -+ the step): cos, sin, window, sag weight
            var cs = new float[3 * (segs + 1)]; var sn = new float[cs.Length]; var win = new float[cs.Length]; var sw = new float[cs.Length];
            for (int k = 0; k <= segs; k++)
            {
                float phi = k == segs ? 1.5f * Mathf.PI : -0.5f * Mathf.PI + 2f * Mathf.PI * k / segs;
                for (int d = 0; d < 3; d++)
                {
                    float ph = phi + (d - 1) * kStep;
                    int i = 3 * k + d;
                    cs[i] = Mathf.Cos(ph); sn[i] = Mathf.Sin(ph); win[i] = WindowAt(ph, side); sw[i] = SagWeightAt(ph);
                }
            }
            var centre = new Vector3[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float u = Mathf.Lerp(at.joint, toJoint, i / (float)rings);
                float u0 = Mathf.Max(0f, u - kStep), u1 = Mathf.Min(joints - 1, u + kStep);
                SpineSection s = m.SpineAt(u), s0 = m.SpineAt(u0), s1 = m.SpineAt(u1);
                Along al = AlongAt(u), al0 = AlongAt(u0), al1 = AlongAt(u1);
                centre[i] = s.c;
                Vector4 skin = FinStripMesh.SpineSkin(u, joints);
                float arc = headLen + sp.spacing * u;
                for (int k = 0; k <= segs; k++)
                {
                    int q = 3 * k + 1;
                    Vector3 p = Point(s, al, cs[q], sn[q], win[q], sw[q]);
                    Vector3 dPhi = Point(s, al, cs[q + 1], sn[q + 1], win[q + 1], sw[q + 1]) - Point(s, al, cs[q - 1], sn[q - 1], win[q - 1], sw[q - 1]);
                    Vector3 dU = Point(s1, al1, cs[q], sn[q], win[q], sw[q]) - Point(s0, al0, cs[q], sn[q], win[q], sw[q]);
                    Vector3 outward = s.x * cs[q] + s.y * sn[q];
                    Vector3 nn = PartMeshEmitter.Unit(Vector3.Cross(dU, dPhi), outward);
                    if (Vector3.Dot(nn, outward) < 0f) nn = -nn;
                    m.Vertex(p, nn, arc, (float)k / segs, PartMeshEmitter.PartBody, sn[q], skin);
                }
            }
            for (int i = 0; i < rings; i++)
            {
                Vector3 mc = 0.5f * (centre[i] + centre[i + 1]);
                for (int k = 0; k < segs; k++)
                {
                    int a = first + i * (segs + 1) + k, b = a + 1, d = a + segs + 1, e = d + 1;
                    Vector3 pa = m.Position(a), pb = m.Position(b), pd = m.Position(d);
                    bool flip = Vector3.Dot(Vector3.Cross(pb - pa, pd - pa), (pa + pb + pd) / 3f - mc) < 0f;
                    m.Tri(a, b, d, flip);
                    m.Tri(b, e, d, flip);
                }
            }
        }
    }
}
