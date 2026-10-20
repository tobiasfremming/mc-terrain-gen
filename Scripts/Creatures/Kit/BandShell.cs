// Creature kit (2026-10-20, the Delver): BandShell - a carapace of overlapping transverse bands over the back, one band
// per spine joint from at.joint to toJoint, each rigid to its joint (ShellBand: an arched shell section standing off the
// body by `lift` at the top and `sideLift` at the flanks, its equator `edgeDrop` below the joint, reaching `skirtDeg`
// under it, the lower edge flaring out). The bands shingle front over back - each band's rear edge stands proud over the
// next band's tucked front edge (`shingle`, `overlap`) - so the shell bends with the spine and never opens a gap or
// clips; the rear edges roll into a lip and may be scalloped into rounded lobes (pangolin scales). A `hump` raises the
// shell over the middle of its run (a hunched digger). The hood: `hoodBands` more bands ahead of the first, rigid to it,
// each further forward, wider and curled down (`hoodCurlDeg`) - a shield over the head (trilobite, armadillo).
// Material layer `layer` (10, armour plate: Chitin; restyle with Layers). Mesh only (no bones, no runtime): every band
// rides its joint. Kit-only; off unless a species names it.
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("BandShell", PartKind.Rigid, Bones = "0 (each band rigid to its joint)", Summary = "A carapace of overlapping transverse bands over the back, one per joint, each rigid to its joint: arched sections standing off the body, shingled front over back (rear edge proud, front edge tucked) so it bends without gaps or clipping, rolled scalloped rear lips, a hump over its middle, and an optional hood - bands ahead of the first, curling down over the head like a shield.",
          System = "mesh only (BandShell.EmitMesh, ShellBand)", MaterialLayers = new[] { 10 }, KitOnly = true,
          UseWhen = "armadillos, pangolins, glyptodonts, trilobites, woodlice, armoured diggers: any banded shell over the back, with a head shield")]
    public sealed class BandShell : CreaturePart, IShellBands
    {
        [Param(ParamUnit.Joint, "last joint with a band (the first is at.joint)")] public int toJoint;
        [Param(ParamUnit.Metres, "the shell's stand-off above the body at the top", Example = "delver 0.06")] public float lift = 0.04f;
        [Param(ParamUnit.Metres, "stand-off at the flanks")] public float sideLift = 0.03f;
        [Param(ParamUnit.Metres, "extra height over the middle of the run (a hunched back)")] public float hump;
        [Param(ParamUnit.Fraction, "where along the run the hump peaks (0 first band .. 1 last)", Min = 0, Max = 1, Default = "0.45")] public float humpAt = 0.45f;
        [Param(ParamUnit.Fraction, "the hump's half-width, fraction of the run", Min = 0.1f, Max = 2, Default = "0.6")] public float humpWidth = 0.6f;
        [Param(ParamUnit.Metres, "the section's equator below the joint (the shell reaches down the flanks)")] public float edgeDrop = 0.03f;
        [Param(ParamUnit.Degrees, "how far the lower edge curls under past the equator", Min = -30, Max = 60, Default = "8")] public float skirtDeg = 8f;
        [Param(ParamUnit.Metres, "the lower edges flare out by this")] public float flare = 0.01f;
        [Param(ParamUnit.Metres, "shell thickness", Example = "delver 0.008")] public float thickness = 0.008f;
        [Param(ParamUnit.Metres, "shingle step: the rear edge stands this proud, the front edge tucks this far in (> thickness / 2 + the bend's sweep)")] public float shingle = 0.008f;
        [Param(ParamUnit.Fraction, "overlap of neighbouring bands, fraction of the joint spacing", Min = 0, Max = 1, Default = "0.45")] public float overlap = 0.45f;
        [Param(ParamUnit.Count, "rounded lobes along each rear edge (0 = straight)", Min = 0, Max = 40)] public int lobes;
        [Param(ParamUnit.Fraction, "how far the rear edge draws back between lobes, fraction of the joint spacing", Min = 0, Max = 0.6f)] public float lobeDepth = 0.15f;
        [Param(ParamUnit.Metres, "rolled lip along the rear edge")] public float lip = 0.004f;
        [Param(ParamUnit.Fraction, "section squareness: 2 = round, up to 4 = boxy shoulders", Min = 2, Max = 4, Default = "2.2")] public float square = 2.2f;
        [Param(ParamUnit.Count, "hood bands ahead of the first band (0 = none)", Min = 0, Max = 8)] public int hoodBands;
        [Param(ParamUnit.Fraction, "each hood band's step forward, fraction of the joint spacing", Min = 0.2f, Max = 1.5f, Default = "0.75")] public float hoodStep = 0.75f;
        [Param(ParamUnit.Degrees, "the front hood band's forward lean about its equator (its top overhangs the head; each band leans its share more)", Min = 0, Max = 120, Default = "45")] public float hoodCurlDeg = 45f;
        [Param(ParamUnit.Metres, "each hood band grows by this (wider and taller: a flaring shield)")] public float hoodFlare = 0.01f;
        [Param(ParamUnit.Metres, "each hood band's lower edge drops by this")] public float hoodDrop = 0.01f;
        [Param(ParamUnit.Count, "mesh: vertices across each band, 0 = 40", Min = 0, Max = 96)] public int around;
        [Param(ParamUnit.Count, "material layer: 10 armour plate (restyle with Layers)", Min = 0, Max = 15, Default = "10")] public int layer = 10;

        public override int BoneCount(in SpeciesCtx c) => 0;
        public int BackBands => toJoint - at.joint + 1;
        public int Bands => BackBands + hoodBands;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a BandShell needs a SpineBody";
            if (at.joint < 1 || toJoint >= c.Joints - 1 || toJoint < at.joint) return $"{id}: joints {at.joint}..{toJoint} must lie in 1..{c.Joints - 2}, first <= last";
            if (lift < 0f || sideLift < 0f || hump < 0f || edgeDrop < 0f || flare < 0f || lip < 0f || hoodFlare < 0f || hoodDrop < 0f) return $"{id}: lift, sideLift, hump, edgeDrop, flare, lip, hoodFlare, hoodDrop >= 0";
            if (!(thickness > 0f)) return $"{id}: thickness must be > 0";
            if (!(shingle > 0.5f * thickness)) return $"{id}: shingle must exceed half the thickness (the rear edge clears the next band)";
            if (overlap < 0f || overlap > 1f || lobeDepth < 0f || lobeDepth > 0.6f || lobes < 0) return $"{id}: overlap 0..1, lobeDepth 0..0.6, lobes >= 0";
            if (square < 2f || square > 4f) return $"{id}: square must be in 2..4";
            if (hoodBands < 0 || hoodBands > 8 || hoodStep < 0.2f || hoodStep > 1.5f || hoodCurlDeg < 0f || hoodCurlDeg > 120f) return $"{id}: hoodBands 0..8, hoodStep 0.2..1.5, hoodCurlDeg 0..120";
            if (humpAt < 0f || humpAt > 1f || humpWidth < 0.1f) return $"{id}: humpAt 0..1, humpWidth >= 0.1";
            if (skirtDeg < -30f || skirtDeg > 60f) return $"{id}: skirtDeg must be in -30..60";
            if (layer < 0 || layer >= PartLayers.MaxParts || (layer >= 4 && layer <= 7)) return $"{id}: layer must be a spine-shader part id (0..3, 8..15)";
            return null;
        }

        float Hump(float f)
        {
            float x = (f - humpAt) / Mathf.Max(0.05f, humpWidth);
            return hump * Mathf.Exp(-2.2f * x * x);
        }

        // Outer half-height of the section at joint j (relative to the joint), for the profile through the run.
        float Height(CreatureSpecies sp, int j)
        {
            int n = BackBands;
            float f = n <= 1 ? 0.5f : (float)(Mathf.Clamp(j, at.joint, toJoint) - at.joint) / (n - 1);
            return sp.joints[Mathf.Clamp(j, 0, sp.JointCount - 1)].halfHeight + lift + Hump(f) + edgeDrop;
        }

        float Width(CreatureSpecies sp, int j) => sp.joints[Mathf.Clamp(j, 0, sp.JointCount - 1)].halfWidth + sideLift;

        // The profile between joints (u in joint units): Catmull-Rom through Width / Height, clamped to the run's ends.
        float ProfileW(CreatureSpecies sp, float u) => Profile(u, j => Width(sp, j));
        float ProfileH(CreatureSpecies sp, float u) => Profile(u, j => Height(sp, j));
        float Profile(float u, Func<int, float> f)
        {
            int i = Mathf.FloorToInt(u);
            float t = u - i;
            int C(int j) => Mathf.Clamp(j, at.joint, toJoint);
            return Mathf.Max(1e-4f, PartMeshEmitter.CR(f(C(i - 1)), f(C(i)), f(C(i + 1)), f(C(i + 2)), t));
        }

        // Band k (0 .. BackBands-1 the back from the front, then the hood bands nearest first), its joint (the bone it is
        // rigid to) and its frame in that joint's frame (identity for back bands; hood bands sit forward and curl down).
        // spacing: the rest spine's joint spacing at size 1.
        public ShellBand Band(CreatureSpecies sp, int k, float spacing, out int joint, out Matrix4x4 local)
        {
            float L = spacing;
            local = Matrix4x4.identity;
            var b = new ShellBand
            {
                lo = -skirtDeg * Mathf.Deg2Rad, hi = Mathf.PI + skirtDeg * Mathf.Deg2Rad,
                yc = -edgeDrop, thick = thickness, shingle = shingle, flare = flare, lip = lip, lobes = lobes,
                lobeDepth = lobeDepth * L, square = square,
                zFront = 0.5f * L * (1f + overlap), zRear = -0.5f * L * (1f + overlap),
            };
            if (k < BackBands)
            {
                joint = at.joint + k;
                b.A = Width(sp, joint); b.B = Height(sp, joint);
                // the band follows the shell's continuous profile (Catmull-Rom through the joints) at its own edges, so
                // two neighbours meet the same surface where they overlap and differ only by the shingle step
                float zf = b.zFront / L, zr = b.zRear / L;   // in joint steps (+ toward the head)
                b.aFront = ProfileW(sp, joint - zf); b.aRear = ProfileW(sp, joint - zr);
                b.bFront = ProfileH(sp, joint - zf); b.bRear = ProfileH(sp, joint - zr);
                return b;
            }
            int h = k - BackBands + 1;   // 1 = the hood band just ahead of the first
            joint = at.joint;
            float g = (float)h / Mathf.Max(1, hoodBands);
            b.A = Width(sp, joint) + hoodFlare * h;
            b.B = Height(sp, joint) + hoodFlare * h * 0.6f + hoodDrop * h;
            b.yc = -edgeDrop - hoodDrop * h;
            // forward by h steps, then tilted forward about its equator line (the flanks stay, the top leans out over
            // the head): a cowl whose top overhangs the neck, each band a little further over than the one behind it
            float z = h * hoodStep * L;
            float curl = hoodCurlDeg * g;
            // each band leans its share more than the one behind it: lengthen it forward by what that opens at its top
            b.zFront += b.B * Mathf.Tan(hoodCurlDeg / Mathf.Max(1, hoodBands) * Mathf.Deg2Rad);
            Vector3 pivot = new Vector3(0f, b.yc, z);
            local = Matrix4x4.TRS(pivot, Quaternion.AngleAxis(curl, Vector3.right), Vector3.one) * Matrix4x4.TRS(new Vector3(0f, 0f, z) - pivot, Quaternion.identity, Vector3.one);
            return b;
        }

        public int BandCount => Bands;
        public ShellBand BandAt(CreatureSpecies sp, CreatureSim rest, int k, out int joint, out Matrix4x4 local)
            => Band(sp, k, Spacing(rest, at.joint), out joint, out local);

        public static float Spacing(CreatureSim rest, int j)
        {
            Vector3[] p = rest.Joints;
            int a = Mathf.Clamp(j, 0, p.Length - 2);
            return (p[a + 1] - p[a]).magnitude;
        }

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            CreatureSpecies sp = m.Species;
            int na = around > 0 ? around : 40;
            float L = Spacing(m.Rest, at.joint);
            float arc = 0f;
            // every band in order: the back from the front, then the hood
            for (int k = 0; k < Bands; k++)
            {
                ShellBand b = Band(sp, k, L, out int j, out Matrix4x4 local);
                Matrix4x4 frame = Matrix4x4.TRS(m.Rest.Joints[j], m.Rest.Rotations[j], Vector3.one) * local;
                b.Emit(m, frame, PartMeshEmitter.Rigid(j), layer, na, 7, arc);
                arc += L;
            }
        }
    }
}
