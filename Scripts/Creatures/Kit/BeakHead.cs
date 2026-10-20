// Creature kit: BeakHead - a beaked head: an elongated skull, a long keratin beak (straight, pointed, keeled), a lower
// jaw on a bone of its own that gapes, eye sockets the species' eyes sit in, and a crest. Built as signed-distance
// solids (SculptField, polygonised by SculptMesher - cached per shape set) from a handful of head-scale numbers, so
// a head is authored as a head, not as a list of SDF shapes. It rides a LongNeck's head bone (`follow`) or a spine
// joint; the Eyes part's two eye bones are moved onto it. The beak gapes when the neck calls (LongNeckRuntime.Calling)
// or strikes (Strike), and now and then on its own (a clack) - wider in a strike.
// Pieces: skull + rostrum (skinLayer), the upper beak's keratin sheath and the lower jaw (beakLayer), the crest
// (crestLayer, the claw row by default). Head frame: +Z along the head (beak), +Y up, origin at the head bone (the end of the neck), metres
// at scale 1. Allocation-free after construction; main thread.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("BeakHead", PartKind.Rigid, Bones = "1 (the lower jaw)",
          Summary = "A beaked head from head-scale numbers: an elongated skull with eye sockets and an antorbital groove, a long keeled keratin beak, a lower jaw on its own bone that gapes when the neck calls or strikes, and a blade crest in its own layer; SDF solids (SculptField / SculptMesher). Rides a LongNeck's head bone or a spine joint; carries the species' eyes.",
          System = "BeakHeadRuntime (part runtime, stage Late: jaw, eye bones) + BeakHead.EmitMesh (SculptMesher pieces)",
          MaterialLayers = new[] { 0, 2 }, KitOnly = true,
          UseWhen = "pterosaurs, birds, herons and storks, beaked dinosaurs, turtles: any head that is mostly beak; with a crest (casque, sail) or without")]
    public partial class BeakHead : CreaturePart
    {
        [Param(ParamUnit.PartRef, "the LongNeck whose head bone it rides (empty: rigid to at.joint, at.offset)")] public string follow = "";
        [Param(ParamUnit.Metres, "skull length behind the beak (occiput to the beak's root)", Example = "pterodactyl 0.16")] public float skullLength = 0.16f;
        [Param(ParamUnit.Metres, "skull half height")] public float skullHeight = 0.048f;
        [Param(ParamUnit.Metres, "skull half width")] public float skullWidth = 0.04f;
        [Param(ParamUnit.Metres, "beak length from its root to the tip", Example = "pterodactyl 0.46")] public float beakLength = 0.46f;
        [Param(ParamUnit.Metres, "the beak's half height at its root (upper beak)")] public float beakDepth = 0.034f;
        [Param(ParamUnit.Fraction, "beak cross-section height / width", Min = 0.8f, Max = 3)] public float beakCompress = 1.4f;
        [Param(ParamUnit.Degrees, "the beak bends down (+) or up (-) toward its tip", Min = -20, Max = 30)] public float beakDroopDeg = 2f;
        [Param(ParamUnit.Fraction, "the keel (culmen ridge) along the upper beak, x the beak depth (0 = round)", Min = 0, Max = 1)] public float keel = 0.35f;
        [Param(ParamUnit.Degrees, "the widest gape (a strike); a call opens it 0.6 x as far", Min = 0, Max = 80)] public float gapeDeg = 38f;
        [Param(ParamUnit.Seconds, "mean interval between idle clacks (0 = none)")] public float clackInterval = 9f;
        [Param(ParamUnit.Metres, "crest height above the skull (0 = none)", Example = "pterodactyl 0.07")] public float crestHeight = 0.07f;
        [Param(ParamUnit.Metres, "crest length along the head")] public float crestLength = 0.16f;
        [Param(ParamUnit.Degrees, "crest sweep back (0 = straight up)", Min = -30, Max = 80)] public float crestSweepDeg = 40f;
        [Param(ParamUnit.Metres, "crest half thickness")] public float crestThickness = 0.006f;
        [Param(ParamUnit.Fraction, "the crest's root along the skull, 0 occiput .. 1 the beak's root", Min = 0, Max = 1.5f)] public float crestAt = 0.6f;
        [Param(ParamUnit.Metres, "eye centre in the head frame (x mirrored)")] public Vector3 eyeOffset = new Vector3(0.035f, 0.04f, 0.07f);
        [Param(ParamUnit.Count, "material layer of the skull and rostrum (0 = the body's skin)", Min = 0, Max = 15)] public int skinLayer = 0;
        [Param(ParamUnit.Count, "material layer of the beak and jaw (2 = claw keratin)", Min = 0, Max = 15)] public int beakLayer = 2;
        [Param(ParamUnit.Count, "material layer of the crest (2 = claw keratin; a species may restyle a spare row for it with Layers: the Pterodactyl's teal crest is row 13)", Min = 0, Max = 15)] public int crestLayer = 2;
        [Param(ParamUnit.Metres, "polygoniser cell (~1/100 of the head's length)")] public float cell = 0.005f;

        public override int BoneCount(in SpeciesCtx c) => 1;
        public override int MeshOrder => 100;
        public override void CollectReferences(List<string> into) { if (!string.IsNullOrEmpty(follow)) into.Add(follow); }

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a BeakHead needs a SpineBody";
            if (!string.IsNullOrEmpty(follow) && !(c.Find(follow) is LongNeck)) return $"{id}: follow '{follow}' is not a LongNeck";
            if (string.IsNullOrEmpty(follow) && (at.joint < 0 || at.joint >= c.Joints)) return $"{id}: joint {at.joint} outside 0..{c.Joints - 1}";
            if (!(skullLength > 0f) || !(skullHeight > 0f) || !(skullWidth > 0f) || !(beakLength > 0f) || !(beakDepth > 0f)) return $"{id}: skull and beak sizes > 0";
            if (!(cell >= Sculpt.MinCell)) return $"{id}: cell >= {Sculpt.MinCell}";
            if (skinLayer >= PartLayers.MaxParts || beakLayer >= PartLayers.MaxParts || crestLayer >= PartLayers.MaxParts) return $"{id}: layers < {PartLayers.MaxParts}";
            return null;
        }

        // the head bone (a LongNeck's last) and the jaw's hinge
        internal int HeadBone(CreatureSpecies runtime, out bool joint)
        {
            joint = false;
            IReadOnlyList<CreaturePart> parts = KitView.Parts(runtime);
            for (int i = 0; i < parts.Count; i++)
                if (parts[i] is LongNeck n && n.id == follow) return runtime.Layout.Start(i) + n.segments - 1;
            joint = true;
            return Mathf.Clamp(at.joint, 0, runtime.JointCount - 1);
        }

        // the jaw's hinge in the head frame, and the gape held at rest in degrees (0 = shut between clacks, calls and
        // strikes); a head type drawn its own way (a subtype) places its own
        internal virtual Vector3 Hinge => new Vector3(0f, -0.45f * skullHeight, 0.2f * skullLength);
        internal virtual float RestGapeDeg => 0f;
        float BeakRoot => skullLength;   // z of the beak's root (the head bone at z = 0 is the occiput)

        public override IPartRuntime CreateRuntime(in PartCtx c)
            => c.Sim != null ? new BeakHeadRuntime(this, c.Sim, c.Species.Layout.Start(c.Index), HeadBone(c.Species, out bool j), j) : null;

        // ------------------------------------------------------------ the solids (head frame)

        static SculptShape Ell(Vector3 c, Vector3 r, SculptOp op = SculptOp.Union, float blend = 0f, bool mirror = false)
            => new SculptShape { kind = SculptKind.Ellipsoid, op = op, centre = c, size = r, blend = blend, mirror = mirror };
        static SculptShape Cone(Vector3 a, Vector3 b, float r0, float r1, float squash, float blend, SculptOp op = SculptOp.Union)
            => new SculptShape { kind = SculptKind.RoundCone, op = op, centre = a, end = b, size = new Vector3(r0, r1, 0f), squash = squash, blend = blend };

        // the beak's centre line at fraction f (0 root .. 1 tip), drooping by beakDroopDeg over its length
        Vector3 BeakLine(float f, float y0)
        {
            float d = beakDroopDeg * Mathf.Deg2Rad;
            return new Vector3(0f, y0 - Mathf.Sin(d) * beakLength * f * f, BeakRoot + beakLength * f);
        }

        internal Sculpt SkullSolid()
        {
            float L = skullLength, H = skullHeight, W = skullWidth;
            var s = new List<SculptShape>
            {
                Ell(new Vector3(0f, 0.45f * H, 0.45f * L), new Vector3(W, H, 0.55f * L)),                                  // cranium
                Cone(new Vector3(0f, 0f, -0.15f * L), new Vector3(0f, 0.4f * H, 0.5f * L), 0.75f * W, 0.95f * W, 1.1f, 0.3f * W), // occiput into the neck
                Cone(new Vector3(0f, 0.35f * H, 0.6f * L), BeakLine(0.45f, 0.25f * H), 0.95f * beakDepth, 0.55f * beakDepth, beakCompress, 0.5f * W), // rostrum
                Ell(new Vector3(eyeOffset.x, eyeOffset.y, eyeOffset.z), new Vector3(0.42f * W, 0.36f * H, 0.25f * L), SculptOp.Subtract, 0.15f * W, true),  // eye sockets
                Ell(new Vector3(0.8f * W, 0.25f * H, L + 0.08f * beakLength), new Vector3(0.22f * W, 0.32f * H, 0.16f * beakLength), SculptOp.Subtract, 0.2f * W, true), // antorbital groove
            };
            return new Sculpt { shapes = s.ToArray(), cell = cell, layer = skinLayer };
        }

        internal Sculpt BeakSolid()
        {
            float H = skullHeight;
            var s = new List<SculptShape>();
            Vector3 a = BeakLine(0.08f, 0.22f * H), m = BeakLine(0.55f, 0.1f * H), b = BeakLine(1f, -0.05f * H);
            s.Add(Cone(a, m, beakDepth, 0.6f * beakDepth, beakCompress, 0.01f));
            s.Add(Cone(m, b, 0.6f * beakDepth, 0.04f * beakDepth, beakCompress, 0.3f * beakDepth));
            if (keel > 0f)
            {
                Vector3 k0 = BeakLine(0.1f, 0.22f * H + 0.85f * beakDepth * beakCompress * 0.7f), k1 = BeakLine(0.85f, 0.15f * beakDepth);
                s.Add(Cone(k0, k1, keel * 0.35f * beakDepth, 0.05f * beakDepth, 3f, 0.4f * beakDepth));
            }
            return new Sculpt { shapes = s.ToArray(), cell = cell, layer = beakLayer };
        }

        internal Sculpt JawSolid()
        {
            float H = skullHeight, W = skullWidth;
            Vector3 hinge = Hinge;
            Vector3 a = hinge + new Vector3(0f, -0.05f * H, -0.05f * skullLength), m = BeakLine(0.5f, -0.45f * H), b = BeakLine(0.97f, -0.32f * H);
            var s = new List<SculptShape>
            {
                Cone(a, m, 0.62f * W, 0.42f * beakDepth, 0.9f, 0.01f),
                Cone(m, b, 0.42f * beakDepth, 0.04f * beakDepth, 1.1f, 0.2f * beakDepth),
            };
            return new Sculpt { shapes = s.ToArray(), cell = cell, layer = beakLayer };
        }

        // A blade: a thin rounded slab standing on the skull, swept back by crestSweepDeg, its top cut by a plane from
        // the front foot to the back tip (a triangular sail), blended into the skull's top.
        internal Sculpt CrestSolid()
        {
            if (!(crestHeight > 0f)) return null;
            float L = skullLength, H = skullHeight;
            float t = Mathf.Max(crestThickness, cell);
            Vector3 root = new Vector3(0f, 0.8f * H, crestAt * L);
            var slab = new SculptShape
            {
                kind = SculptKind.Box, op = SculptOp.Union, centre = root + new Vector3(0f, 0.5f * crestHeight, -0.15f * crestLength),
                size = new Vector3(t, 0.5f * crestHeight + 0.3f * H, 0.5f * crestLength), rotationDeg = new Vector3(-crestSweepDeg, 0f, 0f),
                round = 0.9f * t,
            };
            // the cut: solid below a plane through the front foot rising back to the tip (rotated +y = its normal)
            float slope = Mathf.Atan2(crestHeight, 0.9f * crestLength) * Mathf.Rad2Deg;
            var cut = new SculptShape
            {
                kind = SculptKind.Plane, op = SculptOp.Intersect, blend = 0.6f * t,
                centre = root + new Vector3(0f, 0.05f * H, 0.45f * crestLength), rotationDeg = new Vector3(slope, 0f, 0f),
            };
            var floor = new SculptShape   // nothing of it below the skull's crown
            {
                kind = SculptKind.Plane, op = SculptOp.Subtract, blend = 0.5f * t,
                centre = root - new Vector3(0f, 0.35f * H, 0f), rotationDeg = Vector3.zero,
            };
            return new Sculpt { shapes = new[] { slab, cut, floor }, cell = Mathf.Min(cell, 0.7f * t + 0.3f * cell), layer = crestLayer };
        }

        // ------------------------------------------------------------ mesh

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            int hb = HeadBone(m.Species, out bool onJoint), jaw = m.Species.Layout.Start(c.Index);
            Matrix4x4 B = onJoint ? Matrix4x4.TRS(m.Rest.Joints[hb] + m.Rest.Rotations[hb] * at.offset, m.Rest.Rotations[hb], Vector3.one) : m.Bones[hb];
            Emit(m, SkullSolid(), B, hb, skinLayer, 0f);
            Emit(m, BeakSolid(), B, hb, beakLayer, 0f);
            Emit(m, JawSolid(), B, jaw, beakLayer, -0.6f);
            Sculpt cr = CrestSolid();
            if (cr != null) Emit(m, cr, B, hb, crestLayer, 0.5f);
        }

        static void Emit(PartMeshEmitter m, Sculpt s, Matrix4x4 B, int bone, int layer, float dorsalShift)
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
    }

    // One head of one individual (stage Late): the jaw bone (gape) and the eye bones on the head bone.
    public sealed class BeakHeadRuntime : IPartRuntime
    {
        readonly BeakHead p;
        readonly CreatureSim sim;
        readonly int jaw, head;
        readonly bool onJoint;
        LongNeckRuntime neck;
        bool looked;
        float gape, clackIn, clackLeft;
        uint rng;

        public float Gape => gape;   // degrees
        public int JawBone => jaw;

        public BeakHeadRuntime(BeakHead part, CreatureSim sim, int jawBone, int headBone, bool joint)
        {
            p = part; this.sim = sim; jaw = jawBone; head = headBone; onJoint = joint;
            rng = (uint)(sim.Seed * 747796405u + 2891336453u + (uint)jawBone);
            if (rng == 0) rng = 1;
            clackIn = part.clackInterval * sim.TimeScale * (0.5f + Rand());
        }

        float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }

        public PartStage Stage => PartStage.Late;

        LongNeckRuntime Neck()
        {
            if (looked) return neck;
            looked = true;
            IPartRuntime[] all = sim.PartRuntimes;
            if (all != null) for (int i = 0; i < all.Length; i++) if (all[i] is LongNeckRuntime n && n.Part.id == p.follow) neck = n;
            return neck;
        }

        public void Step(in StepCtx c, float dt)
        {
            if (!(dt > 0f)) return;
            float T = sim.TimeScale;
            LongNeckRuntime n = Neck();
            float want = 0f;
            if (n != null) want = Mathf.Max(0.6f * n.Calling, n.Strike > 0.25f ? Mathf.Clamp01((n.Strike - 0.25f) / 0.5f) : 0f);
            float rest = p.RestGapeDeg;
            if (rest > 0f && p.gapeDeg > 0f) want = Mathf.Max(want, Mathf.Min(1f, rest / p.gapeDeg));
            if (p.clackInterval > 0f)
            {
                if (clackLeft > 0f) { clackLeft -= dt; want = Mathf.Max(want, 0.35f); }
                else { clackIn -= dt; if (clackIn <= 0f) { clackLeft = 0.25f * T; clackIn = p.clackInterval * T * (0.5f + Rand()); } }
            }
            gape += (want * p.gapeDeg - gape) * (1f - Mathf.Exp(-dt / (0.07f * T)));
        }

        public void WriteBones(Matrix4x4[] dst, int o)
        {
            float s = sim.SizeScale;
            Matrix4x4 H;
            if (onJoint) { Quaternion r = sim.Rotations[head]; H = Matrix4x4.TRS(sim.Joints[head] + r * (p.at.offset * s), r, new Vector3(s, s, s)); }
            else H = dst[o + head];
            Vector3 hinge = p.Hinge;
            Quaternion q = Quaternion.AngleAxis(gape, Vector3.right);
            dst[o + jaw] = H * Matrix4x4.TRS(hinge - q * hinge, q, Vector3.one);   // about the hinge
            CreatureSpecies sp = sim.Species;
            for (int e = 0; e < 2; e++)
            {
                Vector3 eo = p.eyeOffset; eo.x *= e == 0 ? -1f : 1f;
                dst[o + sp.EyeBone(e)] = H * Matrix4x4.TRS(eo, Quaternion.identity, Vector3.one);
            }
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi) { }   // the neck grows the bounds round the head
        public void DrawDebug(ICreatureDebugDraw d) { }
    }
}
