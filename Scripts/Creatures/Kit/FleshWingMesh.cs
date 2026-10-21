// Creature kit: FleshWing's mesh (see FleshWing.cs), drawn once on the emitter's spread rest pose (the bind, part-folded
// by bindFold): the sail (the flight's fan from the wrist and the flank patches, as two skins offset by the thickness law
// along each zone's normal), the rounded rim along the free edge (leading edge on the first finger, the smooth trailing
// edge to the flank), the arm's flesh (one loft round the humerus and forearm, LeafArm) and the wrist pad. Each region's
// thickness comes from its place on the SPREAD wing (span from the body out to the tip, chord from the leading to the
// trailing edge), so the law reads the same at any bindFold. Build-time only: it allocates.
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public partial class FleshWing
    {
        const int kRimSides = 6, kArmSides = 14;
        // the leading edge's flesh (FleshWing.leadFlesh) spreads over this much of the chord
        const float kLeadWidth = 0.2f;
        // near the wrist the sail's zones are narrow (a fan's width grows with the radius): the half thickness grows at most
        // this x the distance from the wrist, so a folded zone's two-ray skinning never pushes a skin across its narrow cell
        const float kWristSlope = 0.15f;
        // the club: the forearm swells to this x padRadius at the wrist; the cone's tip radius x padRadius and its half
        // angle (blunt: it stays the lowest point while the planted hand turns ~30 deg); the hook's root radius x padRadius
        const float kClubArm = 0.8f, kClubTip = 0.1f, kClubConeDeg = 58f, kHookRoot = 0.18f;
        // the maroon / teal boundary's stagger between stripes (x the rays' reach); the leaf's sail side (its section's
        // cos beyond this) coloured as the sail
        const float kColourBlend = 0.08f, kLeafSailSide = 0.2f;

        // test hook (CreatureFleshWingTests): the uv0.y offset that marks the rim's vertices
        internal const float RimMark = 4f;
        // diagnostics: zero thickness (the mid-surface alone: is a flip the skins' or the fold's?)
        internal static bool FlatForTest;
        // RED hook (CreatureFleshWingTests.Red_FleshWing_OpenWithoutRims): the rims left out
        internal static bool NoRimsForTest;
        // RED hook (CreatureFleshWingTests.Red_FleshWing_NoKnuckles_BonesWithoutSkin): the wrist knuckles left out
        internal static bool NoKnucklesForTest;
        // the knuckles' radius x padRadius (Knuckles: the bones the sail does not ride, hidden in the club)
        internal const float KnuckleRadius = 0.25f;
        // RED hook (CreatureFleshWingTests.Red_FleshWing_BigKnuckles_ShowOutsideTheClub): the knuckles this much larger
        internal static float KnuckleScaleForTest = 1f;
        // RED hook (CreatureFleshWingTests.Red_FleshWing_TubeArm_IsATube, Red_FleshWing_BallElbow_SeparateSolids): the old arm
        internal static bool TubeArmForTest;

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Wings == null) return;
            int w = PteroWing.WingIndex(KitView.Parts(m.Species), c.Index);
            if (w >= m.WingSpecs.Length) return;
            int extra = m.Species.Layout.Start(c.Index) + FlightBones;
            // the wing's mesh only depends on the species' part data (its own, the arm's, the body's), its bones' place in
            // the layout, the rest pose and the bind's bones (the arm and the spread wing): built once, then replayed
            // (RestMeshCache - ~0.5 ms a wing)
            ulong key = RestMeshCache.Mix(RestMeshCache.MixData(RestMeshCache.Mix(RestMeshCache.Seed, 0xF1E5), this), w);
            foreach (CreaturePart q in KitView.Parts(m.Species)) key = RestMeshCache.MixData(key, q);
            key = RestMeshCache.Mix(RestMeshCache.Mix(RestMeshCache.Mix(RestMeshCache.Mix(RestMeshCache.Mix(key, extra), FlatForTest), NoRimsForTest), NoKnucklesForTest), TubeArmForTest);
            key = RestMeshCache.MixBones(RestMeshCache.MixRest(RestMeshCache.Mix(key, KnuckleScaleForTest), m), m);
            string[] kinds = stripes > 0 ? kKindsStriped : kKinds;
            if (RestMeshCache.TryReplay(m, key, kinds)) return;
            int firstVertex = m.VertexCount, firstTri = m.Lists.tris.Count;
            new FleshBuilder(this, m, w, extra).Build();
            RestMeshCache.Store(m, key, firstVertex, firstTri, kinds);
        }

        static readonly string[] kKinds = { "wingfan" }, kKindsStriped = { "wingfan", "wingstripe" };

        // One grid of the sail (the fan from the wrist, or the flank patches from the elbow) on its mid-surface: per sample
        // the point, the unit dorsal normal, the half thickness, the skin and uv0; per cell the kind (material slot).
        sealed class Sheet
        {
            public readonly int Cols, Rows;   // samples: (Cols + 1) x (Rows + 1)
            public readonly Vector3[] P, N; public readonly float[] H; public readonly Vector4[] Skin; public readonly Vector2[] Uv;
            public readonly int[] Kind;       // per cell
            public readonly int[] Home;       // per cell: the bone whose frame the cell is (a triangle starts at its heaviest vertex)
            public bool PoleRow0;
            public bool PinchRow0;            // row 0 has no thickness (the skins meet there: the rims close onto it with caps)
            public Sheet(int cols, int rows)
            {
                Cols = cols; Rows = rows;
                int n = (cols + 1) * (rows + 1);
                P = new Vector3[n]; N = new Vector3[n]; H = new float[n]; Skin = new Vector4[n]; Uv = new Vector2[n];
                Kind = new int[cols * rows]; Home = new int[cols * rows];
            }
            public int I(int i, int j) => j * (Cols + 1) + i;
        }

        sealed class FleshBuilder
        {
            readonly FleshWing p;
            readonly PartMeshEmitter em;
            readonly CreatureSpecies sp;
            readonly CreatureWings cw, spreadW;
            readonly WingSpec ws;
            readonly int w, extra, F, Z, NS, side, bu, bl, bh, kindBody, kindFan, kindStripe;
            readonly float[] colX;    // fan columns, in zone units (z + u), 0 .. Z
            readonly float[] rowV;    // fan rows, 0 .. 1 along the rays
            readonly int freeCols;    // the fan column on the rear flank rib (the free zones end there)

            public FleshBuilder(FleshWing part, PartMeshEmitter m, int wing, int firstExtra)
            {
                p = part; em = m; sp = m.Species; w = wing; extra = firstExtra;
                spreadW = m.Wings;
                cw = part.bindFold > 0f ? part.BindWings(m.Rest, w) : m.Wings;   // the bind's wing (FleshWingRuntime.AfterSpreadWings)
                ws = m.WingSpecs[w];
                F = cw.FingerCount(w); Z = F + 2; NS = part.Segments; side = cw.Side(w);
                bu = sp.LegBone(ws.leg, 0); bl = sp.LegBone(ws.leg, 1); bh = sp.LegBone(ws.leg, 2);
                kindBody = PartMeshEmitter.PartBody;
                kindFan = m.Slot("wingfan"); kindStripe = part.stripes > 0 ? m.Slot("wingstripe") : kindFan;
                // columns: C per zone, plus the stripes' edges across the free zones (finger 0 .. the rear flank rib)
                int C = part.columns, Zf = Z - 1;
                // (by priority, a column never closer than a third of a column to another: the rays, then the stripes' edges,
                // then the even columns - a sliver column makes slivers of the rim)
                var xs = new List<float>();
                float near = 0.34f / C;
                bool Free(float x) { foreach (float y in xs) if (Mathf.Abs(x - y) < near) return false; return true; }
                for (int k = 0; k <= Z; k++) xs.Add(k);
                for (int k = 0; k < part.stripes; k++)
                {
                    float g = (k + 0.5f) / part.stripes, hw = StripeHalf;
                    if (Free((g - hw) * Zf)) xs.Add((g - hw) * Zf);
                    if (Free((g + hw) * Zf)) xs.Add((g + hw) * Zf);
                }
                for (int k = 0; k <= Z * C; k++) if (Free((float)k / C)) xs.Add((float)k / C);
                colX = Sorted(xs, 1e-4f);
                for (int i = 0; i < colX.Length; i++) if (Mathf.Abs(colX[i] - Zf) < 1e-4f) freeCols = i;
                // rows: R along the rays, the segment joins, the colour split, two rows inside the pad
                int R = Mathf.Max(4, part.rows);
                var vs = new List<float>();
                // the sail starts a little way out from the wrist, inside the club (its first row an inner edge closed by a
                // rim, not skins pinched onto the wrist)
                float vp = PadV;
                vs.Add(vp);
                for (int j = 1; j <= R; j++) if ((float)j / R > vp + 2e-3f) vs.Add((float)j / R);
                for (int s = 1; s < NS; s++) vs.Add((float)s / NS);
                if (part.innerColour > vp && part.innerColour < 1f) vs.Add(part.innerColour);
                rowV = Sorted(vs, 2e-3f);
            }

            int patchFirst = -1, patchCount, sectorBone = -1;

            // a bone's normal axis in the bind, dorsal: +Y of a rigid bone, the normal column of an affine one (the arm sector,
            // the flank patches)
            Vector3 BoneN(int b, Vector3 dorsal)
            {
                Matrix4x4 m = em.Bones[b];
                bool affine = b == sectorBone || b == p.FanSectorBone(extra) || (b >= patchFirst && b < patchFirst + patchCount);
                Vector3 n = PartMeshEmitter.Unit(affine ? (Vector3)m.GetColumn(2) : (Vector3)m.GetColumn(1), dorsal);
                return Vector3.Dot(n, dorsal) < 0f ? -n : n;
            }

            Vector3 SkinN(Vector4 s, Vector3 dorsal) => PartMeshEmitter.Unit(s.z * BoneN((int)s.x, dorsal) + s.w * BoneN((int)s.y, dorsal), dorsal);

            float StripeHalf => p.stripes > 0 ? 0.2f / p.stripes : 0f;
            // the rows inside the pad: the skins close onto the wrist there
            float PadV => Mathf.Clamp(0.5f * p.padRadius / Mathf.Max(1e-3f, ws.fingerLengths[0]), 0.01f, 0.2f);

            static float[] Sorted(List<float> xs, float eps)
            {
                xs.Sort();
                var o = new List<float>();
                foreach (float x in xs) if (o.Count == 0 || x - o[o.Count - 1] > eps) o.Add(x);
                return o.ToArray();
            }

            int Seg(float v) => Mathf.Clamp(Mathf.FloorToInt(v * NS + 1e-4f), 0, NS - 1);

            public void Build()
            {
                int first = em.VertexCount;
                Rays(cw, true, out Vector3[] ray, out Vector3 wrist, out Vector3 elbow, out Vector3 shoulder);
                Rays(spreadW, false, out Vector3[] sRay, out Vector3 sWrist, out Vector3 sElbow, out _);
                // the fan's points: bind and spread
                Vector3[] fanB = FanPoints(cw, ray, wrist, elbow), fanS = FanPoints(spreadW, sRay, sWrist, sElbow);
                int PC = cw.FlankPatches(w), pcols = 2;
                Vector3[] patB = PatchPoints(cw, fanB, elbow, PC, pcols), patS = PatchPoints(spreadW, fanS, sElbow, PC, pcols);
                // span / chord from the spread wing (girdle frame)
                var coords = new SpanChord(em.Rest, sp.legs[ws.leg].attachJoint, side, fanS, patS);
                FanSheets(wrist, fanB, fanS, coords, out Sheet free, out Sheet aft, out Sheet arm);
                List<Sheet> pats = PatchSheets(arm, elbow, patB, patS, coords, PC, pcols);
                EmitSheet(free); EmitSheet(aft); EmitSheet(arm);
                foreach (Sheet q in pats) EmitSheet(q);
                Rims(free, aft, arm, pats);
                if (TubeArmForTest) TubeArm(shoulder, elbow, wrist, arm); else LeafArm(shoulder, elbow, wrist, arm);
                Club();
                if (!NoKnucklesForTest) Knuckles(first);
            }

            // Every flight bone the sail does not ride - the rear flank rib and the tall stance's fan-side copy of it (the
            // aft zone rides the fan sector, the arm zone the arm sector) - carries a small knuckle at its origin: the wrist,
            // inside the club. No bone is left without skin (CreatureKitTests 3, Layout); a rib's frame only turns about the
            // wrist and stretches with its rib (CreatureWings.WriteBones), so the knuckle stays inside the club in any pose.
            void Knuckles(int first)
            {
                var used = new HashSet<int>();
                List<Vector4> uv2 = em.Lists.uv2;
                for (int i = first; i < uv2.Count; i++) { Vector4 s = uv2[i]; if (s.z > 0f) used.Add((int)s.x); if (s.w > 0f) used.Add((int)s.y); }
                float r = KnuckleRadius * KnuckleScaleForTest * p.padRadius;
                for (int k = 0; k < p.FlightBones; k++)
                {
                    int b = sp.WingBone(w, k);
                    if (used.Contains(b)) continue;
                    Vector3 c = em.Bones[b].GetColumn(3);
                    em.Ellipsoid(Matrix4x4.TRS(c, Quaternion.identity, Vector3.one), Vector3.zero, r, r, r, 8, 4, PartMeshEmitter.PartBody, b);
                }
            }

            // ---------------------------------------------------------- rays

            // the fan's rays from the wrist, leading to trailing: fingers, the trailing rib, the rear flank rib, the forearm
            void Rays(CreatureWings wings, bool bind, out Vector3[] ray, out Vector3 wrist, out Vector3 elbow, out Vector3 shoulder)
            {
                WingState st = wings.State[w];
                wrist = st.wrist; elbow = st.elbow; shoulder = st.shoulder;
                ray = new Vector3[Z + 1];
                for (int k = 0; k < F; k++) ray[k] = wings.FingerTip[wings.FingerIndex(w, k)] - wrist;
                // the trailing rib as the runtime lays it in the bind (in the fingers' plane, FleshWingRuntime.Write) and as long
                // as the last finger: the edge between them an arc that keeps its way across the zone however far it closes
                ray[F] = (!bind ? (wings.RibTarget[w * WingSpec.Ribs] - wrist).normalized
                                           : PartMeshEmitter.Unit(em.Bones[sp.WingBone(w, WingBoneLayout.FirstRibOf(ws))].GetColumn(2), Vector3.forward)) * ray[F - 1].magnitude;
                ray[F + 1] = wings.RibTarget[w * WingSpec.Ribs + 1] - wrist;
                ray[Z] = elbow - wrist;
            }

            int RayBone(int k, int seg)
            {
                if (k < F) return p.SegmentBone(sp, w, extra, k, seg);
                if (k == F) return sp.WingBone(w, WingBoneLayout.FirstRibOf(ws));
                if (k == F + 1) return p.FanSectorBone(extra);   // the zone behind the trailing rib rides the fan sector
                return sp.WingBone(w, WingBoneLayout.ArmRibOf(ws));
            }

            static float CR(float a, float b, float c, float d, float t) => PartMeshEmitter.CR(a, b, c, d, t);

            // the fan's samples (columns colX, rows rowV): the free zones out to a smooth edge through the rays' tips (a
            // Catmull-Rom of the ray lengths), the arm zone (rear flank rib .. forearm) out to the straight chord from the
            // rib's flank end to the elbow
            Vector3[] FanPoints(CreatureWings wings, Vector3[] ray, Vector3 wrist, Vector3 elbow)
            {
                int nc = colX.Length, nr = rowV.Length;
                var o = new Vector3[nc * nr];
                var len = new float[Z + 1];
                for (int k = 0; k <= Z; k++) len[k] = ray[k].magnitude;
                for (int i = 0; i < nc; i++)
                {
                    int z = Mathf.Min(Z - 1, Mathf.FloorToInt(colX[i] + 1e-5f));
                    float u = Mathf.Clamp01(colX[i] - z);
                    Vector3 A = ray[z], B = ray[z + 1];
                    float la = Mathf.Max(1e-6f, len[z]), lb = Mathf.Max(1e-6f, len[z + 1]);
                    Vector3 da = A / la, db = B / lb;
                    float th = Mathf.Acos(Mathf.Clamp(Vector3.Dot(da, db), -1f, 1f)), ph = u * th;
                    Vector3 dir = th > 1e-4f ? (Mathf.Sin(th - ph) * da + Mathf.Sin(ph) * db) / Mathf.Sin(th) : Vector3.Lerp(da, db, u).normalized;
                    float reach;
                    if (z == Z - 1)
                    {
                        float den = la * Mathf.Sin(ph) + lb * Mathf.Sin(th - ph);
                        reach = th > 1e-4f && den > 1e-6f ? la * lb * Mathf.Sin(th) / den : Mathf.Lerp(la, lb, u);
                    }
                    else
                    {
                        float l0 = len[Mathf.Max(0, z - 1)], l3 = len[Mathf.Min(Z - 1, z + 2)];
                        reach = Mathf.Max(0.5f * Mathf.Min(la, lb), CR(l0, la, lb, l3, u));
                    }
                    for (int j = 0; j < nr; j++) o[j * nc + i] = wrist + dir * (reach * rowV[j]);
                }
                return o;
            }

            // the flank patches: a fan from the elbow per patch, between the arm zone's far edge (patch 0's first column) and
            // the flank nodes; rows follow the arm zone's columns (row j at the radius of its chord point)
            Vector3[] PatchPoints(CreatureWings wings, Vector3[] fanPts, Vector3 elbow, int P, int pc)
            {
                int nc = colX.Length, nr = rowV.Length, armFirst = freeCols;
                int rows = nc - 1 - armFirst;   // the arm zone's columns: chord[0] = rib end .. chord[rows] = elbow
                Vector3 Chord(int i) => fanPts[(nr - 1) * nc + armFirst + i];
                int f0 = wings.FlankStart(w);
                Vector3 Node(int k) => k == 0 ? Chord(0) : wings.FlankNode[f0 + k];
                float la0 = Mathf.Max(1e-6f, (Chord(0) - elbow).magnitude);
                int cols = P * pc;
                var o = new Vector3[(cols + 1) * (rows + 1)];
                for (int j = 0; j <= rows; j++)
                {
                    Vector3 cj = Chord(rows - j);
                    float v = Mathf.Clamp01((cj - elbow).magnitude / la0);
                    for (int gi = 0; gi <= cols; gi++)
                    {
                        int q = Mathf.Min(P - 1, gi / pc); float u = (float)(gi - q * pc) / pc;
                        Vector3 A = Node(q) - elbow, B = Node(q + 1) - elbow;
                        float la = Mathf.Max(1e-6f, A.magnitude), lb = Mathf.Max(1e-6f, B.magnitude);
                        Vector3 da = A / la, db = B / lb;
                        float th = Mathf.Acos(Mathf.Clamp(Vector3.Dot(da, db), -1f, 1f)), ph = u * th;
                        float den = la * Mathf.Sin(ph) + lb * Mathf.Sin(th - ph);
                        float reach = th > 1e-4f && den > 1e-6f ? la * lb * Mathf.Sin(th) / den : Mathf.Lerp(la, lb, u);
                        Vector3 dir = th > 1e-4f ? (Mathf.Sin(th - ph) * da + Mathf.Sin(ph) * db) / Mathf.Sin(th) : da;
                        o[j * (cols + 1) + gi] = gi == 0 ? cj : elbow + dir * (reach * v);
                    }
                }
                return o;
            }

            // ---------------------------------------------------------- the sheets

            float Ridge(float g)
            {
                if (p.stripes <= 0 || !(p.stripeDepth > 0f)) return 0f;
                float x = g * p.stripes - Mathf.Floor(g * p.stripes) - 0.5f;   // -0.5 .. 0.5 about a stripe's centre
                float hw = StripeHalf * p.stripes;
                if (Mathf.Abs(x) >= hw) return 0f;
                float c = Mathf.Cos(0.5f * Mathf.PI * x / hw);
                return p.stripeDepth * c * c;
            }

            float Half(SpanChord coords, Vector3 spreadP)
            {
                coords.At(spreadP, out float span, out float chord);
                float h = 0.5f * p.ThicknessAt(span, chord);
                // the root muscles: a swell next to the body, fading out by a quarter of the span
                float r = Mathf.Clamp01(1f - span / 0.25f);
                // the arm's flesh runs on along the leading edge (the leading finger is a fleshy spar), tapering to the tip
                float lead = Mathf.Exp(-(chord * chord) / (kLeadWidth * kLeadWidth));
                return h + 0.5f * p.rootSwell * r * r * (1f - chord) + p.leadFlesh * p.armBulk * (1f - span) * lead;
            }

            // the skins close onto a pole hidden inside the pad or the arm: 0 within r0 of it, full from r1
            static float Ramp(float d, float r0, float r1) => Mathf.SmoothStep(0f, 1f, (d - r0) / Mathf.Max(1e-5f, r1 - r0));

            int FanKind(float xm, float vm) => xm >= Z - 1 ? kindBody : ColourAt(xm / (Z - 1), vm);

            // The sail's colour at g (across the rays: 0 the leading finger .. 1 the rear flank rib) and v (along them from
            // the wrist, x the ray's reach): the dark stripes run the whole length, through the maroon too; between them the
            // body's maroon inside innerColour, the teal fan beyond, their boundary staggered by +-kColourBlend between
            // neighbouring stripes (teal tongues reaching down, maroon up: a blend at a distance, one kind a triangle).
            int ColourAt(float g, float v)
            {
                if (p.stripes > 0)
                {
                    float x = g * p.stripes - Mathf.Floor(g * p.stripes) - 0.5f;
                    if (Mathf.Abs(x) < StripeHalf * p.stripes) return kindStripe;
                }
                float stagger = p.stripes > 0 ? Mathf.Sin(Mathf.PI * g * p.stripes) : 0f;
                return v < p.innerColour + kColourBlend * stagger ? kindBody : kindFan;
            }

            // The fan from the wrist as three sheets, each closed by rims where it ends: the free sail (finger 0 .. the
            // trailing rib) on the rays' bones (two-ray skinning at the same span segment: rays of fixed length whose ups agree,
            // so a closing zone keeps its two skins apart), the aft sail (the trailing rib .. the rear flank rib) on the fan
            // sector alone, and the arm zone (the rear flank rib .. the forearm) on the arm sector alone (exact: an affine bone
            // never turns its own triangles over, however its rays stretch). The sheets meet along the trailing and the rear
            // rib, a crease when the folded wing stands on the ground.
            void FanSheets(Vector3 wrist, Vector3[] bind, Vector3[] spread, SpanChord coords, out Sheet free, out Sheet aft, out Sheet arm)
            {
                int nc = colX.Length, nr = rowV.Length;
                patchFirst = sp.WingBone(w, WingBoneLayout.HumerusPatchOf(ws)); patchCount = cw.FlankPatches(w);
                sectorBone = sp.WingBone(w, WingBoneLayout.ArmRibOf(ws));
                int fan = p.FanSectorBone(extra), trail = RayBone(F, 0);
                int iT = 0;
                for (int i = 0; i < nc; i++) if (Mathf.Abs(colX[i] - F) < 1e-4f) iT = i;
                free = FanSheet(0, iT, wrist, bind, spread, coords, true,
                    (i, j, z, u, seg) => i == iT ? PartMeshEmitter.Rigid(trail) : u < 1e-5f ? PartMeshEmitter.Rigid(RayBone(z, seg)) : PartMeshEmitter.Blend(RayBone(z, seg), RayBone(z + 1, seg), u),
                    (xm, vm) => { int zc = Mathf.Min(F - 1, Mathf.FloorToInt(xm)); return RayBone(xm - zc < 0.5f ? zc : zc + 1, Seg(vm)); });
                aft = FanSheet(iT, freeCols, wrist, bind, spread, coords, true, (i, j, z, u, seg) => PartMeshEmitter.Rigid(fan), (xm, vm) => fan);
                arm = FanSheet(freeCols, nc - 1, wrist, bind, spread, coords, false, (i, j, z, u, seg) => PartMeshEmitter.Rigid(sectorBone), (xm, vm) => sectorBone);
            }

            delegate Vector4 SkinOf(int i, int j, int z, float u, int seg);

            // the fan's columns i0..i1 as one sheet: its outer row and both end columns are free edges (beads for the rims);
            // ridges: the stripes' ridges (the sail, not the arm zone)
            Sheet FanSheet(int i0, int i1, Vector3 wrist, Vector3[] bind, Vector3[] spread, SpanChord coords, bool ridges, SkinOf skinOf, System.Func<float, float, int> homeOf)
            {
                int nc = colX.Length, nr = rowV.Length;
                Vector3 hn = cw.State[w].handNormal;
                var sh = new Sheet(i1 - i0, nr - 1) { PinchRow0 = true };
                for (int j = 0; j < nr; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        int z = Mathf.Min(Z - 1, Mathf.FloorToInt(colX[i] + 1e-5f));
                        float u = Mathf.Clamp01(colX[i] - z), v = rowV[j];
                        int k = sh.I(i - i0, j), src = j * nc + i;
                        Vector4 skin = skinOf(i, Mathf.Max(j, 1), z, u, Seg(Mathf.Max(v, 1e-3f)));
                        sh.Skin[k] = skin;
                        sh.N[k] = SkinN(skin, hn);
                        sh.P[k] = bind[src];
                        float d = (bind[src] - wrist).magnitude;
                        float full = Half(coords, spread[src]) + (ridges ? Ridge(Mathf.Clamp01(colX[i] / (Z - 1))) : 0f);
                        // the inner edge (row 0, inside the club and the forearm's swelling) closes: the two skins meet there
                        float h = j == 0 ? 0f : Mathf.Min(full, kWristSlope * d);
                        // every other edge rolls into a rim's bead
                        if (j > 0 && (i == i0 || i == i1 || j == nr - 1)) h = p.RimRadius(h);
                        sh.H[k] = FlatForTest ? 0f : h;
                        sh.Uv[k] = new Vector2((sh.P[k] - wrist).magnitude, colX[i] / Z);
                    }
                for (int j = 0; j < nr - 1; j++)
                    for (int i = i0; i < i1; i++)
                    {
                        float vm = 0.5f * (rowV[j] + rowV[j + 1]), xm = 0.5f * (colX[i] + colX[i + 1]);
                        sh.Kind[j * sh.Cols + i - i0] = FanKind(xm, vm);
                        sh.Home[j * sh.Cols + i - i0] = homeOf(xm, vm);
                    }
                return sh;
            }

            // The flank patches (the membrane between the humerus and the body): one sheet per patch, each rigid to its affine
            // patch bone (exact on its triangle, so never turned over), closed by a rim along both radial edges (where the
            // neighbouring patch takes over, and the arm zone's far edge on the first); row j at the radius of the arm zone's
            // far-edge sample j from the elbow.
            List<Sheet> PatchSheets(Sheet arm, Vector3 elbow, Vector3[] bind, Vector3[] spread, SpanChord coords, int P, int pc)
            {
                int nr = rowV.Length;
                int rows = arm.Cols, cols = P * pc;
                Vector3 nz = arm.N[arm.I(1, nr - 1)];
                float r0 = 0.5f * p.armBulk, r1 = 1.5f * p.armBulk;
                var o = new List<Sheet>();
                for (int q = 0; q < P; q++)
                {
                    var sh = new Sheet(pc, rows) { PoleRow0 = true, PinchRow0 = true };
                    int bone = patchFirst + q;
                    Vector3 n = BoneN(bone, nz);
                    for (int j = 0; j <= rows; j++)
                        for (int iq = 0; iq <= pc; iq++)
                        {
                            int k = sh.I(iq, j), gi = q * pc + iq, src = j * (cols + 1) + gi;
                            sh.Skin[k] = PartMeshEmitter.Rigid(bone);
                            sh.N[k] = n;
                            sh.P[k] = j == 0 ? elbow : bind[src];
                            float ramp = j == 0 ? 0f : Ramp((sh.P[k] - elbow).magnitude, r0, r1);
                            float h = Half(coords, spread[src]) * ramp;
                            if (j > 0 && (iq == 0 || iq == pc || j == rows)) h = p.RimRadius(h);
                            sh.H[k] = FlatForTest ? 0f : h;
                            sh.Uv[k] = new Vector2((sh.P[k] - elbow).magnitude, 1f + (float)gi / cols);
                        }
                    for (int c = 0; c < sh.Kind.Length; c++) { sh.Kind[c] = kindBody; sh.Home[c] = bone; }
                    o.Add(sh);
                }
                return o;
            }

            // both skins of a sheet: the dorsal at +H along the normal, the ventral at -H; a vertex per kind of the cells
            // around it (the shader reads one kind per triangle)
            void EmitSheet(Sheet sh)
            {
                for (int sheet = 0; sheet < 2; sheet++)
                {
                    float sg = sheet == 0 ? 1f : -1f;
                    var made = new Dictionary<long, int>();
                    int V(int i, int j, int kind)
                    {
                        long key = ((long)sh.I(i, j) << 8) | (long)kind;
                        if (made.TryGetValue(key, out int idx)) return idx;
                        int k = sh.I(i, j);
                        idx = em.VertexCount;
                        em.Vertex(sh.P[k] + sh.N[k] * (sg * sh.H[k]), sh.N[k] * sg, sh.Uv[k].x, sh.Uv[k].y, kind, 1f, sh.Skin[k]);
                        made[key] = idx;
                        return idx;
                    }
                    for (int j = 0; j < sh.Rows; j++)
                        for (int i = 0; i < sh.Cols; i++)
                        {
                            int kind = sh.Kind[j * sh.Cols + i], home = sh.Home[j * sh.Cols + i];
                            int a = V(i, j, kind), b = V(i + 1, j, kind), d = V(i, j + 1, kind), e = V(i + 1, j + 1, kind);
                            Vector3 n = sh.N[sh.I(i, j + 1)] * sg;
                            if (!(j == 0 && sh.PoleRow0)) Tri(a, b, d, n, home);
                            Tri(b, e, d, n, home);
                        }
                }
            }

            // one triangle, wound to face n; a degenerate one is dropped (a zero-area triangle reads as inverted). home >= 0:
            // the triangle starts at its vertex weighted most to that bone (a cyclic turn keeps the winding) - a triangle's
            // frame is its first vertex's bone (the kit's inverted-triangle measure)
            void Tri(int a, int b, int c, Vector3 n, int home = -1)
            {
                Vector3 pa = em.Position(a), pb = em.Position(b), pc = em.Position(c);
                Vector3 x = Vector3.Cross(pb - pa, pc - pa);
                if (x.sqrMagnitude < 1e-16f) return;
                if (home >= 0)
                {
                    float wa = Weight(a, home), wb = Weight(b, home), wc = Weight(c, home);
                    if (wb > wa && wb >= wc) { int t = a; a = b; b = c; c = t; }
                    else if (wc > wa && wc > wb) { int t = c; c = b; b = a; a = t; }
                }
                em.Tri(a, b, c, Vector3.Dot(x, n) < 0f);
            }

            float Weight(int v, int bone)
            {
                Vector4 s = em.Lists.uv2[v];
                return ((int)s.x == bone ? s.z : 0f) + ((int)s.y == bone && (int)s.x != bone ? s.w : 0f);
            }

            // ---------------------------------------------------------- the rims

            // A semicircle per sample of a sheet's free edge, from the dorsal skin round to the ventral (radius = the bead's
            // half thickness there), stitched sample to sample. path: the edge's samples in order; inward: a point on the
            // sheet's inner side of each; kinds: per segment (sample s .. s + 1); corners: samples where the edge turns (the
            // tip): the rim rounds them with a fan of rings on the one sample (one skin), each segment meeting rings that face
            // its own way - a quad never spans a sharp turn of the outward direction.
            void Rim(Sheet sh, List<int> path, List<Vector3> inward, List<int> kinds, ICollection<int> corners = null, int capStart = -1, int capEnd = -1)
            {
                int n = path.Count, firstRing = -1;
                Vector3 Out(int s, int a, int b)
                {
                    int k = path[s];
                    Vector3 nn = sh.N[k];
                    Vector3 t = PartMeshEmitter.Unit(sh.P[path[b]] - sh.P[path[a]], Vector3.forward);
                    Vector3 o = sh.P[k] - inward[s];
                    o -= t * Vector3.Dot(o, t); o -= nn * Vector3.Dot(o, nn);
                    return PartMeshEmitter.Unit(o, Vector3.Cross(t, nn));
                }
                int Ring(int s, Vector3 e, int kind)
                {
                    int k = path[s], first = em.VertexCount;
                    for (int q = 0; q <= kRimSides; q++)
                    {
                        float th = Mathf.PI * q / kRimSides;
                        Vector3 rn = sh.N[k] * Mathf.Cos(th) + e * Mathf.Sin(th);
                        em.Vertex(sh.P[k] + rn * sh.H[k], rn, sh.Uv[k].x, sh.Uv[k].y + RimMark, kind, 1f, sh.Skin[k]);
                    }
                    return first;
                }
                void Strip(int a, int b, Vector3 n0, Vector3 e0, int home, bool fan = false)
                {
                    // fan: rings on one sample (a corner); their triangles at the skins' edge are degenerate (dropped by Tri)
                    for (int q = 0; q < kRimSides; q++)
                    {
                        float th = Mathf.PI * (q + 0.5f) / kRimSides;
                        Vector3 out0 = n0 * Mathf.Cos(th) + e0 * Mathf.Sin(th);
                        Tri(a + q, b + q, a + q + 1, out0, home);
                        Tri(a + q + 1, b + q, b + q + 1, out0, home);
                    }
                }
                int prev = -1; Vector3 prevE = Vector3.zero;
                for (int s = 0; s < n; s++)
                {
                    int k = path[s];
                    int kindIn = s > 0 ? kinds[s - 1] : kinds[0], kindOut = s < n - 1 ? kinds[s] : kindIn;
                    bool corner = corners != null && corners.Contains(s) && s > 0 && s < n - 1;
                    Vector3 eIn = corner ? Out(s, s - 1, s) : Out(s, Mathf.Max(0, s - 1), Mathf.Min(n - 1, s + 1));
                    Vector3 eOut = corner ? Out(s, s, s + 1) : eIn;
                    int home = (int)sh.Skin[k].x;
                    // the segment arriving: from the previous sample's ring to this sample's ring facing the same way
                    if (prev >= 0)
                    {
                        int a = prev, b = Ring(s, eIn, kindIn);
                        Strip(a, b, sh.N[path[s - 1]], prevE, (int)sh.Skin[path[s - 1]].x);
                        prev = b; prevE = eIn;
                    }
                    else { prev = Ring(s, eIn, kindOut); prevE = eIn; firstRing = prev; }
                    if (corner)
                    {
                        // round the corner on this sample: rings turning from the arriving to the leaving direction
                        const int steps = 4;
                        for (int c = 1; c <= steps; c++)
                        {
                            Vector3 e = PartMeshEmitter.Unit(Vector3.Slerp(eIn, eOut, (float)c / steps), eOut);
                            int b = Ring(s, e, kindOut);
                            Strip(prev, b, sh.N[k], prevE, home, true);
                            prev = b; prevE = e;
                        }
                    }
                    else if (s < n - 1 && kindOut != kindIn)
                    {
                        // a colour change on this sample: the leaving segment starts from a ring of its own kind
                        prev = Ring(s, eIn, kindOut);
                    }
                }
                // caps: the first / last ring closed onto a pinched sample (the skins' meeting point)
                void Cap(int apexK, int ring, int sampleK, int kind)
                {
                    Vector3 apex = sh.P[apexK], c = sh.P[sampleK];
                    int a0 = em.VertexCount;
                    em.Vertex(apex, PartMeshEmitter.Unit(apex - c, sh.N[sampleK]), sh.Uv[apexK].x, sh.Uv[apexK].y + RimMark, kind, 1f, sh.Skin[apexK]);
                    for (int q = 0; q < kRimSides; q++)
                    {
                        Vector3 mid = 0.5f * (em.Position(ring + q) + em.Position(ring + q + 1));
                        Tri(a0, ring + q, ring + q + 1, PartMeshEmitter.Unit((mid - c) + (apex - c), apex - c), (int)sh.Skin[sampleK].x);
                    }
                }
                if (capStart >= 0 && firstRing >= 0) Cap(capStart, firstRing, path[0], kinds[0]);
                if (capEnd >= 0 && prev >= 0) Cap(capEnd, prev, path[n - 1], kinds[Mathf.Max(0, n - 2)]);
            }

            // a fan sheet's outline (each side optional): up its first column, along the outer row, down its last column, back
            // along the inner row; the turns rounded; each step the kind of the cell beside it
            void Outline(Sheet sh, bool left, bool top, bool right, bool bottom)
            {
                int R = sh.Rows, C = sh.Cols;
                int Kind(int ci, int cj) => sh.Kind[Mathf.Clamp(cj, 0, R - 1) * C + Mathf.Clamp(ci, 0, C - 1)];
                // the four sides, counter-clockwise from the inner-left corner: samples (i, j), the cell beside each step
                var sides = new List<(int i, int j, int ci, int cj)>[4];
                for (int k = 0; k < 4; k++) sides[k] = new List<(int, int, int, int)>();
                int j0 = sh.PinchRow0 ? 1 : 0;   // a pinched row 0: the side rims start a row up, capped onto it
                if (sh.PinchRow0) bottom = false;
                for (int j = j0; j <= R; j++) sides[0].Add((0, j, 0, j));                // up the first column
                for (int i = 0; i <= C; i++) sides[1].Add((i, R, i, R - 1));             // along the outer row
                for (int j = R; j >= j0; j--) sides[2].Add((C, j, C - 1, j - 1));        // down the last column
                for (int i = C; i >= 0; i--) sides[3].Add((i, 0, i - 1, 0));             // back along the inner row
                bool[] on = { left, top, right, bottom };
                // start after a missing side (or anywhere on a closed loop)
                int s0 = 0;
                for (int k = 0; k < 4; k++) if (!on[k]) { s0 = (k + 1) % 4; break; }
                var path = new List<int>(); var inward = new List<Vector3>(); var kinds = new List<int>(); var corners = new List<int>();
                void Flush()
                {
                    if (path.Count > 1)
                    {
                        // a path starting / ending on a pinched row's neighbour is capped onto the pinched sample
                        int cs = sh.PinchRow0 && path[0] == sh.I(0, 1) ? sh.I(0, 0) : sh.PinchRow0 && path[0] == sh.I(C, 1) ? sh.I(C, 0) : -1;
                        int last = path[path.Count - 1];
                        int ce = sh.PinchRow0 && last == sh.I(C, 1) ? sh.I(C, 0) : sh.PinchRow0 && last == sh.I(0, 1) ? sh.I(0, 0) : -1;
                        Rim(sh, new List<int>(path), new List<Vector3>(inward), new List<int>(kinds), new List<int>(corners), cs, ce);
                    }
                    path.Clear(); inward.Clear(); kinds.Clear(); corners.Clear();
                }
                Vector3 In(int i, int j)
                {
                    int ii = i == 0 ? 1 : i == C ? C - 1 : i, jj = j == 0 ? 1 : j == R ? R - 1 : j;
                    bool ci = i == 0 || i == C, cj = j == 0 || j == R;
                    if (ci && cj) return 0.5f * (sh.P[sh.I(ii, j)] + sh.P[sh.I(i, jj)]);
                    return ci ? sh.P[sh.I(ii, j)] : sh.P[sh.I(i, jj)];
                }
                bool all = left && top && right && bottom;
                for (int n = 0; n < 4; n++)
                {
                    int k = (s0 + n) % 4;
                    if (!on[k]) { Flush(); continue; }
                    var side = sides[k];
                    for (int q = path.Count == 0 ? 0 : 1; q < side.Count; q++)
                    {
                        var e = side[q];
                        if (q > 0 && path.Count > 0) kinds[kinds.Count - 1] = Kind(side[q - 1].ci, side[q - 1].cj);
                        path.Add(sh.I(e.i, e.j)); inward.Add(In(e.i, e.j)); kinds.Add(Kind(e.ci, e.cj));
                    }
                    if (n < 3 && on[(k + 1) % 4]) corners.Add(path.Count - 1);
                }
                if (all) { corners.Add(path.Count - 1); }
                Flush();
            }

            // Every sheet closed all round: the free sail (the leading edge along finger 0, round the tip, the trailing edge,
            // down the trailing rib, back along its inner edge near the wrist), the aft sail and the arm zone likewise, each
            // flank patch along its radial edges and its flank edge. The skins never pinch onto the wrist: their inner edge
            // is a rim inside the club and the forearm's swelling, so no hole opens whatever covers it.
            void Rims(Sheet free, Sheet aft, Sheet arm, List<Sheet> pats)
            {
                if (NoRimsForTest) return;
                Outline(free, true, true, true, true);
                Outline(aft, true, true, true, true);
                Outline(arm, true, true, true, true);
                foreach (Sheet q in pats) Outline(q, true, true, true, false);   // row 0 is the elbow's pole
            }

            // ---------------------------------------------------------- the arm and the pad

            static PartMeshEmitter.TubeSample S(Vector3 c, Vector3 t, float r, float dr, float s, Vector4 skin, bool pole = false)
                => PartMeshEmitter.Sample(c, t, r, dr, s, skin, pole);

            // the old arm (RED hook TubeArmForTest only): a round humerus tube out of the chest, a ball elbow, a round forearm
            // tapering to the pad, each rigid to its leg bone - the tube the folded blade's lower half read as
            void TubeArm(Vector3 S0, Vector3 E, Vector3 Wr, Sheet fan)
            {
                float lu = Mathf.Max(1e-4f, (E - S0).magnitude), ll = Mathf.Max(1e-4f, (Wr - E).magnitude);
                Vector3 du = (E - S0) / lu, dl = (Wr - E) / ll;
                ArmRadii(fan, out float r0, out float rE, out float rW, out float rC);
                Vector3 dors = cw.State[w].handNormal;
                {
                    var q = new List<PartMeshEmitter.TubeSample>();
                    float R(float f) => Mathf.Lerp(r0, rE, f) * (1f + 0.15f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(f)));   // the muscle's swell
                    q.Add(S(S0 - du * r0, du, 0f, 0f, 0f, PartMeshEmitter.Rigid(bu), true));
                    q.Add(S(S0 - du * (0.6f * r0), du, 0.8f * r0, 0.8f, 0.4f * r0, PartMeshEmitter.Rigid(bu)));
                    for (int i = 0; i <= 6; i++)
                    {
                        float f = i / 6f;
                        q.Add(S(S0 + du * (f * lu), du, R(f), (R(Mathf.Min(1f, f + 0.01f)) - R(Mathf.Max(0f, f - 0.01f))) / (0.02f * lu), r0 + f * lu, PartMeshEmitter.Rigid(bu)));
                    }
                    q.Add(S(E + du * (0.6f * rE), du, 0.8f * rE, -0.8f, r0 + lu + 0.6f * rE, PartMeshEmitter.Rigid(bu)));
                    q.Add(S(E + du * rE, du, 0f, 0f, r0 + lu + rE, PartMeshEmitter.Rigid(bu), true));
                    em.Tube(q, PartMeshEmitter.Unit(Vector3.Cross(du, dors), Vector3.right), kArmSides, PartMeshEmitter.PartBody);
                }
                {
                    Matrix4x4 f = Matrix4x4.TRS(E, Quaternion.LookRotation(dl, PartMeshEmitter.Unit(dors - dl * Vector3.Dot(dors, dl), Vector3.up)), Vector3.one);
                    em.Ellipsoid(f, Vector3.zero, rE * 1.05f, rE * 1.05f, rE * 1.1f, kArmSides, 7, PartMeshEmitter.PartBody, bl);
                }
                {
                    // the forearm, its lower clubLength swelling into the club (the sail rolled round the arm's end), closed at
                    // the wrist itself (nothing of it below the wrist: the hand's club carries the end)
                    var q = new List<PartMeshEmitter.TubeSample>();
                    float cf = Mathf.Clamp(p.clubLength / ll, 0.1f, 0.9f);
                    float R(float f) => f <= 1f - cf ? Mathf.Lerp(rE, rW, f / (1f - cf)) : Mathf.Lerp(rW, rC, Mathf.SmoothStep(0f, 1f, (f - (1f - cf)) / cf));
                    float end = Mathf.Max(0.05f, 1f - 0.6f * rC / ll);   // the dome's start
                    q.Add(S(E - dl * (0.5f * rE), dl, 0f, 0f, 0f, PartMeshEmitter.Rigid(bl), true));
                    q.Add(S(E - dl * (0.3f * rE), dl, 0.8f * rE, 1f, 0.2f * rE, PartMeshEmitter.Rigid(bl)));
                    for (int i = 0; i <= 10; i++)
                    {
                        float f = end * i / 10f;
                        float dr = (R(Mathf.Min(1f, f + 0.01f)) - R(Mathf.Max(0f, f - 0.01f))) / (0.02f * ll);
                        q.Add(S(E + dl * (f * ll), dl, R(f), dr, 0.5f * rE + f * ll, PartMeshEmitter.Rigid(bl)));
                    }
                    float re = R(end), le = (1f - end) * ll;
                    q.Add(S(Wr - dl * (0.45f * le), dl, 0.8f * re, -0.8f, 0.5f * rE + ll - 0.45f * le, PartMeshEmitter.Rigid(bl)));
                    q.Add(S(Wr, dl, 0f, 0f, 0.5f * rE + ll, PartMeshEmitter.Rigid(bl), true));
                    em.Tube(q, PartMeshEmitter.Unit(Vector3.Cross(dl, dors), Vector3.right), kArmSides, PartMeshEmitter.PartBody);
                }
            }

            // The arm's radii (the old tube's): never thinner than the sail's skins that end inside it (the arm zone's
            // forearm edge, its rim's bead): r0 at the shoulder, rE at the elbow, rW at the wrist, rC the club's swelling.
            void ArmRadii(Sheet fan, out float r0, out float rE, out float rW, out float rC)
            {
                float hArm = 0f;
                for (int j = 0; j < rowV.Length; j++) hArm = Mathf.Max(hArm, fan.H[fan.I(fan.Cols, j)]);
                r0 = Mathf.Max(p.armBulk, hArm + 0.004f); rE = Mathf.Max(0.8f * p.armBulk, hArm + 0.004f); rW = Mathf.Max(0.55f * p.armBulk, hArm + 0.004f);
                rC = Mathf.Max(rW, kClubArm * p.padRadius);
            }

            // the soft bend's tangent length a on either side of the elbow (x rE, at most these shares of the humerus / the
            // forearm); the inner crease's radius x the bend's radius; the elbow's blend fades to the rings' own frames from
            // (a + rE) to kElbowFade x that from the joint, and down the forearm by kForeFade x rE past the bend (where the
            // leaf's roll toward the sail starts, turning on to the club's top: a shorter turn shears the leaf's tail through
            // its triangles in a launch - scanned); the leaf grows to full over kGrow x rE below the bend; near the hinge axis
            // (kAxisNear .. kAxisFar x rE off it) the rings' shares along the bend; the poles' hair of the arm's leg bones
            const float kElbowRound = 1.3f, kElbowUpper = 0.45f, kElbowLower = 0.25f, kCrease = 0.85f, kElbowFade = 1.6f;
            const float kForeFade = 0.5f, kGrow = 1.5f;
            const float kAxisNear = 0.25f, kAxisFar = 0.75f, kLegHair = 1e-3f;
            const int kLeafSides = 20;

            struct ArmRing { public Vector3 c; public Quaternion q; public float r, chord, rin, warc, x, tw; public Vector3 inner; public bool fore, pole; public float s; }

            // The arm's flesh: ONE loft from inside the chest (the shoulder's pole) along the humerus, round the elbow in a soft
            // bend (a circular fillet of the two bones' lines, its inner side creased so the bend never folds through itself),
            // down the forearm to a dome at the wrist inside the club. Its section is a leaf: a round edge of the arm's radius
            // round the bone (r: the leading edge, and both faces) whose far side runs on toward the sail as a half ellipse
            // (armChord x r from the bone): the leading edge's flesh rolled into the sheet; round along the humerus and the
            // bend, growing into the leaf from the bend down the forearm (a leaf's rounded upper end), full from there to the
            // club's top, rounding back into the club below it; its sail side coloured as the sail (ColourAt), its stripes
            // running down it. It rides the three flesh frames (ArmFleshBone): the humerus,
            // rigid; round the elbow a blend of the humerus and the elbow frames by the angle about the elbow's hinge axis (a
            // point's share of the elbow frame grows with its angle round the joint from the humerus' side to the forearm's: the
            // pair is a pure hinge, so the bend - whatever it does to the joint's angle - turns each point about the joint
            // monotonically: no triangle turns over, the inner crease folds as one); down the forearm, below the elbow's blend,
            // the elbow frame turning into the forearm's to the club's top (the leaf's roll on toward the sail, at most a radian),
            // the forearm's from there. Its two poles lie on the humerus' and the forearm's axes and carry a hair (kLegHair) of
            // the arm's upper and lower leg bones: every bone skinned (the kit's rule), nothing of their roll seen.
            void LeafArm(Vector3 S0, Vector3 E, Vector3 Wr, Sheet fan)
            {
                float lu = Mathf.Max(1e-4f, (E - S0).magnitude), ll = Mathf.Max(1e-4f, (Wr - E).magnitude);
                Vector3 du = (E - S0) / lu, dl = (Wr - E) / ll;
                ArmRadii(fan, out float r0, out float rE, out float rW, out float rC);
                int bH = p.ArmFleshBone(extra, 0), bE = p.ArmFleshBone(extra, 1), bF = p.ArmFleshBone(extra, 2);
                Quaternion Rot(int b) => Quaternion.LookRotation(em.Bones[b].GetColumn(2), em.Bones[b].GetColumn(1));
                Quaternion qH = Rot(bH), qE = Rot(bE), qF = Rot(bF);
                // the bend: tangent length a on either line, the fillet's radius rho about O (the inside of the turn)
                float beta = Vector3.Angle(du, dl) * Mathf.Deg2Rad;
                float a = Mathf.Min(kElbowRound * rE, kElbowUpper * lu, kElbowLower * ll);
                Vector3 A = E - du * a, B = E + dl * a;
                Vector3 inA = PartMeshEmitter.Unit(dl - du * Vector3.Dot(dl, du), Vector3.Cross(du, Vector3.right));
                float rho = beta > 1e-3f ? a / Mathf.Tan(0.5f * beta) : 1e3f;
                Vector3 O = A + inA * rho, inB = PartMeshEmitter.Unit(O - B, inA);
                Vector3 axis = PartMeshEmitter.Unit(Vector3.Cross(du, dl), Vector3.right);
                float rinMin = Mathf.Min(rE, kCrease * rho);
                // the club's top up the forearm from the wrist (the leaf rounds back into the club below it); the dome's length
                GroundFrameInBind(out Vector3 cup, out _);
                float Lc = Mathf.Max(p.clubLength, 2.2f * p.padRadius);
                float dDome = Mathf.Min(0.6f * rC, 0.3f * ll);
                float dTop = Mathf.Clamp(Vector3.Dot(cw.ThumbTip(w) + cup * Lc - Wr, -dl), dDome + 0.25f * rC, 0.5f * ll);
                float Rf(float x)
                {
                    float f = x / ll, cf = Mathf.Clamp(p.clubLength / ll, 0.1f, 0.9f);
                    return f <= 1f - cf ? Mathf.Lerp(rE, rW, f / (1f - cf)) : Mathf.Lerp(rW, rC, Mathf.SmoothStep(0f, 1f, (f - (1f - cf)) / cf));
                }
                float Rh(float x) { float f = x / lu; return Mathf.Lerp(r0, rE, f) * (1f + 0.15f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(f))); }
                // the inner crease's share near the bend: full on the fillet, easing out over rE along either line
                float Crease(float dist) => 1f - Mathf.SmoothStep(0f, 1f, dist / Mathf.Max(1e-4f, rE));
                var rings = new List<ArmRing>();
                float arc = 0f; Vector3 last = S0 - du * r0;
                void Add(Vector3 c, Quaternion q, float r, float chord, float crease, Vector3 inner, bool fore, bool pole = false, float warc = -1f, float x = -1f, float tw = 0f)
                {
                    arc += (c - last).magnitude; last = c;
                    rings.Add(new ArmRing { c = c, q = q, r = r, chord = Mathf.Max(r, chord), rin = Mathf.Lerp(r, Mathf.Min(r, rinMin), crease), inner = inner, fore = fore, s = arc, pole = pole, warc = warc >= 0f ? warc : fore ? 1f : 0f, x = x, tw = tw });
                }
                // the shoulder's pole inside the chest, then the humerus, round
                Add(S0 - du * r0, qH, 0f, 0f, 0f, inA, false, true);
                Add(S0 - du * (0.6f * r0), qH, 0.8f * r0, 0f, 0f, inA, false);
                const int nh = 4;
                for (int i = 0; i <= nh; i++)
                {
                    float x = (lu - a) * i / nh;
                    Add(S0 + du * x, qH, Rh(x), 0f, Crease(lu - a - x), inA, false);
                }
                // the bend: the fillet's rings turn about its axis, their frames the humerus' turned toward the forearm
                const int nb = 6;
                float rA = Rh(lu - a), rB = Rf(a);
                for (int i = 1; i < nb; i++)
                {
                    float t = (float)i / nb;
                    Quaternion turn = Quaternion.AngleAxis(beta * t * Mathf.Rad2Deg, axis);
                    Vector3 c = O + turn * (A - O);
                    Add(c, turn * qH, Mathf.Lerp(rA, rB, t), 0f, 1f, PartMeshEmitter.Unit(O - c, inA), t >= 0.5f, false, Mathf.SmoothStep(0f, 1f, t));
                }
                // the forearm: from the bend's end, the leaf growing out of the round over its rounded upper end, full down to
                // the club's top, then rounding back into the club; the elbow frame turning into the forearm's from the elbow's
                // blend's end (x0) to the club's top; the dome at the wrist (its pole on the lower leg bone's axis)
                float psi = Mathf.PI - beta, d1 = a + rE, d2 = kElbowFade * d1;
                float grow = Mathf.Min(kGrow * rE, 0.5f * ll);
                float xEnd = ll - dDome, x0 = Mathf.Min(a + kForeFade * rE, 0.6f * ll), x1 = Mathf.Max(x0 + 0.1f * ll, ll - dTop);
                const int nf = 14;
                for (int i = 0; i <= nf; i++)
                {
                    float x = Mathf.Lerp(a, xEnd, (float)i / nf);
                    float g = Mathf.SmoothStep(0f, 1f, (x - a) / grow) * Mathf.SmoothStep(0f, 1f, (ll - x - dDome) / Mathf.Max(1e-4f, dTop - dDome));
                    float tw = Mathf.SmoothStep(0f, 1f, (x - x0) / (x1 - x0));
                    float r = Rf(x);
                    Add(E + dl * x, Quaternion.Slerp(qE, qF, tw), r, r * Mathf.Lerp(1f, p.armChord, g), Crease(x - a), inB, true, false, -1f, x, tw);
                }
                float rEnd = Rf(xEnd);
                foreach (float deg in new[] { 40f, 65f, 82f })
                {
                    float th = deg * Mathf.Deg2Rad;
                    Add(E + dl * (xEnd + dDome * Mathf.Sin(th)), qF, rEnd * Mathf.Cos(th), 0f, 0f, inB, true, false, -1f, ll, 1f);
                }
                Add(Wr, qF, 0f, 0f, 0f, inB, true, true, -1f, ll, 1f);
                // the elbow's blend: the elbow frame's share of a point by its angle about the hinge axis through the joint,
                // from the humerus' side (-du, 0) to the forearm's (dl, 1) both ways round (the inner wedge psi, the outer
                // 2 pi - psi); near the hinge axis itself (where that angle is ill-defined; the hinge barely moves such a point)
                // the ring's share along the bend instead; fading to the ring's own frame from d1 = (a + rE) to d2 = kElbowFade
                // x that from the joint, and by ring along either bone (the humerus' back to half its straight part, the
                // forearm's to x0: below x0 every point is on the elbow frame, the roll toward the sail starts there - never
                // mixed with the humerus, two bones a vertex)
                Vector3 e0 = -du, e1 = PartMeshEmitter.Unit(dl - e0 * Vector3.Dot(dl, e0), inA);
                Vector4 Skin(Vector3 v, ArmRing g)
                {
                    if (g.fore && g.x >= x0) return g.tw <= 0f ? PartMeshEmitter.Rigid(bE) : g.tw >= 1f ? PartMeshEmitter.Rigid(bF) : PartMeshEmitter.Blend(bE, bF, g.tw);
                    float own = g.fore ? 1f : 0f;
                    Vector3 d = v - E; d -= axis * Vector3.Dot(d, axis);
                    float wf = g.warc;
                    if (d.sqrMagnitude > 1e-12f && psi > 1e-3f)
                    {
                        float th = Mathf.Atan2(Vector3.Dot(d, e1), Vector3.Dot(d, e0));
                        float polar = th >= 0f && th <= psi ? th / psi : (th < 0f ? -th : 2f * Mathf.PI - th) / (2f * Mathf.PI - psi);
                        wf = Mathf.Lerp(g.warc, polar, Mathf.SmoothStep(0f, 1f, (d.magnitude - kAxisNear * rE) / Mathf.Max(1e-5f, (kAxisFar - kAxisNear) * rE)));
                    }
                    float fade = Mathf.SmoothStep(0f, 1f, ((v - E).magnitude - d1) / Mathf.Max(1e-4f, d2 - d1));
                    if (!g.fore) fade = Mathf.Max(fade, Mathf.SmoothStep(0f, 1f, (Vector3.Dot(E - g.c, du) - a) / Mathf.Max(1e-4f, 0.5f * (lu - a))));
                    else if (g.x >= 0f) fade = Mathf.Max(fade, Mathf.SmoothStep(0f, 1f, (g.x - a) / Mathf.Max(1e-4f, x0 - a)));
                    wf = Mathf.Lerp(wf, own, fade);
                    return wf <= 1e-4f ? PartMeshEmitter.Rigid(bH) : wf >= 1f - 1e-4f ? PartMeshEmitter.Rigid(bE) : PartMeshEmitter.Blend(bH, bE, wf);
                }
                // the leaf's sail side coloured as the sail at the same distance from the wrist (v x the leading finger's
                // length), its stripes running down it (a third of the sail's across its tail)
                float L0 = Mathf.Max(1e-4f, ws.fingerLengths[0]);
                int leafStripes = Mathf.Max(1, Mathf.RoundToInt(p.stripes / 3f));
                int KindOf(int i, float cs)
                {
                    ArmRing g0 = rings[i], g1 = rings[i + 1];
                    if (!g0.fore || !g1.fore || g0.pole || g1.pole || cs <= kLeafSailSide) return kindBody;
                    if (0.5f * (g0.chord + g1.chord) < 1.2f * 0.5f * (g0.r + g1.r)) return kindBody;
                    float u = (cs - kLeafSailSide) / (1f - kLeafSailSide);
                    float d = ll - 0.5f * (g0.x + g1.x);
                    return ColourAt(p.stripes > 0 ? u * leafStripes / p.stripes : 0f, d / L0);
                }
                EmitLoft(rings, (v, i) => rings[i].pole ? PartMeshEmitter.Blend(i == 0 ? bH : bF, i == 0 ? bu : bl, kLegHair) : Skin(v, rings[i]), KindOf);
            }

            // the loft's rings: each a leaf section in its frame (+X: chord there, r the other way; +-Y the faces, r), its half
            // facing the bend's inside pressed to rin along that direction (the inner crease), vertex normals from the grid,
            // both ends poles; skin per vertex; a triangle starts at its vertex weighted most to the bone its vertices weigh
            // most in all
            void EmitLoft(List<ArmRing> rings, System.Func<Vector3, int, Vector4> skinOf, System.Func<int, float, int> kindOf)
            {
                int n = rings.Count, M = kLeafSides;
                var P = new Vector3[n, M];
                for (int i = 0; i < n; i++)
                {
                    ArmRing g = rings[i];
                    Vector3 X = g.q * Vector3.right, Y = g.q * Vector3.up;
                    // the inner crease: the half facing the bend's inside pressed toward the axis along that direction (to rin)
                    Vector3 inn = PartMeshEmitter.Unit(g.inner - g.q * Vector3.forward * Vector3.Dot(g.inner, g.q * Vector3.forward), Y);
                    float press = g.r > 1e-6f ? 1f - g.rin / g.r : 0f;
                    for (int k = 0; k < M; k++)
                    {
                        float ph = 2f * Mathf.PI * k / M, cs = Mathf.Cos(ph), sn = Mathf.Sin(ph);
                        Vector3 o = X * ((cs > 0f ? g.chord : g.r) * cs) + Y * (g.r * sn);
                        float into = Vector3.Dot(o, inn);
                        if (into > 0f) o -= inn * (into * press);
                        P[i, k] = g.pole ? g.c : g.c + o;
                    }
                }
                // per grid vertex: normal (from the grid) and skin; a vertex per kind of the cells around it
                var N = new Vector3[n, M];
                var S = new Vector4[n, M];
                for (int i = 0; i < n; i++)
                {
                    ArmRing g = rings[i];
                    Vector3 t = rings[Mathf.Min(n - 1, i + 1)].c - rings[Mathf.Max(0, i - 1)].c;
                    for (int k = 0; k < M; k++)
                    {
                        Vector3 nn;
                        if (g.pole) nn = PartMeshEmitter.Unit(i == 0 ? -t : t, Vector3.up);
                        else
                        {
                            Vector3 around = P[i, (k + 1) % M] - P[i, (k + M - 1) % M];
                            Vector3 along = P[Mathf.Min(n - 1, i + 1), k] - P[Mathf.Max(0, i - 1), k];
                            nn = PartMeshEmitter.Unit(Vector3.Cross(along, around), P[i, k] - g.c);
                            if (Vector3.Dot(nn, P[i, k] - g.c) < 0f) nn = -nn;
                        }
                        N[i, k] = nn; S[i, k] = skinOf(P[i, k], i);
                    }
                }
                var made = new Dictionary<long, int>();
                int V(int i, int k, int kind)
                {
                    long key = ((long)(i * (M + 1) + k) << 8) | (long)kind;
                    if (made.TryGetValue(key, out int idx)) return idx;
                    int kk = k % M;
                    idx = em.VertexCount;
                    em.Vertex(P[i, kk], N[i, kk], rings[i].s, (float)k / M, kind, 1f, S[i, kk]);
                    made[key] = idx;
                    return idx;
                }
                // the bone a triangle's vertices weigh most in all
                int Home(int a, int b, int c)
                {
                    int best = -1; float most = -1f;
                    foreach (int v in new[] { a, b, c })
                    {
                        Vector4 s = em.Lists.uv2[v];
                        foreach (int bone in new[] { (int)s.x, (int)s.y })
                        {
                            float sum = Weight(a, bone) + Weight(b, bone) + Weight(c, bone);
                            if (sum > most) { most = sum; best = bone; }
                        }
                    }
                    return best;
                }
                for (int i = 0; i < n - 1; i++)
                    for (int k = 0; k < M; k++)
                    {
                        int kind = kindOf(i, Mathf.Cos(2f * Mathf.PI * (k + 0.5f) / M));
                        int a = V(i, k, kind), b = V(i, k + 1, kind), d = V(i + 1, k, kind), e = V(i + 1, k + 1, kind);
                        Vector3 mid = 0.25f * (em.Position(a) + em.Position(b) + em.Position(d) + em.Position(e));
                        Vector3 c = Vector3.Lerp(rings[i].c, rings[i + 1].c, 0.5f);
                        Vector3 o = PartMeshEmitter.Unit(em.Normal(a) + em.Normal(b) + em.Normal(d) + em.Normal(e), mid - c);
                        if (!rings[i].pole) Tri(a, b, d, o, Home(a, b, d));
                        if (!rings[i + 1].pole) Tri(b, e, d, o, Home(b, e, d));
                    }
            }

            // The club the folded wing stands on: the hand's end of the rolled sail, rigid to the hand bone, along the rest
            // wing-walk's up (the hand bone's frame on the ground, GroundFrameInBind) from the walked contact
            // (CreatureWings.ThumbTip, planted by WingSpec.thumbPlant) up past the wrist into the forearm's swelling: a blunt
            // cone (its tip, rounded small, the contact: it stays the lowest point while the planted hand turns), padRadius
            // wide, closed by a dome clubLength above the contact; an optional hook curling forward and down off its front.
            void Club()
            {
                Vector3 tip = cw.ThumbTip(w);
                GroundFrameInBind(out Vector3 up, out Vector3 fwd);
                float R = p.padRadius, Lc = Mathf.Max(p.clubLength, 2.2f * R), rt = kClubTip * R;
                float sc = (R - rt) / Mathf.Tan(kClubConeDeg * Mathf.Deg2Rad);   // the cone's height
                var q = new List<PartMeshEmitter.TubeSample>();
                Vector4 skin = PartMeshEmitter.Rigid(bh);
                float Rad(float x)
                {
                    if (x <= rt) return rt * Mathf.Sqrt(Mathf.Max(0f, 1f - (rt - x) * (rt - x) / (rt * rt)));   // the rounded tip
                    if (x <= rt + sc) return rt + (x - rt) / sc * (R - rt);
                    float top = Lc - R;
                    if (x <= top) return R;
                    float y = (x - top) / R;
                    return R * Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));                                         // the dome
                }
                q.Add(S(tip, up, 0f, 0f, 0f, skin, true));
                float[] at = { 0.3f * rt, rt, rt + 0.35f * sc, rt + 0.7f * sc, rt + sc, 0.5f * (rt + sc + Lc - R), Lc - R, Lc - 0.5f * R, Lc - 0.15f * R };
                foreach (float x in at)
                {
                    float dr = (Rad(x + 1e-3f) - Rad(Mathf.Max(0f, x - 1e-3f))) / 2e-3f;
                    q.Add(S(tip + up * x, up, Mathf.Max(1e-4f, Rad(x)), Mathf.Clamp(dr, -4f, 4f), x, skin));
                }
                q.Add(S(tip + up * Lc, up, 0f, 0f, Lc, skin, true));
                em.Tube(q, PartMeshEmitter.Unit(Vector3.Cross(up, fwd), Vector3.right), kArmSides, PartMeshEmitter.PartBody);
                if (p.hookLength > 0f)
                {
                    // the hook: off the club's front, high enough that its curl (a quarter turn) stays above the contact
                    float h = 0.4f * R + 1.1f * p.hookLength;   // a quarter turn drops ~0.64 x its length; a planted club tilts
                    float rise = (h - rt) / sc;
                    float rr = h <= rt + sc ? rt + Mathf.Clamp01(rise) * (R - rt) : R;
                    Vector3 root = tip + up * h + fwd * (0.8f * rr);
                    Vector3 dir = fwd;
                    Vector3 hu = PartMeshEmitter.Unit(up - dir * Vector3.Dot(up, dir), up);
                    em.Horn(root, Quaternion.LookRotation(dir, hu), kHookRoot * R, 0.04f * R, p.hookLength, 90f, bh, PartMeshEmitter.PartFoot, 8, 0f, out _, out _);
                }
            }

            // the rest wing-walk's world up and the girdle's forward on the ground, carried into the bind by the hand bone
            void GroundFrameInBind(out Vector3 up, out Vector3 fwd)
            {
                CreatureSim rest = em.Rest;
                var g = new CreatureWings(sp, sp.wings, rest.Joints, rest.Rotations, 1f);
                LegSpec arm = sp.legs[ws.leg];
                Quaternion r = rest.Rotations[arm.attachJoint];
                Vector3 so = arm.shoulderOffset; so.x *= arm.side;
                Vector3 sh = rest.Joints[arm.attachJoint] + r * so, foot = rest.Legs[ws.leg].foot;
                Vector3 wt = foot + Vector3.up * ws.groundWristLift;
                Vector3 el = TwoBoneIK.Solve(sh, wt, arm.upperLength, arm.lowerLength, g.ArmPole(w), out Vector3 wr);
                g.PoseOnArm(w, sh, el, wr, 1f);
                for (int pass = 0; pass < 4; pass++)
                {
                    wt -= g.ThumbTip(w) - foot;
                    el = TwoBoneIK.Solve(sh, wt, arm.upperLength, arm.lowerLength, g.ArmPole(w), out wr);
                    g.PoseOnArm(w, sh, el, wr, 1f);
                }
                Vector3 o = g.HandBoneOut[w], n = g.State[w].handNormal;
                Matrix4x4 hand = Matrix4x4.TRS(wr, Quaternion.LookRotation(o, PartMeshEmitter.Unit(n - o * Vector3.Dot(n, o), Vector3.up)), Vector3.one);
                Matrix4x4 inv = hand.inverse;
                up = PartMeshEmitter.Unit(em.Bones[bh].MultiplyVector(inv.MultiplyVector(Vector3.up)), cw.State[w].handNormal);
                Vector3 gf = Vector3.ProjectOnPlane(r * Vector3.forward, Vector3.up);
                fwd = em.Bones[bh].MultiplyVector(inv.MultiplyVector(PartMeshEmitter.Unit(gf, Vector3.forward)));
                fwd = PartMeshEmitter.Unit(fwd - up * Vector3.Dot(fwd, up), Vector3.Cross(up, Vector3.right));
            }
        }

        // Span (0 at the body .. 1 at the tip) and chord (0 leading .. 1 trailing edge) of a point on the SPREAD wing: its
        // lateral and fore-aft place in the girdle frame against the wing's extent there (binned over the spread samples).
        sealed class SpanChord
        {
            const int Bins = 40;
            readonly Vector3 g, right, fwd;
            readonly float lat0, lat1;
            readonly float[] lead = new float[Bins], trail = new float[Bins];

            public SpanChord(CreatureSim rest, int joint, int side, Vector3[] a, Vector3[] b)
            {
                g = rest.Joints[joint];
                Quaternion r = rest.Rotations[joint];
                right = r * Vector3.right * side; fwd = r * Vector3.forward;
                lat0 = float.MaxValue; lat1 = float.MinValue;
                foreach (Vector3 q in a) { float x = Lat(q); lat0 = Mathf.Min(lat0, x); lat1 = Mathf.Max(lat1, x); }
                foreach (Vector3 q in b) { float x = Lat(q); lat0 = Mathf.Min(lat0, x); lat1 = Mathf.Max(lat1, x); }
                if (!(lat1 > lat0 + 1e-4f)) lat1 = lat0 + 1e-4f;
                for (int i = 0; i < Bins; i++) { lead[i] = float.MinValue; trail[i] = float.MaxValue; }
                void Add(Vector3 q)
                {
                    int bi = Mathf.Clamp(Mathf.FloorToInt((Lat(q) - lat0) / (lat1 - lat0) * Bins), 0, Bins - 1);
                    float f = Vector3.Dot(q - g, fwd);
                    lead[bi] = Mathf.Max(lead[bi], f); trail[bi] = Mathf.Min(trail[bi], f);
                }
                foreach (Vector3 q in a) Add(q);
                foreach (Vector3 q in b) Add(q);
                // empty bins: from the nearest filled ones
                for (int i = 0; i < Bins; i++)
                {
                    if (lead[i] > float.MinValue) continue;
                    int lo = i - 1, hi = i + 1;
                    while (lo >= 0 && lead[lo] == float.MinValue) lo--;
                    while (hi < Bins && lead[hi] == float.MinValue) hi++;
                    int src = lo >= 0 ? lo : hi;
                    if (src >= 0 && src < Bins) { lead[i] = lead[src]; trail[i] = trail[src]; } else { lead[i] = 0f; trail[i] = -1f; }
                }
            }

            float Lat(Vector3 q) => Vector3.Dot(q - g, right);

            public void At(Vector3 q, out float span, out float chord)
            {
                float x = (Lat(q) - lat0) / (lat1 - lat0);
                span = Mathf.Clamp01(x);
                float fb = Mathf.Clamp(x * Bins - 0.5f, 0f, Bins - 1);
                int i0 = Mathf.FloorToInt(fb), i1 = Mathf.Min(Bins - 1, i0 + 1);
                float t = fb - i0;
                float l = Mathf.Lerp(lead[i0], lead[i1], t), tr = Mathf.Lerp(trail[i0], trail[i1], t);
                float f = Vector3.Dot(q - g, fwd);
                chord = Mathf.Clamp01((l - f) / Mathf.Max(1e-3f, l - tr));
            }
        }
    }
}
