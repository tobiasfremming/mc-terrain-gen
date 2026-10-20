// Creature kit (sky manta): School - flying (or swimming) in a loose group: every member steers at a point a few
// spans ahead along a blend of the group's shared waypoint (a new one every so often, somewhere in the enclosure at
// a height in the band), cohesion to the group's centre, alignment with the group's heading, separation from
// neighbours inside `separation` spans (strongest, and in 3D), the enclosure's wall, and its obstacles (the aviary's
// trees and rocks: their perch spots as spheres to keep clear of, their trunks as cylinders below them). It vetoes the
// locomotion's manoeuvres (Soar.ManoeuvreAllowed) while a neighbour is near.
// Members find each other through the kit's group service (KitGroup, Kit/Services/KitGroups.cs), keyed by the species
// and the enclosure: every individual of a lab group shares it. A member counts while its sim's clock runs with the
// asker's (a sim nobody ticks any more drops out), so a group respawned in place or a test's fresh sims never steer
// round ghosts; the school's waypoint is the group's shared state (SchoolGroup). Joining allocates (once); the
// per-frame steering does not. Main thread.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Module("School", CreatureModule.Behaviour, Summary = "Flying in a loose group: separation (3D, strongest), alignment, cohesion, a shared waypoint that moves round the enclosure at a height in the band, the wall and the aviary's obstacles; vetoes the locomotion's manoeuvres while a neighbour is near. Members of a lab group find each other by species and enclosure.",
            Needs = "an airborne locomotion (Soar)", KitOnly = true)]
    public sealed class School : CreatureModule
    {
        [Param(ParamUnit.Fraction, "keep at least this many spans from a neighbour (separation acts inside it)", Min = 0.5f, Max = 6, Example = "manta 3")] public float separation = 3f;
        [Param(ParamUnit.Fraction, "weight of the pull to the group's centre", Min = 0, Max = 3)] public float cohesion = 0.35f;
        [Param(ParamUnit.Fraction, "weight of the group's mean heading", Min = 0, Max = 3)] public float alignment = 0.8f;
        [Param(ParamUnit.Fraction, "weight of the shared waypoint", Min = 0, Max = 3)] public float waypoint = 1f;
        [Param(ParamUnit.Seconds, "a new shared waypoint about this often")] public float waypointSeconds = 9f;
        [Param(ParamUnit.Fraction, "height band of the waypoints: lowest, as a fraction of the room under the roof", Min = 0, Max = 1)] public float bandLow = 0.35f;
        [Param(ParamUnit.Fraction, "height band of the waypoints: highest", Min = 0, Max = 1)] public float bandHigh = 0.75f;
        [Param(ParamUnit.Fraction, "no manoeuvres while a neighbour is within this many spans (or will come within 3/4 of it in the next seconds)", Min = 0, Max = 10)] public float manoeuvreGap = 2.8f;

        public override string Validate(in SpeciesCtx c)
        {
            if (bandHigh < bandLow) return "School: bandLow <= bandHigh";
            for (int i = 0; i < c.Modules.Count; i++) if (c.Modules[i] is Soar) return null;
            return "School needs an airborne locomotion (Soar)";
        }

        public override IBehaviour CreateBehaviour(in BehaviourCtx c) => new SchoolBehaviour(this, c);
    }

    // The school's state shared by its group (KitGroup.Shared: one per group, made again when the group starts over): the
    // shared waypoint and the group's own random stream.
    public sealed class SchoolGroup
    {
        public Vector3 Waypoint;
        public float WaypointTime = float.NegativeInfinity;
        public uint Rng = 0x2545F491u;

        public float Rand()
        {
            Rng ^= Rng << 13; Rng ^= Rng >> 17; Rng ^= Rng << 5;
            return (Rng & 0xFFFFFFu) / 16777216f;
        }
    }

    public sealed class SchoolBehaviour : IBehaviour
    {
        public const float BaseUtility = 0.4f;

        readonly School p;
        readonly CreatureSpecies sp;
        readonly EnclosureArea area;
        readonly PerchSpot[] perches;
        readonly int seed;
        KitGroup group;
        SoarFlight soar;
        float pruneLeft;

        public KitGroup Group => group;
        public float NearestNeighbour { get; private set; } = float.PositiveInfinity;

        public SchoolBehaviour(School school, in BehaviourCtx c)
        {
            p = school; sp = c.Species; area = c.Area; perches = c.Perches; seed = c.Seed;
        }

        public float Utility(in BrainCtx c) => BaseUtility;

        public void Enter(in BrainCtx c)
        {
            if (group == null) { group = KitGroup.For(sp, area); group.Join(c.Sim); }
            soar = c.Sim.LocomotionMode<SoarFlight>();
        }

        public BrainOutput Tick(in BrainCtx c, float dt)
        {
            CreatureSim me = c.Sim;
            if (group == null) Enter(c);
            Vector3 up = area.up.sqrMagnitude > 0.5f ? area.up : Vector3.up;
            Vector3 P = me.Joints[0];
            float s = me.SizeScale;
            float span = soar != null ? 2f * soar.HalfSpan : 2f * me.Species.MaxLateralReach * s;
            Vector3 F = soar != null ? soar.Forward : me.Rotations[0] * Vector3.forward;

            if ((pruneLeft -= dt) <= 0f) { pruneLeft = 5f; group.Prune(me); }

            // the shared waypoint: whoever finds it stale picks the next (the group's own random stream)
            SchoolGroup school = group.Shared<SchoolGroup>();
            if (me.Time - school.WaypointTime > p.waypointSeconds * me.TimeScale || me.Time < school.WaypointTime) NewWaypoint(me, up, school);

            Vector3 sep = Vector3.zero, centre = Vector3.zero, heading = Vector3.zero;
            int k = 0;
            float nearest = float.PositiveInfinity, nearNow = float.PositiveInfinity, urgency = 0f;
            float keep = p.separation * span, horizon = 3f * me.TimeScale;
            Vector3 myV = F * me.Speed;
            for (int i = 0; i < group.Members.Count; i++)
            {
                if (!group.Live(i, me)) continue;
                CreatureSim o = group.Members[i];
                Vector3 q = o.Joints[0];
                // the closest approach within the horizon if both fly on as they are
                Vector3 d = P - q;
                Vector3 vr = myV - (o.Rotations[0] * Vector3.forward) * o.Speed;
                float vv = vr.sqrMagnitude;
                float tc = vv > 1e-8f ? Mathf.Clamp(-Vector3.Dot(d, vr) / vv, 0f, horizon) : 0f;
                Vector3 dc = d + vr * tc;
                nearNow = Mathf.Min(nearNow, d.magnitude / Mathf.Max(span, 1e-3f));
                if (dc.sqrMagnitude < d.sqrMagnitude) d = dc;
                float dist = d.magnitude;
                nearest = Mathf.Min(nearest, dist / Mathf.Max(span, 1e-3f));
                // a neighbour in a roll or a loop sweeps more room and cannot dodge: keep further from it
                SoarFlight os = o.LocomotionMode<SoarFlight>();
                float keepO = os != null && os.Manoeuvre != SoarManoeuvre.None ? 1.5f * keep : keep;
                if (dist < keepO)
                {
                    float w = (keepO - dist) / keepO;
                    Vector3 away = dist > 1e-4f ? d / dist : Vector3.zero;
                    // head on: both turn to their own right (they pass left to left), and a little apart in height
                    float headOn = Mathf.Clamp01(-Vector3.Dot(F, o.Rotations[0] * Vector3.forward));
                    Vector3 right = Vector3.Cross(up, F).normalized;
                    away += right * (0.8f * headOn * w) + up * ((me.Seed < o.Seed ? 0.25f : -0.25f) * w);
                    sep += away * (w * w * 8f);
                    urgency = Mathf.Max(urgency, w);
                }
                centre += q; heading += o.Rotations[0] * Vector3.forward; k++;
            }
            NearestNeighbour = nearest;
            Vector3 want = Vector3.zero;
            Vector3 toWp = school.Waypoint - P;
            want += toWp.normalized * p.waypoint;
            if (k > 0)
            {
                centre /= k;
                Vector3 toC = centre - P;
                float cd = toC.magnitude;
                if (cd > 3f * span) want += toC / cd * p.cohesion * Mathf.Clamp01((cd - 3f * span) / (4f * span));
                if (heading.sqrMagnitude > 1e-6f) want += heading.normalized * p.alignment;
                // the closer a neighbour comes, the less anything else counts
                want = want * Mathf.Clamp01(1f - 1.4f * urgency) + sep;
            }
            // obstacles: perch spots as spheres, the trunks below them as cylinders
            want += Obstacles(P, F, up, span);
            // the wall: turn in well before it (the locomotion's own turn-back is the last resort, blind to neighbours)
            {
                Vector3 d = Vector3.ProjectOnPlane(P - area.center, up);
                Vector3 ahead = d + Vector3.ProjectOnPlane(F, up) * (me.Speed * 2f * me.TimeScale);
                float room = area.radius - 2f * span, r = ahead.magnitude;
                if (r > 0.65f * room && r > 1e-4f) want += -ahead / r * (2.5f * Mathf.Clamp01((r - 0.65f * room) / (0.35f * room)));
            }
            if (want.sqrMagnitude < 1e-8f) want = F;
            want.Normalize();
            if (soar != null) { soar.ManoeuvreAllowed = nearNow > p.manoeuvreGap && nearest > 0.75f * p.manoeuvreGap; soar.Urgency = urgency; }
            return BrainOutput.Toward(P + want * (3f * span));
        }

        void NewWaypoint(CreatureSim me, Vector3 up, SchoolGroup group)
        {
            group.WaypointTime = me.Time;
            float s = me.SizeScale;
            float span = soar != null ? 2f * soar.HalfSpan : 1f;
            float room = Mathf.Max(0.5f, area.radius - 2.5f * span);
            float a = group.Rand() * 2f * Mathf.PI, r = room * Mathf.Sqrt(Mathf.Lerp(0.15f, 0.7f, group.Rand()));
            Vector3 x = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.forward).normalized;
            Vector3 z = Vector3.Cross(x, up);
            float roof = area.maxHeight > 0f ? area.maxHeight : 12f * s;
            float lo = Mathf.Lerp(0f, roof, p.bandLow), hi = Mathf.Lerp(0f, roof, p.bandHigh);
            group.Waypoint = area.center + (x * Mathf.Cos(a) + z * Mathf.Sin(a)) * r + up * Mathf.Lerp(lo, hi, group.Rand());
        }

        Vector3 Obstacles(Vector3 P, Vector3 F, Vector3 up, float span)
        {
            Vector3 push = Vector3.zero;
            float top = float.NegativeInfinity;
            if (perches != null)
                for (int i = 0; i < perches.Length; i++)
                {
                    Vector3 c = perches[i].point;
                    top = Mathf.Max(top, Vector3.Dot(c - area.center, up));
                    Vector3 d = P - c;
                    float keep = perches[i].radius + 1.5f * span, dist = d.magnitude;
                    if (dist < keep && dist > 1e-4f) push += d / dist * (3f * (keep - dist) / keep);
                }
            float h = Vector3.Dot(P - area.center, up);
            for (int i = 0; i < area.ObstacleCount; i++)
            {
                if (perches != null && perches.Length > 0 && h > top + span) continue;
                Vector3 d = P - area.obstacleCenters[i];
                d -= up * Vector3.Dot(d, up);
                float keep = area.obstacleRadii[i] + 1.5f * span, dist = d.magnitude;
                if (dist < keep && dist > 1e-4f) push += d / dist * (3f * (keep - dist) / keep);
            }
            return push;
        }
    }
}
