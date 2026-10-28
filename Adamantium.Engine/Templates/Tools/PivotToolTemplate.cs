using Adamantium.ECS;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.Tools;

public class PivotToolTemplate: BaseToolTemplate
{
    private float axisLength;
    private int tesselation;

    public PivotToolTemplate(float axisLength, Vector3F scale, int tesselation = 40)
    {
        this.tesselation = tesselation;
        this.axisLength = axisLength;
        this.baseScale = scale;
    }

    public override Entity BuildEntity(Entity owner, string name)
    {
        var root = new Entity(owner, name);
        var xSize = new Vector3<double>(axisLength, 0, 0);
        var ySize = new Vector3<double>(0, axisLength, 0);
        var zSize = new Vector3<double>(0, 0, axisLength);

        var quarterPart = axisLength * 0.25f;

        var xAxisMesh = Shapes.Line.GenerateGeometry(GeometryType.Outlined, new Vector3<double>(axisLength/2, 0, 0), xSize, 0);
        var xAxisTransform = Matrix4x4.CreateRotationZ<double>(float.DegreesToRadians(-90)) * Matrix4x4.CreateTranslation<double>(xSize);
        var coneMeshX = Shapes.Cone.GenerateGeometry(GeometryType.Solid, quarterPart, axisLength * 0.1f, 0, tesselation, xAxisTransform);

        var rightAxisEntity = BuildSubEntity(root, "MoveRight", xAxisMesh, Colors.Red);
        BuildSubEntity(rightAxisEntity, "MoveRightManipulator", coneMeshX, Colors.Red, BoundingVolume.OrientedBox);

        var yAxisMesh = Shapes.Line.GenerateGeometry(GeometryType.Outlined, new Vector3<double>(0, axisLength/2, 0), ySize, 0);
        var coneMeshY = coneMeshX.Clone(Matrix4x4.CreateRotationZ<double>(float.DegreesToRadians(90)));

        var upAxis = BuildSubEntity(root, "MoveUp", yAxisMesh, Colors.Green);
        BuildSubEntity(upAxis, "MoveUpManipulator", coneMeshY, Colors.Green, BoundingVolume.OrientedBox);

        var zAxisMesh = Shapes.Line.GenerateGeometry(GeometryType.Outlined, new Vector3<double>(0, 0, axisLength/2), zSize, 0);
        var coneMeshZ = coneMeshX.Clone(Matrix4x4.CreateRotationY<double>(float.DegreesToRadians(-90)));

        var forwardAxis = BuildSubEntity(root, "MoveForward", zAxisMesh, Colors.Blue);
        BuildSubEntity(forwardAxis, "MoveForwardManipulator", coneMeshZ, Colors.Blue, BoundingVolume.OrientedBox);

        var circle = Shapes.Ellipse.GenerateGeometry(GeometryType.Solid, EllipseType.EdgeToEdge, new Vector2<double>(axisLength / 20.0f));
        BuildSubEntity(root, "PivotPoint", circle, Colors.White);

        var forwardOrbit = Shapes.Ellipse.GenerateGeometry(
            GeometryType.Outlined,
            EllipseType.EdgeToEdge,
            new Vector2<double>(axisLength),
            0,
            360,
            true,
            tesselation).ContoursToLines();

        var upOrbit = forwardOrbit.Clone(Matrix4x4.CreateRotationX<double>(float.DegreesToRadians(90)));
        var rightOrbit = upOrbit.Clone(Matrix4x4.CreateRotationZ<double>(float.DegreesToRadians(90)));

        BuildSubEntity(root, "ForwardOrbit", forwardOrbit, Colors.Blue);
        BuildSubEntity(root, "RightOrbit", rightOrbit, Colors.Red);
        BuildSubEntity(root, "UpOrbit", upOrbit, Colors.Green);

        var centralRectangle = Shapes.Rectangle.GenerateGeometry(GeometryType.Outlined, axisLength / 5, axisLength / 5, new CornerRadius(0), 0).ContoursToLines();
        BuildSubEntity(root, "CentralManipulator", centralRectangle, Colors.Turquoise, BoundingVolume.OrientedBox);

        return root;
    }
}