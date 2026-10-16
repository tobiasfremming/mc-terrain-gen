using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Per-individual shading data, read by the shader as _Instances[SV_InstanceID].
//   seed       pattern offset (any float; hashed bitwise in the shader)
//   hueShift   hue rotation in turns (0.03 = 10.8 degrees)
//   valueShift brightness offset: albedo *= 1 + valueShift
//   sizeScale  the sim's size scale (the bone matrices already carry it)
// HLSL (Shaders/Creature/CreatureSkin.hlsl): struct CreatureInstance { float seed, hueShift, valueShift, sizeScale; }
[StructLayout(LayoutKind.Sequential)]
public struct CreatureInstance { public float seed, hueShift, valueShift, sizeScale; }

// One instanced, GPU-skinned draw per species. Each frame: bone worlds x bind poses -> skin matrices
// (Count x BoneCount float4x4), uploaded to _Bones; the shader blends them by uv2 and indexes by
// SV_InstanceID. Arrays and buffers grow by doubling on Add; Render never allocates.
public sealed class CreatureRenderer : IDisposable
{
    const int kInitialCapacity = 4;
    const int kMatrixStride = 64;
    const float kBoundsMargin = 0.05f;

    static readonly int
        kBones = Shader.PropertyToID("_Bones"), kInstances = Shader.PropertyToID("_Instances"),
        kBindPoses = Shader.PropertyToID("_BindPoses"), kBonesPerCreature = Shader.PropertyToID("_BonesPerCreature"),
        kDorsal = Shader.PropertyToID("_Dorsal"), kFlank = Shader.PropertyToID("_Flank"),
        kBelly = Shader.PropertyToID("_Belly"), kPatternColor = Shader.PropertyToID("_PatternColor"),
        kPatternType = Shader.PropertyToID("_PatternType"), kPatternParams = Shader.PropertyToID("_PatternParams"),
        kSurfaceParams = Shader.PropertyToID("_SurfaceParams"), kBodyParams = Shader.PropertyToID("_BodyParams");

    readonly CreatureSpecies sp;
    readonly Material material;
    readonly Matrix4x4[] bindPoses;
    readonly Matrix4x4[] boneScratch;
    readonly int bonesPer;
    readonly float reach;
    readonly Vector4 bodyParams;
    readonly List<CreatureSim> sims = new List<CreatureSim>();
    readonly MaterialPropertyBlock props = new MaterialPropertyBlock();
    readonly bool gpu;

    Matrix4x4[] _skin;
    CreatureInstance[] instances;
    int capacity;
    bool instancesDirty;
    GraphicsBuffer bonesBuf, instBuf, bindBuf;
    RenderParams rp;

    public Mesh Mesh { get; private set; }
    public int Count => sims.Count;

    internal Matrix4x4[] SkinForTest => _skin;

    public CreatureRenderer(CreatureSpecies sp, Material material)
    {
        this.sp = sp; this.material = material;
        bonesPer = sp.BoneCount;
        bindPoses = CreatureRestPose.BindPoses(sp);
        boneScratch = new Matrix4x4[bonesPer];
        Mesh = CreatureMeshBuilder.Build(sp);

        // Everything that sticks out past the spine joints: legs and feet sideways and down, the head
        // cap and tail tip along the body, the eyes.
        float halfHeight = 0f;
        for (int i = 0; i < sp.JointCount; i++) halfHeight = Mathf.Max(halfHeight, sp.joints[i].halfHeight);
        reach = Mathf.Max(sp.MaxLateralReach, Mathf.Max(sp.headCapLength, sp.tailTipLength), halfHeight) + sp.eyeRadius + kBoundsMargin;

        // _BodyParams = (body length, 1 / length, arc length where the tail begins (behind the hind legs), 0)
        float len = Mathf.Max(sp.BodyLength, 1e-3f);
        int hip = -1;
        for (int l = 0; l < (sp.legs != null ? sp.legs.Length : 0); l++) hip = Mathf.Max(hip, sp.legs[l].attachJoint);
        float tailStart = hip >= 0 ? sp.headCapLength + sp.spacing * hip : 0.5f * len;
        bodyParams = new Vector4(len, 1f / len, tailStart, 0f);

        // Without a graphics device (batch -nographics) only the CPU side runs.
        gpu = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        if (gpu)
        {
            bindBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bonesPer, kMatrixStride);
            bindBuf.SetData(bindPoses);
        }
        Grow(kInitialCapacity);

        rp = new RenderParams(material)
        {
            shadowCastingMode = ShadowCastingMode.On,
            receiveShadows = true,
            matProps = props,
        };
    }

    public void Add(CreatureSim sim, CreatureInstance inst)
    {
        if (sim == null) throw new ArgumentNullException(nameof(sim));
        if (sim.Species != sp) throw new ArgumentException("sim belongs to another species", nameof(sim));
        if (sims.Count == capacity) Grow(capacity * 2);
        instances[sims.Count] = inst;
        sims.Add(sim);
        instancesDirty = true;
    }

    // Swap-removes the sim (the last one takes its slot). False if it was not drawn by this renderer.
    public bool Remove(CreatureSim sim)
    {
        int i = sims.IndexOf(sim);
        if (i < 0) return false;
        int last = sims.Count - 1;
        sims[i] = sims[last];
        instances[i] = instances[last];
        sims.RemoveAt(last);
        instancesDirty = true;
        return true;
    }

    void Grow(int newCapacity)
    {
        var skin = new Matrix4x4[newCapacity * bonesPer];
        var inst = new CreatureInstance[newCapacity];
        if (_skin != null) Array.Copy(_skin, skin, sims.Count * bonesPer);
        if (instances != null) Array.Copy(instances, inst, sims.Count);
        _skin = skin; instances = inst; capacity = newCapacity;
        if (gpu)
        {
            bonesBuf?.Dispose(); instBuf?.Dispose();
            bonesBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, newCapacity * bonesPer, kMatrixStride);
            instBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, newCapacity, Marshal.SizeOf<CreatureInstance>());
        }
        instancesDirty = true;
    }

    // Call once per frame from LateUpdate.
    public void Render()
    {
        int n = sims.Count;
        if (n == 0 || Mesh == null) return;

        Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
        float maxScale = 0f;
        for (int s = 0; s < n; s++)
        {
            CreatureSim sim = sims[s];
            sim.WriteBoneWorld(boneScratch, 0);
            int o = s * bonesPer;
            for (int b = 0; b < bonesPer; b++) _skin[o + b] = boneScratch[b] * bindPoses[b];
            Vector3[] j = sim.Joints;
            for (int k = 0; k < j.Length; k++) { lo = Vector3.Min(lo, j[k]); hi = Vector3.Max(hi, j[k]); }
            maxScale = Mathf.Max(maxScale, sim.SizeScale);
        }

        if (!gpu || material == null) return;

        bonesBuf.SetData(_skin, 0, 0, n * bonesPer);
        if (instancesDirty) { instBuf.SetData(instances, 0, 0, n); instancesDirty = false; }

        CreatureLook look = sp.look;
        props.SetBuffer(kBones, bonesBuf);
        props.SetBuffer(kInstances, instBuf);
        props.SetBuffer(kBindPoses, bindBuf);
        props.SetInt(kBonesPerCreature, bonesPer);
        props.SetColor(kDorsal, look.dorsal);
        props.SetColor(kFlank, look.flank);
        props.SetColor(kBelly, look.belly);
        props.SetColor(kPatternColor, look.pattern);
        props.SetFloat(kPatternType, (float)look.patternType);
        props.SetVector(kPatternParams, new Vector4(look.patternScale, look.patternContrast, look.patternSoftness, look.spotScale));
        props.SetVector(kSurfaceParams, new Vector4(look.smoothness, look.rimStrength, look.subsurface, 0f));
        props.SetVector(kBodyParams, bodyParams);

        var bounds = new Bounds();
        bounds.SetMinMax(lo, hi);
        bounds.Expand(2f * reach * maxScale);
        rp.worldBounds = bounds;
        rp.matProps = props;
        Graphics.RenderMeshPrimitives(rp, Mesh, 0, n);
    }

    public void Dispose()
    {
        bonesBuf?.Dispose(); bonesBuf = null;
        instBuf?.Dispose(); instBuf = null;
        bindBuf?.Dispose(); bindBuf = null;
        if (Mesh != null)
        {
            if (Application.isPlaying) Object.Destroy(Mesh); else Object.DestroyImmediate(Mesh);
            Mesh = null;
        }
        sims.Clear();
    }
}
