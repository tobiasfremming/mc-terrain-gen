using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The kit's DigLeg part (2026-10-20, the Delver's limbs): validation; it walks as a two-bone leg (its LegSpec is a
    // Leg2Bone's); its mesh - finite unit normals, the limb tubes facing away from their bones, one big claw per digit
    // rigid to the hand bone, the claws' tips on the ground (not under it) - and a RED proof that the outward check bites.
    public class CreatureDigLegTests
    {
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        static SpeciesCtx Ctx(int joints) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, false);

        static DigLeg Ok() => new DigLeg
        {
            id = "fore.L", at = new PartSocket(4, -1, new Vector3(0.1f, -0.05f, 0f)), upperLength = 0.12f, lowerLength = 0.11f,
            upperRadius = 0.05f, lowerRadius = 0.045f, footLength = 0.09f, toeCount = 3, pole = new Vector3(0.6f, 0.3f, -0.8f),
            clawLength = 0.1f,
        };

        [Test] public void DigLeg_Validate_RejectsBadData()
        {
            Assert.IsNull(Ok().Validate(Ctx(10)));
            var bad = (DigLeg)Ok().Clone(); bad.lowerLength = 0f;
            Assert.IsNotNull(bad.Validate(Ctx(10)));
            bad = (DigLeg)Ok().Clone(); bad.upperRadius = 0f;
            Assert.IsNotNull(bad.Validate(Ctx(10)));
            bad = (DigLeg)Ok().Clone(); bad.toeCount = 0;
            Assert.IsNotNull(bad.Validate(Ctx(10)));
            bad = (DigLeg)Ok().Clone(); bad.toeCount = 7;
            Assert.IsNotNull(bad.Validate(Ctx(10)));
            bad = (DigLeg)Ok().Clone(); bad.clawLength = -0.01f;
            Assert.IsNotNull(bad.Validate(Ctx(10)));
            bad = (DigLeg)Ok().Clone(); bad.role = LimbRole.WingArm;
            Assert.IsNotNull(bad.Validate(Ctx(10)));
            bad = (DigLeg)Ok().Clone(); bad.handWidth = 0f;
            Assert.IsNotNull(bad.Validate(Ctx(10)));
        }

        [Test] public void DigLeg_LegSpec_IsATwoBoneLeg()
        {
            DigLeg p = Ok(); p.ankleHeight = 0.03f;
            LegSpec l = p.ToLegSpec();
            Assert.IsFalse(l.IsArthropod);
            Assert.AreEqual(0.12f, l.upperLength); Assert.AreEqual(0.11f, l.lowerLength);
            Assert.AreEqual(0.05f, l.upperRadius); Assert.AreEqual(0.045f, l.lowerRadius);
            Assert.AreEqual(0.03f, l.ankleHeight); Assert.AreEqual(4, l.attachJoint); Assert.AreEqual(-1, l.side);
            Assert.AreEqual(0f, l.kneeBlend);
        }

        struct MeshData
        {
            public Vector3[] v, n; public int[] t;
            public List<Vector2> uv1; public List<Vector4> uv2;
        }

        MeshData Mesh(CreatureSpecies sp)
        {
            Mesh m = CreatureKitAssets.BuildMesh(sp);
            trash.Add(m);
            var d = new MeshData { v = m.vertices, n = m.normals, t = m.triangles, uv1 = new List<Vector2>(), uv2 = new List<Vector4>() };
            m.GetUVs(1, d.uv1); m.GetUVs(2, d.uv2);
            return d;
        }

        // The limb's tubes (part 1, rigid to the upper or lower bone) face away from their bone segment.
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
                    if (s.z < 0.999f) continue;
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

        [Test] public void DigLeg_Delver_UsesIt()
        {
            CompiledSpecies c = CreatureKitAssets.Load("Delver").Compiled;
            Assert.AreEqual(4, c.Limbs.Count);
            foreach (LimbPart l in c.Limbs) Assert.IsTrue(l is DigLeg, l.id + " is a DigLeg");
        }

        [Test] public void DigLeg_Mesh_FiniteUnitNormals_FacingOut()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("Delver");
            MeshData d = Mesh(sp);
            for (int i = 0; i < d.v.Length; i++)
            {
                Vector3 p = d.v[i], n = d.n[i];
                Assert.IsFalse(float.IsNaN(p.x + p.y + p.z) || float.IsInfinity(p.x + p.y + p.z), $"vertex {i} not finite");
                Assert.AreEqual(1f, n.magnitude, 1e-3f, $"vertex {i} normal {n}");
            }
            Assert.IsNull(OutwardProblem(sp, d, CreatureRestPose.Sim(sp.Compiled.Runtime), 0.98f));
        }

        [Test] public void DigLeg_Red_InwardNormalsAreCaught()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("Delver");
            MeshData d = Mesh(sp);
            for (int i = 0; i < d.n.Length; i++) d.n[i] = -d.n[i];
            Assert.IsNotNull(OutwardProblem(sp, d, CreatureRestPose.Sim(sp.Compiled.Runtime), 0.98f));
        }

        // The claws (part 11, rigid to each hand bone): toeCount separate claws per limb, their lowest point at the ground
        // (within 15 % of the claw length above it, never more than 2 % below), each at least clawLength long.
        [Test] public void DigLeg_Claws_OnePerDigit_TipsOnTheGround()
        {
            CreatureSpecies asset = CreatureKitAssets.Load("Delver");
            CreatureSpecies sp = asset.Compiled.Runtime;
            MeshData d = Mesh(asset);
            CreatureSim rest = CreatureRestPose.Sim(sp);
            var limbs = asset.Compiled.Limbs;
            for (int l = 0; l < sp.legs.Length; l++)
            {
                var dl = (DigLeg)limbs[l];
                int bf = sp.LegBone(l, 2);
                float ground = rest.Legs[l].foot.y;
                var pts = new List<Vector3>();
                var mine = new bool[d.v.Length];
                for (int i = 0; i < d.v.Length; i++)
                    if (Mathf.RoundToInt(d.uv1[i].x) == PartMeshEmitter.PartPincer && (int)d.uv2[i].x == bf) { pts.Add(d.v[i]); mine[i] = true; }
                Assert.Greater(pts.Count, 0, dl.id + ": no claw vertices");
                // claws are separate solids: count the connected pieces of their triangles
                int claws = Pieces(d.t, mine, d.v);
                Assert.AreEqual(dl.toeCount, claws, dl.id + ": claws");
                float lo = float.MaxValue;
                foreach (Vector3 p in pts) lo = Mathf.Min(lo, p.y - ground);
                Assert.That(lo, Is.InRange(-0.02f * dl.clawLength, 0.15f * dl.clawLength), dl.id + ": claw tips' height above the ground");
            }
        }

        // Connected pieces of the triangles whose vertices are all marked (union-find over triangle edges).
        // Vertices at the same position (a loft's seam and pole copies) are one.
        internal static int Pieces(int[] t, bool[] mark, Vector3[] v)
        {
            int n = mark.Length;
            var id = new int[n];
            for (int i = 0; i < n; i++) id[i] = i;
            int Find(int x) { while (id[x] != x) x = id[x] = id[id[x]]; return x; }
            var at = new Dictionary<(int, int, int), int>();
            for (int i = 0; i < n; i++)
            {
                if (!mark[i]) continue;
                var key = (Mathf.RoundToInt(v[i].x * 1e5f), Mathf.RoundToInt(v[i].y * 1e5f), Mathf.RoundToInt(v[i].z * 1e5f));
                if (at.TryGetValue(key, out int j)) id[Find(i)] = Find(j); else at[key] = i;
            }
            for (int k = 0; k < t.Length; k += 3)
            {
                if (!mark[t[k]] || !mark[t[k + 1]] || !mark[t[k + 2]]) continue;
                id[Find(t[k])] = Find(t[k + 1]); id[Find(t[k + 1])] = Find(t[k + 2]);
            }
            var roots = new HashSet<int>();
            for (int i = 0; i < n; i++) if (mark[i]) roots.Add(Find(i));
            return roots.Count;
        }
    }
}
