using UnityEngine;

// Cold rocky alien terrain as a PURE 3D density function (SDF-style).
// Faithful port of the map() construction in Shane's "Desert Canyon"
// shadertoy (techniques reimplemented on our own noise; no code copied):
//
//   float map(in vec3 p){
//       float tx = <pebble texture lookup>;
//       vec3 q = p*.25;
//       float h = dot(sin(q)*cos(q.yzx), vec3(.222)) + dot(sin(q*1.5)*cos(q.yzx*1.5), vec3(.111));
//       float d = p.y + h*6.;
//       q = sin(p*.5 + h); h = q.x*q.y*q.z;
//       p.xy -= path(p.z);
//       float tnl = 1.5 - length(p.xy*vec2(.33,.66)) + h + (1.-tx)*.25;
//       return smaxP(d, tnl, 2.) - tx*.5 + tnl*.8;
//   }
//   vec2 path(in float z){ return vec2(20.*sin(z*.04), 4.*cos(z*.0909) + 3.*(sin(z*.025)-1.)); }
//
// Shane's raw expression uses the standard raymarch SDF convention (positive
// = outside/air) -- the OPPOSITE of ours (positive = solid) -- so this is
// ported as a literal 1:1 expression and negated only at the very end. `tx`
// (a tri-planar pebble texture lookup) is substituted with comparable-scale
// 3D value noise since we have no texture to sample.
//
// `d` (an infinite half-space with a two-layer sin/cos lattice riding on
// it -- gently rolling desert hills) and `tnl` (a tube of open air following
// a winding 2D path as a function of z, elliptical in cross-section) are
// each their own "open region" using the same sign convention; smaxP is a
// smooth MAX, which for two open (positive=outside) regions computes their
// UNION (open wherever EITHER is open) -- i.e. open sky above the hills, OR
// inside the tunnel. Solid rock fills everything else. Because that
// complement is generically well-behaved for a single 1D-parameterized tube
// cut through an otherwise-simple rolling terrain, this never produces
// floating rock (verified via 3D flood-fill at several points along the
// tunnel's path) without needing any of the extra invariants the
// Canyon/bridge fields required.
//
// All amplitude/frequency constants are exposed as tunable fields, uniformly
// scaled 3x from the shader's own raw units to read as full-size terrain in
// our world (same situation the original IQ canyon port faced) -- but at
// that uniform scale, since every length is scaled together, the shape's
// proportions are unchanged from the source.
[CreateAssetMenu(fileName = "AlienField", menuName = "Marching Cubes/Volume (Alien Rock)")]
public class AlienVolumeField : DensityField
{
    [Header("World")]
    public int seed = 555;

    [Header("Rock lattice (Shane's two-layer sin/cos hills)")]
    public float latticeFreq = 0.0833f;
    [Tooltip("Weight of the base (1x frequency) lattice layer.")]
    public float lattice1Amp = 0.222f;
    [Tooltip("Weight of the finer (1.5x frequency) lattice layer.")]
    public float lattice2Amp = 0.111f;
    [Tooltip("Overall height of the rolling hills.")]
    public float hillAmp = 18f;

    [Header("Wall/floor ripple detail (reused-h trick)")]
    public float wallDetailFreq = 0.1667f;

    [Header("Winding tunnel path")]
    public bool enableTunnel = true;
    public float pathAmpA = 60f;
    public float pathFreqA = 0.01333f;
    public float pathAmpB = 12f;
    public float pathFreqB = 0.0303f;
    public float pathDriftAmp = 9f;
    public float pathDriftFreq = 0.00833f;
    public float tunnelRadiusX = 13.5f;
    public float tunnelRadiusY = 6.81f;
    [Tooltip("Smooth-union blend radius (Shane's smaxP s=2, scaled).")]
    public float tunnelBlend = 6f;

    [Header("Pebbled surface detail (stand-in for the shader's texture lookup)")]
    public float pebbleScale = 48f;
    public float pebbleAmp = 0.25f;
    public float tnlPebbleOffset = 0.5f;
    [Tooltip("How strongly the tunnel term is reinforced into the final result (Shane's tnl*.8).")]
    public float tunnelReinforce = 0.8f;

    static float SMax(float a, float b, float k)
    {
        float h = Mathf.Clamp01(0.5f + 0.5f * (a - b) / k);
        return b * (1f - h) + a * h + h * (1f - h) * k;
    }

    void Path(float z, out float px, out float py)
    {
        px = pathAmpA * Mathf.Sin(z * pathFreqA);
        py = pathAmpB * Mathf.Cos(z * pathFreqB) + pathDriftAmp * (Mathf.Sin(z * pathDriftFreq) - 1f);
    }

    float DensityAt(float wx, float wy, float wz)
    {
        uint s = unchecked((uint)seed);

        float qx = wx * latticeFreq, qy = wy * latticeFreq, qz = wz * latticeFreq;
        float l1 = Mathf.Sin(qx) * Mathf.Cos(qy) + Mathf.Sin(qy) * Mathf.Cos(qz) + Mathf.Sin(qz) * Mathf.Cos(qx);
        float l2 = Mathf.Sin(qx * 1.5f) * Mathf.Cos(qy * 1.5f) + Mathf.Sin(qy * 1.5f) * Mathf.Cos(qz * 1.5f) + Mathf.Sin(qz * 1.5f) * Mathf.Cos(qx * 1.5f);
        float h = lattice1Amp * l1 + lattice2Amp * l2;

        float tx = TerrainNoise.VNoise3(wx / pebbleScale, wy / pebbleScale, wz / pebbleScale, s + 8801u);

        float d = wy + h * hillAmp;

        float raw;
        if (enableTunnel)
        {
            float qx2 = Mathf.Sin(wx * wallDetailFreq + h);
            float qy2 = Mathf.Sin(wy * wallDetailFreq + h);
            float qz2 = Mathf.Sin(wz * wallDetailFreq + h);
            float h2 = qx2 * qy2 * qz2;

            Path(wz, out float px, out float py);
            float lx = wx - px, ly = wy - py;
            float dist = Mathf.Sqrt((lx / tunnelRadiusX) * (lx / tunnelRadiusX) + (ly / tunnelRadiusY) * (ly / tunnelRadiusY));
            float tnl = 1.5f - dist * 1.5f + h2 + (1f - tx) * pebbleAmp;

            raw = SMax(d, tnl, tunnelBlend) - tx * tnlPebbleOffset + tnl * tunnelReinforce;
        }
        else
        {
            raw = d - tx * tnlPebbleOffset;
        }
        return -raw; // flip Shane's positive-outside convention to ours (positive = solid)
    }

    public override float Sample(Vector3 p) => DensityAt(p.x, p.y, p.z);

    public override void AddDensityColumn(float wx, float wz, float yStart, float yStep, int count,
                                          float weight, float[] dest, int destIndex, int destStride)
    {
        for (int i = 0; i < count; i++)
        {
            float y = yStart + i * yStep;
            dest[destIndex] += weight * DensityAt(wx, y, wz);
            destIndex += destStride;
        }
    }

    public override bool TryGetHeightBounds(out float minH, out float maxH)
    {
        float hillSpread = (lattice1Amp + lattice2Amp) * hillAmp + 2f;
        float tunnelSpread = enableTunnel ? (pathAmpB + pathDriftAmp * 2f + tunnelRadiusY + tunnelBlend + 3f) : 0f;
        minH = -hillSpread - tunnelSpread;
        maxH = hillSpread + tunnelSpread;
        return true;
    }
}
