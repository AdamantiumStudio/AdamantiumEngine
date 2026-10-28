using System;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.Templates.Lights;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Tools;

/// <summary>
/// The reach of a point light as a sphere, with a handle on each side that drags the range.
/// </summary>
public class PointLightHandles : Handles
{
    private const float AnchorPoints = 9;
    private const float Smallest = 0.01f;

    private static readonly Vector3F[] Directions =
    [
        Vector3F.UnitX, -Vector3F.UnitX, Vector3F.UnitY, -Vector3F.UnitY, Vector3F.UnitZ, -Vector3F.UnitZ
    ];

    private readonly Entity[] anchors;
    private Vector3<double> center;
    private Vector3F direction;

    public PointLightHandles()
        : base(new PointLightVisualTemplate().BuildEntity(null, nameof(PointLightHandles)))
    {
        Shape.IgnoreInCollisionDetection = true;
        anchors =
        [
            Shape.Get("AnchorPointRight"), Shape.Get("AnchorPointLeft"), Shape.Get("AnchorPointUp"),
            Shape.Get("AnchorPointDown"), Shape.Get("AnchorPointForward"), Shape.Get("AnchorPointBackward")
        ];
    }

    public override bool AppliesTo(Entity target)
    {
        return target?.GetComponent<Light>() is { Type: LightType.Point };
    }

    public override void Place(Entity target, Camera camera)
    {
        var range = target.GetComponent<Light>().Range;
        var at = target.Transform.WorldPosition;

        var metadata = Shape.Transform.GetMetadata(camera);
        metadata.WorldMatrixF = Matrix4x4.CreateScale<float>(2 * range) * Matrix4x4.CreateTranslation<float>(InRender(at, camera));
        metadata.Enabled = true;

        for (int i = 0; i < anchors.Length; i++)
        {
            PlaceAt(anchors[i], at + (Directions[i] * range).As<double>(), AnchorPoints, camera);
        }
    }

    public override void BeginDrag(Entity target, Entity handle, in PickRay ray)
    {
        center = target.Transform.WorldPosition;
        direction = Directions[Math.Max(Array.IndexOf(anchors, handle), 0)];
    }

    public override void Drag(Entity target, in PickRay ray)
    {
        if (AlongLine(ray.Ray, InRender(center, ray.Camera), direction, out var along))
        {
            target.GetComponent<Light>().Range = Math.Max(Math.Abs(along), Smallest);
        }
    }
}
