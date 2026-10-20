using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Creature kit check 14 (kit services 2026-10-20): the GROUP. A species the lab keeps as a group (the roster's
    // `count` > 1) is spawned as the lab spawns it - CreatureKit.LabGroup: the ring at 0.4 of the safe radius, the
    // consecutive seeds, each individual's own size - with its own brains in its lab area, and run for kSeconds
    // (x sqrt s). No two members may touch: every member's body is the capsules fitted to its real mesh, one or more per
    // bone (CreatureKit.BoneCapsules: body, legs, wings, tail, head), and two members' capsules must never overlap while
    // both are in sight (a burrower underground is out of the picture). Reports name the time, the two members and the
    // two parts. Groups are module state shared through static registries: they are cleared before and after.
    public static class KitGroupCheck
    {
        public const float kSeconds = 180f;
        const int kEvery = 4;                   // sample every 4th tick (15 Hz)
        const float kSettle = 1f;               // the first second (x sqrt s): the ring sorts itself out
        const int kLabSeed = 7;                 // CreatureLab.seed's default

        // The lab's group size for a species (the roster entry naming its asset), and its enclosure index (seeds).
        public static int RosterCount(string species, out int enclosure)
        {
            enclosure = 0;
            string guid = null;
            foreach (string l in File.ReadAllLines(CreatureKitAssets.FullPath(CreatureKitAssets.PathOf(species) + ".meta")))
                if (l.StartsWith("guid:", StringComparison.Ordinal)) guid = l.Substring(5).Trim();
            string[] lines = File.ReadAllLines(CreatureKitAssets.FullPath(CreatureLabRoster.AssetPath));
            int k = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (!t.StartsWith("- species:", StringComparison.Ordinal)) continue;
                k++;
                if (guid == null || !t.Contains(guid)) continue;
                enclosure = k;
                for (int j = i + 1; j < lines.Length && !lines[j].TrimStart().StartsWith("- ", StringComparison.Ordinal); j++)
                {
                    string q = lines[j].Trim();
                    if (q.StartsWith("count:", StringComparison.Ordinal)) return Mathf.Max(1, int.Parse(q.Substring(6).Trim()));
                }
                return 1;
            }
            return 1;
        }

        public sealed class Result
        {
            public string Problem;
            public float Closest = float.PositiveInfinity;   // smallest capsule gap seen (m; < 0 = overlap)
            public string ClosestAt;
            public int Samples, Contacts;
            public float Seconds;
        }

        static void ClearGroups() { KitGroup.ClearAll(); CreatureGroup.Clear(); TunnelNetwork.ClearAll(); CreatureDigging.Clear(); }

        // count individuals of sp (enclosure k's seeds), run seconds (x sqrt s). stopAtFirst: the check; false: count
        // every contact (reports). brains: replace the species' own (a RED).
        public static Result Run(CreatureSpecies sp, int count, int enclosure, float seconds = kSeconds, bool stopAtFirst = true,
                                 Func<CreatureSim, EnclosureArea, int, ICreatureBrain> brains = null)
        {
            ClearGroups();
            try { return RunGroup(sp, count, enclosure, seconds, stopAtFirst, brains); }
            finally { ClearGroups(); }
        }

        static Result RunGroup(CreatureSpecies sp, int count, int enclosure, float seconds, bool stopAtFirst, Func<CreatureSim, EnclosureArea, int, ICreatureBrain> brains)
        {
            var res = new Result();
            EnclosureArea area = KitChecks.LabArea(sp);
            Vector3 up = Vector3.up, face = Vector3.forward;
            var sims = new CreatureSim[count]; var brain = new ICreatureBrain[count];
            for (int i = 0; i < count; i++)
            {
                LabGroup.Start(area.center, up, face, area.safeRadius, i, count, out Vector3 at, out Vector3 dir);
                int seed = LabGroup.Seed(kLabSeed, enclosure, i, count);
                float s = LabGroup.Individual(sp, seed, sp.scale).sizeScale;
                sims[i] = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, at, dir, s, seed) { Bounds = area };
                brain[i] = brains != null ? brains(sims[i], area, seed) : KitPoseSweep.BrainOf(sp, area, seed);
            }

            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            Vector3[] v = mesh.vertices;
            var uv2 = new List<Vector4>(); mesh.GetUVs(2, uv2);
            Object.DestroyImmediate(mesh);
            BoneCapsules caps = BoneCapsules.Fit(v, uv2, CreatureRestPose.BindPoses(sp));
            int n = caps.Count;
            var bones = new Matrix4x4[sp.BoneCount];
            var A = new Vector3[count][]; var B = new Vector3[count][]; var R = new float[count][];
            var centre = new Vector3[count]; var reach = new float[count];
            for (int i = 0; i < count; i++) { A[i] = new Vector3[n]; B[i] = new Vector3[n]; R[i] = new float[n]; }

            float T = Mathf.Sqrt(sp.scale);
            int ticks = Mathf.CeilToInt(seconds * T / KitPoseSweep.kDt), settle = Mathf.CeilToInt(kSettle * T / KitPoseSweep.kDt);
            for (int t = 0; t < ticks; t++)
            {
                for (int i = 0; i < count; i++) sims[i].Tick(KitPoseSweep.kDt, brain[i].Target(sims[i], KitPoseSweep.kDt));
                if (t < settle || t % kEvery != 0) continue;
                res.Samples++;
                for (int i = 0; i < count; i++)
                {
                    sims[i].WriteBoneWorld(bones, 0);
                    caps.World(bones, A[i], B[i], R[i]);
                    // a bounding sphere for the broad phase
                    Vector3 lo = A[i][0], hi = A[i][0];
                    for (int c = 0; c < n; c++) { lo = Vector3.Min(lo, Vector3.Min(A[i][c], B[i][c]) - Vector3.one * R[i][c]); hi = Vector3.Max(hi, Vector3.Max(A[i][c], B[i][c]) + Vector3.one * R[i][c]); }
                    centre[i] = 0.5f * (lo + hi); reach[i] = 0.5f * (hi - lo).magnitude;
                }
                for (int i = 0; i < count; i++)
                    for (int j = i + 1; j < count; j++)
                    {
                        if ((centre[i] - centre[j]).magnitude > reach[i] + reach[j]) continue;
                        if (KitGroup.Concealed(sims[i]) || KitGroup.Concealed(sims[j])) continue;   // underground: out of the picture
                        for (int a = 0; a < n; a++)
                            for (int b = 0; b < n; b++)
                            {
                                float d = BoneCapsules.SegmentDistance(A[i][a], B[i][a], A[j][b], B[j][b], out Vector3 ca, out Vector3 cb) - R[i][a] - R[j][b];
                                if (d < res.Closest)
                                {
                                    res.Closest = d;
                                    res.ClosestAt = $"t={sims[i].Time:F1} s: members {i} and {j}, {KitPoseSweep.BonePart(sp, caps.Bone[a])} and {KitPoseSweep.BonePart(sp, caps.Bone[b])} at ({ca.x:F2}, {ca.y:F2}, {ca.z:F2})";
                                }
                                if (d < 0f)
                                {
                                    res.Contacts++;
                                    if (stopAtFirst)
                                    {
                                        res.Problem = $"members touch: {res.ClosestAt}, overlapping {-d * 1000f:F0} mm";
                                        res.Seconds = sims[0].Time;
                                        return res;
                                    }
                                }
                            }
                    }
            }
            res.Seconds = sims[0].Time;
            if (res.Contacts > 0) res.Problem = $"{res.Contacts} contact samples; the deepest: {res.ClosestAt}";
            return res;
        }
    }
}
