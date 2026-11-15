using System;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>A compression context of ECMA-363 clause 10: static (values of a range equally likely) or dynamic (learns
/// its odds, shared by every field of its name).</summary>
internal readonly struct U3dContext
{
    /// <summary>The largest range a static context is coded in; a value of a wider one is written uncompressed.</summary>
    internal const uint MaximumStaticRange = 0x3FFE;

    private U3dContext(string name, uint range)
    {
        Name = name;
        Range = range;
    }

    /// <summary>The name of a dynamic context, as the spec spells it (<c>cShading</c>); null for a static one.</summary>
    internal string Name { get; }

    /// <summary>How many values a static context holds; 0 for a dynamic one.</summary>
    internal uint Range { get; }

    /// <summary>Whether the context learns from its values.</summary>
    internal bool IsDynamic => Name != null;

    /// <summary>Creates the dynamic context of the name.</summary>
    internal static U3dContext CreateDynamic(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new U3dContext(name, 0);
    }

    /// <summary>Creates the static context of values 0 to <paramref name="range"/> - 1.</summary>
    internal static U3dContext CreateStatic(uint range) => new(null, range);

    /// <inheritdoc />
    public override string ToString() => IsDynamic ? Name : $"r{Range}";
}
