using System.Collections.Generic;
using CreatureKit;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TerrainGen.Tests
{
    // The lab on hilly ground (2026-10-19, "trees spawning underneath the terrain"): the ring puts enclosures ~70 m from
    // the player, where the DesertWorld dunes stand 8-11 m above or below the player's spot. The creature probe
    // (FieldGroundProbe) only reaches 16 m down through air and 8 m up through solid from the point it is given, and
    // the lab asked at its slots on the player's height: every probe under a high dune missed and those enclosures,
    // their trees, rocks and animals stayed at the player's height, inside the sand. LabGround scans the column instead.
    // Offline: a heightfield probe with the field probe's reach stands in for the dunes.
    public class CreatureLabGroundTests
    {
        // The player in a dune basin (as on DesertWorld, where the ring's left side stands 8-11 m above the spawn):
        // the ground rises ~0.16 m per metre out, tilted and rippled. Probe semantics as FieldGroundProbe: the nearest up-facing
        // surface along up within the window, then 16 m down through air / 8 m up through solid; otherwise a miss.
        sealed class DuneProbe : IGroundProbe
        {
            public int Misses;
            public static float H(float x, float z) => 0.16f * Mathf.Sqrt(x * x + z * z) - 0.05f * x + 2.5f * Mathf.Sin(x / 13f) * Mathf.Sin(z / 11f);
            public bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal)
            {
                float h = H(near.x, near.z);
                float reach = near.y >= h ? Mathf.Max(window, 16f) : Mathf.Max(window, 8f);
                const float e = 0.01f;
                float dx = (H(near.x + e, near.z) - H(near.x - e, near.z)) / (2f * e), dz = (H(near.x, near.z + e) - H(near.x, near.z - e)) / (2f * e);
                normal = new Vector3(-dx, 1f, -dz).normalized;
                point = new Vector3(near.x, h, near.z);
                if (Mathf.Abs(near.y - h) <= reach) return true;
                Misses++; point = near; normal = up;
                return false;
            }
        }

        static float[] RosterRadii(out List<CreatureLabRoster.Entry> list)
        {
            var r = AssetDatabase.LoadAssetAtPath<CreatureLabRoster>(CreatureLabRoster.AssetPath);
            Assert.IsNotNull(r, CreatureLabRoster.AssetPath);
            r.Split(out list, out _);
            var radius = new float[list.Count];
            for (int k = 0; k < list.Count; k++)
            {
                CreatureSpecies sp = list[k].species;
                float scale = list[k].scaleOverride > 0f ? list[k].scaleOverride : sp.scale;
                radius[k] = LabHabitat.EnclosureRadius(sp, CreatureLab.MaxScale(sp, scale), 14f);
            }
            return radius;
        }

        const float spacing = 25f, arcRadius = 30f, search = 15f;   // CreatureLab's defaults
        const float kOnGround = 0.02f;

        // RED (the bug): the field probe's reach from the player's height misses the dune tops on the ring.
        [Test] public void Red_ProbeFromPlayerHeight_MissesTheHighDunes()
        {
            var dunes = new DuneProbe();
            int misses = 0;
            for (int a = 0; a < 36; a++)
            {
                Vector3 p = Quaternion.AngleAxis(10f * a, Vector3.up) * Vector3.forward * 70f;
                if (!dunes.Probe(new Vector3(p.x, DuneProbe.H(0f, 0f), p.z), Vector3.up, LabLayout.kProbeWindow, out _, out _)) misses++;
            }
            TestContext.Out.WriteLine($"RED: {misses} of 36 ring points out of the probe's reach from the player's height");
            Assert.Greater(misses, 0, "the dunes do not bite");
        }

        // LabGround answers anywhere in a +-kColumnSpan column, with the surface (not the point asked from).
        [Test] public void LabGround_FindsTheColumnSurface()
        {
            IGroundProbe g = LabGround.Wrap(new DuneProbe());
            Assert.AreSame(g, LabGround.Wrap(g), "wrapping twice");
            for (int i = 0; i < 50; i++)
            {
                float x = -90f + 3.7f * i, z = 60f - 2.3f * i;
                foreach (float y in new[] { -60f, -15f, 0f, 20f, 80f })
                {
                    Assert.IsTrue(g.Probe(new Vector3(x, y, z), Vector3.up, LabLayout.kProbeWindow, out Vector3 p, out Vector3 n), $"({x}, {y}, {z})");
                    Assert.AreEqual(DuneProbe.H(x, z), p.y, 1e-3f, $"({x}, {y}, {z})");
                    Assert.Greater(n.y, 0.5f);
                }
            }
        }

        // The whole roster on the dunes: every centre on the surface (and the rims answered: real flatness, not the
        // -2 of an unprobed spot), no two curbs within the gap, the player's spot outside every curb.
        [Test] public void Lab_Centres_OnTheSurface_NoOverlaps()
        {
            float[] radius = RosterRadii(out List<CreatureLabRoster.Entry> list);
            int n = radius.Length;
            var dunes = new DuneProbe();
            Vector3 origin = new Vector3(0f, DuneProbe.H(0f, 0f), 0f);
            var placed = new List<Vector4>();
            var flat = new float[n];
            Vector3[] c = LabLayout.Centres(dunes, origin, Vector3.forward, Vector3.up, radius, spacing, arcRadius, search, flat, placed);
            Assert.AreEqual(n, placed.Count);
            float maxDy = 0f;
            for (int k = 0; k < n; k++)
            {
                float dy = c[k].y - DuneProbe.H(c[k].x, c[k].z);
                maxDy = Mathf.Max(maxDy, Mathf.Abs(c[k].y - origin.y));
                Assert.AreEqual(0f, dy, kOnGround, $"{list[k].species.name} at {c[k]}: {dy:F2} m off the ground");
                Assert.Greater(flat[k], 0f, $"{list[k].species.name}: its rim was never probed");
                Assert.Greater(new Vector2(c[k].x, c[k].z).magnitude, radius[k] + LabLayout.kViewGap - 1e-3f, list[k].species.name + " on the player's spot");
                for (int b = k + 1; b < n; b++)
                {
                    Vector3 d = c[k] - c[b]; d.y = 0f;
                    Assert.GreaterOrEqual(d.magnitude, radius[k] + radius[b] + LabLayout.kEnclosureGap - 1e-3f, $"{list[k].species.name} and {list[b].species.name} overlap");
                }
            }
            TestContext.Out.WriteLine($"{n} enclosures, up to {maxDy:F1} m above/below the player, {dunes.Misses} raw probe misses rescued");
            Assert.Greater(maxDy, 8f, "the dunes do not reach past the probe: the test does not bite");
        }

        // A grove after the ring (OctopusLab: a slot left-front at 0.6 x the farthest curb, which lies on the ring):
        // FreeSpot moves it out until it crowds nothing, on the surface; the second grove clears the first.
        [Test] public void Grove_FreeSpot_ClearOfTheRing()
        {
            float[] radius = RosterRadii(out _);
            var dunes = new DuneProbe();
            Vector3 origin = new Vector3(0f, DuneProbe.H(0f, 0f), 0f);
            var placed = new List<Vector4>();
            LabLayout.Centres(dunes, origin, Vector3.forward, Vector3.up, radius, spacing, arcRadius, search, null, placed);
            placed.Add(new Vector4(origin.x, origin.y, origin.z, 1f)); // the player
            float farthest = 0f;
            foreach (Vector4 p in placed) farthest = Mathf.Max(farthest, new Vector2(p.x, p.z).magnitude + p.w);
            Vector3 dir = (Vector3.forward * 0.6f - Vector3.right * 0.8f).normalized;
            Assert.GreaterOrEqual(LabLayout.Crowds(origin + dir * (0.6f * farthest + 18f), Vector3.up, 12f, placed), 0, "the grove slot is clear of the ring already: the test does not bite");
            foreach (float groveR in new[] { 12f, 9f })
            {
                Vector3 slot = origin + dir * (0.6f * farthest + groveR + 6f);
                Vector3 g = LabLayout.FreeSpot(dunes, origin, slot, Vector3.up, groveR, 2f * groveR, placed, out _);
                Assert.Less(LabLayout.Crowds(g, Vector3.up, groveR, placed), 0, $"grove r {groveR} at {g} crowds an enclosure");
                Assert.AreEqual(DuneProbe.H(g.x, g.z), g.y, kOnGround, "grove centre off the ground");
                placed.Add(new Vector4(g.x, g.y, g.z, groveR));
            }
        }

        [Test] public void Clear_OutsideEveryCurb()
        {
            var placed = new List<Vector4> { new Vector4(0f, 0f, 0f, 5f), new Vector4(20f, 0f, 0f, 5f) };
            Assert.IsTrue(LabLayout.Clear(new Vector3(10f, 0f, 0f), Vector3.up, 1.5f, placed));
            Assert.IsFalse(LabLayout.Clear(new Vector3(6f, 0f, 0f), Vector3.up, 1.5f, placed));
            Assert.IsFalse(LabLayout.Clear(new Vector3(20f, 5f, 3f), Vector3.up, 1.5f, placed));
        }
    }
}
