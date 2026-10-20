// Creature kit: CasqueHead - the Apex's head: a wedge-shaped reptilian head that continues the neck, its mouth held
// open, a toothed upper jaw, a pale keeled lower jaw, and a teal casque hooked up and back off the snout's tip. It rides
// a LongNeck's head bone and works as a BeakHead does (BeakHeadRuntime: the jaw gapes when the neck calls or strikes,
// clacks now and then, the eyes ride the head) - with restGapeDeg the mouth never quite closes. Drawn its own way:
//   skull     a flat-topped wedge from the occiput (as wide as the neck's head end) to a blunt snout: a cranium, brow
//             ridges over the eye sockets, nostril pits near the tip;
//   upper jaw the snout's underside, its margin lined with teeth (teeth per side, toothLength), the snout's tip
//             (layer skinLayer; teeth in beakLayer);
//   lower jaw a long jaw on its own bone, deepened below into a keel (keel x the jaw's depth), with its own row of
//             teeth (beakLayer: the pale jaw);
//   casque    a laterally flattened horn rising off the snout's top near its tip and hooking back (crestHeight,
//             crestLength, crestSweepDeg, crestThickness, crestAt along the snout), in crestLayer.
// The BeakHead's numbers read as: skull* the cranium, beakLength / beakDepth / beakCompress / beakDroopDeg the snout,
// keel the lower jaw's keel, crest* the casque. Signed-distance solids (SculptMesher) plus mesh teeth (Horn).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("CasqueHead", PartKind.Rigid, Bones = "1 (the lower jaw), as BeakHead",
          Summary = "A wedge-shaped reptilian head continuing a LongNeck, its mouth held open (restGapeDeg): a flat-topped skull with brow ridges, a toothed snout, a pale keeled lower jaw on its own bone with teeth, and a flattened casque hooking up and back off the snout's tip. Works as a BeakHead (gape on calls and strikes, clacks, eyes ride it).",
          System = "BeakHeadRuntime (as BeakHead: jaw, eye bones, rest gape) + CasqueHead.EmitMesh (SculptMesher pieces, Horn teeth)",
          MaterialLayers = new[] { 0, 2 }, KitOnly = true,
          UseWhen = "monster and dragon heads at the end of a long neck (the Apex): a toothed open mouth and a casque, not a beak")]
    public sealed class CasqueHead : BeakHead
    {
        [Param(ParamUnit.Degrees, "the gape held at rest (the mouth never quite closes; calls and strikes open it to gapeDeg)", Min = 0, Max = 60, Example = "apex 16")] public float restGapeDeg = 14f;
        [Param(ParamUnit.Count, "teeth per side, upper and lower (0 = none)", Min = 0, Max = 24, Example = "apex 9")] public int teeth = 8;
        [Param(ParamUnit.Metres, "the longest tooth (they shorten toward the back)", Example = "apex 0.022")] public float toothLength = 0.02f;
        [Param(ParamUnit.Fraction, "the brow ridges over the eyes, x the skull's half height", Min = 0, Max = 0.8f)] public float brow = 0.35f;
        [Param(ParamUnit.Degrees, "the casque's hook: how far its tip curls back over (0 = a straight swept blade)", Min = 0, Max = 120)] public float casqueHookDeg = 60f;

        internal override float RestGapeDeg => restGapeDeg;
        // the hinge at the back of the mouth, low on the skull
        internal override Vector3 Hinge => new Vector3(0f, -0.5f * skullHeight, 0.35f * skullLength);

        public override string Validate(in SpeciesCtx c)
        {
            string e = base.Validate(c);
            if (e != null) return e;
            if (restGapeDeg < 0f || restGapeDeg > 60f || restGapeDeg > gapeDeg) return $"{id}: restGapeDeg 0..60 and <= gapeDeg";
            if (teeth < 0 || teeth > 24 || toothLength < 0f || brow < 0f || brow > 0.8f || casqueHookDeg < 0f || casqueHookDeg > 120f)
                return $"{id}: teeth 0..24, toothLength >= 0, brow 0..0.8, casqueHookDeg 0..120";
            return null;
        }

        static SculptShape Ell(Vector3 c, Vector3 r, SculptOp op = SculptOp.Union, float blend = 0f, bool mirror = false)
            => new SculptShape { kind = SculptKind.Ellipsoid, op = op, centre = c, size = r, blend = blend, mirror = mirror };
        static SculptShape Cone(Vector3 a, Vector3 b, float r0, float r1, float squash, float blend, SculptOp op = SculptOp.Union)
            => new SculptShape { kind = SculptKind.RoundCone, op = op, centre = a, end = b, size = new Vector3(r0, r1, 0f), squash = squash, blend = blend };

        // the snout's centre line at fraction f (0 its root at the skull's front .. 1 its tip), drooping by beakDroopDeg
        Vector3 Snout(float f, float y0)
        {
            float d = beakDroopDeg * Mathf.Deg2Rad;
            return new Vector3(0f, y0 - Mathf.Sin(d) * beakLength * f * f, skullLength + beakLength * f);
        }
        // the snout's half width (x) at fraction f
        float SnoutW(float f) => Mathf.Lerp(beakDepth, 0.55f * beakDepth, f) / Mathf.Max(0.5f, beakCompress) * 1.25f;

        internal Sculpt CasqueSkull()
        {
            float L = skullLength, H = skullHeight, W = skullWidth;
            var s = new List<SculptShape>
            {
                Ell(new Vector3(0f, 0.3f * H, 0.5f * L), new Vector3(W, H, 0.62f * L)),                                     // cranium, flat wedge
                Cone(new Vector3(0f, 0.05f * H, -0.2f * L), new Vector3(0f, 0.25f * H, 0.55f * L), 0.85f * W, 0.95f * W, 1.05f, 0.3f * W), // occiput into the neck
                Cone(new Vector3(0f, 0.3f * H, 0.7f * L), Snout(1f, 0.1f * H), beakDepth, 0.5f * beakDepth, beakCompress, 0.45f * W),     // the snout (upper jaw)
                Ell(new Vector3(eyeOffset.x, eyeOffset.y, eyeOffset.z), new Vector3(0.4f * W, 0.34f * H, 0.22f * L), SculptOp.Subtract, 0.12f * W, true), // eye sockets
                Ell(new Vector3(0.18f * beakDepth, 0.1f * H + 0.55f * beakDepth * beakCompress, skullLength + 0.9f * beakLength), new Vector3(0.12f * beakDepth, 0.1f * beakDepth, 0.07f * beakLength), SculptOp.Subtract, 0.05f * beakDepth, true), // nostrils
            };
            if (brow > 0f)
                s.Add(Ell(new Vector3(eyeOffset.x * 0.9f, eyeOffset.y + 0.32f * H, eyeOffset.z + 0.02f * L), new Vector3(0.38f * W, brow * H * 0.45f, 0.3f * L), SculptOp.Union, 0.25f * W, true));
            return new Sculpt { shapes = s.ToArray(), cell = cell, layer = skinLayer };
        }

        internal Sculpt CasqueJaw()
        {
            float H = skullHeight, W = skullWidth;
            Vector3 hinge = Hinge;
            Vector3 a = hinge + new Vector3(0f, -0.05f * H, -0.08f * skullLength), m = Snout(0.45f, -0.55f * H), b = Snout(0.95f, -0.38f * H);
            var s = new List<SculptShape>
            {
                Cone(a, m, 0.6f * W, 0.5f * beakDepth, 0.9f, 0.01f),
                Cone(m, b, 0.5f * beakDepth, 0.12f * beakDepth, 1f, 0.25f * beakDepth),
            };
            if (keel > 0f)
            {
                // the keel: a flattened blade under the jaw, deepest a third of the way along
                Vector3 k0 = hinge + new Vector3(0f, -0.35f * H, 0.15f * skullLength), k1 = Snout(0.3f, -0.55f * H - keel * 1.2f * beakDepth), k2 = Snout(0.8f, -0.5f * H);
                s.Add(Cone(k0, k1, 0.18f * W, 0.22f * beakDepth, 3f, 0.3f * beakDepth));
                s.Add(Cone(k1, k2, 0.22f * beakDepth, 0.08f * beakDepth, 3f, 0.3f * beakDepth));
            }
            return new Sculpt { shapes = s.ToArray(), cell = cell, layer = beakLayer };
        }

        // the casque: a flattened horn off the snout's top near crestAt of the snout, rising and hooking back
        internal Sculpt Casque()
        {
            if (!(crestHeight > 0f)) return null;
            float t = Mathf.Max(crestThickness, cell);
            Vector3 root = Snout(Mathf.Clamp01(crestAt), 0.1f * skullHeight) + new Vector3(0f, 0.75f * beakDepth * beakCompress, 0f);
            float sweep = crestSweepDeg * Mathf.Deg2Rad, hook = casqueHookDeg * Mathf.Deg2Rad;
            // three segments: up (swept back), then curling over by the hook
            Vector3 d0 = new Vector3(0f, Mathf.Cos(sweep), -Mathf.Sin(sweep));
            Vector3 d1 = new Vector3(0f, Mathf.Cos(sweep + 0.5f * hook), -Mathf.Sin(sweep + 0.5f * hook));
            Vector3 d2 = new Vector3(0f, Mathf.Cos(sweep + hook), -Mathf.Sin(sweep + hook));
            float seg = crestLength / 3f;
            Vector3 p1 = root + d0 * (crestHeight * 0.55f), p2 = p1 + d1 * Mathf.Max(seg, 0.35f * crestHeight), p3 = p2 + d2 * Mathf.Max(0.7f * seg, 0.25f * crestHeight);
            float sq = Mathf.Clamp(t > 0f ? 1f / Mathf.Max(0.25f, t / (0.45f * crestHeight)) : 3f, 1f, 4f);   // tall, thin section
            float r0 = 0.32f * crestHeight / sq;
            var s = new[]
            {
                Cone(root - d0 * (0.3f * crestHeight), p1, r0, 0.8f * r0, sq, 0.01f),
                Cone(p1, p2, 0.8f * r0, 0.5f * r0, sq, 0.4f * r0),
                Cone(p2, p3, 0.5f * r0, 0.08f * r0, sq, 0.3f * r0),
            };
            // the cone's section is squashed along its local y (= world up made square to it): turned so the blade is
            // thin across the head (x) - RoundCone flattens y; a vertical cone flattens toward z, so stand it in the
            // head's mid-plane: x width = radius, y extent = radius x squash (the blade's chord)
            return new Sculpt { shapes = s, cell = Mathf.Min(cell, 0.6f * r0), layer = crestLayer };
        }

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            int hb = HeadBone(m.Species, out bool onJoint), jaw = m.Species.Layout.Start(c.Index);
            Matrix4x4 B = onJoint ? Matrix4x4.TRS(m.Rest.Joints[hb] + m.Rest.Rotations[hb] * at.offset, m.Rest.Rotations[hb], Vector3.one) : m.Bones[hb];
            EmitSolid(m, CasqueSkull(), B, hb, skinLayer, 0f);
            EmitSolid(m, CasqueJaw(), B, jaw, beakLayer, -0.6f);
            Sculpt cq = Casque();
            if (cq != null) EmitSolid(m, cq, B, hb, crestLayer, 0.5f);
            Teeth(m, B, hb, jaw);
        }

        static void EmitSolid(PartMeshEmitter m, Sculpt s, Matrix4x4 B, int bone, int layer, float dorsalShift)
        {
            SculptPiece piece = SculptMesher.For(s);
            Vector4 skin = PartMeshEmitter.Rigid(bone);
            Vector3 up = PartMeshEmitter.Unit(B.GetColumn(1), Vector3.up), fwd = PartMeshEmitter.Unit(B.GetColumn(2), Vector3.forward);
            Vector3 o = B.GetColumn(3);
            int first = m.VertexCount;
            for (int i = 0; i < piece.Pos.Length; i++)
            {
                Vector3 wp = B.MultiplyPoint3x4(piece.Pos[i]);
                Vector3 wn = PartMeshEmitter.Unit(B.MultiplyVector(piece.Nrm[i]), up);
                m.Vertex(wp, wn, Vector3.Dot(wp - o, fwd), piece.V[i], layer, Vector3.Dot(wn, up) + dorsalShift, skin);
            }
            int[] t = piece.Tris;
            for (int k = 0; k < t.Length; k += 3) m.Tri(first + t[k], first + t[k + 1], first + t[k + 2], false);
        }

        // Tooth i of a row (0 front .. teeth-1 back), head frame: its root on the jaw's margin and its length.
        internal void Tooth(bool upper, int i, out Vector3 root, out Vector3 dir, out float len)
        {
            float f = teeth > 1 ? Mathf.Lerp(0.92f, 0.12f, (float)i / (teeth - 1)) : 0.6f;
            float H = skullHeight;
            if (upper)
            {
                Vector3 cl = Vector3.Lerp(new Vector3(0f, 0.3f * H, 0.7f * skullLength), Snout(1f, 0.1f * H), Mathf.InverseLerp(-0.2f, 1f, f));
                float r = Mathf.Lerp(beakDepth, 0.5f * beakDepth, f);
                root = cl + new Vector3(0.62f * r / Mathf.Max(1f, 0.8f * beakCompress), -0.55f * r * beakCompress, 0f);
                dir = new Vector3(-0.15f, -1f, 0.1f).normalized;
            }
            else
            {
                Vector3 cl = Snout(Mathf.Lerp(0.45f, 0.95f, Mathf.InverseLerp(0.12f, 0.92f, f)), Mathf.Lerp(-0.55f, -0.38f, Mathf.InverseLerp(0.12f, 0.92f, f)) * H);
                float r = Mathf.Lerp(0.5f * beakDepth, 0.12f * beakDepth, Mathf.InverseLerp(0.12f, 0.92f, f));
                root = cl + new Vector3(0.6f * r, 0.6f * r, 0f);
                dir = new Vector3(-0.15f, 1f, 0.1f).normalized;
            }
            len = toothLength * Mathf.Lerp(0.55f, 1f, f) * (upper ? 1f : 0.8f);
        }

        void Teeth(PartMeshEmitter m, Matrix4x4 B, int head, int jaw)
        {
            if (teeth <= 0 || !(toothLength > 0f)) return;
            for (int up = 0; up < 2; up++)
                for (int i = 0; i < teeth; i++)
                    for (int sdx = -1; sdx <= 1; sdx += 2)
                    {
                        Tooth(up == 0, i, out Vector3 root, out Vector3 dir, out float len);
                        root.x *= sdx; dir.x *= sdx;
                        Vector3 wr = B.MultiplyPoint3x4(root - dir * (0.35f * len)), wd = PartMeshEmitter.Unit(B.MultiplyVector(dir), Vector3.down);
                        // curving back toward the throat (the frame's -y is the curl: y = the head's forward made square)
                        Vector3 fw = PartMeshEmitter.Unit(B.MultiplyVector(Vector3.back), Vector3.back);
                        Vector3 yy = PartMeshEmitter.Unit(fw - wd * Vector3.Dot(fw, wd), Vector3.up);
                        // Horn curls toward -y: aim -y back toward the throat
                        m.Horn(wr, Quaternion.LookRotation(wd, -yy), 0.22f * len, 0.02f * len, 1.35f * len, 20f, up == 0 ? head : jaw, beakLayer, 6, 0f, out _, out _);
                    }
        }
    }
}
