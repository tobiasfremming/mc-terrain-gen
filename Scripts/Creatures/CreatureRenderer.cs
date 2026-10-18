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
// bounds points in the same order. Construction is one path for every plan (CreatureKit.KitRender: mesh, bind
// poses, bones, reach, body params); the soft plan differs only in its affine skin product and shader inputs.
public sealed class CreatureRenderer : IDisposable
{
    const int kInitialCapacity = 4;
    const int kMatrixStride = 64;

    static readonly int
        kBones = Shader.PropertyToID("_Bones"), kInstances = Shader.PropertyToID("_Instances"),
        kBindPoses = Shader.PropertyToID("_BindPoses"), kBonesPerCreature = Shader.PropertyToID("_BonesPerCreature"),
        kDorsal = Shader.PropertyToID("_Dorsal"), kFlank = Shader.PropertyToID("_Flank"),
        kBelly = Shader.PropertyToID("_Belly"), kPatternColor = Shader.PropertyToID("_PatternColor"),
        kPatternType = Shader.PropertyToID("_PatternType"), kPatternParams = Shader.PropertyToID("_PatternParams"),
        kSurfaceParams = Shader.PropertyToID("_SurfaceParams"), kBodyParams = Shader.PropertyToID("_BodyParams"),
        kOctoColors0 = Shader.PropertyToID("_OctoSucker"), kOctoColors1 = Shader.PropertyToID("_OctoSubsurface"),
        kOctoParams = Shader.PropertyToID("_OctoParams"), kOctoParams2 = Shader.PropertyToID("_OctoParams2"),
        kMembraneColor = Shader.PropertyToID("_MembraneColor"), kMembraneParams = Shader.PropertyToID("_MembraneParams"),
        kBandColor = Shader.PropertyToID("_BandColor"), kSheenColor = Shader.PropertyToID("_SheenColor"), // arthropod bands, velvet sheen (alpha = strength)
        kShimmerColor = Shader.PropertyToID("_ShimmerColor"), kShimmerColor2 = Shader.PropertyToID("_ShimmerColor2"), kShimmerParams = Shader.PropertyToID("_ShimmerParams"); // iridescent shimmer

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
        sp = sp.Compiled.Runtime;   // creature kit: the runtime view (a legacy asset is its own)
        this.sp = sp; this.material = material;
        // creature kit: mesh, bind poses, bones, reach and body params from the species' body plan (KitRender)
        CreatureKit.KitRender rd = CreatureKit.KitRender.For(sp);
        cephalopod = rd.AffineSkin;
        bonesPer = rd.Bones;
        bindPoses = rd.BindPoses;
        Mesh = rd.Mesh;
        reach = rd.Reach;
        bodyParams = rd.BodyParams;
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
        props.SetColor(kBandColor, look.bandColor);
        Color sheen = look.sheenColor; sheen.a = look.sheen;
        props.SetColor(kSheenColor, sheen);
        // iridescent shimmer (alpha: strength, glow; 0 = none): bands per rest metre, bands per second
        Color sh1 = look.shimmerColor; sh1.a = look.shimmer;
        Color sh2 = look.shimmerColor2; sh2.a = look.shimmerGlow;
        props.SetColor(kShimmerColor, sh1);
        props.SetColor(kShimmerColor2, sh2);
        float shPeriod = look.shimmerPeriod * Mathf.Sqrt(Mathf.Max(1e-3f, sp.scale));
        props.SetVector(kShimmerParams, new Vector4(look.shimmerWavelength > 0f ? 1f / look.shimmerWavelength : 0f, shPeriod > 0f ? 1f / shPeriod : 0f, 0f, 0f));
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
        else if (sp.WingCount > 0)
        {
            props.SetColor(kMembraneColor, look.membrane);
            props.SetVector(kMembraneParams, new Vector4(look.membraneTranslucency, look.veinContrast, look.membraneRim, 0f));
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
