using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Adamantium.Engine.Compiler.Models.Prc;

namespace Adamantium.Engine.CompilerTests;

/// <summary>A PRC file for tests, laid out as the specification's SerializePRCHeader and SerializeFileStructure give
/// it: the uncompressed header with every section's offset, then each file structure's header and its five sections,
/// each zlib-compressed on its own, then the model file. Each section's bits come from a <see cref="PrcTestBitWriter"/>.</summary>
internal sealed class PrcTestFile
{
    public uint MinimalVersion { get; init; } = 8137;

    public uint AuthoringVersion { get; init; } = 8137;

    public List<PrcTestStructure> Structures { get; } = [];

    /// <summary>The model file section: its schema, then PRC_TYPE_ASM_ModelFile.</summary>
    public byte[] ModelFile { get; set; }

    /// <summary>A file of one file structure whose root occurrence is the last of its tree, in millimeters unless
    /// <paramref name="unit"/> says otherwise, a unit of a CAD system unless <paramref name="unitFromCad"/> says not.</summary>
    public static PrcTestFile CreateSingle(byte[] globals, byte[] tree, byte[] tessellation, int occurrences, double unit = 1,
        bool unitFromCad = true)
    {
        var file = new PrcTestFile();
        var structure = new PrcTestStructure(1) { Globals = globals, Tree = tree, Tessellation = tessellation };
        file.Structures.Add(structure);
        file.ModelFile = PrcTestEntities.CreateModelFile(structure.Id, occurrences - 1, unit, unitFromCad: unitFromCad);
        return file;
    }

    public static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    public byte[] ToArray()
    {
        List<byte[]> sections = [];
        foreach (var structure in Structures)
        {
            sections.Add(CreateStructureHeader(structure));
            sections.Add(Compress(structure.Globals));
            sections.Add(Compress(structure.Tree));
            sections.Add(Compress(structure.Tessellation));
            sections.Add(Compress(structure.Geometry ?? PrcTestEntities.CreateEmptyGeometry(PrcTypes.FileStructureGeometry)));
            sections.Add(Compress(structure.ExtraGeometry ?? PrcTestEntities.CreateEmptyGeometry(PrcTypes.FileStructureExtraGeometry)));
        }

        var model = Compress(ModelFile);
        var headerLength = 3 + 4 * 2 + 16 * 2 + 4 + Structures.Count * (16 + 4 + 4 + 4 * 6) + 4 * 3;
        using var output = new MemoryStream();
        using var header = new BinaryWriter(output);
        header.Write("PRC"u8.ToArray());
        header.Write(MinimalVersion);
        header.Write(AuthoringVersion);
        WriteId(header, [1, 2, 3, 4]);
        WriteId(header, [0, 0, 0, 0]);
        header.Write((uint)Structures.Count);
        var offset = (uint)headerLength;
        var section = 0;
        foreach (var structure in Structures)
        {
            WriteId(header, structure.Id);
            header.Write(0u);
            header.Write(6u);
            for (var part = 0; part < 6; part++)
            {
                header.Write(offset);
                offset += (uint)sections[section++].Length;
            }
        }

        header.Write(offset);
        header.Write(offset + (uint)model.Length);
        header.Write(0u);
        foreach (var bytes in sections)
        {
            header.Write(bytes);
        }

        header.Write(model);
        return output.ToArray();
    }

    private byte[] CreateStructureHeader(PrcTestStructure structure)
    {
        using var output = new MemoryStream();
        using var header = new BinaryWriter(output);
        header.Write("PRC"u8.ToArray());
        header.Write(MinimalVersion);
        header.Write(AuthoringVersion);
        WriteId(header, structure.Id);
        WriteId(header, [0, 0, 0, 0]);
        header.Write((uint)structure.UncompressedFiles.Count);
        foreach (var embedded in structure.UncompressedFiles)
        {
            header.Write((uint)embedded.Length);
            header.Write(embedded);
        }

        return output.ToArray();
    }

    private static void WriteId(BinaryWriter header, uint[] id)
    {
        foreach (var part in id)
        {
            header.Write(part);
        }
    }
}
