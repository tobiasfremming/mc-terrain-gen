// Creature kit (2026-10-20, the Delver): behaviours for a digger's day, for a ModularBrain (Kit/Brains.cs) beside
// Wander (the idle default, utility 0.1):
//   Tunnel   every so often (mean surfaceSeconds on the surface between trips, per individual) it goes underground: a
//            goal across the enclosure, a trip the Burrow part plans through the enclosure's tunnels (reusing them), and
//            it holds until the trip is over. Never while too many of its group are under already (maxHiddenShare).
//   Forage   walks a little way, then sniffs and scratches at the ground (Burrow's Scratch posture) for a while.
//   Bask     lies down where it is: curled with the head under its hood, or flat in the sun (Burrow's Rest posture).
//   Follow   trails a visible member of its group (same species, same enclosure) at a distance.
// Each comes due on its own per-individual clock (seeded, Froude-timed: x sqrt s), runs for its time and goes quiet
// again; while it runs it holds a high utility so the brain does not drop it half way. Targets stay inside the safe
// disc (containment is structural anyway). Allocation-free per tick; main thread.
using System;
using UnityEngine;

namespace CreatureKit
{
    static class DiggerUtil
    {
        public static float Hash01(int k)
        {
            uint x = (uint)k * 0x9E3779B1u;
            x ^= x >> 16; x *= 0x7FEB352Du; x ^= x >> 15; x *= 0x846CA68Bu; x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777216f;
        }

        public static BurrowRuntime Runtime(CreatureSim sim)
        {
            IPartRuntime[] r = sim.PartRuntimes;
            for (int i = 0; i < r.Length; i++) if (r[i] is BurrowRuntime b) return b;
            return null;
        }

        // A point in the safe disc at `distance` from `from` toward a hashed direction (clamped inside the disc).
        public static Vector3 PointFrom(in EnclosureArea a, Vector3 from, float distance, float angle01, float margin)
        {
            Vector3 up = a.up.sqrMagnitude > 0.5f ? a.up : Vector3.up;
            Vector3 x = Vector3.Cross(up, Mathf.Abs(up.z) < 0.9f ? Vector3.forward : Vector3.right).normalized, z = Vector3.Cross(x, up);
            float ang = 2f * Mathf.PI * angle01;
            Vector3 p = from + (x * Mathf.Cos(ang) + z * Mathf.Sin(ang)) * distance;
            return Clamp(a, p, margin);
        }

        public static Vector3 Clamp(in EnclosureArea a, Vector3 p, float margin)
        {
            Vector3 up = a.up.sqrMagnitude > 0.5f ? a.up : Vector3.up;
            Vector3 d = p - a.center; float y = Vector3.Dot(d, up); Vector3 pl = d - up * y;
            float r = Mathf.Max(0f, a.safeRadius - margin);
            if (pl.magnitude > r) pl = pl.normalized * r;
            return a.center + pl + up * y;
        }

        public static float Planar(Vector3 a, Vector3 b, Vector3 up) { Vector3 d = a - b; d -= up * Vector3.Dot(d, up); return d.magnitude; }
    }

    [Serializable]
    [Module("Tunnel", CreatureModule.Behaviour, Summary = "Every so often (per individual) goes underground to a goal across the enclosure: the Burrow part plans the trip through the enclosure's shared tunnels (walk to a hole, ride, dig only the rest), digs in, travels hidden and bursts out. Holds until the trip ends; never while more than maxHiddenShare of its group is under. Gives the species a ModularBrain.",
            Needs = "Walk + a Burrow part", KitOnly = true)]
    public sealed class Tunnel : CreatureModule
    {
        [Param(ParamUnit.Seconds, "mean time on the surface between trips (uniform 0.5..1.5 x, per individual)", Example = "delver 45")] public float surfaceSeconds = 40f;
        [Param(ParamUnit.Fraction, "the shortest trip, fraction of the safe disc's radius", Min = 0.2f, Max = 2, Default = "0.7")] public float minTrip = 0.7f;
        [Param(ParamUnit.Fraction, "the longest trip, fraction of the safe disc's radius", Min = 0.2f, Max = 2, Default = "1.6")] public float maxTrip = 1.6f;
        [Param(ParamUnit.Fraction, "at most this share of the group underground at once (always lets one go)", Min = 0, Max = 1, Default = "0.5")] public float maxHiddenShare = 0.5f;
        [Param(ParamUnit.Fraction, "utility when due (Wander idles at 0.1)", Min = 0.2f, Max = 2, Default = "0.9")] public float utility = 0.9f;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Module<Walk>() == null) return "Tunnel needs Walk";
            bool burrow = false;
            for (int i = 0; i < c.Parts.Count; i++) if (c.Parts[i] is Burrow) burrow = true;
            if (!burrow) return "Tunnel needs a Burrow part";
            if (!(surfaceSeconds > 0f) || minTrip < 0.2f || maxTrip < minTrip || maxHiddenShare < 0f || maxHiddenShare > 1f || utility < 0.2f) return "Tunnel: surfaceSeconds > 0, 0.2 <= minTrip <= maxTrip, maxHiddenShare 0..1, utility >= 0.2";
            return null;
        }

        public override IBehaviour CreateBehaviour(in BehaviourCtx c) => new TunnelBehaviour(this, c.Seed);
    }

    public sealed class TunnelBehaviour : IBehaviour
    {
        readonly Tunnel m;
        readonly int seed;
        BurrowRuntime rt;
        float due = -1f, last;
        int trips;
        bool wasActive;

        public TunnelBehaviour(Tunnel m, int seed) { this.m = m; this.seed = seed; }

        float Interval(int k) => m.surfaceSeconds * (k == 0 ? 0.3f + 0.7f * DiggerUtil.Hash01(seed * 31 + 7) : 0.5f + DiggerUtil.Hash01(seed * 7919 + k * 104729));

        void Clock(in BrainCtx c)
        {
            if (rt == null) rt = DiggerUtil.Runtime(c.Sim);
            CreatureGroup.Join(c.Sim, c.Area);
            float t = c.Time / c.Sim.TimeScale;   // Froude seconds
            if (due < 0f) due = t + Interval(0);
            if (wasActive && rt != null && !rt.TripActive) { trips++; due = t + Interval(trips); }
            wasActive = rt != null && rt.TripActive;
            last = t;
        }

        // the ones on a trip (digging in, under or climbing out) count against the share, not only the hidden ones
        bool GroupAllows(in BrainCtx c)
        {
            int members = CreatureGroup.Count(c.Area, out _, out int away);
            int allowed = Mathf.Max(1, Mathf.FloorToInt(m.maxHiddenShare * members));
            return away < allowed;
        }

        public float Utility(in BrainCtx c)
        {
            Clock(c);
            if (rt == null) return 0f;
            if (rt.TripActive) return 2f;
            return last >= due && GroupAllows(c) ? m.utility : 0f;
        }

        public void Enter(in BrainCtx c) { }

        public BrainOutput Tick(in BrainCtx c, float dt)
        {
            Clock(c);
            if (rt == null) return BrainOutput.Toward(c.Sim.Joints[0]);
            if (!rt.TripActive && last >= due && GroupAllows(c))
            {
                float s = c.Sim.SizeScale, r = c.Area.safeRadius, len = c.Sim.Species.BodyLength * s;
                float dist = Mathf.Lerp(m.minTrip, m.maxTrip, DiggerUtil.Hash01(seed * 131 + trips * 977 + 3)) * r;
                // across the enclosure: the farthest of a few hashed directions (a trip toward the wall is cut short)
                Vector3 goal = c.Sim.Joints[0]; float far = -1f;
                for (int k = 0; k < 6; k++)
                {
                    Vector3 g = DiggerUtil.PointFrom(c.Area, c.Sim.Joints[0], dist, DiggerUtil.Hash01(seed * 17 + trips * 389 + k * 7 + 11), 0.6f * len);
                    float d = (g - c.Sim.Joints[0]).sqrMagnitude;
                    if (d > far) { far = d; goal = g; }
                }
                rt.BeginTrip(goal);
                wasActive = true;
            }
            return BrainOutput.Toward(rt.TripActive ? rt.Target : c.Sim.Joints[0]);
        }
    }

    [Serializable]
    [Module("Forage", CreatureModule.Behaviour, Summary = "Walks a little way, then sniffs and scratches at the ground (the Burrow part's Scratch posture: head down, digging limbs scooping) for a while; comes due every so often per individual.",
            Needs = "Walk + a Burrow part", KitOnly = true)]
    public sealed class Forage : CreatureModule
    {
        [Param(ParamUnit.Seconds, "mean time between forays (uniform 0.5..1.5 x, per individual)")] public float everySeconds = 20f;
        [Param(ParamUnit.Seconds, "time spent scratching at the spot")] public float scratchSeconds = 5f;
        [Param(ParamUnit.Fraction, "how far it walks to the spot, x its body length", Min = 0, Max = 5, Default = "1.2")] public float walk = 1.2f;
        [Param(ParamUnit.Fraction, "utility when due", Min = 0.2f, Max = 2, Default = "0.35")] public float utility = 0.35f;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Module<Walk>() == null) return "Forage needs Walk";
            bool burrow = false;
            for (int i = 0; i < c.Parts.Count; i++) if (c.Parts[i] is Burrow) burrow = true;
            if (!burrow) return "Forage needs a Burrow part (its Scratch posture)";
            if (!(everySeconds > 0f) || !(scratchSeconds > 0f) || walk < 0f || utility < 0.2f) return "Forage: everySeconds, scratchSeconds > 0, walk >= 0, utility >= 0.2";
            return null;
        }

        public override IBehaviour CreateBehaviour(in BehaviourCtx c) => new ForageBehaviour(this, c.Seed);
    }

    // A shared shape for the timed behaviours: due on a per-individual clock, then active for a while.
    public abstract class TimedBehaviour : IBehaviour
    {
        protected readonly int seed;
        protected BurrowRuntime rt;
        protected float due = -1f, now, start;
        protected int runs;
        protected bool active;
        protected TimedBehaviour(int seed) { this.seed = seed; }

        protected abstract float Every { get; }
        protected abstract float BaseUtility { get; }
        protected abstract float Salt { get; }

        protected float Interval(int k) => Every * (k == 0 ? 0.2f + 0.8f * DiggerUtil.Hash01(seed * 53 + (int)Salt) : 0.5f + DiggerUtil.Hash01(seed * 7907 + k * 1009 + (int)Salt));

        protected void Clock(in BrainCtx c)
        {
            if (rt == null) rt = DiggerUtil.Runtime(c.Sim);
            CreatureGroup.Join(c.Sim, c.Area);
            now = c.Time / c.Sim.TimeScale;
            if (due < 0f) due = now + Interval(0);
        }

        protected void Done() { active = false; runs++; due = now + Interval(runs); }

        protected virtual bool CanStart(in BrainCtx c) => rt == null || !rt.TripActive;

        public float Utility(in BrainCtx c)
        {
            Clock(c);
            if (active) return BaseUtility + 0.3f;
            return now >= due && CanStart(c) ? BaseUtility : 0f;
        }

        public void Enter(in BrainCtx c) { Clock(c); if (!active && now >= due) { active = true; start = now; OnStart(c); } }

        protected virtual void OnStart(in BrainCtx c) { }

        public abstract BrainOutput Tick(in BrainCtx c, float dt);
    }

    public sealed class ForageBehaviour : TimedBehaviour
    {
        readonly Forage m;
        Vector3 spot;
        bool there;
        public ForageBehaviour(Forage m, int seed) : base(seed) { this.m = m; }
        protected override float Every => m.everySeconds;
        protected override float BaseUtility => m.utility;
        protected override float Salt => 101f;

        protected override void OnStart(in BrainCtx c)
        {
            float len = c.Sim.Species.BodyLength * c.Sim.SizeScale;
            spot = DiggerUtil.PointFrom(c.Area, c.Sim.Joints[0] + c.Sim.Heading * (0.3f * len), m.walk * len, DiggerUtil.Hash01(seed * 3 + runs * 71 + 5), 0.6f * len);
            there = false;
        }

        public override BrainOutput Tick(in BrainCtx c, float dt)
        {
            Clock(c);
            if (!active) { if (now >= due && CanStart(c)) { active = true; start = now; OnStart(c); } else return BrainOutput.Toward(c.Sim.Joints[0]); }
            float len = c.Sim.Species.BodyLength * c.Sim.SizeScale;
            Vector3 up = c.Area.up.sqrMagnitude > 0.5f ? c.Area.up : Vector3.up;
            if (!there && (DiggerUtil.Planar(c.Sim.Joints[0], spot, up) < 0.25f * len || now - start > 25f)) { there = true; start = now; }
            if (!there) return BrainOutput.Toward(spot);
            if (rt != null) rt.Request(DigActivity.Scratch);
            if (now - start >= m.scratchSeconds) Done();
            return BrainOutput.Toward(c.Sim.Joints[0]);
        }
    }

    [Serializable]
    [Module("Bask", CreatureModule.Behaviour, Summary = "Lies down where it is for a while: curled, the head tucked under its hood, or flat in the sun (the Burrow part's Rest posture); comes due every so often per individual.",
            Needs = "a Burrow part", KitOnly = true)]
    public sealed class Bask : CreatureModule
    {
        [Param(ParamUnit.Seconds, "mean time between rests (uniform 0.5..1.5 x, per individual)")] public float everySeconds = 45f;
        [Param(ParamUnit.Seconds, "a rest's length (x 0.7..1.3)")] public float restSeconds = 12f;
        [Param(ParamUnit.Fraction, "share of the rests taken curled up (the rest flat in the sun)", Min = 0, Max = 1, Default = "0.5")] public float curlShare = 0.5f;
        [Param(ParamUnit.Fraction, "utility when due", Min = 0.2f, Max = 2, Default = "0.4")] public float utility = 0.4f;

        public override string Validate(in SpeciesCtx c)
        {
            bool burrow = false;
            for (int i = 0; i < c.Parts.Count; i++) if (c.Parts[i] is Burrow) burrow = true;
            if (!burrow) return "Bask needs a Burrow part (its Rest posture)";
            if (!(everySeconds > 0f) || !(restSeconds > 0f) || curlShare < 0f || curlShare > 1f || utility < 0.2f) return "Bask: everySeconds, restSeconds > 0, curlShare 0..1, utility >= 0.2";
            return null;
        }

        public override IBehaviour CreateBehaviour(in BehaviourCtx c) => new BaskBehaviour(this, c.Seed);
    }

    public sealed class BaskBehaviour : TimedBehaviour
    {
        readonly Bask m;
        float length, curl;
        public BaskBehaviour(Bask m, int seed) : base(seed) { this.m = m; }
        protected override float Every => m.everySeconds;
        protected override float BaseUtility => m.utility;
        protected override float Salt => 211f;

        protected override void OnStart(in BrainCtx c)
        {
            length = m.restSeconds * (0.7f + 0.6f * DiggerUtil.Hash01(seed * 13 + runs * 37 + 1));
            curl = DiggerUtil.Hash01(seed * 29 + runs * 113 + 2) < m.curlShare ? 1f : 0f;
        }

        public override BrainOutput Tick(in BrainCtx c, float dt)
        {
            Clock(c);
            if (!active) { if (now >= due && CanStart(c)) { active = true; start = now; OnStart(c); } else return BrainOutput.Toward(c.Sim.Joints[0]); }
            if (rt != null) rt.Request(DigActivity.Rest, curl);
            if (now - start >= length) Done();
            return BrainOutput.Toward(c.Sim.Joints[0]);
        }
    }

    [Serializable]
    [Module("Follow", CreatureModule.Behaviour, Summary = "Trails a visible member of its group (same species, same enclosure: CreatureGroup) at a distance for a while, every so often per individual.",
            Needs = "Walk", KitOnly = true)]
    public sealed class Follow : CreatureModule
    {
        [Param(ParamUnit.Seconds, "mean time between follows (uniform 0.5..1.5 x, per individual)")] public float everySeconds = 30f;
        [Param(ParamUnit.Seconds, "a follow's length")] public float followSeconds = 14f;
        [Param(ParamUnit.Fraction, "how far behind the leader's tail it keeps, x its body length", Min = 0, Max = 3, Default = "0.4")] public float distance = 0.4f;
        [Param(ParamUnit.Fraction, "a leader must be within this x its body length", Min = 1, Max = 20, Default = "6")] public float radius = 6f;
        [Param(ParamUnit.Fraction, "utility when due and a leader is in sight", Min = 0.2f, Max = 2, Default = "0.3")] public float utility = 0.3f;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Module<Walk>() == null) return "Follow needs Walk";
            if (!(everySeconds > 0f) || !(followSeconds > 0f) || distance < 0f || radius < 1f || utility < 0.2f) return "Follow: everySeconds, followSeconds > 0, distance >= 0, radius >= 1, utility >= 0.2";
            return null;
        }

        public override IBehaviour CreateBehaviour(in BehaviourCtx c) => new FollowBehaviour(this, c.Seed);
    }

    public sealed class FollowBehaviour : TimedBehaviour
    {
        readonly Follow m;
        CreatureSim leader;
        public FollowBehaviour(Follow m, int seed) : base(seed) { this.m = m; }
        protected override float Every => m.everySeconds;
        protected override float BaseUtility => m.utility;
        protected override float Salt => 307f;
        public CreatureSim Leader => active ? leader : null;

        protected override bool CanStart(in BrainCtx c)
        {
            if (rt != null && rt.TripActive) return false;
            float len = c.Sim.Species.BodyLength * c.Sim.SizeScale;
            return CreatureGroup.NearestVisible(c.Sim, c.Area, m.radius * len) != null;
        }

        protected override void OnStart(in BrainCtx c)
        {
            float len = c.Sim.Species.BodyLength * c.Sim.SizeScale;
            leader = CreatureGroup.NearestVisible(c.Sim, c.Area, m.radius * len);
        }

        public override BrainOutput Tick(in BrainCtx c, float dt)
        {
            Clock(c);
            if (!active) { if (now >= due && CanStart(c)) { active = true; start = now; OnStart(c); } else return BrainOutput.Toward(c.Sim.Joints[0]); }
            if (leader == null || CreatureGroup.IsConcealed(leader) || now - start >= m.followSeconds) { Done(); leader = null; return BrainOutput.Toward(c.Sim.Joints[0]); }
            float len = c.Sim.Species.BodyLength * c.Sim.SizeScale;
            Vector3[] lj = leader.Joints;
            Vector3 tail = lj[lj.Length - 1];
            Vector3 back = (tail - lj[Mathf.Max(0, lj.Length - 2)]);
            back = back.sqrMagnitude > 1e-10f ? back.normalized : -leader.Heading;
            Vector3 t = DiggerUtil.Clamp(c.Area, tail + back * (m.distance * len), 0.6f * len);
            return BrainOutput.Toward(t);
        }
    }
}
