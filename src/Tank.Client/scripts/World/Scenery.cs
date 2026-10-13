using Godot;

namespace Tank.Client.World;

/// <summary>Sky, haze and sun for outdoor maps.</summary>
public static class Scenery
{
    public static WorldEnvironment CreateEnvironment()
    {
        var sky = new Sky
        {
            SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.30f, 0.50f, 0.78f),
                SkyHorizonColor = new Color(0.74f, 0.80f, 0.85f),
                GroundHorizonColor = new Color(0.60f, 0.62f, 0.58f),
                GroundBottomColor = new Color(0.25f, 0.27f, 0.22f),
            },
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.8f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogLightColor = new Color(0.72f, 0.78f, 0.84f),
            FogDensity = 0.0018f,
            FogSkyAffect = 0.25f,
        };
        return new WorldEnvironment { Environment = environment };
    }

    public static DirectionalLight3D CreateSun() => new()
    {
        RotationDegrees = new Vector3(-48, -35, 0),
        LightEnergy = 1.25f,
        ShadowEnabled = true,
        DirectionalShadowMaxDistance = 220f,
    };
}
