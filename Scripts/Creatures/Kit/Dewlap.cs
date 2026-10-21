// Creature kit (Apex redesign §1 throat flap + flap motion, §5.3): Dewlap - a broad pale throat sail hanging from the
// underside of a FlowBody's neck on a little chain of its own, so it swings a little behind the neck's motion.
//   stations  `stations` places spread evenly over body joints fromJoint..toJoint (jaw end first; a fractional joint
//             is the lerp / slerp of its two neighbours). Each station roots on the throat's midline underside (the
//             drawn section, FlowBody.Section at phi -90) and hangs `links` links down to the sail's lower edge.
//   shape     the lower edge: each station's depth below the throat (depthAt, a monotone cubic between stations) is the
//             curved edge; edgeStraightness blends it toward the straight line from the first station's edge to the
//             last's (1 = a straight, sharpish edge running from under the jaw down and back to the chest).
//   mesh      a thin double-sided sheet (`thickness`) with faint lengthwise creases, a round rim tube (`rim` radius)
//             along the free lower edge and closed ends, in the pale "dewlap" kind. Its root row is tucked RootTuck
//             under the body's facets (no slit between the sail and the throat) and skinned as those facets are (the
//             two rings' SpineBody.SegmentSkin blends, interpolated), so the sail's top never separates from the
//             throat; every row below rides its link's bones, blended between the two neighbouring stations.
//   swing     per station two AngleChains (fore-aft about the station's right axis, sideways about its forward axis),
//             link 0 driven by the station point's acceleration in its own frame (a pendulum's lean, atan(a / g),
//             low-passed over 0.05 s x sqrt s), links 1..links following it as damped springs (swingHz / sqrt s,
//             swingDamping, at most maxSwingDeg of bend per link and of drive), with gravity bending each link
//             gravityDeg x sin(its angle to the down direction) toward hanging. No link ever points above its
//             station's lateral plane (it keeps PlaneMarginDeg below it): a hard stop in the runtime.
// Bones: stations x links, contiguous, station-major (bone k * links + l). The bind is the rest (no swing).
// Author at scale 1 (metres). Allocation-free after construction; main thread.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("Dewlap", PartKind.Chain, Bones = "= stations x links", Summary = "A broad pale throat sail hanging from a FlowBody's neck joints (fromJoint..toJoint, jaw end first) on its own little chain: `stations` stations of `links` links, a nearly straight lower edge (edgeStraightness), a thin double-sided sheet with faint lengthwise creases and a soft round rim along the edge. It swings behind the neck's motion (each station's acceleration drives two damped AngleChains, gravity pulls toward hanging) and never rises above the neck.",
          System = "DewlapRuntime (part runtime, stage AfterLimbs: two AngleChains per station) + Dewlap.EmitMesh", MaterialKinds = new[] { "dewlap" }, KitOnly = true,
          UseWhen = "a dewlap, throat fan, pelican pouch or throat sail hanging under a FlowBody's neck")]
    public sealed class Dewlap : CreaturePart
    {
        [Param(ParamUnit.Count, "the body joint the sail's front (jaw) end hangs from", Min = 0, Max = 127, Example = "apex fixture 1")] public int fromJoint = 1;
        [Param(ParamUnit.Count, "the body joint its back (chest) end hangs from (> fromJoint)", Min = 1, Max = 127, Example = "apex fixture 7")] public int toJoint = 7;
        [Param(ParamUnit.Count, "stations along the sail (each a chain of links)", Min = 2, Max = 16, Default = "5")] public int stations = 5;
        [Param(ParamUnit.Count, "links per station, root to edge", Min = 1, Max = 6, Default = "3")] public int links = 3;
        [Param(ParamUnit.Metres, "the sail's depth below the throat at each station (one per station, jaw end first)", Example = "apex fixture 0.12 0.2 0.24 0.22 0.16")] public float[] depthAt = { 0.12f, 0.2f, 0.24f, 0.22f, 0.16f };
        [Param(ParamUnit.Fraction, "0 = the edge follows depthAt (a curved belly) .. 1 = a straight lower edge from the first station's edge to the last's", Min = 0, Max = 1, Default = "0.8")] public float edgeStraightness = 0.8f;
        [Param(ParamUnit.Metres, "radius of the soft round rim along the lower edge (>= thickness / 2)", Default = "0.012")] public float rim = 0.012f;
        [Param(ParamUnit.Metres, "the sheet's thickness", Default = "0.008")] public float thickness = 0.008f;
        [Param(ParamUnit.Hz, "natural frequency of a link following the one above it (x 1/sqrt size)", Default = "1.6")] public float swingHz = 1.6f;
        [Param(ParamUnit.Fraction, "the links' damping ratio (< 0.7: the edge overshoots a little)", Min = 0.05f, Max = 2, Default = "0.35")] public float swingDamping = 0.35f;
        [Param(ParamUnit.Degrees, "the most the drive leans a station and a link bends over the one above it, either way (0 = a stiff sail)", Min = 0, Max = 60, Default = "25")] public float maxSwingDeg = 25f;
        [Param(ParamUnit.Degrees, "gravity: each link bends this much x sin(its angle to the down direction) toward hanging", Min = -90, Max = 90, Default = "6")] public float gravityDeg = 6f;

        // RED test hooks: the runtime's plane stop off (CreatureDewlapTests.Red_Dewlap_Above); the bind geometry from the
        // plain rest sim instead of the bind's (Red_Dewlap_PlainRestGeometry).
        internal static bool NoPlaneClampForTest, PlainRestForTest;

        public const float PlaneMarginDeg = 20f;   // a link keeps at least this far below its station's lateral plane
        public const int ColumnsPerSpan = 4;       // mesh columns between two stations
        public const int RowsPerLink = 5;          // face rows per link
        public const int RimSegments = 10;         // the rim's half-turns: segments around the lower edge (even: a bottom point)
        public const int Creases = 3;              // lengthwise creases down the sheet
        public const float CreaseAmount = 0.35f;   // their amplitude x thickness
        public const float RowLift = 1e-3f;         // the rows' (and links') root stands this far off the throat's facets
        public const float RootTuck = 1.5e-3f;      // the root row is tucked this far under the throat's facets (metres at scale 1)

        public override int BoneCount(in SpeciesCtx c) => Mathf.Max(1, stations * links);
        public override int MeshOrder => 100;   // a new type: after every shipped vertex
        protected override void CopyArrays() { depthAt = (float[])depthAt?.Clone(); geo = null; geoFor = null; }

        // What the bind geometry is built from (with the species): an edited part rebuilds it.
        int GeoKey()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + fromJoint; h = h * 31 + toJoint; h = h * 31 + stations; h = h * 31 + links;
                h = h * 31 + edgeStraightness.GetHashCode(); h = h * 31 + rim.GetHashCode();
                if (depthAt != null) for (int i = 0; i < depthAt.Length; i++) h = h * 31 + depthAt[i].GetHashCode();
                return h;
            }
        }

        public override string Validate(in SpeciesCtx c)
        {
            if (FindBody(c.Parts) == null) return $"{id}: a Dewlap hangs from a FlowBody's neck";
            if (fromJoint < 0 || toJoint <= fromJoint || toJoint > c.Joints - 1) return $"{id}: joints {fromJoint}..{toJoint} must satisfy 0 <= fromJoint < toJoint <= {c.Joints - 1}";
            if (stations < 2 || stations > 16) return $"{id}: stations must be 2..16";
            if (links < 1 || links > 6) return $"{id}: links must be 1..6";
            if (depthAt == null || depthAt.Length != stations) return $"{id}: depthAt needs one depth per station ({stations})";
            if (!(thickness > 0f) || !(rim >= 0.5f * thickness)) return $"{id}: thickness > 0 and rim >= thickness / 2";
            for (int i = 0; i < depthAt.Length; i++) if (!(depthAt[i] > 2.5f * rim)) return $"{id}: depthAt[{i}] must be > 2.5 x rim ({2.5f * rim})";
            if (!(edgeStraightness >= 0f && edgeStraightness <= 1f)) return $"{id}: edgeStraightness must be 0..1";
            if (!(swingHz > 0f) || !(swingDamping > 0f)) return $"{id}: swingHz and swingDamping must be > 0";
            if (!(maxSwingDeg >= 0f && maxSwingDeg <= 60f)) return $"{id}: maxSwingDeg must be 0..60";
            if (!(gravityDeg >= -90f && gravityDeg <= 90f)) return $"{id}: gravityDeg must be -90..90";
            return null;
        }

        static FlowBody FindBody(IReadOnlyList<CreaturePart> parts)
        {
            if (parts != null) for (int i = 0; i < parts.Count; i++) if (parts[i] is FlowBody f) return f;
            return null;
        }

        // ------------------------------------------------------------ shared geometry (runtime and mesh)

        // The body joint (fractional) station k (or the mesh column at station place sigma, 0 .. stations - 1) hangs from.
        public float StationJoint(float sigma) => Mathf.Lerp(fromJoint, toJoint, sigma / Mathf.Max(1f, stations - 1f));

        // The frame at fractional joint `at`: the two neighbouring joints' positions lerped, rotations slerped (exact at
        // a whole joint).
        internal static void StationFrame(Vector3[] joints, Quaternion[] rot, float at, out Vector3 p, out Quaternion q)
        {
            int n = joints.Length;
            int i = Mathf.Clamp((int)at, 0, n - 2);
            float f = Mathf.Clamp01(at - i);
            if (f == 0f) { p = joints[i]; q = rot[i]; return; }
            p = Vector3.Lerp(joints[i], joints[i + 1], f);
            q = Quaternion.Slerp(rot[i], rot[i + 1], f);
        }

        readonly struct DepthKeys : MonotoneCubic.IKeys
        {
            readonly float[] d;
            public DepthKeys(float[] d) { this.d = d; }
            public int Count => d.Length;
            public float X(int i) => i;
            public float Y(int i) => d[i];
        }

        // The depthAt profile at station place sigma (a monotone cubic: no overshoot between stations).
        float Depth(float sigma) => Mathf.Max(0f, MonotoneCubic.Eval(new DepthKeys(depthAt), sigma));

        // The root in the station's frame (metres at scale 1): the drawn section's midline underside at that joint.
        static Vector3 RootLocal(FlowBody body, float joint)
        {
            body.Section(body.UAtJoint(joint), -0.5f * Mathf.PI, out Vector2 o);
            return new Vector3(o.x, o.y, 0f);
        }

        Vector3 RootAt(FlowBody body, Vector3[] j, Quaternion[] r, float sigma)
        {
            float at = StationJoint(sigma);
            StationFrame(j, r, at, out Vector3 p, out Quaternion q);
            return p + q * RootLocal(body, at);
        }

        Vector3 CurvedEdge(FlowBody body, Vector3[] j, Quaternion[] r, float sigma)
        {
            StationFrame(j, r, StationJoint(sigma), out _, out Quaternion q);
            return RootAt(body, j, r, sigma) + q * Vector3.down * Depth(sigma);
        }

        // The lower edge at station place sigma in the rest pose (world, scale 1): depthAt's curved edge blended toward the
        // straight edge - the root's foot on the line from the first station's curved edge to the last's, so a straight
        // edge's rows hang square to the edge (rows square to a bent neck would cross inside its bend). At least 2.5 x rim
        // below the root.
        internal Vector3 EdgeAt(FlowBody body, Vector3[] j, Quaternion[] r, float sigma)
        {
            Vector3 root = RootAt(body, j, r, sigma), edge = CurvedEdge(body, j, r, sigma);
            if (edgeStraightness > 0f)
            {
                Vector3 e0 = CurvedEdge(body, j, r, 0f), e = CurvedEdge(body, j, r, stations - 1) - e0;
                float ee = Vector3.Dot(e, e);
                Vector3 foot = ee > 1e-10f ? e0 + e * (Vector3.Dot(root - e0, e) / ee) : edge;
                edge = Vector3.Lerp(edge, foot, edgeStraightness);
            }
            Vector3 d = edge - root;
            float min = 2.5f * rim;
            if (d.magnitude < min)
            {
                StationFrame(j, r, StationJoint(sigma), out _, out Quaternion q);
                edge = root + PartMeshEmitter.Unit(d, q * Vector3.down) * min;
            }
            return edge;
        }

        // Each station's rest chain in its own frame (scale 1): root, unit link direction, the rotation taking -Y to it,
        // the link length. Built once per species (and part data) from the bind's rest (CreatureRestPose.BindSim: the
        // same rest the mesh is drawn on - a tall-stance winged species' straightened spine included).
        internal sealed class RestGeo
        {
            public Vector3[] root, dir;
            public Quaternion[] rot;
            public float[] len;
        }

        [NonSerialized] RestGeo geo;
        [NonSerialized] CreatureSpecies geoFor;
        [NonSerialized] int geoKey;
        [ThreadStatic] static bool buildingRest;

        internal RestGeo Geometry(CreatureSpecies sp)
        {
            int key = GeoKey() ^ (PlainRestForTest ? 0x5bd1e995 : 0);
            if (geo != null && geoFor == sp && geoKey == key) return geo;
            FlowBody body = FindBody(KitView.Parts(sp));
            if (body == null) return null;
            PartMeshEmitter m;
            CreatureSim plain = null;
            buildingRest = true;   // the bind rest sim's own Dewlap runtime is not needed (and would recurse)
            try
            {
                m = PartMeshEmitter.ForBind(sp);   // its Rest is CreatureRestPose.BindSim: the rest the mesh is drawn on
                if (PlainRestForTest) plain = CreatureRestPose.Sim(sp);
            }
            finally { buildingRest = false; }
            Vector3[] J = plain != null ? plain.Joints : m.Rest.Joints;
            Quaternion[] R = plain != null ? plain.Rotations : m.Rest.Rotations;
            int n = stations;
            var g = new RestGeo { root = new Vector3[n], dir = new Vector3[n], rot = new Quaternion[n], len = new float[n] };
            for (int k = 0; k < n; k++)
            {
                StationFrame(J, R, StationJoint(k), out Vector3 p, out Quaternion q);
                Quaternion inv = Quaternion.Inverse(q);
                Vector3 root = inv * (RowRoot(body, m, k) - p);
                Vector3 d = inv * (EdgeAt(body, J, R, k) - p) - root;
                g.root[k] = root;
                g.dir[k] = PartMeshEmitter.Unit(d, Vector3.down);
                g.rot[k] = Quaternion.FromToRotation(Vector3.down, g.dir[k]);
                g.len[k] = d.magnitude / links;
            }
            geo = g; geoFor = sp; geoKey = key;
            return g;
        }

        public override IPartRuntime CreateRuntime(in PartCtx c)
        {
            if (c.Sim == null || buildingRest) return null;
            RestGeo g = Geometry(c.Species);
            return g != null ? new DewlapRuntime(this, c.Sim, g, c.Species.Layout.Start(c.Index), c.Size) : null;
        }

        // ------------------------------------------------------------ mesh

        // Columns from the jaw end to the chest end; each column a profile down the right face (+x) from the root, round
        // the rim under the edge and up the left face. The ends are closed with flat caps.
        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            FlowBody body = FindBody(KitView.Parts(m.Species));
            if (body == null) return;
            int kind = m.Slot("dewlap"), start = m.Species.Layout.Start(c.Index);
            // the mesh only depends on this sail's data, its first bone, the body's shape and the rest pose: built once,
            // then replayed (RestMeshCache - its surface queries cost ~0.8 ms)
            ulong key = CacheKey(body, m, start);
            if (RestMeshCache.TryReplay(m, key, kKinds)) return;
            int firstVertex = m.VertexCount, firstTri = m.Lists.tris.Count;
            Vector3[] J = m.Rest.Joints; Quaternion[] R = m.Rest.Rotations;
            int S = stations, L = links, F = RowsPerLink * L, B = RimSegments;
            int cols = (S - 1) * ColumnsPerSpan + 1, prof = 2 * (F + 1) + (B - 1);
            float half = 0.5f * thickness, rr = Mathf.Max(rim, half), amp = CreaseAmount * thickness;
            float a0 = Mathf.Asin(Mathf.Clamp01(half / rr));
            var pos = new Vector3[prof]; var skins = new Vector4[prof];
            int prevCol = -1;
            for (int ci = 0; ci < cols; ci++)
            {
                float sigma = (float)ci / ColumnsPerSpan, at = StationJoint(sigma);
                StationFrame(J, R, at, out _, out Quaternion q);
                Vector3 x = q * Vector3.right;
                float u = body.UAtJoint(at), arc = u * body.BodyLength;
                // the rows hang from the throat's drawn midline (RowRoot: the links' own root, so the bind's links run
                // exactly along them) to the edge
                Vector3 root = RowRoot(body, m, sigma), edge = EdgeAt(body, J, R, sigma);
                Vector3 d = PartMeshEmitter.Unit(edge - root, q * Vector3.down);
                float D = Mathf.Max(Vector3.Distance(edge, root), 1e-4f);
                Vector3 bulb = edge - d * rr;
                float hF = Mathf.Max(0.1f * D, D - rr - rr * Mathf.Cos(a0));
                int s0 = Mathf.Min((int)sigma, S - 2);
                float f = sigma - s0;
                // the root rows sit in the drawn throat at x = +-half (phi off the midline), a hair under its facets
                body.Section(u, 0f, out Vector2 flank);
                float dphi = Mathf.Asin(Mathf.Clamp(half / Mathf.Max(flank.x, 1e-4f), 0f, 0.9f));
                int col = m.VertexCount, i = 0;

                void Emit(Vector3 p, Vector3 n, Vector4 skin)
                {
                    pos[i] = p; skins[i] = skin;
                    m.Vertex(p, n, arc, i / (prof - 1f), kind, -1f, skin);
                    i++;
                }
                void Face(int side, int row)
                {
                    if (row == 0)
                    {
                        Vector3 p0 = OnRings(body, m, arc, -0.5f * Mathf.PI + side * dphi, out Vector3 sn, out Vector4 facetSkin) - sn * RootTuck;
                        Emit(p0, x * side, facetSkin);
                        return;
                    }
                    float h = hF * row / F;
                    Crease(h, hF, D, amp, out float w, out float dw);
                    Emit(root + d * h + x * (side * half + w), PartMeshEmitter.Unit(x * side - d * (side * dw), x * side), Skin(start, s0, f, h / D, L));
                }

                for (int row = 0; row <= F; row++) Face(1, row);   // right face, root to rim
                for (int b = 1; b < B; b++)                         // the rim, round under the edge
                {
                    float al = a0 + (2f * Mathf.PI - 2f * a0) * b / B;
                    Vector3 rn = x * Mathf.Sin(al) - d * Mathf.Cos(al);
                    Vector3 rp = bulb + rr * rn;
                    Emit(rp, rn, Skin(start, s0, f, Vector3.Dot(rp - root, d) / D, L));
                }
                for (int row = F; row >= 0; row--) Face(-1, row);  // left face, rim to root

                if (ci > 0)
                {
                    int pc = prevCol;
                    for (int k = 0; k + 1 < prof; k++)
                    {
                        // column direction (toward the tail) x profile direction: outward is cross(profile, column)
                        m.Tri(pc + k, pc + k + 1, col + k, false);
                        m.Tri(pc + k + 1, col + k + 1, col + k, false);
                    }
                }
                prevCol = col;
                // the caps face along the neck, square to the column's plane (x, d)
                if (ci == 0) Cap(m, pos, skins, PartMeshEmitter.Unit(Vector3.Cross(d, x), q * Vector3.forward), true, kind);
                if (ci == cols - 1) Cap(m, pos, skins, PartMeshEmitter.Unit(Vector3.Cross(x, d), q * Vector3.back), false, kind);
            }
            RestMeshCache.Store(m, key, firstVertex, firstTri, kKinds);
        }

        static readonly string[] kKinds = { "dewlap" };

        ulong CacheKey(FlowBody body, PartMeshEmitter m, int start)
        {
            ulong h = RestMeshCache.Mix(RestMeshCache.Seed, 0xDE71);   // Dewlap
            h = RestMeshCache.Mix(RestMeshCache.Mix(RestMeshCache.Mix(RestMeshCache.Mix(RestMeshCache.Mix(h, fromJoint), toJoint), stations), links), start);
            h = RestMeshCache.Mix(RestMeshCache.Mix(RestMeshCache.Mix(h, edgeStraightness), rim), thickness);
            int n = depthAt?.Length ?? 0;
            h = RestMeshCache.Mix(h, n);
            for (int i = 0; i < n; i++) h = RestMeshCache.Mix(h, depthAt[i]);
            h = RestMeshCache.Mix(RestMeshCache.Mix(h, NoPlaneClampForTest), PlainRestForTest);
            return RestMeshCache.MixRest(body.ShapeKey(h), m);
        }

        // Where the rows (and the links) hang from at station place sigma in the bind rest: the throat's drawn midline
        // (its facets, OnRings) RowLift outside it. Shared by the mesh and the bind geometry.
        Vector3 RowRoot(FlowBody body, PartMeshEmitter m, float sigma)
        {
            float arc = body.UAtJoint(StationJoint(sigma)) * body.BodyLength;
            return OnRings(body, m, arc, -0.5f * Mathf.PI, out Vector3 n) + n * RowLift;
        }

        // The body's drawn skin at arc s, angle phi as its triangles have it: the surface at the four ring vertices around
        // (FlowBody's rings: ringsPerSegment per segment, ringSegments around from phi -90) interpolated bilinearly, so a
        // point placed by it stays on the faceted skin (the true surface can lie inside a chord where the neck bends).
        // normal: the true surface's.
        static Vector3 OnRings(FlowBody body, PartMeshEmitter m, float s, float phi, out Vector3 normal) => OnRings(body, m, s, phi, out normal, out _);

        // As above, and the skin the faceted skin has there: the two rings' blends (SpineBody.SegmentSkin) interpolated
        // linearly, as the triangle between them is - so the point stays on its facet when the neck bends.
        static Vector3 OnRings(FlowBody body, PartMeshEmitter m, float s, float phi, out Vector3 normal, out Vector4 skin)
        {
            float L = body.BodyLength, lh = body.FrontRingU * L, step = body.spacing / Mathf.Max(1, body.ringsPerSegment);
            float k = Mathf.Max(0f, (s - lh) / step);
            float sa = lh + Mathf.Floor(k) * step, sb = sa + step, ts = Mathf.Clamp01((s - sa) / step);
            int rps = Mathf.Max(1, body.ringsPerSegment), ring = (int)Mathf.Floor(k), seg = Mathf.Min(ring / rps, body.jointCount - 2);
            float fa = Mathf.Clamp01((ring - seg * rps) / (float)rps), fb = Mathf.Clamp01((ring + 1 - seg * rps) / (float)rps);
            skin = PartMeshEmitter.Blend(seg, seg + 1, Mathf.Lerp(fa * fa * (3f - 2f * fa), fb * fb * (3f - 2f * fb), ts));
            float dp = 2f * Mathf.PI / body.ringSegments, kp = (phi + 0.5f * Mathf.PI) / dp;
            float pa = -0.5f * Mathf.PI + Mathf.Floor(kp) * dp, pb = pa + dp, tp = Mathf.Clamp01((phi - pa) / dp);
            body.SurfaceAt(m, s / L, phi, out normal, out _);
            Vector3 a0 = body.SurfaceAt(m, sa / L, pa, out _, out _), a1 = body.SurfaceAt(m, sa / L, pb, out _, out _);
            Vector3 b0 = body.SurfaceAt(m, sb / L, pa, out _, out _), b1 = body.SurfaceAt(m, sb / L, pb, out _, out _);
            return Vector3.Lerp(Vector3.Lerp(a0, a1, tp), Vector3.Lerp(b0, b1, tp), ts);
        }

        // A flat end cap over a column's profile (its own vertices with the cap's normal): pairs k / prof-1-k, the rim's
        // bottom point in the middle. front: facing the head.
        static void Cap(PartMeshEmitter m, Vector3[] pos, Vector4[] skins, Vector3 normal, bool front, int kind)
        {
            int prof = pos.Length, c0 = m.VertexCount;
            for (int k = 0; k < prof; k++) m.Vertex(pos[k], normal, 0f, (float)k / (prof - 1), kind, -1f, skins[k]);
            int mid = (prof - 1) / 2;
            for (int k = 0; k < mid; k++)
            {
                int a = c0 + k, a1 = c0 + k + 1, b = c0 + prof - 1 - k, b1 = c0 + prof - 2 - k;
                m.Tri(a, b, a1, !front);
                if (b1 != a1) m.Tri(b, b1, a1, !front);
            }
        }

        // Lengthwise creases: a faint ripple across the sheet (both faces alike), zero at the root and at the rim.
        static void Crease(float h, float hF, float D, float amp, out float w, out float dw)
        {
            float k = 2f * Mathf.PI * Creases / D, t = Mathf.PI / hF;
            float sn = Mathf.Sin(k * h), tp = Mathf.Sin(t * h);
            w = amp * sn * tp;
            dw = amp * (k * Mathf.Cos(k * h) * tp + sn * t * Mathf.Cos(t * h));
        }

        // A sail vertex `v` (0 root .. 1 edge) down column sigma = s0 + f: its link's bones at the two stations, blended.
        static Vector4 Skin(int start, int s0, float f, float v, int L)
        {
            int l = Mathf.Clamp((int)(v * L), 0, L - 1);
            return PartMeshEmitter.Blend(start + s0 * L + l, start + (s0 + 1) * L + l, f);
        }
    }

    // One Dewlap of one individual: per station a fore-aft and a sideways AngleChain (link 0 the drive, 1..links the
    // drawn links), the station point's smoothed velocity and acceleration, and its link points (bounds, debug).
    public sealed class DewlapRuntime : IPartRuntime
    {
        const float G = 9.81f;            // m/s^2 (an acceleration is size-free under Froude scaling)
        const float SmoothSeconds = 0.05f; // the drive's low-pass (x sqrt s)

        readonly Dewlap p;
        readonly CreatureSim sim;
        readonly Dewlap.RestGeo geo;
        readonly int start, S, L;
        readonly float T, scale;
        readonly float[] at;
        readonly AngleChain[] fa, sw;
        readonly float[] eqFa, eqSw, driveFa, driveSw, lean;
        readonly Vector3[] prev, vel, acc, points;
        bool started;

        public int Stations => S;
        public int Links => L;
        public int FirstBone => start;

        internal DewlapRuntime(Dewlap part, CreatureSim sim, Dewlap.RestGeo geo, int firstBone, SizeCtx size)
        {
            p = part; this.sim = sim; this.geo = geo; start = firstBone;
            S = part.stations; L = part.links; T = size.T; scale = size.L;
            at = new float[S];
            fa = new AngleChain[S]; sw = new AngleChain[S];
            eqFa = new float[S]; eqSw = new float[S]; driveFa = new float[S]; driveSw = new float[S]; lean = new float[S];
            prev = new Vector3[S]; vel = new Vector3[S]; acc = new Vector3[S];
            points = new Vector3[S * (L + 1)];
            for (int k = 0; k < S; k++) { at[k] = part.StationJoint(k); fa[k] = new AngleChain(L + 1); sw[k] = new AngleChain(L + 1); }
            Pose(null, 0, true);
        }

        public PartStage Stage => PartStage.AfterLimbs;

        // Station k's swing (degrees): how far its edge link stands from its still hang (the drive at rest, gravity's bend).
        public float Swing(int k)
        {
            float a = fa[k].Angle(L) - eqFa[k], b = sw[k].Angle(L) - eqSw[k];
            return Mathf.Sqrt(a * a + b * b);
        }

        // Station k's lean (degrees): the pendulum angle its acceleration asks for this step, before the flap's own ease and
        // chain - what the swing lags behind (tests).
        public float Lean(int k) => lean[k];

        // A link's direction in its station's frame at fore-aft angle a and sideways angle b (degrees).
        Vector3 Dir(int k, float a, float b) => Swung(a, b) * geo.dir[k];

        static Quaternion Swung(float a, float b) => Quaternion.AngleAxis(a, Vector3.right) * Quaternion.AngleAxis(b, Vector3.forward);

        public void Step(in StepCtx c, float dt)
        {
            if (!(dt > 0f)) return;
            float k = 1f - Mathf.Exp(-dt / (SmoothSeconds * T));
            float omega = 2f * Mathf.PI * p.swingHz / T, zeta = p.swingDamping, max = p.maxSwingDeg;
            float kd = 1f - Mathf.Exp(-dt * omega);
            int sub = Mathf.Clamp(Mathf.CeilToInt(omega * dt / 0.05f), 1, 32);
            float h = dt / sub;
            float sinM = Mathf.Sin(Dewlap.PlaneMarginDeg * Mathf.Deg2Rad);
            Vector3[] J = sim.Joints; Quaternion[] R = sim.Rotations;
            for (int s = 0; s < S; s++)
            {
                Dewlap.StationFrame(J, R, at[s], out Vector3 P, out Quaternion Q);
                if (!started) prev[s] = P;
                Vector3 v = (P - prev[s]) / dt;
                prev[s] = P;
                Vector3 nv = vel[s] + (v - vel[s]) * k;
                Vector3 ar = (nv - vel[s]) / dt;
                vel[s] = nv;
                acc[s] += (ar - acc[s]) * k;
                Quaternion inv = Quaternion.Inverse(Q);
                Vector3 al = inv * acc[s];
                float den = Mathf.Max(0.2f * G, G + al.y);
                // a pendulum leans away from its root's acceleration: forward (+z) acceleration swings the edge back (+a)
                float leanFa = Mathf.Clamp(Mathf.Atan2(al.z, den) * Mathf.Rad2Deg, -max, max);
                float leanSw = Mathf.Clamp(-Mathf.Atan2(al.x, den) * Mathf.Rad2Deg, -max, max);
                // the flap cannot lean faster than it swings: the lean eases in over 1 / (2 pi swingHz) (x sqrt s)
                lean[s] = Mathf.Sqrt(leanFa * leanFa + leanSw * leanSw);
                if (!started) { driveFa[s] = leanFa; driveSw[s] = leanSw; }
                float fromFa = driveFa[s], fromSw = driveSw[s];
                driveFa[s] += (leanFa - driveFa[s]) * kd;
                driveSw[s] += (leanSw - driveSw[s]) * kd;
                // gravity: each link bends toward the down direction by gravityDeg x the sine of its angle to it
                Vector3 down = inv * -sim.Up[Mathf.Clamp(Mathf.RoundToInt(at[s]), 0, J.Length - 1)];
                AngleChain cf = fa[s], cs = sw[s];
                float sf = 0f, ss = 0f;
                for (int i = 1; i <= L; i++)
                {
                    Vector3 g = Vector3.Cross(Dir(s, cf.Angle(i - 1), cs.Angle(i - 1)), down);
                    float of = p.gravityDeg * g.x, os = p.gravityDeg * g.z;
                    cf.SetOffset(i, of); cs.SetOffset(i, os);
                    sf += Mathf.Clamp(of, -max, max); ss += Mathf.Clamp(os, -max, max);
                }
                eqFa[s] = sf; eqSw[s] = ss;
                // fine steps (omega h <= 0.05: the swing's curve is the same at every size and frame rate), the drive
                // eased across them, the plane stop after each
                for (int it = 1; it <= sub; it++)
                {
                    float w = (float)it / sub;
                    cf.Step(fromFa + (driveFa[s] - fromFa) * w, h, omega, zeta, max);
                    cs.Step(fromSw + (driveSw[s] - fromSw) * w, h, omega, zeta, max);
                    if (!Dewlap.NoPlaneClampForTest) PlaneStop(s, cf, cs, sinM);
                }
            }
            started = true;
            Pose(null, 0, false);
        }

        // The plane stop: a link pointing above (or within PlaneMarginDeg of) its station's lateral plane is turned back
        // toward its rest direction until it is PlaneMarginDeg below it.
        void PlaneStop(int s, AngleChain cf, AngleChain cs, float sinM)
        {
            for (int i = 1; i <= L; i++)
            {
                float a = cf.Angle(i), b = cs.Angle(i);
                if (Dir(s, a, b).y <= -sinM) continue;
                float lo = 0f, hi = 1f;
                for (int it = 0; it < 14; it++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (Dir(s, a * mid, b * mid).y <= -sinM) lo = mid; else hi = mid;
                }
                cf.Hold(i, a * lo); cs.Hold(i, b * lo);
            }
        }

        public void WriteBones(Matrix4x4[] dst, int offset) => Pose(dst, offset, false);

        // The bones with no swing: the rest chains carried by the station joints (the bind, in the rest pose).
        public void WriteRest(Matrix4x4[] dst, int offset) => Pose(dst, offset, true);

        // Each station's links down from its root (dst null: only the link points).
        void Pose(Matrix4x4[] dst, int offset, bool rest)
        {
            Vector3[] J = sim.Joints; Quaternion[] R = sim.Rotations;
            Vector3 sv = new Vector3(scale, scale, scale);
            for (int s = 0; s < S; s++)
            {
                Dewlap.StationFrame(J, R, at[s], out Vector3 P, out Quaternion Q);
                Vector3 pt = P + Q * (geo.root[s] * scale);
                int o = s * (L + 1);
                points[o] = pt;
                for (int l = 0; l < L; l++)
                {
                    float a = rest ? 0f : fa[s].Angle(l + 1), b = rest ? 0f : sw[s].Angle(l + 1);
                    Quaternion r = a == 0f && b == 0f ? Q * geo.rot[s] : Q * Swung(a, b) * geo.rot[s];
                    if (dst != null) dst[offset + start + s * L + l] = Matrix4x4.TRS(pt, r, sv);
                    pt += r * Vector3.down * (geo.len[s] * scale);
                    points[o + l + 1] = pt;
                }
            }
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            float r = (p.rim + p.thickness) * scale;
            Vector3 e = new Vector3(r, r, r);
            for (int i = 0; i < points.Length; i++) { lo = Vector3.Min(lo, points[i] - e); hi = Vector3.Max(hi, points[i] + e); }
        }

        public void DrawDebug(ICreatureDebugDraw d)
        {
            var c = new Color(0.85f, 0.8f, 0.7f);
            for (int s = 0; s < S; s++)
                for (int l = 0; l < L; l++) d.Line(points[s * (L + 1) + l], points[s * (L + 1) + l + 1], c);
        }
    }
}
