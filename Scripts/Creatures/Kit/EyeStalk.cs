// Creature kit: EyeStalk - a rigid stalk on a bone of its own, rooted on a spine joint: a crab's or a snail's eye
// stalk, a periscope, a horn that folds away. At rest it is rigid to its joint (its bone follows the joint exactly);
// a posture part (Tuck) may swing it about its root to its tuckDirection - folded down into a groove of the shell -
// and the eye of its side (Eyes, carriesEye) rides its tip. Mesh: a tapered round tube with a rounded tip, rigid to
// its own bone, in the material layer `layer` (1 = limb, 9 = the band colour, 2 = the claw/cone row). mirror
// expands into .L / .R (side -1 / +1: offset.x and the directions' x mirrored). Kit-only part; bones from the
// species layout (Rigid block: after every shipped bone). Allocation-free after construction; main thread.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("EyeStalk", PartKind.Rigid, Bones = "1", Summary = "A rigid stalk on a bone of its own rooted on a spine joint (a crab's eye stalk): rigid to its joint at rest; a posture part (Tuck) swings it about its root to tuckDirection, and the eye of its side rides its tip (carriesEye). Tapered tube, rounded tip.",
          System = "EyeStalkRuntime (part runtime, bones only) + EyeStalk.EmitMesh; folded by TuckRuntime", MaterialLayers = new[] { 1, 2, 9 }, KitOnly = true,
          UseWhen = "crab and snail eye stalks, periscopes, anything on a stalk that folds away")]
    public sealed class EyeStalk : CreaturePart
    {
        [Param(ParamUnit.Direction, "rest direction in the joint frame (x mirrored by side)")] public Vector3 direction = new Vector3(0.2f, 1f, 0.3f);
        [Param(ParamUnit.Direction, "direction when folded by a Tuck part (x mirrored by side); zero = it never folds")] public Vector3 tuckDirection = new Vector3(0.6f, -0.3f, 0.2f);
        [Param(ParamUnit.Metres, "length, root to tip", Example = "rock crab 0.045")] public float length = 0.045f;
        [Param(ParamUnit.Metres, "radius at the root")] public float radiusStart = 0.007f;
        [Param(ParamUnit.Metres, "radius at the tip")] public float radiusEnd = 0.005f;
        [Param(ParamUnit.Flag, "the eye of this side (Eyes part: eye 0 left, 1 right) rides the tip: place the Eyes' offset at the tip")] public bool carriesEye = true;
        [Param(ParamUnit.Count, "material layer (part id): 1 the limbs', 2 the claw/cone row, 9 the band colour's", Min = 1, Max = 9, Default = "1")] public int layer = 1;

        public int Side => at.side < 0 ? -1 : at.side > 0 ? 1 : 0;
        public int EyeIndex => Side < 0 ? 0 : 1;

        public override int BoneCount(in SpeciesCtx c) => 1;
        public override int MeshOrder => 100;   // a new type: after every shipped vertex

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: an EyeStalk roots on a SpineBody";
            if (at.joint < 0 || at.joint >= c.Joints) return $"{id}: joint {at.joint} outside 0..{c.Joints - 1}";
            if (!(length > 0f) || !(radiusStart > 0f) || radiusEnd < 0f) return $"{id}: length > 0, radiusStart > 0, radiusEnd >= 0";
            if (direction.sqrMagnitude < 1e-8f) return $"{id}: direction is zero";
            if (layer != 1 && layer != 2 && layer != 9) return $"{id}: layer must be 1 (limb), 2 (claw/cone) or 9 (band)";
            return null;
        }

        // Mirrored by side (a midline stalk: as authored).
        public Vector3 Mirror(Vector3 v) { if (Side < 0) v.x = -v.x; return v; }

        // The stalk's frame in the joint frame: +Z along the rest direction, +Y the joint's up made perpendicular.
        public Quaternion LocalFrame()
        {
            Vector3 d = PartMeshEmitter.Unit(Mirror(direction), Vector3.up);
            Vector3 up = Vector3.up - d * Vector3.Dot(Vector3.up, d);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.back - d * Vector3.Dot(Vector3.back, d);
            return Quaternion.LookRotation(d, up.normalized);
        }

        // The fold (in the joint frame) that swings the rest direction onto the tuck direction; identity when none.
        public Quaternion LocalFold()
        {
            if (tuckDirection.sqrMagnitude < 1e-8f) return Quaternion.identity;
            Vector3 a = PartMeshEmitter.Unit(Mirror(direction), Vector3.up), b = PartMeshEmitter.Unit(Mirror(tuckDirection), Vector3.up);
            return Quaternion.FromToRotation(a, b);
        }

        public override IPartRuntime CreateRuntime(in PartCtx c)
            => c.Sim != null ? new EyeStalkRuntime(this, c.Sim, c.Species.Layout.Start(c.Index)) : null;

        // ------------------------------------------------------------ mesh

        const int kSides = 8, kRings = 4;

        // A tapered tube from just inside the root (buried by its radius) to a rounded tip, rigid to its own bone.
        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            int bone = m.Species.Layout.Start(c.Index);
            Matrix4x4 b = m.Bones[bone];
            Vector3 p0 = b.MultiplyPoint3x4(Vector3.zero);
            Vector3 t = PartMeshEmitter.Unit(b.MultiplyVector(Vector3.forward), Vector3.up);
            Vector3 x0 = PartMeshEmitter.Unit(b.MultiplyVector(Vector3.right), Vector3.right);
            Vector4 skin = PartMeshEmitter.Rigid(bone);
            float r0 = radiusStart, r1 = Mathf.Max(PartMeshEmitter.MinRadius, radiusEnd), dr = (r1 - r0) / length;
            var q = new List<PartMeshEmitter.TubeSample>(kRings + 4);
            q.Add(PartMeshEmitter.Sample(p0 - t * r0, t, 0f, 0f, 0f, skin, true));
            q.Add(PartMeshEmitter.Sample(p0 - t * (0.5f * r0), t, 0.87f * r0, 1.7f, 0.5f * r0, skin));
            for (int k = 0; k <= kRings; k++)
            {
                float f = (float)k / kRings, s = f * length;
                q.Add(PartMeshEmitter.Sample(p0 + t * s, t, r0 + (r1 - r0) * f, dr, r0 + s, skin));
            }
            q.Add(PartMeshEmitter.Sample(p0 + t * (length + 0.6f * r1), t, 0.8f * r1, -1.3f, r0 + length + 0.6f * r1, skin));
            q.Add(PartMeshEmitter.Sample(p0 + t * (length + r1), t, 0f, 0f, r0 + length + r1, skin, true));
            m.Tube(q, x0, kSides, layer);
        }
    }

    // One stalk of one individual: its bone, rigid to its joint (TuckRuntime folds it after every part has written).
    public sealed class EyeStalkRuntime : IPartRuntime
    {
        readonly EyeStalk s;
        readonly CreatureSim sim;
        readonly int bone;
        readonly Vector3 offset;
        readonly Quaternion frame;

        public EyeStalk Part => s;
        public int Bone => bone;

        public EyeStalkRuntime(EyeStalk stalk, CreatureSim sim, int bone)
        {
            s = stalk; this.sim = sim; this.bone = bone;
            offset = stalk.Mirror(stalk.at.offset);
            frame = stalk.LocalFrame();
        }

        public PartStage Stage => PartStage.None;
        public void Step(in StepCtx c, float dt) { }

        // The root in world space (this individual's size).
        public Vector3 Root
        {
            get { int j = s.at.joint; return sim.Joints[j] + sim.Rotations[j] * (offset * sim.SizeScale); }
        }

        public void WriteBones(Matrix4x4[] dst, int o)
        {
            int j = s.at.joint;
            float sc = sim.SizeScale;
            dst[o + bone] = Matrix4x4.TRS(sim.Joints[j] + sim.Rotations[j] * (offset * sc), sim.Rotations[j] * frame, new Vector3(sc, sc, sc));
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi) { }   // within the renderer's reach of the head joint
        public void DrawDebug(ICreatureDebugDraw d) { }
    }
}
