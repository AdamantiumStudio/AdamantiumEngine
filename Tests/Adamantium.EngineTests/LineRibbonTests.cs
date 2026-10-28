using Adamantium.Engine.Rendering;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class LineRibbonTests
{
    [Test]
    public void ALineList_BecomesAQuadPerSegment_WithBothEndsInEveryCorner()
    {
        var lines = new Mesh(PrimitiveType.LineList).SetPoints(new[]
        {
            new Vector3<double>(0, 0, 0), new Vector3<double>(1, 0, 0),
            new Vector3<double>(1, 0, 0), new Vector3<double>(1, 2, 0),
        });

        var ribbon = LineRibbon.Build(lines);

        Assert.That(ribbon.MeshTopology, Is.EqualTo(PrimitiveType.TriangleList));
        Assert.That(ribbon.Points.Length, Is.EqualTo(8));
        Assert.That(ribbon.Indices.Length, Is.EqualTo(12));

        AssertCorner(ribbon, 0, new Vector3<double>(0, 0, 0), new Vector3F(1, 0, 0), new Vector2F(-1, 0));
        AssertCorner(ribbon, 1, new Vector3<double>(0, 0, 0), new Vector3F(1, 0, 0), new Vector2F(1, 0));
        AssertCorner(ribbon, 2, new Vector3<double>(1, 0, 0), new Vector3F(0, 0, 0), new Vector2F(-1, 1));
        AssertCorner(ribbon, 3, new Vector3<double>(1, 0, 0), new Vector3F(0, 0, 0), new Vector2F(1, 1));
        AssertCorner(ribbon, 6, new Vector3<double>(1, 2, 0), new Vector3F(1, 0, 0), new Vector2F(-1, 1));

        Assert.That(ribbon.Indices[..6], Is.EqualTo(new[] { 0, 1, 2, 2, 1, 3 }));
        Assert.That(ribbon.Indices[6..], Is.EqualTo(new[] { 4, 5, 6, 6, 5, 7 }));
    }

    [Test]
    public void ALineStrip_JoinsEachPointToTheNext()
    {
        var strip = new Mesh(PrimitiveType.LineStrip).SetPoints(new[]
        {
            new Vector3<double>(0, 0, 0), new Vector3<double>(1, 0, 0), new Vector3<double>(1, 1, 0),
        });

        var ribbon = LineRibbon.Build(strip);

        Assert.That(ribbon.Points.Length, Is.EqualTo(8));
        AssertCorner(ribbon, 4, new Vector3<double>(1, 0, 0), new Vector3F(1, 1, 0), new Vector2F(-1, 0));
    }

    [Test]
    public void ARestartIndex_BreaksTheStrip()
    {
        var strip = new Mesh(PrimitiveType.LineStrip)
            .SetPoints(new[] { new Vector3<double>(0, 0, 0), new Vector3<double>(1, 0, 0), new Vector3<double>(2, 0, 0), new Vector3<double>(3, 0, 0) })
            .SetIndices(new[] { 0, 1, -1, 2, 3 });

        var ribbon = LineRibbon.Build(strip);

        Assert.That(ribbon.Points.Length, Is.EqualTo(8));
        AssertCorner(ribbon, 4, new Vector3<double>(2, 0, 0), new Vector3F(3, 0, 0), new Vector2F(-1, 0));
    }

    private static void AssertCorner(Mesh ribbon, int corner, Vector3<double> own, Vector3F other, Vector2F sideAndEnd)
    {
        Assert.That(ribbon.Points[corner], Is.EqualTo(own), $"corner {corner}: position");
        Assert.That(ribbon.Normals[corner], Is.EqualTo(other), $"corner {corner}: other end");
        Assert.That(ribbon.UV0[corner], Is.EqualTo(sideAndEnd), $"corner {corner}: side and end");
    }
}
