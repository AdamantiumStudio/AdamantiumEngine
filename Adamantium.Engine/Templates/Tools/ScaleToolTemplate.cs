using Adamantium.ECS;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.Tools;

public class ScaleToolTemplate : BaseToolTemplate
{
    private float axisLength;

    public ScaleToolTemplate(
        float axisLength,
        Vector3F baseScale)
    {
        this.axisLength = axisLength;
        this.baseScale = baseScale;
    }

    public override Entity BuildEntity(
        Entity owner,
        string name)
    {
        var root = new Entity(owner, name);

        var cubeSize = axisLength * 0.15f;

        var xSize = new Vector3<double>(axisLength, 0, 0);
        var ySize = new Vector3<double>(0, axisLength, 0);
        var zSize = new Vector3<double>(0, 0, axisLength);

        var xAxisMesh = Shapes.Line.GenerateGeometry(GeometryType.Outlined, Vector3<double>.Zero, xSize, 0);
        var yAxisMesh = Shapes.Line.GenerateGeometry(GeometryType.Outlined, Vector3<double>.Zero, ySize, 0);
        var zAxisMesh = Shapes.Line.GenerateGeometry(GeometryType.Outlined, Vector3<double>.Zero, zSize, 0);

        var xAxisRoot = BuildSubEntity(root, "RightAxis", xAxisMesh, Colors.Red);
        var yAxisRoot = BuildSubEntity(root, "UpAxis", yAxisMesh, Colors.Green);
        var zAxisRoot = BuildSubEntity(root, "ForwardAxis", zAxisMesh, Colors.Blue);

        var transform = Matrix4x4.CreateTranslation<double>(new Vector3<double>(axisLength, 0, 0));
        var xManipulator = Shapes.Cube.GenerateGeometry(GeometryType.Solid, cubeSize, 1, transform);

        transform = Matrix4x4.CreateTranslation<double>(new Vector3<double>(0, axisLength, 0));
        var yManipulator = Shapes.Cube.GenerateGeometry(GeometryType.Solid, cubeSize, 1, transform);

        transform = Matrix4x4.CreateTranslation<double>(new Vector3<double>(0, 0, axisLength));
        var zManipulator = Shapes.Cube.GenerateGeometry(GeometryType.Solid, cubeSize, 1, transform);

        var centralManipulator = Shapes.Cube.GenerateGeometry(GeometryType.Solid, cubeSize, 1);

        BuildSubEntity(xAxisRoot, "RightAxisManipulator", xManipulator, Colors.Red, BoundingVolume.OrientedBox);
        BuildSubEntity(yAxisRoot, "UpAxisManipulator", yManipulator, Colors.Green, BoundingVolume.OrientedBox);
        BuildSubEntity(zAxisRoot, "ForwardAxisManipulator", zManipulator, Colors.Blue, BoundingVolume.OrientedBox);
        BuildSubEntity(root, "CentralManipulator", centralManipulator, Colors.Turquoise, BoundingVolume.OrientedBox);

        var xzCube = Shapes.Cube.GenerateGeometry(
            GeometryType.Solid,
            cubeSize,
            1,
            Matrix4x4.CreateTranslation<double>(axisLength, 0, axisLength));

        BuildSubEntity(root, "RightForwardManipulator", xzCube, Colors.Orange, BoundingVolume.OrientedBox);

        var xyCube = Shapes.Cube.GenerateGeometry(
            GeometryType.Solid,
            cubeSize,
            1,
            Matrix4x4.CreateRotationX<double>(float.DegreesToRadians(90)) * Matrix4x4.CreateTranslation<double>(axisLength, axisLength, 0));

        BuildSubEntity(root, "RightUpManipulator", xyCube, Colors.DarkCyan, BoundingVolume.OrientedBox);

        var rot = Quaternion.CreateFromYawPitchRoll<float>(float.DegreesToRadians(90), float.DegreesToRadians(90), 0);
        var zyCube = Shapes.Cube.GenerateGeometry(
            GeometryType.Solid,
            cubeSize,
            1,
            Matrix4x4.CreateFromQuaternion<double>(rot.As<double>()) * Matrix4x4.CreateTranslation<double>(0, axisLength, axisLength));

        BuildSubEntity(root, "UpForwardManipulator", zyCube, Colors.DarkViolet, BoundingVolume.OrientedBox);

        root.Transform.BaseScale = baseScale;

        return root;
    }
}