// Creature kit: Sculpt - a solid modelled as a signed-distance field (SDF) and polygonised once per species: a
// realistic head (skull, brow over a recessed eye socket, jowls, a lip line, nostrils) that superellipse lofts cannot
// express. The field is a sequence of shapes in the joint frame - ellipsoids, round cones (tapered capsules, optionally
// flattened), rounded boxes and planes - each combined with what came before by a smooth union (blend radius), a smooth
// subtraction (eye sockets, nostrils, a lip groove) or a smooth intersection (trim with a plane: a flat skull table),
// optionally mirrored across the midline. Surface nets (SculptMesher) turn it into a closed mesh on a grid of `cell`
// metres: one vertex per surface cell, moved onto the surface by a Newton step, normals from the field's gradient (no
// faceted glow), every triangle wound against the gradient (none inverted). Tooth rows ride its surface: each tooth
// is placed where its guide line leaves the solid (the lip line), sunk into the gum by `sink`, pointing `direction`.
// Rigid: every vertex is skinned to one bone - its joint's (the spine's rigid head) or, with `follow`, the bone of a
// Jaw part (a lower jaw that drops): it never deforms or inverts. Build cost only (no runtime): the polygonised piece
// is cached per shape set (SculptMesher.For), so every build after the first is a copy. Kit-only part; main thread.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public enum SculptKind { Ellipsoid = 0, RoundCone = 1, Box = 2, Plane = 3 }
    public enum SculptOp { Union = 0, Subtract = 1, Intersect = 2 }

    // One shape of a Sculpt's field.
    [Serializable]
    public struct SculptShape
    {
        [Param(ParamUnit.Enum, "Ellipsoid | RoundCone (a tapered capsule from centre to end) | Box (rounded) | Plane (solid below it; for Intersect / Subtract)")] public SculptKind kind;
        [Param(ParamUnit.Enum, "Union (add) | Subtract (carve: a socket, a nostril, a groove) | Intersect (trim)")] public SculptOp op;
        [Param(ParamUnit.Metres, "smooth blend radius with the shapes before it (0 = a sharp crease)")] public float blend;
        [Param(ParamUnit.Metres, "centre in the joint frame (RoundCone: its start; Plane: a point on it)")] public Vector3 centre;
        [Param(ParamUnit.Metres, "Ellipsoid radii / Box half extents; RoundCone: x = radius at the start, y = radius at the end")] public Vector3 size;
        [Param(ParamUnit.Metres, "RoundCone: its end in the joint frame")] public Vector3 end;
        [Param(ParamUnit.Degrees, "rotation of an Ellipsoid, Box or Plane: x pitch (+ nose down), y yaw (+ toward +x), z roll; a Plane's normal is its rotated +y")] public Vector3 rotationDeg;
        [Param(ParamUnit.Metres, "Box: corner rounding")] public float round;
        [Param(ParamUnit.Fraction, "RoundCone: its cross-section's height / width (0 or 1 = round, 0.5 = flattened)", Min = 0, Max = 4)] public float squash;
        [Param(ParamUnit.Flag, "also its mirror image across the midline (x -> -x): eye sockets, nostrils, jowls (an Intersect trims by both)")] public bool mirror;
    }

    // A row of teeth on a Sculpt's surface.
    [Serializable]
    public struct SculptTeeth
    {
        [Param(ParamUnit.Metres, "guide line start in the joint frame (just outside the lip, on the side the teeth come out)")] public Vector3 from;
        [Param(ParamUnit.Metres, "guide line end")] public Vector3 to;
        [Param(ParamUnit.Metres, "the guide line bows by this much at its middle (a curved tooth row)")] public Vector3 bow;
        [Param(ParamUnit.Count, "teeth in the row", Min = 1, Max = 32)] public int count;
        [Param(ParamUnit.Metres, "tooth length (the end teeth 0.6 x)")] public float length;
        [Param(ParamUnit.Metres, "tooth base radius")] public float radius;
        [Param(ParamUnit.Direction, "the way the teeth point (an upper row: down), in the joint frame; each tooth sits where this line leaves the solid")] public Vector3 direction;
        [Param(ParamUnit.Degrees, "teeth splay outward (away from the midline) by this much", Min = -45, Max = 45)] public float splayDeg;
        [Param(ParamUnit.Degrees, "each tooth curls back by this much", Min = -60, Max = 60)] public float curveDeg;
        [Param(ParamUnit.Fraction, "tooth lengths vary by up to this fraction (deterministic)", Min = 0, Max = 0.9f)] public float jitter;
        [Param(ParamUnit.Fraction, "sunk into the gum by this fraction of its length", Min = 0, Max = 0.9f)] public float sink;
        [Param(ParamUnit.Flag, "also the mirror row (x -> -x)")] public bool mirror;
    }

    [Serializable]
    [Part("Sculpt", PartKind.Rigid, Bones = "0 (rigid to its joint, or rides a Jaw's bone)", Summary = "A solid modelled as a signed-distance field: ellipsoids, round cones, rounded boxes and planes in the joint frame, smooth-unioned, carved (smooth subtraction) and trimmed (smooth intersection), mirrored across the midline; polygonised once per species by surface nets with normals from the field's gradient, plus tooth rows placed on its lip line. Rigid to its joint or riding a Jaw's bone.",
          System = "mesh only (Sculpt.EmitMesh -> SculptMesher, cached per shape set), rigid to one bone", MaterialLayers = new[] { 0, 2, 13 }, KitOnly = true,
          UseWhen = "realistic heads and skulls: brows over recessed eye sockets, jowls, lip lines, nostrils, skull tables, a lower jaw on a Jaw; any rigid organic solid a loft cannot express")]
    public sealed class Sculpt : CreaturePart
    {
        [Param(ParamUnit.Struct, "the field's shapes, combined in order (the first is usually a Union)")] public SculptShape[] shapes = new SculptShape[0];
        [Param(ParamUnit.PartRef, "a Jaw part whose bone it rides (a lower jaw that opens); empty = rigid to its joint")] public string follow = "";
        [Param(ParamUnit.Metres, "grid cell of the polygoniser (smaller: finer and slower; ~1/60 of the head's length)", Example = "crocodile 0.0045")] public float cell = 0.005f;
        [Param(ParamUnit.Count, "material layer (part id): 0 the body's skin, 2 claw / horn", Min = 0, Max = 15)] public int layer;
        [Param(ParamUnit.Metres, "the surface's upward-facing parts (normal within 60 deg of up) below this height (joint frame) are shaded as belly (a mouth's floor and palate show the pale lining, not the back); 0 = off", Min = -1, Max = 1)] public float liningBelow;
        [Param(ParamUnit.Fraction, "shading: added to the dorsal value (-1 belly .. +1 back); -0.5 turns a lower jaw's sides pale like the throat", Min = -2, Max = 2)] public float dorsalShift;
        [Param(ParamUnit.Struct, "tooth rows on the surface")] public SculptTeeth[] teeth = new SculptTeeth[0];
        [Param(ParamUnit.Count, "material layer of the teeth: 2 claw, 13 teeth", Min = 0, Max = 15, Default = "13")] public int toothLayer = 13;

        public const int MaxShapes = 64;
        public const float MinCell = 0.001f;

        public override int BoneCount(in SpeciesCtx c) => 0;
        public override int MeshOrder => 100;
        protected override void CopyArrays() { shapes = (SculptShape[])shapes?.Clone(); teeth = (SculptTeeth[])teeth?.Clone(); }

        public override void CollectReferences(List<string> into) { if (!string.IsNullOrEmpty(follow)) into.Add(follow); }

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a Sculpt needs a SpineBody";
            if (at.joint < 0 || at.joint >= c.Joints) return $"{id}: joint {at.joint} outside 0..{c.Joints - 1}";
            if (shapes == null || shapes.Length == 0 || shapes.Length > MaxShapes) return $"{id}: needs 1..{MaxShapes} shapes";
            if (!(cell >= MinCell)) return $"{id}: cell must be >= {MinCell} m";
            if (layer < 0 || layer >= PartLayers.MaxParts || toothLayer < 0 || toothLayer >= PartLayers.MaxParts) return $"{id}: layers must be in 0..{PartLayers.MaxParts - 1}";
            bool solid = false;
            for (int i = 0; i < shapes.Length; i++)
            {
                SculptShape s = shapes[i];
                if (!(s.blend >= 0f)) return $"{id}: shape {i}: blend must be >= 0";
                if ((int)s.kind < 0 || (int)s.kind > 3 || (int)s.op < 0 || (int)s.op > 2) return $"{id}: shape {i}: unknown kind or op";
                if (s.kind == SculptKind.Plane && s.op == SculptOp.Union) return $"{id}: shape {i}: a Plane is unbounded - Intersect or Subtract it";
                if (s.kind == SculptKind.Ellipsoid && !(s.size.x > 0f && s.size.y > 0f && s.size.z > 0f)) return $"{id}: shape {i}: ellipsoid radii must be > 0";
                if (s.kind == SculptKind.Box && (!(s.size.x > 0f && s.size.y > 0f && s.size.z > 0f) || s.round < 0f || s.round > Mathf.Min(s.size.x, Mathf.Min(s.size.y, s.size.z)))) return $"{id}: shape {i}: box half extents > 0, 0 <= round <= the smallest";
                if (s.kind == SculptKind.RoundCone)
                {
                    float L = (s.end - s.centre).magnitude;
                    if (!(s.size.x > 0f && s.size.y > 0f)) return $"{id}: shape {i}: round cone radii (size.x, size.y) must be > 0";
                    if (!(L > Mathf.Abs(s.size.x - s.size.y) + 1e-5f)) return $"{id}: shape {i}: a round cone must be longer than its radius change";
                    if (s.squash < 0f || s.squash > 4f) return $"{id}: shape {i}: squash must be in 0..4";
                }
                if (s.op == SculptOp.Union) solid = true;
                else if (!solid) return $"{id}: shape {i}: carves or trims before any Union";
            }
            if (teeth != null)
                for (int i = 0; i < teeth.Length; i++)
                {
                    SculptTeeth t = teeth[i];
                    if (t.count < 1 || t.count > 32) return $"{id}: teeth {i}: count must be 1..32";
                    if (!(t.length > 0f) || !(t.radius > 0f)) return $"{id}: teeth {i}: length and radius must be > 0";
                    if (t.direction.sqrMagnitude < 1e-8f) return $"{id}: teeth {i}: direction is zero";
                    if (t.sink < 0f || t.sink > 0.9f || t.jitter < 0f || t.jitter > 0.9f) return $"{id}: teeth {i}: sink and jitter must be in 0..0.9";
                }
            if (!string.IsNullOrEmpty(follow))
            {
                CreaturePart f = c.Find(follow);
                if (!(f is Jaw)) return $"{id}: follow '{follow}' is not a Jaw part";
            }
            return null;
        }

        // The bone it is skinned to: its joint's, or the followed Jaw's.
        public int Bone(CreatureSpecies runtime)
        {
            if (!string.IsNullOrEmpty(follow))
            {
                IReadOnlyList<CreaturePart> parts = KitView.Parts(runtime);
                for (int i = 0; i < parts.Count; i++)
                    if (parts[i] is Jaw && parts[i].id == follow) return runtime.Layout.Start(i);
            }
            return Mathf.Clamp(at.joint, 0, runtime.JointCount - 1);
        }

        // The field (joint frame, scale 1): negative inside.
        public SculptField Field() => new SculptField(shapes);

        // ------------------------------------------------------------ mesh

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            CreatureSpecies sp = m.Species;
            int j = Mathf.Clamp(at.joint, 0, m.Joints - 1);
            Vector3 jp = m.Rest.Joints[j];
            Quaternion jr = m.Rest.Rotations[j];
            int bone = Bone(sp);
            Vector4 skin = PartMeshEmitter.Rigid(bone);
            SculptPiece piece = SculptMesher.For(this);
            Vector3 jUp = jr * Vector3.up, jF = jr * Vector3.forward;
            float uBase = sp.headCapLength + sp.spacing * j;   // the body's uv0.x at this joint (metres from the snout tip)
            int first = m.VertexCount;
            for (int i = 0; i < piece.Pos.Length; i++)
            {
                Vector3 p = piece.Pos[i], n = piece.Nrm[i];
                Vector3 wp = jp + jr * p, wn = PartMeshEmitter.Unit(jr * n, jUp);
                float dorsal = Vector3.Dot(wn, jUp) + dorsalShift;
                if (liningBelow != 0f && p.y < liningBelow && n.y > 0.5f) dorsal = -Mathf.Abs(dorsal);   // a mouth's floor: the pale lining
                m.Vertex(wp, wn, uBase - Vector3.Dot(wp - jp, jF), piece.V[i], layer, dorsal, skin);
            }
            int[] t = piece.Tris;
            for (int k = 0; k < t.Length; k += 3) m.Tri(first + t[k], first + t[k + 1], first + t[k + 2], false);

            // teeth: horns rigid to the same bone, curling back, from the lip line
            for (int k = 0; k < piece.Teeth.Length; k++)
            {
                SculptPiece.Tooth th = piece.Teeth[k];
                Vector3 wb = jp + jr * th.basePt, wd = PartMeshEmitter.Unit(jr * th.dir, -jUp);
                Vector3 back = -jF;
                Quaternion q = Quaternion.LookRotation(wd, PartMeshEmitter.Unit(back - wd * Vector3.Dot(back, wd), jUp));
                m.Horn(wb, q, th.radius, th.radius * 0.15f, th.length, th.curveDeg, bone, toothLayer, 6, uBase - Vector3.Dot(wb - jp, jF), out _, out _);
            }
        }
    }

    // A Sculpt's field, compiled: each shape with its frame, its bounds and its op. Eval is allocation-free.
    public sealed class SculptField
    {
        internal struct Prim
        {
            public SculptKind kind; public SculptOp op; public bool mirror;
            public float k;                       // blend
            public Vector3 c, ax, ay, az;         // centre (cone: start) and local axes (rows of the inverse rotation)
            public Vector3 size;                  // radii / half extents
            public float round, len, r1, r2, squash, scaleMin;
            public Vector3 lo, hi;                // bounds of the shape (its mirror image: x negated), before the blend
            public bool bounded;
            // cull: outside the shape its field is >= bA |p - bc| - bB (a union or subtract that cannot reach the
            // running value by its blend leaves it exactly as it is, so Eval skips computing it)
            public bool cull;
            public Vector3 bc;
            public float bA, bB;
        }

        internal readonly Prim[] prims;
        public readonly Vector3 Lo, Hi;           // bounds of the solid (the unions' bounds grown by their blends)

        public SculptField(SculptShape[] shapes)
        {
            prims = new Prim[shapes.Length];
            Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
            for (int i = 0; i < shapes.Length; i++)
            {
                SculptShape s = shapes[i];
                var p = new Prim { kind = s.kind, op = s.op, mirror = s.mirror, k = Mathf.Max(0f, s.blend), c = s.centre, size = s.size, round = s.round, bounded = true };
                Quaternion q = Rotation(s.rotationDeg);
                if (s.kind == SculptKind.RoundCone)
                {
                    Vector3 d = s.end - s.centre;
                    p.len = d.magnitude;
                    p.az = d / Mathf.Max(1e-9f, p.len);
                    Vector3 up = Vector3.up - p.az * Vector3.Dot(Vector3.up, p.az);
                    if (up.sqrMagnitude < 1e-8f) up = Vector3.forward - p.az * Vector3.Dot(Vector3.forward, p.az);
                    p.ay = up.normalized;
                    p.ax = Vector3.Cross(p.ay, p.az);
                    p.r1 = s.size.x; p.r2 = s.size.y;
                    p.squash = s.squash > 0f ? s.squash : 1f;
                    p.scaleMin = Mathf.Min(1f, p.squash);
                    float r = Mathf.Max(p.r1, p.r2) * Mathf.Max(1f, p.squash);
                    Vector3 rr = new Vector3(r, r, r);
                    p.lo = Vector3.Min(s.centre, s.end) - rr; p.hi = Vector3.Max(s.centre, s.end) + rr;
                }
                else
                {
                    p.ax = q * Vector3.right; p.ay = q * Vector3.up; p.az = q * Vector3.forward;
                    if (s.kind == SculptKind.Plane) p.bounded = false;
                    else
                    {
                        Vector3 h = s.size;
                        // a rotated box's extent along each world axis: sum |axis component| x half extent
                        Vector3 e = new Vector3(
                            Mathf.Abs(p.ax.x) * h.x + Mathf.Abs(p.ay.x) * h.y + Mathf.Abs(p.az.x) * h.z,
                            Mathf.Abs(p.ax.y) * h.x + Mathf.Abs(p.ay.y) * h.y + Mathf.Abs(p.az.y) * h.z,
                            Mathf.Abs(p.ax.z) * h.x + Mathf.Abs(p.ay.z) * h.y + Mathf.Abs(p.az.z) * h.z);
                        p.lo = s.centre - e; p.hi = s.centre + e;
                    }
                }
                if (p.op == SculptOp.Union && p.bounded)
                {
                    Vector3 g = new Vector3(p.k, p.k, p.k);
                    lo = Vector3.Min(lo, p.lo - g); hi = Vector3.Max(hi, p.hi + g);
                    if (p.mirror) { lo.x = Mathf.Min(lo.x, -p.hi.x - p.k); hi.x = Mathf.Max(hi.x, -p.lo.x + p.k); }
                }
                p.cull = p.bounded && p.op != SculptOp.Intersect;
                switch (p.kind)
                {
                    case SculptKind.Ellipsoid:
                    {
                        // outside, k0 (k0 - 1) / k1 = (k0 - 1) (k0 / k1) >= (r / max - 1) min
                        float mx = Mathf.Max(p.size.x, Mathf.Max(p.size.y, p.size.z)), mn = Mathf.Min(p.size.x, Mathf.Min(p.size.y, p.size.z));
                        p.bc = p.c; p.bA = mn / mx; p.bB = mn;
                        if (!(mn > 0f)) p.cull = false;
                        break;
                    }
                    case SculptKind.Box:
                        p.bc = p.c; p.bA = 1f; p.bB = p.size.magnitude;   // inside the circumscribed ball
                        break;
                    case SculptKind.RoundCone:
                        // within max radius of the axis segment (its middle +- len / 2), in the squashed frame, x scaleMin
                        p.bc = 0.5f * (s.centre + s.end); p.bA = Mathf.Min(p.squash, 1f / p.squash);
                        p.bB = p.scaleMin * (0.5f * p.len + Mathf.Max(p.r1, p.r2));
                        break;
                    default: p.cull = false; break;
                }
                prims[i] = p;
            }
            Lo = lo; Hi = hi;
        }

        // Unity's Euler order (z, then x, then y), from AngleAxis (the harness's stubs have no Quaternion.Euler).
        public static Quaternion Rotation(Vector3 deg)
            => Quaternion.AngleAxis(deg.y, Vector3.up) * Quaternion.AngleAxis(deg.x, Vector3.right) * Quaternion.AngleAxis(deg.z, Vector3.forward);

        public int Count => prims.Length;

        // Which copies of shape i can change the field near the box lo..hi (grown by margin): bit 1 the shape, bit 2
        // its mirror image (0 = neither). Unbounded shapes always can.
        internal int Touches(int i, Vector3 lo, Vector3 hi, float margin)
        {
            ref readonly Prim p = ref prims[i];
            if (!p.bounded) return p.mirror ? 3 : 1;
            float m = margin + p.k;
            if (!(p.lo.y - m <= hi.y && p.hi.y + m >= lo.y && p.lo.z - m <= hi.z && p.hi.z + m >= lo.z)) return 0;
            int r = p.lo.x - m <= hi.x && p.hi.x + m >= lo.x ? 1 : 0;
            if (p.mirror && -p.hi.x - m <= hi.x && -p.lo.x + m >= lo.x) r |= 2;
            return r;
        }

        internal SculptOp Op(int i) => prims[i].op;
        internal bool Mirrored(int i) => prims[i].mirror;

        // How far shape i's field can overstate the distance to its surface, inside it (1 = never): the ellipsoid's
        // bound k0 (k0 - 1) / k1 is exact along its axes and close outside, but inside, along its long axis, it reads
        // the distance to the far end while the near side is only the short radius away - up to max / min radius.
        internal float InsideOverstatement(int i)
        {
            ref readonly Prim p = ref prims[i];
            if (p.kind != SculptKind.Ellipsoid) return 1f;
            float a = Mathf.Max(p.size.x, Mathf.Max(p.size.y, p.size.z)), b = Mathf.Min(p.size.x, Mathf.Min(p.size.y, p.size.z));
            return b > 0f ? a / b : float.PositiveInfinity;
        }

        public const float Far = 1e3f;

        // The field at p over every shape.
        public float Eval(Vector3 p)
        {
            return Eval(p.x, p.y, p.z, null, prims.Length);
        }

        // The field at p over the listed shapes only (the mesher's per-block lists: shape index << 2 | copies).
        internal float Eval(Vector3 p, int[] list, int count) => Eval(p.x, p.y, p.z, list, count);

        // The hot path of every build (hundreds of thousands of shapes per build): scalar float arithmetic in one loop,
        // no Vector3 operators, Mathf, Shape or SMin calls (Mono inlines none of them; the call overhead was most of a
        // build). Each expression is the one Vector3.Dot / Mathf.Max / Mathf.Sqrt / SMin computes, in the same order:
        // bit-identical under .NET; Mono keeps float intermediates wider, so there it agrees to float rounding (meshes
        // within 2e-7 m). list null: every shape, each with its mirror image when it has one.
        internal float Eval(float px, float py, float pz, int[] list, int count)
        {
            Prim[] pr = prims;
            float d = Far;
            for (int n = 0; n < count; n++)
            {
                int e = list != null ? list[n] : n << 2 | (pr[n].mirror ? 3 : 1);
                ref readonly Prim q = ref pr[e >> 2];
                int copies = e & 3;
                SculptOp op = q.op;
                if (q.cull)
                {
                    // SMin(d, s, k) is exactly d once s >= d + k (subtract: s >= -d + k): skip the shape when its bound
                    // says so for every copy (outside it only: t > 0; a relative and an absolute margin over rounding)
                    float t = (op == SculptOp.Union ? d : -d) + q.k;
                    if (t > 0f && t < 0.5f * Far)
                    {
                        float need = (t + q.bB) * 1.0001f + 1e-6f, need2 = need * need, a2 = q.bA * q.bA;
                        float ex = px - q.bc.x, ey = py - q.bc.y, ez = pz - q.bc.z, mx = -px - q.bc.x;
                        if (((copies & 1) == 0 || (ex * ex + ey * ey + ez * ez) * a2 >= need2)
                            && ((copies & 2) == 0 || (mx * mx + ey * ey + ez * ez) * a2 >= need2)) continue;
                    }
                }
                // the shape, and its mirror image: an intersect is trimmed by both (the solid is inside each), a union or
                // subtract takes the nearer; a mirrored intersect without its image does not trim at all (Far)
                float s = Far;
                for (int cp = 0; cp < 2; cp++)
                {
                    if ((copies & (1 << cp)) == 0) continue;
                    float dx = (cp == 0 ? px : -px) - q.c.x, dy = py - q.c.y, dz = pz - q.c.z, v;
                    switch (q.kind)   // Shape(), inline
                    {
                        case SculptKind.Ellipsoid:
                        {
                            float x = dx * q.ax.x + dy * q.ax.y + dz * q.ax.z, y = dx * q.ay.x + dy * q.ay.y + dz * q.ay.z, z = dx * q.az.x + dy * q.az.y + dz * q.az.z;
                            float sx = q.size.x, sy = q.size.y, sz = q.size.z;
                            float ex = x / sx, ey = y / sy, ez = z / sz;
                            float k0 = (float)Math.Sqrt(ex * ex + ey * ey + ez * ez);
                            float fx = ex / sx, fy = ey / sy, fz = ez / sz;
                            float k1 = (float)Math.Sqrt(fx * fx + fy * fy + fz * fz);
                            if (!(k1 > 1e-12f)) { float mn = sy < sz ? sy : sz; v = -(sx < mn ? sx : mn); }
                            else v = k0 * (k0 - 1f) / k1;
                            break;
                        }
                        case SculptKind.Box:
                        {
                            float r = q.round;
                            float x = Math.Abs(dx * q.ax.x + dy * q.ax.y + dz * q.ax.z) - (q.size.x - r);
                            float y = Math.Abs(dx * q.ay.x + dy * q.ay.y + dz * q.ay.z) - (q.size.y - r);
                            float z = Math.Abs(dx * q.az.x + dy * q.az.y + dz * q.az.z) - (q.size.z - r);
                            float mx = x > 0f ? x : 0f, my = y > 0f ? y : 0f, mz = z > 0f ? z : 0f;
                            float yz = y > z ? y : z, mm = x > yz ? x : yz;
                            v = (float)Math.Sqrt(mx * mx + my * my + mz * mz) + (mm < 0f ? mm : 0f) - r;
                            break;
                        }
                        case SculptKind.Plane:
                            v = dx * q.ay.x + dy * q.ay.y + dz * q.ay.z;
                            break;
                        default:
                        {
                            float x = dx * q.ax.x + dy * q.ax.y + dz * q.ax.z, y = (dx * q.ay.x + dy * q.ay.y + dz * q.ay.z) / q.squash, z = dx * q.az.x + dy * q.az.y + dz * q.az.z;
                            v = RoundCone(x, y, z, q.len, q.r1, q.r2) * q.scaleMin;
                            break;
                        }
                    }
                    if (cp == 0) s = v;
                    else if (op == SculptOp.Intersect) s = s > v ? s : v;
                    else s = s < v ? s : v;
                }
                if (op == SculptOp.Intersect && q.mirror && (copies & 2) == 0) s = Far;
                float a = op == SculptOp.Union ? d : -d, b = op == SculptOp.Intersect ? -s : s;
                // SMin(a, b, k), inline
                float lo = a < b ? a : b, k = q.k;
                if (k > 0f)
                {
                    float hh = k - Math.Abs(a - b);
                    if (hh > 0f) { hh /= k; lo -= hh * hh * k * 0.25f; }
                }
                d = op == SculptOp.Union ? lo : -lo;
            }
            return d;
        }

        // Polynomial smooth minimum (k = 0: min).
        public static float SMin(float a, float b, float k)
        {
            float lo = a < b ? a : b;
            if (!(k > 0f)) return lo;
            float h = k - Math.Abs(a - b);
            if (!(h > 0f)) h = 0f;   // Mathf.Max(h, 0)
            h /= k;
            return lo - h * h * k * 0.25f;
        }

        static float RoundCone(float px, float py, float pz, float L, float r1, float r2)
        {
            // b - a = (0, 0, L)
            float l2 = L * L, rr = r1 - r2, a2 = l2 - rr * rr, il2 = 1f / l2;
            float y = pz * L, z = y - l2;
            float qx = px * l2, qy = py * l2, qz = pz * l2 - L * y;
            float x2 = qx * qx + qy * qy + qz * qz;
            float y2 = y * y * l2, z2 = z * z * l2;
            float k = (rr >= 0f ? 1f : -1f) * rr * rr * x2;
            if ((z >= 0f ? 1f : -1f) * a2 * z2 > k) return (float)Math.Sqrt(x2 + z2) * il2 - r2;
            if ((y >= 0f ? 1f : -1f) * a2 * y2 < k) return (float)Math.Sqrt(x2 + y2) * il2 - r1;
            return ((float)Math.Sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
        }

        // Central-difference gradient (unnormalised).
        public Vector3 Gradient(Vector3 p, float e)
        {
            return new Vector3(Eval(new Vector3(p.x + e, p.y, p.z)) - Eval(new Vector3(p.x - e, p.y, p.z)),
                               Eval(new Vector3(p.x, p.y + e, p.z)) - Eval(new Vector3(p.x, p.y - e, p.z)),
                               Eval(new Vector3(p.x, p.y, p.z + e)) - Eval(new Vector3(p.x, p.y, p.z - e)));
        }

        // Gradient from four samples on a tetrahedron (unnormalised; the mesher's normals).
        internal Vector3 Tetra(Vector3 p, float e, int[] list, int count)
        {
            float xp = p.x + e, xm = p.x - e, yp = p.y + e, ym = p.y - e, zp = p.z + e, zm = p.z - e;
            float a = Eval(xp, ym, zm, list, count), b = Eval(xm, ym, zp, list, count);
            float c = Eval(xm, yp, zm, list, count), d = Eval(xp, yp, zp, list, count);
            return new Vector3(a - b - c + d, -a - b + c + d, -a + b - c + d);
        }

        internal Vector3 Gradient(Vector3 p, float e, int[] list, int count)
        {
            return new Vector3(Eval(new Vector3(p.x + e, p.y, p.z), list, count) - Eval(new Vector3(p.x - e, p.y, p.z), list, count),
                               Eval(new Vector3(p.x, p.y + e, p.z), list, count) - Eval(new Vector3(p.x, p.y - e, p.z), list, count),
                               Eval(new Vector3(p.x, p.y, p.z + e), list, count) - Eval(new Vector3(p.x, p.y, p.z - e), list, count));
        }
    }

    // A polygonised Sculpt in its joint frame at scale 1 (shared by every build of the same shapes).
    public sealed class SculptPiece
    {
        public struct Tooth { public Vector3 basePt, dir; public float length, radius, curveDeg; }
        public Vector3[] Pos, Nrm;
        public float[] V;          // uv0.y: the angle round the head's long axis / 2 pi (seam vertices duplicated)
        public int[] Tris;          // wound so cross(b - a, c - a) points out of the solid
        public Tooth[] Teeth;
        public double BuildMs;      // the first build's time (the cached copies cost nothing)
        public int Folds;           // triangles turned to face their normals (a crest finer than a cell)
    }
}
