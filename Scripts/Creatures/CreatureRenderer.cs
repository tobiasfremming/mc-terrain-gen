using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Per-individual shading data, read by the shader as _Instances[SV_InstanceID] (32 bytes).
//   seed       pattern offset (any float; hashed bitwise in the shader)
//   hueShift   hue rotation in turns (0.03 = 10.8 degrees)
//   valueShift brightness offset: albedo *= 1 + valueShift
//   sizeScale  the body's size scale (the bone matrices already carry it)
//   cloak      0 visible .. 1 gone (Forward pass dissolves; shadow and depth passes ignore it)
//   chroma     chromatophore expansion 0 (pale) .. 1 (dark, mottled)
//   flash      deimatic flash 0..1 (pale with dark rings)
//   cloudPhase passing-cloud bands phase (metres along the body)
// HLSL (Shaders/Creature/CreatureSkin.hlsl): struct CreatureInstance, the same eight floats.
[StructLayout(LayoutKind.Sequential)]
public struct CreatureInstance
{
    public float seed, hueShift, valueShift, sizeScale;
    public float cloak, chroma, flash, cloudPhase;
}

// One instanced, GPU-skinned draw per species, for any body plan (ICreatureBody). Each frame: bone worlds x
// bind poses -> skin matrices (Count x BoneCount float4x4), uploaded to _Bones; the shader blends them by
// uv2 (+ uv3) and indexes by SV_InstanceID. Arrays and buffers grow by doubling on Add; Render never allocates.
// The spine path is the former CreatureSim path unchanged: same bone writer, same matrix product, same
// bounds points in the same order.
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
        kSurfaceParams = Shader.PropertyToID("_SurfaceParams"), kBodyParams = Shader.PropertyToID("_BodyParams"),
        kOctoColors0 = Shader.PropertyToID("_OctoSucker"), kOctoColors1 = Shader.PropertyToID("_OctoSubsurface"),
        kOctoParams = Shader.PropertyToID("_OctoParams"), kOctoParams2 = Shader.PropertyToID("_OctoParams2");

    readonly CreatureSpecies sp;
    readonly Material material;
    readonly Matrix4x4[] bindPoses;
    readonly Matrix4x4[] boneScratch;
    readonly int bonesPer;
    readonly float reach;
    readonly Vector4 bodyParams;
    readonly bool cephalopod;
    readonly List<ICreatureBody> bodies = new List<ICreatureBody>();
    readonly MaterialPropertyBlock props = new MaterialPropertyBlock();
    readonly bool gpu;

    Matrix4x4[] _skin;
    CreatureInstance[] instances;
    int capacity;
    bool instancesDirty;
    GraphicsBuffer bonesBuf, instBuf, bindBuf;
    RenderParams rp;

    public Mesh Mesh { get; private set; }
    public int Count => bodies.Count;
    public int BonesPerCreature => bonesPer;

    internal Matrix4x4[] SkinForTest => _skin;
    internal CreatureInstance[] InstancesForTest => instances;
    // the world bounds of the last Render (the draw's culling bounds)
    public Bounds LastBounds { get; private set; }

    public CreatureRenderer(CreatureSpecies sp, Material material)
    {
        this.sp = sp; this.material = material;
        cephalopod = sp.bodyPlan == CreatureBodyPlan.Cephalopod;
        if (cephalopod)
        {
            CephalopodSpec c = sp.cephalopod;
            bonesPer = OctopusBody.BoneCountFor(c);
            bindPoses = OctopusRestPose.BindPoses(c);
            Mesh = OctopusMeshBuilder.Build(c, (sp != null ? sp.name : "") + " octopus");
            // past the simulated points: the arm radius (bulging), the head/mantle shell around its inset
            // lattice, the eyes
            reach = Mathf.Max(c.arm.RootRadius * c.arm.Bulge, 1.5f * Mathf.Max(c.MantleRadius, c.HeadRadius)) + c.EyeRadius + kBoundsMargin;
            float len = c.arm.Length;
            bodyParams = new Vector4(len, 1f / len, c.WebDepth * len, 1f);
        }
        else
        {
            bonesPer = sp.BoneCount;
            bindPoses = CreatureRestPose.BindPoses(sp);
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
        }
        boneScratch = new Matrix4x4[bonesPer];

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
        Add(new SpineBody(sim), inst);
    }

    public void Add(ICreatureBody body, CreatureInstance inst)
    {
        if (body == null) throw new ArgumentNullException(nameof(body));
        if (body.Species != sp) throw new ArgumentException("body belongs to another species", nameof(body));
        if (bodies.Count == capacity) Grow(capacity * 2);
        instances[bodies.Count] = inst;
        bodies.Add(body);
        instancesDirty = true;
    }

    // Swap-removes the sim (the last one takes its slot). False if it was not drawn by this renderer.
    public bool Remove(CreatureSim sim)
    {
        for (int i = 0; i < bodies.Count; i++)
            if (bodies[i] is SpineBody s && s.Sim == sim) { RemoveAt(i); return true; }
        return false;
    }

    public bool Remove(ICreatureBody body)
    {
        int i = bodies.IndexOf(body);
        if (i < 0) return false;
        RemoveAt(i);
        return true;
    }

    void RemoveAt(int i)
    {
        int last = bodies.Count - 1;
        bodies[i] = bodies[last];
        instances[i] = instances[last];
        bodies.RemoveAt(last);
        instancesDirty = true;
    }

    void Grow(int newCapacity)
    {
        var skin = new Matrix4x4[newCapacity * bonesPer];
        var inst = new CreatureInstance[newCapacity];
        if (_skin != null) Array.Copy(_skin, skin, bodies.Count * bonesPer);
        if (instances != null) Array.Copy(instances, inst, bodies.Count);
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
        int n = bodies.Count;
        if (n == 0 || Mesh == null) return;

        Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
        float maxScale = 0f;
        for (int s = 0; s < n; s++)
        {
            ICreatureBody body = bodies[s];
            body.WriteBoneWorld(boneScratch, 0);
            int o = s * bonesPer;
            if (cephalopod) for (int b = 0; b < bonesPer; b++) _skin[o + b] = OctoMath.MulAffine(boneScratch[b], bindPoses[b]);
            else for (int b = 0; b < bonesPer; b++) _skin[o + b] = boneScratch[b] * bindPoses[b];
            body.EncapsulateBounds(ref lo, ref hi);
            maxScale = Mathf.Max(maxScale, body.SizeScale);
            if (body.DynamicLook) { body.UpdateLook(ref instances[s]); instancesDirty = true; }
        }

        var bounds = new Bounds();
        bounds.SetMinMax(lo, hi);
        bounds.Expand(2f * reach * maxScale);
        LastBounds = bounds;

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
        if (cephalopod)
        {
            OctopusLook ol = sp.cephalopod.look;
            TentacleSpec arm = sp.cephalopod.arm;
            props.SetColor(kOctoColors0, ol.Sucker);
            props.SetColor(kOctoColors1, ol.Subsurface);
            props.SetVector(kOctoParams, new Vector4(ol.ChromaScale, ol.Papillae, ol.SuckerSize, ol.CloudSpeed));
            props.SetVector(kOctoParams2, new Vector4(ol.Wetness, ol.RimGlint, arm.RootRadius, arm.Length));
        }

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
        bodies.Clear();
    }
}
