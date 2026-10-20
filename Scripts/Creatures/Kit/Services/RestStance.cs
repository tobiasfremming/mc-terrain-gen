// Creature kit service: the rest stance of an arthropod leg, from the leg's own rest pose (the legacy spider
// generator's ground_radius, legs_impl/gen_species.py). An archetype places each foot where the leg's rest shape puts
// it and sets the body height so the reference leg stands unbent; the IK then draws the leg exactly as authored
// (reach synergy s = 0, leg plane barely pitched) instead of bending and tilting it to reach a hand-typed rest point.
//   Foot          the rest pose's foot relative to the coxa (x out, y up) on level ground, as the IK draws it at s = 0
//   CoxaHeight    how high the coxa stands when that foot is on the ground (its natural drop)
//   GroundRadius  the radial foot distance on level ground coxaHeight below the coxa that keeps the rest pose's
//                 coxa-to-ankle distance (s stays 0; only the leg plane's pitch changes)
//   Report        a species leg's rest stance through the real IK (s, pitch, knee height, clamped): what the tests and
//                 the look loop measure
// Pure: no Unity objects, no per-frame use (compose time and tests only; it allocates a leg model per call).
using System;
using UnityEngine;

namespace CreatureKit
{
    // A leg's rest stance through the IK. s: the reach synergy (deg; 0 = the authored shape); pitchDeg: the leg plane's
    // pitch; kneeAboveBody: the highest leg point above the attach joint's top (m, scale 1); clamped: out of reach.
    public struct RestReport { public float s, pitchDeg, kneeAboveBody; public bool clamped; }

    public static class RestStance
    {
        static ArthropodLegModel Model(SegmentDef[] segs, float contactDeg)
        {
            var ls = new LegSegment[segs.Length];
            for (int i = 0; i < segs.Length; i++) ls[i] = segs[i].ToRuntime();
            var prm = ArthropodLegParams.Default; prm.contactAngleDeg = contactDeg;
            var m = new ArthropodLegModel(ls, prm);
            if (m.Error != null) throw new InvalidOperationException("leg template: " + m.Error);
            return m;
        }

        public static Vector2 Foot(SegmentDef[] segs, float contactDeg) => Model(segs, contactDeg).RestFootLocal();

        public static float CoxaHeight(SegmentDef[] segs, float contactDeg) => -Foot(segs, contactDeg).y;

        public static float GroundRadius(SegmentDef[] segs, float contactDeg, float coxaHeight)
        {
            Vector2 foot = Foot(segs, contactDeg);
            float t = segs[segs.Length - 1].length, c = contactDeg * Mathf.Deg2Rad;
            var ankle = new Vector2(foot.x - t * Mathf.Cos(c), foot.y + t * Mathf.Sin(c));
            float d = ankle.magnitude, ha = coxaHeight - t * Mathf.Sin(c);
            float horiz = Mathf.Sqrt(Mathf.Max(d * d - ha * ha, 0.09f * d * d));
            return horiz + t * Mathf.Cos(c);
        }

        // The species' leg l standing at rest on flat ground (attach-joint frame, scale 1).
        public static RestReport Report(CreatureSpecies sp, int l)
        {
            LegSpec q = sp.legs[l];
            if (!q.IsArthropod) throw new ArgumentException($"leg {l} is not an arthropod leg");
            ArthropodLegModel m = sp.LegModel(l);
            if (m.Error != null) throw new InvalidOperationException($"leg {l}: {m.Error}");
            var sh = new Vector3(q.side * q.shoulderOffset.x, q.shoulderOffset.y, q.shoulderOffset.z);
            var rs = new Vector3(q.side * q.restOffset.x, -sp.joints[q.attachJoint].clearance, q.restOffset.z);
            Vector3 ry = rs; ry.y = 0f;
            var pts = new Vector3[m.PointCount];
            ArthropodLegIK.Solve(m, sh, rs, Vector3.up, Vector3.up, ry.normalized, 0f, 1f, pts, 0, out ArthropodLegPose pose);
            float top = float.NegativeInfinity;
            for (int i = 0; i < pts.Length - 1; i++) top = Mathf.Max(top, pts[i].y);
            return new RestReport { s = pose.s, pitchDeg = pose.pitchDeg, clamped = pose.clamped, kneeAboveBody = top - sp.joints[q.attachJoint].halfHeight };
        }
    }
}
