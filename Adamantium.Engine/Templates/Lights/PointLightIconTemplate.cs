using System;
using System.Collections.Generic;
using System.Diagnostics;
using Adamantium.ECS;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Triangulation;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.Lights;

public class PointLightIconTemplate : LightIconTemplate
{
    public override Entity BuildEntity(Entity owner, string name)
    {
        var range = 360.0f;
        var tessellation = 60;
        float angle = range / tessellation;

        var bulbPoints = new List<Vector2<double>>();

        var angleItem = float.DegreesToRadians(angle);
        var startAngle = float.DegreesToRadians(0.0f);
        angle = startAngle;
        var center = new Vector3<double>(0, 0.0f, 0);
        var radiusX = 0.2;
        var radiusY = 0.21;
        var triggerAngle1 = float.DegreesToRadians(315);
        var triggerAngle2 = float.DegreesToRadians(225);
        Vector2<double> leftLineStart = Vector2<double>.Zero;
        Vector2<double> rightLineStart = Vector2<double>.Zero;

        for (int i = 0; i <= tessellation; ++i)
        {
            var x = center.X + (radiusX * Math.Cos(angle));
            var y = center.Y + (radiusY * Math.Sin(angle));

            var vertex = new Vector2<double>(x, y);

            bulbPoints.Add(vertex);

            angle += angleItem;
        }

        rightLineStart.X = center.X + (radiusX * Math.Cos(triggerAngle1));
        rightLineStart.Y = center.Y + (radiusY * Math.Sin(triggerAngle1));

        leftLineStart.X = center.X + (radiusX * Math.Cos(triggerAngle2));
        leftLineStart.Y = center.Y + (radiusY * Math.Sin(triggerAngle2));

        var basePartSize = 0.05;
        var smallSize = 0.025;
        var bottomPartStartCoords = 0.25;
        var bulbBasePoints1 = new List<Vector2<double>>();
        bulbBasePoints1.Add(leftLineStart);
        bulbBasePoints1.Add(new Vector2<double>(-basePartSize, -bottomPartStartCoords));
        bulbBasePoints1.Add(new Vector2<double>(basePartSize, -bottomPartStartCoords));
        bulbBasePoints1.Add(rightLineStart);

        bottomPartStartCoords = 0.29;
        var bulbBasePoints2 = new List<Vector2<double>>();
        bulbBasePoints2.Add(new Vector2<double>(basePartSize, -bottomPartStartCoords));
        bulbBasePoints2.Add(new Vector2<double>(basePartSize, -bottomPartStartCoords + smallSize));
        bulbBasePoints2.Add(new Vector2<double>(-basePartSize, -bottomPartStartCoords + smallSize));
        bulbBasePoints2.Add(new Vector2<double>(-basePartSize, -bottomPartStartCoords));

        bottomPartStartCoords = 0.33;
        var bulbBasePoints3 = new List<Vector2<double>>();
        bulbBasePoints3.Add(new Vector2<double>(basePartSize, -bottomPartStartCoords));
        bulbBasePoints3.Add(new Vector2<double>(basePartSize, -bottomPartStartCoords + smallSize));
        bulbBasePoints3.Add(new Vector2<double>(-basePartSize, -bottomPartStartCoords + smallSize));
        bulbBasePoints3.Add(new Vector2<double>(-basePartSize, -bottomPartStartCoords));

        var bulbBasePoints4 = new List<Vector2<double>>();
        var baseStartCoords = 0.35;
        range = 180;
        tessellation = 40;
        angle = range / tessellation;

        angleItem = float.DegreesToRadians(angle);
        startAngle = float.DegreesToRadians(-180);
        angle = startAngle;
        center = new Vector3<double>(0, -baseStartCoords, 0);
        radiusX = basePartSize;
        radiusY = basePartSize / 2;

        for (int i = 0; i <= tessellation; ++i)
        {
            var x = center.X + (radiusX * Math.Cos(angle));
            var y = center.Y + (radiusY * Math.Sin(angle));

            var vertex = new Vector2<double>(x, y);

            bulbBasePoints4.Add(vertex);

            angle += angleItem;
        }

        MeshContour bulb     = new MeshContour(bulbPoints)      { Name = "Lightbulb" };
        MeshContour bulbBase = new MeshContour(bulbBasePoints1) { Name = "Bulb_Base" };
        MeshContour base3    = new MeshContour(bulbBasePoints2) { Name = "Base_2" };
        MeshContour base2    = new MeshContour(bulbBasePoints3) { Name = "Base_3" };
        MeshContour base1    = new MeshContour(bulbBasePoints4) { Name = "Base_4" };

        Polygon polygon = new Polygon();
        polygon.AddContours(base1, base2, base3, bulbBase, bulb);

        polygon.FillRule = FillRule.NonZero;
        Stopwatch timer = Stopwatch.StartNew();
        var triangulatedList = polygon.FillIndirect();
        timer.Stop();
        var pointLightIcon = new Mesh();
        pointLightIcon.MeshTopology = PrimitiveType.TriangleList;
        pointLightIcon.SetPoints(triangulatedList);
        pointLightIcon.GenerateBasicIndices();
        pointLightIcon.Optimize();

        var meshes = new List<Mesh>();

        tessellation = 20;

        float height = 0.1f;
        float raySize = 0.3f;
        startAngle = -45;
        range = 360;
        angleItem = range / 8;
        angle = startAngle;
        var ray = Shapes.Rectangle.GenerateGeometry(
            GeometryType.Solid, 
            raySize, 
            height, 
            new CornerRadius(height / 2),
            tessellation, 
            Matrix4x4.CreateTranslation<double>(new Vector3<double>((raySize / 2) + 0.3f, 0, 0)));
        for (int i = 0; i < 7; i++)
        {
            meshes.Add(ray.Clone(Matrix4x4.CreateRotationZ<double>(float.DegreesToRadians(angle))));
            angle += angleItem;
        }

        MergeInstance[] instances = new MergeInstance[meshes.Count];
        for (int i = 0; i < instances.Length; i++)
        {
            var inst = new MergeInstance(meshes[i], Matrix4x4<double>.Identity, false);
            instances[i] = inst;
        }

        pointLightIcon.Merge(instances);

        return BuildSubEntity(owner, name, Colors.White, pointLightIcon, BoundingVolume.OrientedBox);
    }
}