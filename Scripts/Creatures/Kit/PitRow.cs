// Creature kit (Apex redesign §1 breathing holes, §5.7): PitRow - a row of recessed pits with soft pale rims on a
// FlowBody's surface: spiracles along a throat, nostrils, sensory pits. `count` pits from body fraction fromU to toU
// (the same u as FlowBody.Section) at section angle angleDeg (FlowBody's phi: -90 belly .. 0 flank .. +90 back; the
// left side, from `mirror` or at.side -1, takes the mirror image: negative x). Each pit is
//   cup   a bowl `depth` x radius deep (kind "pit": near-black by default, a species restyles it with a Layers row)
//   rim   a raised half-round lip, `rim` x radius high and wide, in the body skin at its palest (dorsal -1: the belly
//         colour), wrapping the cup, its outer foot on the surface
// both standing on the surface point the drawn section gives (FlowBody.SurfaceAt, along its normal), and both skinned
// the way the body's rings are at that place (FlowBody.SkinAt), so the pit travels with the skin. Mesh only, no bones.
// Author at scale 1 (metres).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("PitRow", PartKind.Rigid, Bones = "0 (skinned like the body's rings)", Summary = "A row of recessed pits with soft pale raised rims on a FlowBody's surface (count from fromU to toU at section angle angleDeg; mirror for both sides): spiracles, nostrils, sensory pits. Dark cup (kind pit), belly-coloured rim, skinned with the body's rings.",
          System = "mesh only (PitRow.EmitMesh), skinned like the FlowBody rings under it", MaterialLayers = new[] { 0 }, MaterialKinds = new[] { "pit" }, KitOnly = true,
          UseWhen = "breathing holes along a throat or flank, nostrils, sensory pits on a FlowBody")]
    public sealed class PitRow : CreaturePart
    {
        [Param(ParamUnit.Fraction, "body fraction of the first pit (FlowBody.Section's u: 0 front tip .. 1 tail tip)", Min = 0, Max = 1, Example = "0.12")] public float fromU = 0.12f;
        [Param(ParamUnit.Fraction, "body fraction of the last pit", Min = 0, Max = 1, Example = "0.3")] public float toU = 0.3f;
        [Param(ParamUnit.Degrees, "place around the section, FlowBody's phi: -90 belly .. 0 flank .. +90 back (the right side; the left is mirrored)", Min = -90, Max = 90, Default = "-35")] public float angleDeg = -35f;
        [Param(ParamUnit.Count, "pits in the row (per side)", Min = 1, Max = 32, Default = "6")] public int count = 6;
        [Param(ParamUnit.Metres, "cup radius", Min = 0.001f, Max = 0.2f, Default = "0.012")] public float radius = 0.012f;
        [Param(ParamUnit.Fraction, "cup depth x radius", Min = 0, Max = 2, Default = "0.6")] public float depth = 0.6f;
        [Param(ParamUnit.Fraction, "rim height and width x radius (the pale raised lip around the cup)", Min = 0, Max = 1, Default = "0.35")] public float rim = 0.35f;

        // RED test hook (CreaturePitRowTests.Red_Pits_Float): the pits lifted this far off the surface, metres.
        internal static float SurfaceBiasForTest;
        // RED test hooks: every pit triangle reversed (Red_Pits_Reversed); the pits skinned rigid to joint 0 instead of
        // like the body rings (Red_Pits_FixedSkin); every other ring of a pit on the tail bone (Red_Pits_TornSkin: shears the pit).
        internal static bool ReverseWindingForTest, RigidSkinForTest, TornSkinForTest;

        public override int BoneCount(in SpeciesCtx c) => 0;
        public override int MeshOrder => 100;

        public const int Segs = 12;             // vertices around a pit (+1 for the seam)
        public const int CupRings = 3;          // rings from the cup's pole to its edge (after the pole ring)
        public const int RimRings = 7;          // rings over the lip, inner foot to outer foot
        public const int CupVertices = (1 + CupRings) * (Segs + 1);
        public const int RimVertices = RimRings * (Segs + 1);
        public const int VertsPerPit = CupVertices + RimVertices;

        public override string Validate(in SpeciesCtx c)
        {
            if (!mirror && at.side != -1 && at.side != 1) return $"{id}: side must be -1 or +1 (or mirror)";
            if (FindBody(c.Parts) == null) return $"{id}: PitRow needs a FlowBody to sit on";
            if (!(fromU >= 0f && fromU <= 1f) || !(toU >= 0f && toU <= 1f)) return $"{id}: fromU and toU must be 0..1";
            if (!(angleDeg >= -90f && angleDeg <= 90f)) return $"{id}: angleDeg must be -90..90";
            if (count < 1 || count > 32) return $"{id}: count must be 1..32";
            if (!(radius > 0f)) return $"{id}: radius must be > 0";
            if (depth < 0f || rim < 0f) return $"{id}: depth and rim must be >= 0";
            return null;
        }

        static FlowBody FindBody(IReadOnlyList<CreaturePart> parts)
        {
            if (parts != null) for (int i = 0; i < parts.Count; i++) if (parts[i] is FlowBody f) return f;
            return null;
        }

        // Pit k's body fraction.
        public float PitU(int k) => count <= 1 ? 0.5f * (fromU + toU) : Mathf.Lerp(fromU, toU, k / (count - 1f));

        // Pit k's section angle (radians) on this part's side: the right side is angleDeg, the left its mirror image.
        public float PitPhi() => at.side < 0 ? Mathf.PI - angleDeg * Mathf.Deg2Rad : angleDeg * Mathf.Deg2Rad;

        // Cup: a bowl, deepest on the axis, level with the surface at r = radius.
        float CupH(float r) => -depth * radius * (1f - r * r / (radius * radius));
        float CupDh(float r) => 2f * depth * r / radius;

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            FlowBody body = FindBody(KitView.Parts(m.Species));
            if (body == null) return;
            int cup = m.Slot("pit"), segs = Segs;
            float rr = rim * radius;
            for (int k = 0; k < count; k++)
            {
                float u = PitU(k);
                float phi = PitPhi();
                Vector3 p = body.SurfaceAt(m, u, phi, out Vector3 n, out Vector3 along);
                Vector4 skin = RigidSkinForTest ? PartMeshEmitter.Rigid(0) : body.SkinAt(u * body.BodyLength);
                // the pit's frame: a along the body (on the surface), b = n x a, so a x b = n
                Vector3 a = PartMeshEmitter.Unit(Vector3.ProjectOnPlane(along, n), Vector3.Cross(n, Vector3.up));
                Vector3 b = Vector3.Cross(n, a);
                // the surface's own tangents at the centre: a ring vertex at (x along a, y along b) is moved on the surface
                // itself (u, phi from the 2x2 metric), so a pit on a strongly curved neck still lies in the skin
                const float eu = 1e-3f, ep = 0.02f;
                Vector3 pu = (body.SurfaceAt(m, Mathf.Min(1f, u + eu), phi, out _, out _) - body.SurfaceAt(m, Mathf.Max(0f, u - eu), phi, out _, out _)) / (Mathf.Min(1f, u + eu) - Mathf.Max(0f, u - eu));
                Vector3 pp = (body.SurfaceAt(m, u, phi + ep, out _, out _) - body.SurfaceAt(m, u, phi - ep, out _, out _)) / (2f * ep);
                float g00 = Vector3.Dot(pu, pu), g01 = Vector3.Dot(pu, pp), g11 = Vector3.Dot(pp, pp);
                float det = Mathf.Max(1e-12f, g00 * g11 - g01 * g01);

                // cup: the pole, then rings out to the edge
                int first = m.VertexCount;
                for (int i = 0; i <= CupRings; i++)
                {
                    float r = radius * i / CupRings, h = CupH(r), dh = CupDh(r);
                    for (int j = 0; j <= segs; j++)
                    {
                        float th = 2f * Mathf.PI * (j == segs ? 0 : j) / segs;
                        Vector3 d = a * Mathf.Cos(th) + b * Mathf.Sin(th);
                        m.Vertex(OnSurface(body, m, u, phi, pu, pp, g00, g01, g11, det, d * r, h), PartMeshEmitter.Unit(n - d * dh, n), 0.5f, (float)j / segs, cup, 0f, Skin(skin, body, i));
                    }
                }
                Stitch(m, first, CupRings + 1, segs, true);

                // rim: a half-round lip from the cup's edge out
                int lip = m.VertexCount;
                for (int i = 0; i < RimRings; i++)
                {
                    float psi = Mathf.PI * i / (RimRings - 1);
                    float r = radius + rr * (1f - Mathf.Cos(psi)), h = rr * Mathf.Sin(psi);
                    float nr = -Mathf.Cos(psi), nh = Mathf.Sin(psi);   // the lip's outward normal in the (r, up) plane
                    for (int j = 0; j <= segs; j++)
                    {
                        float th = 2f * Mathf.PI * (j == segs ? 0 : j) / segs;
                        Vector3 d = a * Mathf.Cos(th) + b * Mathf.Sin(th);
                        m.Vertex(OnSurface(body, m, u, phi, pu, pp, g00, g01, g11, det, d * r, h), PartMeshEmitter.Unit(d * nr + n * nh, n), 0.5f, (float)j / segs, PartMeshEmitter.PartBody, -1f, Skin(skin, body, i));
                    }
                }
                Stitch(m, lip, RimRings, segs, false);
            }
        }

        static Vector4 Skin(Vector4 skin, FlowBody body, int ring) => TornSkinForTest && (ring & 1) == 1 ? PartMeshEmitter.Rigid(body.jointCount - 1) : skin;

        // The point `h` above the skin where the tangent displacement `disp` from the pit centre (u, phi) lands when it is
        // carried along the surface: (du, dphi) solve disp = du pu + dphi pp (least squares), then the drawn section there.
        static Vector3 OnSurface(FlowBody body, PartMeshEmitter m, float u, float phi, Vector3 pu, Vector3 pp, float g00, float g01, float g11, float det, Vector3 disp, float h)
        {
            float r0 = Vector3.Dot(disp, pu), r1 = Vector3.Dot(disp, pp);
            float du = (g11 * r0 - g01 * r1) / det, dphi = (g00 * r1 - g01 * r0) / det;
            Vector3 q = body.SurfaceAt(m, Mathf.Clamp01(u + du), phi + dphi, out Vector3 nq, out _);
            return q + nq * (h + SurfaceBiasForTest);
        }

        // Quads between consecutive rings, inner to outer (pole: the first ring is one point, so it has no quad).
        static void Stitch(PartMeshEmitter m, int first, int rings, int segs, bool pole)
        {
            int w = segs + 1;
            for (int i = 0; i + 1 < rings; i++)
                for (int j = 0; j < segs; j++)
                {
                    int i0 = first + i * w + j, i1 = i0 + 1, o0 = i0 + w, o1 = o0 + 1;
                    if (!(pole && i == 0)) m.Tri(i0, i1, o0, !ReverseWindingForTest);
                    m.Tri(i1, o1, o0, !ReverseWindingForTest);
                }
        }
    }
}
