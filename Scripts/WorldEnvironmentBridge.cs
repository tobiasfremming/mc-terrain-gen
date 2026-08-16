using UnityEngine;

[ExecuteAlways]
public class WorldEnvironmentBridge : MonoBehaviour
{
    public WorldConfig config;

    void OnEnable()  { Apply(); }
    void OnValidate(){ Apply(); }
    void Update()    { if (Application.isEditor) Apply(); }

    void Apply()
    {
        if (!config) return;
        Shader.SetGlobalVector("_World_WindDir", config.windDir);
        Shader.SetGlobalFloat ("_World_WindStrength", config.windStrength);
        Shader.SetGlobalFloat ("_World_SeaLevel", config.seaLevel);
        Shader.SetGlobalVector("_World_SlopeThresholds",
            new Vector4(config.slopeThresholds.x, config.slopeThresholds.y, 0, 0));
        Shader.SetGlobalFloat ("_World_TriplanarScale", config.triplanarScale);
    }
}
