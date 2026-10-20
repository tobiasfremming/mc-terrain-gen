// Creature kit, arthropod archetype: the crab's rock tuck, DERIVED from the legs' and the shell's known geometry (in
// place of the search-based FoldFit the hand-made stone crab needed). The body crouches until the shell's lowest point
// stands just over one leg thickness off the ground; then, in each limb's own frame (joint frame, x outward):
//   walking leg: coxa and basis drop from the shoulder to the ground along the leg's rest direction; the merus lies on
//     the ground swept toward the tail just far enough that its knee sits at the shell's edge (the knees line the margin
//     like teeth); carpus and propodus fold back beside it, under the shell; the dactyl points down into the ground;
//   cheliped: the ischium drops forward, merus and carpus bring the propodus in front of the face, and the claw lies
//     across it on the ground pointing to the other side (the cutter against the face, the crusher in front of it).
// Each segment's direction becomes a LegFold row (segmentYawDeg / pitchDeg: Tuck's convention, x outward, + toward
// the tail). Pure geometry: compose time only.
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public static class CrabFold
    {
        // Rock crab timings (the shipped stone crab's): curl, hold, peek, uncurl; a scare below 0.25 is ignored.
        public const float CurlSeconds = 0.4f, HoldSeconds = 4.5f, PeekSeconds = 1.4f, UncurlSeconds = 0.7f;

        static float YawOf(Vector3 d) => Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg;
        static float PitchOf(Vector3 d) => Mathf.Asin(Mathf.Clamp(d.y / Mathf.Max(1e-6f, d.magnitude), -1f, 1f)) * Mathf.Rad2Deg;
        // a horizontal direction at `yaw` (Tuck's convention: x outward, + toward the tail) pitched by `pitch`
        static Vector3 Dir(float yaw, float pitch)
        {
            float y = yaw * Mathf.Deg2Rad, p = pitch * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(y) * Mathf.Cos(p), Mathf.Sin(p), -Mathf.Sin(y) * Mathf.Cos(p));
        }

        internal static Tuck Make(string id, ArthropodPlan plan, ArthropodPlan.Shell S, RigidLoft shell, JointDef[] joints,
                                  ArthropodLeg[] walking, List<ArthropodLeg> arms, float scareSeconds)
        {
            float h = joints[0].clearance;
            // the shell's lowest point (joint frame; the joints at y = 0)
            float lowest = ArthropodPlan.LowestPoint(shell);
            // the folded legs lie flat on the ground under the shell: the shell comes down onto them (its lowest point
            // just over the thickest folded segment)
            float pile = 0f;
            foreach (ArthropodLeg l in walking) for (int s = 1; s < l.segments.Length; s++) pile = Mathf.Max(pile, 2f * HalfHeight(l.segments[s]));
            pile += 0.01f * S.CL;
            float gap = h + lowest;
            float crouch = Mathf.Max(0f, gap - pile);
            float ground = -(h - crouch);   // the ground's height in the joint frame, crouched

            var rows = new List<LegFold>();
            int n = walking.Length;
            for (int p = 0; p < n; p++)
            {
                float lag = n == 4 ? new[] { 0.15f, 0.05f, 0f, 0.1f }[p] : 0.05f * (p % 3);
                rows.Add(WalkingFold(walking[p], S, ground, lag));
            }
            // the claws' propodi end in front of the face: the cutter's palm against it, the crusher's in front of that
            float faceZ = S.zFront + 0.02f * S.CL;   // joint 0 frame
            float cutterW = 0f, crusherW = 0f;
            foreach (ArthropodLeg a in arms) { float w = a.segments[a.segments.Length - 1].length / 0.096f * 0.157f; if (a.at.side > 0) crusherW = w; else cutterW = w; }
            foreach (ArthropodLeg a in arms)
            {
                bool crusher = a.at.side > 0;
                float z = faceZ + 0.5f * cutterW + (crusher ? 0.5f * (cutterW + crusherW) : 0f);
                rows.Add(ClawFold(a, ground, z));
            }
            return new Tuck
            {
                id = id, curlSeconds = CurlSeconds, holdSeconds = HoldSeconds, peekSeconds = PeekSeconds, uncurlSeconds = UncurlSeconds,
                crouch = crouch, randomScareSeconds = scareSeconds, minScare = 0.25f, legs = rows.ToArray(),
            };
        }

        const float kFoldOut = 28f;   // degrees the carpus folds back past the merus, to its outer side
        const float kClawElbow = 60f; // degrees the cheliped's carpus turns in from its merus

        // A segment's half height (flattened in the leg plane) at its thicker end.
        static float HalfHeight(in SegmentDef s) => Mathf.Max(s.radiusStart, s.radiusEnd) * (1f - Mathf.Clamp(s.flatten, 0f, 0.8f));

        // The walking leg (see the header), in joint 0's frame on the leg's right-side geometry: the coxa drops from
        // the shoulder, every later segment lies flat on the ground (its axis its half height up); the merus is swept
        // toward the tail until its knee sits just outside the shell's edge, carpus and propodus fold back beside it
        // under the shell, the dactyl's tip in the ground.
        static LegFold WalkingFold(ArthropodLeg leg, ArthropodPlan.Shell S, float ground, float lag)
        {
            SegmentDef[] g = leg.segments;
            int n = g.Length;
            float zj = -leg.at.joint * S.spacing;   // the attach joint's z in joint 0's frame
            Vector3 sh = leg.at.offset; sh.z += zj;
            Vector3 rest = leg.restOffset; rest.z += zj;
            Vector3 u = rest - sh; u.y = 0f; u.Normalize();
            float restYaw = YawOf(u);
            var yaw = new float[n]; var pitch = new float[n];
            // the coxa drops to the basis' height on the ground (and the basis further, if the coxa is too short)
            float y0 = ground + HalfHeight(g[1]);
            float p0 = -Mathf.Asin(Mathf.Clamp01((sh.y - y0) / g[0].length)) * Mathf.Rad2Deg;
            Vector3 a = sh + Dir(restYaw, p0) * g[0].length;
            float p1 = Mathf.Asin(Mathf.Clamp((ground + HalfHeight(g[2]) - a.y) / g[1].length, -1f, 1f)) * Mathf.Rad2Deg;
            yaw[0] = yaw[1] = restYaw; pitch[0] = p0; pitch[1] = p1;
            Vector3 knee0 = a + Dir(restYaw, p1) * g[1].length;
            // the merus swept toward the tail until its knee sits just outside the shell's edge (0.3 of its radius)
            float best = restYaw, bestErr = float.PositiveInfinity;
            for (int k = 0; k <= 85; k++)
            {
                float y = restYaw + k;
                Vector3 kn = knee0 + Dir(y, 0f) * g[2].length;
                float edge = S.HalfWidth(Mathf.Clamp01(S.T(kn.z))) + 0.3f * g[2].radiusEnd;
                float err = Mathf.Abs(kn.x - edge);
                if (err < bestErr) { best = y; bestErr = err; }
            }
            yaw[2] = best; pitch[2] = 0f;
            // carpus and propodus fold back along the merus on its outer side (a Z flat on the ground round the rim,
            // never under the low shell); the dactyl's tip into the ground
            Vector3 knee = knee0 + Dir(best, 0f) * g[2].length;
            float back = (knee + Dir(best + 180f - kFoldOut, 0f) * g[3].length).x >= (knee + Dir(best + 180f + kFoldOut, 0f) * g[3].length).x ? -kFoldOut : kFoldOut;
            yaw[3] = best + 180f + back; pitch[3] = 0f;
            if (n > 4) { yaw[4] = best + 180f + 0.4f * back; pitch[4] = 0f; }
            for (int s = 5; s < n; s++) { yaw[s] = best + 180f; pitch[s] = -25f; }
            return new LegFold { leg = leg.id, yawDeg = Mathf.Clamp(restYaw, -90f, 90f), pitchDeg = pitch, segmentYawDeg = Wrap(yaw), lag = lag };
        }

        // The cheliped (see the header): the propodus' axis across the face at z (joint 0 frame), on the ground.
        static LegFold ClawFold(ArthropodLeg arm, float ground, float z)
        {
            SegmentDef[] g = arm.segments;
            int n = g.Length;
            Vector3 sh = arm.at.offset; sh.x = Mathf.Abs(sh.x);
            var yaw = new float[n]; var pitch = new float[n];
            // the ischium drops to the merus lying on the ground; the carpus rises to the propodus, its axis the palm's
            // half height up (Chela: palmHeight 0.218 / radius 0.06 of the arm's scale)
            float palm = g[n - 1].radiusStart * (0.5f * 0.218f / 0.06f);
            float r1 = Mathf.Max(g[1].radiusStart, g[1].radiusEnd);
            float drop = sh.y - (ground + r1);
            float p0 = -Mathf.Asin(Mathf.Clamp01(drop / g[0].length)) * Mathf.Rad2Deg;
            float p1 = -Mathf.Asin(Mathf.Clamp01((drop - g[0].length) / g[1].length)) * Mathf.Rad2Deg;
            float p2 = Mathf.Asin(Mathf.Clamp((palm - r1) / g[2].length, -1f, 1f)) * Mathf.Rad2Deg;
            yaw[0] = -90f; pitch[0] = p0;
            Vector3 a = sh + Dir(-90f, p0) * g[0].length;
            // the merus out and forward, the carpus in and forward, the propodus straight ahead: the claw (turned across
            // its propodus, Chela.yawDeg) then lies across the face; the merus' yaw that brings the propodus' end to z
            float bestY = -60f, bestErr = float.PositiveInfinity;
            for (float y = -20f; y >= -90f; y -= 1f)
            {
                Vector3 b = a + Dir(y, p1) * g[1].length + Dir(y - kClawElbow, 0f) * g[2].length;
                for (int s = 3; s < n; s++) b += Dir(-90f, 0f) * g[s].length;
                float err = Mathf.Abs(b.z - z);
                if (err < bestErr) { bestErr = err; bestY = y; }
            }
            yaw[1] = bestY; pitch[1] = p1;
            yaw[2] = bestY - kClawElbow; pitch[2] = p2;
            for (int s = 3; s < n; s++) { yaw[s] = -90f; pitch[s] = 0f; }
            return new LegFold { leg = arm.id, yawDeg = 0f, pitchDeg = pitch, segmentYawDeg = Wrap(yaw), lag = 0f };
        }

        static float[] Wrap(float[] y)
        {
            for (int i = 0; i < y.Length; i++) { y[i] = Mathf.Repeat(y[i] + 180f, 360f) - 180f; }
            return y;
        }
    }
}
