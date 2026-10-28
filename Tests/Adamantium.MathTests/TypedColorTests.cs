using System.Runtime.CompilerServices;
using Adamantium.Mathematics.Typed;
using NUnit.Framework;
using Color = Adamantium.Mathematics.Typed.Color;
using Vector4 = Adamantium.Mathematics.Typed.Vector4;

namespace Adamantium.MathTests;

[TestFixture]
public class TypedColorTests
{
    [Test]
    public void EveryByte_GoesThroughAFractionAndBack_Unchanged()
    {
        for (var value = 0; value <= 255; value++)
        {
            var color = new Color((byte)value, (byte)value, (byte)value, (byte)value);
            Assert.That(new Color(color.ToVector4()), Is.EqualTo(color), $"{value}");
            Assert.That((Color)color.ToColor4(), Is.EqualTo(color), $"{value}");
        }
    }

    [Test]
    public void AFraction_BecomesTheNearestByte_ClampedAndNaNAsZero()
    {
        Assert.That(new Color(Vector4.Create(0.5f, 1.5f, -0.2f, float.NaN)), Is.EqualTo(new Color(128, 255, 0, 0)));
        Assert.That(new Color(Vector4.Create(0.499f / 255, 0.501f / 255, 0.50392157f, 1f)), Is.EqualTo(new Color(0, 1, 128, 255)),
            "0.50392157 times 255 is 128.49999994, not a half");
        Assert.That(new Color(300, -5, 7), Is.EqualTo(new Color(255, 0, 7, 255)));
    }

    [Test]
    public void EachPacking_IsTheInverseOfItsUnpacking_InTheOrderOfItsName()
    {
        var color = new Color(0x11, 0x22, 0x33, 0x44);

        Assert.That(color.ToRgba(), Is.EqualTo(0x44332211u));
        Assert.That(color.ToBgra(), Is.EqualTo(0x44112233u));
        Assert.That(color.ToArgb(), Is.EqualTo(0x33221144u));
        Assert.That(color.ToAbgr(), Is.EqualTo(0x11223344u));
        Assert.That(Color.FromRgba(color.ToRgba()), Is.EqualTo(color));
        Assert.That(Color.FromBgra(color.ToBgra()), Is.EqualTo(color));
        Assert.That(Color.FromArgb(color.ToArgb()), Is.EqualTo(color));
        Assert.That(Color.FromAbgr(color.ToAbgr()), Is.EqualTo(color));
        Assert.That(Color.FromBgra(0x99000000), Is.EqualTo(new Color(0, 0, 0, 0x99)));
        Assert.That(Unsafe.As<Color, uint>(ref color), Is.EqualTo(color.ToRgba()), "R8G8B8A8: red in the first byte");
        ColorBGRA bgra = color;
        Assert.That(Unsafe.As<ColorBGRA, uint>(ref bgra), Is.EqualTo(color.ToBgra()), "B8G8R8A8: blue in the first byte");
        Assert.That((Color)bgra, Is.EqualTo(color));
    }

    [Test]
    public void Lerp_RoundsAndClamps()
    {
        var (dark, light) = (new Color(10, 20, 30, 40), new Color(11, 220, 230, 240));

        Assert.That(Color.Lerp(dark, light, 0.5f), Is.EqualTo(new Color(11, 120, 130, 140)));
        Assert.That(Color.Lerp(dark, light, -3f), Is.EqualTo(new Color(7, 0, 0, 0)));
        Assert.That(Color.Lerp(dark, light, 2f), Is.EqualTo(new Color(12, 255, 255, 255)));
        Assert.That(Color.Lerp(dark, light, 0f), Is.EqualTo(dark));
        Assert.That(Color.Lerp(dark, light, 1f), Is.EqualTo(light));
        Assert.That(Color.Lerp(dark, dark, float.PositiveInfinity), Is.EqualTo(dark));
        Assert.That(Color.Lerp(dark, dark, float.NaN), Is.EqualTo(dark));
        Assert.That(Color.Lerp(dark, light, float.NaN), Is.EqualTo(new Color(0, 0, 0, 0)));
    }

    [Test]
    public void FractionColors_ConvertAndCompare()
    {
        var color = new Color4F(0.25f, 0.5f, 1f, 1f);
        var nan = new Color4F(float.NaN, 0, 0, 1);
        var same = nan;

        Assert.That((Color)color, Is.EqualTo(new Color(64, 128, 255, 255)));
        Assert.That(new Color3F(0.25f, 0.5f, 1f).ToRgba(), Is.EqualTo(new Color(64, 128, 255, 255).ToRgba()));
        Assert.That(Color4F.Lerp(color, new Color4F(0.75f, 0.5f, 0f, 0f), 0.5f), Is.EqualTo(new Color4F(0.5f, 0.5f, 0.5f, 0.5f)));
        Assert.That(nan == same, Is.False);
        Assert.That(nan.Equals(same), Is.True);
        Assert.That(Color4F.NearEqual(color, new Color4F(0.25f + 1e-7f, 0.5f, 1f, 1f)), Is.True);
    }

    [Test]
    public void TheVertexFormats_HaveTheirSizes()
    {
        Assert.That(Unsafe.SizeOf<Color>(), Is.EqualTo(4));
        Assert.That(Unsafe.SizeOf<ColorBGRA>(), Is.EqualTo(4));
        Assert.That(Unsafe.SizeOf<Color3F>(), Is.EqualTo(12));
        Assert.That(Unsafe.SizeOf<Color4F>(), Is.EqualTo(16));
        Assert.That(Unsafe.SizeOf<Bool4>(), Is.EqualTo(16));
        var truths = new Bool4(true, false, true, false);
        Assert.That(Unsafe.As<Bool4, ulong>(ref truths), Is.EqualTo(1ul), "true is the number 1, false 0");
        Assert.That(truths.X && !truths.Y && truths.Z && !truths.W, Is.True);
    }
}
