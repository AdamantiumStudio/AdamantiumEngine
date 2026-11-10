using System;
using System.Collections.Generic;
using System.Text;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Graphics;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;
using Adamantium.Pdf.Shadings;
using NUnit.Framework;

namespace Adamantium.PdfTests;

public class PdfShadingTests
{
    private const string RedToBlue = "<< /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >>";

    private static (PdfDocument Document, int First) Document(params byte[][] objects)
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        var first = builder.Next;
        foreach (var body in objects)
        {
            builder.Add(body);
        }

        return (PdfDocument.Open(builder.Build("/Root 1 0 R")), first);
    }

    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text);

    private static T Read<T>(PdfDocument document, int number) where T : PdfShading =>
        PdfShading.Create(document.GetObject(number), new PdfResources(new PdfDictionary(), document), document) as T;

    private static double[] Rgb(PdfShading shading, params double[] values)
    {
        var rgb = new double[3];
        shading.ToRgb(values, rgb);
        return rgb;
    }

    [Test]
    public void AnAxialShadingIsReadWithItsAxisDomainAndExtend()
    {
        var (document, first) = Document(
            Text("<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 100 0] /Domain [0 2] " +
                 "/Extend [true false] /Function 4 0 R /Background [1 1 1] /BBox [0 0 50 50] >>"),
            Text(RedToBlue));
        var shading = Read<PdfAxialShading>(document, first);
        Assert.That(shading.Type, Is.EqualTo(2));
        Assert.That(shading.Start, Is.EqualTo(new PdfPoint(0, 0)));
        Assert.That(shading.End, Is.EqualTo(new PdfPoint(100, 0)));
        Assert.That((shading.T0, shading.T1), Is.EqualTo((0.0, 2.0)));
        Assert.That((shading.ExtendStart, shading.ExtendEnd), Is.EqualTo((true, false)));
        Assert.That(shading.Background, Is.EqualTo(new[] { 1.0, 1, 1 }));
        Assert.That(shading.BBox?.Width, Is.EqualTo(50));
        Assert.That(shading.ValueCount, Is.EqualTo(1));
        Assert.That(Rgb(shading, 0.5), Is.EqualTo(new[] { 0.5, 0, 0.5 }).Within(1e-6));
    }

    [Test]
    public void ARadialShadingIsReadWithBothCircles()
    {
        var (document, first) = Document(
            Text("<< /ShadingType 3 /ColorSpace /DeviceRGB /Coords [10 20 0 30 40 50] /Function 4 0 R >>"),
            Text(RedToBlue));
        var shading = Read<PdfRadialShading>(document, first);
        Assert.That(shading.StartCenter, Is.EqualTo(new PdfPoint(10, 20)));
        Assert.That(shading.StartRadius, Is.EqualTo(0));
        Assert.That(shading.EndCenter, Is.EqualTo(new PdfPoint(30, 40)));
        Assert.That(shading.EndRadius, Is.EqualTo(50));
        Assert.That((shading.T0, shading.T1), Is.EqualTo((0.0, 1.0)));
        Assert.That((shading.ExtendStart, shading.ExtendEnd), Is.EqualTo((false, false)));
    }

    [TestCase("/ShadingType 3 /ColorSpace /DeviceRGB /Coords [0 0 -1 0 0 5] /Function 4 0 R")]
    [TestCase("/ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 1] /Function 4 0 R")]
    [TestCase("/ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 1 1]")]
    [TestCase("/ShadingType 2 /ColorSpace /Pattern /Coords [0 0 1 1] /Function 4 0 R")]
    [TestCase("/ShadingType 9 /ColorSpace /DeviceRGB")]
    [TestCase("/ShadingType 4 /ColorSpace /DeviceRGB")]
    public void ABrokenShadingIsNone(string dictionary)
    {
        var (document, first) = Document(Text("<< " + dictionary + " >>"), Text(RedToBlue));
        Assert.That(Read<PdfShading>(document, first), Is.Null);
    }

    [Test]
    public void AFunctionShadingKeepsItsDomainAndMatrix()
    {
        var (document, first) = Document(
            Text("<< /ShadingType 1 /ColorSpace /DeviceGray /Matrix [2 0 0 2 5 5] /Function 4 0 R >>"),
            PdfFileBuilder.Stream("/FunctionType 4 /Domain [0 1 0 1] /Range [0 1]", Text("{ add 2 div }")));
        var shading = Read<PdfFunctionShading>(document, first);
        Assert.That(shading, Is.Not.Null);
        Assert.That(shading.Domain, Is.EqualTo(new[] { 0.0, 1, 0, 1 }));
        Assert.That(shading.Matrix, Is.EqualTo(new PdfMatrix(2, 0, 0, 2, 5, 5)));
    }

    [Test]
    public void AFreeFormMeshMakesItsTrianglesByTheFlags()
    {
        // 8-bit flag, coordinates and gray; each vertex starts on a byte. Flags 0 x x, then 1 (b c new), then 2 (a c new).
        var data = new BitPacker();
        foreach (var (flag, x, y, gray) in new[] { (0, 0, 0, 0), (0, 10, 0, 85), (0, 0, 10, 170), (1, 10, 10, 255), (2, 5, 20, 0) })
        {
            data.Put(flag, 8).Put(x, 8).Put(y, 8).Put(gray, 8);
        }

        var shading = MeshOf(4, "/BitsPerFlag 8 /Decode [0 255 0 255 0 1]", data.ToArray());
        Assert.That(shading.Triangles, Has.Count.EqualTo(3));
        Assert.That(Positions(shading.Triangles[0]), Is.EqualTo(new[] { (0.0, 0.0), (10.0, 0.0), (0.0, 10.0) }));
        Assert.That(Positions(shading.Triangles[1]), Is.EqualTo(new[] { (10.0, 0.0), (0.0, 10.0), (10.0, 10.0) }));
        Assert.That(Positions(shading.Triangles[2]), Is.EqualTo(new[] { (10.0, 0.0), (10.0, 10.0), (5.0, 20.0) }));
        Assert.That(shading.Triangles[0].B.Values[0], Is.EqualTo(85 / 255.0).Within(1e-12));
    }

    [Test]
    public void AFreeFormMeshPacksBitsAndRealignsAtEachVertex()
    {
        // 2-bit flag, 4-bit coordinates, 4-bit gray: 14 bits, padded to two bytes.
        var data = new BitPacker();
        foreach (var (x, y) in new[] { (0, 0), (15, 0), (0, 15) })
        {
            data.Put(0, 2).Put(x, 4).Put(y, 4).Put(15, 4).Align();
        }

        var shading = MeshOf(4, "/BitsPerCoordinate 4 /BitsPerComponent 4 /BitsPerFlag 2 /Decode [0 30 0 30 0 1]",
            data.ToArray(), coordinateBits: null);
        Assert.That(Positions(shading.Triangles[0]), Is.EqualTo(new[] { (0.0, 0.0), (30.0, 0.0), (0.0, 30.0) }));
        Assert.That(shading.Triangles[0].C.Values[0], Is.EqualTo(1));
    }

    [Test]
    public void ALatticeMakesTwoTrianglesACell()
    {
        var data = new BitPacker();
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 2; column++)
            {
                data.Put(column * 10, 8).Put(row * 10, 8).Put(0, 8);
            }
        }

        var shading = MeshOf(5, "/VerticesPerRow 2 /Decode [0 255 0 255 0 1]", data.ToArray());
        Assert.That(shading.Triangles, Has.Count.EqualTo(4));
        Assert.That(Positions(shading.Triangles[0]), Is.EqualTo(new[] { (0.0, 0.0), (10.0, 0.0), (0.0, 10.0) }));
        Assert.That(Positions(shading.Triangles[1]), Is.EqualTo(new[] { (10.0, 0.0), (10.0, 10.0), (0.0, 10.0) }));
        Assert.That(Positions(shading.Triangles[3]), Is.EqualTo(new[] { (10.0, 10.0), (10.0, 20.0), (0.0, 20.0) }));
    }

    [Test]
    public void ACoonsPatchWithStraightEdgesIsTheBilinearTensorPatch()
    {
        // The square 0..30 with its edges cut in thirds: the inner points fall on the thirds too.
        var data = new BitPacker();
        data.Put(0, 8);
        foreach (var index in BoundaryOrder)
        {
            data.Put(index / 4 * 10, 8).Put(index % 4 * 10, 8);
        }

        for (var corner = 0; corner < 4; corner++)
        {
            data.Put(corner * 85, 8);
        }

        var shading = MeshOf(6, "/BitsPerFlag 8 /Decode [0 255 0 255 0 1]", data.ToArray());
        var patch = shading.Patches[0];
        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                Assert.That(patch[i, j].X, Is.EqualTo(i * 10).Within(1e-9), $"p{i}{j}.X");
                Assert.That(patch[i, j].Y, Is.EqualTo(j * 10).Within(1e-9), $"p{i}{j}.Y");
            }
        }

        Assert.That(patch.Corners[2][0], Is.EqualTo(170 / 255.0).Within(1e-12));
    }

    [Test]
    public void APatchWithAFlagTakesAnEdgeAndTwoCornersOfTheOneBefore()
    {
        var data = new BitPacker();
        data.Put(0, 8);
        foreach (var index in BoundaryOrder)
        {
            data.Put(index / 4 * 10, 8).Put(index % 4 * 10, 8);
        }

        for (var corner = 0; corner < 4; corner++)
        {
            data.Put(corner * 85, 8);
        }

        // Flag 1: the new patch's p00..p03 are the old p03, p13, p23, p33 (its top edge); eight new points, two colors.
        data.Put(1, 8);
        for (var k = 4; k < 12; k++)
        {
            data.Put(100 + k, 8).Put(100 + k, 8);
        }

        data.Put(7, 8).Put(9, 8);
        var shading = MeshOf(6, "/BitsPerFlag 8 /Decode [0 255 0 255 0 255]", data.ToArray());
        Assert.That(shading.Patches, Has.Count.EqualTo(2));
        var (before, after) = (shading.Patches[0], shading.Patches[1]);
        Assert.That(after[0, 0], Is.EqualTo(before[0, 3]));
        Assert.That(after[0, 1], Is.EqualTo(before[1, 3]));
        Assert.That(after[0, 2], Is.EqualTo(before[2, 3]));
        Assert.That(after[0, 3], Is.EqualTo(before[3, 3]));
        Assert.That(after.Corners[0], Is.EqualTo(before.Corners[1]));
        Assert.That(after.Corners[1], Is.EqualTo(before.Corners[2]));
        Assert.That(after.Corners[2][0], Is.EqualTo(7));
        Assert.That(after.Corners[3][0], Is.EqualTo(9));
    }

    [Test]
    public void ATensorPatchReadsItsInnerPoints()
    {
        var data = new BitPacker();
        data.Put(0, 8);
        foreach (var index in AllOrder)
        {
            data.Put(index, 8).Put(index * 2, 8);
        }

        for (var corner = 0; corner < 4; corner++)
        {
            data.Put(0, 8);
        }

        var patch = MeshOf(7, "/BitsPerFlag 8 /Decode [0 255 0 255 0 1]", data.ToArray()).Patches[0];
        for (var index = 0; index < 16; index++)
        {
            Assert.That(patch.Points[index], Is.EqualTo(new PdfPoint(index, index * 2)));
        }
    }

    [Test]
    public void DataCutShortEndsTheMesh()
    {
        var data = new BitPacker();
        data.Put(0, 8).Put(0, 8).Put(0, 8).Put(0, 8).Put(0, 8).Put(1, 8);
        var shading = MeshOf(4, "/BitsPerFlag 8 /Decode [0 255 0 255 0 1]", data.ToArray());
        Assert.That(shading.Triangles, Is.Empty);
    }

    [Test]
    public void AMeshWithAFunctionGivesOneValueAVertex()
    {
        var data = new BitPacker();
        foreach (var t in new[] { 0, 128, 255 })
        {
            data.Put(0, 8).Put(t, 8).Put(0, 8).Put(t, 8);
        }

        var (document, first) = Document(
            PdfFileBuilder.Stream("/ShadingType 4 /ColorSpace /DeviceRGB /BitsPerCoordinate 8 /BitsPerComponent 8 " +
                                  "/BitsPerFlag 8 /Decode [0 255 0 255 0 1] /Function 4 0 R", data.ToArray()),
            Text(RedToBlue));
        var shading = Read<PdfMeshShading>(document, first);
        Assert.That(shading.ValueCount, Is.EqualTo(1));
        Assert.That(shading.Triangles[0].C.Values, Has.Length.EqualTo(1));
        Assert.That(Rgb(shading, shading.Triangles[0].C.Values), Is.EqualTo(new[] { 0.0, 0, 1 }).Within(1e-9));
    }

    [Test]
    public void ATilingPatternKeepsItsCellAndSteps()
    {
        var (document, first) = Document(
            PdfFileBuilder.Stream("/PatternType 1 /PaintType 2 /TilingType 3 /BBox [0 0 4 4] /XStep 5 /YStep 6 " +
                                  "/Matrix [1 0 0 1 2 3] /Resources << >>", Text("0 0 2 2 re f")));
        var pattern = PdfPattern.Create(document.GetObject(first), new PdfResources(new PdfDictionary(), document),
            document) as PdfTilingPattern;
        Assert.That(pattern.IsColored, Is.False);
        Assert.That(pattern.TilingType, Is.EqualTo(3));
        Assert.That((pattern.XStep, pattern.YStep), Is.EqualTo((5.0, 6.0)));
        Assert.That(pattern.BBox.Width, Is.EqualTo(4));
        Assert.That(pattern.Matrix, Is.EqualTo(new PdfMatrix(1, 0, 0, 1, 2, 3)));
        Assert.That(pattern.Cell, Is.SameAs(document.GetObject(first)));
    }

    [Test]
    public void AShadingPatternCarriesItsShadingAndGraphicsState()
    {
        var (document, first) = Document(
            Text("<< /PatternType 2 /Shading 4 0 R /ExtGState << /ca 0.5 >> >>"),
            Text("<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 1 0] /Function 5 0 R >>"),
            Text(RedToBlue));
        var pattern = PdfPattern.Create(document.GetObject(first), new PdfResources(new PdfDictionary(), document),
            document) as PdfShadingPattern;
        Assert.That(pattern.Shading, Is.InstanceOf<PdfAxialShading>());
        Assert.That(pattern.Parameters.FillAlpha, Is.EqualTo(0.5));
        Assert.That(pattern.Matrix, Is.EqualTo(PdfMatrix.Identity));
    }

    [TestCase("/PatternType 1 /BBox [0 0 1 1] /XStep 0 /YStep 1")]
    [TestCase("/PatternType 1 /XStep 1 /YStep 1")]
    [TestCase("/PatternType 3")]
    public void ABrokenPatternIsNone(string dictionary)
    {
        var (document, first) = Document(PdfFileBuilder.Stream(dictionary, Text("")));
        Assert.That(PdfPattern.Create(document.GetObject(first), new PdfResources(new PdfDictionary(), document),
            document), Is.Null);
    }

    private static readonly int[] BoundaryOrder = [0, 1, 2, 3, 7, 11, 15, 14, 13, 12, 8, 4];

    private static readonly int[] AllOrder = [0, 1, 2, 3, 7, 11, 15, 14, 13, 12, 8, 4, 5, 6, 10, 9];

    private static PdfMeshShading MeshOf(int type, string entries, byte[] data, int? coordinateBits = 8)
    {
        var bits = coordinateBits is { } given ? $"/BitsPerCoordinate {given} /BitsPerComponent 8 " : "";
        var (document, first) = Document(PdfFileBuilder.Stream(
            $"/ShadingType {type} /ColorSpace /DeviceGray {bits}{entries}", data));
        return Read<PdfMeshShading>(document, first);
    }

    private static (double, double)[] Positions(PdfMeshTriangle triangle) =>
    [
        (triangle.A.Position.X, triangle.A.Position.Y), (triangle.B.Position.X, triangle.B.Position.Y),
        (triangle.C.Position.X, triangle.C.Position.Y),
    ];
}
