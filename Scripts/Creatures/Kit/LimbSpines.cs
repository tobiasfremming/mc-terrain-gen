// Creature kit: LimbSpines - spines or bristles along an ArthropodLeg's segments: a king crab's spiny legs, the
// fringe of setae on a stone crab's or a tarantula's legs, a mantis' raptorial spines. perMetre spines on each segment
// fromSegment..toSegment, scattered along it (12-92 % of its length) and round it within spreadDeg of aroundDeg (0 = the
// leg's top at rest, 90 = its side toward the head, 180 = below, -90 = toward the tail; the same on both sides), each a
// curved cone from a base sunk into the segment's tube, raked toward the leg's tip by leanDeg. Long thin dark ones
// (baseRatio 0.08, tipRatio 0) read as hairs. Deterministic (seed). Mesh only, no bones: every spine is rigid to its
// segment's bone. mirror expands into .L / .R naming the limb's .L / .R. Material layer `layer`.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("LimbSpines", PartKind.Rigid, Bones = "0 (rigid to the leg's segment bones)", Summary = "Spines or bristles along an ArthropodLeg's segments: perMetre per segment, scattered along and round the tube (aroundDeg +- spreadDeg from the top), raked toward the tip. Long thin ones read as hairs. Deterministic; mirror names the limb's .L / .R.",
          System = "mesh only (LimbSpines.EmitMesh), rigid to the leg's bones", MaterialLayers = new[] { 1, 2, 9 }, KitOnly = true,
          UseWhen = "spiny legs (king crab), leg bristles and setae fringes (stone crab, tarantula), raptorial spines")]
    public sealed class LimbSpines : CreaturePart
    {
        [Param(ParamUnit.PartRef, "the ArthropodLeg; a mirrored part names the unmirrored id and takes .L / .R")] public string leg = "";
        [Param(ParamUnit.Count, "first segment with spines", Min = 0, Max = 6)] public int fromSegment;
        [Param(ParamUnit.Count, "last segment with spines", Min = 0, Max = 6, Default = "6")] public int toSegment = 6;
        [Param(ParamUnit.Count, "spines per metre of segment (x 1 / s: the same count at every size)", Min = 0, Max = 2000, Example = "king crab 90")] public float perMetre = 60f;
        [Param(ParamUnit.Degrees, "round the tube: the band's centre (0 = top, 90 = toward the head, 180 = below)", Min = -180, Max = 180)] public float aroundDeg;
        [Param(ParamUnit.Degrees, "round the tube: +- this about the centre", Min = 0, Max = 180, Default = "70")] public float spreadDeg = 70f;
        [Param(ParamUnit.Metres, "spine length")] public float length;
        [Param(ParamUnit.Fraction, "length varies by +- this share", Min = 0, Max = 0.9f, Default = "0.3")] public float lengthJitter = 0.3f;
        [Param(ParamUnit.Fraction, "base radius, x the length", Min = 0.03f, Max = 1f, Default = "0.3")] public float baseRatio = 0.3f;
        [Param(ParamUnit.Fraction, "tip radius, x the base radius (0 = sharp)", Min = 0, Max = 1)] public float tipRatio;
        [Param(ParamUnit.Degrees, "raked toward the leg's tip", Min = -80, Max = 85, Default = "45")] public float leanDeg = 45f;
        [Param(ParamUnit.Degrees, "each spine curls toward the tip by this much", Min = -90, Max = 90)] public float curveDeg;
        [Param(ParamUnit.Count, "seed of the scatter")] public int seed;
        [Param(ParamUnit.Count, "material layer (1 = the limbs', 2 = the claw row, 9 = the band colour)", Min = 0, Max = 15, Default = "1")] public int layer = 1;
        [Param(ParamUnit.Count, "mesh: sides of each cone (0 = 5)", Min = 0, Max = 12)] public int sides;

        public override int BoneCount(in SpeciesCtx c) => 0;
        public override int MeshOrder => 100;   // a new type: after every shipped vertex
        public override void CollectReferences(List<string> into) => into.Add(LegId);

        public string LegId => leg == null ? "" : at.side == 0 || leg.EndsWith(".L") || leg.EndsWith(".R") ? leg : leg + (at.side < 0 ? ".L" : ".R");

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: LimbSpines need a SpineBody";
            if (!(c.Find(LegId) is ArthropodLeg a)) return $"{id}: leg '{LegId}' must name an ArthropodLeg";
            int n = a.segments != null ? a.segments.Length : 0;
            if (fromSegment < 0 || toSegment < fromSegment || fromSegment >= n) return $"{id}: segments {fromSegment}..{toSegment} outside the leg's 0..{n - 1}";
            if (perMetre < 0f || perMetre > 2000f) return $"{id}: perMetre 0..2000";
            if (!(length > 0f) || lengthJitter < 0f || lengthJitter > 0.9f) return $"{id}: length > 0, lengthJitter 0..0.9";
            if (baseRatio < 0.03f || baseRatio > 1f || tipRatio < 0f || tipRatio > 1f) return $"{id}: baseRatio 0.03..1, tipRatio 0..1";
            if (spreadDeg < 0f || spreadDeg > 180f || leanDeg < -80f || leanDeg > 85f || Mathf.Abs(curveDeg) > 90f) return $"{id}: spreadDeg 0..180, leanDeg -80..85, |curveDeg| <= 90";
            if (layer < 0 || layer >= PartLayers.MaxParts || (layer >= 4 && layer <= 7)) return $"{id}: layer must be a spine-shader part id (0..3, 8..15)";
            return null;
        }

        // The tube radius of a leg segment at u (0..1) - ArthropodLegMesh.Segment's spindle, restated (pure).
        public static float TubeRadius(in SegmentDef s, bool attached, float u)
        {
            float r0 = Mathf.Max(1e-4f, s.radiusStart), r1 = Mathf.Max(1e-4f, s.radiusEnd > 0f ? s.radiusEnd : s.radiusStart);
            float a = attached ? r0 : Mathf.Max(1e-4f, 0.8f * r0);
            float swell = s.length > 3f * Mathf.Max(r0, r1) ? ArthropodLegMesh.SpindleSwell : 0f;
            u = Mathf.Clamp01(u);
            return Mathf.Lerp(a, r1, u) * (1f + swell * Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.85f)));
        }

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            IReadOnlyList<CreaturePart> parts = KitView.Parts(m.Species);
            int li = -1;
            for (int i = 0; i < parts.Count; i++) if (parts[i] is ArthropodLeg && parts[i].id == LegId) li = i;
            if (li < 0) return;
            var a = (ArthropodLeg)parts[li];
            int first = m.Species.Layout.Start(li), sd = sides > 0 ? sides : 5;
            Vector3 bodyFwd = m.Rest.Rotations[Mathf.Clamp(a.at.joint, 0, m.Joints - 1)] * Vector3.forward;
            float arc = 0f;
            int last = Mathf.Min(toSegment, a.segments.Length - 1);
            for (int k = 0; k < a.segments.Length; k++)
            {
                SegmentDef s = a.segments[k];
                if (k < fromSegment || k > last) { arc += s.length; continue; }
                int bone = first + k;
                Matrix4x4 B = m.Bones[bone];
                Vector3 o = B.MultiplyPoint3x4(Vector3.zero);
                Vector3 Z = PartMeshEmitter.Unit(B.MultiplyVector(Vector3.forward), Vector3.forward);
                Vector3 U = Vector3.ProjectOnPlane(Vector3.up, Z);
                U = U.sqrMagnitude > 1e-8f ? U.normalized : PartMeshEmitter.Unit(B.MultiplyVector(Vector3.up), Vector3.up);
                Vector3 F = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(bodyFwd, Z), U);
                F = F.sqrMagnitude > 1e-8f ? F.normalized : Vector3.Cross(Z, U);
                int count = Mathf.RoundToInt(perMetre * s.length);
                Vector4 skin = PartMeshEmitter.Rigid(bone);
                for (int i = 0; i < count; i++)
                {
                    int key = seed * 7919 + k * 613;
                    float u = Mathf.Lerp(0.12f, 0.92f, count == 1 ? 0.5f : (i + KitSweep.Hash01(key + 1, i)) / count);
                    float th = (aroundDeg + spreadDeg * (2f * KitSweep.Hash01(key + 2, i) - 1f)) * Mathf.Deg2Rad;
                    float len = length * (1f + lengthJitter * (2f * KitSweep.Hash01(key + 3, i) - 1f));
                    Vector3 radial = U * Mathf.Cos(th) + F * Mathf.Sin(th);
                    float r = TubeRadius(s, k == 0, u);
                    // a flattened segment (an ellipse, height r x (1 - flatten) along the leg plane's up): its radius this way round
                    if (s.flatten > 0f) { float sq = 1f - Mathf.Min(0.8f, s.flatten), cu = Mathf.Cos(th), su = Mathf.Sin(th); r *= sq / Mathf.Sqrt(cu * cu + sq * sq * su * su); }
                    Vector3 basePt = o + Z * (u * s.length) + radial * r;
                    float lean = leanDeg * Mathf.Deg2Rad;
                    Vector3 dir = radial * Mathf.Cos(lean) + Z * Mathf.Sin(lean);
                    float r0 = baseRatio * len;
                    KitSweep.Spike(m, basePt, dir, Z, len, r0, tipRatio * r0, curveDeg, Mathf.Min(0.5f, 0.6f * r / len), bone, layer, sd, arc + u * s.length);
                }
                arc += s.length;
            }
        }
    }
}
