using Adamantium.ECS;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.Tools;

public class MoveToolTemplate : BaseToolTemplate
{
   private const float SquareFill = 0.3f;

   private double axisLength;
   private int tesselation;

   public MoveToolTemplate(
      double axisLength,
      Vector3F baseScale,
      int tesselation = 20)
   {
      this.axisLength = axisLength;
      this.tesselation = tesselation;
      this.baseScale = baseScale;
   }

   public override Entity BuildEntity(Entity owner, string name)
   {
      var root = new Entity(owner, name);
      var xSize = new Vector3(axisLength, 0, 0);
      var ySize = new Vector3(0, axisLength, 0);
      var zSize = new Vector3(0, 0, axisLength);

      var quarterPart = axisLength * 0.25;

      var xAxisMesh = Shapes.Line.GenerateGeometry(GeometryType.Outlined, new Vector3(quarterPart, 0, 0), xSize, 0);
      var xAxisTransform = Matrix4x4.RotationZ(MathHelper.DegreesToRadians(-90)) * Matrix4x4.Translation(xSize);
      var coneMeshX = Shapes.Cone.GenerateGeometry(GeometryType.Solid, quarterPart, axisLength * 0.1f, 0, tesselation, xAxisTransform);

      var rightAxis = BuildSubEntity(root, "RightAxis", xAxisMesh, Colors.Red);
      BuildSubEntity(rightAxis, "RightAxisManipulator", coneMeshX, Colors.Red, BoundingVolume.OrientedBox);

      var yAxisMesh = Shapes.Line.GenerateGeometry(GeometryType.Outlined, new Vector3(0, quarterPart, 0), ySize, 0);
      var coneMeshY = coneMeshX.Clone(Matrix4x4.RotationZ(MathHelper.DegreesToRadians(90)));

      var upAxis = BuildSubEntity(root, "UpAxis", yAxisMesh, Colors.Green);
      BuildSubEntity(upAxis, "UpAxisManipulator", coneMeshY, Colors.Green, BoundingVolume.OrientedBox);

      var zAxisMesh = Shapes.Line.GenerateGeometry(GeometryType.Outlined, new Vector3(0, 0, quarterPart), zSize, 0);
      var coneMeshZ = coneMeshX.Clone(Matrix4x4.RotationY(MathHelper.DegreesToRadians(-90)));

      var forwardAxis = BuildSubEntity(root, "ForwardAxis", zAxisMesh, Colors.Blue);
      BuildSubEntity(forwardAxis, "ForwardAxisManipulator", coneMeshZ, Colors.Blue, BoundingVolume.OrientedBox);

      var squareCenter = quarterPart * 1.5;
      BuildSquare(root, "RightForward", quarterPart, Matrix4x4.Translation(squareCenter, 0, squareCenter), Colors.Green);
      BuildSquare(
         root,
         "RightUp",
         quarterPart,
         Matrix4x4.RotationX(MathHelper.DegreesToRadians(90)) * Matrix4x4.Translation(squareCenter, squareCenter, 0),
         Colors.Blue);

      var rot = QuaternionF.RotationYawPitchRoll(MathHelper.DegreesToRadians(90), MathHelper.DegreesToRadians(90), 0);
      BuildSquare(
         root,
         "UpForward",
         quarterPart,
         Matrix4x4.RotationQuaternion(rot) * Matrix4x4.Translation(0, squareCenter, squareCenter),
         Colors.Red);

      var centralCube = Shapes.Cube.GenerateGeometry(GeometryType.Outlined, quarterPart, 1);
      BuildSubEntity(root, "CentralManipulator", centralCube, Colors.Turquoise, BoundingVolume.OrientedBox);

      return root;
   }

   private void BuildSquare(Entity root, string name, double size, Matrix4x4 transform, Color color)
   {
      var fill = Shapes.Plane.GenerateGeometry(GeometryType.Solid, size, size, 1, transform).ClearNormals();
      var square = BuildSubEntity(root, name + "Manipulator", fill, color, BoundingVolume.OrientedBox, SquareFill);
      var outline = Shapes.Plane.GenerateGeometry(GeometryType.Outlined, size, size, 1, transform);
      BuildSubEntity(square, name + "Outline", outline, color);
   }
}