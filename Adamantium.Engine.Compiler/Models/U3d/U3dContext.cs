using System;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>A compression context of ECMA-363 (clause 10): a static one, every value from 0 to one less than its range
/// equally likely (the spec's <c>r</c> contexts), or a dynamic one that learns the odds of its values from those read
/// in it so far, known by its name (the spec's <c>c</c> contexts). Two fields naming the same dynamic context share
/// what it has learned.</summary>
public readonly struct U3dContext
{
    /// <summary>The largest range a static context is coded in; a value of a wider one is written uncompressed.</summary>
    public const uint MaximumStaticRange = 0x3FFE;

    private U3dContext(string name, uint range)
    {
        Name = name;
        Range = range;
    }

    /// <summary>The name of a dynamic context, as the spec spells it (<c>cShading</c>); null for a static one.</summary>
    public string Name { get; }

    /// <summary>How many values a static context holds; 0 for a dynamic one.</summary>
    public uint Range { get; }

    /// <summary>Whether the context learns from its values.</summary>
    public bool IsDynamic => Name != null;

    /// <summary>The dynamic context of the name.</summary>
    public static U3dContext Dynamic(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new U3dContext(name, 0);
    }

    /// <summary>The static context of values 0 to <paramref name="range"/> - 1.</summary>
    public static U3dContext Static(uint range) => new(null, range);

    /// <inheritdoc />
    public override string ToString() => IsDynamic ? Name : $"r{Range}";
}
