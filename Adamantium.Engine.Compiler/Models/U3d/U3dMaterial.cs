namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>A material resource (ECMA-363 9.8.4).</summary>
internal sealed class U3dMaterial
{
    internal const uint AmbientFlag = 0x1;
    internal const uint DiffuseFlag = 0x2;
    internal const uint SpecularFlag = 0x4;
    internal const uint EmissiveFlag = 0x8;
    internal const uint ReflectivityFlag = 0x10;
    internal const uint OpacityFlag = 0x20;

    internal string Name { get; init; }

    /// <summary>Which of the values are enabled, one flag each.</summary>
    internal uint Attributes { get; init; }

    internal Vector3F AmbientColor { get; init; }

    internal Vector3F DiffuseColor { get; init; }

    internal Vector3F SpecularColor { get; init; }

    internal Vector3F EmissiveColor { get; init; }

    internal float Reflectivity { get; init; }

    internal float Opacity { get; init; }
}
