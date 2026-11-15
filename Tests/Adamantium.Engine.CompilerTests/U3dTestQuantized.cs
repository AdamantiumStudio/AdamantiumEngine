namespace Adamantium.Engine.CompilerTests;

/// <summary>A value quantized against its prediction: the sign, the difference and what a reader rebuilds.</summary>
internal sealed class U3dTestQuantized
{
    public int SignBit { get; init; }

    public uint Difference { get; init; }

    public float Reconstructed { get; init; }
}
