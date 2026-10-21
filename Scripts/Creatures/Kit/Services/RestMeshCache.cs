// Creature kit service (Apex redesign, task 10): a cache of the rest-pose mesh a surface part emits onto a FlowBody
// (PitRow, Dewlap; FlowBody itself). Such a part samples the body's drawn surface (FlowBody.SurfaceAt: six monotone cubics and finite
// differences per call) for every vertex - exact, but slow: the Apex's ten pits alone took ~4 ms of a 5 ms mesh budget
// (CreatureKitTests 4, MeshTime). The mesh depends only on the part's data, the body's shape data and the rest pose the
// emitter draws on, so the part keys it on all three (Key, Mix, FlowBody.ShapeKey, MixRest), records what it emitted the
// first time (Store) and replays it after (TryReplay): the same vertices, triangles and skins bit for bit, its named
// kinds re-resolved to this species' slots. Build-time only: it allocates; the cache is bounded (cleared when full).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public static class RestMeshCache
    {
        sealed class Entry
        {
            public Vector3[] pos, nrm;
            public Vector2[] uv0, uv1;
            public Vector4[] uv2;
            public int[] tris;          // relative to the first vertex
            public int[] kindSlot;      // the slots the kinds had when recorded
        }

        const int kMax = 64;
        static readonly Dictionary<ulong, Entry> cache = new Dictionary<ulong, Entry>();

        // test hooks (CreatureRestMeshCacheTests): count the replays, turn the cache off
        internal static int Hits;
        internal static bool OffForTest;
        internal static bool OneKeyForTest;   // RED (CreatureRestMeshCacheTests): every key the same - an edited part replays the old mesh

        public const ulong Seed = 14695981039346656037UL;
        public static ulong Mix(ulong h, float f) { h ^= (uint)BitConverter.SingleToInt32Bits(f); return h * 1099511628211UL; }
        public static ulong Mix(ulong h, int i) { h ^= (uint)i; return h * 1099511628211UL; }
        public static ulong Mix(ulong h, bool b) => Mix(h, b ? 1 : 0);
        public static ulong Mix(ulong h, Vector3 v) => Mix(Mix(Mix(h, v.x), v.y), v.z);

        // The rest pose the emitter draws on: every spine joint's place and frame.
        public static ulong MixRest(ulong h, PartMeshEmitter m)
        {
            Vector3[] J = m.Rest.Joints; Quaternion[] R = m.Rest.Rotations;
            h = Mix(h, J.Length);
            for (int i = 0; i < J.Length; i++)
            {
                h = Mix(h, J[i]);
                Quaternion q = R[i];
                h = Mix(Mix(Mix(Mix(h, q.x), q.y), q.z), q.w);
            }
            return h;
        }

        // Replays the mesh recorded under `key` (false: none, emit it and Store it). kinds: the named kinds the part emits
        // with, in the order it passed them to Store.
        public static bool TryReplay(PartMeshEmitter m, ulong key, string[] kinds)
        {
            if (OneKeyForTest) key = 0UL;
            if (OffForTest || !cache.TryGetValue(key, out Entry e)) return false;
            int first = m.VertexCount;
            CreatureMeshLists l = m.Lists;
            l.pos.AddRange(e.pos); l.nrm.AddRange(e.nrm); l.uv0.AddRange(e.uv0); l.uv2.AddRange(e.uv2);
            bool same = true;
            for (int k = 0; k < e.kindSlot.Length; k++) same &= m.Slot(kinds[k]) == e.kindSlot[k];
            if (same) l.uv1.AddRange(e.uv1);
            else
                for (int i = 0; i < e.uv1.Length; i++)
                {
                    Vector2 u1 = e.uv1[i];
                    int part = Mathf.RoundToInt(u1.x);
                    for (int k = 0; k < e.kindSlot.Length; k++) if (part == e.kindSlot[k]) { u1.x = m.Slot(kinds[k]); break; }
                    l.uv1.Add(u1);
                }
            for (int t = 0; t < e.tris.Length; t++) l.tris.Add(first + e.tris[t]);
            Hits++;
            return true;
        }

        internal static void ClearForTest() => cache.Clear();

        // Records what the part emitted from firstVertex / firstTri (the emitter's counts before it began).
        public static void Store(PartMeshEmitter m, ulong key, int firstVertex, int firstTri, string[] kinds)
        {
            if (OffForTest) return;
            if (OneKeyForTest) key = 0UL;
            CreatureMeshLists l = m.Lists;
            int n = l.pos.Count - firstVertex, nt = l.tris.Count - firstTri;
            var e = new Entry
            {
                pos = new Vector3[n], nrm = new Vector3[n], uv0 = new Vector2[n], uv1 = new Vector2[n], uv2 = new Vector4[n],
                tris = new int[nt], kindSlot = new int[kinds.Length],
            };
            for (int i = 0; i < n; i++)
            {
                e.pos[i] = l.pos[firstVertex + i]; e.nrm[i] = l.nrm[firstVertex + i];
                e.uv0[i] = l.uv0[firstVertex + i]; e.uv1[i] = l.uv1[firstVertex + i]; e.uv2[i] = l.uv2[firstVertex + i];
            }
            for (int t = 0; t < nt; t++) e.tris[t] = l.tris[firstTri + t] - firstVertex;
            for (int k = 0; k < kinds.Length; k++) e.kindSlot[k] = m.Slot(kinds[k]);
            if (cache.Count >= kMax) cache.Clear();
            cache[key] = e;
        }
    }
}
