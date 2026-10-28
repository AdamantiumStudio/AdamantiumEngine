using System;
using System.Collections.Generic;
using Adamantium.ECS;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Triangulation;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.Lights;

public class SpotLightIconTemplate: LightIconTemplate
{
    public override Entity BuildEntity(Entity owner, string name)
    {
        float size = 0.2f;
        var points = new List<Vector2<double>>();
        points.Add(new Vector2<double>(-size, size));
        points.Add(new Vector2<double>(-size, 0));
        points.Add(new Vector2<double>(size, 0));
        points.Add(new Vector2<double>(size, 0));

        float range = 180;
        int tessellation = 20;
        float angle = range / tessellation;

        float angleItem = float.DegreesToRadians(angle);
        var startAngle = float.DegreesToRadians(0);
        angle = startAngle;
        var center = new Vector3F(0, size, 0);
        float radiusX = size;
        float radiusY = size;

        for (int i = 0; i <= tessellation; ++i)
        {
            float x = center.X + (radiusX * (float)Math.Cos(angle));
            float y = center.Y + (radiusY * (float)Math.Sin(angle));
            var vertex = new Vector2<double>(x, y);

            points.Add(vertex);

            angle += angleItem;
        }

        var item = new MeshContour(points);
        var polygon = new Polygon();
        polygon.AddContour(item);
        var triangulatedList = polygon.FillIndirect();
        var spotLightIcon = new Mesh();
        spotLightIcon.MeshTopology = PrimitiveType.TriangleList;
        spotLightIcon.SetPoints(triangulatedList);
        spotLightIcon.Optimize();

        double height = 0.1f;
        double raySize = 0.3f;
        var centralRay = Shapes.Rectangle.GenerateGeometry(
            GeometryType.Solid, 
            raySize, 
            height, 
            new CornerRadius(height/2),
            tessellation, 
            Matrix4x4.CreateRotationZ<double>(float.DegreesToRadians(90)) * Matrix4x4.CreateTranslation<double>(new Vector3<double>(0, (-raySize / 2)-0.2f, 0)));
        var leftRay = centralRay.Clone(Matrix4x4.CreateRotationZ<double>(float.DegreesToRadians(-45)) * Matrix4x4.CreateTranslation<double>(-size/2, 0, 0));
        var rightRay = centralRay.Clone(Matrix4x4.CreateRotationZ<double>(float.DegreesToRadians(45)) * Matrix4x4.CreateTranslation<double>(size/2, 0, 0));

        MergeInstance[] instances = new MergeInstance[3];
        var instance1 = new MergeInstance(leftRay, Matrix4x4<double>.Identity, false);
        instances[0] = instance1;
        var instance2 = new MergeInstance(centralRay, Matrix4x4<double>.Identity, false);
        instances[1] = instance2;
        var instance3 = new MergeInstance(rightRay, Matrix4x4<double>.Identity, false);
        instances[2] = instance3;

        spotLightIcon.Merge(instances);

        return BuildSubEntity(owner, name, Colors.White, spotLightIcon, BoundingVolume.OrientedBox);
    }
}