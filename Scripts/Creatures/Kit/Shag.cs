// Creature kit (2026-10-20, the Delver): Shag - shaggy fur as hanging locks: flattened, tapering, drooping strands
// rooted in a band round the body (from dorsalFrom to dorsalTo: -1 the belly's midline .. +1 the back's) over a run of
// joints, staggered row by row, each one's length jittered (a hashed, repeatable shag), falling out and down (droop)
// and swept toward the tail (sweep); and optionally on the upper bones of named limbs (a shaggy chest and thighs). A lock
// never reaches the ground of the rest pose (groundClear). Each lock is rigid to its nearest joint (or its limb bone):
// the fur rides the body's bends without stretching. Material layer `layer` (0 the body's skin by default; no fur row of its
// own - the layer table is full: put it on an id the species does not use and restyle that row with Layers).
// Mesh only. Kit-only; off unless a species names it.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("Shag", PartKind.Rigid, Bones = "0 (each lock rigid to its joint or limb bone)", Summary = "Shaggy fur: flattened tapering locks rooted in a band round the body over a run of joints (and on named limbs' upper bones), staggered, length-jittered, drooping and swept back, never reaching the ground.",
          System = "mesh only (Shag.EmitMesh)", MaterialLayers = new[] { 0 }, KitOnly = true,
          UseWhen = "furry or shaggy underbodies, manes, beards, chest ruffs, hairy thighs; any coat of locks on a spine body")]
    public sealed class Shag : CreaturePart
    {
        [Param(ParamUnit.Joint, "last joint of the run (the first is at.joint)")] public int toJoint;
        [Param(ParamUnit.Fraction, "rows of locks per joint step along the run", Min = 0.5f, Max = 8, Default = "2")] public float perJoint = 2f;
        [Param(ParamUnit.Count, "rows round the body on each side (and under the midline)", Min = 1, Max = 16, Default = "4")] public int around = 4;
        [Param(ParamUnit.Fraction, "the band round the body: from (-1 the belly's midline)", Min = -1, Max = 1, Default = "-1")] public float dorsalFrom = -1f;
        [Param(ParamUnit.Fraction, "the band round the body: to (+1 the back's midline)", Min = -1, Max = 1, Default = "-0.2")] public float dorsalTo = -0.2f;
        [Param(ParamUnit.Metres, "lock length", Example = "delver 0.05")] public float length = 0.04f;
        [Param(ParamUnit.Fraction, "length jitter (+-)", Min = 0, Max = 0.9f, Default = "0.35")] public float jitter = 0.35f;
        [Param(ParamUnit.Metres, "lock width at the root")] public float width = 0.012f;
        [Param(ParamUnit.Fraction, "0 straight out from the skin .. 1 hanging straight down", Min = 0, Max = 1, Default = "0.6")] public float droop = 0.6f;
        [Param(ParamUnit.Fraction, "sweep toward the tail", Min = 0, Max = 1, Default = "0.35")] public float sweep = 0.35f;
        [Param(ParamUnit.Fraction, "the locks taper off over the run's first and last joints (fraction of the run)", Min = 0, Max = 0.5f, Default = "0.15")] public float fade = 0.15f;
        [Param(ParamUnit.Fraction, "a lock's tip stays above this fraction of its root's height over the rest pose's ground", Min = 0, Max = 0.9f, Default = "0.25")] public float groundClear = 0.25f;
        [Param(ParamUnit.Text, "limbs whose upper bones get locks too (ids; a mirrored limb's base id names both)")] public string[] limbs = new string[0];
        [Param(ParamUnit.Fraction, "locks on a limb, x length", Min = 0, Max = 2, Default = "0.8")] public float limbLength = 0.8f;
        [Param(ParamUnit.Count, "material layer (0 the body's skin; a species may put its fur on any id it does not use and restyle that row with Layers - the Delver: 12)", Min = 0, Max = 15, Default = "0")] public int layer;

        public override int BoneCount(in SpeciesCtx c) => 0;
        protected override void CopyArrays() { limbs = (string[])limbs?.Clone() ?? new string[0]; }

        public override void CollectReferences(List<string> into) { }   // mirrored base ids are matched loosely (Tuck.Names)

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: Shag needs a SpineBody";
            if (at.joint < 0 || toJoint >= c.Joints || toJoint < at.joint) return $"{id}: joints {at.joint}..{toJoint} must lie in 0..{c.Joints - 1}, first <= last";
            if (perJoint < 0.5f || perJoint > 8f || around < 1 || around > 16) return $"{id}: perJoint 0.5..8, around 1..16";
            if (dorsalFrom < -1f || dorsalTo > 1f || !(dorsalTo > dorsalFrom)) return $"{id}: -1 <= dorsalFrom < dorsalTo <= 1";
            if (!(length > 0f) || !(width > 0f) || jitter < 0f || jitter > 0.9f) return $"{id}: length, width > 0, jitter 0..0.9";
            if (droop < 0f || droop > 1f || sweep < 0f || sweep > 1f || fade < 0f || fade > 0.5f || groundClear < 0f || groundClear > 0.9f) return $"{id}: droop, sweep 0..1, fade 0..0.5, groundClear 0..0.9";
            if (layer < 0 || layer >= PartLayers.MaxParts || (layer >= 4 && layer <= 7)) return $"{id}: layer must be a spine-shader part id (0..3, 8..15)";
            if (limbs != null)
                foreach (string l in limbs)
                {
                    bool found = false;
                    foreach (CreaturePart p in c.Parts) if (p is LimbPart && Tuck.Names(l, p.id)) found = true;
                    if (!found) return $"{id}: limb '{l}' names no limb";
                }
            return null;
        }

        static float Hash01(int k)
        {
            uint x = (uint)k * 0x9E3779B1u;
            x ^= x >> 16; x *= 0x7FEB352Du; x ^= x >> 15; x *= 0x846CA68Bu; x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777216f;
        }

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            CreatureSpecies sp = m.Species;
            int rows = Mathf.Max(1, Mathf.RoundToInt((toJoint - at.joint) * perJoint) + 1);
            int seed = 7;
            // angle round the section: dorsal d = sin(angle); the band from dorsalFrom to dorsalTo on both sides
            float aFrom = Mathf.Asin(Mathf.Clamp(dorsalFrom, -1f, 1f)), aTo = Mathf.Asin(Mathf.Clamp(dorsalTo, -1f, 1f));
            for (int r = 0; r < rows; r++)
            {
                float f = rows == 1 ? 0.5f : (float)r / (rows - 1);
                float fadeW = fade <= 0f ? 1f : Mathf.Clamp01(Mathf.Min(f, 1f - f) / fade);
                float u = Mathf.Lerp(at.joint, toJoint, f);
                SpineSection s = m.SpineAt(u);
                int bone = Mathf.Clamp(Mathf.RoundToInt(u), 0, m.Joints - 1);
                for (int side = -1; side <= 1; side += 2)
                    for (int k = 0; k < around; k++)
                    {
                        // staggered by half a row on odd rows
                        float g = (k + ((r & 1) == 1 ? 0.5f : 0.25f)) / around;
                        float ang = Mathf.Lerp(aFrom, aTo, g);
                        float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                        Vector3 root = s.c + s.x * (side * cs * s.a * 0.96f) + s.y * (sn * s.b * 0.96f);
                        Vector3 nrm = PartMeshEmitter.Unit(s.x * (side * cs / Mathf.Max(1e-4f, s.a)) + s.y * (sn / Mathf.Max(1e-4f, s.b)), Vector3.down);
                        float len = length * fadeW * (1f + jitter * (2f * Hash01(seed++) - 1f));
                        Lock(m, root, nrm, -s.t, len, bone, u);
                    }
            }
            if (limbs == null || limbs.Length == 0) return;
            IReadOnlyList<CreaturePart> parts = KitView.Parts(sp);
            int[] ordinal = KitView.Ordinals(parts);
            for (int i = 0; i < parts.Count; i++)
            {
                if (!(parts[i] is LimbPart lp)) continue;
                bool named = false;
                foreach (string l in limbs) if (Tuck.Names(l, lp.id)) named = true;
                if (!named) continue;
                int leg = ordinal[i];
                LegState st = m.Rest.Legs[leg];
                LegSpec spec = sp.legs[leg];
                int bu = sp.LegBone(leg, 0);
                Vector3 S = st.shoulder, E = st.elbow;
                Vector3 axis = PartMeshEmitter.Unit(E - S, Vector3.down);
                Vector3 back = -(m.Rest.Rotations[spec.attachJoint] * Vector3.forward);
                Vector3 x0 = PartMeshEmitter.Unit(Vector3.Cross(axis, back), Vector3.right), y0 = Vector3.Cross(axis, x0);
                float rad = spec.upperRadius * 1.05f;
                const int along = 3, ring = 6;
                for (int a = 0; a < along; a++)
                    for (int k = 0; k < ring; k++)
                    {
                        float t = (a + 0.5f) / along * 0.8f;
                        float phi = 2f * Mathf.PI * (k + 0.5f * (a & 1)) / ring;
                        Vector3 dir = x0 * Mathf.Cos(phi) + y0 * Mathf.Sin(phi);
                        Vector3 root = S + axis * (t * (E - S).magnitude) + dir * rad;
                        float len = length * limbLength * (1f + jitter * (2f * Hash01(seed++) - 1f));
                        Lock(m, root, dir, back, len, bu, spec.attachJoint);
                    }
            }
        }

        // One lock: a flattened tapering strand from root along the skin normal, bending down (droop) and back (sweep),
        // rigid to `bone`; four sides, flat across its width.
        void Lock(PartMeshEmitter m, Vector3 root, Vector3 nrm, Vector3 back, float len, int bone, float arcU)
        {
            if (len <= 1e-5f) return;
            // the tip stays above groundClear of the root's height over the rest ground (y = 0)
            float h = root.y;
            Vector3 down = Vector3.down;
            Vector3 d0 = PartMeshEmitter.Unit(nrm + back * sweep * 0.5f, down);
            Vector3 d1 = PartMeshEmitter.Unit(Vector3.Lerp(nrm, down, droop) + back * sweep, down);
            Vector3 P(float t)
            {
                // a quadratic: leaves along d0, ends along d1
                Vector3 mid = root + d0 * (0.5f * len);
                Vector3 tip = mid + d1 * (0.5f * len);
                Vector3 a = Vector3.Lerp(root, mid, t), b = Vector3.Lerp(mid, tip, t);
                return Vector3.Lerp(a, b, t);
            }
            Vector3 end = P(1f);
            float minY = groundClear * h;
            if (end.y < minY && root.y - end.y > 1e-5f)
            {
                float k = Mathf.Clamp01((root.y - minY) / (root.y - end.y));
                len *= k;
                if (len <= 1e-5f) return;
            }
            Vector4 skin = PartMeshEmitter.Rigid(bone);
            const int rings = 4, sides = 4;
            float w = width * Mathf.Sqrt(Mathf.Clamp01(len / Mathf.Max(1e-5f, length)) + 0.2f);
            // emitted directly (no loft: a coat is hundreds of locks): flat elliptical rings, analytic normals, a pole at
            // the root (inside the skin) and at the tip
            int root0 = m.VertexCount;
            m.Vertex(root - d0 * (0.15f * w), -d0, arcU, 0f, layer, -d0.y, skin);
            int firstRing = m.VertexCount;
            for (int i = 0; i < rings; i++)
            {
                float t = (float)i / (rings - 1 + 0.6f);
                Vector3 c = P(t), tan = PartMeshEmitter.Unit(P(Mathf.Min(1f, t + 0.02f)) - P(Mathf.Max(0f, t - 0.02f)), d1);
                Vector3 across = PartMeshEmitter.Unit(Vector3.Cross(tan, back), Vector3.Cross(tan, Vector3.right));
                Vector3 flat = Vector3.Cross(tan, across);
                float rw = w * (1f - 0.75f * t), rt = 0.3f * rw;
                for (int k = 0; k < sides; k++)
                {
                    float phi = 2f * Mathf.PI * k / sides, cs = Mathf.Cos(phi), sn = Mathf.Sin(phi);
                    Vector3 nn = PartMeshEmitter.Unit(across * (cs / rw) + flat * (sn / rt), across);
                    m.Vertex(c + across * (cs * rw) + flat * (sn * rt), nn, arcU + len * t, (float)k / sides, layer, nn.y, skin);
                }
            }
            Vector3 tipDir = PartMeshEmitter.Unit(P(1f) - P(0.97f), d1);
            int tip = m.VertexCount;
            m.Vertex(P(1f), tipDir, arcU + len, 0f, layer, tipDir.y, skin);
            for (int k = 0; k < sides; k++)
            {
                int k1 = (k + 1) % sides;
                Tri(m, root0, firstRing + k1, firstRing + k);
                for (int i = 0; i + 1 < rings; i++)
                {
                    int a0 = firstRing + i * sides + k, a1 = firstRing + i * sides + k1, b0 = a0 + sides, b1 = a1 + sides;
                    Tri(m, a0, a1, b0); Tri(m, a1, b1, b0);
                }
                int l0 = firstRing + (rings - 1) * sides;
                Tri(m, l0 + k, l0 + k1, tip);
            }
        }

        // a triangle wound outward: its face normal on the side of the vertex normals (emitter's own normals)
        static void Tri(PartMeshEmitter m, int a, int b, int c)
        {
            Vector3 pa = m.Position(a);
            Vector3 fn = Vector3.Cross(m.Position(b) - pa, m.Position(c) - pa);
            m.Tri(a, b, c, Vector3.Dot(fn, m.Normal(a) + m.Normal(b) + m.Normal(c)) < 0f);
        }
    }
}
