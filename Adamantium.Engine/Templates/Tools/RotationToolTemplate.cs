using Adamantium.ECS;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.Tools;

public class RotationToolTemplate:BaseToolTemplate
{
    private float maxDiameter;
    private int tessellation;

    public RotationToolTemplate(float maxDiameter, Vector3F baseScale, int tessellation = 32)
    {
        this.maxDiameter = maxDiameter;
        this.tessellation = tessellation;
        this.baseScale = baseScale;
    }

    public override Entity BuildEntity(Entity owner, string name)
    {
        var root = new Entity(owner, name);

        var zAxisMeshManipulator = Shapes.Ellipse.GenerateGeometry(
            GeometryType.Outlined,
            EllipseType.EdgeToEdge,
            new Vector2<double>(maxDiameter),
            0,
            360,
            true,
            tessellation).ContoursToLines();

        var yAxisMeshManipulator = zAxisMeshManipulator.Clone(Matrix4x4.CreateRotationX<double>(float.DegreesToRadians(90)));
        var xAxisMeshManipulator = yAxisMeshManipulator.Clone(Matrix4x4.CreateRotationZ<double>(float.DegreesToRadians(90)));

        var currentViewAxisMesh = Shapes.Ellipse.GenerateGeometry(
            GeometryType.Outlined,
            EllipseType.EdgeToEdge,
            new Vector2<double>(maxDiameter + 0.25f),
            0,
            360,
            true,
            tessellation).ContoursToLines();

        var currentViewCircleMesh = Shapes.Ellipse.GenerateGeometry(
            GeometryType.Outlined,
            EllipseType.EdgeToEdge,
            new Vector2<double>(maxDiameter + 0.1f),
            0,
            360,
            true,
            tessellation).ContoursToLines();

        var centralSphere = Shapes.Sphere.GenerateGeometry(GeometryType.Solid, SphereType.UVSphere, maxDiameter - 0.05f, tessellation);
        var centralPart = BuildSubEntity(root, "CentralManipulator", centralSphere, Colors.LightGray, BoundingVolume.Sphere, 0.1f);
        centralPart.Visible = false;

        BuildSubEntity(root, "ForwardAxisOrbit", zAxisMeshManipulator, Colors.Blue);
        BuildSubEntity(root, "RightAxisOrbit", xAxisMeshManipulator, Colors.Red);
        BuildSubEntity(root, "UpAxisOrbit", yAxisMeshManipulator, Colors.Green);

        var circle = BuildSubEntity(root, "CurrentViewCircle", currentViewCircleMesh, Colors.Black);
        circle.IgnoreInCollisionDetection = true;
        BuildSubEntity(root, "CurrentViewManipulator", currentViewAxisMesh, Colors.White);

        return root;

    }

}