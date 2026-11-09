using System;
using System.IO;

namespace Adamantium.Imaging.Icc;

/// <summary>A profile's colors as sRGB: device values through the profile's A2B table of the intent - or its colorant
/// matrix and tone curves, or its gray curve - to the profile connection space, and from there to sRGB by the sRGB
/// colorants adapted to D50, as an sRGB profile has them. Absolute colorimetric scales by the media white point - D50
/// for a version 2 display profile, whose white point tag names the display's own. Safe to use from any thread.
/// </summary>
public sealed class IccTransform
{
    /// <summary>The PCS illuminant, D50.</summary>
    public static readonly (double X, double Y, double Z) D50 = (0.9642, 1.0, 0.8249);

    private static readonly double[] XyzToSrgb = Invert(
    [
        0.4360747, 0.3850649, 0.1430804,
        0.2225045, 0.7168786, 0.0606169,
        0.0139322, 0.0971045, 0.7141733,
    ]);

    private readonly IccLut _lut;
    private readonly IccCurve[] _curves;
    private readonly double[] _matrix;
    private readonly IccColorSpace _connection;
    private readonly (double X, double Y, double Z) _absolute;
    private readonly bool _isAbsolute;
    private (double X, double Y, double Z)? _black;

    private IccTransform(IccProfile profile, IccLut lut, IccCurve[] curves, double[] matrix,
        IccRenderingIntent intent)
    {
        Components = profile.Components;
        _lut = lut;
        _curves = curves;
        _matrix = matrix;
        _connection = profile.ConnectionSpace;
        _isAbsolute = intent == IccRenderingIntent.AbsoluteColorimetric;
        var white = profile.Version.Major < 4 && profile.Class == "mntr" ? D50 : profile.MediaWhite;
        _absolute = (white.X / D50.X, white.Y / D50.Y, white.Z / D50.Z);
    }

    /// <summary>The black of the perceptual reference medium (ICC.1, 6.3.4.2): where a version 4 perceptual or
    /// saturation table puts black, brought to the screen's black.</summary>
    public static readonly (double X, double Y, double Z) ReferenceBlack = (0.00336, 0.0034731, 0.00287);

    /// <summary>The device channels the transform takes.</summary>
    public int Components { get; }

    /// <summary>The transform of <paramref name="profile"/> to sRGB with <paramref name="intent"/>. The perceptual and
    /// saturation intents, where the profile has a table of its own for them or is a matrix-shaper, bring the profile's
    /// black to the screen's (black point compensation, ISO 18619): a version 4 table's reference medium black, else
    /// a neutral of the lightness of the device's darkest values - its colorants all full, or none - no lighter than
    /// L* 50.</summary>
    /// <param name="profile">The profile.</param>
    /// <param name="intent">The rendering intent.</param>
    /// <param name="blackPointCompensation">Whether to compensate black: null for the perceptual and saturation
    /// intents only, as said above; true for the relative colorimetric intent too, as a reader shows a page - a CMYK
    /// printer profile's black then the darkest it prints, found by its perceptual B2A table from L* 0 and back.
    /// </param>
    /// <exception cref="InvalidDataException">The profile has no device-to-PCS transform the engine reads.</exception>
    public static IccTransform ToSrgb(IccProfile profile, IccRenderingIntent intent,
        bool? blackPointCompensation = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var transform = Build(profile, intent);
        var perceptual = intent is IccRenderingIntent.Perceptual or IccRenderingIntent.Saturation;
        if (intent == IccRenderingIntent.AbsoluteColorimetric || !(blackPointCompensation ?? perceptual))
        {
            return transform;
        }

        var version4 = profile.Version.Major >= 4;
        var ownTable = intent switch
        {
            IccRenderingIntent.Perceptual => IccTags.AToB0,
            IccRenderingIntent.Saturation => IccTags.AToB2,
            _ => IccTags.AToB1,
        };
        if (version4 && perceptual && transform._lut != null)
        {
            transform._black = ReferenceBlack;
        }
        else if (!perceptual && profile.ColorSpace == IccColorSpace.Cmyk && profile.Class == "prtr" &&
                 profile.TryTag(IccTags.BToA0, out var back))
        {
            transform._black = PerceptualBlack(transform,
                IccLut.Read(back, transform._connection == IccColorSpace.Xyz), version4);
        }
        else if ((transform._lut == null || profile.Has(ownTable)) && Darkest(profile) is { } darkest)
        {
            var measure = version4 && perceptual ? Build(profile, IccRenderingIntent.RelativeColorimetric) : transform;
            var lightness = 116 * Forward(measure.ConnectionOf(darkest).Y / D50.Y) - 16;
            if (lightness is > 0 and <= 95)
            {
                transform._black = FromLab(Math.Min(lightness, 50), 0, 0);
            }
        }

        return transform;
    }

    private static (double X, double Y, double Z)? PerceptualBlack(IccTransform relative, IccLut back, bool version4)
    {
        if (back.Inputs != 3 || back.Outputs != relative.Components)
        {
            return null;
        }

        var start = version4 ? ReferenceBlack : (X: 0.0, Y: 0.0, Z: 0.0);
        Span<float> black = stackalloc float[3];
        if (relative._connection == IccColorSpace.Lab)
        {
            var fy = Forward(start.Y / D50.Y);
            var l = 116 * fy - 16;
            var a = 500 * (Forward(start.X / D50.X) - fy) + 128;
            var b = 200 * (fy - Forward(start.Z / D50.Z)) + 128;
            if (back.Encoding == IccPcsEncoding.Legacy16)
            {
                (black[0], black[1], black[2]) = ((float)(l * 652.8 / 65535), (float)(a * 256 / 65535),
                    (float)(b * 256 / 65535));
            }
            else
            {
                (black[0], black[1], black[2]) = ((float)(l / 100), (float)(a / 255), (float)(b / 255));
            }
        }
        else
        {
            const double unit = 65535.0 / 32768;
            (black[0], black[1], black[2]) = ((float)(start.X / unit), (float)(start.Y / unit),
                (float)(start.Z / unit));
        }

        Span<float> device = stackalloc float[back.Outputs];
        back.Evaluate(black, device);
        var (_, y, _) = relative.ConnectionOf(device);
        var lightness = Math.Min(50, 116 * Forward(y / D50.Y) - 16);
        return lightness <= 0 ? null : FromLab(lightness, 0, 0);
    }

    private static float[] Darkest(IccProfile profile)
    {
        var device = new float[profile.Components];
        switch (profile.ColorSpace)
        {
            case IccColorSpace.Rgb or IccColorSpace.Gray:
                return device;
            case IccColorSpace.Cmyk or IccColorSpace.Cmy or IccColorSpace.Colorants:
                Array.Fill(device, 1f);
                return device;
            default:
                return null;
        }
    }

    private static double Forward(double t) =>
        t > 216.0 / 24389 ? Math.Cbrt(t) : t * 24389.0 / 27 / 116 + 16.0 / 116;

    private static IccTransform Build(IccProfile profile, IccRenderingIntent intent)
    {
        var tables = intent switch
        {
            IccRenderingIntent.Perceptual => new[] { IccTags.AToB0, IccTags.AToB1, IccTags.AToB2 },
            IccRenderingIntent.Saturation => [IccTags.AToB2, IccTags.AToB0, IccTags.AToB1],
            _ => [IccTags.AToB1, IccTags.AToB0, IccTags.AToB2],
        };
        foreach (var signature in tables)
        {
            if (profile.TryTag(signature, out var tag))
            {
                var lut = IccLut.Read(tag);
                if (lut.Inputs != profile.Components || lut.Outputs != 3)
                {
                    throw new InvalidDataException("An ICC table has channels its profile does not.");
                }

                return new IccTransform(profile, lut, null, null, intent);
            }
        }

        if (profile.ColorSpace == IccColorSpace.Rgb && profile.TryTag(IccTags.RedTrc, out var red) &&
            profile.TryTag(IccTags.GreenTrc, out var green) && profile.TryTag(IccTags.BlueTrc, out var blue) &&
            profile.TryTag(IccTags.RedColorant, out var redXyz) &&
            profile.TryTag(IccTags.GreenColorant, out var greenXyz) &&
            profile.TryTag(IccTags.BlueColorant, out var blueXyz))
        {
            var (rx, ry, rz) = IccProfile.ReadXyz(redXyz);
            var (gx, gy, gz) = IccProfile.ReadXyz(greenXyz);
            var (bx, by, bz) = IccProfile.ReadXyz(blueXyz);
            return new IccTransform(profile, null,
                [IccCurve.Read(red), IccCurve.Read(green), IccCurve.Read(blue)],
                [rx, gx, bx, ry, gy, by, rz, gz, bz], intent);
        }

        if (profile.ColorSpace == IccColorSpace.Gray && profile.TryTag(IccTags.GrayTrc, out var gray))
        {
            return new IccTransform(profile, null, [IccCurve.Read(gray)], null, intent);
        }

        throw new InvalidDataException("An ICC profile has no device-to-PCS transform the engine reads.");
    }

    /// <summary>Converts device values, each 0..1, to sRGB, each 0..1.</summary>
    public void Convert(ReadOnlySpan<float> device, Span<float> rgb)
    {
        ConvertToLinear(device, rgb);
        rgb[0] = Encode(rgb[0]);
        rgb[1] = Encode(rgb[1]);
        rgb[2] = Encode(rgb[2]);
    }

    /// <summary>Converts device values, each 0..1, to linear sRGB - before the transfer function and not clipped, so
    /// smooth enough to interpolate between colors converted before; <see cref="Encode"/> then gives sRGB.</summary>
    public void ConvertToLinear(ReadOnlySpan<float> device, Span<float> linear)
    {
        var (x, y, z) = ConnectionOf(device);
        if (_isAbsolute)
        {
            (x, y, z) = (x * _absolute.X, y * _absolute.Y, z * _absolute.Z);
        }
        else if (_black is { } black)
        {
            x = (x - black.X) * D50.X / (D50.X - black.X);
            y = (y - black.Y) * D50.Y / (D50.Y - black.Y);
            z = (z - black.Z) * D50.Z / (D50.Z - black.Z);
        }

        linear[0] = (float)(XyzToSrgb[0] * x + XyzToSrgb[1] * y + XyzToSrgb[2] * z);
        linear[1] = (float)(XyzToSrgb[3] * x + XyzToSrgb[4] * y + XyzToSrgb[5] * z);
        linear[2] = (float)(XyzToSrgb[6] * x + XyzToSrgb[7] * y + XyzToSrgb[8] * z);
    }

    private (double X, double Y, double Z) ConnectionOf(ReadOnlySpan<float> device)
    {
        double x, y, z;
        if (_lut != null)
        {
            Span<float> pcs = stackalloc float[3];
            _lut.Evaluate(device, pcs);
            (x, y, z) = Connection(pcs, _lut.Encoding);
        }
        else if (_matrix != null)
        {
            var r = _curves[0].Evaluate(device[0]);
            var g = _curves[1].Evaluate(device[1]);
            var b = _curves[2].Evaluate(device[2]);
            x = _matrix[0] * r + _matrix[1] * g + _matrix[2] * b;
            y = _matrix[3] * r + _matrix[4] * g + _matrix[5] * b;
            z = _matrix[6] * r + _matrix[7] * g + _matrix[8] * b;
        }
        else
        {
            var k = _curves[0].Evaluate(device[0]);
            if (_connection == IccColorSpace.Lab)
            {
                (x, y, z) = FromLab(k * 100, 0, 0);
            }
            else
            {
                (x, y, z) = (D50.X * k, D50.Y * k, D50.Z * k);
            }
        }

        return (x, y, z);
    }

    private (double X, double Y, double Z) Connection(ReadOnlySpan<float> pcs, IccPcsEncoding encoding)
    {
        if (_connection == IccColorSpace.Xyz)
        {
            const double scale = 65535.0 / 32768.0;
            return (pcs[0] * scale, pcs[1] * scale, pcs[2] * scale);
        }

        return encoding switch
        {
            IccPcsEncoding.Legacy16 => FromLab(pcs[0] * 65535.0 / 65280.0 * 100, pcs[1] * 65535.0 / 256 - 128,
                pcs[2] * 65535.0 / 256 - 128),
            _ => FromLab(pcs[0] * 100, pcs[1] * 255 - 128, pcs[2] * 255 - 128),
        };
    }

    private static (double X, double Y, double Z) FromLab(double l, double a, double b)
    {
        var fy = (l + 16) / 116;
        return (D50.X * Inverse(fy + a / 500), D50.Y * Inverse(fy), D50.Z * Inverse(fy - b / 200));
    }

    private static double Inverse(double t) =>
        t > 6.0 / 29 ? t * t * t : 3 * (6.0 / 29) * (6.0 / 29) * (t - 4.0 / 29);

    /// <summary>The sRGB transfer function (IEC 61966-2-1) of <paramref name="linear"/> clipped to 0..1.</summary>
    public static float Encode(double linear)
    {
        linear = Math.Clamp(linear, 0, 1);
        return (float)(linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055);
    }

    private static double[] Invert(double[] m)
    {
        var determinant = m[0] * (m[4] * m[8] - m[5] * m[7]) - m[1] * (m[3] * m[8] - m[5] * m[6]) +
                          m[2] * (m[3] * m[7] - m[4] * m[6]);
        return
        [
            (m[4] * m[8] - m[5] * m[7]) / determinant, (m[2] * m[7] - m[1] * m[8]) / determinant,
            (m[1] * m[5] - m[2] * m[4]) / determinant,
            (m[5] * m[6] - m[3] * m[8]) / determinant, (m[0] * m[8] - m[2] * m[6]) / determinant,
            (m[2] * m[3] - m[0] * m[5]) / determinant,
            (m[3] * m[7] - m[4] * m[6]) / determinant, (m[1] * m[6] - m[0] * m[7]) / determinant,
            (m[0] * m[4] - m[1] * m[3]) / determinant,
        ];
    }
}
