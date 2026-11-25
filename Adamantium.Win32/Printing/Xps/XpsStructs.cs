using System.Runtime.InteropServices;

namespace Adamantium.Win32.Printing.Xps;

[StructLayout(LayoutKind.Sequential)]
public struct XpsPoint(float x, float y)
{
    public float X = x;
    public float Y = y;
}

[StructLayout(LayoutKind.Sequential)]
public struct XpsSize(float width, float height)
{
    public float Width = width;
    public float Height = height;
}

[StructLayout(LayoutKind.Sequential)]
public struct XpsRect(float x, float y, float width, float height)
{
    public float X = x;
    public float Y = y;
    public float Width = width;
    public float Height = height;
}

[StructLayout(LayoutKind.Sequential)]
public struct XpsDash(float length, float gap)
{
    public float Length = length;
    public float Gap = gap;
}

/// <summary>A 2D affine matrix in XPS's row order: x' = x*M11 + y*M21 + M31.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct XpsMatrix(float m11, float m12, float m21, float m22, float m31, float m32)
{
    public float M11 = m11;
    public float M12 = m12;
    public float M21 = m21;
    public float M22 = m22;
    public float M31 = m31;
    public float M32 = m32;
}

/// <summary><c>XPS_COLOR</c>: a type, then a union of the sRGB bytes, the scRGB floats or a context's channels - 44
/// bytes, as the largest member is.</summary>
[StructLayout(LayoutKind.Explicit, Size = 44)]
public struct XpsColor
{
    [FieldOffset(0)] public XpsColorType Type;

    [FieldOffset(4)] public byte Alpha;
    [FieldOffset(5)] public byte Red;
    [FieldOffset(6)] public byte Green;
    [FieldOffset(7)] public byte Blue;

    public static XpsColor FromSrgb(byte alpha, byte red, byte green, byte blue) => new()
    {
        Type = XpsColorType.Srgb, Alpha = alpha, Red = red, Green = green, Blue = blue,
    };
}

public enum XpsColorType
{
    Srgb = 1,
    ScRgb = 2,
    Context = 3,
}

public enum XpsSegmentType
{
    ArcLargeClockwise = 1,
    ArcLargeCounterClockwise = 2,
    ArcSmallClockwise = 3,
    ArcSmallCounterClockwise = 4,
    Bezier = 5,
    Line = 6,
    QuadraticBezier = 7,
}

public enum XpsFillRule
{
    EvenOdd = 1,
    NonZero = 2,
}

public enum XpsLineCap
{
    Flat = 1,
    Round = 2,
    Square = 3,
    Triangle = 4,
}

public enum XpsDashCap
{
    Flat = 1,
    Round = 2,
    Square = 3,
    Triangle = 4,
}

public enum XpsLineJoin
{
    Miter = 1,
    Bevel = 2,
    Round = 3,
}

public enum XpsTileMode
{
    None = 1,
    Tile = 2,
    FlipX = 3,
    FlipY = 4,
    FlipXY = 5,
}

public enum XpsSpreadMethod
{
    Pad = 1,
    Reflect = 2,
    Repeat = 3,
}

public enum XpsColorInterpolation
{
    ScRgbLinear = 1,
    SrgbLinear = 2,
}

public enum XpsImageType
{
    Jpeg = 1,
    Png = 2,
    Tiff = 3,
    Wdp = 4,
    Jxr = 5,
}

public enum XpsInterleaving
{
    Off = 1,
    On = 2,
}
