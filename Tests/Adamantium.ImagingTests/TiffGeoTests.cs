using System;
using System.IO;
using System.Linq;
using System.Text;
using Adamantium.Imaging.Tiff;
using NUnit.Framework;
using static Adamantium.ImagingTests.HandMadeTiff;

namespace Adamantium.ImagingTests;

/// <summary>
/// GeoTIFF's tags read as OGC GeoTIFF 1.1 defines them: the raster-to-model transformation from a tie point and pixel
/// scale or from a matrix, a pixel as an area or a point, the key directory with its short, double and text values - and
/// GDAL's no-data value and band metadata.
/// </summary>
[TestFixture]
public class TiffGeoTests
{
    private const ushort Short = 3;
    private const ushort Long = 4;
    private const ushort Ascii = 2;
    private const ushort Double = 12;

    private static readonly (ushort, ushort, ulong[]) Utm33 =
        (34735, Short, [1, 1, 0, 4, 1024, 0, 1, 1, 1025, 0, 1, 1, 3072, 0, 1, 32633, 3076, 0, 1, 9001]);

    private static (ushort, ushort, ulong[]) Doubles(ushort tag, params double[] values) =>
        (tag, Double, values.Select(BitConverter.DoubleToUInt64Bits).ToArray());

    private static (ushort, ushort, ulong[]) Text(ushort tag, string text) =>
        (tag, Ascii, Encoding.ASCII.GetBytes(text + "\0").Select(b => (ulong)b).ToArray());

    private static (ushort, ushort, ulong[])[] Gray(int width, int height) =>
    [
        (256, Long, [(ulong)width]), (257, Long, [(ulong)height]), (258, Short, [8]), (259, Short, [1]),
        (262, Short, [1]), (273, Long, [Data]), (277, Short, [1]), (278, Long, [(ulong)height]),
        (279, Long, [(ulong)(width * height)])
    ];

    private static TiffReader Open(params (ushort, ushort, ulong[])[] fields) =>
        TiffReader.Open(new MemoryStream(Build(new byte[4 * 4], [.. Gray(4, 4), .. fields])));

    [Test]
    public void ATiePointAndPixelScale_PlaceEveryCorner()
    {
        using var reader = Open(Doubles(33922, 0, 0, 0, 500000, 4600000, 0), Doubles(33550, 30, 30, 0), Utm33);
        var geo = reader.GeoReference;

        Assert.Multiple(() =>
        {
            Assert.That((geo.ModelType, geo.RasterType, geo.ProjectedCrs),
                Is.EqualTo((TiffModelType.Projected, TiffRasterType.PixelIsArea, 32633)));
            Assert.That(geo.ToModel(0, 0), Is.EqualTo((500000d, 4600000d)));
            Assert.That(geo.ToModel(10, 20), Is.EqualTo((500300d, 4599400d)));
            Assert.That(geo.Numbers[TiffGeoKey.LinearUnits], Is.EqualTo(new double[] { 9001 }));
        });
    }

    // Elevation samples are points: the file's coordinates are pixel centers, and a corner is half a pixel off.
    [Test]
    public void APixelThatIsAPoint_HasItsCornerHalfAPixelOff()
    {
        (ushort, ushort, ulong[]) keys = (34735, Short, [1, 1, 0, 2, 1024, 0, 1, 2, 1025, 0, 1, 2]);
        using var reader = Open(Doubles(33922, 0, 0, 0, 20, 50, 0), Doubles(33550, 0.5, 0.25, 0), keys);

        Assert.That(reader.GeoReference.ToModel(0, 0), Is.EqualTo((19.75d, 50.125d)));
        Assert.That(reader.GeoReference.ModelType, Is.EqualTo(TiffModelType.Geographic));
    }

    [Test]
    public void AMatrix_TurnsTheRaster()
    {
        using var reader = Open(Doubles(34264, 2, 1, 0, 100, -1, 2, 0, 200, 0, 0, 0, 0, 0, 0, 0, 1));

        Assert.That(reader.GeoReference.ToModel(3, 4), Is.EqualTo((2 * 3 + 4 + 100d, -3 + 2 * 4 + 200d)));
    }

    [Test]
    public void ControlPointsAlone_GiveNoTransform()
    {
        using var reader = Open(Doubles(33922, 0, 0, 0, 10, 20, 0, 3, 3, 0, 40, 50, 0));

        Assert.That(reader.GeoReference.HasTransform, Is.False);
        Assert.That(reader.GeoReference.TiePoints, Has.Count.EqualTo(2));
        Assert.That(() => reader.GeoReference.ToModel(0, 0), Throws.InvalidOperationException);
    }

    [Test]
    public void KeysInDoublesAndText_AreReadFromTheirTags()
    {
        (ushort, ushort, ulong[]) keys = (34735, Short, [1, 1, 0, 3, 1026, 34737, 7, 0, 2048, 0, 1, 32767,
            2057, 34736, 1, 1]);
        using var reader = Open(keys, Doubles(34736, 1, 6378137), Text(34737, "Mine 1|"));

        Assert.Multiple(() =>
        {
            Assert.That(reader.GeoReference.Texts[TiffGeoKey.Citation], Is.EqualTo("Mine 1"));
            Assert.That(reader.GeoReference.GeographicCrs, Is.EqualTo(32767));
            Assert.That(reader.GeoReference.Numbers[TiffGeoKey.SemiMajorAxis], Is.EqualTo(new double[] { 6378137 }));
        });
    }

    [Test]
    public void AReducedCopy_CoversMoreOfTheModelAPixel()
    {
        using var reader = Open(Doubles(33922, 0, 0, 0, 1000, 2000, 0), Doubles(33550, 10, 10, 0), Utm33);

        var half = reader.GeoReference.Resampled(4, 4, 2, 2);

        Assert.That(half.ToModel(1, 1), Is.EqualTo((1020d, 1980d)));
    }

    [Test]
    public void GdalNoDataAndBandMetadata_AreRead()
    {
        const string metadata = "<GDALMetadata><Item name=\"AREA\">north</Item>"
                                + "<Item name=\"SCALE\" sample=\"0\" role=\"scale\">0.5</Item>"
                                + "<Item name=\"OFFSET\" sample=\"0\" role=\"offset\">-10</Item>"
                                + "<Item name=\"DESCRIPTION\" sample=\"0\" role=\"description\">height</Item>"
                                + "</GDALMetadata>";
        using var reader = Open(Text(42112, metadata), Text(42113, "-9999"));

        Assert.Multiple(() =>
        {
            Assert.That(reader.NoData, Is.EqualTo(-9999));
            Assert.That(reader.Metadata["AREA"], Is.EqualTo("north"));
            Assert.That((reader.Bands[0].Scale, reader.Bands[0].Offset, reader.Bands[0].Description),
                Is.EqualTo((0.5, -10d, "height")));
            Assert.That(reader.GeoReference, Is.Null);
        });
    }

    // Key values pointing past their arrays - by a hair, or wrapping around 2^64 - are passed over, never read.
    [TestCase(34736UL)]
    [TestCase(34735UL)]
    [TestCase(34737UL)]
    public void AKeyPointingPastItsValues_IsPassedOver(ulong location)
    {
        (ushort, ushort, ulong[]) keys = (34735, 16, [1, 1, 0, 2, 1024, 0, 1, 1, 2057, location, 4, ulong.MaxValue - 1]);
        using var reader = Open(keys, Doubles(34736, 1, 2), Text(34737, "a|"));

        Assert.That(reader.GeoReference.Numbers.ContainsKey(TiffGeoKey.SemiMajorAxis), Is.False);
        Assert.That(reader.GeoReference.ModelType, Is.EqualTo(TiffModelType.Projected));
    }

    [Test]
    public void ABrokenGeoTag_LeavesTheRasterReadable()
    {
        using var reader = Open((34735, Double, [BitConverter.DoubleToUInt64Bits(0.5)]));

        Assert.That(reader.GeoReference, Is.Null);
        Assert.That(reader.Levels[0].Width, Is.EqualTo(4));
    }

    // GDAL takes the first tie point with the pixel scale, and prefers that pair to a matrix.
    [Test]
    public void TiePointsWithAScale_TakeTheFirst()
    {
        using var reader = Open(Doubles(33922, 0, 0, 0, 100, 200, 0, 3, 3, 0, 999, 999, 0), Doubles(33550, 2, 2, 0),
            Doubles(34264, 9, 0, 0, 9, 0, 9, 0, 9, 0, 0, 0, 0, 0, 0, 0, 1));

        Assert.That(reader.GeoReference.ToModel(1, 1), Is.EqualTo((102d, 198d)));
    }

    [Test]
    public void MetadataInUtf8AndDomains_IsReadAsWritten()
    {
        const string metadata = "<GDALMetadata><Item name=\"DESCRIPTION\" sample=\"0\" role=\"description\">Temp °C</Item>"
                                + "<Item name=\"SCALE\" domain=\"OTHER\">7</Item><Item sample=\"0\">nameless</Item>"
                                + "<Item name=\"X\" sample=\"-1\">no band</Item></GDALMetadata>";
        var bytes = Encoding.UTF8.GetBytes(metadata + "\0").Select(b => (ulong)b).ToArray();
        using var reader = Open((42112, Ascii, bytes));

        Assert.Multiple(() =>
        {
            Assert.That(reader.Bands[0].Description, Is.EqualTo("Temp °C"));
            Assert.That(reader.Metadata["OTHER:SCALE"], Is.EqualTo("7"));
            Assert.That(reader.Metadata, Has.Count.EqualTo(1));
            Assert.That(reader.Bands[0].Scale, Is.EqualTo(1));
            Assert.That(reader.Metadata, Is.Not.InstanceOf<System.Collections.Generic.Dictionary<string, string>>());
        });
    }

    [Test]
    public void BrokenGdalMetadata_GivesDefaults()
    {
        using var reader = Open(Text(42112, "<GDALMetadata><Item"), Text(42113, "nan"));

        Assert.That(double.IsNaN(reader.NoData.Value), Is.True);
        Assert.That((reader.Bands[0].Scale, reader.Bands[0].Offset), Is.EqualTo((1d, 0d)));
    }
}
