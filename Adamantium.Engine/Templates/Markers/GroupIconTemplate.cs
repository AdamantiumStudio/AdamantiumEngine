using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Triangulation;

namespace Adamantium.Engine.Templates.Markers;

/// <summary>The icon of a group: a tree of cubes, one over two, as its icon in a hierarchy has. Each cube is its three
/// faces apart, so it reads as a cube filled.</summary>
public class GroupIconTemplate : MarkerIconTemplate
{
    private const double Unit = 0.05;
    private const double CenterX = 7.5;
    private const double CenterY = 8.4;
    private const double FaceShrink = 0.8;
    private const double BarWidth = 0.9;

    protected override Mesh Generate()
    {
        var points = new List<Vector3>();
        AddCube(points, 4, 3.75);
        AddCube(points, 11, 7.5);
        AddCube(points, 11, 13);
        AddBar(points, 4, 6.25, 4, 13);
        AddBar(points, 4, 7.5, 8.7, 7.5);
        AddBar(points, 4, 13, 8.7, 13);

        var mesh = new Mesh { MeshTopology = PrimitiveType.TriangleList };
        mesh.SetPoints(points);
        mesh.GenerateBasicIndices();
        mesh.Optimize();
        return mesh;
    }

    private static void AddCube(List<Vector3> points, double x, double y)
    {
        const double halfWidth = 2.3;
        const double halfHeight = 2.5;
        const double side = 1.25;
        var top = new Vector2(x, y - halfHeight);
        var topRight = new Vector2(x + halfWidth, y - side);
        var bottomRight = new Vector2(x + halfWidth, y + side);
        var bottom = new Vector2(x, y + halfHeight);
        var bottomLeft = new Vector2(x - halfWidth, y + side);
        var topLeft = new Vector2(x - halfWidth, y - side);
        var center = new Vector2(x, y);

        AddFace(points, top, topRight, center, topLeft);
        AddFace(points, topLeft, center, bottom, bottomLeft);
        AddFace(points, center, topRight, bottomRight, bottom);
    }

    private static void AddFace(List<Vector3> points, params Vector2[] corners)
    {
        var middle = Vector2.Zero;
        foreach (var corner in corners)
        {
            middle += corner;
        }

        middle /= corners.Length;
        var shrunk = new Vector2[corners.Length];
        for (var i = 0; i < corners.Length; i++)
        {
            shrunk[i] = middle + (corners[i] - middle) * FaceShrink;
        }

        Fill(points, shrunk);
    }

    private static void AddBar(List<Vector3> points, double fromX, double fromY, double toX, double toY)
    {
        var half = BarWidth / 2;
        Fill(points, fromX == toX
            ? [new Vector2(fromX - half, fromY), new Vector2(fromX + half, fromY), new Vector2(toX + half, toY), new Vector2(toX - half, toY)]
            : [new Vector2(fromX, fromY - half), new Vector2(toX, toY - half), new Vector2(toX, toY + half), new Vector2(fromX, fromY + half)]);
    }

    private static void Fill(List<Vector3> points, Vector2[] pixels)
    {
        var contour = new Vector2[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            contour[i] = new Vector2((pixels[i].X - CenterX) * Unit, (CenterY - pixels[i].Y) * Unit);
        }

        var polygon = new Polygon { FillRule = FillRule.NonZero };
        polygon.AddContours(new MeshContour(contour));
        points.AddRange(polygon.FillIndirect());
    }
}
