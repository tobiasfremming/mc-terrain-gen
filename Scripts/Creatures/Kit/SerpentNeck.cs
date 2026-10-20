// Creature kit: SerpentNeck - the Apex's neck: a long, thick, muscular neck carried exactly as a LongNeck is (its bones,
// its runtime: LongNeckRuntime's posture, stabilised head, glances, calls, aim and strike), drawn its own way:
//   root      a heavy flare into the chest over the first rootFlareLength of the neck (the neck grows out of the torso,
//             not out of a socket);
//   section   deeper than wide, the throat drawn down toward a keel (throatKeel), a low dorsal ridge (dorsalRidge), a
//             muscle swell low on the flanks of the lower neck (muscle);
//   throat    transverse ridges across the ventral half (bands, bandDepth): the banded pale throat;
//   spiracles a row of pits along each side of the upper neck (spiracles, spiracleRadius, spiracleAt, spiracleDepth).
// The surface is built as rings between the neck's bones (as LongNeck's, Hermite-placed, two-bone skinned) and its
// normals come from the surface itself (central differences round and along), so every ridge and pit is shaded as
// drawn. Shaded as the body (part 0: the Skin's dorsal-to-belly shading gives the pale throat). Build-time only.
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("SerpentNeck", PartKind.Chain, Bones = "= segments (the last is the head's bone), as LongNeck",
          Summary = "A long thick muscular neck carried and aimed as a LongNeck (posture, stabilised head, glances, calls, strikes: LongNeckRuntime), drawn as its own: a heavy flare into the chest, a deep section with a keeled throat and a dorsal ridge, flank muscle, a banded throat and a row of spiracle pits along each side.",
          System = "LongNeckRuntime (as LongNeck) + SerpentNeck.EmitMesh",
          MaterialLayers = new[] { 0 }, KitOnly = true,
          UseWhen = "heavy serpentine necks on big flyers and monsters (the Apex): a neck that should read as muscle out of a massive chest, not a slender pterosaur's")]
    public sealed class SerpentNeck : LongNeck
    {
        [Param(ParamUnit.Fraction, "the root's flare into the chest: extra radius at the root, x rootRadius", Min = 0, Max = 2, Example = "apex 0.7")] public float rootFlare = 0.6f;
        [Param(ParamUnit.Fraction, "the flare's length, a fraction of the neck from the root", Min = 0.05f, Max = 0.6f)] public float rootFlareLength = 0.3f;
        [Param(ParamUnit.Fraction, "the throat drawn down toward a keel, x the half height", Min = 0, Max = 0.6f)] public float throatKeel = 0.2f;
        [Param(ParamUnit.Fraction, "the dorsal ridge, x the half height", Min = 0, Max = 0.4f)] public float dorsalRidge = 0.08f;
        [Param(ParamUnit.Fraction, "muscle low on the flanks of the lower neck, x the half width", Min = 0, Max = 0.5f)] public float muscle = 0.15f;
        [Param(ParamUnit.Count, "transverse ridges across the throat along the whole neck (0 = none)", Min = 0, Max = 60, Example = "apex 26")] public int bands = 24;
        [Param(ParamUnit.Fraction, "their depth, x the local radius", Min = 0, Max = 0.12f)] public float bandDepth = 0.035f;
        [Param(ParamUnit.Count, "spiracle pits per side (0 = none)", Min = 0, Max = 8, Example = "apex 4")] public int spiracles = 4;
        [Param(ParamUnit.Metres, "a pit's radius", Example = "apex 0.016")] public float spiracleRadius = 0.015f;
        [Param(ParamUnit.Fraction, "the row's middle along the neck (0 root .. 1 head)", Min = 0.1f, Max = 0.95f)] public float spiracleAt = 0.72f;
        [Param(ParamUnit.Fraction, "a pit's depth, x its radius", Min = 0, Max = 1)] public float spiracleDepth = 0.5f;

        public override int MeshOrder => 100;

        public override string Validate(in SpeciesCtx c)
        {
            string e = base.Validate(c);
            if (e != null) return e;
            if (rootFlare < 0f || rootFlare > 2f || rootFlareLength < 0.05f || rootFlareLength > 0.6f) return $"{id}: rootFlare 0..2, rootFlareLength 0.05..0.6";
            if (throatKeel < 0f || throatKeel > 0.6f || dorsalRidge < 0f || dorsalRidge > 0.4f || muscle < 0f || muscle > 0.5f) return $"{id}: throatKeel 0..0.6, dorsalRidge 0..0.4, muscle 0..0.5";
            if (bands < 0 || bands > 60 || bandDepth < 0f || bandDepth > 0.12f) return $"{id}: bands 0..60, bandDepth 0..0.12";
            if (spiracles < 0 || spiracles > 8 || spiracleRadius < 0f || spiracleAt < 0.1f || spiracleAt > 0.95f || spiracleDepth < 0f || spiracleDepth > 1f) return $"{id}: spiracles 0..8, spiracleRadius >= 0, spiracleAt 0.1..0.95, spiracleDepth 0..1";
            return null;
        }

        // half width along the neck, g = 0 root .. 1 head: a heavy flare into the chest
        internal float Width(float g)
        {
            float w = Mathf.Lerp(rootRadius, headRadius, Mathf.Pow(Mathf.Clamp01(g), 0.8f));
            float f = Mathf.Clamp01(1f - g / rootFlareLength);
            return w + rootRadius * rootFlare * f * f * (3f - 2f * f);
        }

        // The section's point (local x across, y up) at ring angle phi (-pi/2 the throat, +pi/2 the back), neck fraction g,
        // arc length s (m) and total length L (m): the ellipse shaped by the keel, ridge, muscle, bands and pits.
        internal Vector2 Section(float phi, float g, float s, float L)
        {
            float a = Width(g), b = a * depth;
            float cs = Mathf.Cos(phi), sn = Mathf.Sin(phi);
            float x = a * cs, y = b * sn;
            float down = Mathf.Max(0f, -sn), up = Mathf.Max(0f, sn);
            y -= b * throatKeel * down * down * down;
            y += b * dorsalRidge * Mathf.Pow(up, 14f);
            float low = 1f - Mathf.Clamp01(g / 0.6f);
            x *= 1f + muscle * low * Mathf.Exp(-(sn + 0.25f) * (sn + 0.25f) / 0.15f);
            float k = 1f;
            if (bands > 0 && bandDepth > 0f)
            {
                float ph = bands * s / Mathf.Max(1e-5f, L);
                float ridge = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * ph);
                float wv = Mathf.Clamp01((-sn - 0.15f) / 0.5f);
                float ends = Mathf.Clamp01(g / 0.08f) * Mathf.Clamp01((1f - g) / 0.06f);
                k -= bandDepth * ridge * wv * wv * ends;
            }
            if (spiracles > 0 && spiracleRadius > 0f && spiracleDepth > 0f)
            {
                float pitch = 2.6f * spiracleRadius;
                float s0 = spiracleAt * L - 0.5f * pitch * (spiracles - 1);
                float side = Mathf.Abs(Mathf.Atan2(sn, Mathf.Abs(cs)));   // 0 at the side
                float rr = Mathf.Max(1e-4f, Mathf.Sqrt(a * b));
                for (int i = 0; i < spiracles; i++)
                {
                    float ds = s - (s0 + pitch * i);
                    // pits sit a little below the side's middle
                    float dv = (sn + 0.2f) * rr;
                    float d2 = (ds * ds + dv * dv) / (spiracleRadius * spiracleRadius);
                    if (d2 < 9f) k -= spiracleDepth * spiracleRadius / rr * Mathf.Exp(-d2) * (side < 1.2f ? 1f : 0f);
                }
            }
            return new Vector2(x * k, y * k);
        }

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            int b0 = m.Species.Layout.Start(c.Index), K = segments;
            var P = new Vector3[K]; var Y = new Vector3[K]; var Z = new Vector3[K];
            for (int k = 0; k < K; k++)
            {
                Matrix4x4 b = m.Bones[b0 + k];
                P[k] = b.GetColumn(3);
                Y[k] = PartMeshEmitter.Unit(b.GetColumn(1), Vector3.up);
                Z[k] = PartMeshEmitter.Unit(b.GetColumn(2), Vector3.forward);
            }
            float total = 0f;
            for (int k = 1; k < K; k++) total += (P[k] - P[k - 1]).magnitude;
            int sides = ringSides, rps = ringsPerSegment, R = (K - 1) * rps + 1;
            // the rings' centres, frames, arcs and skins
            var C = new Vector3[R]; var RX = new Vector3[R]; var RY = new Vector3[R]; var RT = new Vector3[R];
            var arcAt = new float[R]; var skin = new Vector4[R];
            float arc = 0f; int r = 0;
            for (int k = 0; k < K - 1; k++)
            {
                Vector3 a0 = P[k], a1 = P[k + 1];
                float seg = (a1 - a0).magnitude;
                Vector3 m0 = Z[k] * seg, m1 = Z[k + 1] * seg;
                for (int i = (k == 0 ? 0 : 1); i <= rps; i++)
                {
                    float f = (float)i / rps;
                    Vector3 t = PartMeshEmitter.Unit(HermD(a0, m0, a1, m1, f), Z[k]);
                    Vector3 y = Vector3.Slerp(Y[k], Y[k + 1], f); y = PartMeshEmitter.Unit(y - t * Vector3.Dot(y, t), Y[k]);
                    C[r] = Herm(a0, m0, a1, m1, f); RT[r] = t; RY[r] = y; RX[r] = Vector3.Cross(y, t);
                    arcAt[r] = arc + f * seg;
                    float wf = f * f * (3f - 2f * f);
                    skin[r] = PartMeshEmitter.Blend(b0 + k, b0 + k + 1, wf);
                    r++;
                }
                arc += seg;
            }
            // the surface points (seam duplicated at k = sides)
            var S = new Vector3[R, sides + 1];
            for (r = 0; r < R; r++)
            {
                float g = total > 0f ? arcAt[r] / total : 0f;
                for (int k = 0; k <= sides; k++)
                {
                    float phi = -0.5f * Mathf.PI + 2f * Mathf.PI * (k % sides) / sides;
                    Vector2 q = Section(phi, g, arcAt[r], total);
                    S[r, k] = C[r] + RX[r] * q.x + RY[r] * q.y;
                }
            }
            // the root: a rounded cap buried in the chest, rigid to the first bone (a ball socket)
            Vector4 rigid0 = PartMeshEmitter.Rigid(b0);
            float rr0 = Width(0f);
            int prev = m.Pole(P[0] - Z[0] * rr0, -Z[0], 0f, PartMeshEmitter.PartBody, 0f, rigid0, sides);
            bool prevPole = true;
            for (int k = 1; k <= 2; k++)
            {
                float a = 30f * k * Mathf.Deg2Rad;
                int ring = m.Ring(P[0] - Z[0] * (rr0 * Mathf.Cos(a)), RX[0], RY[0], Z[0], rr0 * Mathf.Sin(a), rr0 * Mathf.Sin(a) * depth, 0f,
                                  Mathf.Cos(a) / Mathf.Sin(a), Mathf.Cos(a) / Mathf.Sin(a) * depth, 0f, rr0 * (1f - Mathf.Cos(a)), PartMeshEmitter.PartBody, rigid0, sides, true);
                m.Stitch(prev, ring, sides, prevPole, false, false);
                prev = ring; prevPole = false;
            }
            // the rings, normals from the surface (round x along), pointing outward
            for (r = 0; r < R; r++)
            {
                int first = m.VertexCount;
                for (int k = 0; k <= sides; k++)
                {
                    int kk = k % sides;
                    Vector3 dk = S[r, (kk + 1) % sides] - S[r, (kk + sides - 1) % sides];
                    Vector3 dr = S[Mathf.Min(R - 1, r + 1), kk] - S[Mathf.Max(0, r - 1), kk];
                    Vector3 n = Vector3.Cross(dr, dk);
                    Vector3 outward = S[r, kk] - C[r];
                    if (Vector3.Dot(n, outward) < 0f) n = -n;
                    n = PartMeshEmitter.Unit(n, PartMeshEmitter.Unit(outward, RY[r]));
                    float phi = -0.5f * Mathf.PI + 2f * Mathf.PI * kk / sides;
                    m.Vertex(S[r, k], n, arcAt[r] + 0.0f, (float)k / sides, PartMeshEmitter.PartBody, Mathf.Sin(phi), skin[r]);
                }
                m.Stitch(prev, first, sides, prevPole, false, false);
                prev = first; prevPole = false;
            }
            // closed inside the head
            int tip = m.Pole(P[K - 1] + Z[K - 1] * (0.8f * Width(1f)), Z[K - 1], total, PartMeshEmitter.PartBody, 0f, PartMeshEmitter.Rigid(b0 + K - 1), sides);
            m.Stitch(prev, tip, sides, false, true, false);
        }
    }
}
