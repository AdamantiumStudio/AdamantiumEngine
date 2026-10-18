using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.Markers;

/// <summary>The icon of an empty entity: three axes from a dot, as its icon in a hierarchy has.</summary>
public class EmptyIconTemplate : MarkerIconTemplate
{
    private const double Length = 0.36;
    private const double Thickness = 0.07;

    protected override Mesh Generate()
    {
        var icon = Shapes.Ellipse.GenerateGeometry(GeometryType.Solid, EllipseType.EdgeToEdge, new Vector2(0.17), 0, 360, true, 24);
        double[] angles = [90, 210, 330];
        var axes = new MergeInstance[angles.Length];
        for (int i = 0; i < angles.Length; i++)
        {
            var axis = Shapes.Rectangle.GenerateGeometry(
                GeometryType.Solid,
                Length,
                Thickness,
                new CornerRadius(Thickness / 2),
                8,
                Matrix4x4.Translation(Length / 2, 0, 0) * Matrix4x4.RotationZ(MathHelper.DegreesToRadians(angles[i])));
            axes[i] = new MergeInstance(axis, Matrix4x4.Identity, false);
        }

        return icon.Merge(axes);
    }
}
