// Creature kit: Chela - a heavy crab claw on the last segment (the propodus) of an ArthropodLeg: a bulbous, laterally
// flattened palm, a fixed finger (pollex) continuing its lower edge and a movable finger (dactyl) hinged above it, held
// open by openDeg, both curling toward each other, with blunt teeth along their biting edges (the first one a big
// molar on a crusher) and a dark tip over tipFraction of their length; optional spines on the palm's outer and upper
// faces (a king crab's spiny claw). A crusher and a cutter are two Chelae of different sizes on two unmirrored limbs.
// Mesh only, no bones of its own: every vertex is rigid to the leg's last bone, so the claw follows the leg - walking,
// posed as a feeler, folded by a Tuck - and can never deform or invert. Built in the claw's own frame: +z along the
// propodus, +y the world's up made perpendicular at rest (the palm's tall axis), +x outward (mirrored by the limb's
// side: the pair is mirror-symmetric); rolled about +z by rollDeg (+ turns the top outward) and the fingers pitched down
// by pitchDeg. Material layers: `layer` (palm and fingers; 1 = the limbs'), `tipLayer` (finger tips and teeth; 2 = the
// claw row), `spineLayer`. Kit-only part; mirror expands into .L / .R naming the limb's .L / .R.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("Chela", PartKind.Rigid, Bones = "0 (rigid to its ArthropodLeg's last bone)", Summary = "A heavy crab claw on an ArthropodLeg's last segment: bulbous flattened palm, fixed finger and movable finger (held open by openDeg) curling together, blunt teeth (a big molar on a crusher), dark tips, optional palm spines. Rigid to the leg's last bone: it walks, poses and folds (Tuck) with the limb.",
          System = "mesh only (Chela.EmitMesh), rigid to the leg's last bone", MaterialLayers = new[] { 1, 2 }, KitOnly = true,
          UseWhen = "crab and lobster claws (crusher and cutter), scorpion pincers: any heavy chela on an arthropod limb")]
    public sealed class Chela : CreaturePart
    {
        [Param(ParamUnit.PartRef, "the ArthropodLeg whose last segment carries the claw; a mirrored chela names the unmirrored id and takes .L / .R")] public string leg = "";
        [Param(ParamUnit.Metres, "palm length along the propodus (from its joint)", Example = "stone crab crusher 0.11")] public float palmLength;
        [Param(ParamUnit.Metres, "palm half height at its widest (the tall axis)", Example = "stone crab crusher 0.05")] public float palmHeight;
        [Param(ParamUnit.Metres, "palm half thickness at its widest", Example = "stone crab crusher 0.034")] public float palmWidth;
        [Param(ParamUnit.Fraction, "the wrist (palm start) half sizes, fraction of the widest", Min = 0.2f, Max = 1f, Default = "0.55")] public float wrist = 0.55f;
        [Param(ParamUnit.Fraction, "superellipse exponent of the palm's section: 2 = oval, 3-4 = boxy", Min = 1.5f, Max = 6f, Default = "2.2")] public float square = 2.2f;
        [Param(ParamUnit.Metres, "finger length past the palm's end", Example = "stone crab crusher 0.07")] public float fingerLength;
        [Param(ParamUnit.Metres, "finger half height at its root", Example = "stone crab crusher 0.02")] public float fingerHeight;
        [Param(ParamUnit.Fraction, "finger half thickness, fraction of its half height", Min = 0.3f, Max = 1.5f, Default = "0.8")] public float fingerWidth = 0.8f;
        [Param(ParamUnit.Fraction, "the movable finger's length, x the fixed finger's", Min = 0.6f, Max = 1.5f, Default = "1.08")] public float dactylScale = 1.08f;
        [Param(ParamUnit.Degrees, "the movable finger held open by this much", Min = 0, Max = 60, Default = "10")] public float openDeg = 10f;
        [Param(ParamUnit.Degrees, "each finger curls toward the other by this much over its length", Min = 0, Max = 80, Default = "22")] public float curveDeg = 22f;
        [Param(ParamUnit.Degrees, "the fingers pitched down from the palm's axis", Min = -40, Max = 40)] public float pitchDeg;
        [Param(ParamUnit.Degrees, "the claw rolled about the propodus (mirrored by side; + turns the top outward)", Min = -180, Max = 180)] public float rollDeg;
        [Param(ParamUnit.Degrees, "the claw turned about the up axis at its wrist (mirrored by side; + points the fingers toward the midline): claws held across the face", Min = -90, Max = 90)] public float yawDeg;
        [Param(ParamUnit.Fraction, "finger tips: 0 = sharp points, 0.5 = blunt rounded ends", Min = 0, Max = 0.7f)] public float tipRound;
        [Param(ParamUnit.Count, "teeth along each finger's biting edge", Min = 0, Max = 8, Default = "3")] public int teeth = 3;
        [Param(ParamUnit.Metres, "tooth size (height)")] public float toothSize;
        [Param(ParamUnit.Fraction, "the first (innermost) tooth x this: a crusher's molar", Min = 0.5f, Max = 3f, Default = "1")] public float molar = 1f;
        [Param(ParamUnit.Fraction, "dark tip: this share of each finger's length, in tipLayer", Min = 0, Max = 1, Default = "0.3")] public float tipFraction = 0.3f;
        [Param(ParamUnit.Count, "spines on the palm's outer and upper faces", Min = 0, Max = 40)] public int spines;
        [Param(ParamUnit.Metres, "palm spine length")] public float spineLength;
        [Param(ParamUnit.Count, "seed of the spines' scatter")] public int seed;
        [Param(ParamUnit.Count, "material layer of the palm and fingers (1 = the limbs')", Min = 0, Max = 15, Default = "1")] public int layer = 1;
        [Param(ParamUnit.Count, "material layer of the finger tips and teeth (2 = the claw row)", Min = 0, Max = 15, Default = "2")] public int tipLayer = 2;
        [Param(ParamUnit.Count, "material layer of the palm spines", Min = 0, Max = 15, Default = "1")] public int spineLayer = 1;
        [Param(ParamUnit.Count, "mesh: vertices around each section (0 = 14)", Min = 0, Max = 32)] public int around;

        public int Around => around > 0 ? around : 14;

        public override int BoneCount(in SpeciesCtx c) => 0;
        public override int MeshOrder => 100;   // a new type: after every shipped vertex
        public override void CollectReferences(List<string> into) => into.Add(LegId);

        public string LegId => leg == null ? "" : at.side == 0 || leg.EndsWith(".L") || leg.EndsWith(".R") ? leg : leg + (at.side < 0 ? ".L" : ".R");

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a Chela needs a SpineBody";
            if (!(c.Find(LegId) is ArthropodLeg)) return $"{id}: leg '{LegId}' must name an ArthropodLeg";
            if (!(palmLength > 0f && palmHeight > 0f && palmWidth > 0f)) return $"{id}: palmLength, palmHeight, palmWidth must be > 0";
            if (!(fingerLength > 0f && fingerHeight > 0f)) return $"{id}: fingerLength, fingerHeight must be > 0";
            if (fingerHeight > palmHeight) return $"{id}: fingerHeight must be <= palmHeight";
            if (wrist < 0.2f || wrist > 1f || square < 1.5f || square > 6f) return $"{id}: wrist 0.2..1, square 1.5..6";
            if (fingerWidth < 0.3f || fingerWidth > 1.5f || dactylScale < 0.6f || dactylScale > 1.5f) return $"{id}: fingerWidth 0.3..1.5, dactylScale 0.6..1.5";
            if (Mathf.Abs(yawDeg) > 90f || tipRound < 0f || tipRound > 0.7f) return $"{id}: |yawDeg| <= 90, tipRound 0..0.7";
            if (openDeg < 0f || openDeg > 60f || curveDeg < 0f || curveDeg > 80f || Mathf.Abs(pitchDeg) > 40f) return $"{id}: openDeg 0..60, curveDeg 0..80, pitchDeg -40..40";
            if (teeth < 0 || teeth > 8 || toothSize < 0f || molar < 0.5f || molar > 3f) return $"{id}: teeth 0..8, toothSize >= 0, molar 0.5..3";
            if (tipFraction < 0f || tipFraction > 1f) return $"{id}: tipFraction 0..1";
            if (spines < 0 || spines > 40 || spineLength < 0f) return $"{id}: spines 0..40, spineLength >= 0";
            if (Bad(layer) || Bad(tipLayer) || Bad(spineLayer)) return $"{id}: layers must be spine-shader part ids (0..3, 8..15)";
            return null;
        }

        static bool Bad(int l) => l < 0 || l >= PartLayers.MaxParts || (l >= 4 && l <= 7);

        // ------------------------------------------------------------ the shape (claw frame, scale 1, side +1)

        // The palm's half-size factor at z (0 = the wrist .. palmLength = its distal end), caps rounded past either end.
        public float PalmProfile(float z)
        {
            float x = Mathf.Clamp01(z / palmLength);
            float f = Mathf.Lerp(wrist, 1f, Smooth(0f, 0.5f, x)) * (1f - 0.1f * Smooth(0.65f, 1f, x));
            float capA = CapStart, capB = CapEnd;
            if (z < 0f) f *= Mathf.Sqrt(Mathf.Max(0f, 1f - (z / capA) * (z / capA)));
            else if (z > palmLength) { float d = (z - palmLength) / capB; f *= Mathf.Sqrt(Mathf.Max(0f, 1f - d * d)); }
            return f;
        }

        public float CapStart => 0.9f * wrist * Mathf.Min(palmHeight, palmWidth);
        public float CapEnd => 0.75f * palmWidth;

        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        static float SPow(float c, float e) => Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), e);

        // A palm surface point: t 0..1 over z from -CapStart to palmLength + CapEnd, phi round the section (0 = outer side).
        public Vector3 Palm(float t, float phi)
        {
            float z = Mathf.Lerp(-CapStart, palmLength + CapEnd, t);
            float f = PalmProfile(z), e = 2f / square;
            // the lower edge runs straighter into the fixed finger: the section's centre drops a little toward the end
            float drop = -0.12f * palmHeight * Smooth(0.4f, 1f, z / palmLength);
            return new Vector3(palmWidth * f * SPow(Mathf.Cos(phi), e), drop + palmHeight * f * SPow(Mathf.Sin(phi), e), z);
        }

        public Vector3 PalmCentre(float t)
        {
            float z = Mathf.Lerp(-CapStart, palmLength + CapEnd, t);
            return new Vector3(0f, -0.12f * palmHeight * Smooth(0.4f, 1f, z / palmLength), z);
        }

        // A finger: root, direction, closing direction (toward the other finger), total length; an arc curling toward
        // `close` by curveDeg. Section half height (along the closing direction) and half thickness (x) taper to a point.
        public struct Finger { public Vector3 root, dir, close; public float length, h0; }

        public Finger Fixed()
        {
            float zr = 0.7f * palmLength;
            float yEnd = palmHeight * PalmProfile(palmLength);
            var f = new Finger { root = new Vector3(0f, -0.12f * palmHeight * Smooth(0.4f, 1f, 0.7f) - (yEnd - fingerHeight) * 0.85f, zr), h0 = fingerHeight };
            Quaternion p = Quaternion.AngleAxis(pitchDeg, Vector3.right);
            f.dir = p * Vector3.forward; f.close = p * Vector3.up;
            f.length = fingerLength + (palmLength + 0.5f * CapEnd - zr);
            return f;
        }

        public Finger Movable()
        {
            float zr = 0.82f * palmLength;
            float yEnd = palmHeight * PalmProfile(zr);
            var f = new Finger { root = new Vector3(0f, -0.12f * palmHeight + (yEnd - 0.9f * fingerHeight) * 0.8f, zr), h0 = 0.9f * fingerHeight };
            Quaternion p = Quaternion.AngleAxis(pitchDeg - openDeg, Vector3.right);   // + about x turns +z down: open = up
            f.dir = p * Vector3.forward; f.close = p * Vector3.down;
            f.length = dactylScale * (fingerLength + (palmLength + 0.5f * CapEnd - 0.7f * palmLength)) - (zr - 0.7f * palmLength);
            return f;
        }

        // Centre, tangent and closing direction at s (0 = root .. 1 = tip).
        public void FingerFrame(in Finger f, float s, out Vector3 c, out Vector3 t, out Vector3 n)
        {
            float a = curveDeg * Mathf.Deg2Rad, ang = a * s;
            if (a > 1e-4f)
            {
                float R = f.length / a;
                c = f.root + f.dir * (R * Mathf.Sin(ang)) + f.close * (R * (1f - Mathf.Cos(ang)));
            }
            else c = f.root + f.dir * (f.length * s);
            t = f.dir * Mathf.Cos(ang) + f.close * Mathf.Sin(ang);
            n = f.close * Mathf.Cos(ang) - f.dir * Mathf.Sin(ang);
        }

        public float FingerHalfHeight(in Finger f, float s)
        {
            s = Mathf.Clamp(s, -0.2f, 1f);
            float k = 1f - Mathf.Max(0f, s);
            if (tipRound <= 0f) return f.h0 * Mathf.Pow(k, 0.6f) * (0.85f + 0.15f * k);
            // a blunt finger: tapers to tipRound of its root, the end rounded off
            float x = Mathf.Max(0f, s);
            return f.h0 * (tipRound + (1f - tipRound) * Mathf.Pow(k, 0.8f)) * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(x, 10f)));
        }

        public Vector3 FingerPoint(in Finger f, float s, float phi)
        {
            FingerFrame(f, s, out Vector3 c, out _, out Vector3 n);
            float h = FingerHalfHeight(f, s);
            // a buried start: the root runs back into the palm with full section, pinched at s = -0.2
            if (s < 0f) h *= Mathf.Sqrt(Mathf.Max(0f, 1f - (s / 0.2f) * (s / 0.2f)));
            float e = 2f / 2.2f;
            return c + n * (h * SPow(Mathf.Sin(phi), e)) + Vector3.right * (fingerWidth * h * SPow(Mathf.Cos(phi), e));
        }

        // ------------------------------------------------------------ mesh

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            IReadOnlyList<CreaturePart> parts = KitView.Parts(m.Species);
            int li = -1;
            for (int i = 0; i < parts.Count; i++) if (parts[i] is ArthropodLeg && parts[i].id == LegId) li = i;
            if (li < 0) return;
            var a = (ArthropodLeg)parts[li];
            int n = a.segments.Length;
            int bone = m.Species.Layout.Start(li) + n - 1;
            Matrix4x4 B = m.Bones[bone];
            Vector3 o = B.MultiplyPoint3x4(Vector3.zero);
            Vector3 Z = PartMeshEmitter.Unit(B.MultiplyVector(Vector3.forward), Vector3.forward);
            Vector3 U = Vector3.ProjectOnPlane(Vector3.up, Z);
            U = U.sqrMagnitude > 1e-8f ? U.normalized : PartMeshEmitter.Unit(B.MultiplyVector(Vector3.up), Vector3.up);
            Vector3 S = Vector3.Cross(U, Z);
            float side = a.at.side < 0 ? -1f : 1f;
            // + roll turns the top (+y) toward +x (outward); + yaw turns +z toward -x (the midline)
            Quaternion turn = Quaternion.AngleAxis(-yawDeg, Vector3.up) * Quaternion.AngleAxis(-rollDeg, Vector3.forward);
            Vector3 W(Vector3 v) { v = turn * v; return o + S * (side * v.x) + U * v.y + Z * v.z; }
            Vector3 Wd(Vector3 v) { v = turn * v; return S * (side * v.x) + U * v.y + Z * v.z; }
            Vector4 skin = PartMeshEmitter.Rigid(bone);
            int na = Around;
            float uBase = 0f;
            for (int k = 0; k < n; k++) uBase += a.segments[k].length;

            // the palm
            float span = palmLength + CapStart + CapEnd;
            KitSweep.Sweep(m, (t, phi) => W(Palm(t, phi)), t => W(PalmCentre(t)), 0f, 1f, 12, na, true, true, layer, skin, uBase, span);

            // the fingers: the root (buried in the palm) in the palm's layer, the tip in tipLayer
            foreach (Finger f in new[] { Fixed(), Movable() })
            {
                Finger g = f;
                float split = Mathf.Clamp(1f - tipFraction, 0f, 1f);
                float u0 = uBase + palmLength;
                Vector3 P(float s, float phi) => W(FingerPoint(g, s, phi));
                Vector3 C(float s) { FingerFrame(g, s, out Vector3 cc, out _, out _); return W(cc); }
                if (split <= 0.02f) KitSweep.Sweep(m, P, C, -0.2f, 1f, 10, na, true, true, tipLayer, skin, u0, g.length);
                else if (split >= 0.98f) KitSweep.Sweep(m, P, C, -0.2f, 1f, 10, na, true, true, layer, skin, u0, g.length);
                else
                {
                    KitSweep.Sweep(m, P, C, -0.2f, split, 7, na, true, false, layer, skin, u0, g.length, false);
                    KitSweep.Sweep(m, P, C, split, 1f, 6, na, false, true, tipLayer, skin, u0, g.length, false);
                }
                // teeth along the biting edge (toward the other finger), the first a molar
                for (int k = 0; k < teeth && toothSize > 0f; k++)
                {
                    float s = teeth == 1 ? 0.35f : Mathf.Lerp(0.18f, 0.7f, k / (teeth - 1f));
                    FingerFrame(g, s, out Vector3 cc, out Vector3 tt, out Vector3 nn);
                    float h = FingerHalfHeight(g, s);
                    float size = toothSize * (k == 0 ? molar : 1f) * (1f - 0.35f * s);
                    Vector3 basePt = cc + nn * (0.8f * h);
                    KitSweep.Spike(m, W(basePt), Wd(nn + 0.35f * tt), Wd(tt), size, 0.75f * size, 0.3f * size, 0f, 0.6f, bone, tipLayer, 6, u0 + s * g.length);
                }
            }

            // spines on the outer and upper faces of the palm, raking toward the fingers
            for (int k = 0; k < spines && spineLength > 0f; k++)
            {
                float t = Mathf.Lerp(0.15f, 0.85f, KitSweep.Hash01(seed * 131 + 7, k));
                float phi = Mathf.Lerp(-0.35f, 1.9f, KitSweep.Hash01(seed * 131 + 11, k));
                float len = spineLength * (0.6f + 0.4f * KitSweep.Hash01(seed * 131 + 13, k));
                Vector3 p = Palm(t, phi), cen = PalmCentre(t);
                Vector3 dPhi = Palm(t, phi + 1e-3f) - Palm(t, phi - 1e-3f), dT = Palm(t + 1e-3f, phi) - Palm(t - 1e-3f, phi);
                Vector3 nl = Vector3.Cross(dPhi, dT);
                if (Vector3.Dot(nl, p - cen) < 0f) nl = -nl;
                nl = PartMeshEmitter.Unit(nl, p - cen);
                KitSweep.Spike(m, W(p), Wd(nl + 0.5f * Vector3.forward), Wd(Vector3.forward), len, 0.3f * len, 0f, 15f, 0.35f, bone, spineLayer, 5, uBase + t * span);
            }
        }
    }
}
