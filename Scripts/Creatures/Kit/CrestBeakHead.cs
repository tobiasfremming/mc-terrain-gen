// Creature kit (Apex redesign §5.4): CrestBeakHead - the beaked serpent head, rigid to a spine body's head joint
// (a FlowBody's joint 0). Built from signed-distance solids (SculptField, polygonised by SculptMesher and cached per
// shape set) from head-scale numbers:
//   skull   a long, slightly flattened cranium ellipsoid with a jowl at each jaw angle, a round cone back into the neck
//           (smaller than the neck where it starts, so the neck's front rings run into it without a step), a long snout
//           cone, and the upper beak: a cone riding on the mouth line to where the jaw ends, then the hooked tip, a cone
//           to the point that hooks below the mouth line by beakHookDeg; all smooth-unioned with generous blends.
//           The mouth line rises toward the gape's corner (the hinge, under the eye). The jaw's region - a box below
//           the mouth line whose top-rear edge is rounded with the jaw's depth, so the cut sweeps from the gape's corner
//           down and back into the throat with no rear face - is carved out with a small blend (the lip edge) and the
//           hooked tip put back; then a soft brow over each eye socket, nostril pits and the lamellae (round-bottomed
//           furrows on the upper lip, each continued on the jaw below the mouth line, every feature at least a cell
//           wide so the mesh stays manifold).
//   tilt    the joint's frame is level (the heading); the skull's long axis continues the neck's line into the head
//           joint in the rest pose (RestTilt), turned by pitchDeg - fixed per individual when its runtime is made.
//   jaw     the same solids inside that region (the same outer surface: the mouth line a fine crease, the jaw flowing
//           into the throat), ending under the hook with a chin sloping back; the same lamellae; rigid to the part's
//           one bone, which turns about the hinge (CrestBeakHeadRuntime.Gape).
//   crests  each CrestDef is one blade on top of the upper beak - or, mirrored, an equal pair rooted side by side that
//           lean apart by splayDeg each, a V seen from the front (the Apex's). A blade: 28 flattened round cones along a
//           centre line in its own plane that leans forward at its root and curls back by curlDeg (curvature growing
//           toward the tip: a wave breaking backward), one flattening (chord / thickness) and radii shared at every
//           join, tapering to a point; a flared foot sunk into the beak. Built once per CrestDef in the blade's own
//           frame (a RoundCone's flattening follows world up, so a leaning blade is drawn upright and turned into place
//           - BladeFrame - when it is emitted; the pair shares one polygonised piece).
// Look: skull and jaw on the body's skin (layer 0) with the dorsal channel from the height in the head frame,
// (y - centre) / half depth of the head's side outline at that z - the body's sin(phi) on a round section, so the
// seam into the neck does not show in colour; the jaw a little paler from the hinge forward (no step at its rear);
// crests on the named kind "crest" (default row: glossy keratin, bright teal #3FC0B0; a species restyles it with a
// Layers row). The derived solids and each piece's dorsal values are cached per parameter set.
// Runtime (stage Late): the jaw opens on its own calls - a timer between callEverySeconds.x and .y (Froude), seeded
// per individual - and the species' two eye bones sit in the sockets. Head frame: +Z along the head (the beak), +Y up,
// origin at the head joint (plus at.offset), metres at scale 1. Allocation-free after construction; main thread.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    // One crest on the upper beak.
    [Serializable]
    public struct CrestDef
    {
        [Param(ParamUnit.Fraction, "where it stands along the beak: 0 the beak's root .. 1 its tip", Min = 0, Max = 1)] public float along;
        [Param(ParamUnit.Metres, "how high its centre line rises above the beak's top (vertically; a splayed blade is longer)")] public float height;
        [Param(ParamUnit.Metres, "its centre line's length, root to tip (>= height / cos splayDeg)")] public float length;
        [Param(ParamUnit.Degrees, "how far it curls backward from its root to its tip (0 = a straight blade leaning forward)", Min = 0, Max = 200)] public float curlDeg;
        [Param(ParamUnit.Metres, "blade half thickness at its root (it tapers to a point at the tip)")] public float thickness;
        [Param(ParamUnit.Metres, "blade half chord (its width seen from the side) at its root; 0 = 0.35 x length; chord / thickness is capped at 4 (it tapers with the thickness)")] public float chord;
        [Param(ParamUnit.Degrees, "the blade leans out from upright by this much (toward +x; a mirrored pair: each away from the midline - a V from the front)", Min = 0, Max = 75)] public float splayDeg;
        [Param(ParamUnit.Flag, "an equal pair rooted side by side on the midline, mirrored left and right (else one blade on the midline)")] public bool mirror;
    }

    [Serializable]
    [Part("CrestBeakHead", PartKind.Rigid, Bones = "1 (the jaw)",
          Summary = "The beaked serpent head: a long, slightly flattened SDF skull flowing into a pointed, hooked beak, small eyes under a soft brow, lamellae along the mouth line, crests curling back like breaking waves on top of the upper beak, and a jaw on its own bone that opens on its own call timer. Rigid to the body's head joint; its back overlaps the neck so the outline runs on; carries the species' eyes.",
          System = "CrestBeakHeadRuntime (part runtime, stage Late: jaw, eye bones) + CrestBeakHead.EmitMesh (SculptMesher pieces, cached)",
          MaterialLayers = new[] { 0 }, MaterialKinds = new[] { "crest" }, KitOnly = true,
          UseWhen = "a serpent- or dragon-like head ending in a beak, on a FlowBody (or SpineBody) head joint: crested beaks, toothless snapping heads; the Apex")]
    public sealed class CrestBeakHead : CreaturePart
    {
        [Param(ParamUnit.Metres, "skull length; the skull reaches half of it behind the head joint (into the neck) and half ahead to the beak's root", Example = "fixture 0.26")] public float skullLength = 0.26f;
        [Param(ParamUnit.Metres, "skull half width (>= skullDepth: slightly flattened)")] public float skullWidth = 0.055f;
        [Param(ParamUnit.Metres, "skull half depth (the head's back starts at ~0.6 x it inside the neck)")] public float skullDepth = 0.052f;
        [Param(ParamUnit.Metres, "beak length from its root (skullLength / 2 ahead of the joint) to the tip")] public float beakLength = 0.26f;
        [Param(ParamUnit.Degrees, "the upper beak's tip hooks down by this much over the beak's last third, over the lower jaw's tip", Min = -10, Max = 30)] public float beakHookDeg = 11f;
        [Param(ParamUnit.Fraction, "how far the brow juts out over the eye socket", Min = 0, Max = 1)] public float browRidge = 0.3f;
        [Param(ParamUnit.Struct, "crests on top of the upper beak: each one blade, or (mirror) an equal pair splayed into a V")] public CrestDef[] crests =
        {
            new CrestDef { along = 0.8f, height = 0.055f, length = 0.085f, curlDeg = 105f, thickness = 0.007f, chord = 0.026f, splayDeg = 30f, mirror = true },
        };
        [Param(ParamUnit.Count, "lamellae: fine grooves across the mouth line", Min = 0, Max = 32)] public int grooves = 9;
        [Param(ParamUnit.Metres, "groove depth")] public float grooveDepth = 0.004f;
        [Param(ParamUnit.Fraction, "the lower jaw's share of the head's depth (the mouth line's height)", Min = 0.15f, Max = 0.7f)] public float jawFraction = 0.42f;
        [Param(ParamUnit.Degrees, "the jaw's widest gape (a call)", Min = 0, Max = 80)] public float gapeDeg = 28f;
        [Param(ParamUnit.Seconds, "time between calls: x the shortest, y the longest (each individual draws its own, seeded)")] public Vector2 callEverySeconds = new Vector2(6f, 14f);
        [Param(ParamUnit.Seconds, "one call: open, hold, close")] public float callSeconds = 0.8f;
        [Param(ParamUnit.Fraction, "eye centre: x along the skull (fraction of skullLength ahead of the joint), y height (fraction of skullDepth)", Min = -0.5f, Max = 0.5f)] public Vector2 eyeAt = new Vector2(0.15f, 0.3f);
        [Param(ParamUnit.Degrees, "the head's long axis tilts nose-down (+) or up (-) from the neck's line at the head joint in the rest pose (0 = the head continues the neck)", Min = -45, Max = 45)] public float pitchDeg = 0f;
        [Param(ParamUnit.Metres, "polygoniser cell of the skull and jaw (the crests use a finer one from their thickness)")] public float cell = 0.005f;

        public const int MaxCrests = 8, MaxGrooves = 32;

        public override int BoneCount(in SpeciesCtx c) => 1;
        public override int MeshOrder => 100;
        protected override void CopyArrays() { crests = (CrestDef[])crests?.Clone(); }

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a CrestBeakHead needs a spine body (FlowBody or SpineBody)";
            if (at.joint < 0 || at.joint >= c.Joints) return $"{id}: joint {at.joint} outside 0..{c.Joints - 1}";
            if (!(skullLength > 0f) || !(skullWidth > 0f) || !(skullDepth > 0f) || !(beakLength > 0f)) return $"{id}: skull and beak sizes must be > 0";
            if (!(cell >= Sculpt.MinCell)) return $"{id}: cell must be >= {Sculpt.MinCell} m";
            if (beakHookDeg < -10f || beakHookDeg > 30f) return $"{id}: beakHookDeg must be -10..30";
            if (browRidge < 0f || browRidge > 1f) return $"{id}: browRidge must be 0..1";
            if (grooves < 0 || grooves > MaxGrooves) return $"{id}: grooves must be 0..{MaxGrooves}";
            if (grooveDepth < 0f) return $"{id}: grooveDepth must be >= 0";
            if (jawFraction < 0.15f || jawFraction > 0.7f) return $"{id}: jawFraction must be 0.15..0.7";
            if (pitchDeg < -45f || pitchDeg > 45f) return $"{id}: pitchDeg must be -45..45";
            if (gapeDeg < 0f || gapeDeg > 80f) return $"{id}: gapeDeg must be 0..80";
            if (!(callEverySeconds.x > 0f) || callEverySeconds.y < callEverySeconds.x) return $"{id}: callEverySeconds needs 0 < x <= y";
            if (!(callSeconds > 0f)) return $"{id}: callSeconds must be > 0";
            if (crests == null || crests.Length > MaxCrests) return $"{id}: at most {MaxCrests} crests";
            for (int k = 0; k < crests.Length; k++)
            {
                CrestDef d = crests[k];
                if (d.along < 0f || d.along > 1f) return $"{id}: crest {k}: along must be 0..1";
                if (!(d.height > 0f) || !(d.thickness > 0f)) return $"{id}: crest {k}: height and thickness must be > 0";
                if (d.chord < 0f) return $"{id}: crest {k}: chord must be >= 0";
                if (d.splayDeg < 0f || d.splayDeg > 75f) return $"{id}: crest {k}: splayDeg must be 0..75";
                if (!(d.length >= d.height / Mathf.Cos(d.splayDeg * Mathf.Deg2Rad))) return $"{id}: crest {k}: length must be >= height / cos splayDeg";
                if (d.curlDeg < 0f || d.curlDeg > 200f) return $"{id}: crest {k}: curlDeg must be 0..200";
            }
            return null;
        }

        public override IPartRuntime CreateRuntime(in PartCtx c)
            => c.Sim != null ? new CrestBeakHeadRuntime(this, c.Sim, c.Species.Layout.Start(c.Index), Mathf.Clamp(at.joint, 0, c.Species.JointCount - 1)) : null;

        // ------------------------------------------------------------ the solids (head frame, scale 1)

        public IReadOnlyList<SculptShape> SkullSolid() => Geo().skull;
        // The whole head's outer solid (skull and jaw closed, before the mouth is cut).
        public IReadOnlyList<SculptShape> HeadSolid() => Geo().head;
        public IReadOnlyList<SculptShape> JawSolid() => Geo().jaw;
        // Blades: each CrestDef gives one, a mirrored one two (its +x blade, then its -x blade).
        public int BladeCount => Geo().bladeDef.Length;
        public CrestDef BladeDef(int b) => crests[Geo().bladeDef[b]];
        // Blade b's solid and centre line (root to tip) in the blade's own frame (upright, +Z along the head), and that
        // frame's place in the head frame.
        public IReadOnlyList<SculptShape> CrestSolid(int b) => Geo().crest[Geo().bladeDef[b]];
        public Vector3[] CrestSpine(int b) => (Vector3[])Geo().spine[Geo().bladeDef[b]].Clone();
        public Matrix4x4 BladeFrame(int b) => Geo().bladeFrame[b];
        // The jaw's hinge and the right eye's centre on the socket floor (head frame).
        public Vector3 Hinge => Geo().hinge;
        public Vector3 EyeSurface => Geo().eye;

        // The polygonised pieces: 0 the skull, 1 the jaw, 2 + b blade b (in its own frame; BladeFrame places it).
        public int PieceCount => 2 + BladeCount;
        public SculptPiece Piece(int i)
        {
            Built g = Geo();
            if (i == 0) return SculptMesher.For(g.skullSculpt);
            if (i == 1) return SculptMesher.For(g.jawSculpt);
            return SculptMesher.For(g.crestSculpt[g.bladeDef[i - 2]]);
        }

        // Everything derived from the numbers, once per parameter set (the field marches cost more than a build's budget).
        sealed class Built
        {
            public SculptShape[] head, skull, jaw;
            public SculptShape[][] crest;
            public Vector3[][] spine;
            public Sculpt skullSculpt, jawSculpt;
            public Sculpt[] crestSculpt;       // per CrestDef
            public int[] bladeDef;             // per blade: its CrestDef
            public Matrix4x4[] bladeFrame;     // per blade: its frame in the head frame
            public Vector3 hinge, eye, boundsCentre;
            public float boundsRadius;
            public float profZ0, profDz;
            public float[] profC, profH;   // the side outline's centre and half depth at profZ0 + i profDz
            public float[][] dorsal;       // per piece (0 skull, 1 jaw, 2 + b blades): each vertex's dorsal value, made on first use
        }

        static readonly Dictionary<ulong, Built> built = new Dictionary<ulong, Built>();

        static ulong Mix(ulong h, float f) { h ^= (uint)BitConverter.SingleToInt32Bits(f); return h * 1099511628211UL; }

        ulong Key()
        {
            ulong h = 14695981039346656037UL;
            h = Mix(Mix(Mix(Mix(h, skullLength), skullWidth), skullDepth), beakLength);
            h = Mix(Mix(Mix(Mix(h, beakHookDeg), browRidge), grooves), grooveDepth);
            h = Mix(Mix(Mix(Mix(h, jawFraction), eyeAt.x), eyeAt.y), cell);
            h = Mix(Mix(Mix(Mix(Mix(h, gapeDeg), BlobCrestsForTest ? 1 : 0), BlobSharpTipForTest ? 1 : 0), CrestSinkForTest), BladeScaleForTest);
            int n = crests?.Length ?? 0;
            h = Mix(h, n);
            for (int k = 0; k < n; k++) { CrestDef d = crests[k]; h = Mix(Mix(Mix(Mix(Mix(Mix(Mix(Mix(h, d.along), d.height), d.length), d.curlDeg), d.thickness), d.chord), d.splayDeg), d.mirror ? 1 : 0); }
            return h;
        }

        Built Geo()
        {
            ulong key = Key();
            if (built.TryGetValue(key, out Built g)) return g;
            g = Build();
            built[key] = g;
            return g;
        }

        static SculptShape Ell(Vector3 c, Vector3 r, SculptOp op, float blend, bool mirror = false)
            => new SculptShape { kind = SculptKind.Ellipsoid, op = op, centre = c, size = r, blend = blend, mirror = mirror };
        static SculptShape Cone(Vector3 a, Vector3 b, float r0, float r1, float squash, float blend)
            => new SculptShape { kind = SculptKind.RoundCone, op = SculptOp.Union, centre = a, end = b, size = new Vector3(r0, r1, 0f), squash = squash, blend = blend };

        // The surface along a ray from `from` (inside) in direction d: the first outside point, refined by bisection.
        static bool March(SculptField f, Vector3 from, Vector3 d, float reach, float step, out Vector3 hit)
        {
            hit = from;
            if (f.Eval(from) > 0f) return false;
            float a = 0f;
            for (float t = step; t <= reach + 1e-6f; t += step)
            {
                if (f.Eval(from + d * t) > 0f)
                {
                    float lo = a, hi = t;
                    for (int i = 0; i < 24; i++) { float m = 0.5f * (lo + hi); if (f.Eval(from + d * m) > 0f) hi = m; else lo = m; }
                    hit = from + d * (0.5f * (lo + hi));
                    return true;
                }
                a = t;
            }
            return false;
        }

        // The head's side outline at x = 0 and z: top and bottom of the solid (false when the plane misses it).
        static bool Outline(SculptField f, float z, float reach, out float top, out float bot)
        {
            top = bot = 0f;
            // a point inside on this z: scan the midline from the bottom up
            float step = 0.002f;
            for (float y = -reach; y <= reach; y += step)
                if (f.Eval(new Vector3(0f, y, z)) < 0f)
                {
                    Vector3 p = new Vector3(0f, y + 0.5f * step, z);
                    if (f.Eval(p) > 0f) p.y = y;
                    if (!March(f, p, Vector3.up, 2f * reach, step, out Vector3 hu) || !March(f, p, Vector3.down, 2f * reach, step, out Vector3 hd)) return false;
                    top = hu.y; bot = hd.y;
                    return true;
                }
            return false;
        }

        Built Build()
        {
            float L = skullLength, W = skullWidth, D = skullDepth, BL = beakLength;
            float zb = -0.5f * L, zr = 0.5f * L, zt = zr + BL;
            float sq = Mathf.Clamp(D / W, 0.5f, 4f);   // the skull's cross-section height / width
            float reach = 4f * Mathf.Max(D, W) + 0.1f;
            var g = new Built();

            // H0: cranium, the cone back into the neck, jowls, and one long snout cone almost to the tip (its lower half
            // is the lower jaw's pointed tip, its upper half the beak)
            var head = new List<SculptShape>
            {
                Ell(new Vector3(0f, 0.05f * D, -0.05f * L), new Vector3(0.9f * W, 0.92f * D, 0.55f * L), SculptOp.Union, 0f),                       // cranium
                Cone(new Vector3(0f, -0.02f * D, zb), new Vector3(0f, 0.05f * D, -0.1f * L), 0.6f * W, 0.85f * W, 0.95f * sq, 0.5f * W),    // back into the neck
                Ell(new Vector3(0.45f * W, -0.45f * D, -0.08f * L), new Vector3(0.38f * W, 0.45f * D, 0.28f * L), SculptOp.Union, 0.5f * W, true), // jowls
                Cone(new Vector3(0f, 0.05f * D, 0.1f * L), new Vector3(0f, -0.12f * D, zr + 0.9f * BL), 0.68f * W, 0.1f * W, 1.15f * sq, 0.6f * W), // snout
            };
            var H0 = new SculptField(head.ToArray());

            // the mouth line: on the beak jawFraction of the depth up from the bottom; at the hinge (the gape's corner,
            // under the eye) 0.1 of the depth higher - it rises toward the rear; straight between
            float zh = 0.02f * L, zm = zr + 0.3f * BL;
            Outline(H0, zh, reach, out float th, out float bh);
            Outline(H0, zm, reach, out float tm, out float bm);
            Vector3 mh = new Vector3(0f, bh + Mathf.Min(0.8f, jawFraction + 0.1f) * (th - bh), zh), mm = new Vector3(0f, bm + jawFraction * (tm - bm), zm);
            Vector3 md = (mm - mh).normalized;
            float MouthY(float z) => mh.y + (mm.y - mh.y) * (z - zh) / (zm - zh);
            g.hinge = mh;

            // the upper beak: a cone riding on the mouth line from mid-beak to where the lower jaw ends (zFront), then the
            // hooked tip - a cone from there to the point, which hooks down below the mouth line by beakHookDeg and
            // overhangs the lower jaw's end
            float z3 = zr + 0.45f * BL, zFront = zt - 0.15f * BL;
            Outline(H0, z3, reach, out float t3, out float b3);
            Outline(H0, zFront, reach, out float tF, out float bF);
            float r3 = 0.5f * Mathf.Max(0.004f, t3 - MouthY(z3)), rF = 0.5f * Mathf.Max(0.003f, tF - MouthY(zFront));
            float drop = Mathf.Tan(beakHookDeg * Mathf.Deg2Rad) * (zt - zFront) * 2f;
            const float kBeakSquash = 1.1f;
            Vector3 cF = new Vector3(0f, MouthY(zFront) + kBeakSquash * rF, zFront);
            SculptShape beakA = Cone(new Vector3(0f, MouthY(z3) + kBeakSquash * r3, z3), cF, r3, rF, kBeakSquash, 0.3f * W);
            SculptShape hook = Cone(cF, new Vector3(0f, MouthY(zt) - drop, zt), rF, Mathf.Max(0.002f, 0.5f * cell), kBeakSquash, 0.2f * W);
            head.Add(beakA);
            head.Add(hook);
            var H = new SculptField(head.ToArray());
            g.head = head.ToArray();

            // the side outline (dorsal shading)
            const int kProf = 64;
            g.profZ0 = zb - 0.6f * W; g.profDz = (zt - g.profZ0) / (kProf - 1);
            g.profC = new float[kProf]; g.profH = new float[kProf];
            for (int i = 0; i < kProf; i++)
            {
                if (Outline(H, g.profZ0 + i * g.profDz, reach, out float t, out float b)) { g.profC[i] = 0.5f * (t + b); g.profH[i] = Mathf.Max(1e-3f, 0.5f * (t - b)); }
                else g.profH[i] = -1f;
            }
            for (int i = 0; i < kProf; i++)   // fill misses from the nearest good station
                if (g.profH[i] < 0f)
                    for (int d = 1; d < kProf; d++)
                    {
                        int j = i - d >= 0 && g.profH[i - d] > 0f ? i - d : i + d < kProf && g.profH[i + d] > 0f ? i + d : -1;
                        if (j >= 0) { g.profC[i] = g.profC[j]; g.profH[i] = g.profH[j]; break; }
                    }

            // the jaw's region: a box below the mouth line from far behind the hinge to past the tip, its top-rear edge
            // rounded with a radius of the jaw's depth at the hinge - the cut sweeps from the gape's corner down and back
            // into the throat (no rear face); its front is beyond the tip, so the jaw runs to the snout's tip
            float pitch = -Mathf.Atan2(md.y, md.z) * Mathf.Rad2Deg;   // + nose down
            Quaternion bq = SculptField.Rotation(new Vector3(pitch, 0f, 0f));
            Vector3 boxUp = bq * Vector3.up;
            float jawDepth = Mathf.Max(0.01f, mh.y - bh);
            float R = 1.1f * jawDepth;
            float sRear = -R, sFront = (zt - zh) / Mathf.Max(0.2f, md.z) + R + 0.02f;   // along md from the hinge
            float hx = 3f * W + R, hy = 3f * D + R, hz = 0.5f * (sFront - sRear);
            float lip = Mathf.Max(0.5f * cell, 0.004f);
            var jawBox = new SculptShape
            {
                kind = SculptKind.Box, centre = mh + md * (0.5f * (sRear + sFront)) - boxUp * hy, size = new Vector3(hx, hy, hz), rotationDeg = new Vector3(pitch, 0f, 0f),
                round = R,
            };

            // lamellae: furrows across the mouth line, grooveDepth deep
            var upperGrooves = new List<SculptShape>(); var lowerGrooves = new List<SculptShape>();
            if (grooves > 0 && grooveDepth > 0f)
            {
                float z0 = Mathf.Max(zh + 0.15f * L, eyeAt.x * L + 0.16f * L + Mathf.Max(1.6f * cell, 2.5f * grooveDepth)), z1 = z3 - 0.05f * BL;   // clear of the eye socket
                for (int i = 0; i < grooves; i++)
                {
                    float z = grooves == 1 ? 0.5f * (z0 + z1) : Mathf.Lerp(z0, z1, i / (grooves - 1f));
                    Vector3 p = new Vector3(0f, MouthY(z), z);
                    if (!March(H, p, Vector3.right, reach, 0.002f, out Vector3 s)) continue;
                    Vector3 n = H.Gradient(s, 1e-4f); n.y = 0f;
                    n = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.right;
                    int pi = Mathf.Clamp(Mathf.RoundToInt((z - g.profZ0) / g.profDz), 0, kProf - 1);
                    // a round-bottomed furrow: a cylinder of radius rg (>= a cell: a narrower slot pinches the mesh), its top
                    // leaning back 15 deg; one on the upper lip above the mouth line and one on the jaw below it, a cell
                    // clear of the lip edge (a furrow through the lip's rounded edge leaves a sliver finer than a cell)
                    float rg = Mathf.Max(1.6f * cell, 2.5f * grooveDepth), gy = 0.35f * g.profH[pi], clear = rg + 0.6f * cell;
                    Vector3 lean = new Vector3(0f, Mathf.Cos(15f * Mathf.Deg2Rad), -Mathf.Sin(15f * Mathf.Deg2Rad));
                    Vector3 dir = (lean - n * Vector3.Dot(lean, n)).normalized;
                    float gb = 0.75f * cell;   // a soft edge (a crease finer than a cell pinches); it fills gb / 4 of the depth back in
                    Vector3 c = s + n * (rg - grooveDepth - 0.25f * gb);
                    float k = 1f / Mathf.Max(0.3f, dir.y);   // along the furrow per metre of height
                    var furrow = new SculptShape { kind = SculptKind.RoundCone, op = SculptOp.Subtract, blend = gb, mirror = true, size = new Vector3(rg, rg, 0f), squash = 1f };
                    // each end of the axis sits on the side's own surface there (the same depth all along: on a convex side a
                    // straight axis would graze out at its ends, a shaving finer than a cell that pinches the mesh)
                    float depthOff = rg - grooveDepth - 0.25f * gb;
                    Vector3 OnSide(Vector3 q)
                    {
                        Vector3 from = new Vector3(0f, q.y, q.z);
                        if (!March(H, from, Vector3.right, reach, 0.002f, out Vector3 hs)) return q;
                        Vector3 hn = H.Gradient(hs, 1e-4f);
                        hn = hn.sqrMagnitude > 1e-12f ? hn.normalized : n;
                        return hs + hn * depthOff;
                    }
                    furrow.centre = OnSide(c + dir * (clear * k)); furrow.end = OnSide(c + dir * ((clear + gy) * k));
                    upperGrooves.Add(furrow);
                    furrow.centre = OnSide(c - dir * (clear * k)); furrow.end = OnSide(c - dir * ((clear + 0.7f * gy) * k));
                    lowerGrooves.Add(furrow);
                }
            }

            // skull: H minus the jaw's region (the lip edge), the hooked tip put back (below the mouth line ahead of the
            // jaw's end it is the skull's); brows, sockets, nostrils, lamellae
            var skull = new List<SculptShape>(head);
            SculptShape cut = jawBox; cut.op = SculptOp.Subtract; cut.blend = lip;
            skull.Add(cut);
            SculptShape hookBack = hook; hookBack.blend = 0.5f * lip;
            skull.Add(hookBack);
            var withCut = new SculptField(skull.ToArray());
            float ze = eyeAt.x * L, ye = eyeAt.y * D;
            Vector3 eyeIn = new Vector3(0f, ye, ze);
            if (!March(withCut, eyeIn, Vector3.right, reach, 0.002f, out Vector3 es)) es = new Vector3(W, ye, ze);
            if (browRidge > 0f)
            {
                Vector3 bc = new Vector3(es.x - 0.15f * W, ye + 0.3f * D, ze - 0.02f * L);
                skull.Add(Ell(bc, new Vector3(0.1f * W + 0.35f * W * browRidge, 0.14f * D, 0.24f * L), SculptOp.Union, 0.35f * W, true));
            }
            skull.Add(Ell(new Vector3(es.x + 0.12f * W, ye, ze), new Vector3(0.3f * W, 0.2f * D, 0.11f * L), SculptOp.Subtract, 0.2f * W, true));   // eye socket
            {
                float zn = zr + 0.08f * BL;
                int pi = Mathf.Clamp(Mathf.RoundToInt((zn - g.profZ0) / g.profDz), 0, kProf - 1);
                Vector3 np = new Vector3(0f, g.profC[pi] + 0.35f * g.profH[pi], zn);
                if (March(H, np, Vector3.right, reach, 0.002f, out Vector3 ns))
                    skull.Add(Ell(ns, new Vector3(0.12f * W, 0.05f * D, 0.07f * BL), SculptOp.Subtract, 0.08f * W, true));   // nostril
            }
            skull.AddRange(upperGrooves);
            g.skull = skull.ToArray();
            var skullField = new SculptField(g.skull);
            if (!March(skullField, new Vector3(0f, ye, ze), Vector3.right, reach, 0.001f, out g.eye)) g.eye = es;

            // jaw: the same outer solid inside the region, ending at zFront under the hook with a chin that slopes back
            // (a plane leaning 30 deg from upright, a gap clear of the hook's start), its lamellae
            var jaw = new List<SculptShape>(head);
            SculptShape keep = jawBox; keep.op = SculptOp.Intersect; keep.blend = lip;
            jaw.Add(keep);
            float gap = Mathf.Max(0.003f, 0.5f * cell);
            jaw.Add(new SculptShape
            {
                kind = SculptKind.Plane, op = SculptOp.Intersect, blend = lip,
                centre = new Vector3(0f, MouthY(zFront), zFront - rF - gap), rotationDeg = new Vector3(120f, 0f, 0f),   // normal (0, -0.5, 0.87)
            });
            jaw.AddRange(lowerGrooves);
            g.jaw = jaw.ToArray();

            g.skullSculpt = new Sculpt { id = "skull", shapes = g.skull, cell = cell };
            g.jawSculpt = new Sculpt { id = "jaw", shapes = g.jaw, cell = cell };

            // crests: blades on the beak's top, curling back; a mirrored def a pair side by side, splayed apart
            int nc = crests?.Length ?? 0;
            g.crest = new SculptShape[nc][]; g.spine = new Vector3[nc][]; g.crestSculpt = new Sculpt[nc];
            var bladeDef = new List<int>(); var bladeFrame = new List<Matrix4x4>();
            for (int k = 0; k < nc; k++)
            {
                CrestDef d = crests[k];
                float t = d.thickness, sink = 0.35f * t + CrestSinkForTest * d.height;
                float splay = d.splayDeg * Mathf.Deg2Rad;
                // in the blade's frame: upright, its root sunk below the frame's origin (the beak's top)
                g.spine[k] = CrestLine(d, new Vector3(0f, -sink, 0f), 0.35f * t, d.height / Mathf.Cos(splay));
                g.crest[k] = BlobCrestsForTest ? BlobCrestShapes(d, g.spine[k], Vector3.zero) : CrestShapes(d, g.spine[k], Vector3.zero);
                g.crestSculpt[k] = new Sculpt { id = "crest", shapes = g.crest[k], cell = Mathf.Min(cell, 0.3f * t) };
                float z = zr + d.along * BL;
                int pi = Mathf.Clamp(Mathf.RoundToInt((z - g.profZ0) / g.profDz), 0, kProf - 1);
                float yIn = Mathf.Max(g.profC[pi], MouthY(z) + 0.5f * (g.profC[pi] + g.profH[pi] - MouthY(z)));
                for (int side = 1; side >= (d.mirror ? -1 : 1); side -= 2)
                {
                    float x = d.mirror ? side * 0.6f * t : 0f;   // the pair's roots side by side, touching on the midline
                    Vector3 inside = new Vector3(x, yIn, z);
                    if (!March(skullField, inside, Vector3.up, reach, 0.001f, out Vector3 bt)) bt = new Vector3(x, g.profC[pi] + g.profH[pi], z);
                    float sc = side < 0 && BladeScaleForTest > 0f ? BladeScaleForTest : 1f;
                    bladeDef.Add(k);
                    bladeFrame.Add(Matrix4x4.TRS(bt, Quaternion.AngleAxis(-side * d.splayDeg, Vector3.forward), new Vector3(sc, sc, sc)));   // leaning toward side * x
                }
            }
            g.bladeDef = bladeDef.ToArray(); g.bladeFrame = bladeFrame.ToArray();

            // a sphere round every piece (the runtime's culling bounds), the open jaw included
            Vector3 lo = Vector3.Min(skullField.Lo, Vector3.zero), hi = Vector3.Max(skullField.Hi, Vector3.zero);
            for (int b2 = 0; b2 < g.bladeDef.Length; b2++)
            {
                CrestDef d = crests[g.bladeDef[b2]];
                float r = 1.2f * (d.length + RootChord(d));
                Vector3 o = g.bladeFrame[b2].GetColumn(3);
                lo = Vector3.Min(lo, o - new Vector3(r, r, r)); hi = Vector3.Max(hi, o + new Vector3(r, r, r));
            }
            g.boundsCentre = 0.5f * (lo + hi); g.boundsRadius = 0.5f * (hi - lo).magnitude + Mathf.Sin(gapeDeg * Mathf.Deg2Rad) * (zt - zh);
            return g;
        }

        // RED test hooks (CreatureCrestBeakHeadTests): the old blade of six blended blobs; crests sunk by this x height.
        internal static bool BlobCrestsForTest;
        internal static bool BlobSharpTipForTest;   // ... tapering to the same point as the real blade (only its scallops differ)
        internal static float CrestSinkForTest;
        internal static float BladeScaleForTest;   // > 0: a mirrored pair's -x blade drawn this much larger (an unequal pair)

        const int kCrestSeg = 28, kCrestSteps = 112;

        // The tangent's angle from straight up toward +z (forward) at u (0 root .. 1 tip): beta at the root, curling back
        // by curlDeg with the curvature growing toward the tip (u^1.5: rises, then breaks backward like a wave).
        static float CrestAngle(float beta, float curl, float u) => beta - curl * u * Mathf.Sqrt(u);

        static float CrestPeak(float beta, float curl, float len)
        {
            float y = 0f, peak = 0f, ds = len / kCrestSteps;
            for (int i = 0; i < kCrestSteps; i++) { y += Mathf.Cos(CrestAngle(beta, curl, (i + 0.5f) / kCrestSteps)) * ds; peak = Mathf.Max(peak, y); }
            return peak;
        }

        // The centre line, kCrestSeg + 1 points from the root: the forward lean at the root is the least that brings the
        // highest point to rise (height / cos splay, along the blade's own up) above the beak (a straight blade leans forward; a curling one stands nearly upright).
        static Vector3[] CrestLine(CrestDef d, Vector3 root, float sink, float rise)
        {
            float curl = d.curlDeg * Mathf.Deg2Rad, len = d.length, want = rise + sink;
            float beta = 0f;
            if (CrestPeak(0f, curl, len) > want)
            {
                float lo = 0f, hi = 0f;
                for (float b = 0f; b <= 85f; b += 0.5f) { hi = b * Mathf.Deg2Rad; if (CrestPeak(hi, curl, len) <= want) break; lo = hi; }
                for (int i = 0; i < 30; i++) { float m = 0.5f * (lo + hi); if (CrestPeak(m, curl, len) > want) lo = m; else hi = m; }
                beta = 0.5f * (lo + hi);
            }
            var pts = new Vector3[kCrestSeg + 1];
            pts[0] = root;
            Vector3 p = root; float ds = len / kCrestSteps;
            int per = kCrestSteps / kCrestSeg;
            for (int i = 0; i < kCrestSteps; i++)
            {
                float a = CrestAngle(beta, curl, (i + 0.5f) / kCrestSteps);
                p += new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a)) * ds;
                if ((i + 1) % per == 0) pts[(i + 1) / per] = p;
            }
            return pts;
        }

        // The blade's half chord at its root (CrestDef.chord, 0 = 0.35 x its length), and its flattening (chord /
        // thickness, one value for the whole blade so the cones' ends match at every join).
        static float RootChord(CrestDef d) => d.chord > 0f ? d.chord : 0.35f * d.length;
        static float Flatten(CrestDef d) => Mathf.Clamp(RootChord(d) / d.thickness, 1f, 4f);

        // The blade: kCrestSeg flattened round cones along its centre line, their radii shared at the joins and tapering
        // linearly to a point (Sculpt.MinCell) at the tip, one flattening - a smooth, thin, sharp blade; and a flared foot
        // sunk into the beak.
        static SculptShape[] CrestShapes(CrestDef d, Vector3[] line, Vector3 beakTop)
        {
            float t = d.thickness, sq = Flatten(d), tip = 0.05f * t;   // a point finer than the crest's cell
            var s = new List<SculptShape>();
            for (int i = 0; i < kCrestSeg; i++)
            {
                float r0 = Mathf.Lerp(t, tip, (float)i / kCrestSeg), r1 = Mathf.Lerp(t, tip, (float)(i + 1) / kCrestSeg);
                s.Add(Cone(line[i], line[i + 1], r0, r1, sq, 0f));   // no blend: a smooth union swells every join by blend / 4 (a ripple)
            }
            float chord = sq * t;
            s.Add(Ell(beakTop - new Vector3(0f, 0.6f * t, 0f), new Vector3(1.3f * t, 0.9f * t, 1.15f * chord), SculptOp.Union, 0.9f * t));
            return s.ToArray();
        }

        // RED reference (BlobCrestsForTest): the first version - six capsules with their own flattening each, blended
        // 0.6 x radius, ending at 0.35 x thickness.
        static SculptShape[] BlobCrestShapes(CrestDef d, Vector3[] line, Vector3 beakTop)
        {
            const int n = 6;
            float t = d.thickness, chord = Mathf.Min(0.35f * d.length, 3.6f * t);
            var s = new List<SculptShape>();
            for (int i = 0; i < n; i++)
            {
                float u0 = (float)i / n, u1 = (float)(i + 1) / n, um = 0.5f * (u0 + u1);
                float r0 = t * (1f - 0.65f * u0), r1 = t * (1f - 0.65f * u1);
                float c = chord * (1f - 0.8f * um), r = t * (1f - 0.65f * um);
                if (BlobSharpTipForTest)
                {
                    r0 = Mathf.Lerp(t, Sculpt.MinCell, u0); r1 = Mathf.Lerp(t, Sculpt.MinCell, u1);
                    r = Mathf.Lerp(t, Sculpt.MinCell, um); c = chord * (1f - 0.9f * um);
                }
                Vector3 a = line[Mathf.RoundToInt(u0 * kCrestSeg)], b = line[Mathf.RoundToInt(u1 * kCrestSeg)];
                s.Add(Cone(a, b, r0, r1, Mathf.Clamp(c / r, 1f, 4f), i == 0 ? 0f : 0.6f * r0));
            }
            s.Add(Ell(beakTop - new Vector3(0f, 0.6f * t, 0f), new Vector3(1.3f * t, 0.9f * t, 1.15f * chord), SculptOp.Union, 0.9f * t));
            return s.ToArray();
        }

        // ------------------------------------------------------------ mesh

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            CreatureSpecies sp = m.Species;
            int j = Mathf.Clamp(at.joint, 0, m.Joints - 1), jaw = sp.Layout.Start(c.Index);
            Vector3 o = m.Rest.Joints[j] + m.Rest.Rotations[j] * at.offset;
            Quaternion tilt = RestTilt(m.Rest, j);
            IPartRuntime[] rts = m.Rest.PartRuntimes;
            if (rts != null) foreach (IPartRuntime r in rts) if (r is CrestBeakHeadRuntime h && h.JawBone == jaw) tilt = h.Tilt;   // the bones' own
            Matrix4x4 B = Matrix4x4.TRS(o, m.Rest.Rotations[j] * tilt, Vector3.one);
            float uBase = sp.headCapLength + sp.spacing * j;   // the body's uv0.x at the head joint (metres from the front tip)
            Built g = Geo();
            if (g.dorsal == null) g.dorsal = new float[2 + g.bladeDef.Length][];
            Emit(m, g, 0, SculptMesher.For(g.skullSculpt), B, Matrix4x4.identity, j, PartMeshEmitter.PartBody, uBase);
            Emit(m, g, 1, SculptMesher.For(g.jawSculpt), B, Matrix4x4.identity, jaw, PartMeshEmitter.PartBody, uBase);
            if (g.bladeDef.Length > 0)
            {
                int kind = m.Slot("crest");
                for (int b = 0; b < g.bladeDef.Length; b++) Emit(m, g, 2 + b, SculptMesher.For(g.crestSculpt[g.bladeDef[b]]), B, g.bladeFrame[b], j, kind, uBase);
            }
        }

        // Each vertex's dorsal value: (y - centre) / half depth of the head's side outline at its z (the body's sin phi on
        // a round section); the jaw a little paler from the hinge forward (0 at the hinge, so its rear flows into the
        // throat's colour); crests 1 (their row is their own colour).
        static float[] Dorsal(Built g, int which, SculptPiece piece)
        {
            var d = new float[piece.Pos.Length];
            int n = g.profC.Length;
            for (int i = 0; i < d.Length; i++)
            {
                if (which >= 2) { d[i] = 1f; continue; }
                Vector3 p = piece.Pos[i];
                float f = Mathf.Clamp((p.z - g.profZ0) / g.profDz, 0f, n - 1.001f);
                int a = (int)f; float w = f - a;
                float cc = Mathf.Lerp(g.profC[a], g.profC[a + 1], w), hh = Mathf.Lerp(g.profH[a], g.profH[a + 1], w);
                d[i] = (p.y - cc) / hh;
                if (which == 1) { float r = Mathf.Clamp01((p.z - g.hinge.z) / (0.25f * Mathf.Max(1e-3f, g.profDz * n))); d[i] -= 0.3f * r * r * (3f - 2f * r); }
            }
            return d;
        }

        // local: the piece's frame in the head frame (a blade's BladeFrame; identity for the skull and jaw).
        static void Emit(PartMeshEmitter m, Built g, int which, SculptPiece piece, Matrix4x4 B, Matrix4x4 local, int bone, int part, float uBase)
        {
            float[] dorsal = g.dorsal[which];
            if (dorsal == null || dorsal.Length != piece.Pos.Length) g.dorsal[which] = dorsal = Dorsal(g, which, piece);
            Vector4 skin = PartMeshEmitter.Rigid(bone);
            Vector3 up = PartMeshEmitter.Unit(B.GetColumn(1), Vector3.up);
            int first = m.VertexCount;
            for (int i = 0; i < piece.Pos.Length; i++)
            {
                Vector3 p = local.MultiplyPoint3x4(piece.Pos[i]);
                m.Vertex(B.MultiplyPoint3x4(p), PartMeshEmitter.Unit(B.MultiplyVector(local.MultiplyVector(piece.Nrm[i])), up), uBase - p.z, piece.V[i], part, dorsal[i], skin);
            }
            int[] t = piece.Tris;
            for (int k = 0; k < t.Length; k += 3) m.Tri(first + t[k], first + t[k + 1], first + t[k + 2], false);
        }

        // ------------------------------------------------------------ runtime support

        // The skull's frame in the head joint's: the joint's frame is level (the heading), the neck rises into it; the
        // skull's long axis continues the neck's line there (the chord from the next joint, as the sim laid the chain
        // out when it was made - the rest pose), turned nose-down by pitchDeg.
        internal Quaternion RestTilt(CreatureSim sim, int j)
        {
            float up = 0f;
            if (j + 1 < sim.Joints.Length)
            {
                Vector3 d = Quaternion.Inverse(sim.Rotations[j]) * (sim.Joints[j] - sim.Joints[j + 1]);
                if (d.z > 1e-6f) up = Mathf.Atan2(d.y, d.z) * Mathf.Rad2Deg;
            }
            return Quaternion.AngleAxis(pitchDeg - up, Vector3.right);
        }

        internal Vector3 BoundsCentre => Geo().boundsCentre;
        internal float BoundsRadius => Geo().boundsRadius;
    }

    // One head of one individual (stage Late): the jaw bone about the hinge (its own call timer) and the eye bones in
    // the sockets.
    public sealed class CrestBeakHeadRuntime : IPartRuntime
    {
        readonly CrestBeakHead p;
        readonly CreatureSim sim;
        readonly int jaw, head;
        readonly Vector3 hinge, eye, boundsCentre;   // in the head joint's frame (the skull's, tilted)
        readonly float boundsRadius;
        float gape, wait, elapsed;
        bool calling;
        uint rng;

        public float Gape => GapeOverride >= 0f ? GapeOverride : gape;   // degrees, 0 = closed
        public bool Calling => calling;
        public int JawBone => jaw;
        // The skull's frame in the head joint's (CrestBeakHead.RestTilt, fixed when the individual is made).
        public readonly Quaternion Tilt;
        // Tests and tools: hold the jaw at this gape (degrees); < 0 = its own calls.
        public float GapeOverride = -1f;
        // RED test hook (CreatureCrestBeakHeadTests.Red_Jaw_IntoTheSkull): the jaw turns the wrong way.
        internal static bool FlipGapeForTest;

        public CrestBeakHeadRuntime(CrestBeakHead part, CreatureSim sim, int jawBone, int headJoint)
        {
            p = part; this.sim = sim; jaw = jawBone; head = headJoint;
            Tilt = part.RestTilt(sim, headJoint);
            hinge = Tilt * part.Hinge;
            Vector3 e = part.EyeSurface;
            e.x -= 0.35f * Mathf.Max(0f, sim.Species.eyeRadius);   // the eyeball sits a little into its socket
            eye = Tilt * e;
            boundsCentre = Tilt * part.BoundsCentre; boundsRadius = part.BoundsRadius;
            rng = (uint)(sim.Seed * 747796405u + 2891336453u + (uint)jawBone * 2654435761u);
            if (rng == 0) rng = 1;
            wait = NextWait();
        }

        float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }
        float NextWait() => Mathf.Lerp(p.callEverySeconds.x, p.callEverySeconds.y, Rand());   // Froude seconds

        public PartStage Stage => PartStage.Late;

        // Starts a call now (a behaviour may ask for one); ignored while calling.
        public void Call() { if (!calling) { calling = true; elapsed = 0f; } }

        // The call's shape over its phase u (0..1): open fast, hold, close.
        static float Envelope(float u)
        {
            float a = Mathf.Clamp01(u / 0.3f), b = Mathf.Clamp01((u - 0.6f) / 0.4f);
            a = a * a * (3f - 2f * a); b = b * b * (3f - 2f * b);
            return a * (1f - b);
        }

        public void Step(in StepCtx c, float dt)
        {
            if (!(dt > 0f)) return;
            float ft = dt / sim.TimeScale;   // Froude time: 0.9x and 1.1x individuals call alike
            if (calling)
            {
                elapsed += ft;
                float u = elapsed / p.callSeconds;
                if (u >= 1f - 1e-4f) { calling = false; gape = 0f; wait = NextWait(); }
                else gape = p.gapeDeg * Envelope(u);
            }
            else
            {
                wait -= ft;
                if (wait <= 1e-4f) { calling = true; elapsed = Mathf.Max(0f, -wait); gape = p.gapeDeg * Envelope(elapsed / p.callSeconds); }
            }
        }

        public void WriteBones(Matrix4x4[] dst, int o)
        {
            float s = sim.SizeScale;
            Quaternion r = sim.Rotations[head];
            Matrix4x4 H = Matrix4x4.TRS(sim.Joints[head] + r * (p.at.offset * s), r, new Vector3(s, s, s));
            Quaternion q = Quaternion.AngleAxis(FlipGapeForTest ? -Gape : Gape, Vector3.right);   // + = the front drops
            dst[o + jaw] = H * Matrix4x4.TRS(hinge - q * hinge, q, Vector3.one);   // about the hinge
            CreatureSpecies sp = sim.Species;
            for (int e = 0; e < 2; e++)
            {
                Vector3 eo = eye; eo.x *= e == 0 ? -1f : 1f;   // eye 0 left (-x); the tilt is about x, so the mirror holds
                dst[o + sp.EyeBone(e)] = H * Matrix4x4.TRS(eo, Quaternion.identity, Vector3.one);
            }
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            float s = sim.SizeScale;
            Quaternion r = sim.Rotations[head];
            Vector3 c = sim.Joints[head] + r * ((p.at.offset + boundsCentre) * s);
            float R = boundsRadius * s;
            lo = Vector3.Min(lo, c - new Vector3(R, R, R)); hi = Vector3.Max(hi, c + new Vector3(R, R, R));
        }

        public void DrawDebug(ICreatureDebugDraw d) { }
    }
}
