namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>A key/value pair of a block's meta data (ECMA-363 9.2.6).</summary>
internal sealed class U3dMetaDataEntry
{
    /// <summary>The formatting bits: 0x1 a binary value, 0x2 hidden, and the viewer hints of 9.2.6.2.</summary>
    internal uint Attributes { get; init; }

    /// <summary>The key, its #attributes as the file writes them.</summary>
    internal string Key { get; init; }

    /// <summary>The value of a string pair; null for a binary one.</summary>
    internal string Value { get; init; }

    /// <summary>The value of a binary pair; null for a string one.</summary>
    internal byte[] BinaryValue { get; init; }

    /// <summary>Whether the value is a binary sequence rather than a string.</summary>
    internal bool IsBinary => (Attributes & 0x1) != 0;
}
