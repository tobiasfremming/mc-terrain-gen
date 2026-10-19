// Creature kit: ScuteRow - a row of raised bony scutes (osteoderms) along the spine: keeled plates set on the body's
// surface from at.joint to toJoint, perJoint to a segment, at an angle round the body that may drift along the row
// (aroundStartDeg -> aroundEndDeg: two crest rows on a crocodile's tail drawing together into one), their size
// tapering from start to end. Each scute is a low keeled pyramid: a base buried a little in the skin and a ridge
// that rises toward the rear (its back end tallest), flat-shaded so its facets catch the light. mirror gives the
// row at +- the angle (the usual paired rows); on the midline (angle 0, no mirror) a single crest. Mesh only:
// each scute is skinned to the spine as one piece (the body's spine skin at its centre), so it rides the body's
// bends without deforming. Material layer `layer` (12 armour; restyle it with Layers, or 0: the body's skin).
// Kit-only part. Runs once per species mesh (may allocate).
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("ScuteRow", PartKind.Rigid, Bones = "0 (skinned to the spine)", Summary = "A row of raised keeled scutes (osteoderms) along the spine at an angle round the body that may drift along the row, tapering; mirror for the paired rows. Each scute a flat-shaded keeled pyramid riding the spine as one piece.",
          System = "mesh only (ScuteRow.EmitMesh), skinned to the spine bones", MaterialLayers = new[] { 12, 0 }, KitOnly = true,
          UseWhen = "crocodile and alligator armour, a tail's double crest, a stegosaur-like row of plates, spiny lizard backs")]
    public sealed class ScuteRow : CreaturePart
    {
        [Param(ParamUnit.Joint, "last spine joint of the row (the first is at.joint)")] public int toJoint;
        [Param(ParamUnit.Fraction, "scutes per spine segment", Min = 0.25f, Max = 4, Default = "1")] public float perJoint = 1f;
        [Param(ParamUnit.Degrees, "angle round the body from the back at the first scute (mirror: also at minus this)", Min = 0, Max = 150)] public float aroundStartDeg;
        [Param(ParamUnit.Degrees, "angle round the body at the last scute", Min = 0, Max = 150)] public float aroundEndDeg;
        [Param(ParamUnit.Metres, "ridge height at the first scute", Example = "crocodile back 0.012")] public float heightStart = 0.01f;
        [Param(ParamUnit.Metres, "ridge height at the last scute")] public float heightEnd = 0.01f;
        [Param(ParamUnit.Metres, "scute width (across) at the first scute")] public float widthStart = 0.02f;
        [Param(ParamUnit.Metres, "scute width at the last scute")] public float widthEnd = 0.02f;
        [Param(ParamUnit.Fraction, "scute length, fraction of the gap between scutes", Min = 0.2f, Max = 1.2f, Default = "0.85")] public float lengthFraction = 0.85f;
        [Param(ParamUnit.Fraction, "keel: the ridge's width, fraction of the scute's (0 = a sharp keel)", Min = 0, Max = 0.9f)] public float keelWidth = 0.15f;
        [Param(ParamUnit.Fraction, "the ridge's front end height, fraction of its back end's (a saw-tooth profile when low)", Min = 0, Max = 1, Default = "0.45")] public float frontHeight = 0.45f;
        [Param(ParamUnit.Fraction, "the base sinks this fraction of the height into the skin", Min = 0, Max = 1, Default = "0.35")] public float inset = 0.35f;
        [Param(ParamUnit.Count, "material layer: 12 armour (restyle it with Layers), 0 the body's skin", Min = 0, Max = 15, Default = "12")] public int layer = 12;

        public override int BoneCount(in SpeciesCtx c) => 0;
        public override int MeshOrder => 100;
        public int Count => Mathf.Max(1, Mathf.RoundToInt((toJoint - at.joint) * perJoint) + 1);

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a ScuteRow needs a SpineBody";
            if (at.joint < 0 || toJoint >= c.Joints || toJoint < at.joint) return $"{id}: joints {at.joint}..{toJoint} must lie in 0..{c.Joints - 1}, first <= last";
            if (!(perJoint >= 0.25f && perJoint <= 4f)) return $"{id}: perJoint must be in 0.25..4";
            if (aroundStartDeg < 0f || aroundStartDeg > 150f || aroundEndDeg < 0f || aroundEndDeg > 150f) return $"{id}: around angles must be in 0..150";
            if (!(heightStart > 0f) || !(heightEnd > 0f) || !(widthStart > 0f) || !(widthEnd > 0f)) return $"{id}: heights and widths must be > 0";
            if (!(lengthFraction >= 0.2f && lengthFraction <= 1.2f) || keelWidth < 0f || keelWidth > 0.9f || frontHeight < 0f || frontHeight > 1f || inset < 0f || inset > 1f)
                return $"{id}: lengthFraction 0.2..1.2, keelWidth 0..0.9, frontHeight 0..1, inset 0..1";
            if (layer < 0 || layer >= PartLayers.MaxParts) return $"{id}: layer must be in 0..{PartLayers.MaxParts - 1}";
            return null;
        }

        // The spine coordinate of scute i and where along the row it is (0 .. 1).
        public float U(int i, out float f)
        {
            int n = Count;
            f = n == 1 ? 0f : i / (float)(n - 1);
            return Mathf.Lerp(at.joint, toJoint, f);
        }

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            CreatureSpecies sp = m.Species;
            int joints = m.Joints, n = Count;
            float headLen = Mathf.Max(sp.headCapLength, 1e-3f);
            float gap = sp.spacing * (n == 1 ? 1f : (toJoint - at.joint) / (float)(n - 1));
            float side = at.side < 0 ? -1f : 1f;   // mirror: .L at -angle (the left), .R at +angle
            var pts = new Vector3[6];
            for (int i = 0; i < n; i++)
            {
                float u = U(i, out float f);
                SpineSection s = m.SpineAt(u);
                float around = Mathf.Lerp(aroundStartDeg, aroundEndDeg, f) * Mathf.Deg2Rad * side;
                // the ring angle (the body's convention: +pi/2 the back) and the surface point / normal there
                float phi = 0.5f * Mathf.PI - around;
                float cs = Mathf.Cos(phi), sn = Mathf.Sin(phi);
                Vector3 onBody = s.c + s.x * (s.a * cs) + s.y * (s.b * sn);
                Vector3 nrm = PartMeshEmitter.Unit(s.x * (cs / s.a) + s.y * (sn / s.b), s.y);
                Vector3 fwd = PartMeshEmitter.Unit(s.t - nrm * Vector3.Dot(s.t, nrm), s.t);   // toward the head, in the surface
                Vector3 across = Vector3.Cross(nrm, fwd);
                float h = Mathf.Lerp(heightStart, heightEnd, f), w = Mathf.Lerp(widthStart, widthEnd, f), l = gap * lengthFraction;
                Vector3 b = onBody - nrm * (inset * h);
                float hw = 0.5f * w, hl = 0.5f * l, kw = 0.5f * w * keelWidth;
                // base corners FL, FR, BL, BR; ridge front RF (lower), back RB (taller) - two ridge edges for a flat keel
                pts[0] = b + fwd * hl - across * hw; pts[1] = b + fwd * hl + across * hw;
                pts[2] = b - fwd * hl - across * hw; pts[3] = b - fwd * hl + across * hw;
                Vector3 rf = onBody + fwd * (0.6f * hl) + nrm * (h * frontHeight), rb = onBody - fwd * (0.7f * hl) + nrm * h;
                pts[4] = rf; pts[5] = rb;
                Vector3 rfL = rf - across * kw, rfR = rf + across * kw, rbL = rb - across * kw, rbR = rb + across * kw;
                Vector4 skin = FinStripMesh.SpineSkin(u, joints);
                float arc = headLen + sp.spacing * u, v = 0.25f + 0.5f * (around / Mathf.PI);
                Vector3 centre = onBody - nrm * h;   // below the scute: every facet faces away from it
                Face(m, centre, nrm, sn, arc, v, skin, pts[0], pts[1], rfR, rfL);   // front
                Face(m, centre, nrm, sn, arc, v, skin, pts[3], pts[2], rbL, rbR);   // back
                Face(m, centre, nrm, sn, arc, v, skin, pts[2], pts[0], rfL, rbL);   // left flank
                Face(m, centre, nrm, sn, arc, v, skin, pts[1], pts[3], rbR, rfR);   // right flank
                if (kw > 1e-6f) Face(m, centre, nrm, sn, arc, v, skin, rfL, rfR, rbR, rbL);   // the keel's top
            }
        }

        // One flat-shaded quad (a, b, c, d in order round it), wound and lit facing away from `centre`.
        void Face(PartMeshEmitter m, Vector3 centre, Vector3 up, float dorsal, float arc, float v, Vector4 skin, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 nn = Vector3.Cross(b - a, d - a) + Vector3.Cross(d - c, b - c);
            Vector3 mid = 0.25f * (a + b + c + d);
            bool flip = Vector3.Dot(nn, mid - centre) < 0f;
            if (flip) nn = -nn;
            nn = PartMeshEmitter.Unit(nn, up);
            int i0 = m.VertexCount;
            m.Vertex(a, nn, arc, v, layer, dorsal, skin);
            m.Vertex(b, nn, arc, v, layer, dorsal, skin);
            m.Vertex(c, nn, arc, v, layer, dorsal, skin);
            m.Vertex(d, nn, arc, v, layer, dorsal, skin);
            // (a, b, d) and (b, c, d); Tri's flip reverses each
            m.Tri(i0, i0 + 1, i0 + 3, flip);
            m.Tri(i0 + 1, i0 + 2, i0 + 3, flip);
        }
    }
}
