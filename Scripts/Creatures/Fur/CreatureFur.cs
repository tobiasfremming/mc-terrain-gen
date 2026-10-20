// Creature fur, GPU side (Unity only; the harness compiles its CPU reference FurBuilder / FurSim instead).
// One per furry species, attached to its CreatureRenderer by CreatureWorld and drawn right after the body with the
// renderer's own buffers (_Bones, _Instances) - so the hair moves with exactly the matrices the body is skinned with.
//   once      FurBuilder.Build on the renderer's mesh: skin vertices, roots, regions -> structured buffers;
//             the undercoat's shell mesh; materials from Resources/CreatureFur (the shaders and the compute)
//   per frame FurSlots picks the nearest maxSimulated individuals (stable identity, reset on a new one), the slot
//             table is uploaded, FurSim.compute steps every slot's strands (one thread per strand), then:
//               shells   one RenderMeshPrimitives: shell mesh x (count x shells) instances
//               strands  one RenderPrimitives: slots x roots x 24 vertices (unused ones are culled in the shader)
// Zero GC per frame: every array, buffer and property block is made here once. No indirect arguments (the counts
// are known on the CPU), so the D3D11 indirect-args UAV gotcha of the grass does not arise.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CreatureKit
{
    public sealed class CreatureFur : ICreatureFurDraw
    {
        public const string ResourceFolder = "CreatureFur/";
        const int kStrandVerts = 24;   // CreatureFurStrands.shader FUR_VERTS
        const int kStrandStride = 96, kRootStride = 64, kVertStride = 64, kRegionStride = 48;

        static readonly int
            kBones = Shader.PropertyToID("_Bones"), kInstances = Shader.PropertyToID("_Instances"),
            kBindPoses = Shader.PropertyToID("_BindPoses"), kBonesPerCreature = Shader.PropertyToID("_BonesPerCreature"),
            kDorsal = Shader.PropertyToID("_Dorsal"), kFlank = Shader.PropertyToID("_Flank"), kBelly = Shader.PropertyToID("_Belly"),
            kPatternColor = Shader.PropertyToID("_PatternColor"), kPatternType = Shader.PropertyToID("_PatternType"),
            kPatternParams = Shader.PropertyToID("_PatternParams"), kBodyParams = Shader.PropertyToID("_BodyParams"),
            kFurLook = Shader.PropertyToID("_FurLook"),
            kFurVerts = Shader.PropertyToID("_FurVerts"), kFurRoots = Shader.PropertyToID("_FurRoots"),
            kFurRegions = Shader.PropertyToID("_FurRegions"), kFurSlots = Shader.PropertyToID("_FurSlots"),
            kFurStrands = Shader.PropertyToID("_FurStrands"), kFurRootCount = Shader.PropertyToID("_FurRootCount"),
            kFurSlotCount = Shader.PropertyToID("_FurSlotCount"), kFurDt = Shader.PropertyToID("_FurDt"),
            kFurFade = Shader.PropertyToID("_FurFade"), kFurCreatureCount = Shader.PropertyToID("_FurCreatureCount"),
            kFurShells = Shader.PropertyToID("_FurShells"), kFurShellFade = Shader.PropertyToID("_FurShellFade");

        readonly CreatureSpecies sp;
        readonly Fur fur;
        readonly FurData data;
        readonly ComputeShader cs;
        readonly int kernel;
        readonly Material shellMat, strandMat;
        readonly Mesh shellMesh;
        readonly GraphicsBuffer vertBuf, rootBuf, regionBuf, slotBuf, strandBuf, bindBuf;
        readonly FurSlots slots;
        readonly uint[] slotData;
        readonly float[] reach;
        readonly MaterialPropertyBlock shellProps = new MaterialPropertyBlock(), strandProps = new MaterialPropertyBlock();
        readonly int bonesPer, roots;
        readonly float boundsPad;
        readonly Vector4 bodyParams;
        RenderParams shellRp, strandRp;

        public FurData Data => data;
        public int SimulatedSlots { get; private set; }

        // null when the species has no fur, the shaders are missing, or there is no compute support.
        public static CreatureFur Create(CreatureSpecies sp, CreatureRenderer renderer)
        {
            if (renderer == null || renderer.Mesh == null) return null;
            Fur f = FurBuilder.ModuleOf(sp);
            if (f == null || !SystemInfo.supportsComputeShaders || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            var c = Resources.Load<ComputeShader>(ResourceFolder + "FurSim");
            Shader shell = Shader.Find("MarchingCubes/CreatureFurShell"), strands = Shader.Find("MarchingCubes/CreatureFurStrands");
            if (c == null || shell == null || strands == null)
            {
                Debug.LogWarning($"CreatureFur: {sp.name} has fur but Resources/CreatureFur is incomplete (compute {c != null}, shell {shell != null}, strands {strands != null}); drawn bare");
                return null;
            }
            return new CreatureFur(sp, f, renderer, c, shell, strands);
        }

        CreatureFur(CreatureSpecies species, Fur f, CreatureRenderer renderer, ComputeShader c, Shader shell, Shader strands)
        {
            sp = species.Compiled.Runtime; fur = f; cs = c;
            kernel = cs.FindKernel("FurStep");
            bonesPer = renderer.BonesPerCreature;
            data = FurBuilder.Build(sp, renderer.Mesh, renderer.BindPoses, QualitySettings.activeColorSpace == ColorSpace.Linear);
            roots = data.StrandCount;
            bodyParams = renderer.BodyParams;   // _BodyParams: the pattern's head fade and tail strength

            vertBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, data.Verts.Length), kVertStride);
            if (data.Verts.Length > 0) vertBuf.SetData(data.Verts);
            rootBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, roots), kRootStride);
            if (roots > 0) rootBuf.SetData(data.Roots);
            regionBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, data.Regions.Length), kRegionStride);
            regionBuf.SetData(data.Regions);
            bindBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, renderer.BindPoses.Length, 64);
            bindBuf.SetData(renderer.BindPoses);

            int nSlots = roots > 0 ? Mathf.Max(0, fur.maxSimulated) : 0;
            slots = new FurSlots(nSlots);
            slotData = new uint[Mathf.Max(1, nSlots) * 4];
            reach = new float[256];
            slotBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, nSlots), 16);
            slotBuf.SetData(slotData);
            strandBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, nSlots * roots), kStrandStride);
            strandBuf.SetData(new byte[Mathf.Max(1, nSlots * roots) * kStrandStride]);   // v2.w = 0: every strand starts with a reset

            if (data.HasShells)
            {
                shellMesh = data.BuildShellMesh(sp.name + " fur shells");
                shellMat = new Material(shell) { name = sp.name + " fur shells (runtime)" };
            }
            strandMat = new Material(strands) { name = sp.name + " fur strands (runtime)" };
            boundsPad = data.MaxLength * 2f;

            shellRp = new RenderParams(shellMat) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, matProps = shellProps };
            strandRp = new RenderParams(strandMat) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true, matProps = strandProps };
        }

        public void Draw(GraphicsBuffer bones, GraphicsBuffer instances, int count, IReadOnlyList<ICreatureBody> bodies, Vector3[] centres, CreatureInstance[] inst)
        {
            if (count <= 0 || bones == null || instances == null) return;
            Camera cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : centres[0];

            // bounds of every individual (shells) and of the simulated ones (strands)
            Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
            for (int i = 0; i < count; i++)
            {
                float s = inst[i].sizeScale;
                if (i < reach.Length) reach[i] = fur.strandFadeEnd * s;
                Vector3 c = centres[i];
                float r = (EnclosureRadius() + boundsPad) * s;
                lo = Vector3.Min(lo, c - new Vector3(r, r, r)); hi = Vector3.Max(hi, c + new Vector3(r, r, r));
            }
            var bounds = new Bounds(); bounds.SetMinMax(lo, hi);
            SetLook(shellProps); SetLook(strandProps);

            // ---- strands: pick, simulate, draw
            int n = 0;
            if (slots.Count > 0)
            {
                slots.Assign(eye, bodies, centres, reach, Mathf.Min(count, reach.Length));
                Vector3 slo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), shi = -slo;
                for (int k = 0; k < slots.Count; k++)
                {
                    int idx = slots.Index[k];
                    bool on = idx >= 0;
                    slotData[4 * k] = on ? (uint)idx : 0u;
                    slotData[4 * k + 1] = slots.Reset[k] ? 1u : 0u;
                    slotData[4 * k + 2] = on ? 1u : 0u;
                    slotData[4 * k + 3] = 0u;
                    if (!on) continue;
                    n++;
                    float r = (EnclosureRadius() + boundsPad) * inst[idx].sizeScale;
                    slo = Vector3.Min(slo, centres[idx] - new Vector3(r, r, r)); shi = Vector3.Max(shi, centres[idx] + new Vector3(r, r, r));
                }
                SimulatedSlots = n;
                if (n > 0)
                {
                    slotBuf.SetData(slotData);
                    cs.SetBuffer(kernel, kBones, bones);
                    cs.SetBuffer(kernel, kInstances, instances);
                    cs.SetBuffer(kernel, kBindPoses, bindBuf);
                    cs.SetInt(kBonesPerCreature, bonesPer);
                    cs.SetBuffer(kernel, kFurVerts, vertBuf);
                    cs.SetBuffer(kernel, kFurRoots, rootBuf);
                    cs.SetBuffer(kernel, kFurRegions, regionBuf);
                    cs.SetBuffer(kernel, kFurSlots, slotBuf);
                    cs.SetBuffer(kernel, kFurStrands, strandBuf);
                    cs.SetInt(kFurRootCount, roots);
                    cs.SetInt(kFurSlotCount, slots.Count);
                    cs.SetFloat(kFurDt, Mathf.Clamp(Time.deltaTime, 0f, 0.1f));
                    SetLook(cs);
                    cs.Dispatch(kernel, (slots.Count * roots + 63) / 64, 1, 1);

                    strandProps.SetBuffer(kFurStrands, strandBuf);
                    strandProps.SetBuffer(kFurSlots, slotBuf);
                    strandProps.SetBuffer(kInstances, instances);
                    strandProps.SetInt(kFurRootCount, roots);
                    strandProps.SetInt(kFurSlotCount, slots.Count);
                    strandProps.SetVector(kFurFade, new Vector4(fur.strandFadeStart, fur.strandFadeEnd, 0f, 0f));
                    var sb = new Bounds(); sb.SetMinMax(slo, shi);
                    strandRp.worldBounds = sb;
                    strandRp.matProps = strandProps;
                    Graphics.RenderPrimitives(strandRp, MeshTopology.Triangles, slots.Count * roots * kStrandVerts, 1);
                }
            }

            // ---- shells
            if (shellMesh != null && fur.shells > 0)
            {
                shellProps.SetBuffer(kBones, bones);
                shellProps.SetBuffer(kInstances, instances);
                shellProps.SetBuffer(kBindPoses, bindBuf);
                shellProps.SetInt(kBonesPerCreature, bonesPer);
                shellProps.SetInt(kFurCreatureCount, count);
                shellProps.SetInt(kFurShells, fur.shells);
                shellProps.SetVector(kFurShellFade, new Vector4(fur.shellFadeEnd, 0f, 0f, 0f));
                shellRp.worldBounds = bounds;
                shellRp.matProps = shellProps;
                Graphics.RenderMeshPrimitives(shellRp, shellMesh, 0, count * fur.shells);
            }
        }

        // the reach of the body past its centre bone (half the body plus the legs), metres at scale 1
        float EnclosureRadius() => 0.5f * sp.BodyLength + sp.MaxLateralReach;

        void SetLook(MaterialPropertyBlock p)
        {
            CreatureLook look = sp.look;
            p.SetColor(kDorsal, look.dorsal); p.SetColor(kFlank, look.flank); p.SetColor(kBelly, look.belly);
            p.SetColor(kPatternColor, look.pattern);
            p.SetFloat(kPatternType, (float)look.patternType);
            p.SetVector(kPatternParams, new Vector4(look.patternScale, look.patternContrast, look.patternSoftness, look.spotScale));
            p.SetVector(kBodyParams, bodyParams);
            p.SetVector(kFurLook, new Vector4(fur.pattern, fur.rootOcclusion, fur.highlight, fur.translucency));
        }

        // the compute bakes the strands' colours on a reset: the same palette, decoded like SetColor does
        void SetLook(ComputeShader c)
        {
            CreatureLook look = sp.look;
            bool lin = QualitySettings.activeColorSpace == ColorSpace.Linear;
            c.SetVector(kDorsal, Lin(look.dorsal, lin)); c.SetVector(kFlank, Lin(look.flank, lin)); c.SetVector(kBelly, Lin(look.belly, lin));
            c.SetVector(kPatternColor, Lin(look.pattern, lin));
            c.SetFloat(kPatternType, (float)look.patternType);
            c.SetVector(kPatternParams, new Vector4(look.patternScale, look.patternContrast, look.patternSoftness, look.spotScale));
            c.SetVector(kBodyParams, bodyParams);
            c.SetVector(kFurLook, new Vector4(fur.pattern, fur.rootOcclusion, fur.highlight, fur.translucency));
        }

        static Vector4 Lin(Color c, bool lin) { Color l = lin ? c.linear : c; return new Vector4(l.r, l.g, l.b, l.a); }

        public void Dispose()
        {
            vertBuf?.Dispose(); rootBuf?.Dispose(); regionBuf?.Dispose(); slotBuf?.Dispose(); strandBuf?.Dispose(); bindBuf?.Dispose();
            DestroyObj(shellMesh); DestroyObj(shellMat); DestroyObj(strandMat);
        }

        static void DestroyObj(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
