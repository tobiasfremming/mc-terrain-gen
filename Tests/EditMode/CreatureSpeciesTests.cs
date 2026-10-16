using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class CreatureSpeciesTests
    {
        CreatureSpecies sp;

        [TearDown] public void TearDown() { if (sp != null) Object.DestroyImmediate(sp); }

        CreatureSpecies Make(int n, int legCount)
        {
            sp = ScriptableObject.CreateInstance<CreatureSpecies>();
            sp.spacing = 0.1f;
            sp.joints = new SpineJoint[n];
            for (int i = 0; i < n; i++) sp.joints[i] = new SpineJoint { halfWidth = 0.05f, halfHeight = 0.04f, clearance = 0.05f, bendLimitDeg = 20f };
            sp.legs = new LegSpec[legCount];
            for (int i = 0; i < legCount; i++)
                sp.legs[i] = new LegSpec { attachJoint = 1, side = i % 2 == 0 ? -1 : 1, upperLength = 0.1f, lowerLength = 0.1f };
            return sp;
        }

        [Test] public void Validate_RejectsLegAttachOutOfRange()
        {
            Make(10, 1);
            sp.legs[0].attachJoint = 10;
            string msg = sp.Validate();
            Assert.IsNotNull(msg);
            StringAssert.Contains("attachJoint", msg);
        }

        [Test] public void Validate_RejectsZeroSpacing()
        {
            Make(10, 0);
            sp.spacing = 0f;
            Assert.IsNotNull(sp.Validate());
        }

        [Test] public void Validate_AcceptsLegless()
        {
            Make(10, 0);
            Assert.IsNull(sp.Validate());
        }

        [Test] public void BoneIndices_Layout()
        {
            Make(10, 4);
            Assert.AreEqual(17, sp.LegBone(2, 1));
            Assert.AreEqual(23, sp.EyeBone(1));
            Assert.AreEqual(24, sp.BoneCount);
        }

        [Test] public void MaxLateralReach_UsesLegs()
        {
            Make(10, 1);
            sp.joints[0].halfWidth = 0.08f;
            sp.legs[0].shoulderOffset = new Vector3(0.05f, 0f, 0f);
            sp.legs[0].upperLength = 0.11f; sp.legs[0].lowerLength = 0.10f;
            Assert.AreEqual(0.26f, sp.MaxLateralReach, 1e-5f);
        }
    }
}
