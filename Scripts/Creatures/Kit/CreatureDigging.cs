// Creature kit (2026-10-20, the Delver): what digging creatures share with the world.
//
//   CreatureDigging   a bounded queue of terrain carve requests (sphere centre + radius). A creature asks for a carve;
//                     the request is refused unless the whole sphere lies inside its enclosure's area (minus a margin), so
//                     the terrain outside an enclosure is never touched. CreatureWorld drains at most a budget of carves a
//                     frame into the terrain's runtime edit path (TerrainModificationSystem.CarveSphere: density edits,
//                     remeshed by the chunk manager; the physical layer, so tunnels are real for colliders and creatures).
//                     Offline (tests) nothing drains it unless a test does: the requests are the record.
//   TunnelNetwork     per enclosure: the holes (entrances / exits on the surface) and the tunnels between them (polylines
//                     of carve centres underground) every digger of that enclosure has dug. A digger plans a trip through
//                     it (walk to a hole, ride existing tunnels, dig only where the network does not reach) and extends it.
//   CreatureGroup     per enclosure: its individuals (for following each other) and which of them are out of sight
//                     underground (IConcealment), so a group never goes under all at once.
// Fixed-size arrays allocated once per enclosure; no allocation per frame. Main thread.
using System;
using UnityEngine;

namespace CreatureKit
{
    // physical: into the terrain's physical layer (colliders, creatures' ground) or rendered only.
    public interface ICarveSink { void Carve(Vector3 centre, float radius, bool physical); }

    // A part runtime that can be out of sight (a burrower underground: Concealed) or committed to going there (Away: on a
    // trip, from planning to climbing out).
    public interface IConcealment { bool Concealed { get; } bool Away { get; } }

    public static class CreatureDigging
    {
        public const int Capacity = 256;
        // the sphere keeps this share of its radius (and at least 0.3 m) inside the area's rim
        public const float RimMargin = 0.25f;

        static readonly Vector3[] centres = new Vector3[Capacity];
        static readonly float[] radii = new float[Capacity];
        static readonly bool[] physical = new bool[Capacity];
        static int head, count;

        public static int Pending => count;
        public static int Requested { get; private set; }
        public static int Refused { get; private set; }
        public static int Dropped { get; private set; }
        public static int Drained { get; private set; }
        // Bumped once per frame by CreatureWorld (staleness of group members); offline it stays 0.
        public static int Frame;

        // Optional observer of every accepted request (tests: the carved volume, the bounds).
        public static Action<Vector3, float> OnRequest;

        public static void Clear()
        {
            head = 0; count = 0; Requested = 0; Refused = 0; Dropped = 0; Drained = 0;
        }

        // Inside the area: the sphere (planar, about the area's up) is within radius - margin of the centre.
        public static bool Inside(in EnclosureArea area, Vector3 c, float r)
        {
            Vector3 d = c - area.center; d -= area.up * Vector3.Dot(d, area.up);
            float margin = Mathf.Max(0.3f, RimMargin * r);
            return d.magnitude + r <= area.radius - margin;
        }

        public static bool Request(in EnclosureArea area, Vector3 c, float r, bool physics = true)
        {
            Requested++;
            if (!(r > 0f) || area.radius <= 0f || !Inside(area, c, r)) { Refused++; return false; }
            if (count == Capacity) { head = (head + 1) % Capacity; count--; Dropped++; }   // the oldest goes
            int i = (head + count) % Capacity;
            centres[i] = c; radii[i] = r; physical[i] = physics; count++;
            OnRequest?.Invoke(c, r);
            return true;
        }

        // Up to `budget` carves into `sink`, oldest first; how many.
        public static int Drain(ICarveSink sink, int budget)
        {
            int n = 0;
            while (count > 0 && n < budget)
            {
                Vector3 c = centres[head]; float r = radii[head]; bool ph = physical[head];
                head = (head + 1) % Capacity; count--;
                if (sink != null) sink.Carve(c, r, ph);
                n++; Drained++;
            }
            return n;
        }
    }

    // The tunnels of one enclosure. Holes are nodes (a surface point); a tunnel joins two holes through a polyline of
    // underground points (its carve centres). Plans are shortest paths over the holes (Dijkstra on <= MaxHoles nodes).
    public sealed class TunnelNetwork
    {
        public const int MaxHoles = 32, MaxTunnels = 48, MaxPoints = 96, MaxNetworks = 16;

        public readonly Vector3 Centre;
        public readonly float Radius;
        public int Holes { get; private set; }
        public int Tunnels { get; private set; }
        readonly Vector3[] hole = new Vector3[MaxHoles];
        readonly int[] tunA = new int[MaxTunnels], tunB = new int[MaxTunnels], tunN = new int[MaxTunnels];
        readonly float[] tunLen = new float[MaxTunnels];
        readonly Vector3[,] pts = new Vector3[MaxTunnels, MaxPoints];
        // Dijkstra scratch
        readonly float[] dist = new float[MaxHoles];
        readonly int[] prevHole = new int[MaxHoles], prevTun = new int[MaxHoles];
        readonly bool[] done = new bool[MaxHoles];

        TunnelNetwork(Vector3 centre, float radius) { Centre = centre; Radius = radius; }

        public Vector3 Hole(int i) => hole[i];
        public int TunnelA(int t) => tunA[t];
        public int TunnelB(int t) => tunB[t];
        public int TunnelPoints(int t) => tunN[t];
        public Vector3 TunnelPoint(int t, int k) => pts[t, k];
        public float TunnelLength(int t) => tunLen[t];

        // ---------------------------------------------------------------- the registry (one network per enclosure)

        static readonly TunnelNetwork[] networks = new TunnelNetwork[MaxNetworks];
        static int networkCount;

        public static TunnelNetwork For(in EnclosureArea area)
        {
            for (int i = 0; i < networkCount; i++)
                if ((networks[i].Centre - area.center).sqrMagnitude < 0.25f) return networks[i];
            var n = new TunnelNetwork(area.center, area.radius);
            if (networkCount < MaxNetworks) networks[networkCount++] = n;
            else { for (int i = 1; i < MaxNetworks; i++) networks[i - 1] = networks[i]; networks[MaxNetworks - 1] = n; }
            return n;
        }

        public static void ClearAll()
        {
            for (int i = 0; i < networkCount; i++) networks[i] = null;
            networkCount = 0;
        }

        // ---------------------------------------------------------------- building

        // The hole within `snap` of p (planar), else a new one (or -1 when full).
        public int AddHole(Vector3 p, float snap, Vector3 up)
        {
            int near = NearestHole(p, up, out float d);
            if (near >= 0 && d <= snap) return near;
            if (Holes == MaxHoles) return -1;
            hole[Holes] = p;
            return Holes++;
        }

        public int NearestHole(Vector3 p, Vector3 up, out float distance)
        {
            int best = -1; distance = float.MaxValue;
            for (int i = 0; i < Holes; i++)
            {
                Vector3 d = hole[i] - p; d -= up * Vector3.Dot(d, up);
                float m = d.magnitude;
                if (m < distance) { distance = m; best = i; }
            }
            return best;
        }

        // A tunnel between holes a and b through `n` points of `p` (copied; at most MaxPoints, evenly thinned). -1 when full.
        public int AddTunnel(int a, int b, Vector3[] p, int n)
        {
            if (Tunnels == MaxTunnels || a < 0 || b < 0 || a == b || n < 1) return -1;
            int t = Tunnels++;
            tunA[t] = a; tunB[t] = b;
            int m = Mathf.Min(n, MaxPoints);
            float len = 0f;
            for (int k = 0; k < m; k++)
            {
                int src = m == n ? k : Mathf.RoundToInt((float)k * (n - 1) / (m - 1));
                pts[t, k] = p[src];
                if (k > 0) len += (pts[t, k] - pts[t, k - 1]).magnitude;
            }
            tunN[t] = m;
            tunLen[t] = len + (hole[a] - pts[t, 0]).magnitude + (hole[b] - pts[t, m - 1]).magnitude;
            return t;
        }

        // ---------------------------------------------------------------- planning

        // Shortest network path from hole `from` to every hole (underground length). Fills dist / prev; no allocation.
        public void Paths(int from)
        {
            for (int i = 0; i < Holes; i++) { dist[i] = float.PositiveInfinity; prevHole[i] = -1; prevTun[i] = -1; done[i] = false; }
            if (from < 0 || from >= Holes) return;
            dist[from] = 0f;
            for (int it = 0; it < Holes; it++)
            {
                int u = -1; float best = float.PositiveInfinity;
                for (int i = 0; i < Holes; i++) if (!done[i] && dist[i] < best) { best = dist[i]; u = i; }
                if (u < 0) break;
                done[u] = true;
                for (int t = 0; t < Tunnels; t++)
                {
                    int v = tunA[t] == u ? tunB[t] : tunB[t] == u ? tunA[t] : -1;
                    if (v < 0 || done[v]) continue;
                    float d = dist[u] + tunLen[t];
                    if (d < dist[v]) { dist[v] = d; prevHole[v] = u; prevTun[v] = t; }
                }
            }
        }

        public float PathLength(int to) => to >= 0 && to < Holes ? dist[to] : float.PositiveInfinity;

        // The underground waypoints from the last Paths() source to `to` (tunnel polylines in travel order) into dst;
        // returns how many (at most dst.Length).
        public int PathPoints(int to, Vector3[] dst)
        {
            // walk back to the source, counting the tunnels
            int hops = 0;
            for (int v = to; v >= 0 && prevTun[v] >= 0; v = prevHole[v]) hops++;
            int n = 0;
            // emit from the source: collect the chain backwards into a small stack of tunnel ids
            for (int h = hops - 1; h >= 0; h--)
            {
                int v = to;
                for (int s = 0; s < h; s++) v = prevHole[v];
                int t = prevTun[v], u = prevHole[v];
                bool forward = tunA[t] == u;
                int m = tunN[t];
                for (int k = 0; k < m && n < dst.Length; k++) dst[n++] = pts[t, forward ? k : m - 1 - k];
                if (n < dst.Length) dst[n++] = hole[v];
            }
            return n;
        }
    }

    // The individuals of one enclosure (any species that joins: the Follow and Tunnel behaviours join theirs).
    public static class CreatureGroup
    {
        public const int Max = 64;
        static readonly CreatureSim[] sims = new CreatureSim[Max];
        static readonly Vector3[] centre = new Vector3[Max];
        static readonly int[] seen = new int[Max];
        static int n;

        public static void Clear() { for (int i = 0; i < n; i++) sims[i] = null; n = 0; }

        public static void Join(CreatureSim s, in EnclosureArea area)
        {
            for (int i = 0; i < n; i++) if (ReferenceEquals(sims[i], s)) { seen[i] = CreatureDigging.Frame; centre[i] = area.center; return; }
            if (n == Max) { for (int i = 1; i < Max; i++) { sims[i - 1] = sims[i]; centre[i - 1] = centre[i]; seen[i - 1] = seen[i]; } n--; }
            sims[n] = s; centre[n] = area.center; seen[n] = CreatureDigging.Frame; n++;
        }

        public static void Forget(CreatureSim s)
        {
            for (int i = 0; i < n; i++)
                if (ReferenceEquals(sims[i], s))
                {
                    for (int k = i + 1; k < n; k++) { sims[k - 1] = sims[k]; centre[k - 1] = centre[k]; seen[k - 1] = seen[k]; }
                    sims[--n] = null;
                    return;
                }
        }

        static bool Fresh(int i) => CreatureDigging.Frame - seen[i] <= 600;   // a despawned sim not forgotten drops out after ~10 s

        public static bool IsConcealed(CreatureSim s)
        {
            IPartRuntime[] r = s.PartRuntimes;
            for (int i = 0; i < r.Length; i++) if (r[i] is IConcealment c && c.Concealed) return true;
            return false;
        }

        public static bool IsAway(CreatureSim s)
        {
            IPartRuntime[] r = s.PartRuntimes;
            for (int i = 0; i < r.Length; i++) if (r[i] is IConcealment c && c.Away) return true;
            return false;
        }

        // Members of the area (fresh ones), how many are out of sight and how many are on a trip.
        public static int Count(in EnclosureArea area, out int concealed, out int away)
        {
            int c = 0; concealed = 0; away = 0;
            for (int i = 0; i < n; i++)
            {
                if (!Fresh(i) || (centre[i] - area.center).sqrMagnitude > 0.25f) continue;
                c++;
                if (IsConcealed(sims[i])) concealed++;
                if (IsAway(sims[i])) away++;
            }
            return c;
        }

        // The nearest other visible member of the same species in the area (null = none within maxDistance).
        public static CreatureSim NearestVisible(CreatureSim me, in EnclosureArea area, float maxDistance)
        {
            CreatureSim best = null; float bd = maxDistance;
            for (int i = 0; i < n; i++)
            {
                CreatureSim s = sims[i];
                if (ReferenceEquals(s, me) || !Fresh(i) || (centre[i] - area.center).sqrMagnitude > 0.25f) continue;
                if (!ReferenceEquals(s.Species, me.Species) || IsConcealed(s)) continue;
                float d = (s.Joints[0] - me.Joints[0]).magnitude;
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }
    }
}
