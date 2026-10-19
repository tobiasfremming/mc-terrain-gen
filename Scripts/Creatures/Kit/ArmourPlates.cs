// Creature kit: ArmourPlates - a row of hard dorsal plates over a spine body, one plate every `step` joints from
// at.joint to toJoint: centipede and millipede tergites, woodlouse and trilobite segments, armadillo bands, an
// ankylosaur's back. Each plate is a shallow domed shell sized from the body's own cross-section at its joint (half
// width x `width`, heights in units of the body's half height), reaching `overlap` of the plate pitch past half way
// to its neighbours, rounded at the corners, with a raised bead along its back and side margins (`rim`) and
// longitudinal sutures (`grooves`). Shingled: a plate's back margin is `slope` higher than its front, so every plate
// tucks under the one before it and its back edge shows as a step - the seam - on the plate behind. When the body
// bends, the plates slide over each other on the inside of the curve and part on the outside, showing the softer
// body between them (rigid plates cannot crease or invert). Optional, for a more aggressive armour (0 = off,
// bit-identical): `keel`, a sharp ridge along each plate's midline; `cornerSpike`, the back corners drawn out sideways
// and backward into upturned points, so the plate margins read as a row of thorns.
// Mesh only, no bones: every vertex of a plate is rigid to its joint's spine bone. Material layer 10 (armour plate,
// Chitin by default; `layer` picks another). Author at scale 1 (the body's joints are).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("ArmourPlates", PartKind.Rigid, Bones = "0 (each plate rigid to its joint)", Summary = "A row of hard shingled dorsal plates over a spine body, one every `step` joints: sized from the body's cross-section (width x half width, heights x half height), domed, rounded corners, a raised rim along the back and side margins, longitudinal sutures; each tucks under the plate before it, so the seams show as steps; they slide over each other and part on a bend.",
          System = "mesh only (ArmourPlates.EmitMesh), each plate rigid to its joint's bone", MaterialLayers = new[] { 10 }, KitOnly = true,
          UseWhen = "segmented armour: centipede / millipede tergites, woodlice, trilobites, armadillo bands, ankylosaur back plates")]
    public sealed class ArmourPlates : CreaturePart
    {
        [Param(ParamUnit.Joint, "the last plate's joint (the first is at.joint)")] public int toJoint;
        [Param(ParamUnit.Count, "joints from one plate to the next (1 = a plate on every joint)", Min = 1, Max = 8, Default = "1")] public int step = 1;
        [Param(ParamUnit.Fraction, "plate half width x the body's half width at its joint (> 1: the plate overhangs the flanks)", Min = 0.5f, Max = 2, Default = "1.1")] public float width = 1.1f;
        [Param(ParamUnit.Fraction, "the plate's side margin above the joint centre, x the body's half height", Min = -1, Max = 1.5f, Default = "0.2")] public float edgeHeight = 0.2f;
        [Param(ParamUnit.Fraction, "dome: the midline above the margin, x the body's half height", Min = 0, Max = 2, Default = "1")] public float crown = 1f;
        [Param(ParamUnit.Fraction, "dome flatness: 2 = round, 4 = flat-topped with rounded shoulders", Min = 1.2f, Max = 8, Default = "3")] public float flatness = 3f;
        [Param(ParamUnit.Fraction, "shingling: the back margin is this much higher than the front (x half height); the front tucks under the plate before", Min = 0, Max = 1, Default = "0.25")] public float slope = 0.25f;
        [Param(ParamUnit.Fraction, "how far a plate reaches past half way to its neighbours, x the plate pitch", Min = 0, Max = 0.5f, Default = "0.15")] public float overlap = 0.15f;
        [Param(ParamUnit.Fraction, "raised bead along the back and side margins, x half height (0 = none)", Min = 0, Max = 0.5f, Default = "0.1")] public float rim = 0.1f;
        [Param(ParamUnit.Count, "longitudinal sutures (grooves) per side of the midline", Min = 0, Max = 3, Default = "1")] public int grooves = 1;
        [Param(ParamUnit.Fraction, "suture depth, x half height", Min = 0, Max = 0.3f, Default = "0.06")] public float grooveDepth = 0.06f;
        [Param(ParamUnit.Fraction, "the plate's edge wall (its thickness at the margin), x half height", Min = 0.02f, Max = 1, Default = "0.25")] public float thickness = 0.25f;
        [Param(ParamUnit.Fraction, "planform: rounding of the four corners (0 = square, 0.9 = nearly oval)", Min = 0, Max = 0.9f, Default = "0.35")] public float cornerRound = 0.35f;
        [Param(ParamUnit.Fraction, "a sharp ridge along each plate's midline, x half height (0 = none)", Min = 0, Max = 1)] public float keel;
        [Param(ParamUnit.Fraction, "the back corners drawn out sideways (x the plate's half width) and backward into upturned points (0 = none)", Min = 0, Max = 0.6f)] public float cornerSpike;
        [Param(ParamUnit.Fraction, "front plate: x the plate pitch it reaches forward (a head shield's collar); 0 = like the others", Min = 0, Max = 2)] public float firstReach;
        [Param(ParamUnit.Count, "material layer (part id): 10 = armour plate; 0 body, 2 claw", Min = 0, Max = 15, Default = "10")] public int layer = PartMeshEmitter.PartPlate;
        [Param(ParamUnit.Count, "mesh: vertices across a plate", Min = 6, Max = 40, Default = "18")] public int across = 18;
        [Param(ParamUnit.Count, "mesh: rows along a plate", Min = 3, Max = 24, Default = "8")] public int along = 8;

        public override int BoneCount(in SpeciesCtx c) => 0;
        public override int MeshOrder => 100;

        public int Step => Mathf.Max(1, step);
        public int PlateCount => toJoint >= at.joint ? (toJoint - at.joint) / Step + 1 : 0;
        public int PlateJoint(int k) => at.joint + k * Step;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: ArmourPlates need a SpineBody";
            if (at.joint < 0 || at.joint >= c.Joints) return $"{id}: joint {at.joint} outside 0..{c.Joints - 1}";
            if (toJoint < at.joint || toJoint >= c.Joints) return $"{id}: toJoint {toJoint} outside {at.joint}..{c.Joints - 1}";
            if (step < 1 || step > 8) return $"{id}: step must be 1..8";
            if (!(width >= 0.5f && width <= 2f)) return $"{id}: width must be in 0.5..2";
            if (!(flatness >= 1.2f && flatness <= 8f)) return $"{id}: flatness must be in 1.2..8";
            if (crown < 0f || slope < 0f || rim < 0f || grooveDepth < 0f) return $"{id}: crown, slope, rim, grooveDepth must be >= 0";
            if (keel < 0f || keel > 1f || cornerSpike < 0f || cornerSpike > 0.6f) return $"{id}: keel 0..1, cornerSpike 0..0.6";
            if (overlap < 0f || overlap > 0.5f) return $"{id}: overlap must be in 0..0.5";
            if (!(thickness >= 0.02f)) return $"{id}: thickness must be >= 0.02";
            if (cornerRound < 0f || cornerRound > 0.9f) return $"{id}: cornerRound must be in 0..0.9";
            if (grooves < 0 || grooves > 3) return $"{id}: grooves must be 0..3";
            if (layer < 0 || layer >= PartLayers.MaxParts || (layer >= 4 && layer <= 7)) return $"{id}: layer must be a spine-shader part id (0..3, 8..15)";
            if (across < 6 || across > 40 || along < 3 || along > 24) return $"{id}: across 6..40, along 3..24";
            return null;
        }

        // ---------------------------------------------------------------- the shape (joint frame, scale 1; pure)

        // One plate's measures: the body's half width / height at its joint, its front and back reach along z.
        public struct Plate { public int joint; public float hw, hh, zFront, zBack, w; }

        public Plate PlateAt(int k, float spacing, float hw, float hh)
        {
            float pitch = Step * spacing;
            float reach = (0.5f + overlap) * pitch;
            float front = k == 0 && firstReach > 0f ? firstReach * pitch : reach;
            return new Plate { joint = PlateJoint(k), hw = hw, hh = hh, zFront = front, zBack = -reach, w = width * hw };
        }

        // Half width of the planform at s (0 = front edge .. 1 = back edge): rounded corners.
        public float HalfWidthAt(in Plate p, float s)
        {
            float rc = Mathf.Max(1e-3f, cornerRound);
            float e = Mathf.Max(0f, (Mathf.Abs(2f * s - 1f) - (1f - rc)) / rc);   // 0 inside, 1 at the ends
            float w = p.w * (1f - 0.5f * cornerRound * (1f - Mathf.Sqrt(Mathf.Max(0f, 1f - e * e))) - 0.5f * cornerRound * e * e);
            if (cornerSpike > 0f) w += p.w * cornerSpike * SpikeAt(s);
            return w;
        }

        // The corner spike's profile along the plate (0 front .. 1 back): it grows from the middle, peaks sharply at
        // kSpikeAt and falls to nothing at the back margin.
        public const float SpikeAt0 = 0.35f, SpikePeak = 0.8f;
        public static float SpikeAt(float s)
        {
            if (s <= SpikeAt0) return 0f;
            if (s < SpikePeak) { float x = (s - SpikeAt0) / (SpikePeak - SpikeAt0); return x * x * x; }
            return Mathf.Clamp01(1f - (s - SpikePeak) / (1f - SpikePeak));
        }

        // How much of the spike a point at lateral u carries (its swept-back, upturned tip at the margin).
        static float SpikeEdge(float au) { float a2 = au * au; return a2 * a2 * a2; }

        static float Bump(float d, float width) { float x = d / width; return Mathf.Exp(-x * x); }

        // Height of the top surface at lateral u (-1..1 of the half width at s) and s (0 front .. 1 back), joint frame.
        public float TopY(in Plate p, float u, float s)
        {
            float au = Mathf.Min(1f, Mathf.Abs(u));
            float dome = Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(au, flatness)), 1f / flatness);
            float y = edgeHeight + crown * dome + slope * (s - 0.5f) + thickness;
            // margins: a bead along the back edge and the sides, strongest at the back corners
            float bead = Mathf.Max(Bump(1f - au, 0.07f), Bump(1f - s, 0.07f));
            y += rim * bead;
            // the keel: a sharp crest along the midline, faded in from the front margin
            if (keel > 0f)
            {
                float k = Mathf.Max(0f, 1f - au / 0.2f);
                y += keel * k * Mathf.Sqrt(k) * Mathf.Clamp01(s / 0.3f);
            }
            if (cornerSpike > 0f) y += 0.5f * cornerSpike * SpikeAt(s) * SpikeEdge(au);   // the spike tips turn up
            // sutures: grooves at evenly spaced places across each half (fading toward the front and back margins)
            if (grooves > 0 && grooveDepth > 0f)
            {
                float fade = Mathf.Clamp01(s / 0.15f) * Mathf.Clamp01((1f - s) / 0.15f);
                for (int g = 1; g <= grooves; g++)
                    y -= grooveDepth * fade * Bump(au - g / (grooves + 1f) * 0.85f, 0.05f);
            }
            return p.hh * y;
        }

        // The underside (hidden inside the body): the margin, sagging toward the midline.
        public float BottomY(in Plate p, float u, float s)
        {
            float au = Mathf.Min(1f, Mathf.Abs(u));
            float y = edgeHeight + slope * (s - 0.5f) - 0.6f * (1f - au * au);
            if (cornerSpike > 0f) y += 0.5f * cornerSpike * SpikeAt(s) * SpikeEdge(au);
            return p.hh * y;
        }

        public float Z(in Plate p, float s) => Mathf.Lerp(p.zFront, p.zBack, s);

        // ... at lateral u: the corner spikes sweep back.
        public float Z(in Plate p, float u, float s)
        {
            float z = Mathf.Lerp(p.zFront, p.zBack, s);
            if (cornerSpike > 0f) z -= 0.6f * cornerSpike * (p.zFront - p.zBack) * SpikeAt(s) * SpikeEdge(Mathf.Min(1f, Mathf.Abs(u)));
            return z;
        }

        public Vector3 Top(in Plate p, float u, float s) => new Vector3(u * HalfWidthAt(p, s), TopY(p, u, s), Z(p, u, s));
        public Vector3 Bottom(in Plate p, float u, float s) => new Vector3(u * HalfWidthAt(p, s), BottomY(p, u, s), Z(p, u, s));

        // ---------------------------------------------------------------- the mesh

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            CreatureSim rest = m.Rest;
            if (rest == null) return;
            CreatureSpecies sp = m.Species;
            int nu = Mathf.Clamp(across, 6, 40), ns = Mathf.Clamp(along, 3, 24);
            for (int k = 0; k < PlateCount; k++)
            {
                int j = Mathf.Clamp(PlateJoint(k), 0, m.Joints - 1);
                Plate p = PlateAt(k, sp.spacing, m.HW(j), m.HH(j));
                EmitPlate(m, p, rest.Joints[j], rest.Rotations[j], sp.headCapLength + sp.spacing * j, nu, ns);
            }
        }

        // u across from -1 (left) to +1 (right), cosine-spaced (dense at the margins, where the bead is)
        static float U(int i, int nu) => -Mathf.Cos(Mathf.PI * i / nu);
        static float S(int r, int ns) => 0.5f - 0.5f * Mathf.Cos(Mathf.PI * r / ns);

        void EmitPlate(PartMeshEmitter m, in Plate p, Vector3 jp, Quaternion jr, float uBase, int nu, int ns)
        {
            Vector4 skin = PartMeshEmitter.Rigid(p.joint);
            Vector3 jUp = jr * Vector3.up;
            int part = layer;
            float len = p.zFront - p.zBack;

            // top surface: positions once, normals from the grid's own neighbours (what the mesh can show)
            int c = nu + 1;
            var tp = new Vector3[(ns + 1) * c];
            for (int r = 0; r <= ns; r++)
                for (int i = 0; i <= nu; i++) tp[r * c + i] = Top(p, U(i, nu), S(r, ns));
            int top = m.VertexCount;
            for (int r = 0; r <= ns; r++)
                for (int i = 0; i <= nu; i++)
                {
                    Vector3 q = tp[r * c + i];
                    Vector3 du = tp[r * c + Mathf.Min(i + 1, nu)] - tp[r * c + Mathf.Max(i - 1, 0)];
                    Vector3 ds = tp[Mathf.Max(r - 1, 0) * c + i] - tp[Mathf.Min(r + 1, ns) * c + i];   // toward the front
                    Vector3 nl = Vector3.Cross(ds, du);
                    if (nl.y < 0f) nl = -nl;
                    Vector3 wn = PartMeshEmitter.Unit(jr * nl, jUp);
                    m.Vertex(jp + jr * q, wn, uBase - q.z, (U(i, nu) + 1f) * 0.25f, part, Vector3.Dot(wn, jUp), skin);
                }
            FixGrid(m, top, ns, nu, jUp);

            // underside (faces down)
            int bot = m.VertexCount;
            for (int r = 0; r <= ns; r++)
                for (int i = 0; i <= nu; i++)
                {
                    float u = U(i, nu), s = S(r, ns);
                    Vector3 q = Bottom(p, u, s);
                    Vector3 wn = -jUp;
                    m.Vertex(jp + jr * q, wn, uBase - q.z, (u + 1f) * 0.25f, part, -1f, skin);
                }
            FixGrid(m, bot, ns, nu, -jUp);

            // the edge wall round the boundary: front (s = 0, u -1..1), right side (u = 1), back (s = 1), left side
            // (grid column i, row r) of each boundary point, so the wall reuses the top's positions
            var ring = new List<int>(2 * (nu + ns));   // grid index r * c + i
            for (int i = 0; i < nu; i++) ring.Add(i);
            for (int r = 0; r < ns; r++) ring.Add(r * c + nu);
            for (int i = nu; i > 0; i--) ring.Add(ns * c + i);
            for (int r = ns; r > 0; r--) ring.Add(r * c);
            int n = ring.Count;
            int wall = m.VertexCount;
            var wallNormal = new Vector3[n + 1];
            for (int k = 0; k <= n; k++)
            {
                int a = ring[k % n];
                Vector3 pa = tp[a];
                Vector3 tan = tp[ring[(k + 1) % n]] - tp[ring[(k - 1 + n) % n]];
                Vector3 outward = Vector3.Cross(tan, Vector3.up);
                outward.y = 0f;
                // outward = away from the plate's middle (the ring is convex in plan)
                Vector3 centre = new Vector3(0f, pa.y, 0.5f * (p.zFront + p.zBack));
                if (Vector3.Dot(outward, pa - centre) < 0f) outward = -outward;
                Vector3 wn = PartMeshEmitter.Unit(jr * outward, jr * Vector3.forward);
                wallNormal[k] = wn;
                Vector3 pb = Bottom(p, U(a % c, nu), S(a / c, ns));
                float arc = (float)k / n;
                m.Vertex(jp + jr * pa, wn, uBase - pa.z, arc, part, 0f, skin);
                m.Vertex(jp + jr * pb, wn, uBase - pb.z, arc, part, -0.5f, skin);
            }
            for (int k = 0; k < n; k++)
            {
                int a0 = wall + 2 * k, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
                Vector3 fa = m.Position(a0), fb = m.Position(a1), fc = m.Position(b1);
                bool flip = Vector3.Dot(Vector3.Cross(fb - fa, fc - fa), wallNormal[k] + wallNormal[k + 1]) < 0f;
                m.Tri(a0, a1, b1, flip);
                m.Tri(a0, b1, b0, flip);
            }
        }

        // Two triangles per grid quad wound so they face along `facing` (each quad decided on its own: the grid may
        // be curved but never folds).
        static void FixGrid(PartMeshEmitter m, int first, int rows, int cols, Vector3 facing)
        {
            int c = cols + 1;
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    int a = first + j * c + i, b = a + 1, d = a + c, e = d + 1;
                    Vector3 pa = m.Position(a), pb = m.Position(b), pd = m.Position(d);
                    bool flip = Vector3.Dot(Vector3.Cross(pb - pa, pd - pa), facing) < 0f;
                    m.Tri(a, b, d, flip);
                    m.Tri(b, e, d, flip);
                }
        }
    }
}
