// Creature kit: SpineField - spines, knobs or teeth grown out of a RigidLoft's (lumpy) surface: the sharp conical
// spines that cover a king crab's carapace, the blunt teeth along a stone crab's front-lateral margin, a tortoise's
// bosses, a horned lizard's crown. Scattered over a patch of the loft (tMin..tMax along it, phiMin..phiMax round it:
// 0 = the right margin, 90 = the top midline, 180 = the left, -90 = the belly) with a minimum spacing, or set in an even
// row along it (`row`: marginal teeth). Each is a curved cone from a base sunk into the surface, along the surface's
// normal raked back toward the loft's start by leanDeg; sizes vary between lengthMin and lengthMax, scaled from
// startScale (t = 0) to endScale (t = 1). Deterministic (seed). mirror expands into .L / .R: the right side's pattern
// mirrored (phi -> 180 - phi), so the pair is symmetric. Mesh only, no bones: rigid to the loft's joint, like the
// loft. Material layer `layer` (0 = the body skin, 2 = the claw row).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("SpineField", PartKind.Rigid, Bones = "0 (rigid to its loft's joint)", Summary = "Spines, knobs or teeth on a RigidLoft's surface: scattered over a patch (t along, phi round; min spacing) or an even row (marginal teeth); curved cones along the surface normal raked back, sizes graded along the loft. Deterministic; mirror makes the symmetric pair.",
          System = "mesh only (SpineField.EmitMesh), rigid to the loft's joint", MaterialLayers = new[] { 0, 2 }, KitOnly = true,
          UseWhen = "a spiny carapace (king crab), marginal teeth (stone crab), bosses, a horned crown: anything studding a shell or a head")]
    public sealed class SpineField : CreaturePart
    {
        [Param(ParamUnit.PartRef, "the RigidLoft it grows from")] public string loft = "";
        [Param(ParamUnit.Count, "spines (a row: per side)", Min = 1, Max = 200)] public int count = 10;
        [Param(ParamUnit.Flag, "an even row along the loft at phiMin (marginal teeth) instead of a scatter")] public bool row;
        [Param(ParamUnit.Fraction, "patch along the loft: from ..", Min = 0, Max = 1)] public float tMin = 0.1f;
        [Param(ParamUnit.Fraction, ".. to", Min = 0, Max = 1, Default = "0.9")] public float tMax = 0.9f;
        [Param(ParamUnit.Degrees, "patch round the loft: from (0 = right margin, 90 = top, 180 = left, -90 = belly; mirrored by side)", Min = -180, Max = 270)] public float phiMinDeg;
        [Param(ParamUnit.Degrees, ".. to", Min = -180, Max = 270, Default = "90")] public float phiMaxDeg = 90f;
        [Param(ParamUnit.Metres, "shortest spine")] public float lengthMin;
        [Param(ParamUnit.Metres, "longest spine")] public float lengthMax;
        [Param(ParamUnit.Fraction, "base radius, x the length", Min = 0.05f, Max = 2f, Default = "0.35")] public float baseRatio = 0.35f;
        [Param(ParamUnit.Fraction, "tip radius, x the base radius (0 = sharp, 0.6 = a blunt knob)", Min = 0, Max = 1)] public float tipRatio;
        [Param(ParamUnit.Fraction, "size x this at the loft's start ..", Min = 0, Max = 3, Default = "1")] public float startScale = 1f;
        [Param(ParamUnit.Fraction, ".. and x this at its end", Min = 0, Max = 3, Default = "1")] public float endScale = 1f;
        [Param(ParamUnit.Degrees, "raked back toward the loft's start (negative: toward its end)", Min = -80, Max = 80)] public float leanDeg;
        [Param(ParamUnit.Degrees, "each spine curls (toward the rake) by this much", Min = -90, Max = 90)] public float curveDeg;
        [Param(ParamUnit.Metres, "scatter: no two bases closer than this (0 = no limit)")] public float spacing;
        [Param(ParamUnit.Count, "seed of the scatter")] public int seed;
        [Param(ParamUnit.Count, "material layer (0 = the body skin, 2 = the claw row)", Min = 0, Max = 15)] public int layer;
        [Param(ParamUnit.Count, "mesh: sides of each cone (0 = 6)", Min = 0, Max = 16)] public int sides;

        public override int BoneCount(in SpeciesCtx c) => 0;
        public override int MeshOrder => 100;   // a new type: after every shipped vertex
        public override void CollectReferences(List<string> into) => into.Add(loft);

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a SpineField needs a SpineBody";
            if (!(c.Find(loft) is RigidLoft)) return $"{id}: loft '{loft}' must name a RigidLoft";
            if (count < 1 || count > 200) return $"{id}: count must be 1..200";
            if (!(tMin >= 0f && tMax <= 1f && tMax >= tMin)) return $"{id}: 0 <= tMin <= tMax <= 1";
            if (!(phiMaxDeg >= phiMinDeg) || phiMinDeg < -180f || phiMaxDeg > 270f) return $"{id}: -180 <= phiMinDeg <= phiMaxDeg <= 270";
            if (!(lengthMin > 0f) || lengthMax < lengthMin) return $"{id}: 0 < lengthMin <= lengthMax";
            if (baseRatio < 0.05f || baseRatio > 2f || tipRatio < 0f || tipRatio > 1f) return $"{id}: baseRatio 0.05..2, tipRatio 0..1";
            if (startScale < 0f || endScale < 0f || Mathf.Abs(leanDeg) > 80f || Mathf.Abs(curveDeg) > 90f || spacing < 0f) return $"{id}: scales >= 0, |leanDeg| <= 80, |curveDeg| <= 90, spacing >= 0";
            if (layer < 0 || layer >= PartLayers.MaxParts || (layer >= 4 && layer <= 7)) return $"{id}: layer must be a spine-shader part id (0..3, 8..15)";
            return null;
        }

        // One spine of the field (scale 1, the loft's joint frame): its (t, phi) on the loft, its length.
        public struct Spot { public float t, phi, length; }

        // The spots, deterministic: the right side's pattern (side +1), mirrored on side -1. Spacing is judged on the
        // smooth loft (mirror-invariant), so both sides make the same choices.
        public List<Spot> Spots(RigidLoft l)
        {
            var spots = new List<Spot>(count);
            float mirror = at.side < 0 ? -1f : 1f;
            float Phi(float deg) { float p = deg * Mathf.Deg2Rad; return mirror < 0f ? Mathf.PI - p : p; }
            float Len(float t, int k) => Mathf.Lerp(lengthMin, lengthMax, KitSweep.Hash01(seed * 977 + 3, k)) * Mathf.Lerp(startScale, endScale, t);
            if (row)
            {
                for (int k = 0; k < count; k++)
                {
                    float t = count == 1 ? 0.5f * (tMin + tMax) : Mathf.Lerp(tMin, tMax, k / (count - 1f));
                    spots.Add(new Spot { t = t, phi = Phi(phiMinDeg), length = Len(t, k) });
                }
                return spots;
            }
            var bases = new List<Vector3>(count);
            int attempts = count * 40;
            for (int k = 0; k < attempts && spots.Count < count; k++)
            {
                float t = Mathf.Lerp(tMin, tMax, KitSweep.Hash01(seed * 977 + 1, k));
                float deg = Mathf.Lerp(phiMinDeg, phiMaxDeg, KitSweep.Hash01(seed * 977 + 2, k));
                Vector3 p = RigidLoft.Point(l.SectionAt(t), deg * Mathf.Deg2Rad);
                bool ok = true;
                if (spacing > 0f) foreach (Vector3 b in bases) if ((b - p).sqrMagnitude < spacing * spacing) { ok = false; break; }
                if (!ok) continue;
                bases.Add(p);
                spots.Add(new Spot { t = t, phi = Phi(deg), length = Len(t, k) });
            }
            return spots;
        }

        // A spot's base on the lumpy surface, its outward normal and the loft's tangent there (joint frame, scale 1).
        public static void Surface(RigidLoft l, float t, float phi, out Vector3 p, out Vector3 normal, out Vector3 tangent)
        {
            const float e = 1e-3f;
            RigidLoft.Section q = l.SectionAt(t), qA = l.SectionAt(Mathf.Max(0f, t - e)), qB = l.SectionAt(Mathf.Min(1f, t + e));
            p = l.Lumped(q, phi);
            Vector3 dPhi = l.Lumped(q, phi + e) - l.Lumped(q, phi - e), dT = l.Lumped(qB, phi) - l.Lumped(qA, phi);
            Vector3 nl = Vector3.Cross(dPhi, dT);
            if (Vector3.Dot(nl, p - q.c) < 0f) nl = -nl;
            normal = PartMeshEmitter.Unit(nl, PartMeshEmitter.Unit(p - q.c, Vector3.up));
            tangent = PartMeshEmitter.Unit(dT, Vector3.forward);
        }

        // A spot's spine direction (joint frame): the normal raked toward the loft's start by leanDeg.
        public Vector3 Direction(Vector3 normal, Vector3 tangent)
        {
            Vector3 back = Vector3.ProjectOnPlane(-tangent, normal);
            back = back.sqrMagnitude > 1e-10f ? back.normalized : Vector3.zero;
            float a = leanDeg * Mathf.Deg2Rad;
            return PartMeshEmitter.Unit(normal * Mathf.Cos(a) + back * Mathf.Sin(a), normal);
        }

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            RigidLoft l = null;
            foreach (CreaturePart p in KitView.Parts(m.Species)) if (p is RigidLoft r && r.id == loft) l = r;
            if (l == null) return;
            int j = Mathf.Clamp(l.at.joint, 0, m.Joints - 1);
            Vector3 jp = m.Rest.Joints[j];
            Quaternion jr = m.Rest.Rotations[j];
            Vector4 skin = PartMeshEmitter.Rigid(j);
            float uBase = m.Species.headCapLength + m.Species.spacing * j;
            int sd = sides > 0 ? sides : 6;
            foreach (Spot s in Spots(l))
            {
                Surface(l, s.t, s.phi, out Vector3 p, out Vector3 n, out Vector3 tan);
                Vector3 d = Direction(n, tan);
                float r0 = baseRatio * s.length;
                KitSweep.Spike(m, jp + jr * p, jr * d, jr * -tan, s.length, r0, tipRatio * r0, curveDeg, Mathf.Min(0.6f, r0 / s.length), j, layer, sd,
                               Mathf.Max(0f, uBase - Vector3.Dot(jr * p, jr * Vector3.forward)));
            }
        }
    }
}
