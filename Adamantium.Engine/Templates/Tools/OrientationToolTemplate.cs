using System;
using System.Collections.Generic;
using Adamantium.ECS;
using Adamantium.Graphics;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.Tools;

public class OrientationToolTemplate: BaseToolTemplate
{
   private double size;
   private Quaternion rotation;

   public OrientationToolTemplate(float size, Vector3F baseScale, QuaternionF rotation)
   {
      this.size = size;
      this.baseScale = baseScale;
      this.rotation = rotation;
   }

   public override Entity BuildEntity(Entity owner, string name)
   {
      var root = new Entity(owner, name);

      // THREE AXES WITH A BALL ON EACH END - the shape Blender and Unity settled on. Six balls are six canonical
      // views, and that is the whole job: a cube buys edge and corner views with twenty-six pickable regions, text on
      // its faces and a bevel, for a gizmo the size of a postage stamp.
      var reach = size * 0.4;
      var ball = size * 0.2;
      var stem = size * 0.035;

      // ONE sphere and ONE arm, placed over and over. Shared BY REFERENCE, which is what lets the renderer send all
      // seven balls as a single instanced draw and all three arms as another - a mesh per part would have been
      // fourteen draws for a gizmo the size of a postage stamp. Where a copy stands lives in its transform, not in
      // its geometry.
      var sphere = Shapes.Sphere.GenerateGeometry(GeometryType.Solid, SphereType.UVSphere, ball, 16);
      var arm = Shapes.Cylinder.GenerateGeometry(GeometryType.Solid, reach * 2, stem, 12);

      var directions = new List<Vector3>();
      var index = 0;

      foreach (var (direction, color) in Axes())
      {
         // One arm per axis, drawn with the positive ball and running the full span, so the two halves meet cleanly.
         if (direction.X + direction.Y + direction.Z > 0)
         {
            var stick = BuildSubEntity(root, "Stem" + index, arm, Colors.DimGray);
            stick.Transform.Rotation = Standing(direction);
         }

         var entity = BuildSubEntity(root, TileName(index++), sphere, color, BoundingVolume.Sphere);
         entity.Transform.Position = direction * reach;
         directions.Add(direction);
      }

      TileDirections = directions.ToArray();

      var hub = BuildSubEntity(root, HomeName, sphere, new Color(104, 106, 108), BoundingVolume.Sphere);
      hub.Transform.ScaleFactor = new Vector3F(1.5f);

      BuildStepArrows(root, size * 0.85);

      return root;
   }

   /// <summary>The hub where the three axes meet - clicking it squares the view back up with the world.</summary>
   public const string HomeName = "HomeManipulator";

   /// <summary>What each ball stands for, in build order - see <see cref="TileName"/>.</summary>
   public static Vector3[] TileDirections { get; private set; } = System.Array.Empty<Vector3>();

   public static string TileName(int index) => "Axis" + index;

   // A cylinder is generated standing along Y; this lays it along the given axis.
   private static QuaternionF Standing(Vector3 direction)
   {
      if (Math.Abs(direction.X) > 0.5) return QuaternionF.RotationAxis(Vector3F.UnitZ, MathHelper.DegreesToRadians(90));
      if (Math.Abs(direction.Z) > 0.5) return QuaternionF.RotationAxis(Vector3F.UnitX, MathHelper.DegreesToRadians(90));

      return QuaternionF.Identity;
   }

   // An axis and its opposite share a hue and differ in shade - which end is which stays readable without labels.
   private static IEnumerable<(Vector3 Direction, Color Color)> Axes()
   {
      yield return (Vector3.Right, Colors.Red);
      yield return (Vector3.Left, Colors.DarkRed);
      yield return (Vector3.Up, Colors.Green);
      yield return (Vector3.Down, Colors.DarkGreen);
      yield return (Vector3.ForwardLH, Colors.Blue);
      yield return (Vector3.BackwardLH, Colors.DarkBlue);
   }

   // The four flat arrows AROUND the gizmo - what steps the view 90 degrees at a time. They live in the screen plane
   // and deliberately do not turn with the axes, so "up" always means up on screen (OrientationTool leaves their
   // orientation alone).
   private void BuildStepArrows(Entity root, double edge)
   {
      var reach = edge * 0.95;
      var arrow = edge * 0.3;

      // ONE cone, placed four ways round - one mesh, one draw. It is generated standing along Y, which already lies IN
      // the screen plane, so it is seen from the SIDE: a round arrowhead the light can shade across, instead of the
      // flat triangle a pyramid turned end-on to the viewer gave.
      var shape = Shapes.Cone.GenerateGeometry(GeometryType.Solid, arrow, arrow * 0.75, 0, 24);

      void Step(string name, double x, double y, double spin)
      {
         var entity = BuildSubEntity(root, name, shape, Colors.LightSlateGray, BoundingVolume.OrientedBox);
         entity.Transform.Rotation = QuaternionF.RotationAxis(Vector3F.UnitZ, MathHelper.DegreesToRadians((float)spin));
         entity.Transform.Position = new Vector3(x, y, 0);
      }

      Step("StepRightManipulator", reach, 0, -90);
      Step("StepLeftManipulator", -reach, 0, 90);
      Step("StepUpManipulator", 0, reach, 0);
      Step("StepDownManipulator", 0, -reach, 180);
   }
}