// Creature kit (2026-10-20, the GolfballStrider): SpiderFace - a small face that sits on a body: a rounded cap (the
// carapace front), a cluster of glossy eyes on its upper front (a big pair, then smaller pairs further out and up), and
// a pair of chelicera fangs hanging from its lower front, curling back. Rigid to its joint, or riding another part's
// bone (`on`: a DimpledOrb, so the face rocks with the orb it is set in). Mesh only (no runtime).
// Material layers: `layer` the cap (default 1, the limb row: dark chitin with Layers), 3 the eyes, 2 the fangs.
// Kit-only part, off unless a species names it; MeshOrder 100.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("SpiderFace", PartKind.Rigid, Bones = "0 (rigid to its joint, or rides `on`'s bone)", Summary = "A small spider face set on a body: a rounded cap, a cluster of glossy eyes (a big pair, then smaller pairs further out and up), and two chelicera fangs hanging from its lower front, curling back. Rigid to its joint or riding a DimpledOrb's bone.",
          System = "mesh only (SpiderFace.EmitMesh)", MaterialLayers = new[] { 1, 3, 2 }, KitOnly = true,
          UseWhen = "spiders, harvestmen, mites, any small-headed arthropod whose face sits low on a round body")]
    public sealed class SpiderFace : CreaturePart
    {
        [Param(ParamUnit.PartRef, "a DimpledOrb whose bone the face rides (empty = rigid to its joint)")] public string on = "";
        [Param(ParamUnit.Direction, "where the face looks, in the joint frame (+z forward, -y down)")] public Vector3 direction = new Vector3(0f, -0.4f, 1f);
        [Param(ParamUnit.Metres, "cap width", Example = "golfball strider 0.075")] public float width = 0.07f;
        [Param(ParamUnit.Metres, "cap height")] public float height = 0.055f;
        [Param(ParamUnit.Metres, "cap depth (how far it bulges along the facing direction)")] public float depth = 0.045f;
        [Param(ParamUnit.Count, "eyes (pairs from the middle out; an odd count puts one on top)", Min = 0, Max = 8, Default = "6")] public int eyeCount = 6;
        [Param(ParamUnit.Metres, "radius of the big front pair (the others are smaller)")] public float eyeRadius = 0.008f;
        [Param(ParamUnit.Metres, "fang length (0 = none)")] public float fangLength = 0.035f;
        [Param(ParamUnit.Metres, "fang radius at its root")] public float fangRadius = 0.006f;
        [Param(ParamUnit.Degrees, "fang curl back toward the body over its length", Default = "45")] public float fangCurveDeg = 45f;
        [Param(ParamUnit.Fraction, "fangs apart, x the cap width", Min = 0f, Max = 1f, Default = "0.32")] public float fangSpread = 0.32f;
        [Param(ParamUnit.Count, "material layer of the cap (part id)", Min = 0, Max = 15, Default = "1")] public int layer = 1;

        public override int MeshOrder => 100;

        public override void CollectReferences(List<string> into) { if (!string.IsNullOrEmpty(on)) into.Add(on); }

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a SpiderFace sits on a SpineBody";
            if (at.joint < 0 || at.joint >= c.Joints) return $"{id}: joint {at.joint} outside 0..{c.Joints - 1}";
            if (!(width > 0f && height > 0f && depth > 0f)) return $"{id}: width, height, depth must be > 0";
            if (eyeCount < 0 || eyeCount > 8) return $"{id}: eyeCount must be in 0..8";
            if (eyeCount > 0 && !(eyeRadius > 0f)) return $"{id}: eyeRadius must be > 0";
            if (fangLength < 0f || (fangLength > 0f && !(fangRadius > 0f))) return $"{id}: fangLength >= 0, fangRadius > 0";
            if (direction.sqrMagnitude < 1e-8f) return $"{id}: direction is zero";
            if (!string.IsNullOrEmpty(on) && !(c.Find(on) is DimpledOrb)) return $"{id}: on '{on}' is not a DimpledOrb";
            return null;
        }

        // The face's frame in the joint frame: +Z the facing direction, +Y the joint's up made perpendicular.
        public Quaternion LocalFrame()
        {
            Vector3 d = PartMeshEmitter.Unit(direction, Vector3.forward);
            Vector3 up = Vector3.up - d * Vector3.Dot(Vector3.up, d);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.forward - d * Vector3.Dot(Vector3.forward, d);
            return Quaternion.LookRotation(d, up.normalized);
        }

        // Eye k (0 .. eyeCount-1) on the cap: azimuth (deg, + to the right), elevation (deg, + up) and radius factor.
        static readonly float[] kAz = { 20f, 42f, 9f, 58f }, kEl = { 22f, 34f, 48f, 12f }, kSize = { 1f, 0.68f, 0.55f, 0.5f };
        public void Eye(int k, out float az, out float el, out float size)
        {
            int pairs = eyeCount / 2;
            if (k < 2 * pairs) { int p = k / 2; az = (k % 2 == 0 ? -1f : 1f) * kAz[p]; el = kEl[p]; size = kSize[p]; }
            else { az = 0f; el = 58f; size = 0.6f; }   // the odd one, on top
        }

        // A point on the cap ellipsoid (frame: x right, y up, z facing) at azimuth / elevation from its front, and its normal.
        Vector3 Cap(float azDeg, float elDeg, out Vector3 normal)
        {
            float a = azDeg * Mathf.Deg2Rad, e = elDeg * Mathf.Deg2Rad;
            Vector3 u = new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
            float ax = 0.5f * width, ay = 0.5f * height, az = depth;
            normal = new Vector3(u.x / ax, u.y / ay, u.z / az).normalized;
            return new Vector3(u.x * ax, u.y * ay, u.z * az);
        }

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            CreatureSpecies sp = m.Species;
            int bone = at.joint;   // the spine bone of its joint
            if (!string.IsNullOrEmpty(on))
            {
                IReadOnlyList<CreaturePart> parts = KitView.Parts(sp);
                for (int i = 0; i < parts.Count; i++) if (parts[i].id == on) bone = sp.Layout.Start(i);
            }
            Quaternion R = m.Rest.Rotations[at.joint] * LocalFrame();
            Vector3 centre = m.Rest.Joints[at.joint] + m.Rest.Rotations[at.joint] * at.offset;
            Matrix4x4 frame = Matrix4x4.TRS(centre, R, Vector3.one);
            int sides = 12;
            // the cap: a half-buried ellipsoid (its back half sinks into the body)
            m.Ellipsoid(frame, Vector3.zero, 0.5f * width, 0.5f * height, depth, sides, 8, layer, bone);
            // the eyes: spheres sunk 40 % into the cap
            for (int k = 0; k < eyeCount; k++)
            {
                Eye(k, out float az, out float el, out float size);
                Vector3 p = Cap(az, el, out Vector3 n);
                float r = eyeRadius * size;
                Vector3 ec = p + n * (0.4f * r);
                Matrix4x4 ef = Matrix4x4.TRS(frame.MultiplyPoint3x4(ec), R * Quaternion.LookRotation(n, Vector3.up), Vector3.one);
                m.Ellipsoid(ef, Vector3.zero, r, r, r, 10, 6, PartMeshEmitter.PartEye, bone);
            }
            // the fangs: from the cap's lower front, hanging down and a little forward, curling back
            if (fangLength > 0f)
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 b = Cap(0f, -42f, out Vector3 nb) + new Vector3(s * 0.5f * fangSpread * width, 0f, 0f);
                    Vector3 down = new Vector3(-s * 0.12f, -1f, 0.3f).normalized;   // a little inward, forward
                    Quaternion q = R * Quaternion.LookRotation(down, new Vector3(0f, 0f, 1f));   // Horn curls toward its -Y: back, to the body
                    m.Horn(frame.MultiplyPoint3x4(b - nb * (0.3f * fangRadius)), q, fangRadius, 0f, fangLength, fangCurveDeg, bone, PartMeshEmitter.PartFoot, 8, 0f, out _, out _);
                }
        }
    }
}
