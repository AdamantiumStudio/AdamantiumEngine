using System.Collections.Generic;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Templates.CameraTemplates;

/// <summary>
/// The rays of a camera's view, drawn beside its icon: from the eye along +Z, its view, to a frame at depth 1 spanning
/// ±1 - a pyramid for a perspective view, a box for a flat one - with a triangle over the frame's top edge, along +Y, to
/// show which way is up in what the camera sees. Stretched along X and Y to the camera's projection where it is drawn.
/// </summary>
public class CameraFrustumTemplate
{
    private const float MarkGap = 0.1f;
    private const float MarkHalfWidth = 0.5f;
    private const float MarkHeight = 0.4f;

    public Entity BuildEntity(Entity owner, string name, bool flat)
    {
        var start = flat ? 1f : 0f;
        List<Vector3<double>> points =
        [
            new(-start, -start, 0), new(start, -start, 0), new(start, start, 0), new(-start, start, 0),
            new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1),
            new(-MarkHalfWidth, 1 + MarkGap, 1), new(MarkHalfWidth, 1 + MarkGap, 1), new(0, 1 + MarkGap + MarkHeight, 1),
        ];
        List<int> indices = [0, 4, 1, 5, 2, 6, 3, 7, 4, 5, 5, 6, 6, 7, 7, 4, 8, 9, 9, 10, 10, 8];
        if (flat)
        {
            indices.AddRange([0, 1, 1, 2, 2, 3, 3, 0]);
        }

        var rays = new Mesh(PrimitiveType.LineList);
        rays.SetPoints(points);
        rays.SetIndices(indices);

        var entity = new Entity(owner, name);
        entity.AddComponent(new MeshData { Mesh = rays });
        entity.AddComponent(new Material
        {
            MeshColor = Colors.White.ToVector3(),
            HighlightColor = Colors.Yellow.ToVector4(),
            Transparency = 1
        });
        return entity;
    }
}
