using System.Collections.Generic;

namespace Adamantium.Engine.CompilerTests;

/// <summary>One file structure of a test file: its identifier and its sections' uncompressed bytes.</summary>
internal sealed class PrcTestStructure
{
    public PrcTestStructure(uint number)
    {
        Id = [0x50524331, number, 0, 0x5A000000 + number];
    }

    public uint[] Id { get; }

    public byte[] Globals { get; set; }

    public byte[] Tree { get; set; }

    public byte[] Tessellation { get; set; }

    public byte[] Geometry { get; set; }

    public byte[] ExtraGeometry { get; set; }

    /// <summary>The private files the file structure's header embeds, the pictures' data.</summary>
    public List<byte[]> UncompressedFiles { get; } = [];
}
