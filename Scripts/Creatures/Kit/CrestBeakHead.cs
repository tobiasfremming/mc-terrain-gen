// Creature kit (Apex redesign §5.4): CrestBeakHead - the beaked serpent head, rigid to a spine body's head joint
// (a FlowBody's joint 0). Built from signed-distance solids (SculptField, polygonised by SculptMesher and cached per
// shape set) from head-scale numbers:
//   skull   a long, slightly flattened cranium ellipsoid with a jowl at each jaw angle, a round cone back into the neck
//           (smaller than the neck where it starts, so the neck's front rings run into it without a step), a long snout
//           cone, and the upper beak: a cone riding on the mouth line to where the jaw ends, then the hooked tip, a cone
//           to the point that hooks below the mouth line by beakHookDeg; all smooth-unioned with generous blends.
//           The mouth line runs straight from the beak back to the gape's corner, far back under and past the eye
//           (mouthCornerAt). The jaw's region - a box below the mouth line whose top-rear edge is rounded with the jaw's
//           depth, so the cut sweeps from the gape's corner down and back into the throat with no rear face - is carved
//           out with a small blend (the lip edge) and the hooked tip put back; then a soft brow over each eye socket,
//           nostril pits and (optional, grooves) lamellae furrows along the lips.
//   tilt    the joint's frame is level (the heading); the skull's long axis continues the neck's line into the head
//           joint in the rest pose (RestTilt), turned by pitchDeg - fixed per individual when its runtime is made.
//   jaw     the same solids inside that region (the same outer surface: the mouth line a fine crease, the jaw flowing
//           into the throat), ending under the hook with a chin sloping back and a rounded knob under its tip (chinKnob);
//           rigid to the part's one bone, which turns about the hinge (Pivot: a little behind and below the gape's
//           corner) - hanging restGapeDeg open at rest (the mesh is bound there), gapeDeg wide on a call.
//   teeth   horns along the lip line, `teeth` per row and side: the upper row on the skull's margin pointing down, the
//           lower row between them on the jaw's pointing up, both raked back and splayed out so their tips stand
//           outboard of the opposite lip (a toothy grin even with the mouth shut); fangPairs fangs at the jaw's tip,
//           pointing forward and up. Upper teeth rigid to the head joint, lower teeth and fangs ride the jaw's bone.
//   crests  each CrestDef is one blade on top of the upper beak - or, mirrored, an equal pair rooted side by side that
//           lean apart by splayDeg each, a V seen from the front (the Apex's). A blade: 56 flattened round cones along a
//           centre line in its own plane that leans forward at its root and curls back by curlDeg (curvature growing
//           toward the tip: a wave breaking backward), one flattening (chord / thickness) and radii shared at every
//           join - a sickle: from a narrow prong it flares to its widest at 0.4 of its length (flare) and sweeps to a
//           point (flare 0: a plain taper); a flared foot sunk into the beak. Built once per CrestDef in the blade's own
//           frame (a RoundCone's flattening follows world up, so a leaning blade is drawn upright and turned into place
//           - BladeFrame - when it is emitted; the pair shares one polygonised piece).
// Look: skull and jaw on the body's skin (layer 0) with the dorsal channel from the height in the head frame,
// (y - centre) / half depth of the head's side outline at that z - the body's sin(phi) on a round section, so the
// seam into the neck does not show in colour; the jaw a little paler from the hinge forward (no step at its rear);
// crests on the named kind "crest" (default row: glossy keratin, bright teal #3FC0B0; a species restyles it with a
// Layers row); teeth on the kind "teeth" (legacy id 13) at the pale end of the dorsal channel (the belly colour: ivory
// on the Apex; a Layers row naming "teeth" restyles them). The derived solids, teeth and each piece's dorsal values are
// cached per parameter set.
// Runtime (stage Late): the jaw rests at restGapeDeg and opens to gapeDeg on its own calls - a timer between
// callEverySeconds.x and .y (Froude), seeded per individual - and the species' two eye bones sit in the sockets. Head
// frame: +Z along the head (the beak), +Y up, origin at the head joint (plus at.offset), metres at scale 1.
// Allocation-free after construction; main thread.
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
        [Param(ParamUnit.Metres, "blade half thickness at its widest (its root; with flare, where it flares out) - it tapers to a point at the tip")] public float thickness;
        [Param(ParamUnit.Metres, "blade half chord (its width seen from the side) at its widest; 0 = 0.35 x length; chord / thickness is capped at 4 (it follows the thickness)")] public float chord;
        [Param(ParamUnit.Fraction, "a sickle: the blade rises from a narrow prong and flares out to its widest (at flareAt of its length) this many times its root's width, then sweeps to the point; 0 = a plain taper from the root (a horn)", Min = 0, Max = 6)] public float flare;
        [Param(ParamUnit.Fraction, "where along the blade it is widest (with flare): toward its end, a crescent (0 = 0.4). The Apex's 0.62 is the latest that keeps the outline smooth: past it the fall to the point is steeper, and with outside's arch (widest at 0.5) the measured widest moves from 0.61 toward the point as this grows", Min = 0, Max = 0.9f)] public float flareAt;
        [Param(ParamUnit.Fraction, "the blade's width shifted to the outside (front, convex side) of its curl: its axis arches out from the centre line by up to this share of the widest half chord (sin(pi u): none at the root and the point) - a broad crescent outside, a thin inner edge hooking to the point (0 = symmetric)", Min = 0, Max = 0.8f)] public float outside;
        [Param(ParamUnit.Degrees, "the tip hooks back by this much more over the last quarter of the blade (on top of curlDeg)", Min = 0, Max = 120)] public float hookDeg;
        [Param(ParamUnit.Degrees, "the blade leans out from upright by this much (toward +x; a mirrored pair: each away from the midline - a V from the front)", Min = 0, Max = 75)] public float splayDeg;
        [Param(ParamUnit.Flag, "an equal pair rooted side by side on the midline, mirrored left and right (else one blade on the midline)")] public bool mirror;
    }

    [Serializable]
    [Part("CrestBeakHead", PartKind.Rigid, Bones = "2 (the jaw, the upper head)",
          Summary = "The beaked serpent head: a long, slightly flattened SDF skull flowing into a pointed, hooked beak, small eyes under a soft brow, a mouth line running back past the eye with rows of small interlocking teeth (fangs at the lower jaw's tip, a knob under it), a deep fleshy lower jaw running back into the throat, a pink mouth inside with cheek webs that stretch when it gapes, sickle crests (a prong widening into a crescent that hooks back) on top of the upper beak, and a jaw on its own bone that hangs a little open at rest and gapes wide on its own call timer while the upper head tips up. Rigid to the body's head joint; its back overlaps the neck so the outline runs on; carries the species' eyes.",
          System = "CrestBeakHeadRuntime (part runtime, stage Late: jaw, upper head, eye bones) + CrestBeakHead.EmitMesh (SculptMesher pieces, cached; teeth as horns; cheek webs skinned between the bones)",
          MaterialLayers = new[] { 0 }, MaterialKinds = new[] { "crest", "mouth" }, KitOnly = true,
          UseWhen = "a serpent- or dragon-like head ending in a beak, on a FlowBody (or SpineBody) head joint: crested, toothed beaks; the Apex")]
    public sealed class CrestBeakHead : CreaturePart, IMaterialKinds
    {
        [Param(ParamUnit.Metres, "skull length; the skull reaches half of it behind the head joint (into the neck) and half ahead to the beak's root", Example = "fixture 0.26")] public float skullLength = 0.26f;
        [Param(ParamUnit.Metres, "skull half width (>= skullDepth: slightly flattened)")] public float skullWidth = 0.055f;
        [Param(ParamUnit.Metres, "skull half depth (the head's back starts at ~0.6 x it inside the neck)")] public float skullDepth = 0.067f;
        [Param(ParamUnit.Metres, "beak length from its root (skullLength / 2 ahead of the joint) to the tip")] public float beakLength = 0.26f;
        [Param(ParamUnit.Degrees, "the upper beak's tip hooks down by this much over the beak's last third, over the lower jaw's tip", Min = -10, Max = 30)] public float beakHookDeg = 11f;
        [Param(ParamUnit.Fraction, "how far the brow juts out over the eye socket", Min = 0, Max = 1)] public float browRidge = 0.3f;
        [Param(ParamUnit.Struct, "crests on top of the upper beak: each one blade, or (mirror) an equal pair splayed into a V")] public CrestDef[] crests =
        {
            new CrestDef { along = 0.8f, height = 0.0825f, length = 0.1275f, curlDeg = 95f, thickness = 0.0065f, chord = 0.026f, flare = 2.6f, flareAt = 0.62f, outside = 0.75f, hookDeg = 40f, splayDeg = 30f, mirror = true },
        };
        [Param(ParamUnit.Count, "lamellae: fine grooves across the mouth line (the teeth took their place: 0)", Min = 0, Max = 32)] public int grooves = 0;
        [Param(ParamUnit.Metres, "groove depth")] public float grooveDepth = 0.004f;
        [Param(ParamUnit.Fraction, "the lower jaw's share of the head's depth (the mouth line's height)", Min = 0.15f, Max = 0.7f)] public float jawFraction = 0.5f;
        [Param(ParamUnit.Fraction, "the gape's corner (the jaw's hinge): fraction of skullLength ahead of the head joint (negative = behind it: the mouth line runs back under and past the eye)", Min = -0.4f, Max = 0.3f)] public float mouthCornerAt = -0.15f;
        [Param(ParamUnit.Fraction, "the jaw's hinge: it turns about a point this far (x the jaw's depth at the gape's corner) behind the corner and half as far below it - the gape opens back into the throat", Min = 0, Max = 1.5f)] public float hingeBehind = 0.15f;
        [Param(ParamUnit.Fraction, "the lower jaw's own depth below the mouth line at mid-length, x skullDepth (a deep, fleshy jaw; 0 = only what the head's outline gives)", Min = 0, Max = 2)] public float jawDepth = 0.9f;
        [Param(ParamUnit.Fraction, "the lower jaw's depth at its chin, x skullDepth", Min = 0, Max = 2)] public float chinDepth = 0.5f;
        [Param(ParamUnit.Fraction, "the lower jaw's depth at the gape's corner, where its underside runs back into the throat, x skullDepth", Min = 0, Max = 2)] public float throatDepth = 0.6f;
        [Param(ParamUnit.Fraction, "cheek webs: skin from the upper lip to the lower one, reaching this far (x skullLength) forward from the gape's corner, stretched between the head and the jaw when it gapes (0 = none: the open mouth is a slot at the corner)", Min = 0, Max = 0.5f)] public float cheekWeb = 0.16f;
        [Param(ParamUnit.Metres, "the throat: a pink plug at the back of the mouth hiding the body's front end (the neck's front rings and cap run into the head; an open mouth would show them) - its half size, about the neck's half height at the head joint (0 = none)")] public float throatRadius = 0.055f;
        [Param(ParamUnit.Metres, "how far ahead of the head joint the body's front end reaches (the FlowBody's front ring plus its headCapLength): the throat plug covers it to here")] public float throatReach = 0.065f;
        [Param(ParamUnit.Degrees, "the jaw's widest gape (a call): the lower jaw drops this far about the hinge", Min = 0, Max = 80)] public float gapeDeg = 62f;
        [Param(ParamUnit.Degrees, "on a call the upper head (skull, beak, crests) tips up this far at the full gape, about the skull's back", Min = 0, Max = 30)] public float callPitchDeg = 6f;
        [Param(ParamUnit.Degrees, "the mouth hangs this far open at rest, showing the teeth (< gapeDeg)", Min = 0, Max = 30)] public float restGapeDeg = 7f;
        [Param(ParamUnit.Count, "teeth in each row (upper and lower, each side): small pointed teeth along the lip line, the lower row between the upper's, showing even at rest", Min = 0, Max = 40)] public int teeth = 16;
        [Param(ParamUnit.Metres, "tooth length (the ones near the gape's corner shorter)")] public float toothLength = 0.014f;
        [Param(ParamUnit.Metres, "tooth base radius")] public float toothRadius = 0.003f;
        [Param(ParamUnit.Text, "the material kind of the teeth and fangs: ivory-teeth (the kit's ivory keratin row, a kit slot of its own). A species short of kit slots uses the legacy \"teeth\" (registry row 13: no kit slot) with a Layers row restyling 13 to ivory Claw. Validate: a registry / NamedKinds kind or one a Layers row names")] public string toothKind = "ivory-teeth";
        [Param(ParamUnit.Degrees, "the teeth lean back toward the throat by this much (both rows)", Min = -45, Max = 60)] public float toothRakeDeg = 18f;
        [Param(ParamUnit.Count, "fang pairs at the lower jaw's tip (0..2: up to four small fangs)", Min = 0, Max = 2)] public int fangPairs = 2;
        [Param(ParamUnit.Metres, "fang length (the second pair 0.85 x)")] public float fangLength = 0.022f;
        [Param(ParamUnit.Degrees, "the fangs point forward out of the jaw's tip, raised this far above its line", Min = -30, Max = 90)] public float fangLiftDeg = 35f;
        [Param(ParamUnit.Metres, "radius of the rounded knob under the lower jaw's tip (0 = none)")] public float chinKnob = 0.009f;
        [Param(ParamUnit.Seconds, "time between calls: x the shortest, y the longest (each individual draws its own, seeded)")] public Vector2 callEverySeconds = new Vector2(6f, 14f);
        [Param(ParamUnit.Seconds, "one call: open, hold, close")] public float callSeconds = 0.8f;
        [Param(ParamUnit.Fraction, "eye centre: x along the skull (fraction of skullLength ahead of the joint), y height (fraction of skullDepth)", Min = -0.5f, Max = 0.5f)] public Vector2 eyeAt = new Vector2(0.15f, 0.4f);
        [Param(ParamUnit.Degrees, "the head's long axis tilts nose-down (+) or up (-) from the neck's line at the head joint in the rest pose (0 = the head continues the neck)", Min = -45, Max = 45)] public float pitchDeg = 0f;
        [Param(ParamUnit.Metres, "polygoniser cell of the skull and jaw (the crests use a finer one from their thickness)")] public float cell = 0.005f;

        public const int MaxCrests = 8, MaxGrooves = 32, MaxTeeth = 40;

        public override int BoneCount(in SpeciesCtx c) => 2;   // the jaw, the upper head

        // A kind the species can draw with: a registry or NamedKinds kind, or one a Layers row names.
        static bool KindKnown(string kind, in SpeciesCtx c)
        {
            if (PartLayers.Find(kind, out _)) return true;
            for (int i = 0; i < c.Modules.Count; i++)
                if (c.Modules[i] is Layers l && l.rows != null)
                    foreach (LayerDef d in l.rows) if (d.kind == kind) return true;
            return false;
        }

        // IMaterialKinds: the teeth's kind (data), besides the crest and mouth kinds of [Part].
        public void Kinds(List<string> into) { if (!string.IsNullOrEmpty(toothKind)) into.Add(toothKind); }
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
            if (restGapeDeg < 0f || restGapeDeg > 30f || restGapeDeg > gapeDeg) return $"{id}: restGapeDeg must be 0..30 and <= gapeDeg";
            if (mouthCornerAt < -0.4f || mouthCornerAt > 0.3f) return $"{id}: mouthCornerAt must be -0.4..0.3";
            if (hingeBehind < 0f || hingeBehind > 1.5f) return $"{id}: hingeBehind must be 0..1.5";
            if (teeth < 0 || teeth > MaxTeeth) return $"{id}: teeth must be 0..{MaxTeeth}";
            if (teeth > 0 && (!(toothLength > 0f) || !(toothRadius > 0f))) return $"{id}: toothLength and toothRadius must be > 0";
            if (toothRakeDeg < -45f || toothRakeDeg > 60f) return $"{id}: toothRakeDeg must be -45..60";
            if (fangPairs < 0 || fangPairs > 2) return $"{id}: fangPairs must be 0..2";
            if (fangPairs > 0 && !(fangLength > 0f)) return $"{id}: fangLength must be > 0";
            if (fangLiftDeg < -30f || fangLiftDeg > 90f) return $"{id}: fangLiftDeg must be -30..90";
            if (chinKnob < 0f) return $"{id}: chinKnob must be >= 0";
            if (jawDepth < 0f || chinDepth < 0f || throatDepth < 0f || jawDepth > 2f || chinDepth > 2f || throatDepth > 2f) return $"{id}: jawDepth, chinDepth and throatDepth must be 0..2";
            if (cheekWeb < 0f || cheekWeb > 0.5f) return $"{id}: cheekWeb must be 0..0.5";
            if (throatRadius < 0f || throatReach < -skullLength) return $"{id}: throatRadius must be >= 0 and throatReach > -skullLength";
            if ((teeth > 0 || fangPairs > 0) && !string.IsNullOrEmpty(toothKind) && !KindKnown(toothKind, c)) return $"{id}: toothKind '{toothKind}' is neither a registry or kit kind (PartLayers) nor named by a Layers row";
            if (callPitchDeg < 0f || callPitchDeg > 30f) return $"{id}: callPitchDeg must be 0..30";
            if ((teeth > 0 || fangPairs > 0) && string.IsNullOrEmpty(toothKind)) return $"{id}: toothKind is empty";
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
                if (d.flare != 0f && (d.flare < 1f || d.flare > 6f)) return $"{id}: crest {k}: flare must be 0 or 1..6";
                if (d.flareAt != 0f && (d.flareAt < 0.2f || d.flareAt > 0.9f)) return $"{id}: crest {k}: flareAt must be 0 or 0.2..0.9";
                if (d.outside < 0f || d.outside > 0.8f) return $"{id}: crest {k}: outside must be 0..0.8";
                if (d.hookDeg < 0f || d.hookDeg > 120f) return $"{id}: crest {k}: hookDeg must be 0..120";
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
        // Blade b's cones' axis (its own frame): the centre line moved toward the outside of the curl by outside x the
        // half chord - the line the blade's outline is symmetric about.
        public Vector3[] CrestAxis(int b) => (Vector3[])Geo().axis[Geo().bladeDef[b]].Clone();
        public Matrix4x4 BladeFrame(int b) => Geo().bladeFrame[b];
        // The jaw's hinge and the right eye's centre on the socket floor (head frame).
        public Vector3 Hinge => Geo().hinge;
        // The point the jaw turns about (head frame): behind and below the gape's corner by hingeBehind (the gape reaches
        // back into the throat; the jaw's rear swings less).
        public Vector3 Pivot => Geo().pivot;
        // The point the upper head tips up about on a call (head frame): the skull's back, inside the neck.
        public Vector3 SkullPivot => Geo().skullPivot;
        // The cheek web's columns (+x side, head frame, the jaw closed): its upper and lower edge from the corner forward
        // (empty: no web).
        public Vector3[] WebTop => (Vector3[])(Geo().webTop ?? new Vector3[0]).Clone();
        public Vector3[] WebBot => (Vector3[])(Geo().webBot ?? new Vector3[0]).Clone();
        public Vector3 EyeSurface => Geo().eye;
        // The mouth line's height at z (head frame, the jaw closed): straight from the gape's corner (the hinge) forward.
        public float MouthY(float z) { Built g = Geo(); return g.hinge.y + g.mouthSlope * (z - g.hinge.z); }
        // The lower jaw's front end at the mouth line (z, head frame) and the knob's centre under its tip.
        public float JawFrontZ => Geo().jawFront;
        public Vector3 KnobCentre => Geo().knob;

        // One tooth or fang (head frame, the jaw closed): Horn from basePt along dir (length, base radius, curling back
        // by curveDeg - the tip is where the curve ends), leaving the gum at emerge. Lower ones ride the jaw's bone.
        public struct ToothDef { public Vector3 basePt, dir, up, emerge, tip; public float length, radius, curveDeg; public bool lower, fang; }
        public int ToothCount => Geo().teeth.Length;
        public ToothDef Tooth(int i) => Geo().teeth[i];

        // The jaw's place at a gape (degrees) in the head frame: a turn about the hinge, + = the front drops. The jaw
        // piece is drawn closed; the mesh binds it at restGapeDeg (the runtime's resting gape), so a bone at gape G shows it at G.
        public Matrix4x4 JawPose(float deg)
        {
            Vector3 h = Pivot; Quaternion q = Quaternion.AngleAxis(deg, Vector3.right);
            return Matrix4x4.TRS(h - q * h, q, Vector3.one);
        }

        // The polygonised pieces: 0 the skull, 1 the jaw (closed: the mesh draws it at JawPose(restGapeDeg)), 2 + b blade b
        // (in its own frame; BladeFrame places it). The teeth are horns (Tooth), not pieces.
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
            public Vector3[][] spine, axis;   // per CrestDef: its centre line, and its cones' axis (moved outside by `outside`)
            public Sculpt skullSculpt, jawSculpt;
            public Sculpt[] crestSculpt;       // per CrestDef
            public int[] bladeDef;             // per blade: its CrestDef
            public Matrix4x4[] bladeFrame;     // per blade: its frame in the head frame
            public Vector3 hinge, pivot, eye, boundsCentre, knob;
            public float boundsRadius, mouthSlope, jawFront;
            public ToothDef[] teeth;
            public float profZ0, profDz;
            public float[] profC, profH;   // the side outline's centre and half depth at profZ0 + i profDz
            public float[][] dorsal;       // per piece (0 skull, 1 jaw, 2 + b blades): each vertex's dorsal value, made on first use
            public bool[][] inside;        // per piece (0 skull, 1 jaw): the vertex is on a cut face inside the closed head (the mouth)
            public SculptField headField, boxField;   // the closed head; the jaw's region (the mouth's cut)
            public Vector3[] webTop, webBot;   // per column (from the gape's corner forward): the cheek web's upper and lower edge (+x side, the jaw closed)
            public Vector3 skullPivot;
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
            h = Mix(Mix(Mix(Mix(Mix(h, restGapeDeg), mouthCornerAt), teeth), toothLength), toothRadius);
            h = Mix(Mix(Mix(Mix(Mix(h, toothRakeDeg), fangPairs), fangLength), fangLiftDeg), chinKnob);
            h = Mix(Mix(Mix(h, TeethInsetForTest), TeethLiftForTest), hingeBehind);
            h = Mix(Mix(Mix(Mix(Mix(h, jawDepth), chinDepth), throatDepth), cheekWeb), NoMouthKindForTest ? 1 : 0);
            h = Mix(Mix(Mix(h, throatRadius), throatReach), MouthNoBoxForTest ? 1 : 0);
            int n = crests?.Length ?? 0;
            h = Mix(h, n);
            for (int k = 0; k < n; k++) { CrestDef d = crests[k]; h = Mix(Mix(Mix(Mix(Mix(Mix(Mix(Mix(Mix(h, d.along), d.height), d.length), d.curlDeg), d.thickness), d.chord), d.flare), d.splayDeg), d.mirror ? 1 : 0); h = Mix(Mix(Mix(h, d.flareAt), d.outside), d.hookDeg); }
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
                Ell(new Vector3(0f, 0.05f * D, 0.02f * L), new Vector3(0.9f * W, 0.92f * D, 0.5f * L), SculptOp.Union, 0f),                         // cranium (deepest by the eye, easing back into the neck)
                Cone(new Vector3(0f, -0.02f * D, zb), new Vector3(0f, 0.05f * D, -0.1f * L), 0.6f * W, 0.85f * W, 0.95f * sq, 0.5f * W),    // back into the neck
                Ell(new Vector3(0.45f * W, -0.45f * D, -0.08f * L), new Vector3(0.38f * W, 0.45f * D, 0.28f * L), SculptOp.Union, 0.5f * W, true), // jowls
                Cone(new Vector3(0f, 0.05f * D, 0.1f * L), new Vector3(0f, -0.12f * D, zr + 0.9f * BL), 0.68f * W, 0.1f * W, 1.15f * sq, 0.6f * W), // snout
            };
            var H0 = new SculptField(head.ToArray());

            // the mouth line: on the beak jawFraction of the depth up from the bottom; at the hinge (the gape's corner,
            // under the eye) 0.1 of the depth higher - it rises toward the rear; straight between
            float zh = mouthCornerAt * L, zm = zr + 0.3f * BL;
            Outline(H0, zh, reach, out float th, out float bh);
            Outline(H0, zm, reach, out float tm, out float bm);
            Vector3 mh = new Vector3(0f, bh + jawFraction * (th - bh), zh), mm = new Vector3(0f, bm + jawFraction * (tm - bm), zm);
            Vector3 md = (mm - mh).normalized;
            float MouthY(float z) => mh.y + (mm.y - mh.y) * (z - zh) / (zm - zh);
            g.hinge = mh; g.mouthSlope = (mm.y - mh.y) / (zm - zh);
            {
                float jd = Mathf.Max(0.01f, mh.y - bh);   // the jaw's depth at the corner
                g.pivot = mh + new Vector3(0f, -0.5f * hingeBehind * jd, -hingeBehind * jd);
            }

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
            SculptShape hook = Cone(cF, new Vector3(0f, MouthY(zt) - drop, zt), rF, Mathf.Max(0.002f, 0.6f * cell), kBeakSquash, 0.2f * W);
            head.Add(beakA);
            head.Add(hook);
            // the lower jaw's own body: a deep, fleshy jaw under the mouth line - round cones from the gape's corner (its
            // underside running back into the throat, throatDepth) through mid-jaw (jawDepth) to the chin (chinDepth),
            // each hanging from the mouth line (its top on it), smooth-unioned into the head
            // (each cone 0.8 x the head's own half width at the mouth line there and hanging from 0.2 of its depth under
            // the line: the lips keep the head's outline, the upper lip's overhang and the teeth's margin)
            {
                var Hb = new SculptField(head.ToArray());
                float gapJ = Mathf.Max(0.003f, 0.5f * cell), zJ = zFront - rF - gapJ;
                float[] zs = { zh, Mathf.Lerp(zh, zJ, 0.5f), zJ - 0.5f * chinDepth * D };
                float[] dd = { throatDepth * D, jawDepth * D, chinDepth * D };
                var ww = new float[3];
                for (int i = 0; i < 3; i++)
                {
                    if (i == 0) { March(Hb, new Vector3(0f, MouthY(zh), zh), Vector3.right, reach, 0.0005f, out Vector3 s0); zs[0] = zh + 0.8f * 0.8f * s0.x; }   // its round end at the corner, not behind it
                    ww[i] = 0.8f * (March(Hb, new Vector3(0f, MouthY(zs[i]), zs[i]), Vector3.right, reach, 0.0005f, out Vector3 sw) ? sw.x : 0.5f * W);
                }
                for (int i = 0; i < 2; i++)
                {
                    if (!(dd[i] > 0.002f) || !(dd[i + 1] > 0.002f)) continue;
                    Vector3 a = new Vector3(0f, MouthY(zs[i]) - 0.7f * dd[i], zs[i]), b = new Vector3(0f, MouthY(zs[i + 1]) - 0.7f * dd[i + 1], zs[i + 1]);
                    float sqj = Mathf.Clamp(0.5f * (0.5f * dd[i] / ww[i] + 0.5f * dd[i + 1] / ww[i + 1]), 0.3f, 4f);
                    head.Add(Cone(a, b, ww[i], ww[i + 1], sqj, 0.25f * W));
                }
            }
            var H = new SculptField(head.ToArray());
            g.head = head.ToArray();
            g.headField = H;
            g.skullPivot = new Vector3(0f, 0.05f * D, zb);

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
            float jd0 = Mathf.Max(0.01f, mh.y - bh);   // the head outline's depth under the corner (before the jaw's own body)
            float R = 1.1f * jd0;
            float sRear = -R, sFront = (zt - zh) / Mathf.Max(0.2f, md.z) + R + 0.02f;   // along md from the hinge
            float hx = 3f * W + R, hy = 3f * D + R, hz = 0.5f * (sFront - sRear);
            float lip = Mathf.Max(0.5f * cell, 0.004f);
            var jawBox = new SculptShape
            {
                kind = SculptKind.Box, centre = mh + md * (0.5f * (sRear + sFront)) - boxUp * hy, size = new Vector3(hx, hy, hz), rotationDeg = new Vector3(pitch, 0f, 0f),
                round = R,
            };

            g.boxField = new SculptField(new[] { jawBox });
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
            SculptShape hookBack = hook; hookBack.blend = lip;   // (0.5 x lip pinched a sub-cell crease under the hook at some mouth lines)
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
            // the throat: a capsule round the body's front end (centred on the head's axis, from the gape's corner to
            // throatReach), a little larger than it, inside the closed head - its faces in the open mouth are the
            // mouth's (the back of the throat), and the neck's front rings and cap stay hidden when the jaw drops
            if (throatRadius > 0f)
            {
                float rr = throatRadius + 0.003f;
                March(H, new Vector3(0f, 0f, throatReach - rr), Vector3.right, reach, 0.0005f, out Vector3 sx);
                float rx = Mathf.Min(rr, Mathf.Max(0.005f, 0.85f * sx.x));
                float zEnd = Mathf.Max(zh + 0.01f, throatReach + 0.003f - rx);   // the round end reaches rx along the axis
                skull.Add(Cone(new Vector3(0f, 0f, zh), new Vector3(0f, 0f, zEnd), rx, rx, Mathf.Clamp(rr / rx, 1f, 4f), lip));
            }
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
            g.jawFront = zFront - rF - gap;
            // the knob under the jaw's tip: a round swelling on its underside a little behind the chin, most of it proud
            if (chinKnob > 0f)
            {
                var jawSoFar = new SculptField(jaw.ToArray());
                float zk = g.jawFront - 0.7f * chinKnob - 0.04f * BL, yk = MouthY(zk) - 0.01f;
                for (float y = MouthY(zk) - 0.002f; y > MouthY(zk) - reach; y -= 0.001f) { if (jawSoFar.Eval(new Vector3(0f, y, zk)) > 0f) break; yk = y; }
                g.knob = new Vector3(0f, yk + 0.2f * chinKnob, zk);
                jaw.Add(Ell(g.knob, new Vector3(0.9f * chinKnob, chinKnob, 1.15f * chinKnob), SculptOp.Union, 0.6f * chinKnob));
            }
            jaw.AddRange(lowerGrooves);
            g.jaw = jaw.ToArray();

            g.teeth = Teeth(H, skullField, new SculptField(g.jaw), zh, zFront, g.jawFront, MouthY, lip, reach);

            // cheek webs: from the gape's corner forward cheekWeb x L, an edge a little above and one a little below the
            // mouth line, a lip's width in from the head's side (hidden inside the closed lips; it shows when they part)
            if (cheekWeb > 0f)
            {
                const int kWebCols = 9;
                g.webTop = new Vector3[kWebCols]; g.webBot = new Vector3[kWebCols];
                for (int c = 0; c < kWebCols; c++)
                {
                    float z = zh + cheekWeb * L * c / (kWebCols - 1f), ym = MouthY(z);
                    if (!March(H, new Vector3(0f, ym, z), Vector3.right, reach, 0.0005f, out Vector3 sw)) sw = new Vector3(W, ym, z);
                    float x = Mathf.Max(0.002f, sw.x - lip);
                    g.webTop[c] = new Vector3(x, ym + 0.6f * lip, z); g.webBot[c] = new Vector3(x, ym - 0.6f * lip, z);
                }
            }

            g.skullSculpt = new Sculpt { id = "skull", shapes = g.skull, cell = cell };
            g.jawSculpt = new Sculpt { id = "jaw", shapes = g.jaw, cell = cell };

            // crests: blades on the beak's top, curling back; a mirrored def a pair side by side, splayed apart
            int nc = crests?.Length ?? 0;
            g.crest = new SculptShape[nc][]; g.spine = new Vector3[nc][]; g.axis = new Vector3[nc][]; g.crestSculpt = new Sculpt[nc];
            var bladeDef = new List<int>(); var bladeFrame = new List<Matrix4x4>();
            for (int k = 0; k < nc; k++)
            {
                CrestDef d = crests[k];
                float t = RootRadius(d), sink = 0.35f * t + CrestSinkForTest * d.height;
                float splay = d.splayDeg * Mathf.Deg2Rad;
                // in the blade's frame: upright, its root sunk below the frame's origin (the beak's top)
                g.spine[k] = CrestLine(d, new Vector3(0f, -sink, 0f), 0.35f * t, d.height / Mathf.Cos(splay));
                float crestCell = Mathf.Min(cell, Mathf.Min(0.3f * d.thickness, kCrestCellOfRoot * t));
                g.axis[k] = g.spine[k];
                g.crest[k] = BlobCrestsForTest ? BlobCrestShapes(d, g.spine[k], Vector3.zero) : CrestShapes(d, g.spine[k], Vector3.zero, crestCell, out g.axis[k]);
                g.crestSculpt[k] = new Sculpt { id = "crest", shapes = g.crest[k], cell = crestCell };
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
            g.boundsCentre = 0.5f * (lo + hi);
            g.boundsRadius = 0.5f * (hi - lo).magnitude + Mathf.Sin(gapeDeg * Mathf.Deg2Rad) * (zt - g.pivot.z + Mathf.Abs(g.hinge.y - g.pivot.y)) + (fangPairs > 0 ? fangLength : 0f) + (teeth > 0 ? toothLength : 0f);
            return g;
        }

        // Teeth along the lip line: `teeth` per row and side - the upper row on the skull's margin pointing down, the lower
        // row between its teeth on the jaw's margin pointing up - both leaning back toward the throat by toothRakeDeg and
        // splayed a little outward, rooted at the lip's outer surface so their tips stand just outside the opposite lip (a
        // toothy grin, even at rest); shorter toward the gape's corner. Then the fangs at the jaw's tip, pointing forward
        // and up, hooking down. Each is sunk kSink of its length into the gum. Head frame, the jaw closed.
        const float kToothSink = 0.35f, kUpperSplayDeg = 20f, kLowerSplayDeg = 26f, kToothCurveDeg = 20f, kFangSplayDeg = 15f, kFangCurveDeg = 25f;

        ToothDef[] Teeth(SculptField H, SculptField skullF, SculptField jawF, float zh, float zFront, float zJaw, Func<float, float> mouthY, float lip, float reach)
        {
            var list = new List<ToothDef>();
            float L = skullLength, BL = beakLength;
            float rake = toothRakeDeg * Mathf.Deg2Rad;
            if (teeth > 0)
            {
                float zA = zh + 0.1f * L;
                for (int row = 0; row < 2; row++)
                {
                    bool lower = row == 1;
                    float zB = lower ? zJaw - 0.1f * BL : zFront - 0.02f * BL;
                    if (!(zB > zA)) continue;
                    float s = (zB - zA) / teeth;
                    for (int i = 0; i < teeth; i++)
                    {
                        float z = zA + (i + (lower ? 0.75f : 0.25f)) * s, f = (z - zA) / (zB - zA);
                        float w = Mathf.Clamp01(f / 0.3f); w = w * w * (3f - 2f * w);
                        float len = toothLength * (0.55f + 0.45f * w), r = toothRadius * Mathf.Sqrt(len / toothLength);
                        float ym = mouthY(z), sg = lower ? 1f : -1f;   // sg: the way it points - the upper row down, the lower up
                        float splay = (lower ? kLowerSplayDeg : kUpperSplayDeg) * Mathf.Deg2Rad;   // the lower row out past the upper lip's overhang
                        Vector3 dir = new Vector3(Mathf.Sin(splay), sg * Mathf.Cos(splay) * Mathf.Cos(rake), -Mathf.Cos(splay) * Mathf.Sin(rake)).normalized;
                        // on the lip's outer surface at the mouth line (the closed head), its root moved in until it is in the
                        // gum (the skull's for the upper row, the jaw's for the lower: the lip's rounded edge is thinner)
                        if (!March(H, new Vector3(0f, ym, z), Vector3.right, reach, 0.0005f, out Vector3 s0)) continue;
                        Vector3 emerge = new Vector3(s0.x - 0.3f * r - TeethInsetForTest, ym + TeethLiftForTest, z);
                        SculptField gum = lower ? jawF : skullF;
                        for (int it = 0; it < 40 && gum.Eval(emerge - dir * (kToothSink * len)) > -0.25f * r; it++) emerge.x -= 0.0005f;
                        Add(list, emerge, dir, Vector3.forward, len, r, kToothCurveDeg, lower, false);
                    }
                }
            }
            for (int k = 0; k < fangPairs; k++)
            {
                float len = fangLength * (k == 0 ? 1f : 0.85f), r = 0.17f * len;
                float z = zJaw - (0.6f + 1.1f * k) * 0.035f * BL, ym = mouthY(z);
                if (!March(H, new Vector3(0f, ym - lip, z), Vector3.right, reach, 0.0005f, out Vector3 s0)) continue;
                float lift = fangLiftDeg * Mathf.Deg2Rad, sp = (kFangSplayDeg + 12f * k) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(sp) * Mathf.Cos(lift), Mathf.Sin(lift), Mathf.Cos(sp) * Mathf.Cos(lift)).normalized;
                Vector3 emerge = new Vector3((k == 0 ? 0.5f : 0.8f) * s0.x, ym - 0.5f * lip, z);
                Add(list, emerge, dir, Vector3.up, len, r, kFangCurveDeg, true, true);
            }
            return list.ToArray();
        }

        // One tooth from where it leaves the gum and its mirror image (x -> -x); up: the way its curl bends away from
        // (it curls toward -up, projected square to dir).
        static void Add(List<ToothDef> list, Vector3 emerge, Vector3 dir, Vector3 up, float len, float r, float curveDeg, bool lower, bool fang)
        {
            for (int side = 1; side >= -1; side -= 2)
            {
                var t = new ToothDef { dir = dir, emerge = emerge, length = len, radius = r, curveDeg = curveDeg, lower = lower, fang = fang };
                t.up = (up - dir * Vector3.Dot(up, dir)).normalized;
                if (side < 0) { t.dir.x = -t.dir.x; t.emerge.x = -t.emerge.x; t.up.x = -t.up.x; }
                t.basePt = t.emerge - t.dir * (kToothSink * len);
                // where Horn's curve ends: an arc of curveDeg in the plane of dir and up, bending toward -up
                float b = curveDeg * Mathf.Deg2Rad, kr = len / b;
                t.tip = t.basePt + t.up * -(kr - kr * Mathf.Cos(b)) + t.dir * (kr * Mathf.Sin(b));
                list.Add(t);
            }
        }

        // RED test hooks (CreatureCrestBeakHeadTests): the teeth moved this far in toward the midline / up (metres).
        internal static float TeethInsetForTest, TeethLiftForTest;
        internal static bool NoMouthKindForTest;   // RED: the mouth's cut faces drawn in the body skin
        internal static bool MouthNoBoxForTest;    // RED: every cut inside the closed head on the mouth kind (eye sockets, nostrils too)

        // RED test hooks (CreatureCrestBeakHeadTests): the old blade of six blended blobs; crests sunk by this x height.
        internal static bool BlobCrestsForTest;
        internal static bool BlobSharpTipForTest;   // ... tapering to the same point as the real blade (only its scallops differ)
        internal static float CrestSinkForTest;
        internal static float BladeScaleForTest;   // > 0: a mirrored pair's -x blade drawn this much larger (an unequal pair)

        const int kCrestSeg = 112, kCrestSteps = 224;

        // The tangent's angle from straight up toward +z (forward) at u (0 root .. 1 tip): beta at the root, curling back
        // by curl with the curvature growing toward the tip (u^1.5: rises, then breaks backward like a wave), and hooking
        // back by hook more over the last quarter (smootherstep 0.75 .. 1: the sickle's point).
        static float CrestAngle(float beta, float curl, float hook, float u)
        {
            float h = Mathf.Clamp01((u - 0.75f) / 0.25f);
            return beta - curl * u * Mathf.Sqrt(u) - hook * h * h * h * (h * (6f * h - 15f) + 10f);   // smootherstep: the bend grows in smoothly
        }

        static float CrestPeak(float beta, float curl, float hook, float len)
        {
            float y = 0f, peak = 0f, ds = len / kCrestSteps;
            for (int i = 0; i < kCrestSteps; i++) { y += Mathf.Cos(CrestAngle(beta, curl, hook, (i + 0.5f) / kCrestSteps)) * ds; peak = Mathf.Max(peak, y); }
            return peak;
        }

        // The centre line, kCrestSeg + 1 points from the root: the forward lean at the root is the least that brings the
        // highest point to rise (height / cos splay, along the blade's own up) above the beak (a straight blade leans forward; a curling one stands nearly upright).
        static Vector3[] CrestLine(CrestDef d, Vector3 root, float sink, float rise)
        {
            float curl = d.curlDeg * Mathf.Deg2Rad, hook = d.hookDeg * Mathf.Deg2Rad, len = d.length, want = rise + sink;
            float beta = 0f;
            if (CrestPeak(0f, curl, hook, len) > want)
            {
                float lo = 0f, hi = 0f;
                for (float b = 0f; b <= 85f; b += 0.5f) { hi = b * Mathf.Deg2Rad; if (CrestPeak(hi, curl, hook, len) <= want) break; lo = hi; }
                for (int i = 0; i < 30; i++) { float m = 0.5f * (lo + hi); if (CrestPeak(m, curl, hook, len) > want) lo = m; else hi = m; }
                beta = 0.5f * (lo + hi);
            }
            var pts = new Vector3[kCrestSeg + 1];
            pts[0] = root;
            Vector3 p = root; float ds = len / kCrestSteps;
            int per = kCrestSteps / kCrestSeg;
            for (int i = 0; i < kCrestSteps; i++)
            {
                float a = CrestAngle(beta, curl, hook, (i + 0.5f) / kCrestSteps);
                p += new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a)) * ds;
                if ((i + 1) % per == 0) pts[(i + 1) / per] = p;
            }
            return pts;
        }

        // The blade's half chord at its widest (CrestDef.chord, 0 = 0.35 x its length), and its flattening (chord /
        // thickness, one value for the whole blade so the cones' ends match at every join).
        static float RootChord(CrestDef d) => d.chord > 0f ? d.chord : 0.35f * d.length;
        static float Flatten(CrestDef d) => Mathf.Clamp(RootChord(d) / d.thickness, 1f, 4f);

        // The crest polygoniser's cell: at most this x the blade's root radius (the thin prong: a coarser cell folds it).
        const float kCrestCellOfRoot = 0.75f, kTipOfCell = 0.5f;

        // The blade's radius over its widest (thickness) at u (0 root .. 1 tip). flare 0: the plain linear taper to
        // 0.05. A flare - a sickle: from the prong at the root (1 / flare) a smootherstep rise to the widest at flareAt
        // (toward the end: the crescent), then a falling smootherstep down to a point over the rest - flat and
        // curvature-free at the widest from both sides (C2: no kink in the outline's bend), so the crescent's outer edge
        // sweeps into the point.
        internal static float Profile(CrestDef d, float u)
        {
            if (!(d.flare > 0f)) return Mathf.Lerp(1f, 0.05f, u);
            float at = d.flareAt > 0f ? d.flareAt : 0.4f, F = Mathf.Max(1f, d.flare), p;
            if (u <= at) { float x = u / at; p = (1f + (F - 1f) * x * x * x * (x * (6f * x - 15f) + 10f)) / F; }
            else { float x = Mathf.Clamp01((u - at) / (1f - at)); p = 1f - x * x * x * (x * (6f * x - 15f) + 10f); }
            return Mathf.Max(0.05f, p);
        }

        // The blade's half thickness at its root (the widest without a flare).
        static float RootRadius(CrestDef d) => d.thickness * Profile(d, 0f);

        // The blade: kCrestSeg flattened round cones along its centre line, their radii shared at the joins and following
        // Profile (a linear taper, or the flared sickle) to a point (0.05 x thickness) at the tip, one flattening - a
        // smooth, thin, sharp blade; and a flared foot sunk into the beak. 56 cones: with the 1.5 x blade's wide chord, 28
        // left a dip at every join on the curl's outside (the flattened end caps are only `thickness` long along the axis).
        static SculptShape[] CrestShapes(CrestDef d, Vector3[] line, Vector3 beakTop, float crestCell, out Vector3[] axis)
        {
            float t = d.thickness, sq = Flatten(d), tip = 0.05f * t;   // a point finer than the crest's cell
            var s = new List<SculptShape>();
            // the cones' axis: the centre line moved toward the outside of the curl (the front edge, side +1 of the
            // blade's plane) by `outside` x the half chord there - the crescent's width outside, a thin inner edge
            var r = new float[kCrestSeg + 1]; var c = new Vector3[kCrestSeg + 1];
            for (int i = 0; i <= kCrestSeg; i++)
            {
                float u = (float)i / kCrestSeg;
                float tp = t * Profile(d, u), fl = kTipOfCell * crestCell;
                r[i] = d.flare > 0f ? Mathf.Sqrt(tp * tp + fl * fl) : Mathf.Lerp(t, tip, u);   // a sickle's needle point: no finer than kTipOfCell cells (finer folds the mesher), eased in (a max() kinks the outline)
                Vector3 tg = (line[Mathf.Min(i + 1, kCrestSeg)] - line[Mathf.Max(i - 1, 0)]).normalized;
                // the shift is a slow arch, outside x the widest half chord x sin(pi u) - 0 at the root and the point, so the
                // crescent's outer edge sweeps into the point and its inner edge hugs the centre line there. (A shift that
                // follows the falling chord bends the axis tighter than the chord is long: the chords cross on the inside
                // of the curl and leave a step in the inner edge.)
                c[i] = line[i] + new Vector3(0f, -tg.z, tg.y) * (d.outside * sq * t * Mathf.Sin(Mathf.PI * u));
            }
            axis = c;
            for (int i = 0; i < kCrestSeg; i++)
                s.Add(Cone(c[i], c[i + 1], r[i], r[i + 1], sq, 0f));   // no blend: a smooth union swells every join by blend / 4 (a ripple)
            float rt = RootRadius(d), chord = sq * rt;
            s.Add(Ell(beakTop - new Vector3(0f, 0.6f * rt, 0f), new Vector3(1.3f * rt, 0.9f * rt, 1.15f * chord), SculptOp.Union, 0.9f * rt));
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
            int j = Mathf.Clamp(at.joint, 0, m.Joints - 1), jaw = sp.Layout.Start(c.Index), upper = jaw + 1;
            Vector3 o = m.Rest.Joints[j] + m.Rest.Rotations[j] * at.offset;
            Quaternion tilt = RestTilt(m.Rest, j);
            IPartRuntime[] rts = m.Rest.PartRuntimes;
            if (rts != null) foreach (IPartRuntime r in rts) if (r is CrestBeakHeadRuntime h && h.JawBone == jaw) tilt = h.Tilt;   // the bones' own
            Matrix4x4 B = Matrix4x4.TRS(o, m.Rest.Rotations[j] * tilt, Vector3.one);
            float uBase = sp.headCapLength + sp.spacing * j;   // the body's uv0.x at the head joint (metres from the front tip)
            Built g = Geo();
            if (g.dorsal == null) g.dorsal = new float[2 + g.bladeDef.Length][];
            int mouth = m.Slot("mouth");
            Emit(m, g, 0, SculptMesher.For(g.skullSculpt), B, Matrix4x4.identity, upper, PartMeshEmitter.PartBody, uBase, mouth, j, g.skullPivot.z, g.hinge.z);
            Matrix4x4 jawRest = JawPose(restGapeDeg);   // the bind: the jaw bone at the runtime's resting gape
            Emit(m, g, 1, SculptMesher.For(g.jawSculpt), B, jawRest, jaw, PartMeshEmitter.PartBody, uBase, mouth);
            if (g.bladeDef.Length > 0)
            {
                int kind = m.Slot("crest");
                for (int b = 0; b < g.bladeDef.Length; b++) Emit(m, g, 2 + b, SculptMesher.For(g.crestSculpt[g.bladeDef[b]]), B, g.bladeFrame[b], upper, kind, uBase, -1);
            }
            // teeth: horns on toothKind (default the kit's ivory keratin row), the upper ones on the upper head's bone,
            // the lower ones and the fangs riding the jaw
            if (g.teeth.Length > 0)
            {
                int kind = m.Slot(toothKind);
                List<Vector2> uv1 = m.Lists.uv1;
                for (int k = 0; k < g.teeth.Length; k++)
                {
                    ToothDef t = g.teeth[k];
                    Matrix4x4 M = t.lower ? B * jawRest : B;
                    Vector3 wb = M.MultiplyPoint3x4(t.basePt);
                    Quaternion q = Quaternion.LookRotation(M.MultiplyVector(t.dir), M.MultiplyVector(t.up));
                    int first = m.VertexCount;
                    m.Horn(wb, q, t.radius, 0f, t.length, t.curveDeg, t.lower ? jaw : upper, kind, 6, uBase - t.basePt.z, out _, out _);
                    for (int i = first; i < m.VertexCount; i++) uv1[i] = new Vector2(kind, -1f);
                }
            }
            if (g.webTop != null) EmitWebs(m, g, B, jawRest, upper, jaw, mouth, uBase);
        }

        // The cheek webs, both sides: kWebRows + 1 rows from the upper edge (the upper head's bone) to the lower (the jaw's,
        // drawn at the rest gape), each row skinned between the two by its share - the skin stretches as the jaw drops.
        // Two faces each: the outside in the body skin (pale, the throat's colour), the inside in the mouth's kind.
        const int kWebRows = 4;

        static void EmitWebs(PartMeshEmitter m, Built g, Matrix4x4 B, Matrix4x4 jawRest, int upper, int jaw, int mouth, float uBase)
        {
            int cols = g.webTop.Length, w = kWebRows + 1;
            var pts = new Vector3[cols * w];
            for (int side = 1; side >= -1; side -= 2)
            {
                for (int c = 0; c < cols; c++)
                {
                    Vector3 top = g.webTop[c], bot = jawRest.MultiplyPoint3x4(g.webBot[c]);
                    top.x *= side; Vector3 bc = g.webBot[c]; bc.x *= side; bot = jawRest.MultiplyPoint3x4(bc);
                    for (int r = 0; r < w; r++) pts[c * w + r] = Vector3.Lerp(top, bot, (float)r / kWebRows);
                }
                for (int face = 0; face < 2; face++)   // 0 outside (facing side * x), 1 inside
                {
                    float want = face == 0 ? side : -side;
                    int first = m.VertexCount;
                    for (int c = 0; c < cols; c++)
                        for (int r = 0; r < w; r++)
                        {
                            Vector3 p = pts[c * w + r];
                            Vector3 dz = pts[Mathf.Min(c + 1, cols - 1) * w + r] - pts[Mathf.Max(c - 1, 0) * w + r];
                            Vector3 dr = pts[c * w + Mathf.Min(r + 1, kWebRows)] - pts[c * w + Mathf.Max(r - 1, 0)];
                            Vector3 n = Vector3.Cross(dz, dr);
                            if (n.x * want < 0f) n = -n;
                            float share = (float)r / kWebRows;
                            m.Vertex(B.MultiplyPoint3x4(p), PartMeshEmitter.Unit(B.MultiplyVector(n), B.MultiplyVector(new Vector3(want, 0f, 0f))), uBase - p.z, share,
                                     face == 0 ? PartMeshEmitter.PartBody : mouth, face == 0 ? -0.6f : -1f, PartMeshEmitter.Blend(upper, jaw, share));
                        }
                    for (int c = 0; c + 1 < cols; c++)
                        for (int r = 0; r < kWebRows; r++)
                        {
                            int i00 = first + c * w + r, i01 = i00 + 1, i10 = i00 + w, i11 = i10 + 1;
                            Vector3 a = pts[c * w + r], b2 = pts[c * w + r + 1], d2 = pts[(c + 1) * w + r];
                            bool flip = Vector3.Cross(b2 - a, d2 - a).x * want < 0f;   // wind toward the face's own side
                            m.Tri(i00, i01, i10, flip);
                            m.Tri(i01, i11, i10, flip);
                        }
                }
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
        // mouth >= 0: the skull's and the jaw's vertices on a cut face inside the closed head (the roof, the floor, the
        // throat's faces) go on that kind - the inside of the mouth.
        // ramp >= 0: vertices behind ramp1 blend from that bone (the head joint's, at the neck seam ramp0) to `bone`
        // (the upper head's) - the skull's back stays with the neck when the upper head tips up on a call.
        static void Emit(PartMeshEmitter m, Built g, int which, SculptPiece piece, Matrix4x4 B, Matrix4x4 local, int bone, int part, float uBase, int mouth,
                         int ramp = -1, float ramp0 = 0f, float ramp1 = 0f)
        {
            float[] dorsal = g.dorsal[which];
            if (dorsal == null || dorsal.Length != piece.Pos.Length) g.dorsal[which] = dorsal = Dorsal(g, which, piece);
            bool[] inside = null;
            if (mouth >= 0 && which < 2 && !NoMouthKindForTest)
            {
                if (g.inside == null) g.inside = new bool[2][];
                inside = g.inside[which];
                if (inside == null || inside.Length != piece.Pos.Length)
                {
                    g.inside[which] = inside = new bool[piece.Pos.Length];
                    float tol = Mathf.Max(0.002f, 0.4f * g.skullSculpt.cell);
                    // inside the closed head and on or in the jaw region (the cut faces, the throat; not an eye socket or a nostril)
                    for (int i = 0; i < inside.Length; i++) inside[i] = g.headField.Eval(piece.Pos[i]) < -tol && (MouthNoBoxForTest || g.boxField.Eval(piece.Pos[i]) < 2f * tol);
                }
            }
            Vector4 skin = PartMeshEmitter.Rigid(bone);
            Vector3 up = PartMeshEmitter.Unit(B.GetColumn(1), Vector3.up);
            int first = m.VertexCount;
            for (int i = 0; i < piece.Pos.Length; i++)
            {
                Vector3 p = local.MultiplyPoint3x4(piece.Pos[i]);
                bool inMouth = inside != null && inside[i];
                Vector4 sk = skin;
                if (ramp >= 0 && p.z < ramp1)
                {
                    float w = Mathf.Clamp01((p.z - ramp0) / Mathf.Max(1e-4f, ramp1 - ramp0)); w = w * w * (3f - 2f * w);
                    sk = PartMeshEmitter.Blend(ramp, bone, w);
                }
                m.Vertex(B.MultiplyPoint3x4(p), PartMeshEmitter.Unit(B.MultiplyVector(local.MultiplyVector(piece.Nrm[i])), up), uBase - p.z, piece.V[i], inMouth ? mouth : part, inMouth ? -1f : dorsal[i], sk);
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
        readonly int jaw, upper, head;
        readonly Vector3 hinge, skullPivot, eye, boundsCentre;   // in the head joint's frame (the skull's, tilted)
        readonly float boundsRadius;
        float gape, wait, elapsed;
        bool calling;
        uint rng;

        public float Gape => GapeOverride >= 0f ? GapeOverride : gape;   // degrees, 0 = closed; restGapeDeg between calls
        public bool Calling => calling;
        public int JawBone => jaw;
        // The upper head's bone (skull, beak, crests, upper teeth): the head joint's, tipped up about the skull's back on
        // a call by callPitchDeg x the call's share of the gape.
        public int UpperBone => upper;
        public float UpperPitch => p.gapeDeg > p.restGapeDeg ? p.callPitchDeg * Mathf.Clamp01((Gape - p.restGapeDeg) / (p.gapeDeg - p.restGapeDeg)) : 0f;
        // The skull's frame in the head joint's (CrestBeakHead.RestTilt, fixed when the individual is made).
        public readonly Quaternion Tilt;
        // Tests and tools: hold the jaw at this gape (degrees); < 0 = its own calls.
        public float GapeOverride = -1f;
        // RED test hook (CreatureCrestBeakHeadTests.Red_Jaw_IntoTheSkull): the jaw turns the wrong way.
        internal static bool FlipGapeForTest;

        public CrestBeakHeadRuntime(CrestBeakHead part, CreatureSim sim, int jawBone, int headJoint)
        {
            p = part; this.sim = sim; jaw = jawBone; upper = jawBone + 1; head = headJoint;
            Tilt = part.RestTilt(sim, headJoint);
            hinge = Tilt * part.Pivot; skullPivot = Tilt * part.SkullPivot;
            Vector3 e = part.EyeSurface;
            e.x -= 0.35f * Mathf.Max(0f, sim.Species.eyeRadius);   // the eyeball sits a little into its socket
            eye = Tilt * e;
            boundsCentre = Tilt * part.BoundsCentre; boundsRadius = part.BoundsRadius;
            rng = (uint)(sim.Seed * 747796405u + 2891336453u + (uint)jawBone * 2654435761u);
            if (rng == 0) rng = 1;
            wait = NextWait();
            gape = part.restGapeDeg;   // the bind pose's gape too (the rest sim is unticked): the mesh is drawn at it
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
                if (u >= 1f - 1e-4f) { calling = false; gape = p.restGapeDeg; wait = NextWait(); }
                else gape = p.restGapeDeg + (p.gapeDeg - p.restGapeDeg) * Envelope(u);
            }
            else
            {
                wait -= ft;
                if (wait <= 1e-4f) { calling = true; elapsed = Mathf.Max(0f, -wait); gape = p.restGapeDeg + (p.gapeDeg - p.restGapeDeg) * Envelope(elapsed / p.callSeconds); }
            }
        }

        public void WriteBones(Matrix4x4[] dst, int o)
        {
            float s = sim.SizeScale;
            Quaternion r = sim.Rotations[head];
            Matrix4x4 H0 = Matrix4x4.TRS(sim.Joints[head] + r * (p.at.offset * s), r, new Vector3(s, s, s));
            Quaternion up = Quaternion.AngleAxis(-UpperPitch, Vector3.right);   // nose up, about the skull's back
            Matrix4x4 H = H0 * Matrix4x4.TRS(skullPivot - up * skullPivot, up, Vector3.one);
            dst[o + upper] = H;
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
