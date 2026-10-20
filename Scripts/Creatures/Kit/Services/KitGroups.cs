// Creature kit service (2026-10-20, kit services): groups and flocks.
//
//   KitGroup     the individuals of one species in one enclosure (the lab spawns the roster's `count` there), shared by
//                every behaviour that needs its neighbours: members (joined once, by reference), liveness (a member
//                counts while its sim's clock runs with the asker's, so a respawned group or a test's fresh sims never
//                steer round ghosts), neighbour queries, the visible ones (IConcealment), and a group-level state slot
//                per behaviour (Shared<T>: a school's waypoint) that starts over with the group.
//   Separation   the steering helpers a group needs: the closest point of approach of two movers within a horizon, and
//                the separation weight of a neighbour inside a keep distance.
//   LabGroup     how the lab places, seeds and sizes a group (CreatureLab and CreatureWorld call it; the kit's group
//                check spawns the same individuals offline).
// Joining and the first Shared<T> allocate (once); queries and steering do not. Main thread.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public sealed class KitGroup
    {
        public readonly List<CreatureSim> Members = new List<CreatureSim>(16);
        readonly Dictionary<Type, object> shared = new Dictionary<Type, object>();

        static readonly Dictionary<string, KitGroup> groups = new Dictionary<string, KitGroup>();

        // The group of species sp in the enclosure at area.center (keyed by the species' name and the centre, 0.1 m).
        public static KitGroup For(CreatureSpecies sp, in EnclosureArea area)
        {
            string key = (sp != null ? sp.name : "") + "|" + Mathf.RoundToInt(area.center.x * 10f) + "|" + Mathf.RoundToInt(area.center.y * 10f) + "|" + Mathf.RoundToInt(area.center.z * 10f);
            if (!groups.TryGetValue(key, out KitGroup g)) { g = new KitGroup(); groups[key] = g; }
            return g;
        }

        // Every group forgotten (a new world, a test).
        public static void ClearAll() => groups.Clear();

        // A sim joins. A group nobody moves in any more (no member's clock near the newcomer's: a respawn, a new test)
        // starts over - members and shared state - so a fresh group is the same group every time. True: it started over.
        public bool Join(CreatureSim sim)
        {
            if (Members.Contains(sim)) return false;
            bool alive = false;
            for (int i = 0; i < Members.Count; i++) if (Mathf.Abs(Members[i].Time - sim.Time) < 0.5f) alive = true;
            if (!alive) { Members.Clear(); shared.Clear(); }
            Members.Add(sim);
            return !alive;
        }

        // Is member i moving with `me` now (its clock running with mine, not me)? No allocation.
        public bool Live(int i, CreatureSim me)
        {
            CreatureSim m = Members[i];
            if (m == me) return false;
            float d = m.Time - me.Time;
            return d > -0.5f && d < 0.5f;
        }

        // Drops members whose clock stopped long ago (not ticked any more).
        public void Prune(CreatureSim me)
        {
            for (int i = Members.Count - 1; i >= 0; i--)
                if (Members[i] != me && me.Time - Members[i].Time > 30f) Members.RemoveAt(i);
        }

        public int LiveCount(CreatureSim me)
        {
            int n = 0;
            for (int i = 0; i < Members.Count; i++) if (Live(i, me)) n++;
            return n;
        }

        // The nearest live member (head to head) within maxDistance, or null; visibleOnly: not concealed (underground).
        public CreatureSim Nearest(CreatureSim me, float maxDistance, bool visibleOnly = false)
        {
            CreatureSim best = null; float bd = maxDistance;
            for (int i = 0; i < Members.Count; i++)
            {
                if (!Live(i, me)) continue;
                CreatureSim s = Members[i];
                if (visibleOnly && Concealed(s)) continue;
                float d = (s.Joints[0] - me.Joints[0]).magnitude;
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        // The live members whose head is within radius of mine, into `into` (up to its length); the count written.
        public int Neighbours(CreatureSim me, float radius, CreatureSim[] into)
        {
            int n = 0; float r2 = radius * radius;
            for (int i = 0; i < Members.Count && n < into.Length; i++)
                if (Live(i, me) && (Members[i].Joints[0] - me.Joints[0]).sqrMagnitude <= r2) into[n++] = Members[i];
            return n;
        }

        // Members out of sight now / committed to it (IConcealment part runtimes: a burrower).
        public int CountConcealed(CreatureSim me, out int away)
        {
            int c = 0; away = 0;
            for (int i = 0; i < Members.Count; i++)
            {
                if (Members[i] != me && !Live(i, me)) continue;
                if (Concealed(Members[i])) c++;
                if (Away(Members[i])) away++;
            }
            return c;
        }

        // One state object per behaviour type for the whole group (created on first use, again after the group starts over).
        public T Shared<T>() where T : class, new()
        {
            if (!shared.TryGetValue(typeof(T), out object o)) { o = new T(); shared[typeof(T)] = o; }
            return (T)o;
        }

        public static bool Concealed(CreatureSim s)
        {
            IPartRuntime[] r = s.PartRuntimes;
            for (int i = 0; i < r.Length; i++) if (r[i] is IConcealment c && c.Concealed) return true;
            return false;
        }

        public static bool Away(CreatureSim s)
        {
            IPartRuntime[] r = s.PartRuntimes;
            for (int i = 0; i < r.Length; i++) if (r[i] is IConcealment c && c.Away) return true;
            return false;
        }
    }

    public static class Separation
    {
        // Two movers at p (velocity v) and q (velocity u): the offset p - q at their closest approach within the
        // horizon (from now, if they are separating), and when (tc, seconds).
        public static Vector3 ClosestApproach(Vector3 p, Vector3 v, Vector3 q, Vector3 u, float horizon, out float tc)
        {
            Vector3 d = p - q, vr = v - u;
            float vv = vr.sqrMagnitude;
            tc = vv > 1e-8f ? Mathf.Clamp(-Vector3.Dot(d, vr) / vv, 0f, horizon) : 0f;
            return d + vr * tc;
        }

        // A neighbour's separation weight: 0 at the keep distance or beyond, rising linearly to 1 at contact.
        public static float Weight(float distance, float keep) => keep > 0f && distance < keep ? (keep - distance) / keep : 0f;
    }

    public static class LabGroup
    {
        // Individual i of n in an enclosure: a lone animal at the centre facing `face`; a group on a ring at 0.4 of the
        // safe radius, each turned a little differently.
        public static void Start(Vector3 center, Vector3 up, Vector3 face, float safeRadius, int i, int n, out Vector3 at, out Vector3 dir)
        {
            if (n <= 1) { at = center; dir = face; return; }
            Quaternion q = Quaternion.AngleAxis(360f * i / n + 17f, up);
            Vector3 radial = q * face;
            at = center + radial * (0.4f * safeRadius);
            dir = Quaternion.AngleAxis(70f + 40f * ((i * 0.618034f) % 1f), up) * radial;
        }

        // Individual i's seed in enclosure k: a lone animal keeps seed + k; a group's are consecutive, seed + k + 1000 + i
        // (part runtimes stagger on consecutive seeds: TuckRuntime's first random scare).
        public static int Seed(int labSeed, int k, int i, int count) => count > 1 ? labSeed + k + 1000 + i : labSeed + k;

        // Per-individual look and size from the seed, within the species' jitter ranges.
        public static CreatureInstance Individual(CreatureSpecies sp, int seed, float scale)
        {
            CreatureLook look = sp.look;
            uint h = (uint)seed * 2654435761u + 0x7F4A7C15u;
            float a = Rand(ref h), b = Rand(ref h), c = Rand(ref h), d = Rand(ref h);
            return new CreatureInstance
            {
                seed = 1024f * a,
                hueShift = look.hueJitter * (2f * b - 1f),
                valueShift = look.valueJitter * (2f * c - 1f),
                sizeScale = scale * (1f + look.sizeJitter * (2f * d - 1f)),
            };
        }

        static float Rand(ref uint s)
        {
            s ^= s >> 16; s *= 0x7FEB352Du; s ^= s >> 15; s *= 0x846CA68Bu; s ^= s >> 16;
            return (s & 0xFFFFFFu) / 16777216f;
        }
    }
}
