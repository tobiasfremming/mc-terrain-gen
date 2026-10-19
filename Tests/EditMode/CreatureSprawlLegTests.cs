using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The kit's SprawlLeg part (2026-10-20, the Crocodile's and the KomodoDragon's legs): validation; it walks as a
    // two-bone leg (its LegSpec is a Leg2Bone's); its mesh - every vertex finite with a unit normal, the limb's
    // surface facing away from its bones (folds, muscle and scale relief included), one claw per clawed digit,
    // webbing only where asked - and a RED proof that the outward check bites. The generic kit checks (MaxBend,
    // Gait, Size, ...) run on both species in CreatureKitTests.
    public class CreatureSprawlLegTests
    {
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        static SpeciesCtx Ctx(int joints) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, false);

        static SprawlLeg Ok() => new SprawlLeg
        {
            id = "fore.L", at = new PartSocket(4, -1, new Vector3(0.08f, -0.04f, 0f)), upperLength = 0.1f, lowerLength = 0.08f,
            upperRadius = 0.03f, lowerRadius = 0.022f, footLength = 0.06f, toeCount = 5, pole = new Vector3(0.5f, 0.3f, -0.9f),
        };

        [Test] public void SprawlLeg_Validate_RejectsBadData()
        {
            Assert.IsNull(Ok().Validate(Ctx(20)));
            var bad = (SprawlLeg)Ok().Clone(); bad.lowerLength = 0f;
            Assert.IsNotNull(bad.Validate(Ctx(20)), "no lower bone");
            bad = (SprawlLeg)Ok().Clone(); bad.upperRadius = 0f;
            Assert.IsNotNull(bad.Validate(Ctx(20)), "no radius");
            bad = (SprawlLeg)Ok().Clone(); bad.toeCount = 0;
            Assert.IsNotNull(bad.Validate(Ctx(20)), "no digits");
            bad = (SprawlLeg)Ok().Clone(); bad.digitLengths = new[] { 0.5f, 0.6f };
            Assert.IsNotNull(bad.Validate(Ctx(20)), "digitLengths not one per digit");
            bad = (SprawlLeg)Ok().Clone(); bad.phalanges = new[] { 2, 3, 4, 9, 3 };
            Assert.IsNotNull(bad.Validate(Ctx(20)), "phalanges out of range");
            bad = (SprawlLeg)Ok().Clone(); bad.rootMass = 0.5f;
            Assert.IsNotNull(bad.Validate(Ctx(20)), "a flank mass thinner than the limb");
            bad = (SprawlLeg)Ok().Clone(); bad.role = LimbRole.WingArm;
            Assert.IsNotNull(bad.Validate(Ctx(20)), "a wing's arm is a Leg2Bone");
            Assert.AreEqual(3, Ok().BoneCount(Ctx(20)), "upper, lower, foot");
        }

        // Its runtime leg is a two-bone leg with its own lengths, radii, pole and ankle (CreatureLegs runs it as is).
        [Test] public void SprawlLeg_LegSpec_IsATwoBoneLeg()
        {
            SprawlLeg p = Ok(); p.ankleHeight = 0.02f; p.clawLength = 0.01f; p.toeWeb = 0.4f;
            LegSpec l = p.ToLegSpec();
            Assert.IsFalse(l.IsArthropod);
            Assert.AreEqual(3, l.SegmentCount);
            Assert.AreEqual(0.1f, l.upperLength); Assert.AreEqual(0.08f, l.lowerLength);
            Assert.AreEqual(0.03f, l.upperRadius); Assert.AreEqual(0.022f, l.lowerRadius);
            Assert.AreEqual(p.pole, l.pole); Assert.AreEqual(0.02f, l.AnkleHeight);
            Assert.AreEqual(4, l.attachJoint); Assert.AreEqual(-1, l.side); Assert.AreEqual(5, l.toeCount);
        }

        // Repeat's taper scales every length of a SprawlLeg.
        [Test] public void SprawlLeg_Taper_ScalesLengths()
        {
            SprawlLeg p = Ok(); p.digitRadius = 0.005f; p.clawLength = 0.01f;
            var r = new Repeat { id = "leg", at = new PartSocket(2, 0, Vector3.zero), template = p, count = 2, fromJoint = 2, toJoint = 6, lastScale = 0.5f };
            var c = CompiledSpecies.Compose("t", new CreaturePart[] { new CreatureKit.SpineBody { id = "body", spacing = 0.05f, joints = Joints(10) }, new Eyes { id = "eyes", radius = 0.01f }, r }, new CreatureModule[0]);
            var legs = new List<SprawlLeg>();
            foreach (CreaturePart q in c.Parts) if (q is SprawlLeg s) legs.Add(s);
            Assert.AreEqual(2, legs.Count, string.Join("; ", c.Problems));
            Assert.AreEqual(0.1f, legs[0].upperLength, 1e-6f);
            Assert.AreEqual(0.05f, legs[1].upperLength, 1e-6f);
            Assert.AreEqual(0.0025f, legs[1].digitRadius, 1e-6f);
            Assert.AreEqual(0.005f, legs[1].clawLength, 1e-6f);
        }

        static JointDef[] Joints(int n)
        {
            var j = new JointDef[n];
            for (int i = 0; i < n; i++) j[i] = new JointDef { halfWidth = 0.05f, halfHeight = 0.04f, clearance = 0.08f, bendLimitDeg = 15f };
            return j;
        }

        static readonly string[] kSpecies = { "Crocodile", "KomodoDragon" };

        [TestCaseSource(nameof(kSpecies))] public void SprawlLeg_Species_UseIt(string name)
        {
            CreatureSpecies sp = CreatureKitAssets.Load(name);
            CompiledSpecies c = sp.Compiled;
            Assert.IsNull(c.Error, c.Error);
            Assert.AreEqual(4, c.Limbs.Count);
            foreach (LimbPart l in c.Limbs) Assert.IsTrue(l is SprawlLeg, l.id + " is a SprawlLeg");
        }

        sealed class MeshData
        {
            public Vector3[] v, n; public int[] t;
            public List<Vector2> uv1 = new List<Vector2>(); public List<Vector4> uv2 = new List<Vector4>();
        }

        MeshData Mesh(CreatureSpecies sp)
        {
            Mesh m = CreatureKitAssets.BuildMesh(sp);
            trash.Add(m);
            var d = new MeshData { v = m.vertices, n = m.normals, t = m.triangles };
            m.GetUVs(1, d.uv1); m.GetUVs(2, d.uv2);
            return d;
        }

        // The limb's tubes (part 1, rigid to the upper or lower bone) face away from their bone: the normal points away
        // from the nearest point of the bone's segment. Null = fine, else the first problem.
        static string OutwardProblem(CreatureSpecies sp, MeshData d, CreatureSim rest, float minShare)
        {
            sp = sp.Compiled.Runtime;
            int bad = 0, total = 0;
            for (int l = 0; l < sp.legs.Length; l++)
            {
                LegState st = rest.Legs[l];
                int bu = sp.LegBone(l, 0), bl = sp.LegBone(l, 1);
                for (int i = 0; i < d.v.Length; i++)
                {
                    if (Mathf.RoundToInt(d.uv1[i].x) != PartMeshEmitter.PartLeg) continue;
                    Vector4 s = d.uv2[i];
                    if (s.z < 0.999f) continue;   // the blended root
                    int b = (int)s.x;
                    Vector3 a, e;
                    if (b == bu) { a = st.shoulder; e = st.elbow; } else if (b == bl) { a = st.elbow; e = st.ankle; } else continue;
                    Vector3 ab = e - a;
                    float t = Mathf.Clamp01(Vector3.Dot(d.v[i] - a, ab) / Mathf.Max(1e-12f, ab.sqrMagnitude));
                    Vector3 away = d.v[i] - (a + ab * t);
                    if (away.sqrMagnitude < 1e-10f) continue;
                    total++;
                    if (Vector3.Dot(d.n[i], away) <= 0f) bad++;
                }
            }
            if (total < 1000) return $"{sp.name}: only {total} limb vertices: the check does not bite";
            float share = 1f - (float)bad / total;
            return share >= minShare ? null : $"{sp.name}: {bad} of {total} limb normals face into the limb ({share:P1} outward < {minShare:P0})";
        }

        [TestCaseSource(nameof(kSpecies))] public void SprawlLeg_Mesh_FiniteUnitNormals_FacingOut(string name)
        {
            CreatureSpecies sp = CreatureKitAssets.Load(name);
            MeshData d = Mesh(sp);
            for (int i = 0; i < d.v.Length; i++)
            {
                Vector3 p = d.v[i], n = d.n[i];
                Assert.IsFalse(float.IsNaN(p.x + p.y + p.z) || float.IsInfinity(p.x + p.y + p.z), $"vertex {i} not finite");
                Assert.AreEqual(1f, n.magnitude, 1e-3f, $"vertex {i} normal {n}");
            }
            Assert.IsNull(OutwardProblem(sp, d, CreatureRestPose.Sim(sp.Compiled.Runtime), 0.98f));
        }

        // RED: the same mesh with its normals turned inward fails the outward check.
        [Test] public void SprawlLeg_Red_InwardNormalsAreCaught()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("KomodoDragon");
            MeshData d = Mesh(sp);
            for (int i = 0; i < d.n.Length; i++) d.n[i] = -d.n[i];
            Assert.IsNotNull(OutwardProblem(sp, d, CreatureRestPose.Sim(sp.Compiled.Runtime), 0.98f));
        }

        // One claw per clawed digit on every foot (claw vertices: part 2, rigid to the foot bone): the crocodile's three
        // inner digits, all five of the Komodo's; clawedDigits -1 claws them all.
        [Test] public void SprawlLeg_Claws_OnTheClawedDigits()
        {
            CreatureSpecies croc = CreatureKitAssets.Load("Crocodile");
            int[] perFoot = ClawVertices(croc, Mesh(croc));
            CreatureSpecies komodo = CreatureKitAssets.Load("KomodoDragon");
            int[] komodoFoot = ClawVertices(komodo, Mesh(komodo));
            int one = komodoFoot[0] / 5;
            Assert.Greater(one, 0);
            foreach (int c in komodoFoot) Assert.AreEqual(5 * one, c, "Komodo: five claws a foot");
            foreach (int c in perFoot) Assert.AreEqual(3 * one, c, "Crocodile: three claws a foot (the inner digits)");

            // all digits clawed: the crocodile's fore feet get five, the hind four (edit the asset's parts, recompile)
            CreatureSpecies asset = CreatureKitAssets.LoadAsset("Crocodile");
            var parts = new List<(SprawlLeg, int)>();
            foreach (CreaturePart p in asset.parts) if (p is SprawlLeg s) { parts.Add((s, s.clawedDigits)); s.clawedDigits = -1; }
            try
            {
                asset.InvalidateCompiled();
                int[] all = ClawVertices(asset, Mesh(asset));
                CreatureSpecies rt = asset.Compiled.Runtime;
                for (int l = 0; l < rt.legs.Length; l++) Assert.AreEqual(rt.legs[l].toeCount * one, all[l], $"leg {l}: a claw on every digit");
            }
            finally
            {
                foreach ((SprawlLeg s, int c) in parts) s.clawedDigits = c;
                asset.InvalidateCompiled();
            }
        }

        static int[] ClawVertices(CreatureSpecies sp, MeshData d)
        {
            CreatureSpecies rt = sp.Compiled.Runtime;
            var count = new int[rt.legs.Length];
            for (int i = 0; i < d.v.Length; i++)
            {
                if (Mathf.RoundToInt(d.uv1[i].x) != PartMeshEmitter.PartFoot) continue;
                int b = (int)d.uv2[i].x;
                for (int l = 0; l < rt.legs.Length; l++) if (b == rt.LegBone(l, 2)) count[l]++;
            }
            return count;
        }

        // Webbing: the crocodile's hind feet carry a membrane between the toes (flat sheets: normals along the foot's
        // up), the fore feet none.
        [Test] public void SprawlLeg_Web_OnlyOnTheWebbedFeet()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("Crocodile");
            MeshData d = Mesh(sp);
            CreatureSpecies rt = sp.Compiled.Runtime;
            Matrix4x4[] bones = CreatureRestPose.BoneWorld(rt);
            for (int l = 0; l < rt.legs.Length; l++)
            {
                int bf = rt.LegBone(l, 2), sheet = 0;
                Vector3 up = bones[bf].rotation * Vector3.up;
                for (int i = 0; i < d.v.Length; i++)
                    if ((int)d.uv2[i].x == bf && Mathf.RoundToInt(d.uv1[i].x) == PartMeshEmitter.PartLeg && Mathf.Abs(Vector3.Dot(d.n[i], up)) > 0.99999f
                        && Vector3.Dot(d.v[i] - (Vector3)bones[bf].GetColumn(3), up) > 0.002f) sheet++;
                bool hind = rt.legs[l].pole.z > 0f;
                if (hind) Assert.GreaterOrEqual(sheet, (rt.legs[l].toeCount - 1) * 2 * 25, $"leg {l}: webbed hind foot");
                else Assert.Less(sheet, 25, $"leg {l}: no web on a fore foot");
            }
        }
    }
}
