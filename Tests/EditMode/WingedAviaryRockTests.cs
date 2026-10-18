using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // The aviary's rock perches (AviaryRock / AviaryDressing.RockMesh) render outside-in: user feedback
    // 2026-10-18 "the rocks have inverted mesh". Every triangle's front face (Cross(b - a, c - a), Unity's
    // winding, the one RecalculateNormals and the creature meshes use) points away from the rock's centre.
    public class WingedAviaryRockTests
    {
        [Test] public void AviaryRock_FacesOutward()
        {
            var v = new List<Vector3>(); var t = new List<int>();
            int checkedTris = 0;
            foreach (int seed in new[] { 1, 7, 42, 1234 })
            {
                var size = new Vector3(1.6f + 0.1f * (seed % 7), 0.9f + 0.05f * (seed % 5), 1.4f);
                AviaryRock.Geometry(size, seed, v, t);
                Vector3 c = AviaryRock.Center(size);
                for (int k = 0; k < t.Count; k += 3)
                {
                    Vector3 a = v[t[k]], b = v[t[k + 1]], d = v[t[k + 2]];
                    Vector3 n = Vector3.Cross(b - a, d - a);
                    Vector3 out_ = (a + b + d) / 3f - c;
                    Assert.Greater(Vector3.Dot(n, out_), 0f, $"rock (seed {seed}) triangle {k / 3} faces inward");
                    checkedTris++;
                }
            }
            Assert.Greater(checkedTris, 1000);
        }
    }
}
