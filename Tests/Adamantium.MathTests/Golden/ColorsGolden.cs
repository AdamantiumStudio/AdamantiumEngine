using System;
using Adamantium.Mathematics.Typed;
using Color = Adamantium.Mathematics.Typed.Color;

namespace Adamantium.MathTests.Golden;

/// <summary>The recorded operations of the colors, answered by <see cref="Color"/>.</summary>
public static class ColorsGolden
{
    private const string C = "Color";

    /// <summary>Adds the operations of Color to <paramref name="map"/>.</summary>
    public static void Register(GoldenMap map)
    {
        RegisterColor(map);
        RegisterFractions(map);
        RegisterBgra(map);
        RegisterBool4(map);
        map.NotMapped(key => Array.Exists(Unused, name => key.StartsWith($"Color4F.{name}(") || key.StartsWith($"Color3F.{name}(")
                                                          || key.StartsWith($"ColorBGRA.{name}(")),
            "used nowhere, not even by the old types themselves - not carried over");
        map.NotMapped(key => key is "Color4F.ToBgra(this Byte&,Byte&,Byte&,Byte&)" or "ColorBGRA.GetBrightness(this )"
                          or "ColorBGRA.GetHue(this )" or "ColorBGRA.GetSaturation(this )",
            "used nowhere, not even by the old types themselves - not carried over");
        map.ChangedWhere(key => key is "Color4F.ToRgba(this )" or "Color4F.ToBgra(this )" or "Color4F.op_Explicit(Color4F)"
                                    or "Color3F.ToRgba(this )" or "Color3F.ToBgra(this )" or "Color3F.op_Explicit(Color3F)"
                                    or "ColorBGRA.op_Explicit(Vector4F)" or "ColorBGRA.op_Explicit(Color4F)" or "ColorBGRA.op_Explicit(Color3F)"
                                    or $"{C}.op_Explicit(Color4F)",
            inputs => Array.Exists(inputs, value => value is < 0 or > 1 || value * 255 != Math.Round(value * 255)),
            "old bug: a fraction was cut down to a byte, and one outside 0 to 1 wrapped round it - 1.5 came out a dim " +
            "127; UNORM clamps and rounds to the nearest");
        map.ExactWhereOldWasNear("Color4F", (a, b) => Color4F.NearEqual(Read4(new GoldenInputs(a)), Read4(new GoldenInputs(b))));
        map.ExactWhereOldWasNear("Color3F", (a, b) => Color3F.NearEqual(Read3(new GoldenInputs(a)), Read3(new GoldenInputs(b))));
        map.NotMapped("ColorBGRA.op_Explicit(Vector3F)",
            "old bug: the fractions were divided by 255 instead of taken times it - every color came out black");
        map.ChangedWhere(key => key == "ColorBGRA.op_Explicit(ColorBGRA)", inputs => Array.Exists(inputs, value => value > 0),
            "old bug: as a Color3F it gave the bytes themselves, not fractions of 255; the other conversions under this " +
            "name are checked by ToVector3, ToVector4 and ToBgra");
        map.NotMapped(key => key.StartsWith("ColorBGRA.Lerp("),
            "old bug as Color's: components cut down and wrapped round the byte; a BGRA color blends as its Color does");
    }

    private static void RegisterFractions(GoldenMap map)
    {
        map.Calls($"{C}.ToColor4(this )", i => Self(Read(i), x => x.ToColor4()));
        map.Returns($"{C}.op_Implicit(Color)", i => (Color4F)Read(i));
        map.Returns($"{C}.op_Explicit(Color4F)", i => (Color)Read4(i));

        map.Calls("Color4F.ToRgba(this )", i => Self(Read4(i), x => unchecked((int)x.ToRgba())));
        map.Calls("Color4F.ToBgra(this )", i => Self(Read4(i), x => unchecked((int)x.ToBgra())));
        map.Calls("Color4F.ToVector3(this )", i => Self(Read4(i), x => x.ToVector3()));
        map.Calls("Color4F.ToVector4(this )", i => Self(Read4(i), x => x.ToVector4()));
        map.Returns("Color4F.Lerp(Color4F,Color4F,Single)", i => Color4F.Lerp(Read4(i), Read4(i), i.Number<float>()));
        map.Calls("Color4F.Equals(this Color4F)", i =>
        {
            var x = Read4(i);
            return [x.Equals(Read4(i)), x];
        });
        map.Returns("Color4F.op_Equality(Color4F,Color4F)", i => Read4(i) == Read4(i));
        map.Returns("Color4F.op_Inequality(Color4F,Color4F)", i => Read4(i) != Read4(i));
        map.Returns("Color3F.op_Explicit(Color3F)", i => new Color4F(Read3(i).ToVector3(), 1f));
        map.Returns("Color4F.op_Explicit(Color4F)", i => Read4(i).ToVector3());
        map.Returns("Color4F.op_Explicit(Color4F)", i => Read4(i).ToVector4());
        map.Returns("Color4F.op_Explicit(Color4F)", i => unchecked((int)Read4(i).ToRgba()));
        map.Returns("Color4F.op_Implicit(Color4F)", i => Read4(i).ToVector4());
        map.Returns("Color4F.op_Implicit(Color4F)", i => Read4(i).ToVector3());
        map.Returns("Color4F.op_Explicit(Vector3F)", i => new Color4F(i.Vector3<float>(), 1f));
        map.Returns("Color4F.op_Explicit(Vector4F)", i => new Color4F(i.Vector4<float>()));
        map.Returns("Color4F.op_Explicit(Int32)", i => (Color4F)Color.FromRgba(unchecked((uint)i.Int())));

        map.Calls("Color3F.ToRgba(this )", i => Self(Read3(i), x => unchecked((int)x.ToRgba())));
        map.Calls("Color3F.ToBgra(this )", i => Self(Read3(i), x => unchecked((int)x.ToBgra())));
        map.Calls("Color3F.ToVector3(this )", i => Self(Read3(i), x => x.ToVector3()));
        map.Returns("Color3F.Lerp(Color3F,Color3F,Single)", i => Color3F.Lerp(Read3(i), Read3(i), i.Number<float>()));
        map.Calls("Color3F.Equals(this Color3F)", i =>
        {
            var x = Read3(i);
            return [x.Equals(Read3(i)), x];
        });
        map.Returns("Color3F.op_Equality(Color3F,Color3F)", i => Read3(i) == Read3(i));
        map.Returns("Color3F.op_Inequality(Color3F,Color3F)", i => Read3(i) != Read3(i));
        map.Returns("Color3F.op_Explicit(Color3F)", i => unchecked((int)Read3(i).ToRgba()));
        map.Returns("Color3F.op_Explicit(Color3F)", i => Read3(i).ToVector3());
        map.Returns("Color3F.op_Implicit(Color3F)", i => Read3(i).ToVector3());
        map.Returns("Color3F.op_Implicit(Vector3F)", i => new Color3F(i.Vector3<float>()));
        map.Returns("Color3F.op_Explicit(Int32)", i => new Color3F(Color.FromRgba(unchecked((uint)i.Int())).ToVector3()));
    }

    private static void RegisterBgra(GoldenMap map)
    {
        map.Calls("ColorBGRA.ToBgra(this )", i => Self(ReadBgra(i), x => unchecked((int)x.ToBgra())));
        map.Calls("ColorBGRA.ToRgba(this )", i => Self(ReadBgra(i), x => unchecked((int)x.ToRgba())));
        map.Calls("ColorBGRA.ToVector3(this )", i => Self(ReadBgra(i), x => x.ToVector3()));
        map.Calls("ColorBGRA.ToVector4(this )", i => Self(ReadBgra(i), x => x.ToVector4()));
        map.Calls("ColorBGRA.ToColor3(this )", i => Self(ReadBgra(i), x => new Color3F(x.ToVector3())));
        map.Returns("ColorBGRA.FromBgra(Int32)", i => ColorBGRA.FromBgra(unchecked((uint)i.Int())));
        map.Returns("ColorBGRA.FromBgra(UInt32)", i => ColorBGRA.FromBgra(i.Number<uint>()));
        map.Returns("ColorBGRA.FromRgba(Int32)", i => ColorBGRA.FromRgba(unchecked((uint)i.Int())));
        map.Returns("ColorBGRA.FromRgba(UInt32)", i => ColorBGRA.FromRgba(i.Number<uint>()));
        map.Calls("ColorBGRA.Equals(this ColorBGRA)", i =>
        {
            var x = ReadBgra(i);
            return [x.Equals(ReadBgra(i)), x];
        });
        map.Returns("ColorBGRA.op_Equality(ColorBGRA,ColorBGRA)", i => ReadBgra(i) == ReadBgra(i));
        map.Returns("ColorBGRA.op_Inequality(ColorBGRA,ColorBGRA)", i => ReadBgra(i) != ReadBgra(i));
        map.Returns("ColorBGRA.op_Implicit(Color)", i => (ColorBGRA)Read(i));
        map.Returns("ColorBGRA.op_Implicit(ColorBGRA)", i => (Color)ReadBgra(i));
        map.Returns("ColorBGRA.op_Explicit(ColorBGRA)", i => unchecked((int)ReadBgra(i).ToBgra()));
        map.Returns("ColorBGRA.op_Explicit(ColorBGRA)", i => ReadBgra(i).ToVector3());
        map.Returns("ColorBGRA.op_Explicit(ColorBGRA)", i => ReadBgra(i).ToVector4());
        map.Returns("ColorBGRA.op_Explicit(ColorBGRA)", i => new Color3F(ReadBgra(i).ToVector3()));
        map.Returns("ColorBGRA.op_Explicit(ColorBGRA)", i => new Color4F(ReadBgra(i).ToVector4()));
        map.Returns("ColorBGRA.op_Explicit(Color3F)", i => (ColorBGRA)new Color(i.Vector3<float>()));
        map.Returns("ColorBGRA.op_Explicit(Vector4F)", i => (ColorBGRA)new Color(i.Vector4<float>()));
        map.Returns("ColorBGRA.op_Explicit(Color4F)", i => (ColorBGRA)new Color(i.Vector4<float>()));
        map.Returns("ColorBGRA.op_Explicit(Int32)", i => ColorBGRA.FromBgra(unchecked((uint)i.Int())));
    }

    private static void RegisterBool4(GoldenMap map)
    {
        map.Calls("Bool4.get_X(this )", i => Raw(i, x => x.X));
        map.Calls("Bool4.get_Y(this )", i => Raw(i, x => x.Y));
        map.Calls("Bool4.get_Z(this )", i => Raw(i, x => x.Z));
        map.Calls("Bool4.get_W(this )", i => Raw(i, x => x.W));
        map.Calls("Bool4.Equals(this Bool4)", i =>
        {
            var (x, raw) = ReadBool4(i);
            return [x.Equals(ReadBool4(i).Value), .. raw];
        });
        map.Calls("Bool4.Equals(this Bool4&)", i =>
        {
            var (x, raw) = ReadBool4(i);
            var (other, otherRaw) = ReadBool4(i);
            return [x.Equals(other), .. otherRaw, .. raw];
        });
        map.Returns("Bool4.op_Equality(Bool4,Bool4)", i => ReadBool4(i).Value == ReadBool4(i).Value);
        map.Returns("Bool4.op_Inequality(Bool4,Bool4)", i => ReadBool4(i).Value != ReadBool4(i).Value);
    }

    private static object[] Raw(GoldenInputs inputs, Func<Bool4, bool> answer)
    {
        var (value, raw) = ReadBool4(inputs);
        return [answer(value), .. raw];
    }

    private static Color4F Read4(GoldenInputs inputs) => new(inputs.Vector4<float>());

    private static Color3F Read3(GoldenInputs inputs) => new(inputs.Vector3<float>());

    private static ColorBGRA ReadBgra(GoldenInputs inputs)
    {
        var (blue, green, red, alpha) = (Byte(inputs), Byte(inputs), Byte(inputs), Byte(inputs));
        return new ColorBGRA(red, green, blue, alpha);
    }

    private static (Bool4 Value, object[] Raw) ReadBool4(GoldenInputs inputs)
    {
        var (x, y, z, w) = (inputs.Int(), inputs.Int(), inputs.Int(), inputs.Int());
        return (new Bool4(x != 0, y != 0, z != 0, w != 0), [x, y, z, w]);
    }

    private static void RegisterColor(GoldenMap map)
    {
        map.Calls($"{C}.ToRgba(this )", i => Self(Read(i), x => unchecked((int)x.ToRgba())));
        map.Calls($"{C}.ToBgra(this )", i => Self(Read(i), x => unchecked((int)x.ToBgra())));
        map.Calls($"{C}.ToArgb(this )", i => Self(Read(i), x => unchecked((int)x.ToArgb())));
        map.Calls($"{C}.ToAbgr(this )", i => Self(Read(i), x => unchecked((int)x.ToAbgr())));
        map.Calls($"{C}.ToVector3(this )", i => Self(Read(i), x => x.ToVector3()));
        map.Calls($"{C}.ToVector4(this )", i => Self(Read(i), x => x.ToVector4()));
        map.Returns($"{C}.op_Explicit(Color)", i => unchecked((int)Read(i).ToRgba()));
        map.Returns($"{C}.op_Explicit(Color)", i => Read(i).ToVector3());
        map.Returns($"{C}.op_Explicit(Color)", i => Read(i).ToVector4());
        map.Returns($"{C}.op_Explicit(Int32)", i => Color.FromRgba(unchecked((uint)i.Int())));
        map.Returns($"{C}.op_Explicit(Vector3F)", i => new Color(i.Vector3<float>()));
        map.Returns($"{C}.op_Explicit(Vector4F)", i => new Color(i.Vector4<float>()));
        map.Returns($"{C}.FromRgba(Byte,Byte,Byte,Byte)", i => Color.FromRgba(Byte(i), Byte(i), Byte(i), Byte(i)));
        map.Returns($"{C}.FromRgba(Byte,Byte,Byte)", i => Color.FromRgba(Byte(i), Byte(i), Byte(i)));
        map.Returns($"{C}.FromRgba(Int32)", i => Color.FromRgba(unchecked((uint)i.Int())));
        map.Returns($"{C}.FromRgba(UInt32)", i => Color.FromRgba(i.Number<uint>()));
        map.Returns($"{C}.FromBgra(Int32)", i => Color.FromBgra(unchecked((uint)i.Int())));
        map.Returns($"{C}.FromBgra(UInt32)", i => Color.FromBgra(i.Number<uint>()));
        map.NotMapped(key => key.StartsWith($"{C}.FromArgb("),
            "old bug: it read the bytes as red, green, blue, alpha - not the inverse of ToArgb; TypedColorTests checks " +
            "each From is the inverse of its To");
        map.Returns($"{C}.FromAbgr(Int32)", i => Color.FromAbgr(unchecked((uint)i.Int())));
        map.Returns($"{C}.FromAbgr(UInt32)", i => Color.FromAbgr(i.Number<uint>()));
        map.NotMapped(key => key.StartsWith($"{C}.Lerp("),
            "old bug: each component was cut down instead of rounded, and past the ends it wrapped round the byte - a " +
            "step beyond black came out bright; now rounded and clamped, TypedColorTests checks it");
        map.ChangedWhere(key => key is $"{C}.op_Explicit(Vector3F)" or $"{C}.op_Explicit(Vector4F)",
            inputs => Array.Exists(inputs, value => value is > 0 and < 1),
            "old bug: a fraction was cut down to a byte; UNORM rounds it to the nearest, so a byte goes through a " +
            "fraction and back unchanged");
        map.NotMapped($"{C}.ToByte(Int32)", "a number clamped to a byte - byte.CreateSaturating does it; its one consumer moves to it in Ф6");
        map.Calls($"{C}.Equals(this Color)", i =>
        {
            var x = Read(i);
            return [x.Equals(Read(i)), x];
        });
        map.Returns($"{C}.op_Equality(Color,Color)", i => Read(i) == Read(i));
        map.Returns($"{C}.op_Inequality(Color,Color)", i => Read(i) != Read(i));
        map.NotMapped(key => key is $"{C}.ToRgb(this )" or $"{C}.GetBrightness(this )" or $"{C}.GetHue(this )" or $"{C}.GetSaturation(this )"
                          || Array.Exists(Unused, name => key.StartsWith($"{C}.{name}(")),
            "used nowhere, not even by the old types themselves - not carried over");
    }

    private static readonly string[] Unused =
    [
        "Add", "Subtract", "Modulate", "Scale", "Negate", "Clamp", "Premultiply", "SmoothStep", "Max", "Min", "AdjustContrast",
        "AdjustSaturation", "op_Addition", "op_UnaryPlus", "op_Subtraction", "op_UnaryNegation", "op_Multiply"
    ];

    private static Color Read(GoldenInputs inputs) => new(Byte(inputs), Byte(inputs), Byte(inputs), Byte(inputs));

    private static byte Byte(GoldenInputs inputs) => inputs.Number<byte>();

    private static object[] Self<TSelf, TResult>(TSelf self, Func<TSelf, TResult> answer) => [answer(self), self];
}
