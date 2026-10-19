using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The kit's SpiderFace part (2026-10-20, the GolfballStrider's face): validation (incl. `on` naming a DimpledOrb),
    // and its mesh on the species - cap, eyes and fangs rigid to the bone it rides, every eye on the face's front half,
    // the fangs hanging down below the face, unit normals - with a RED proof that the "fangs hang down" check bites.
    public class CreatureSpiderFaceTests
    {
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        const string kSpecies = CreatureBendyLegTests.kSpecies;

        static SpiderFace Ok() => new SpiderFace { id = "face", at = new PartSocket(0, 0, new Vector3(0f, 0.02f, 0.04f)) };

        [Test] public void SpiderFace_Validate_RejectsBadData()
        {
            var orb = new DimpledOrb { id = "orb", at = new PartSocket(1, 0, Vector3.zero) };
            var ctx = new SpeciesCtx(new CreaturePart[] { orb }, new CreatureModule[0], 3, false);
            Assert.IsNull(Ok().Validate(ctx));
            SpiderFace bad;
            bad = (SpiderFace)Ok().Clone(); bad.on = "orb"; Assert.IsNull(bad.Validate(ctx), "on an orb");
            bad = (SpiderFace)Ok().Clone(); bad.on = "nothing"; Assert.IsNotNull(bad.Validate(ctx), "on a missing part");
            bad = (SpiderFace)Ok().Clone(); bad.width = 0f; Assert.IsNotNull(bad.Validate(ctx), "no width");
            bad = (SpiderFace)Ok().Clone(); bad.eyeCount = 9; Assert.IsNotNull(bad.Validate(ctx), "too many eyes");
            bad = (SpiderFace)Ok().Clone(); bad.fangRadius = 0f; Assert.IsNotNull(bad.Validate(ctx), "fang without radius");
            bad = (SpiderFace)Ok().Clone(); bad.at.joint = 7; Assert.IsNotNull(bad.Validate(ctx), "joint out of range");
            var refs = new List<string>(); bad = (SpiderFace)Ok().Clone(); bad.on = "orb"; bad.CollectReferences(refs);
            Assert.AreEqual(1, refs.Count); Assert.AreEqual("orb", refs[0]);
        }

        sealed class Face { public List<Vector3> cap = new List<Vector3>(), eye = new List<Vector3>(), fang = new List<Vector3>(), n = new List<Vector3>(); }

        static void Find(CreatureSpecies rt, out SpiderFace face, out int bone)
        {
            IReadOnlyList<CreaturePart> parts = KitView.Parts(rt);
            face = null; bone = -1;
            foreach (CreaturePart p in parts) if (p is SpiderFace f) face = f;
            Assert.IsNotNull(face, "the strider has a SpiderFace");
            bone = face.at.joint;
            for (int i = 0; i < parts.Count; i++) if (parts[i].id == face.on) bone = rt.Layout.Start(i);
        }

        // The face's vertices: rigid to its bone, cap (its layer), eyes (3), fangs (2), split by part id.
        Face FaceMesh(CreatureSpecies sp, out SpiderFace face, out Vector3 centre, out Vector3 look)
        {
            CreatureSpecies rt = sp.Compiled.Runtime;
            Find(rt, out face, out int bone);
            CreatureSim rest = CreatureRestPose.Sim(rt);
            centre = rest.Joints[face.at.joint] + rest.Rotations[face.at.joint] * face.at.offset;
            look = rest.Rotations[face.at.joint] * face.LocalFrame() * Vector3.forward;
            Mesh m = CreatureKitAssets.BuildMesh(sp);
            trash.Add(m);
            var v = m.vertices; var nn = m.normals;
            var uv1 = new List<Vector2>(); var uv2 = new List<Vector4>();
            m.GetUVs(1, uv1); m.GetUVs(2, uv2);
            var r = new Face();
            for (int i = 0; i < v.Length; i++)
            {
                if ((int)uv2[i].x != bone || uv2[i].z < 0.999f) continue;
                if ((v[i] - centre).magnitude > 0.5f) continue;   // the orb's far side shares the bone: keep the face's region
                int part = Mathf.RoundToInt(uv1[i].x);
                if (part == face.layer && (v[i] - centre).magnitude <= Mathf.Max(face.width, face.depth) * 1.01f) r.cap.Add(v[i]);
                else if (part == PartMeshEmitter.PartEye) r.eye.Add(v[i]);
                else if (part == PartMeshEmitter.PartFoot) r.fang.Add(v[i]);
                else continue;
                r.n.Add(nn[i]);
            }
            return r;
        }

        // Null when both fangs hang down: their lowest point at least 0.6 x fangLength below the face centre.
        static string FangProblem(Face f, SpiderFace face, Vector3 centre, Vector3 up)
        {
            if (f.fang.Count == 0) return "no fang vertices";
            float low = float.MaxValue;
            foreach (Vector3 p in f.fang) low = Mathf.Min(low, Vector3.Dot(p - centre, up));
            return low < -0.6f * face.fangLength ? null : $"the fangs reach only {low:F4} m below the face (want < {-0.6f * face.fangLength:F4})";
        }

        [Test] public void SpiderFace_Mesh_CapEyesFangs()
        {
            Face f = FaceMesh(CreatureKitAssets.Load(kSpecies), out SpiderFace face, out Vector3 centre, out Vector3 look);
            Assert.Greater(f.cap.Count, 50, "cap");
            Assert.Greater(f.eye.Count, 30 * face.eyeCount, "eyes");
            foreach (Vector3 p in f.eye) Assert.Greater(Vector3.Dot(p - centre, look), 0f, "every eye on the face's front half");
            foreach (Vector3 n in f.n) Assert.AreEqual(1f, n.magnitude, 1e-3f);
            Assert.IsNull(FangProblem(f, face, centre, Vector3.up));
        }

        // RED: fangs that point up fail the hanging check (judged against an upside-down up).
        [Test] public void SpiderFace_Red_FangsUpAreCaught()
        {
            Face f = FaceMesh(CreatureKitAssets.Load(kSpecies), out SpiderFace face, out Vector3 centre, out _);
            Assert.IsNotNull(FangProblem(f, face, centre, Vector3.down));
        }
    }
}
