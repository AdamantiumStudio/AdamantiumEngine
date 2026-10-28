using System;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Rendering;

internal static class ClusterLightExtensions
{
    private const float InnerConeShare = 0.8f;

    public static ClusterLight ToClusterLight(this Light light, CameraBase camera)
    {
        var view = camera.ViewMatrix;
        var color = light.Color * light.Intensity;
        var direction = Vector3F.Normalize(Vector3F.TransformNormal(light.Direction, view));
        if (light.Type == LightType.Directional)
        {
            return new ClusterLight
            {
                ColorCosOuter = new Vector4F(color, 0),
                DirectionCosInner = new Vector4F(direction, 0),
                Kind = ClusterLight.Directional
            };
        }

        var relative = (light.Owner.Transform.WorldPosition - camera.WorldPosition).As<float>();
        var outer = light.OuterSpotAngle;
        var inner = light.InnerSpotAngle > 0 ? Math.Min(light.InnerSpotAngle, outer) : outer * InnerConeShare;
        return new ClusterLight
        {
            PositionRange = new Vector4F(Vector3F.TransformCoordinate(relative, view), light.Range),
            ColorCosOuter = new Vector4F(color, MathF.Cos(outer)),
            DirectionCosInner = new Vector4F(direction, MathF.Cos(inner)),
            Kind = light.Type == LightType.Spot ? ClusterLight.Spot : ClusterLight.Point
        };
    }
}
