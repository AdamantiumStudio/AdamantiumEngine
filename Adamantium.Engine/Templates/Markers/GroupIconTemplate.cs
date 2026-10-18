using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Triangulation;

namespace Adamantium.Engine.Templates.Markers;

/// <summary>The icon of a group: a folder, as its icon in a hierarchy has.</summary>
public class GroupIconTemplate : MarkerIconTemplate
{
    protected override Mesh Generate()
    {
        var folder = new MeshContour(new[]
        {
            new Vector2(-0.34, 0.2), new Vector2(-0.1, 0.2), new Vector2(-0.02, 0.12),
            new Vector2(0.34, 0.12), new Vector2(0.34, -0.28), new Vector2(-0.34, -0.28)
        });
        var polygon = new Polygon { FillRule = FillRule.NonZero };
        polygon.AddContours(folder);

        var mesh = new Mesh { MeshTopology = PrimitiveType.TriangleList };
        mesh.SetPoints(polygon.FillIndirect());
        mesh.GenerateBasicIndices();
        mesh.Optimize();
        return mesh;
    }
}
