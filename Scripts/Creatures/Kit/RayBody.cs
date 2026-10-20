// Creature kit (sky manta): RayBody - the thick, flat body of a ray (manta, eagle ray, skate): a short, stiff spine
// chain under a broad lens-shaped trunk whose edges run on into the RayWings without a seam (Kit/RayShape.cs). It is
// a Body part like SpineBody (ISpineChainBody: the sim runs its joints exactly as a SpineBody's), but it draws its
// own mesh: the disc's centre, skinned to the spine joints along the body (two joints blended by the point's place
// along the spine), with the shared chord samples so the wing's root column stitches onto its edge.
// Kit-only, no runtime of its own (the sim and the locomotion mode move the joints). Mesh only; allocation at build.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("RayBody", PartKind.Body, Bones = "= jointCount", Summary = "The thick flat body of a ray: a short stiff spine chain (the sim runs it like a SpineBody's) under a broad lens-shaped trunk - domed back, flatter belly, blunt rear at the tail root - whose edges run on into RayWing pectoral fins without a seam (one shared surface, RayShape).",
          System = "CreatureSim chain (ISpineChainBody) + RayBody.EmitMesh", MaterialLayers = new[] { 15 }, KitOnly = true,
          UseWhen = "manta and eagle rays, skates, stingrays, flatfish-like gliders: a disc body whose sides are its wings")]
    public sealed class RayBody : CreaturePart, ISpineChainBody
    {
        [Param(ParamUnit.Count, "spine joints head to tail root", Min = 3, Max = 24, Example = "manta 8")] public int jointCount = 8;
        [Param(ParamUnit.Metres, "joint spacing", Example = "manta 0.14")] public float spacing = 0.14f;
        [Param(ParamUnit.Metres, "disc front ahead of joint 0 (the head and mouth)")] public float headCapLength = 0.08f;
        [Param(ParamUnit.Metres, "disc rear behind the last joint (the tail root)")] public float tailTipLength = 0.06f;
        [Param(ParamUnit.Metres, "half width of the body; the wings start here", Example = "manta 0.24")] public float halfWidth = 0.24f;
        [Param(ParamUnit.Metres, "full thickness at the centre", Example = "manta 0.2")] public float thickness = 0.2f;
        [Param(ParamUnit.Metres, "full thickness at the body's edge (the wing root)")] public float edgeThickness = 0.13f;
        [Param(ParamUnit.Metres, "minimum thickness of the trailing edge (never a knife edge)")] public float trailingThickness = 0.004f;
        [Param(ParamUnit.Metres, "the front edge recedes this much at the body's edge (a rounded head)")] public float headRound = 0.05f;
        [Param(ParamUnit.Metres, "the rear edge comes forward this much at the body's edge")] public float rearInset = 0.3f;
        [Param(ParamUnit.Fraction, "share of the thickness above the chord line (domed back, flatter belly)", Min = 0.3f, Max = 0.8f)] public float topShare = 0.62f;
        [Param(ParamUnit.Fraction, "section fullness at the centre (exponent on the thickness form: < 1 fuller)", Min = 0.3f, Max = 1f)] public float fullness = 0.6f;
        [Param(ParamUnit.Fraction, "blunt rear at the centre: the section ends this far before its fine trailing edge (the tail root)", Min = 0, Max = 0.4f)] public float bluntRear = 0.12f;
        [Param(ParamUnit.Degrees, "bend limit per joint (a ray's trunk is stiff)", Example = "manta 5")] public float bendLimitDeg = 5f;
        [Param(ParamUnit.Count, "mesh: columns across the body", Min = 4, Max = 24, Default = "8")] public int columns = 8;
        [Param(ParamUnit.Metres, "dorsal fin height (0 = none)", Example = "manta 0.07")] public float dorsalFinHeight = 0.07f;
        [Param(ParamUnit.Metres, "dorsal fin base length")] public float dorsalFinLength = 0.12f;
        [Param(ParamUnit.Fraction, "dorsal fin place along the spine: 0 head .. 1 last joint", Min = 0, Max = 1)] public float dorsalFinAt = 0.92f;
        [Param(ParamUnit.Metres, "dorsal fin thickness at its base")] public float dorsalFinThickness = 0.014f;

        public int JointCount => jointCount;
        public float Spacing => spacing;
        public float HeadCapLength => headCapLength;
        public float TailTipLength => tailTipLength;
        public int RigidHeadJoints => 0;   // a ray's head is part of the disc: no rigid head of its own

        public override int BoneCount(in SpeciesCtx c) => JointCount;
        public override int MeshOrder => 100;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a RayBody is a spine body, not with a SoftBody";
            if (jointCount < 3 || jointCount > 24) return $"{id}: jointCount must be 3..24";
            if (!(spacing > 0f) || !(halfWidth > 0f) || !(thickness > 0f)) return $"{id}: spacing, halfWidth and thickness must be > 0";
            if (edgeThickness <= 0f || edgeThickness > thickness) return $"{id}: edgeThickness must be in (0, thickness]";
            if (topShare < 0.3f || topShare > 0.8f || fullness < 0.3f || fullness > 1f || bluntRear < 0f || bluntRear > 0.4f) return $"{id}: topShare 0.3..0.8, fullness 0.3..1, bluntRear 0..0.4";
            if (columns < 4 || columns > 24) return $"{id}: columns must be 4..24";
            return null;
        }

        // The sim's joints: the body's cross-section (half width, half the thickness), resting on its belly.
        public SpineJoint[] BuildJoints()
        {
            var r = new SpineJoint[jointCount];
            float below = (1f - topShare) * thickness;
            for (int i = 0; i < r.Length; i++)
                r[i] = new SpineJoint { halfWidth = halfWidth, halfHeight = 0.5f * thickness, clearance = below, bendLimitDeg = bendLimitDeg };
            return r;
        }

        // ------------------------------------------------------------ mesh

        // Spine skin for a point at rest z (joint i at z = -i spacing): the two joints around it.
        public Vector4 SpineSkin(float z)
        {
            float k = -z / spacing;
            if (k <= 0f) return PartMeshEmitter.Rigid(0);
            if (k >= jointCount - 1) return PartMeshEmitter.Rigid(jointCount - 1);
            int i = Mathf.FloorToInt(k);
            return PartMeshEmitter.Blend(i, i + 1, k - i);
        }

        // The heavier spine joint at rest z.
        public int SpineBone(float z) => Mathf.Clamp(Mathf.RoundToInt(-z / spacing), 0, jointCount - 1);

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            CreatureSim rest = m.Rest;
            if (rest == null) return;
            RayShape shape = RayShape.For(KitView.Parts(m.Species));
            Matrix4x4 frame = Matrix4x4.TRS(rest.Joints[0], rest.Rotations[0], Vector3.one);
            int cols = columns, L = shape.LoopCount;
            int first = m.VertexCount;
            for (int j = 0; j <= cols; j++)
            {
                float x = -halfWidth + 2f * halfWidth * j / cols;
                for (int l = 0; l < L; l++) EmitDiscVertex(m, shape, frame, x, l, SpineSkin(shape.LoopPoint(x, l).z));
            }
            DiscQuads(m, first, cols, L);
            if (dorsalFinHeight > 0f && dorsalFinLength > 0f) DorsalFin(m, shape, frame);
        }

        // A small swept blade on the back near the tail root: a closed wedge (two flanks, a leading and a trailing
        // edge face), rigid to the nearest joint, its foot sunk a little into the back.
        void DorsalFin(PartMeshEmitter m, RayShape shape, Matrix4x4 frame)
        {
            float zf = -dorsalFinAt * spacing * (jointCount - 1) + 0.5f * dorsalFinLength, zb = zf - dorsalFinLength;
            float front = shape.Front(0f), rear = shape.Rear(0f);
            float Top(float z) => shape.Point(0f, Mathf.Clamp01((front - z) / (front - rear)), 1).y;
            float t = 0.5f * dorsalFinThickness;
            float sink = 0.25f * dorsalFinHeight;
            Vector3 a = new Vector3(0f, Top(zf) - sink, zf), b = new Vector3(0f, Top(zb) - sink, zb);
            Vector3 tip = new Vector3(0f, Top(zb) + dorsalFinHeight, zb - 0.35f * dorsalFinLength);
            Vector4 skin = PartMeshEmitter.Rigid(SpineBone(0.5f * (zf + zb)));
            Vector3 aL = a - Vector3.right * t, aR = a + Vector3.right * t, bL = b - Vector3.right * t, bR = b + Vector3.right * t;
            // flanks
            Face(m, frame, aL, bL, tip, Vector3.left, skin);
            Face(m, frame, aR, tip, bR, Vector3.right, skin);
            // the leading edge (a, tip) and the trailing edge (b, tip): thin faces
            Vector3 lead = Vector3.Cross(tip - a, Vector3.right).normalized; if (lead.z < 0f) lead = -lead;
            Face(m, frame, aL, tip, aR, lead, skin);
            Vector3 trail = Vector3.Cross(tip - b, Vector3.right).normalized; if (trail.z > 0f) trail = -trail;
            Face(m, frame, bL, bR, tip, trail, skin);
        }

        // One flat triangle (its own vertices: a crisp fin), wound to face `n` (rest joint-0 frame).
        static void Face(PartMeshEmitter m, Matrix4x4 frame, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 n, Vector4 skin)
        {
            Vector3 wn = frame.MultiplyVector(n).normalized;
            int i = m.VertexCount;
            m.Vertex(frame.MultiplyPoint3x4(p0), wn, 0f, 0f, PartRay, 0.8f, skin);
            m.Vertex(frame.MultiplyPoint3x4(p1), wn, 0f, 0f, PartRay, 0.8f, skin);
            m.Vertex(frame.MultiplyPoint3x4(p2), wn, 0f, 0f, PartRay, 0.8f, skin);
            Vector3 a = m.Position(i), b = m.Position(i + 1), c = m.Position(i + 2);
            m.Tri(i, i + 1, i + 2, Vector3.Dot(Vector3.Cross(b - a, c - a), wn) < 0f);
        }

        // One vertex of the disc surface at (x, loop index l), in the frame of rest joint 0. uv0 = (metres from the
        // front, 0): the pattern's surface space is (that, the rest distance from the bone's axis) - a planar map of
        // the disc for bones on the body's axis (the spine; the wing lattice puts its bind origin there too).
        internal static void EmitDiscVertex(PartMeshEmitter m, RayShape shape, Matrix4x4 frame, float x, int l, Vector4 skin)
        {
            Vector3 p = shape.LoopPoint(x, l), n = shape.LoopNormal(x, l);
            Vector3 wp = frame.MultiplyPoint3x4(p), wn = frame.MultiplyVector(n);
            float arc = shape.Body.headCapLength - p.z;
            m.Vertex(wp, wn, arc, 0f, PartRay, Mathf.Clamp(n.y, -1f, 1f), skin);
        }

        public const int PartRay = 15;

        // Quads between consecutive columns of L loop vertices, plus the trailing-edge strip (loop end to start).
        internal static void DiscQuads(PartMeshEmitter m, int first, int cols, int L)
        {
            for (int j = 0; j < cols; j++)
            {
                int a = first + j * L, b = a + L;
                for (int l = 0; l < L - 1; l++) Quad(m, a + l, b + l, b + l + 1, a + l + 1);
                Quad(m, a + L - 1, b + L - 1, b, a);   // trailing edge: top end -> bottom end
            }
        }

        // two triangles wound so their face normal points along the vertices' mean normal
        internal static void Quad(PartMeshEmitter m, int a, int b, int c, int d)
        {
            Vector3 fa = m.Position(a), fb = m.Position(b), fc = m.Position(c), fd = m.Position(d);
            Vector3 facing = m.Normal(a) + m.Normal(b) + m.Normal(c) + m.Normal(d);
            Vector3 face = Vector3.Cross(fb - fa, fc - fa) + Vector3.Cross(fc - fa, fd - fa);
            bool flip = Vector3.Dot(face, facing) < 0f;
            m.Tri(a, b, c, flip);
            m.Tri(a, c, d, flip);
        }
    }
}
