// Creature kit T5.2: the Flee behaviour - run from a threat that comes close, then calm down. A threat is a point
// in CreatureThreats (the viewer: CreatureThreatSource puts the camera there in play mode; tests and other systems
// add their own). Utility rises as the nearest threat closes in inside the alarm radius and stays up for a calm-
// down time after it leaves, so a ModularBrain (BrainFactory: any species with Flee gets one) switches to it and
// back without flicker (its minimum hold and margin on top). The target is a point straight away from the threat,
// kept inside the enclosure's safe disc and out of obstacles: against the wall it slides along it, round the side
// that leads away from the threat. Containment stays structural (the sim clamps the head to the safe disc anyway).
// Froude: distances x s, times x sqrt s. Allocation-free per tick; main thread.
using System;
using UnityEngine;

namespace CreatureKit
{
    // Points creatures flee from (world space). A tiny fixed table, no allocation: slots are set and cleared by
    // their owners (CreatureThreatSource, tests). Main thread.
    public static class CreatureThreats
    {
        public const int Max = 8;
        static readonly Vector3[] points = new Vector3[Max];
        static readonly bool[] used = new bool[Max];

        public static int Count { get { int n = 0; for (int i = 0; i < Max; i++) if (used[i]) n++; return n; } }

        // A free slot (or -1 when all are taken).
        public static int Acquire() { for (int i = 0; i < Max; i++) if (!used[i]) { used[i] = true; points[i] = Vector3.zero; return i; } return -1; }
        public static void Set(int slot, Vector3 p) { if (slot >= 0 && slot < Max) { used[slot] = true; points[slot] = p; } }
        public static void Release(int slot) { if (slot >= 0 && slot < Max) used[slot] = false; }
        public static void Clear() { for (int i = 0; i < Max; i++) used[i] = false; }

        // The nearest threat to `at`, distance measured in the plane perpendicular to `up` (a viewer above still
        // counts by how close it stands). False when there is none.
        public static bool Nearest(Vector3 at, Vector3 up, out Vector3 threat, out float distance)
        {
            threat = Vector3.zero; distance = float.MaxValue;
            bool any = false;
            for (int i = 0; i < Max; i++)
            {
                if (!used[i]) continue;
                Vector3 d = points[i] - at; d -= up * Vector3.Dot(d, up);
                float m = d.magnitude;
                if (m < distance) { distance = m; threat = points[i]; any = true; }
            }
            return any;
        }
    }

    [Serializable]
    [Module("Flee", CreatureModule.Behaviour, Summary = "Runs straight away from the nearest threat (CreatureThreats: the viewer in play mode) once it comes within the alarm radius, sliding along the enclosure wall when cornered; calms down after it leaves. Gives the species a ModularBrain (with Wander as the idle default).",
            Needs = "Walk")]
    public sealed class Flee : CreatureModule
    {
        [Param(ParamUnit.Metres, "alarm radius: a threat closer than this (planar) starts the flight", Example = "sand eel 1.2")] public float radius = 1.2f;
        [Param(ParamUnit.Metres, "how far ahead of the head the escape target lies")] public float runDistance = 0.8f;
        [Param(ParamUnit.Seconds, "keeps fleeing this long after the threat left the radius")] public float calmSeconds = 1.5f;
        [Param(ParamUnit.Fraction, "utility with the threat at the head (Wander idles at 0.1)", Min = 0.2f, Max = 2)] public float utility = 0.8f;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Module<Walk>() == null) return "Flee needs Walk";
            if (!(radius > 0f) || !(runDistance > 0f) || calmSeconds < 0f || utility < 0.2f) return "Flee: radius, runDistance > 0, calmSeconds >= 0, utility >= 0.2";
            return null;
        }

        public override IBehaviour CreateBehaviour(in BehaviourCtx c) => new FleeBehaviour(this);
    }

    public sealed class FleeBehaviour : IBehaviour
    {
        readonly Flee f;
        float lastSeen = float.NegativeInfinity;   // brain clock when a threat was last inside the radius
        Vector3 lastThreat;
        int slide;                                 // +1 / -1: which way along the wall (chosen once per flight)

        public FleeBehaviour(Flee flee) { f = flee; }

        public bool Alarmed { get; private set; }

        // The utility with no threat about. Not 0: the idle Wander (0.1) must beat it by ModularBrain's margin (0.1)
        // to take over again (found by Flee_SwitchesInAndCalmsDown: at 0 the eel fled forever).
        public const float Calm = -1f;

        float Radius(CreatureSim s) => f.radius * s.SizeScale;

        public float Utility(in BrainCtx c)
        {
            CreatureSim s = c.Sim;
            Vector3 up = c.Area.up.sqrMagnitude > 0.5f ? c.Area.up : Vector3.up;
            float r = Radius(s);
            if (CreatureThreats.Nearest(s.Joints[0], up, out Vector3 t, out float d) && d < r)
            {
                lastSeen = c.Time; lastThreat = t;
                Alarmed = true;
                // 0.5 x utility at the edge of the radius .. utility at the head: always above the idle wander
                return f.utility * (0.5f + 0.5f * (1f - d / r));
            }
            Alarmed = false;
            float calm = f.calmSeconds * s.TimeScale;
            // calm: below any idle behaviour by more than the brain's switch margin, so it hands back at once
            return c.Time - lastSeen <= calm ? 0.5f * f.utility : Calm;
        }

        public void Enter(in BrainCtx c) { slide = 0; }

        public BrainOutput Tick(in BrainCtx c, float dt)
        {
            CreatureSim s = c.Sim;
            EnclosureArea a = c.Area;
            Vector3 up = a.up.sqrMagnitude > 0.5f ? a.up : Vector3.up;
            Vector3 head = s.Joints[0];
            if (CreatureThreats.Nearest(head, up, out Vector3 t, out float d) && d < Radius(s)) lastThreat = t;
            Vector3 away = head - lastThreat; away -= up * Vector3.Dot(away, up);
            if (away.sqrMagnitude < 1e-8f) away = s.Rotations[0] * Vector3.forward;   // threat on top of it: keep going
            away.Normalize();
            float run = f.runDistance * s.SizeScale;
            Vector3 target = head + away * run;
            if (a.safeRadius > 0f && a.PlanarDistance(target) > a.safeRadius)
            {
                // cornered: slide along the wall, round the side that leads away from the threat
                Vector3 radial = head - a.center; radial -= up * Vector3.Dot(radial, up);
                if (radial.sqrMagnitude < 1e-8f) radial = away;
                Vector3 tangent = Vector3.Cross(up, radial.normalized);
                if (slide == 0) slide = Vector3.Dot(tangent, away) >= 0f ? 1 : -1;
                target = a.ClampToSafe(head + (tangent * slide * 0.8f - radial.normalized * 0.2f).normalized * run);
            }
            target = a.ClampToSafe(target);
            int o = a.ObstacleAt(target);
            if (o >= 0)
            {
                // out of the obstacle's keep-out disc, radially
                Vector3 r = target - a.obstacleCenters[o]; r -= up * Vector3.Dot(r, up);
                if (r.sqrMagnitude < 1e-8f) r = Vector3.Cross(up, away);
                target = a.ClampToSafe(a.obstacleCenters[o] + r.normalized * (a.obstacleRadii[o] + a.keepOut + 0.05f * s.SizeScale) + up * Vector3.Dot(target - a.center, up));
            }
            return BrainOutput.Toward(target);
        }
    }
}
