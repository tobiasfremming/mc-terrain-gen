using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class AssemblySmokeTests
    {
        [Test]
        public void RuntimeTypesAreVisibleFromTests()
        {
            var f = ScriptableObject.CreateInstance<PlaneField>();
            f.height = 3f;
            Assert.That(f.Sample(new Vector3(0, 0, 0)), Is.EqualTo(3f));
            Assert.That(typeof(MCChunkManager).Assembly.GetName().Name, Is.EqualTo("TerrainGen.Runtime"));
            Object.DestroyImmediate(f);
        }
    }
}
