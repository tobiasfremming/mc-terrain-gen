using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class WorldConfigTests
    {
        [Test]
        public void TerrainLayerMaskMatchesLayer()
        {
            var c = ScriptableObject.CreateInstance<WorldConfig>();
            c.terrainLayer = 9;
            Assert.That(c.TerrainLayerMask, Is.EqualTo(1 << 9));
            Assert.That(c.demoBootstrap, Is.True, "existing scenes keep the demo player by default");
            Object.DestroyImmediate(c);
        }
    }
}
