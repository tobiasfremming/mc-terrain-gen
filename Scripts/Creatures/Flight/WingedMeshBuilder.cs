using System.Collections.Generic;
using CreatureKit;
using UnityEngine;
using UnityEngine.Rendering;

// Skinned mesh of a winged species (design section 6), built once in the rest pose by the creature kit's part
// emitter (Kit/PartMeshEmitter: every part draws itself; the body, hind legs, eyes, cones, fins and antennae as
// for any species, on the full species' layout). The kit's Wing part draws its wing here (EmitWing):
//   wing arms               tubes shoulder -> rounded elbow -> wrist, a wrist knuckle and a thumb claw
//   fingers                 tapered tubes along the finger bones (3..5, WingSpec.FingerCount)
//   membrane (part 8)       double-sided: a fan from the wrist over F + 2 zones between neighbouring rays
//                           (finger 1-2, .., (F-1)-F, finger F-trailing rib, trailing rib-rear flank rib,
//                           rear flank rib-forearm; 5 with 3 fingers), each a 6 x 8 grid skinned to its two rays (weights by
//                           the angular coordinate) and smooth-shaded as one sheet (normals averaged
//                           across zone borders), then the humerus patch, a fan from the elbow between
//                           the last zone's far edge and the humerus out to the flank. The free trailing
//                           edge is scalloped; the leading edge runs along the arm, shoulder -> elbow ->
//                           wrist, and stays on it however the elbow flexes (user feedback 2026-10-18):
//                           the forearm zone rides the arm sector (CreatureWings.ArmSector, exact on the
//                           forearm), the patch blends it into the humerus.
// Rest pose = the bind pose: CreatureRestPose for the spine and hind legs, the wings spread (stroke angle
// 0, unfolded) on the same spine. The ribs' uniform scale (CreatureWings.WriteBones) leaves the membrane
// normals exact.
// Vertex layout as CreatureMeshBuilder: uv0 (distance m, angle 0..1), uv1 (part, dorsal), uv2 (b0, b1, w0, w1).
public static class WingedMeshBuilder
{
    // Part ids 4-7 belong to the octopus (arm, web, mantle, siphon); the membrane is 8 (CREATURE_PART_MEMBRANE).
    public const int PartMembrane = 8;
    public const int MembraneZones = 5, ZoneColumns = 6, ZoneRows = 8;   // MembraneZones: a 3-finger wing's
    public static int MembraneZonesFor(WingSpec ws) => WingBoneLayout.FingerCount(ws) + 2;
    // the five fan zones and the humerus patch, two sheets each (the one humerus patch: a zone block, transposed)
    public const int MembraneVerticesPerWing = (MembraneZones + 1) * (ZoneColumns + 1) * (ZoneRows + 1) * 2;
    // columns per flank patch when the humerus patch is split along the flank (WingSpec.flankPatches > 1)
    public const int PatchColumns = 2;
    public static int PatchColumnsFor(int patches) => patches > 1 ? PatchColumns : ZoneRows;
    // the five fan zones and the flank patches, two sheets each
    public static int MembraneVertices(WingSpec ws)
    {
        int P = WingBoneLayout.FlankPatchCount(ws), C = ColumnsFor(ws), R = RowsFor(ws);
        return MembraneZonesFor(ws) * (C + 1) * (R + 1) * 2 + P * (C + 1) * (PatchColumnsFor(P) + 1) * 2;
    }
    // a zone's grid (WingSpec.membraneDetail: finer both ways)
    public static int Detail(WingSpec ws) => Mathf.Clamp(ws.membraneDetail, 1, 3);
    public static int ColumnsFor(WingSpec ws) => ZoneColumns * Detail(ws);
    public static int RowsFor(WingSpec ws) => ZoneRows * Detail(ws);

    const int kTubeSegments = 8, kFingerSegments = 6, kKneeRings = 7;
    const float kShoulderBlob = 0.6f, kWristCap = 0.8f;

    // Bone world matrices of the rest (bind) pose, WingBoneLayout order.
    public static Matrix4x4[] RestBoneWorld(CreatureSpecies sp, WingSpec[] wings)
    {
        CreatureSim sim = CreatureRestPose.Sim(sp);
        StraightenBind(sp, wings, sim);
        return RestBoneWorld(sp, wings, sim, out _);
    }

    // A tall stance (WingSpec.groundTallStance): the rest sim's spine straight at the lowest leg attach's
    // clearance (legs replanted from there) - the bind pose is the spread glide pose, not the stance's bent back.
    internal static void StraightenBind(CreatureSpecies sp, WingSpec[] wings, CreatureSim sim)
    {
        if (!CreatureWings.StraightBind(sp, wings) || wings.Length == 0) return;
        // at the lowest leg attach's clearance (the hips): the walking legs keep their stance bend in the bind
        float h = float.MaxValue;
        for (int l = 0; l < sp.legs.Length; l++) h = Mathf.Min(h, sp.joints[sp.legs[l].attachJoint].clearance);
        for (int i = 0; i < sim.Joints.Length; i++) sim.Joints[i].y = h;
        sim.ResetGround(0f);
    }

    public static Matrix4x4[] BindPoses(CreatureSpecies sp, WingSpec[] wings)
    {
        Matrix4x4[] m = RestBoneWorld(sp, wings);
        for (int i = 0; i < m.Length; i++) m[i] = m[i].inverse;
        return m;
    }

    internal static Matrix4x4[] RestBoneWorld(CreatureSpecies sp, WingSpec[] wings, CreatureSim sim, out CreatureWings cw)
    {
        // the sim writes spine, legs and eyes (CreatureSpecies' layout is WingBoneLayout); the spread wings
        // override the arms' leg bones and write the finger and rib bones
        var m = new Matrix4x4[sp.BoneCount];
        sim.WriteBoneWorld(m, 0);
        cw = new CreatureWings(sp, wings, sim.Joints, sim.Rotations, 1f);
        cw.WriteBones(m, 0);
        return m;
    }

    // A winged creature's bones from its sim (spine, legs, eyes) with these wings on top (tests).
    public static void WriteBones(CreatureSim sim, CreatureWings wings, Matrix4x4[] dst, int offset = 0)
    {
        sim.WriteBoneWorld(dst, offset);
        wings.WriteBones(dst, offset);
    }

    public static Mesh Build(CreatureSpecies sp) => PartMeshEmitter.Build(sp);
    public static Matrix4x4[] BindPoses(CreatureSpecies sp) => BindPoses(sp, sp.wings);

    // wings: the species' own (sp.wings; the test factories pass them explicitly).
    public static Mesh Build(CreatureSpecies sp, WingSpec[] wings) => PartMeshEmitter.Build(sp, wings);

    // The kit's Wing part w (CreaturePart.EmitMesh): arm, fingers and membrane on the emitter's spread rest pose.
    internal static void EmitWing(PartMeshEmitter m, int w)
    {
        var b = new Builder(m);
        b.Arm(w); b.Fingers(w); b.Membrane(w);
    }

    sealed class Builder
    {
        readonly CreatureSpecies sp;
        readonly WingSpec[] wings;
        readonly CreatureWings cw;
        readonly Matrix4x4[] bones;
        readonly int n, legs;
        readonly List<Vector3> pos, nrm;
        readonly List<Vector2> uv0, uv1;
        readonly List<Vector4> uv2;
        readonly List<int> tris;

        public Builder(PartMeshEmitter m)
        {
            sp = m.Species; wings = m.WingSpecs; cw = m.Wings; bones = m.Bones;
            n = sp.JointCount; legs = sp.legs != null ? sp.legs.Length : 0;
            pos = m.Lists.pos; nrm = m.Lists.nrm; uv0 = m.Lists.uv0; uv1 = m.Lists.uv1; uv2 = m.Lists.uv2; tris = m.Lists.tris;
        }

        // ---------------------------------------------------------------- primitives (after CreatureMeshBuilder)

        void Vertex(Vector3 p, Vector3 normal, float u, float v, int part, float dorsal, Vector4 skin)
        {
            pos.Add(p); nrm.Add(normal);
            uv0.Add(new Vector2(u, v)); uv1.Add(new Vector2(part, Mathf.Clamp(dorsal, -1f, 1f))); uv2.Add(skin);
        }

        static Vector4 Rigid(int bone) => new Vector4(bone, bone, 1f, 0f);
        static Vector4 Blend(int b0, int b1, float w1) => w1 > 0.5f ? new Vector4(b1, b0, w1, 1f - w1) : new Vector4(b0, b1, 1f - w1, w1);

        void Tri(int i0, int i1, int i2, bool flip)
        {
            tris.Add(i0);
            if (flip) { tris.Add(i2); tris.Add(i1); } else { tris.Add(i1); tris.Add(i2); }
        }

        struct Sample { public Vector3 c, t; public float r, dr, s; public Vector4 skin; public bool pole; }

        // A tube through the samples: circular rings in a rotation-minimising frame started from x0, poles
        // at the flagged ends. Outward winding (ring basis x, y = t x x).
        void Tube(List<Sample> q, Vector3 x0, int m, int part)
        {
            Vector3 x = x0;
            int prev = -1; bool prevPole = false;
            for (int i = 0; i < q.Count; i++)
            {
                Sample a = q[i];
                Vector3 xt = x - a.t * Vector3.Dot(x, a.t);
                x = xt.sqrMagnitude > 1e-10f ? xt.normalized : x;
                Vector3 y = Vector3.Cross(a.t, x);
                int cur = pos.Count;
                if (a.pole)
                {
                    Vector3 pn = i == 0 ? -a.t : a.t;
                    for (int k = 0; k <= m; k++) Vertex(a.c, pn, a.s, (k + 0.5f) / m, part, pn.y, a.skin);
                }
                else
                    for (int k = 0; k <= m; k++)
                    {
                        float phi = 2f * Mathf.PI * k / m;
                        float cs = k == m ? 1f : Mathf.Cos(phi), sn = k == m ? 0f : Mathf.Sin(phi);
                        Vector3 radial = x * cs + y * sn;
                        Vector3 nn = (radial - a.t * a.dr).normalized;
                        Vertex(a.c + radial * Mathf.Max(1e-4f, a.r), nn, a.s, (float)k / m, part, nn.y, a.skin);
                    }
                if (prev >= 0)
                    for (int k = 0; k < m; k++)
                    {
                        if (!prevPole) Tri(prev + k, prev + k + 1, cur + k, false);
                        if (!a.pole) Tri(prevPole ? prev + k : prev + k + 1, cur + k + 1, cur + k, false);
                    }
                prev = cur; prevPole = a.pole;
            }
        }

        static Sample S(Vector3 c, Vector3 t, float r, float dr, float s, Vector4 skin, bool pole = false) =>
            new Sample { c = c, t = t, r = r, dr = dr, s = s, skin = skin, pole = pole };

        // ---------------------------------------------------------------- arm, fingers

        public void Arm(int w)
        {
            WingSpec ws = wings[w];
            LegSpec arm = sp.legs[ws.leg];
            WingState st = cw.State[w];
            int bu = WingBoneLayout.LegBone(n, ws.leg, 0), bl = WingBoneLayout.LegBone(n, ws.leg, 1), bh = WingBoneLayout.LegBone(n, ws.leg, 2);
            Vector3 S0 = st.shoulder, E = st.elbow, Wr = st.wrist;
            float lu = Mathf.Max(1e-4f, (E - S0).magnitude), ll = Mathf.Max(1e-4f, (Wr - E).magnitude);
            Vector3 du = (E - S0) / lu, dl = (Wr - E) / ll;
            float ru = Mathf.Max(1e-4f, arm.upperRadius), rl = Mathf.Max(1e-4f, arm.lowerRadius);
            float blob = kShoulderBlob * ru, cap = kWristCap * rl, total = lu + ll;
            float drLimb = (rl - ru) / total;
            var q = new List<Sample>();
            Vector4 shoulderSkin = Blend(arm.attachJoint, bu, 0.5f);
            q.Add(S(S0 - du * blob, du, 0f, 0f, 0f, shoulderSkin, true));
            for (int k = 1; k <= 2; k++)
            {
                float a = (90f - 30f * k) * Mathf.Deg2Rad; // 60, 30 deg: rounded end buried in the body
                float r = ru * Mathf.Cos(a);
                q.Add(S(S0 - du * (blob * Mathf.Sin(a)), du, r, Mathf.Tan(a) * ru / blob, blob - blob * Mathf.Sin(a), shoulderSkin));
            }
            q.Add(S(S0, du, ru, drLimb, blob, shoulderSkin));
            q.Add(S(S0 + du * (0.4f * lu), du, Mathf.Lerp(ru, rl, 0.4f * lu / total), drLimb, blob + 0.4f * lu, Rigid(bu)));
            float kb = arm.KneeBlend;
            Vector3 k0 = S0 + du * ((1f - kb) * lu), k2 = E + dl * (kb * ll);
            for (int i = 0; i < kKneeRings; i++)
            {
                float t = i / (kKneeRings - 1f), wt = t * t * (3f - 2f * t);
                Vector3 c = (1f - t) * (1f - t) * k0 + 2f * (1f - t) * t * E + t * t * k2;
                Vector3 tan = 2f * (1f - t) * (E - k0) + 2f * t * (k2 - E);
                tan = tan.sqrMagnitude > 1e-12f ? tan.normalized : du;
                float sg = Mathf.Lerp((1f - kb) * lu, lu + kb * ll, t);
                q.Add(S(c, tan, Mathf.Lerp(ru, rl, sg / total), drLimb, blob + sg, Blend(bu, bl, wt)));
            }
            q.Add(S(E + dl * (0.6f * ll), dl, Mathf.Lerp(ru, rl, (lu + 0.6f * ll) / total), drLimb, blob + lu + 0.6f * ll, Rigid(bl)));
            q.Add(S(Wr, dl, rl, 0f, blob + total, Blend(bl, bh, 0.5f)));
            for (int k = 1; k <= 2; k++)
            {
                float a = 30f * k * Mathf.Deg2Rad;
                q.Add(S(Wr + dl * (cap * Mathf.Sin(a)), dl, rl * Mathf.Cos(a), -Mathf.Tan(a) * rl / cap, blob + total + cap * Mathf.Sin(a), Rigid(bh)));
            }
            q.Add(S(Wr + dl * cap, dl, 0f, 0f, blob + total + cap, Rigid(bh), true));
            Tube(q, bones[bu].rotation * Vector3.right, kTubeSegments, CreatureMeshBuilder.PartLeg);

            // thumb claw: a short curved cone off the wrist's leading edge, rigid to the hand
            Vector3 fwd = -cw.Side(w) * Vector3.Cross(st.handNormal, st.handOut); // the leading edge
            Vector3 dir = (fwd * 0.8f + st.handNormal * 0.3f - dl * 0.2f).normalized;
            float len = Mathf.Max(1e-3f, ws.thumbLength), r0 = 0.45f * rl;
            Vector3 root = Wr + fwd * (0.6f * rl);
            var c2 = new List<Sample>();
            c2.Add(S(root - dir * (0.5f * r0), dir, 0f, 0f, 0f, Rigid(bh), true));
            for (int k = 0; k < 3; k++)
            {
                float t = k / 3f;
                Vector3 bend = -st.handNormal * (0.25f * len * t * t);
                c2.Add(S(root + dir * (len * t) + bend, (dir - st.handNormal * (0.5f * t)).normalized, r0 * (1f - 0.85f * t), -r0 * 0.85f / len, len * t, Rigid(bh)));
            }
            c2.Add(S(root + dir * len - st.handNormal * (0.25f * len), (dir - st.handNormal * 0.5f).normalized, 0f, 0f, len, Rigid(bh), true));
            Tube(c2, st.handNormal, kFingerSegments, CreatureMeshBuilder.PartFoot);

            if (ws.wristKnuckle > 0f)
            {
                // a knuckle bulb on the wrist (rigid to the hand), a little toward the leading edge and the back
                float kr = ws.wristKnuckle * rl;
                Vector3 kc = Wr + fwd * (0.25f * kr) + st.handNormal * (0.15f * kr);
                var kq = new List<Sample>();
                kq.Add(S(kc - dl * kr, dl, 0f, 0f, 0f, Rigid(bh), true));
                for (int k = 1; k < 6; k++)
                {
                    float a = -0.5f * Mathf.PI + Mathf.PI * k / 6f;
                    kq.Add(S(kc + dl * (kr * Mathf.Sin(a)), dl, kr * Mathf.Cos(a), -Mathf.Tan(a), kr * (1f + Mathf.Sin(a)), Rigid(bh)));
                }
                kq.Add(S(kc + dl * kr, dl, 0f, 0f, 2f * kr, Rigid(bh), true));
                Tube(kq, st.handNormal, kTubeSegments + 2, CreatureMeshBuilder.PartLeg);
            }
        }

        public void Fingers(int w)
        {
            WingSpec ws = wings[w];
            WingState st = cw.State[w];
            float r0 = Mathf.Max(1e-4f, ws.fingerRadius);
            for (int k = 0; k < cw.FingerCount(w); k++)
            {
                int bone = sp.WingBone(w, k);
                Vector3 d = cw.FingerDir[cw.FingerIndex(w, k)];
                float len = ws.fingerLengths[k];
                var q = new List<Sample>();
                Vector4 rig = Rigid(bone);
                float dr = -0.75f * r0 / len;
                if (ws.fingerKnuckles > 0f)
                {
                    // a lumpy finger bone: flared root, knuckles at ~1/3 and ~2/3, a fine claw-like tip
                    q.Add(S(st.wrist - d * r0, d, 0f, 0f, 0f, rig, true));
                    for (int i = 0; i < kKnuckleT.Length; i++)
                    {
                        float t = kKnuckleT[i], r = FingerRadius(r0, t, ws.fingerKnuckles);
                        float h = 0.004f;
                        float drr = (FingerRadius(r0, Mathf.Min(1f, t + h), ws.fingerKnuckles) - FingerRadius(r0, Mathf.Max(0f, t - h), ws.fingerKnuckles)) / (2f * h * len);
                        q.Add(S(st.wrist + d * (t * len), d, r, drr, r0 + t * len, rig));
                    }
                    q.Add(S(st.wrist + d * len, d, 0f, 0f, r0 + len, rig, true));
                    Tube(q, st.handNormal, kFingerSegments + 2, CreatureMeshBuilder.PartLeg);
                    continue;
                }
                q.Add(S(st.wrist - d * r0, d, 0f, 0f, 0f, rig, true));
                q.Add(S(st.wrist, d, r0, dr, r0, rig));
                q.Add(S(st.wrist + d * (0.35f * len), d, r0 * (1f - 0.26f), dr, r0 + 0.35f * len, rig));
                q.Add(S(st.wrist + d * (0.7f * len), d, r0 * (1f - 0.52f), dr, r0 + 0.7f * len, rig));
                q.Add(S(st.wrist + d * (0.96f * len), d, r0 * 0.25f, dr, r0 + 0.96f * len, rig));
                q.Add(S(st.wrist + d * len, d, 0f, 0f, r0 + len, rig, true));
                Tube(q, st.handNormal, kFingerSegments, CreatureMeshBuilder.PartLeg);
            }
        }

        // the knuckled finger's rings (fractions of its length) and its radius there: a taper to a fine tip, a flared
        // root, two knuckles (WingSpec.fingerKnuckles)
        static readonly float[] kKnuckleT = { 0f, 0.04f, 0.1f, 0.18f, 0.26f, 0.3f, 0.34f, 0.38f, 0.46f, 0.55f, 0.6f, 0.64f, 0.68f, 0.76f, 0.86f, 0.94f, 0.985f };
        static float FingerRadius(float r0, float t, float k)
        {
            float taper = 1f - 0.8f * t;
            float g1 = (t - 0.32f) / 0.045f, g2 = (t - 0.62f) / 0.04f, g0 = t / 0.07f;
            return r0 * taper * (1f + k * (0.5f * Mathf.Exp(-g1 * g1) + 0.42f * Mathf.Exp(-g2 * g2) + 0.35f * Mathf.Exp(-g0 * g0)));
        }

        // ---------------------------------------------------------------- membrane

        public void Membrane(int w)
        {
            WingSpec ws = wings[w];
            WingState st = cw.State[w];
            Vector3 W = st.wrist;
            // rays in fan order: fingers 0..F-1 (lengths), ribs 0..2 (to the flank)
            int F = cw.FingerCount(w), Z = MembraneZonesFor(ws);
            var ray = new Vector3[F + WingSpec.Ribs];
            var bone = new int[ray.Length];
            for (int k = 0; k < F; k++)
            {
                ray[k] = cw.FingerTip[cw.FingerIndex(w, k)] - W;
                bone[k] = sp.WingBone(w, k);
            }
            for (int k = 0; k < WingSpec.Ribs; k++)
            {
                ray[F + k] = cw.RibTarget[w * WingSpec.Ribs + k] - W;
                bone[F + k] = sp.WingBone(w, WingBoneLayout.FirstRibOf(ws) + k);
            }
            // The last zone runs from the rear flank rib to the forearm (the front flank rib only shapes the
            // fold); its bone is the arm sector.
            ray[ray.Length - 1] = st.elbow - W;
            // One smooth sheet: each zone's plane normal, averaged onto the rays between zones; a vertex takes
            // its ray normals blended by its angle fraction, so neighbouring zones share the normal where
            // they meet (both border vertices ride the same bone). The ribs' uniform scale keeps it exact.
            var zn = new Vector3[Z];
            for (int z = 0; z < Z; z++)
            {
                Vector3 c = Vector3.Cross(ray[z], ray[z + 1]);
                if (Vector3.Dot(c, st.handNormal) < 0f) c = -c;
                zn[z] = c.sqrMagnitude > 1e-12f ? c.normalized : st.handNormal;
            }
            // the arm zone (on the arm sector) keeps the sector's normal axis
            Vector3 sectorN = cw.ArmSector(w).MultiplyVector(Vector3.forward).normalized;
            zn[Z - 1] = sectorN;
            var rn = new Vector3[ray.Length];
            for (int k = 0; k < ray.Length; k++)
            {
                Vector3 sum = (k > 0 ? zn[k - 1] : Vector3.zero) + (k < Z ? zn[k] : Vector3.zero);
                rn[k] = sum.sqrMagnitude > 1e-12f ? sum.normalized : st.handNormal;
            }
            int C = ColumnsFor(ws), R = RowsFor(ws);
            bool billow = ws.membraneBillow > 0f;
            var chord = new Vector3[C + 1]; // the arm zone's far edge: rear flank attach -> elbow
            var grid = billow ? new Vector3[(C + 1) * (R + 1)] : null;
            int fr = WingBoneLayout.FanRearRib(ws), fanRear = fr >= 0 ? sp.WingBone(w, fr) : -1;
            for (int z = 0; z < Z; z++)
            {
                Vector3 A = ray[z], B = ray[z + 1];
                Vector3 nz = zn[z];
                // the free trailing edge (fingers to the rear flank attach) is scalloped; the last zone runs from
                // the rear flank rib to the forearm
                bool free = z < Z - 1;
                bool arm = z == Z - 1;
                // Columns are spaced evenly in angle (slerp between the rays) out to the straight edge A-B, and
                // weighted by that angle fraction. Evenly spaced columns survive the zone compressing when
                // the wing folds (two-ray skinning of columns spaced along the chord crowds the columns at
                // the longer ray until they cross).
                float la = A.magnitude, lb = B.magnitude;
                Vector3 da = A / Mathf.Max(1e-6f, la), db = B / Mathf.Max(1e-6f, lb);
                float th = Mathf.Acos(Mathf.Clamp(Vector3.Dot(da, db), -1f, 1f));
                // scallopByChord: the depth a fraction of the gap between the two ray tips, not of the reach
                float scallop = ws.scallop;
                if (ws.scallopByChord) scallop *= Mathf.Clamp01((A - B).magnitude / Mathf.Max(1e-6f, 0.5f * (la + lb)));
                // (not the arm zone: its far edge is the seam to the flank patches, two different affine bones that
                // agree only on the straight line from the elbow - a bellied seam would open a crack there)
                billow = ws.membraneBillow > 0f && !arm;
                if (billow)
                {
                    // the bellied sheet: flat point + camber along the zone normal, height a fraction of the local width
                    for (int j = 0; j <= R; j++)
                        for (int i = 0; i <= C; i++)
                        {
                            float u = (float)i / C, v = (float)j / R;
                            Vector3 flat = ZonePoint(W, da, db, la, lb, th, u, v, free ? scallop : 0f);
                            float width = (flat - W).magnitude * th;
                            grid[j * (C + 1) + i] = flat + nz * (ws.membraneBillow * width * Mathf.Sin(Mathf.PI * u));
                        }
                }
                for (int sheet = 0; sheet < 2; sheet++)
                {
                    float sg = sheet == 0 ? 1f : -1f;
                    Vector3 sn = sg * nz;
                    int first = pos.Count;
                    for (int j = 0; j <= R; j++)
                    {
                        float v = (float)j / R;
                        for (int i = 0; i <= C; i++)
                        {
                            float u = (float)i / C;
                            Vector3 p;
                            if (billow) p = grid[j * (C + 1) + i];
                            else
                            {
                                float edge = free ? 1f - scallop * Mathf.Sin(Mathf.PI * u) : 1f;
                                float ph = u * th;
                                float den = la * Mathf.Sin(ph) + lb * Mathf.Sin(th - ph);
                                float reach = th > 1e-4f && den > 1e-6f ? la * lb * Mathf.Sin(th) / den : Mathf.Lerp(la, lb, u);
                                Vector3 dir = th > 1e-4f ? (Mathf.Sin(th - ph) * da + Mathf.Sin(ph) * db) / Mathf.Sin(th) : da;
                                p = W + dir * (reach * v * Mathf.Lerp(1f, edge, v * v));
                            }
                            if (arm && j == R) chord[i] = p;
                            float dist = (p - W).magnitude;
                            // The arm zone rides the arm sector (exact on the forearm and on its own plane, whose
                            // normal it keeps), its seam column the rear flank rib (shared with zone 3).
                            // (groundTallStance: the zone before it rides the fan-side rear rib instead,
                            // WingBoneLayout.FanRearRib: the same frame in the air, rolled with the lifted fan on the ground)
                            int b1 = z + 2 == Z && fanRear >= 0 ? fanRear : bone[z + 1];
                            Vector4 skin = !arm ? Blend(bone[z], b1, u) : Rigid(i == 0 ? bone[z] : bone[z + 1]);
                            Vector3 vn;
                            if (billow)
                            {
                                // the bellied sheet's own normal (its grid tangents; the zone normal at the wrist pole)
                                Vector3 du = grid[j * (C + 1) + Mathf.Min(i + 1, C)] - grid[j * (C + 1) + Mathf.Max(i - 1, 0)];
                                Vector3 dv = grid[Mathf.Min(j + 1, R) * (C + 1) + i] - grid[Mathf.Max(j - 1, 0) * (C + 1) + i];
                                Vector3 gn = PartMeshEmitter.Unit(Vector3.Cross(du, dv), nz);
                                if (Vector3.Dot(gn, nz) < 0f) gn = -gn;
                                vn = sg * (j == 0 ? nz : gn);
                            }
                            else vn = (sg * (arm && i > 0 ? sectorN : Vector3.Lerp(rn[z], rn[z + 1], u))).normalized;
                            // uv1.y: the sheet's sign x (0.1 + 0.9 x radial fraction): the shader's trailing rim
                            Vertex(p, vn, dist, (z + u) / Z, PartMembrane, sg * (0.1f + 0.9f * v), skin);
                        }
                    }
                    GridRC(first, sn, R, C);
                }
            }
            HumerusPatch(w, st, chord, sp.WingBone(w, WingBoneLayout.HumerusPatchOf(ws)), sectorN, Z);
        }

        // A zone's flat point at angle fraction u, radial fraction v (the plain path's formula).
        static Vector3 ZonePoint(Vector3 W, Vector3 da, Vector3 db, float la, float lb, float th, float u, float v, float scallop)
        {
            float edge = 1f - scallop * Mathf.Sin(Mathf.PI * u);
            float ph = u * th;
            float den = la * Mathf.Sin(ph) + lb * Mathf.Sin(th - ph);
            float reach = th > 1e-4f && den > 1e-6f ? la * lb * Mathf.Sin(th) / den : Mathf.Lerp(la, lb, u);
            Vector3 dir = th > 1e-4f ? (Mathf.Sin(th - ph) * da + Mathf.Sin(ph) * db) / Mathf.Sin(th) : da;
            return W + dir * (reach * v * Mathf.Lerp(1f, edge, v * v));
        }

        // winding: front faces along sn; row 0 is a pole (one triangle per quad)
        void Grid(int first, Vector3 sn) => GridRC(first, sn, ZoneRows, ZoneColumns);

        void GridRC(int first, Vector3 sn, int rows, int cols)
        {
            int c = cols + 1;
            Vector3 p00 = pos[first + c], p01 = pos[first + c + 1], p10 = pos[first + 2 * c];
            bool flip = Vector3.Dot(Vector3.Cross(p01 - p00, p10 - p00), sn) < 0f;
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    int a = first + j * c + i, b = a + 1, d = a + c, e = d + 1;
                    if (j > 0) Tri(a, b, d, flip);
                    Tri(b, e, d, flip);
                }
        }

        // The membrane between the humerus and the flank: fans from the elbow out to the flank edge, one per flank
        // patch (WingSpec.flankPatches; one = the humerus patch), side by side between the arm zone's far edge (elbow -> rear flank
        // attach, on the arm sector) and the humerus (elbow -> shoulder). Patch p spans flank nodes p and p + 1
        // (CreatureWings.FlankNode: the rear attach, points on the flank, the shoulder), so the edge follows the
        // body. Rows run out from the elbow (row 0, the pole) through the arm zone's far-edge points' distances,
        // so patch 0 shares that edge vertex for vertex; columns are spaced evenly in angle. Each patch rides its
        // own affine bone (CreatureWings.FlankPatch), exact on its triangle: the shared edges (elbow -> a node)
        // stay exact on both sides. A seam vertex takes the mean of the two patches' normals.
        void HumerusPatch(int w, WingState st, Vector3[] chord, int patchBone, Vector3 nz, int zones)
        {
            Vector3 E = st.elbow, W = st.wrist;
            int P = cw.FlankPatches(w), f0 = cw.FlankStart(w);
            float la0 = Mathf.Max(1e-6f, (chord[0] - E).magnitude);
            int rows = chord.Length - 1;                             // the arm zone's columns
            int cols = PatchColumnsFor(P);                           // per patch
            // the first node: the arm zone's far edge's end (the rear flank attach as the mesh has it)
            Vector3 Node(int k) => k == 0 ? chord[0] : cw.FlankNode[f0 + k];
            // the patches' plane normals, then the seam (node) normals
            var pn = new Vector3[P];
            for (int p = 0; p < P; p++)
            {
                Vector3 c = Vector3.Cross(Node(p + 1) - E, Node(p) - E);
                if (Vector3.Dot(c, nz) < 0f) c = -c;
                pn[p] = c.sqrMagnitude > 1e-12f ? c.normalized : nz;
            }
            for (int p = 0; p < P; p++)
            {
                Vector3 A = Node(p) - E, B = Node(p + 1) - E;
                float la = A.magnitude, lb = B.magnitude;
                Vector3 da = A / Mathf.Max(1e-6f, la), db = B / Mathf.Max(1e-6f, lb);
                float th = Mathf.Acos(Mathf.Clamp(Vector3.Dot(da, db), -1f, 1f));
                Vector3 nA = p == 0 ? nz : (pn[p - 1] + pn[p]).normalized, nB = p == P - 1 ? pn[p] : (pn[p] + pn[p + 1]).normalized;
                for (int sheet = 0; sheet < 2; sheet++)
                {
                    float sg = sheet == 0 ? 1f : -1f;
                    int first = pos.Count;
                    for (int j = 0; j <= rows; j++)
                    {
                        // row j at the radial fraction of the arm zone's far-edge point chord[rows - j]
                        float v = Mathf.Clamp01((chord[rows - j] - E).magnitude / la0);
                        for (int i = 0; i <= cols; i++)
                        {
                            float u = (float)i / cols;
                            float ph = u * th;
                            float den = la * Mathf.Sin(ph) + lb * Mathf.Sin(th - ph);
                            float reach = th > 1e-4f && den > 1e-6f ? la * lb * Mathf.Sin(th) / den : Mathf.Lerp(la, lb, u);
                            Vector3 dir = th > 1e-4f ? (Mathf.Sin(th - ph) * da + Mathf.Sin(ph) * db) / Mathf.Sin(th) : da;
                            Vector3 q = p == 0 && i == 0 ? chord[rows - j] : E + dir * (reach * v);
                            Vector4 skin = Rigid(patchBone + p); // exact on the elbow, node p, node p + 1 triangle
                            Vector3 vn = i == 0 ? nA : i == cols ? nB : pn[p];
                            float uu = (p + u) / P;
                            Vertex(q, sg * vn, (q - W).magnitude, (zones - uu) / zones, PartMembrane, sg * (0.1f + 0.9f * v), skin);
                        }
                    }
                    GridRC(first, sg * pn[p], rows, cols);
                }
            }
        }
    }
}
