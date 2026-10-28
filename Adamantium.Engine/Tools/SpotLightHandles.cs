using System;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.Templates.Lights;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Tools;

/// <summary>
/// The cone of a spot light, with a handle at its end that drags the range and handles on its rim that drag the angle.
/// </summary>
public class SpotLightHandles : Handles
{
    private const float AnchorPoints = 9;
    private const float Smallest = 0.01f;

    private static readonly float SmallestAngle = float.DegreesToRadians(1f);
    private static readonly float LargestAngle = float.DegreesToRadians(89.5f);

    private readonly Entity end;
    private readonly Entity[] rim;
    private Vector3<double> apex;
    private Vector3F axis;
    private Vector3F side;
    private bool range;

    public SpotLightHandles()
        : base(new SpotLightVisualTemplate().BuildEntity(null, nameof(SpotLightHandles)))
    {
        Shape.IgnoreInCollisionDetection = true;
        end = Shape.Get("AnchorPointCenter");
        rim = [Shape.Get("AnchorPointRight"), Shape.Get("AnchorPointLeft"), Shape.Get("AnchorPointForward"), Shape.Get("AnchorPointBackward")];
    }

    public override bool AppliesTo(Entity target)
    {
        return target?.GetComponent<Light>() is { Type: LightType.Spot };
    }

    public override void Place(Entity target, Camera camera)
    {
        var light = target.GetComponent<Light>();
        var at = target.Transform.WorldPosition;
        var orientation = OrientationOf(light);
        var direction = light.Direction;
        var radius = light.SpotRadius;

        var metadata = Shape.Transform.GetMetadata(camera);
        metadata.WorldMatrixF = Matrix4x4.CreateScale<float>(2 * radius, light.Range, 2 * radius)
                                * Matrix4x4.CreateFromQuaternion<float>(orientation)
                                * Matrix4x4.CreateTranslation<float>(InRender(at, camera));
        metadata.Enabled = true;

        var baseCenter = at + (direction * light.Range).As<double>();
        PlaceAt(end, baseCenter, AnchorPoints, camera);

        var sides = SidesOf(orientation);
        for (int i = 0; i < rim.Length; i++)
        {
            PlaceAt(rim[i], baseCenter + (sides[i] * radius).As<double>(), AnchorPoints, camera);
        }
    }

    public override void BeginDrag(Entity target, Entity handle, in PickRay ray)
    {
        var light = target.GetComponent<Light>();
        var orientation = OrientationOf(light);
        apex = target.Transform.WorldPosition;
        axis = light.Direction;

        range = handle == end;
        if (!range)
        {
            side = SidesOf(orientation)[Math.Max(Array.IndexOf(rim, handle), 0)];
        }
    }

    public override void Drag(Entity target, in PickRay ray)
    {
        var light = target.GetComponent<Light>();
        var origin = InRender(apex, ray.Camera);

        if (range)
        {
            if (AlongLine(ray.Ray, origin, axis, out var along))
            {
                light.Range = Math.Max(along, Smallest);
            }

            return;
        }

        if (AlongLine(ray.Ray, origin + axis * light.Range, side, out var reach))
        {
            var angle = (float)Math.Atan(Math.Abs(reach) / light.Range);
            light.OuterSpotAngle = Math.Clamp(angle, SmallestAngle, LargestAngle);
        }
    }

    private static QuaternionF OrientationOf(Light light)
    {
        return RotationBetween(Vector3F.Down, light.Direction);
    }

    private static Vector3F[] SidesOf(QuaternionF orientation)
    {
        var right = Axis(orientation, Vector3F.UnitX);
        var forward = Axis(orientation, Vector3F.UnitZ);
        return [right, -right, forward, -forward];
    }
}
