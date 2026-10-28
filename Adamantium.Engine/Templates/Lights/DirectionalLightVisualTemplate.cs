using System.Collections.Generic;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Templates.Lights;

/// <summary>
/// The rays of a directional light, drawn beside its icon: a row of parallel arrows across X that run along -Y, the axis
/// <see cref="Light.Direction"/> is taken from, starting a gap away from the icon so they read apart from it.
/// </summary>
public class DirectionalLightVisualTemplate : LightVisualTemplate
{
    private const int Rays = 3;
    private const float Spacing = 0.25f;
    private const float Gap = 0.9f;
    private const float Length = 0.9f;
    private const float HeadLength = 0.15f;
    private const float HeadHalfWidth = 0.08f;

    public override Entity BuildEntity(Entity owner, string name)
    {
        List<Vector3<double>> points = [];
        List<int> indices = [];
        for (int i = 0; i < Rays; i++)
        {
            var start = new Vector3<double>((i - (Rays - 1) / 2f) * Spacing, 0, 0) + Vector3<double>.Down * Gap;
            var tip = start + Vector3<double>.Down * Length;
            var back = tip - Vector3<double>.Down * HeadLength;
            var first = points.Count;
            points.Add(start);
            points.Add(tip);
            points.Add(back - Vector3<double>.UnitX * HeadHalfWidth);
            points.Add(back + Vector3<double>.UnitX * HeadHalfWidth);
            indices.AddRange([first, first + 1, -1, first + 2, first + 1, first + 3, -1]);
        }

        var rays = new Mesh(PrimitiveType.LineStrip);
        rays.SetPoints(points);
        rays.SetIndices(indices);
        return BuildSubEntity(owner, name, Colors.Yellow, rays);
    }
}
