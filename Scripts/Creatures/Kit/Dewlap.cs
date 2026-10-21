// Creature kit (Apex redesign §1 throat flap + flap motion, §5.3): Dewlap - a broad pale throat sail hanging from the
// underside of a FlowBody's neck on a little chain of its own, so it swings a little behind the neck's motion.
//   stations  `stations` places spread evenly over body joints fromJoint..toJoint (jaw end first; a fractional joint
//             is the lerp / slerp of its two neighbours). Each station roots on the throat's midline underside (the
//             drawn section, FlowBody.Section at phi -90) and hangs `links` links down to the sail's lower edge.
//   shape     the lower edge: each station's depth below the throat (depthAt, a monotone cubic between stations) is the
//             curved edge; edgeStraightness blends it toward the straight line from the first station's edge to the
//             last's (1 = a straight, sharpish edge running from under the jaw down and back to the chest).
//   mesh      a soft pouch: a double-sided sheet `rootThickness` thick where it leaves the throat, thinning to
//             `thickness` at the rim, its faces bellied out (billow x the row's depth halfway down), faint lengthwise
//             creases, a round rim tube (`rim` radius) along the free lower edge, in the pale "dewlap" kind. Over the
//             last rearSweep of its length the sheet is cut short - depth, rim and thickness down to RearEnd at the
//             chest end - so the edge sweeps up and back into the chest skin (a small closing cap, no square rear end);
//             its links stay the full sail's (so the cut-short rear moves as that part of a full sail would), but the
//             chest end's chain spans its own drawn row (LinkScale, linear over the last span: every link carries
//             skin), and a cut-short row keeps the full sail's spacing across each link joint (RowDepths). The root
//             row is tucked RootTuck under the body's facets (no slit between the sail and the throat) and skinned as
//             those facets are (the two rings' SpineBody.SegmentSkin blends, interpolated), so the sail's top never
//             separates from the throat; every row below rides its link's bones, blended between the two neighbouring
//             stations. Normals are square to the profile and to the column's run along the neck.
//   swing     per station two AngleChains (fore-aft about the station's right axis, sideways about its forward axis),
//             link 0 driven by the station point's acceleration in its own frame (a pendulum's lean, atan(a / g),
//             low-passed over 0.05 s x sqrt s), links 1..links following it as damped springs (swingHz / sqrt s,
//             swingDamping, at most maxSwingDeg of bend per link and of drive - the whole angle, fore-aft and sideways
//             together), with gravity bending each link
//             gravityDeg x sin(its angle to the down direction) toward hanging. No link ever points above its
//             station's lateral plane (it keeps PlaneMarginDeg below it): a hard stop in the runtime.
//   coupling  the stations swing as one sheet: each lean is smoothed with its neighbours', every link's swing is drawn
//             toward the same link's at the neighbouring stations, and two neighbours' swings (and bends) stay within
//             maxTwistDeg (the still hangs too) - so a station on a joint that heaves alone (the chest under a heavy
//             flight bob) cannot turn the rim over. The swept rear is anchored into the chest: a station swings and
//             bends at most its RearScale share. A new sail starts hanging still.
//             The pouch puffs out (its bones widen across the sheet) by up to PuffPerBillow x billow with the swing.
// Bones: stations x links, contiguous, station-major (bone k * links + l). The bind is the rest (no swing).
// Author at scale 1 (metres). Allocation-free after construction; main thread.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("Dewlap", PartKind.Chain, Bones = "= stations x links", Summary = "A broad pale throat sail or pouch hanging from a FlowBody's neck joints (fromJoint..toJoint, jaw end first, the last may be the chest) on its own little chain: `stations` stations of `links` links, a lower edge from curved to straight (edgeStraightness), a soft sheet thick at the root and bellied (rootThickness, billow) with faint lengthwise creases and a round rim, its rear sweeping up into the chest (rearSweep). It swings behind the neck's motion (each station's acceleration drives two damped AngleChains, gravity pulls toward hanging), the stations coupled so even a heavy flight bob never turns the rim over (coupling, maxTwistDeg); it never rises above the neck.",
          System = "DewlapRuntime (part runtime, stage AfterLimbs: two AngleChains per station) + Dewlap.EmitMesh", MaterialKinds = new[] { "dewlap" }, KitOnly = true,
          UseWhen = "a dewlap, throat fan, pelican pouch or throat sail hanging under a FlowBody's neck")]
    public sealed class Dewlap : CreaturePart
    {
        [Param(ParamUnit.Count, "the body joint the sail's front (jaw) end hangs from", Min = 0, Max = 127, Example = "apex fixture 1")] public int fromJoint = 1;
        [Param(ParamUnit.Count, "the body joint its back (chest) end hangs from (> fromJoint)", Min = 1, Max = 127, Example = "apex fixture 7")] public int toJoint = 7;
        [Param(ParamUnit.Count, "stations along the sail (each a chain of links)", Min = 2, Max = 16, Default = "5")] public int stations = 5;
        [Param(ParamUnit.Count, "links per station, root to edge (under a heavy flight bob - the Apex's, ~13 % of the body - 3 or fewer: 4 folded the swept rear)", Min = 1, Max = 6, Default = "3")] public int links = 3;
        [Param(ParamUnit.Metres, "the sail's depth below the throat at each station (one per station, jaw end first)", Example = "apex fixture 0.12 0.2 0.24 0.22 0.16")] public float[] depthAt = { 0.12f, 0.2f, 0.24f, 0.22f, 0.16f };
        [Param(ParamUnit.Fraction, "0 = the edge follows depthAt (a curved belly) .. 1 = a straight lower edge from the first station's edge to the last's. A straight edge under a strongly curved neck (or a shallow station between deep ones) tilts a station's rest hang toward its lateral plane: within PlaneMarginDeg (20 deg) of it the plane stop holds that station still - the bind warns (Dewlap.RestPlaneProblem)", Min = 0, Max = 1, Default = "0.8")] public float edgeStraightness = 0.8f;
        [Param(ParamUnit.Metres, "radius of the soft round rim along the lower edge (>= thickness / 2)", Default = "0.012")] public float rim = 0.012f;
        [Param(ParamUnit.Metres, "the sheet's thickness", Default = "0.008")] public float thickness = 0.008f;
        [Param(ParamUnit.Hz, "natural frequency of a link following the one above it (x 1/sqrt size)", Default = "1.6")] public float swingHz = 1.6f;
        [Param(ParamUnit.Fraction, "the links' damping ratio (< 0.7: the edge overshoots a little)", Min = 0.05f, Max = 2, Default = "0.35")] public float swingDamping = 0.35f;
        [Param(ParamUnit.Degrees, "the most the drive leans a station and a link bends over the one above it, either way (0 = a stiff sail)", Min = 0, Max = 60, Default = "25")] public float maxSwingDeg = 25f;
        [Param(ParamUnit.Degrees, "gravity: each link bends this much x sin(its angle to the down direction) toward hanging", Min = -90, Max = 90, Default = "6")] public float gravityDeg = 6f;
        [Param(ParamUnit.Metres, "the sheet's thickness where it leaves the throat (>= thickness): it thins toward the rim. Keep it under ~0.02: a wider root sits inside the body's facets, which its two-bone skin follows only to first order, and the root row leaves the bending throat by more than 2.5 mm", Default = "0.012")] public float rootThickness = 0.012f;
        [Param(ParamUnit.Fraction, "the pouch's belly: each face bulges outward by this x the local depth halfway down (0 = a flat card), and puffs up to 3 x billow wider while it swings", Min = 0, Max = 0.3f, Default = "0.05")] public float billow = 0.05f;
        [Param(ParamUnit.Fraction, "the share of the sail's length (from the chest end) over which the edge sweeps up and the sheet thins into the chest skin (0 = a square rear end; under a heavy flight bob keep it >= ~0.5: 0.35 packs the shrinking rows into one span and folded them)", Min = 0, Max = 1, Default = "0.6")] public float rearSweep = 0.6f;
        [Param(ParamUnit.Fraction, "how strongly each station's swing follows its neighbours' (0 = independent stations: under a heavy bob the rim turns over)", Min = 0, Max = 1, Default = "0.6")] public float coupling = 0.6f;
        [Param(ParamUnit.Degrees, "the most a link's swing, and its bend over the link above, may differ from the same link's at the neighbouring station, fore-aft and sideways (the rows' relative twist)", Min = 1, Max = 60, Default = "8")] public float maxTwistDeg = 8f;

        // RED test hooks: the runtime's plane stop off (CreatureDewlapTests.Red_Dewlap_Above); the bind geometry from the
        // plain rest sim instead of the bind's (Red_Dewlap_PlainRestGeometry); the stations' coupling and twist limit off
        // (Red_Dewlap_HeavyBob_Uncoupled).
        internal static bool NoPlaneClampForTest, PlainRestForTest, NoCouplingForTest;

        public const float PlaneMarginDeg = 20f;   // a link keeps at least this far below its station's lateral plane
        public const int ColumnsPerSpan = 4;       // mesh columns between two stations
        public const int RowsPerLink = 5;          // face rows per link
        public const int RimSegments = 10;         // the rim's half-turns: segments around the lower edge (even: a bottom point)
        public const int Creases = 3;              // lengthwise creases down the sheet
        public const float CreaseAmount = 0.35f;   // their amplitude x thickness
        public const float RowLift = 1e-3f;         // the rows' (and links') root stands this far off the throat's facets
        public const float RootTuck = 1.5e-3f;      // the root row is tucked this far under the throat's facets (metres at scale 1)
        public const float RearEnd = 0.25f;         // the last column's size (depth, rim, thickness) x its untapered size
        public const float PuffPerBillow = 3f;      // the lateral puff at full swing, x billow
        public const float FirstRowMax = 0.25f;     // a swept row's first face row: the full row's gap, at most this x the row (RowDepths)

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
                h = h * 31 + edgeStraightness.GetHashCode(); h = h * 31 + rim.GetHashCode(); h = h * 31 + rearSweep.GetHashCode();
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
            if (!(rootThickness >= thickness)) return $"{id}: rootThickness must be >= thickness";
            if (!(billow >= 0f && billow <= 0.3f)) return $"{id}: billow must be 0..0.3";
            if (!(rearSweep >= 0f && rearSweep <= 1f)) return $"{id}: rearSweep must be 0..1";
            if (!(coupling >= 0f && coupling <= 1f)) return $"{id}: coupling must be 0..1";
            if (!(maxTwistDeg >= 1f && maxTwistDeg <= 60f)) return $"{id}: maxTwistDeg must be 1..60";
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

        // The rear sweep at station place sigma: 1 ahead of the last rearSweep of the sail, then down to RearEnd at the
        // chest end along x (1 + x - x^2) of the way (x: 1 at the sweep's start .. 0 at the chest end; it leaves the full
        // depth smoothly and is still falling at the end: the edge sweeps up and back into the chest skin - no square
        // cap): the drawn sheet's depth, rim and thickness x this. Only the drawing is cut short: the links
        // (and so the skin of every drawn point) are the full sail's, so the swept rear moves exactly as the same part
        // of an unswept sail would.
        internal float RearScale(float sigma)
        {
            float span = rearSweep * Mathf.Max(1f, stations - 1f);
            if (!(span > 0f)) return 1f;
            float x = Mathf.Clamp01((stations - 1f - sigma) / span);
            return RearEnd + (1f - RearEnd) * x * (1f + x - x * x);
        }

        // A station's chain of links spans its full row - except the chest end's, cut short with its drawn row (so its
        // links all carry skin and its last ends at its rim); between the last two stations, linear (a row between two
        // stations rides both chains: its depth on them must be their blend).
        internal float LinkScale(float sigma)
        {
            float end = RearScale(stations - 1);
            if (!(end < 1f) || sigma <= stations - 2f) return 1f;
            return Mathf.Lerp(1f, end, Mathf.Clamp01(sigma - (stations - 2f)));
        }

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
                g.len[k] = d.magnitude * LinkScale(k) / links;
            }
            geo = g; geoFor = sp; geoKey = key;
            string flat = RestPlaneProblem(g);
            if (flat != null) Debug.LogWarning($"{id}: {flat}");
            return g;
        }

        // A station whose rest hang is within PlaneMarginDeg of its lateral plane (or above it): the runtime's plane
        // stop turns any link that close back down, so that station would be held still - null if none. Its rest
        // direction comes from the bind (the neck's rest curve, edgeStraightness), so it is checked when the rest
        // geometry is built, not in Validate.
        internal static string RestPlaneProblem(RestGeo g)
        {
            for (int k = 0; k < g.dir.Length; k++)
            {
                float below = Mathf.Asin(Mathf.Clamp(-g.dir[k].y, -1f, 1f)) * Mathf.Rad2Deg;
                if (!(below > PlaneMarginDeg)) return $"station {k}'s rest hang is only {below:F1} deg below its lateral plane (<= PlaneMarginDeg {PlaneMarginDeg}): the plane stop holds it still - a less straight edge (edgeStraightness) or deeper stations";
            }
            return null;
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
            int S = stations, F = RowsPerLink * links, B = RimSegments;
            int cols = (S - 1) * ColumnsPerSpan + 1, prof = 2 * (F + 1) + (B - 1);
            var pos = new Vector3[cols][]; var nrm = new Vector3[cols][]; var skins = new Vector4[cols][];
            var arcs = new float[cols]; var xs = new Vector3[cols]; var ds = new Vector3[cols]; var qs = new Quaternion[cols];
            for (int ci = 0; ci < cols; ci++)
            {
                pos[ci] = new Vector3[prof]; nrm[ci] = new Vector3[prof]; skins[ci] = new Vector4[prof];
                Column(body, m, start, (float)ci / ColumnsPerSpan, pos[ci], nrm[ci], skins[ci], out arcs[ci], out xs[ci], out ds[ci], out qs[ci]);
            }
            int prevCol = -1;
            for (int ci = 0; ci < cols; ci++)
            {
                Vector3 x = xs[ci], d = ds[ci];
                Vector3[] P = pos[ci], A = pos[Mathf.Max(0, ci - 1)], Bn = pos[Mathf.Min(cols - 1, ci + 1)];
                // each vertex normal also square to the sheet's run along the neck (the edge sweeping up into the chest,
                // the thinning rear): the analytic normal (square to the profile) with its part along the column's
                // tangent - the chord between the neighbouring columns, as the triangles have it - taken out
                Vector3 along = Vector3.Cross(x, d);   // the column plane's normal (toward the tail)
                int col = m.VertexCount;
                for (int k = 0; k < prof; k++)
                {
                    Vector3 n = nrm[ci][k], tp = PartMeshEmitter.Unit(Vector3.Cross(along, n), d);
                    Vector3 tc = Bn[k] - A[k];
                    tc -= tp * Vector3.Dot(tc, tp);
                    if (tc.sqrMagnitude > 1e-14f)
                    {
                        tc.Normalize();
                        n = PartMeshEmitter.Unit(n - tc * Vector3.Dot(n, tc), n);
                    }
                    m.Vertex(P[k], n, arcs[ci], k / (prof - 1f), kind, -1f, skins[ci][k]);
                }

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
                // the caps face along the neck, square to the column's plane (x, d); the rear one is RearEnd small
                if (ci == 0) Cap(m, P, skins[ci], PartMeshEmitter.Unit(Vector3.Cross(d, x), qs[ci] * Vector3.forward), true, kind);
                if (ci == cols - 1) Cap(m, P, skins[ci], PartMeshEmitter.Unit(Vector3.Cross(x, d), qs[ci] * Vector3.back), false, kind);
            }
            RestMeshCache.Store(m, key, firstVertex, firstTri, kKinds);
        }

        // One column of the sail at station place sigma in the bind rest: its profile down the right face (+x) from the
        // root, round the rim under the edge and up the left face - positions, and (nrm not null) the normals square to
        // the profile and the skins. arc: the body arc it hangs at; x, d: its lateral and down (root to edge) axes.
        // depths (tests, F + 2 long): the face rows' depths below the root (0 .. F), then the depth its links' chain spans.
        void Column(FlowBody body, PartMeshEmitter m, int start, float sigma, Vector3[] pos, Vector3[] nrm, Vector4[] skins,
                    out float arc, out Vector3 x, out Vector3 d, out Quaternion q, float[] depths = null)
        {
            Vector3[] J = m.Rest.Joints; Quaternion[] R = m.Rest.Rotations;
            int S = stations, L = links, F = RowsPerLink * L, B = RimSegments;
            float at = StationJoint(sigma);
            StationFrame(J, R, at, out _, out q);
            x = q * Vector3.right;
            float u = body.UAtJoint(at);
            arc = u * body.BodyLength;
            // the rows hang from the throat's drawn midline (RowRoot: the links' own root, so the bind's links run
            // exactly along them) to the edge
            Vector3 root = RowRoot(body, m, sigma), edge = EdgeAt(body, J, R, sigma);
            d = PartMeshEmitter.Unit(edge - root, q * Vector3.down);
            // Dl: the full row's depth (its links'); D: the drawn depth, cut short by the rear sweep
            float Dl = Mathf.Max(Vector3.Distance(edge, root), 1e-4f), cs = RearScale(sigma), D = Dl * cs;
            // the cross-section: thinner toward the chest end, thick at the root, a belly halfway down
            float half = 0.5f * thickness * cs, rootHalf = 0.5f * rootThickness * cs, belly = billow * D;
            float rr = Mathf.Max(rim * cs, half), amp = CreaseAmount * thickness * cs;
            float a0 = Mathf.Asin(Mathf.Clamp01(half / rr));
            Vector3 bulb = root + d * (D - rr);
            float hF = Mathf.Max(0.1f * D, D - rr - rr * Mathf.Cos(a0));
            // the depth the rows ride their links by: the links' chain's (LinkScale: a cut-short row's points ride the
            // bones the full row's would; the chest end's own chain spans its drawn row)
            float Dv = Dl * LinkScale(sigma);
            // the face rows' depths: the full sail's even rows, or (swept) RowDepths
            var hs = new float[F + 1];
            if (cs < 1f) RowDepths(hF, cs, Dv, L, hs);
            else for (int r = 1; r <= F; r++) hs[r] = hF * r / F;
            if (depths != null) { for (int r = 0; r <= F; r++) depths[r] = hs[r]; depths[F + 1] = Dv; }
            int s0 = Mathf.Min((int)sigma, S - 2);
            float f = sigma - s0;
            // the root rows sit in the drawn throat at x = +-rootHalf (phi off the midline), a hair under its facets - on
            // the body's own ring-vertex lines once that is at least half a ring segment out (a point inside a facet is
            // skinned as its corners are only to first order: the bending neck pulls it off the facet)
            body.Section(u, 0f, out Vector2 flank);
            float dphi = Mathf.Asin(Mathf.Clamp(rootHalf / Mathf.Max(flank.x, 1e-4f), 0f, 0.9f)), seg = 2f * Mathf.PI / body.ringSegments;
            if (dphi >= 0.5f * seg) dphi = Mathf.Round(dphi / seg) * seg;
            int i = 0;
            Vector3 xx = x, dd = d;
            float arcc = arc;

            void Put(Vector3 p, Vector3 n, Vector4 skin)
            {
                pos[i] = p;
                if (nrm != null) { nrm[i] = n; skins[i] = skin; }
                i++;
            }
            void Face(int side, int row)
            {
                if (row == 0)
                {
                    Vector3 p0 = OnRings(body, m, arcc, -0.5f * Mathf.PI + side * dphi, out Vector3 sn, out Vector4 facetSkin) - sn * RootTuck;
                    Put(p0, xx * side, facetSkin);
                    return;
                }
                float h = hs[row];
                Crease(h, hF, D, amp, out float w, out float dw);
                Profile(h, hF, half, rootHalf, belly, out float W, out float dW);
                // the face at x = side W + w (W: the half-thickness, both faces mirrored; w: the creases, both alike)
                Put(root + dd * h + xx * (side * W + w), PartMeshEmitter.Unit(xx * side - dd * (dW + side * dw), xx * side), Skin(start, s0, f, h / Dv, L));
            }

            for (int row = 0; row <= F; row++) Face(1, row);   // right face, root to rim
            for (int b = 1; b < B; b++)                         // the rim, round under the edge
            {
                float al = a0 + (2f * Mathf.PI - 2f * a0) * b / B;
                Vector3 rn = xx * Mathf.Sin(al) - dd * Mathf.Cos(al);
                Vector3 rp = bulb + rr * rn;
                // the whole rim on the last face row's link: a tube straddling a link joint is crushed by the bend
                Put(rp, rn, Skin(start, s0, f, Mathf.Min(hF / Dv, 0.9999f), L));
            }
            for (int row = F; row >= 0; row--) Face(-1, row);  // left face, rim to root
        }

        static readonly string[] kKinds = { "dewlap" };

        // Tests: station k's column in the bind rest - its face rows' depths below the root (0 .. F) and, last, the depth
        // its links' chain spans (metres at scale 1).
        internal float[] RowDepthsForTest(CreatureSpecies sp, int k)
        {
            FlowBody body = FindBody(KitView.Parts(sp));
            PartMeshEmitter m;
            buildingRest = true;
            try { m = PartMeshEmitter.ForBind(sp); }
            finally { buildingRest = false; }
            int F = RowsPerLink * links, prof = 2 * (F + 1) + (RimSegments - 1);
            var depths = new float[F + 2];
            Column(body, m, 0, k, new Vector3[prof], null, null, out _, out _, out _, out _, depths);
            return depths;
        }

        ulong CacheKey(FlowBody body, PartMeshEmitter m, int start)
        {
            // every serialized field of this sail and of the body it hangs from (RestMeshCache.MixData), its first bone,
            // the test hooks, the rest pose
            ulong h = RestMeshCache.MixData(RestMeshCache.MixData(RestMeshCache.Mix(RestMeshCache.Seed, 0xDE71), this), body);   // Dewlap
            h = RestMeshCache.Mix(RestMeshCache.Mix(RestMeshCache.Mix(h, start), NoPlaneClampForTest), PlainRestForTest);
            return RestMeshCache.MixRest(h, m);
        }

        // Where the rows (and the links) hang from at station place sigma in the bind rest: the throat's drawn midline
        // (its facets, OnRings) RowLift outside it. Shared by the mesh and the bind geometry.
        Vector3 RowRoot(FlowBody body, PartMeshEmitter m, float sigma)
        {
            float arc = body.UAtJoint(StationJoint(sigma)) * body.BodyLength;
            return OnRings(body, m, arc, -0.5f * Mathf.PI, out Vector3 n) + n * RowLift;
        }

        // The body's drawn skin at arc s, angle phi as its triangles have it: the surface at the four ring vertices around
        // (FlowBody's rings: ringsPerSegment per segment, ringSegments around from phi -90) interpolated on the quad's
        // triangle, so a point placed by it stays on the faceted skin (the true surface can lie inside a chord where the
        // neck bends).
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
            // the quad's two triangles as PartMeshEmitter.Stitch cuts it (a0 a1 b0, a1 b1 b0: the diagonal a1 - b0)
            return tp + ts <= 1f ? a0 + (a1 - a0) * tp + (b0 - a0) * ts : b1 + (b0 - b1) * (1f - tp) + (a1 - b1) * (1f - ts);
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

        // A swept (cut-short) row's face rows 1..F (hs[0] = 0: the root), from the throat to hF (the full row's x cs). The
        // full sail's rows are hF / cs / F apart, and that spacing is what a link joint needs: the rows on either side of
        // it ride different links, and a bend there shifts them against each other by up to a few mm (a sail between two
        // stations rides both stations' links, which the drawn rows only follow to first order). So: the first row keeps
        // the full gap below the throat (at most FirstRowMax x hF: a row hugging the throat is pushed into it by the
        // neck's bend); every link joint (depth j x Dl / links) inside the row gets a full gap of its own, centred on it;
        // the rest of the rows share the remaining stretches evenly (rows on one link move together, however close).
        static void RowDepths(float hF, float cs, float Dl, int L, float[] hs)
        {
            int F = hs.Length - 1;
            float g = hF / (cs * F), h1 = Mathf.Max(hF / F, Mathf.Min(FirstRowMax * hF, g));
            hs[0] = 0f; hs[1] = h1;
            if (F < 2) { hs[F] = hF; return; }
            // the joints strictly inside (h1, hF), each with its gap [b - g/2, b + g/2] (narrowed to share the room)
            int nb = 0;
            float b0 = 0f, b1 = 0f;
            for (int j = 1; j < L && nb < 2; j++)
            {
                float b = Dl * j / L;
                if (b <= h1 || b >= hF) continue;
                if (nb == 0) b0 = b; else b1 = b;
                nb++;
            }
            if (nb == 0)
            {
                for (int r = 2; r <= F; r++) hs[r] = Mathf.Lerp(h1, hF, (r - 1f) / (F - 1f));
                return;
            }
            float gap = Mathf.Min(g, (hF - h1) / (nb + 1));
            // the stretches between the gaps: [h1, b0 - gap/2], [b0 + gap/2, b1 - gap/2], [.. , hF]
            float s0a = h1, s0b = Mathf.Max(h1, b0 - 0.5f * gap);
            float s1a = Mathf.Min(hF, b0 + 0.5f * gap), s1b = nb > 1 ? Mathf.Max(s1a, b1 - 0.5f * gap) : hF;
            float s2a = nb > 1 ? Mathf.Min(hF, b1 + 0.5f * gap) : hF, s2b = hF;
            int n = nb + 1;
            float l0 = s0b - s0a, l1 = s1b - s1a, l2 = nb > 1 ? s2b - s2a : 0f, sum = Mathf.Max(1e-9f, l0 + l1 + l2);
            // rows per stretch: at least one each, the rest (F - n) by length
            int extra = F - n;
            int m0 = 1 + Mathf.FloorToInt(extra * l0 / sum), m1 = 1 + Mathf.FloorToInt(extra * l1 / sum), m2 = nb > 1 ? 1 + Mathf.FloorToInt(extra * l2 / sum) : 0;
            int left = F - (m0 + m1 + m2);
            // the remainder to the longest stretches (the last first: it ends at the rim)
            while (left > 0) { if (nb > 1 && l2 >= l1 && l2 >= l0) m2++; else if (l1 >= l0) m1++; else m0++; left--; }
            int r0 = 1;
            Fill(hs, ref r0, m0, s0a, s0b, true, false);
            Fill(hs, ref r0, m1, s1a, s1b, false, nb == 1);
            if (nb > 1) Fill(hs, ref r0, m2, s2a, s2b, false, true);
            hs[F] = hF;
        }

        // m rows evenly over [a, b] from row r on (one row: at a for the first stretch, b for the last, else the middle).
        static void Fill(float[] hs, ref int r, int m, float a, float b, bool firstStretch, bool lastStretch)
        {
            if (m == 1) hs[r++] = firstStretch ? a : lastStretch ? b : 0.5f * (a + b);
            else for (int k = 0; k < m; k++) hs[r++] = Mathf.Lerp(a, b, k / (m - 1f));
        }

        // The sheet's half-thickness W at h down a row (0 .. hF, the faces' end at the rim) and its slope dW / dh: the root's
        // excess thinning away as (1 - t)^2, plus the belly's sin^2 (no slope at the root or where the face meets the rim's
        // circle, so the sides are smooth): half at the rim, rootHalf at the root, half + belly-ish halfway down.
        static void Profile(float h, float hF, float half, float rootHalf, float belly, out float W, out float dW)
        {
            float t = h / hF, e = rootHalf - half, sn = Mathf.Sin(Mathf.PI * t);
            W = half + e * (1f - t) * (1f - t) + belly * sn * sn;
            dW = (-2f * e * (1f - t) + belly * Mathf.PI * Mathf.Sin(2f * Mathf.PI * t)) / hF;
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
    // The stations are coupled (coupling): each one's lean is smoothed with its neighbours', and every sub-step draws each
    // link's swing toward the same link's at the neighbouring stations (angle and rate) and holds the difference - and
    // the still hangs' - within maxTwistDeg (Limit), then no link bends more than its station's share of maxSwingDeg
    // (BendStop) - so a station whose root heaves on its own (the girdle joint under a heavy flight bob, or a fast neck
    // root) cannot swing apart from its neighbour and turn the sheet between them over.
    public sealed class DewlapRuntime : IPartRuntime
    {
        const float G = 9.81f;            // m/s^2 (an acceleration is size-free under Froude scaling)
        const float SmoothSeconds = 0.05f; // the drive's low-pass (x sqrt s)
        const float CouplingRate = 4f;     // the neighbours' pull on a link: CouplingRate x coupling x omega (1/s)
        const int LeanPasses = 2;          // the lean's smoothing passes along the stations

        readonly Dewlap p;
        readonly CreatureSim sim;
        readonly Dewlap.RestGeo geo;
        readonly int start, S, L;
        readonly float T, scale, reach;
        readonly float[] at;
        readonly AngleChain[] fa, sw;
        readonly float[] eqFa, eqSw, driveFa, driveSw, lean, leanFa, leanSw, fromFa, fromSw, tmp, tmpA, tmpV, tmpR, above, puff, give;
        readonly float[] hangFa, hangSw;   // each station's still hang per link (stations x (links + 1)): gravity's bend
        readonly Quaternion[] inv;
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
            leanFa = new float[S]; leanSw = new float[S]; fromFa = new float[S]; fromSw = new float[S];
            tmp = new float[S]; tmpA = new float[S]; tmpV = new float[S]; tmpR = new float[S]; above = new float[S]; puff = new float[S]; give = new float[S];
            inv = new Quaternion[S];
            hangFa = new float[S * (L + 1)]; hangSw = new float[S * (L + 1)];
            prev = new Vector3[S]; vel = new Vector3[S]; acc = new Vector3[S];
            points = new Vector3[S * (L + 1)];
            // the sheet's farthest reach sideways from a link (scale 1): the root's or the belly's half-thickness, the
            // creases and the rim, puffed at most
            float deepest = 0f;
            for (int k = 0; k < S; k++)
            {
                at[k] = part.StationJoint(k); fa[k] = new AngleChain(L + 1); sw[k] = new AngleChain(L + 1);
                // the swept rear is anchored into the chest: a station swings (and bends) at most its RearScale share
                give[k] = part.RearScale(k);
                deepest = Mathf.Max(deepest, geo.len[k] * L);
            }
            reach = (Mathf.Max(0.5f * part.rootThickness, 0.5f * part.thickness + part.billow * deepest) + Dewlap.CreaseAmount * part.thickness + part.rim)
                    * (1f + Dewlap.PuffPerBillow * part.billow);
            Pose(null, 0, true);
        }

        public PartStage Stage => PartStage.AfterLimbs;

        // Station k's swing (degrees): how far its edge link stands from its still hang (the drive at rest, gravity's bend).
        public float Swing(int k)
        {
            float a = fa[k].Angle(L) - eqFa[k], b = sw[k].Angle(L) - eqSw[k];
            return Mathf.Sqrt(a * a + b * b);
        }

        // Station k's lean (degrees): the pendulum angle its acceleration asks for this step (smoothed with its
        // neighbours'), before the flap's own ease and chain - what the swing lags behind (tests).
        public float Lean(int k) => lean[k];

        // Station k's link angles (degrees), fore-aft and sideways, link 1..links (tests).
        public float AngleFa(int k, int link) => fa[k].Angle(link);
        public float AngleSw(int k, int link) => sw[k].Angle(link);

        // A link's direction in its station's frame at fore-aft angle a and sideways angle b (degrees).
        Vector3 Dir(int k, float a, float b) => Swung(a, b) * geo.dir[k];

        static Quaternion Swung(float a, float b) => Quaternion.AngleAxis(a, Vector3.right) * Quaternion.AngleAxis(b, Vector3.forward);

        bool Coupled => p.coupling > 0f && !Dewlap.NoCouplingForTest;

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
            // each station's lean: a pendulum leans away from its root's acceleration
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
                inv[s] = Quaternion.Inverse(Q);
                Vector3 al = inv[s] * acc[s];
                float den = Mathf.Max(0.2f * G, G + al.y);
                // forward (+z) acceleration swings the edge back (+a)
                float ms = max * give[s];
                leanFa[s] = Mathf.Atan2(al.z, den) * Mathf.Rad2Deg;
                leanSw[s] = -Mathf.Atan2(al.x, den) * Mathf.Rad2Deg;
                // at most ms either way: the lean's whole angle, not each axis (a diagonal lean is no steeper)
                float lm = Mathf.Sqrt(leanFa[s] * leanFa[s] + leanSw[s] * leanSw[s]);
                if (lm > ms) { float sc = ms / lm; leanFa[s] *= sc; leanSw[s] *= sc; }
            }
            // the sheet leans as one: each station's lean smoothed with its neighbours' (a station on a heaving girdle
            // takes its neighbour's share)
            bool coupled = Coupled;
            if (coupled) { SmoothAlong(leanFa); SmoothAlong(leanSw); }
            for (int s = 0; s < S; s++)
            {
                float leanA = leanFa[s], leanB = leanSw[s];
                lean[s] = Mathf.Sqrt(leanA * leanA + leanB * leanB);
                // the flap cannot lean faster than it swings: the lean eases in over 1 / (2 pi swingHz) (x sqrt s)
                if (!started) { driveFa[s] = leanA; driveSw[s] = leanB; }
                fromFa[s] = driveFa[s]; fromSw[s] = driveSw[s];
                driveFa[s] += (leanA - driveFa[s]) * kd;
                driveSw[s] += (leanB - driveSw[s]) * kd;
                // gravity: each link bends toward the down direction by gravityDeg x the sine of its angle to it
                Vector3 down = inv[s] * -sim.Up[Mathf.Clamp(Mathf.RoundToInt(at[s]), 0, J.Length - 1)];
                AngleChain cf = fa[s], cs = sw[s];
                float sf = 0f, ss = 0f, ms = max * give[s];
                for (int i = 1; i <= L; i++)
                {
                    Vector3 g = Vector3.Cross(Dir(s, cf.Angle(i - 1), cs.Angle(i - 1)), down);
                    sf += Mathf.Clamp(p.gravityDeg * g.x, -ms, ms); ss += Mathf.Clamp(p.gravityDeg * g.z, -ms, ms);
                    hangFa[s * (L + 1) + i] = sf; hangSw[s * (L + 1) + i] = ss;
                }
            }
            // the still hangs keep the stations' relative twist too (stations hanging apart shear the sheet between
            // them as a swing would) - then each link's offset is its hang's bend
            if (coupled) { Twist(hangFa); Twist(hangSw); }
            for (int s = 0; s < S; s++)
            {
                AngleChain cf = fa[s], cs = sw[s];
                int o = s * (L + 1);
                for (int i = 1; i <= L; i++)
                {
                    cf.SetOffset(i, hangFa[o + i] - (i > 1 ? hangFa[o + i - 1] : 0f));
                    cs.SetOffset(i, hangSw[o + i] - (i > 1 ? hangSw[o + i - 1] : 0f));
                }
                eqFa[s] = hangFa[o + L]; eqSw[s] = hangSw[o + L];
                // a new sail starts hanging still (its drive, gravity's bend), not swinging in from straight
                if (!started)
                {
                    cf.Reset(driveFa[s]); cs.Reset(driveSw[s]);
                    for (int i = 1; i <= L; i++) { cf.Set(i, driveFa[s] + hangFa[o + i], 0f); cs.Set(i, driveSw[s] + hangSw[o + i], 0f); }
                }
            }
            // fine steps (omega h <= 0.05: the swing's curve is the same at every size and frame rate), the drive eased
            // across them; after each the stations' coupling, then the plane stop
            for (int it = 1; it <= sub; it++)
            {
                float w = (float)it / sub;
                for (int s = 0; s < S; s++)
                {
                    fa[s].Step(fromFa[s] + (driveFa[s] - fromFa[s]) * w, h, omega, zeta, max * give[s]);
                    sw[s].Step(fromSw[s] + (driveSw[s] - fromSw[s]) * w, h, omega, zeta, max * give[s]);
                }
                if (coupled) { Couple(fa, hangFa, h, omega); Couple(sw, hangSw, h, omega); }
                for (int s = 0; s < S; s++) BendStop(fa[s], sw[s], max * give[s]);
                if (!Dewlap.NoPlaneClampForTest) for (int s = 0; s < S; s++) PlaneStop(s, fa[s], sw[s], sinM);
            }
            started = true;
            // the belly puffs out with the swing: up to PuffPerBillow x billow wider at a full swing
            for (int s = 0; s < S; s++) puff[s] = max > 0f && p.billow > 0f ? Dewlap.PuffPerBillow * p.billow * Mathf.Clamp01(Swing(s) / max) : 0f;
            Pose(null, 0, false);
        }

        // One value per station drawn toward its neighbours' mean (an end station toward its one neighbour), coupling of
        // the way, LeanPasses times.
        void SmoothAlong(float[] a)
        {
            float c = p.coupling;
            for (int pass = 0; pass < LeanPasses; pass++)
            {
                for (int s = 0; s < S; s++) tmp[s] = a[s] + c * (Neighbours(a, s) - a[s]);
                for (int s = 0; s < S; s++) a[s] = tmp[s];
            }
        }

        float Neighbours(float[] a, int s) => s == 0 ? a[1] : s == S - 1 ? a[S - 2] : 0.5f * (a[s - 1] + a[s + 1]);

        // The stations' coupling on one family of chains for a sub-step h, on each link's swing (its angle less the
        // station's still hang: stations hang differently at rest, and that is the bind, not a twist), link by link from
        // the root: each link's swing drawn toward the same link's at its neighbouring stations (swing and rate,
        // 1 - exp(-CouplingRate x coupling x omega x h) of the way), then the relative twist held - two neighbouring
        // stations' swings, and their bends over the link above, at most maxTwistDeg apart (a pair over it meets halfway,
        // at its mean rate; two Gauss-Seidel sweeps). Rows between two stations ride both stations' links: the stations'
        // bends at a link joint differing by b shear them there by about b x a quarter of the stations' spacing.
        void Couple(AngleChain[] ch, float[] hang, float h, float omega)
        {
            float kap = 1f - Mathf.Exp(-CouplingRate * p.coupling * omega * h);
            for (int s = 0; s < S; s++) above[s] = ch[s].Angle(0);   // link 0: the drive (no hang)
            for (int i = 1; i <= L; i++)
            {
                for (int s = 0; s < S; s++) { tmp[s] = ch[s].Angle(i) - hang[s * (L + 1) + i]; tmpV[s] = ch[s].Rate(i); }
                for (int s = 0; s < S; s++)
                {
                    tmpA[s] = tmp[s] + kap * (Neighbours(tmp, s) - tmp[s]);
                    tmpR[s] = tmpV[s] + kap * (Neighbours(tmpV, s) - tmpV[s]);
                }
                Limit(tmpA, above, tmpR);
                for (int s = 0; s < S; s++) { ch[s].Set(i, tmpA[s] + hang[s * (L + 1) + i], tmpR[s]); above[s] = tmpA[s]; }
            }
        }

        // The relative twist held on one link's values along the stations (a: this link's, up: the link above's; rates:
        // averaged over a pair it moves, or null): neighbours at most maxTwistDeg apart, and their bends over the link
        // above too; a pair over it meets in proportion to the stations' give (a rigid station stays put); two
        // Gauss-Seidel sweeps.
        void Limit(float[] a, float[] up, float[] rates)
        {
            float mt = p.maxTwistDeg;
            for (int sweep = 0; sweep < 2; sweep++)
                for (int s = 0; s + 1 < S; s++)
                {
                    float d = a[s + 1] - a[s], db = d - (up[s + 1] - up[s]);
                    float e = Over(d, mt) + Over(db - Over(d, mt), mt), wsum = give[s] + give[s + 1];
                    if (e == 0f || !(wsum > 0f)) continue;
                    a[s] += e * give[s] / wsum; a[s + 1] -= e * give[s + 1] / wsum;
                    if (rates != null) { float vm = 0.5f * (rates[s] + rates[s + 1]); rates[s] = vm; rates[s + 1] = vm; }
                }
        }

        // Limit on the still hangs (stations x (links + 1), link 0 = 0), link by link from the root.
        void Twist(float[] hang)
        {
            for (int s = 0; s < S; s++) above[s] = 0f;
            for (int i = 1; i <= L; i++)
            {
                for (int s = 0; s < S; s++) tmpA[s] = hang[s * (L + 1) + i];
                Limit(tmpA, above, null);
                for (int s = 0; s < S; s++) { hang[s * (L + 1) + i] = tmpA[s]; above[s] = tmpA[s]; }
            }
        }

        // How far x is beyond +-limit (signed; 0 within).
        static float Over(float x, float limit) => x > limit ? x - limit : x < -limit ? x + limit : 0f;

        // The bend stop: no link bends more than lim (degrees) over the link above it - its whole bend, fore-aft and
        // sideways together (the chains clamp each axis alone: a diagonal bend could reach sqrt 2 x lim); an
        // over-bent link is turned back toward the link above, its rate with it.
        static void BendStop(AngleChain cf, AngleChain cs, float lim)
        {
            for (int i = 1; i < cf.Count; i++)
            {
                float bf = cf.Angle(i) - cf.Angle(i - 1), bs = cs.Angle(i) - cs.Angle(i - 1), b2 = bf * bf + bs * bs;
                if (!(b2 > lim * lim)) continue;
                float k = lim / Mathf.Sqrt(b2);
                cf.Set(i, cf.Angle(i - 1) + bf * k, cf.Rate(i - 1) + (cf.Rate(i) - cf.Rate(i - 1)) * k);
                cs.Set(i, cs.Angle(i - 1) + bs * k, cs.Rate(i - 1) + (cs.Rate(i) - cs.Rate(i - 1)) * k);
            }
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

        // Each station's links down from its root (dst null: only the link points). A swinging station's bones are
        // widened sideways (their x: across the sheet) by its puff.
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
                Vector3 bs = rest || puff[s] == 0f ? sv : new Vector3(scale * (1f + puff[s]), scale, scale);
                for (int l = 0; l < L; l++)
                {
                    float a = rest ? 0f : fa[s].Angle(l + 1), b = rest ? 0f : sw[s].Angle(l + 1);
                    Quaternion r = a == 0f && b == 0f ? Q * geo.rot[s] : Q * Swung(a, b) * geo.rot[s];
                    if (dst != null) dst[offset + start + s * L + l] = Matrix4x4.TRS(pt, r, bs);
                    pt += r * Vector3.down * (geo.len[s] * scale);
                    points[o + l + 1] = pt;
                }
            }
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            float r = reach * scale;
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
