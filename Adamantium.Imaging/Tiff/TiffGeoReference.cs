using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Adamantium.Imaging.Tiff;

/// <summary>Where a GeoTIFF's pixels are on the Earth (OGC GeoTIFF 1.1): the transformation from raster to model
/// coordinates, and the coordinate system those are in - by EPSG code, or by the parameters the file gives. Systems are
/// not converted here.</summary>
public sealed class TiffGeoReference
{
    private const ushort ShortsTag = (ushort)TiffTag.GeoKeyDirectory;
    private const ushort DoublesTag = (ushort)TiffTag.GeoDoubleParams;
    private const ushort TextsTag = (ushort)TiffTag.GeoAsciiParams;

    private readonly double[] _transform;

    private TiffGeoReference(double[] transform, IReadOnlyList<TiffTiePoint> tiePoints,
        IReadOnlyDictionary<ushort, IReadOnlyList<double>> numbers, IReadOnlyDictionary<ushort, string> texts)
    {
        _transform = transform;
        TiePoints = tiePoints;
        Numbers = numbers;
        Texts = texts;
    }

    /// <summary>Whether the raster maps to the model by one affine transformation; false when the file gives only
    /// control points.</summary>
    public bool HasTransform => _transform != null;

    /// <summary>The affine transformation (a, b, c, d, e, f): a pixel corner at (column, row) is at
    /// X = a·column + b·row + c, Y = d·column + e·row + f - for <see cref="TiffRasterType.PixelIsPoint"/> too, shifted
    /// half a pixel from the centers the file gives. From a tie point and pixel scale when the file has them, as GDAL
    /// reads it, else from ModelTransformation. Empty without <see cref="HasTransform"/>.</summary>
    public IReadOnlyList<double> Transform => _transform == null ? [] : Array.AsReadOnly(_transform);

    /// <summary>The tie points as the file gives them.</summary>
    public IReadOnlyList<TiffTiePoint> TiePoints { get; }

    /// <summary>The numeric keys, by key number: shorts and doubles alike.</summary>
    public IReadOnlyDictionary<ushort, IReadOnlyList<double>> Numbers { get; }

    /// <summary>The text keys, by key number.</summary>
    public IReadOnlyDictionary<ushort, string> Texts { get; }

    /// <summary>Projected, geographic or geocentric.</summary>
    public TiffModelType ModelType => (TiffModelType)Code(TiffGeoKey.ModelType) is var type && Enum.IsDefined(type)
        ? type
        : TiffModelType.Unknown;

    /// <summary>Whether a pixel is an area or a point.</summary>
    public TiffRasterType RasterType => Code(TiffGeoKey.RasterType) switch
    {
        1 => TiffRasterType.PixelIsArea,
        2 => TiffRasterType.PixelIsPoint,
        _ => TiffRasterType.Unknown
    };

    /// <summary>The projected system's EPSG code; 0 when none, 32767 when the file describes it itself.</summary>
    public int ProjectedCrs => Code(TiffGeoKey.ProjectedType);

    /// <summary>The geographic system's EPSG code; 0 when none, 32767 when the file describes it itself.</summary>
    public int GeographicCrs => Code(TiffGeoKey.GeographicType);

    /// <summary>The vertical system's EPSG code; 0 when none.</summary>
    public int VerticalCrs => Code(TiffGeoKey.VerticalType);

    /// <summary>The model coordinates of the pixel corner at (<paramref name="column"/>, <paramref name="row"/>);
    /// throws <see cref="InvalidOperationException"/> without <see cref="HasTransform"/>.</summary>
    public (double X, double Y) ToModel(double column, double row) => _transform == null
        ? throw new InvalidOperationException("The GeoTIFF gives control points, not one transformation.")
        : (_transform[0] * column + _transform[1] * row + _transform[2],
            _transform[3] * column + _transform[4] * row + _transform[5]);

    /// <summary>The same reference for a copy of the raster resampled to <paramref name="width"/> ×
    /// <paramref name="height"/> from <paramref name="fullWidth"/> × <paramref name="fullHeight"/>: a pixel there
    /// covers more of the model. Tie points stay as the file gives them.</summary>
    public TiffGeoReference Resampled(int fullWidth, int fullHeight, int width, int height)
    {
        if (_transform == null)
        {
            return this;
        }

        var (sx, sy) = ((double)fullWidth / width, (double)fullHeight / height);
        var t = _transform;
        return new TiffGeoReference([t[0] * sx, t[1] * sy, t[2], t[3] * sx, t[4] * sy, t[5]], TiePoints, Numbers,
            Texts);
    }

    /// <summary>The reference <paramref name="directory"/> gives; null when it has no GeoTIFF tags.</summary>
    internal static TiffGeoReference Read(TiffDirectory directory)
    {
        var keys = directory.GetUnsignedValues(TiffTag.GeoKeyDirectory);
        var tiePoints = directory.GetDoubleValues(TiffTag.ModelTiepoint) ?? [];
        var scale = directory.GetDoubleValues(TiffTag.ModelPixelScale);
        var matrix = directory.GetDoubleValues(TiffTag.ModelTransformation);
        if (keys == null && tiePoints.Length == 0 && matrix == null)
        {
            return null;
        }

        var points = Enumerable.Range(0, tiePoints.Length / 6)
            .Select(i => new TiffTiePoint(tiePoints[i * 6], tiePoints[i * 6 + 1], tiePoints[i * 6 + 3],
                tiePoints[i * 6 + 4], tiePoints[i * 6 + 5]))
            .ToArray();
        var (numbers, texts) = Keys(keys ?? [], directory.GetDoubleValues(TiffTag.GeoDoubleParams) ?? [],
            directory[TiffTag.GeoAsciiParams]?.GetString() ?? "");
        var transform = Affine(matrix, points, scale);
        if (transform != null && numbers.TryGetValue(TiffGeoKey.RasterType, out var raster)
            && raster.Count > 0 && raster[0] == 2)
        {
            transform =
            [
                transform[0], transform[1], transform[2] - 0.5 * (transform[0] + transform[1]),
                transform[3], transform[4], transform[5] - 0.5 * (transform[3] + transform[4])
            ];
        }

        return new TiffGeoReference(transform, Array.AsReadOnly(points), numbers, texts);
    }

    private static double[] Affine(double[] matrix, TiffTiePoint[] points, double[] scale)
    {
        if (points.Length > 0 && scale is { Length: >= 2 } && scale[0] != 0 && scale[1] != 0
            && double.IsFinite(scale[0]) && double.IsFinite(scale[1]))
        {
            var point = points[0];
            return [scale[0], 0, point.X - point.Column * scale[0], 0, -scale[1], point.Y + point.Row * scale[1]];
        }

        if (matrix is { Length: 16 })
        {
            return [matrix[0], matrix[1], matrix[3], matrix[4], matrix[5], matrix[7]];
        }

        return null;
    }

    private static (ReadOnlyDictionary<ushort, IReadOnlyList<double>> Numbers, ReadOnlyDictionary<ushort, string> Texts)
        Keys(ulong[] directory, double[] doubles, string ascii)
    {
        var numbers = new Dictionary<ushort, IReadOnlyList<double>>();
        var texts = new Dictionary<ushort, string>();
        var count = directory.Length < 4 ? 0 : (int)Math.Min(directory[3], (ulong)(directory.Length / 4 - 1));
        for (var i = 1; i <= count; i++)
        {
            var key = (ushort)directory[i * 4];
            var location = directory[i * 4 + 1];
            var length = directory[i * 4 + 2];
            var value = directory[i * 4 + 3];
            switch (location)
            {
                case 0:
                    numbers[key] = Array.AsReadOnly<double>([value]);
                    break;
                case DoublesTag when Within(value, length, doubles.Length):
                    numbers[key] = Array.AsReadOnly(doubles.AsSpan((int)value, (int)length).ToArray());
                    break;
                case ShortsTag when Within(value, length, directory.Length):
                    numbers[key] = Array.AsReadOnly(directory.Skip((int)value).Take((int)length)
                        .Select(v => (double)v).ToArray());
                    break;
                case TextsTag when Within(value, length, ascii.Length):
                    texts[key] = ascii.Substring((int)value, (int)length).TrimEnd('|', '\0');
                    break;
            }
        }

        return (numbers.AsReadOnly(), texts.AsReadOnly());
    }

    private static bool Within(ulong start, ulong length, int size) =>
        start <= (ulong)size && length <= (ulong)size - start;

    private int Code(ushort key) => Numbers.TryGetValue(key, out var values) && values.Count > 0
                                    && double.IsFinite(values[0])
        ? (int)Math.Clamp(values[0], int.MinValue, int.MaxValue)
        : 0;
}
