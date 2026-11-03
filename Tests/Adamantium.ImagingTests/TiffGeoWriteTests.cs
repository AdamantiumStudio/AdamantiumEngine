using System.Collections.Generic;
using System.IO;
using Adamantium.Imaging;
using Adamantium.Imaging.Tiff;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>
/// A georeference written with a TIFF and read back as it went in: the transformation - by tie point and scale, or by
/// matrix when it turns - its keys in the directory, in doubles and in text, and a pixel taken as a point.
/// </summary>
[TestFixture]
public class TiffGeoWriteTests
{
    private static TiffReader RoundTrip(TiffGeoReference geo, int pages = 1)
    {
        var frames = new List<byte[]>();
        for (var page = 0; page < pages; page++)
        {
            frames.Add(new byte[4 * 3 * 4]);
        }

        var stream = new MemoryStream();
        TiffEncoder.Save(new FrameBitmap(4, 3, SurfaceFormat.R8G8B8A8.UNorm, [.. frames]), stream,
            new TiffSaveOptions { GeoReference = geo });
        stream.Position = 0;
        return TiffReader.Open(stream);
    }

    private static Dictionary<ushort, IReadOnlyList<double>> Numbers(params (ushort Key, double[] Values)[] keys)
    {
        var numbers = new Dictionary<ushort, IReadOnlyList<double>>();
        foreach (var (key, values) in keys)
        {
            numbers[key] = values;
        }

        return numbers;
    }

    [Test]
    public void ATransformationWithoutTurn_IsReadBack()
    {
        var geo = TiffGeoReference.Create([20, 0, 500000, 0, -20, 6200000],
            Numbers((TiffGeoKey.ModelType, [1]), (TiffGeoKey.RasterType, [1]), (TiffGeoKey.ProjectedType, [32637])));

        using var reader = RoundTrip(geo);
        var read = reader.GeoReference;

        Assert.Multiple(() =>
        {
            Assert.That(read.Transform, Is.EqualTo(geo.Transform));
            Assert.That(read.ProjectedCrs, Is.EqualTo(32637));
            Assert.That(read.ModelType, Is.EqualTo(TiffModelType.Projected));
            Assert.That(read.TiePoints, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ATurnedTransformation_IsWrittenAsTheMatrix()
    {
        var geo = TiffGeoReference.Create([10, 2, 1000, 3, -10, 2000], Numbers((TiffGeoKey.ModelType, [1])));

        using var reader = RoundTrip(geo);

        Assert.That(reader.GeoReference.Transform, Is.EqualTo(geo.Transform));
    }

    [Test]
    public void APixelTakenAsAPoint_IsReadBackAtTheSameCorners()
    {
        var geo = TiffGeoReference.Create([0.5, 0, 10, 0, -0.5, 50],
            Numbers((TiffGeoKey.ModelType, [2]), (TiffGeoKey.RasterType, [2]), (TiffGeoKey.GeographicType, [4326])));

        using var reader = RoundTrip(geo);

        Assert.Multiple(() =>
        {
            Assert.That(reader.GeoReference.RasterType, Is.EqualTo(TiffRasterType.PixelIsPoint));
            Assert.That(reader.GeoReference.Transform, Is.EqualTo(geo.Transform));
        });
    }

    [Test]
    public void DoublesAndTexts_AreReadBack()
    {
        var geo = TiffGeoReference.Create([1, 0, 0, 0, -1, 0],
            Numbers((TiffGeoKey.ModelType, [1]), (TiffGeoKey.ProjectedType, [32767]), (TiffGeoKey.ProjectionMethod, [1]),
                (TiffGeoKey.NaturalOriginLongitude, [39]), (TiffGeoKey.ScaleAtNaturalOrigin, [0.9996]),
                (TiffGeoKey.FalseEasting, [500000])),
            new Dictionary<ushort, string> { [TiffGeoKey.Citation] = "Mine", [TiffGeoKey.ProjectedCitation] = "TM 39" });

        using var reader = RoundTrip(geo);
        var read = reader.GeoReference;

        Assert.Multiple(() =>
        {
            Assert.That(read.ProjectionMethod, Is.EqualTo(TiffProjectionMethod.TransverseMercator));
            Assert.That(read.Numbers[TiffGeoKey.NaturalOriginLongitude], Is.EqualTo(new double[] { 39 }));
            Assert.That(read.Numbers[TiffGeoKey.ScaleAtNaturalOrigin], Is.EqualTo(new double[] { 0.9996 }));
            Assert.That(read.Numbers[TiffGeoKey.FalseEasting], Is.EqualTo(new double[] { 500000 }));
            Assert.That(read.Texts[TiffGeoKey.Citation], Is.EqualTo("Mine"));
            Assert.That(read.Texts[TiffGeoKey.ProjectedCitation], Is.EqualTo("TM 39"));
        });
    }

    [Test]
    public void OnlyTheFirstPage_CarriesTheGeoreference()
    {
        var geo = TiffGeoReference.Create([1, 0, 0, 0, -1, 0], Numbers((TiffGeoKey.ModelType, [2])));

        using var reader = RoundTrip(geo, pages: 2);

        Assert.That(reader.GeoReference, Is.Not.Null);
    }

    [Test]
    public void ATransformationWithoutInverse_IsRefused()
    {
        var model = Numbers((TiffGeoKey.ModelType, [1]));

        Assert.Multiple(() =>
        {
            Assert.That(() => TiffGeoReference.Create([1, 2, 0, 2, 4, 0], model), Throws.ArgumentException);
            Assert.That(() => TiffGeoReference.Create([1e-170, 0, 0, 0, -1e-170, 0], model), Throws.Nothing,
                "tiny but turnable back");
        });
    }

    // GeoTIFF fixes each key's type: a double key with a whole value is still a double, not a short in the directory.
    [Test]
    public void EachKey_IsWrittenAsTheTypeGeoTiffGivesIt()
    {
        var geo = TiffGeoReference.Create([1, 0, 0, 0, -1, 0],
            Numbers((TiffGeoKey.ModelType, [1]), (TiffGeoKey.FalseEasting, [0]), (TiffGeoKey.ScaleAtNaturalOrigin, [1]),
                (TiffGeoKey.NaturalOriginLongitude, [39]), (5000, [7, 8])));
        var stream = new MemoryStream();
        TiffEncoder.Save(new FrameBitmap(4, 3, SurfaceFormat.R8G8B8A8.UNorm, [new byte[48]]), stream,
            new TiffSaveOptions { GeoReference = geo });

        var tiff = TiffFile.Open(new TiffMemorySource(stream.ToArray()));
        var directory = tiff.Directories[0].GetUnsignedValues(TiffTag.GeoKeyDirectory);
        ulong LocationOf(ushort key)
        {
            for (var i = 4; i + 3 < directory.Length; i += 4)
            {
                if (directory[i] == key)
                {
                    return directory[i + 1];
                }
            }

            return ulong.MaxValue;
        }

        Assert.Multiple(() =>
        {
            Assert.That(directory[..4], Is.EqualTo(new ulong[] { 1, 1, 1, 5 }), "GeoTIFF 1.1, five keys");
            Assert.That(LocationOf(TiffGeoKey.ModelType), Is.EqualTo(0));
            Assert.That(LocationOf(TiffGeoKey.FalseEasting), Is.EqualTo(34736));
            Assert.That(LocationOf(TiffGeoKey.ScaleAtNaturalOrigin), Is.EqualTo(34736));
            Assert.That(LocationOf(TiffGeoKey.NaturalOriginLongitude), Is.EqualTo(34736));
            Assert.That(LocationOf(5000), Is.EqualTo(34735), "several shorts stay in the directory");
        });
        using var reader = TiffReader.Open(new MemoryStream(stream.ToArray()));
        Assert.That(reader.GeoReference.Numbers[5000], Is.EqualTo(new double[] { 7, 8 }));
    }

    [Test]
    public void ASouthUpTransformation_IsWrittenAsTheMatrix()
    {
        var geo = TiffGeoReference.Create([2, 0, 10, 0, 3, 20], Numbers((TiffGeoKey.ModelType, [1])));

        using var reader = RoundTrip(geo);

        Assert.Multiple(() =>
        {
            Assert.That(reader.GeoReference.Transform, Is.EqualTo(geo.Transform));
            Assert.That(reader.GeoReference.TiePoints, Is.Empty);
        });
    }

    [Test]
    public void KeysGeoTiffCannotHold_AreRefused()
    {
        double[] transform = [1, 0, 0, 0, -1, 0];
        var model = Numbers((TiffGeoKey.ModelType, [1]));

        Assert.Multiple(() =>
        {
            Assert.That(() => TiffGeoReference.Create(transform, Numbers((TiffGeoKey.RasterType, [1]))),
                Throws.ArgumentException, "no model type");
            Assert.That(() => TiffGeoReference.Create(transform,
                Numbers((TiffGeoKey.ModelType, [1]), (TiffGeoKey.ProjectedType, [1.5]))), Throws.ArgumentException);
            Assert.That(() => TiffGeoReference.Create(transform,
                Numbers((TiffGeoKey.ModelType, [1]), (TiffGeoKey.ProjectedType, [70000]))), Throws.ArgumentException);
            Assert.That(() => TiffGeoReference.Create(transform,
                Numbers((TiffGeoKey.ModelType, [1]), (TiffGeoKey.FalseEasting, []))), Throws.ArgumentException);
            Assert.That(() => TiffGeoReference.Create(transform, model,
                new Dictionary<ushort, string> { [TiffGeoKey.Citation] = "a|b" }), Throws.ArgumentException);
            Assert.That(() => TiffGeoReference.Create(transform, model,
                new Dictionary<ushort, string> { [TiffGeoKey.Citation] = "a\0b" }), Throws.ArgumentException);
            Assert.That(() => TiffGeoReference.Create(transform, model,
                new Dictionary<ushort, string> { [TiffGeoKey.Citation] = "Пулково" }), Throws.ArgumentException);
            Assert.That(() => TiffGeoReference.Create(transform, model,
                new Dictionary<ushort, string> { [TiffGeoKey.ModelType] = "x" }), Throws.ArgumentException);
            Assert.That(() => TiffGeoReference.Create(transform, model,
                new Dictionary<ushort, string> { [TiffGeoKey.Citation] = null }), Throws.ArgumentException);
        });
    }

    [Test]
    public void TiledAndBigTiff_CarryTheGeoreference()
    {
        var geo = TiffGeoReference.Create([20, 0, 500000, 0, -20, 6200000],
            Numbers((TiffGeoKey.ModelType, [1]), (TiffGeoKey.ProjectedType, [32637])));
        var stream = new MemoryStream();
        TiffEncoder.Save(new FrameBitmap(40, 30, SurfaceFormat.R8G8B8A8.UNorm, [new byte[40 * 30 * 4]]), stream,
            new TiffSaveOptions { GeoReference = geo, TileSize = 16, ForceBigTiff = true });
        stream.Position = 0;

        using var reader = TiffReader.Open(stream);

        Assert.That(reader.GeoReference.Transform, Is.EqualTo(geo.Transform));
    }
}
