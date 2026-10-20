// Creature kit (sky manta): the shape of a ray's disc - the RayBody's thick centre and the RayWings' thick pectoral
// fins as ONE surface, so the wing's root column is the body's edge column exactly (same points, same normals: no
// seam). Scale 1, in the rest frame of spine joint 0 (x right, y up, z forward; joint i at z = -i x spacing).
//
// Planform, over the signed span coordinate x (|x| <= halfWidth: the body; past it: the wing, u = 0 at its root):
//   body   front edge z = headCap - headRound (x/hw)^2, rear edge z = -(spine + tailTip) + rearInset (x/hw)^2
//   wing   leading edge swept back (sweepDeg, plus tipSweep u^3: a falcate tip), chord falling to tipChord
// Section along the chord v (0 leading edge .. 1 trailing edge): a NACA thickness form (round nose, fine trailing
// edge) A(v), fuller in the body (A^p, p < 1) and blunt at the body's rear (the tail root), a trailing edge
// thickness that never reaches zero; topShare of it above the chord line (a domed back, a flatter belly).
// Thickness across the span: the body's centre thickness easing to edgeThickness at its edge (x^2), then on the
// wing a cubic from the edge's thickness (slope 0 on both sides: C1, no shading crease at the root) down to tipThickness.
// Chord samples are shared (Chord): a cluster round the nose, then one sample per wing chord row, so a wing's
// vertices sit on its lattice rows (Kit/RayWing.cs: skinning is two bones, blended along the span only).
using UnityEngine;

namespace CreatureKit
{
    public sealed class RayShape
    {
        public readonly RayBody Body;
        public readonly RayWing Wing;      // null: a body without wings (its edge closes as a body)
        public readonly float HalfWidth, Span, HalfSpan;
        public readonly float[] Chord;     // the shared chord samples (v), nose first
        public readonly int Rows;          // wing chord rows (lattice), at v = r / (Rows - 1)
        public readonly float NoseEnd;     // samples v <= this ride row 0 (the rigid nose)

        static readonly float[] kNose = { 0f, 0.006f, 0.025f, 0.06f };

        public RayShape(RayBody body, RayWing wing)
        {
            Body = body; Wing = wing;
            HalfWidth = body.halfWidth;
            Span = wing != null ? wing.span : 0f;
            HalfSpan = HalfWidth + Span;
            Rows = wing != null ? wing.Rows : 6;
            float first = 1f / (Rows - 1);
            NoseEnd = 0.6f * first;
            var list = new System.Collections.Generic.List<float>(kNose.Length + Rows + 1);
            foreach (float v in kNose) if (v < NoseEnd) list.Add(v);
            list.Add(NoseEnd);
            for (int r = 1; r < Rows; r++) list.Add(r * first);
            Chord = list.ToArray();
        }

        public static RayShape For(System.Collections.Generic.IReadOnlyList<CreaturePart> parts)
        {
            RayBody b = null; RayWing w = null;
            for (int i = 0; i < parts.Count; i++)
            {
                if (b == null && parts[i] is RayBody rb) b = rb;
                if (w == null && parts[i] is RayWing rw) w = rw;
            }
            return b != null ? new RayShape(b, w) : null;
        }

        // The wing chord row a chord sample rides (the nose cluster rides row 0).
        public int RowOf(float v) => v <= NoseEnd + 1e-6f ? 0 : Mathf.Clamp(Mathf.RoundToInt(v * (Rows - 1)), 0, Rows - 1);

        // ------------------------------------------------------------ planform

        float SpineLen => Body.spacing * (Body.JointCount - 1);
        float HW => Mathf.Max(HalfWidth, 1e-3f);

        // the wing's span fraction at |x| (0 at the root; < 0 in the body)
        public float U(float ax) => Span > 0f ? (ax - HalfWidth) / Span : 0f;

        public float Front(float x)
        {
            float ax = Mathf.Abs(x);
            float q = Mathf.Min(ax, HalfWidth) / HW;
            float zf = Body.headCapLength - Body.headRound * q * q;
            if (ax <= HalfWidth || Wing == null) return zf;
            float u = U(ax);
            return zf - Span * (Mathf.Tan(Wing.sweepDeg * Mathf.Deg2Rad) * u + Wing.tipSweep / Mathf.Max(Span, 1e-3f) * u * u * u);
        }

        public float Rear(float x)
        {
            float ax = Mathf.Abs(x);
            float q = Mathf.Min(ax, HalfWidth) / HW;
            float zr = -(SpineLen + Body.tailTipLength) + Body.rearInset * q * q;
            if (ax <= HalfWidth || Wing == null) return zr;
            float u = U(ax);
            float c0 = Front(HalfWidth) - zr;
            float chord = Wing.tipChord + (c0 - Wing.tipChord) * Mathf.Pow(Mathf.Max(0f, 1f - u), Wing.chordExponent);
            return Front(ax) - chord;
        }

        // ------------------------------------------------------------ thickness

        public float Thickness(float x)
        {
            float ax = Mathf.Abs(x);
            float t0 = Body.thickness, te = Body.edgeThickness;
            if (ax <= HalfWidth || Wing == null)
            {
                // a dome over the body, flat where it meets the wing (zero slope: the wing's root carries on level)
                float q = Mathf.Min(ax, HalfWidth) / HW, r = 1f - q * q;
                return te + (t0 - te) * r * r;
            }
            // cubic Hermite from the body edge (te, slope 0) to (tipThickness, slope 0), in u
            float u = Mathf.Min(U(ax), 1f);
            float t1 = Wing.tipThickness;
            float h00 = 2f * u * u * u - 3f * u * u + 1f, h01 = -2f * u * u * u + 3f * u * u;
            float t = h00 * te + h01 * t1;
            // the root's thick part thins out faster on a long wing (thicknessFalloff > 1: a slimmer mid-wing)
            float f = Mathf.Pow(Mathf.Max(0f, 1f - u), Mathf.Max(0f, Wing.thicknessFalloff - 1f));
            return Mathf.Max(t1, t1 + (t - t1) * f);
        }

        // fullness exponent of the section (the body's is fuller: a deep head and trunk)
        float Fullness(float ax)
        {
            if (ax >= HalfWidth) return 1f;
            float q = ax / HW;
            return Mathf.Lerp(Body.fullness, 1f, q * q);
        }

        float Blunt(float ax)
        {
            if (ax >= HalfWidth) return 0f;
            float q = ax / HW;
            return Body.bluntRear * (1f - q * q);
        }

        public float TopShare(float ax)
        {
            if (ax <= HalfWidth || Wing == null) return Body.topShare;
            return Mathf.Lerp(Body.topShare, 0.5f, Mathf.Clamp01(U(ax)));
        }

        // NACA 00xx thickness form normalised to a maximum of 1 (at v = 0.3), closed at v = 1
        public static float Airfoil(float v)
        {
            v = Mathf.Clamp01(v);
            float a = 0.2969f * Mathf.Sqrt(v) - 0.1260f * v - 0.3516f * v * v + 0.2843f * v * v * v - 0.1036f * v * v * v * v;
            return Mathf.Max(0f, a / 0.1f);
        }

        // Full thickness of the section at x, chord fraction v.
        public float SectionThickness(float x, float v)
        {
            float ax = Mathf.Abs(x);
            float vb = v * (1f - Blunt(ax));
            return Thickness(ax) * Mathf.Pow(Airfoil(vb), Fullness(ax)) + Body.trailingThickness * v;
        }

        // ------------------------------------------------------------ points

        // The surface point at signed x, chord fraction v, on the top (+1) or the bottom (-1) side.
        public Vector3 Point(float x, float v, int top)
        {
            float z = Mathf.Lerp(Front(x), Rear(x), v);
            float th = SectionThickness(x, v);
            float share = TopShare(Mathf.Abs(x));
            float y = top > 0 ? share * th : -(1f - share) * th;
            return new Vector3(x, y, z);
        }

        // The chord-line point a wing lattice node sits on: half way between top and bottom.
        public Vector3 Mid(float x, float v)
        {
            float z = Mathf.Lerp(Front(x), Rear(x), v);
            float th = SectionThickness(x, v);
            float share = TopShare(Mathf.Abs(x));
            return new Vector3(x, (share - 0.5f) * th, z);
        }

        // Loop index -> (chord sample, side): bottom from the trailing edge to the nose, then the top back to the
        // trailing edge; the nose (v = 0) once. LoopCount = 2 x Chord.Length - 1.
        public int LoopCount => 2 * Chord.Length - 1;
        public void Loop(int l, out float v, out int top)
        {
            int m = Chord.Length;
            if (l < m - 1) { v = Chord[m - 1 - l]; top = -1; }
            else { v = Chord[l - (m - 1)]; top = 1; }
        }

        public Vector3 LoopPoint(float x, int l)
        {
            Loop(l, out float v, out int top);
            return Point(x, v, top);
        }

        // Outward normal at loop index l of the column at signed x (finite differences along the loop and the span).
        public Vector3 LoopNormal(float x, int l)
        {
            const float dx = 1e-3f;
            int L = LoopCount;
            Vector3 a = LoopPoint(x, Mathf.Max(0, l - 1)), b = LoopPoint(x, Mathf.Min(L - 1, l + 1));
            Vector3 along = b - a;
            Vector3 span = LoopPoint(x + dx, l) - LoopPoint(x - dx, l);
            Loop(l, out float v, out int top);
            Vector3 n = Vector3.Cross(span, along);
            Vector3 fallback = v <= 0f ? Vector3.forward : (top > 0 ? Vector3.up : Vector3.down);
            n = PartMeshEmitter.Unit(n, fallback);
            // the loop runs bottom (backward to forward) then top (forward to backward): cross(+x, loop) is outward
            return n;
        }
    }
}
