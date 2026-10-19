// Creature kit: Swell - a muscle mass lofted round the spine over a stretch of joints: a deep keeled chest with
// pectorals at the wing roots, a withers hump, haunches, a pot belly. Mesh only, no bones: each ring is the body's
// own cross-section (PartMeshEmitter.SpineAt) grown outward along the ellipse normal by a profile around the body
// (keel below, drawn to a ridge by keelSharpness; breadth at breadthAroundDeg from the back; hump above) times a
// bump along the stretch (0 at both ends, 1 at peakAt). It is skinned to the spine exactly as the body rings are
// (FinStripMesh.SpineSkin), so it bends, twists and bobs with the body; at both ends it sinks inset of the body's
// radius inside the body, so it grows out of the skin instead of ending in an edge. Shaded as the body (part 0:
// arc length and angle as the body's rings, so the pattern runs on across it).
// Kit-only part (no legacy CreatureSpecies field). Runs once per species mesh (may allocate).
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("Swell", PartKind.Rigid, Bones = "0 (skinned to the spine)", Summary = "A muscle mass lofted round the spine over a stretch of joints: keel below (a sternum ridge), breadth at the sides (pectorals, haunches), hump above (withers), fading into the body at both ends. Skinned to the spine like the body rings, shaded as the body.",
          System = "mesh only (Swell.EmitMesh), skinned to the spine bones", MaterialLayers = new[] { 0 }, KitOnly = true,
          UseWhen = "a deep chest with flight muscles at the wing roots, shoulders, haunches, a belly - any bulk the body's elliptical profile cannot give")]
    public sealed class Swell : CreaturePart
    {
        [Param(ParamUnit.Joint, "last spine joint of the stretch (the first is at.joint)")] public int toJoint;
        [Param(ParamUnit.Fraction, "where along the stretch it is fullest (0 = at.joint .. 1 = toJoint)", Min = 0.05f, Max = 0.95f, Example = "dragon chest 0.35")] public float peakAt = 0.5f;
        [Param(ParamUnit.Metres, "extra depth straight down at the peak (the keel)", Example = "dragon chest 0.05")] public float keel;
        [Param(ParamUnit.Fraction, "how sharply the keel is drawn to a ridge: 1 = a round belly, 4 = a sternum keel", Min = 1, Max = 8, Default = "2")] public float keelSharpness = 2f;
        [Param(ParamUnit.Metres, "extra breadth at the sides at the peak (pectorals, haunches)")] public float breadth;
        [Param(ParamUnit.Degrees, "where round the body the breadth sits: 90 = the flank, 120 = the lower flank", Min = 0, Max = 180, Default = "100")] public float breadthAroundDeg = 100f;
        [Param(ParamUnit.Degrees, "angular half width of the breadth bulge", Min = 10, Max = 90, Default = "40")] public float breadthWidthDeg = 40f;
        [Param(ParamUnit.Metres, "extra height above at the peak (withers, a shoulder hump)")] public float hump;
        [Param(ParamUnit.Fraction, "both ends sink this fraction of the body's radius inside the body", Min = 0, Max = 0.5f, Default = "0.08")] public float inset = 0.08f;
        [Param(ParamUnit.Count, "mesh: rings per spine segment (0 = 4)", Min = 0, Max = 12)] public int ringsPerJoint;
        [Param(ParamUnit.Count, "mesh: vertices round each ring (0 = 32)", Min = 0, Max = 64)] public int around;

        const float kClear = 0.03f;       // the swell's skin sits this fraction of the body's radius outside the body
        const float kStep = 1e-3f;        // finite-difference step (joint units and radians) for the normals

        public override int BoneCount(in SpeciesCtx c) => 0;
        public int Rings => Mathf.Max(1, toJoint - at.joint) * (ringsPerJoint > 0 ? ringsPerJoint : 4);
        public int Around => around > 0 ? Mathf.Max(8, around) : 32;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a Swell needs a SpineBody";
            if (at.joint < 0 || toJoint >= c.Joints || toJoint <= at.joint) return $"{id}: joints {at.joint}..{toJoint} must lie in 0..{c.Joints - 1}, first < last";
            if (!(peakAt >= 0.05f && peakAt <= 0.95f)) return $"{id}: peakAt must be in 0.05..0.95";
            if (keel < 0f || breadth < 0f || hump < 0f || keel + breadth + hump <= 0f) return $"{id}: keel, breadth, hump must be >= 0 and not all 0";
            if (!(keelSharpness >= 1f && keelSharpness <= 8f)) return $"{id}: keelSharpness must be in 1..8";
            if (inset < 0f || inset > 0.5f) return $"{id}: inset must be in 0..0.5";
            return null;
        }

        // The bump along the stretch at spine coordinate u: 0 at both ends, 1 at the peak, C2 smooth.
        public float Amount(float u)
        {
            float t = Mathf.Clamp01((u - at.joint) / Mathf.Max(1e-4f, toJoint - at.joint));
            float q = t < peakAt ? t / peakAt : (1f - t) / (1f - peakAt);
            q = Mathf.Clamp01(q);
            return q * q * q * (q * (6f * q - 15f) + 10f);
        }

        // The extra round the body at ring angle phi (the body's ring convention: -pi/2 the belly, +pi/2 the back).
        public float Extra(float phi)
        {
            float sn = Mathf.Sin(phi);
            float k = keel * Mathf.Pow(Mathf.Max(0f, -sn), keelSharpness);
            float h = hump * Mathf.Max(0f, sn) * Mathf.Max(0f, sn);
            float fromBack = Mathf.Acos(Mathf.Clamp(sn, -1f, 1f)) * Mathf.Rad2Deg;   // 0 back .. 180 belly
            float d = (fromBack - breadthAroundDeg) / Mathf.Max(1f, breadthWidthDeg);
            float b = breadth * Mathf.Exp(-d * d);
            return k + h + b;
        }

        // A surface point at ring angle phi on the section s at spine coordinate u.
        Vector3 Point(in SpineSection s, float u, float phi)
        {
            float cs = Mathf.Cos(phi), sn = Mathf.Sin(phi);
            Vector3 onBody = s.c + s.x * (s.a * cs) + s.y * (s.b * sn);
            Vector3 n = PartMeshEmitter.Unit(s.x * (cs / s.a) + s.y * (sn / s.b), s.y);
            float r = Mathf.Sqrt(s.a * s.b), w = Amount(u);
            float off = w * (Extra(phi) + (inset + kClear) * r) - inset * r;
            return onBody + n * off;
        }

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            CreatureSpecies sp = m.Species;
            int rings = Rings, segs = Around, joints = m.Joints;
            float headLen = Mathf.Max(sp.headCapLength, 1e-3f);
            int first = m.VertexCount;
            var centre = new Vector3[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float u = Mathf.Lerp(at.joint, toJoint, i / (float)rings);
                float u0 = Mathf.Max(0f, u - kStep), u1 = Mathf.Min(joints - 1, u + kStep);
                SpineSection s = m.SpineAt(u), s0 = m.SpineAt(u0), s1 = m.SpineAt(u1);
                centre[i] = s.c;
                Vector4 skin = FinStripMesh.SpineSkin(u, joints);
                float arc = headLen + sp.spacing * u;
                for (int k = 0; k <= segs; k++)
                {
                    float phi = -0.5f * Mathf.PI + 2f * Mathf.PI * k / segs;
                    if (k == segs) phi = -0.5f * Mathf.PI + 2f * Mathf.PI;
                    Vector3 p = Point(s, u, phi);
                    // the normal from the surface's own tangents (round the ring, along the spine toward the tail)
                    Vector3 dPhi = Point(s, u, phi + kStep) - Point(s, u, phi - kStep);
                    Vector3 dU = Point(s1, u1, phi) - Point(s0, u0, phi);
                    Vector3 outward = s.x * Mathf.Cos(phi) + s.y * Mathf.Sin(phi);
                    Vector3 nn = PartMeshEmitter.Unit(Vector3.Cross(dU, dPhi), outward);
                    if (Vector3.Dot(nn, outward) < 0f) nn = -nn;
                    m.Vertex(p, nn, arc, (float)k / segs, PartMeshEmitter.PartBody, Mathf.Sin(phi), skin);
                }
            }
            // quads ring i -> i + 1, wound to face outward
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
