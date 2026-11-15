namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>A texture layer of a lit texture shader (ECMA-363 9.8.3.10).</summary>
internal sealed class U3dTextureLayer
{
    internal string TextureName { get; init; }

    internal float Intensity { get; init; }

    internal byte BlendFunction { get; init; }

    internal byte TextureMode { get; init; }

    internal Matrix4x4F TextureTransform { get; init; }

    internal byte Repeat { get; init; }
}
