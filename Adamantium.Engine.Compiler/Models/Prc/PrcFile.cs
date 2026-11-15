using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A PRC file as far as the import reads it: file structures with their globals, tree and tessellations, and
/// the model file naming the root occurrences.</summary>
internal sealed class PrcFile
{
    private const int SectionsOfAFileStructure = 6;
    private const int GlobalsSection = 1;
    private const int TreeSection = 2;
    private const int TessellationSection = 3;
    private const int GeometrySection = 4;
    private const int MaximumInflatedBytes = 1 << 30;

    private readonly ImportNotes _notes;
    private readonly List<uint[]> _sectionOffsets = [];
    private readonly Dictionary<PrcUniqueId, PrcFileStructure> _structureById = new();
    private uint _modelFileStart;
    private uint _modelFileEnd;

    private PrcFile(ImportNotes notes)
    {
        _notes = notes;
    }

    internal uint MinimalVersionForRead { get; private set; }

    internal uint AuthoringVersion { get; private set; }

    internal List<PrcFileStructure> FileStructures { get; } = [];

    /// <summary>The root product occurrences of the model file: the file structure each lies in and its index there.</summary>
    internal List<PrcRootReference> Roots { get; } = [];

    internal bool UnitFromCad { get; private set; }

    /// <summary>The model file's unit in millimeters; 0 when it gives none.</summary>
    internal double Unit { get; private set; }

    internal List<byte[]> UncompressedFiles { get; } = [];

    /// <summary>Reads <paramref name="bytes"/>, telling <paramref name="notes"/> what it leaves out; corrupt data throws
    /// <see cref="InvalidDataException"/> naming the section and where in it.</summary>
    internal static PrcFile Read(byte[] bytes, CancellationToken cancellationToken, ImportNotes notes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var file = new PrcFile(notes);
        var header = new PrcHeaderReader(bytes);
        file.ReadHeader(header);
        foreach (var structure in file.FileStructures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            file.ReadFileStructure(bytes, structure);
        }

        cancellationToken.ThrowIfCancellationRequested();
        file.ReadModelFile(bytes);
        return file;
    }

    /// <summary>The file structure of <paramref name="id"/>, the first of it when several share it; null for none.</summary>
    internal PrcFileStructure FindStructure(PrcUniqueId id) => id != null && _structureById.TryGetValue(id, out var structure) ? structure : null;

    private void ReadHeader(PrcHeaderReader header)
    {
        if (header.Length < 3 || header.GetByte(0) != 'P' || header.GetByte(1) != 'R' || header.GetByte(2) != 'C')
        {
            throw new InvalidDataException("This is not a PRC file: it does not start with \"PRC\"");
        }

        header.Position = 3;
        MinimalVersionForRead = header.ReadUnsignedInteger("the minimal version for read");
        AuthoringVersion = header.ReadUnsignedInteger("the authoring version");
        if (MinimalVersionForRead > PrcSchema.ReaderVersion)
        {
            throw new InvalidDataException(
                $"The PRC file needs a reader of version {MinimalVersionForRead}; this one reads version {PrcSchema.ReaderVersion}");
        }

        header.ReadUniqueId("the file's identifier");
        header.ReadUniqueId("the application's identifier");
        var count = header.ReadUnsignedInteger("the count of file structures");
        header.CheckCount(count, 24, "file structures");
        for (var index = 0; index < count; index++)
        {
            var id = header.ReadUniqueId("a file structure's identifier");
            header.ReadUnsignedInteger("a reserved value");
            var sections = header.ReadUnsignedInteger("a file structure's count of sections");
            header.CheckCount(sections, 4, "section offsets");
            var offsets = new uint[sections];
            for (var section = 0; section < sections; section++)
            {
                offsets[section] = header.ReadUnsignedInteger("a section offset");
            }

            if (sections < SectionsOfAFileStructure)
            {
                throw new InvalidDataException($"PRC file structure {index} has {sections} sections, not the six it needs");
            }

            _sectionOffsets.Add(offsets);
            var structure = new PrcFileStructure { Index = index, Id = id };
            FileStructures.Add(structure);
            _structureById.TryAdd(id, structure);
        }

        _modelFileStart = header.ReadUnsignedInteger("the model file's start");
        _modelFileEnd = header.ReadUnsignedInteger("the model file's end");
        var files = header.ReadUnsignedInteger("the count of uncompressed files");
        header.CheckCount(files, 4, "uncompressed files");
        for (var index = 0; index < files; index++)
        {
            var size = header.ReadUnsignedInteger("an uncompressed file's size");
            UncompressedFiles.Add(header.ReadBytes(size, "an uncompressed file"));
        }
    }

    private void ReadFileStructure(byte[] bytes, PrcFileStructure structure)
    {
        var offsets = _sectionOffsets[structure.Index];
        var name = $"file structure {structure.Index}";
        var header = new PrcHeaderReader(bytes) { Position = (int)Math.Min(offsets[0], int.MaxValue) };
        if (header.Position + 3 > header.Length || header.GetByte(header.Position) != 'P')
        {
            throw new InvalidDataException($"The header of PRC {name} at byte {offsets[0]} does not start with \"PRC\"");
        }

        header.Position += 3;
        structure.MinimalVersionForRead = header.ReadUnsignedInteger($"the minimal version of {name}");
        structure.AuthoringVersion = header.ReadUnsignedInteger($"the authoring version of {name}");
        if (structure.MinimalVersionForRead > PrcSchema.ReaderVersion)
        {
            throw new InvalidDataException(
                $"PRC {name} needs a reader of version {structure.MinimalVersionForRead}; this one reads version {PrcSchema.ReaderVersion}");
        }

        header.ReadUniqueId($"the identifier of {name}");
        header.ReadUniqueId($"the application's identifier of {name}");
        var files = header.ReadUnsignedInteger($"the count of uncompressed files of {name}");
        header.CheckCount(files, 4, $"uncompressed files of {name}");
        for (var index = 0; index < files; index++)
        {
            var size = header.ReadUnsignedInteger($"an uncompressed file's size in {name}");
            structure.UncompressedFiles.Add(header.ReadBytes(size, $"an uncompressed file of {name}"));
        }

        var globalsBits = Inflate(bytes, offsets, GlobalsSection, $"globals of {name}");
        PrcGlobalsReader.Read(globalsBits, structure, _notes);
        PrcTreeReader.Read(Inflate(bytes, offsets, TreeSection, $"tree of {name}"), structure, _notes);
        PrcTessellationReader.Read(Inflate(bytes, offsets, TessellationSection, $"tessellation of {name}"), structure, _notes);
        var geometry = Inflate(bytes, offsets, GeometrySection, $"geometry of {name}");
        var reader = new PrcEntityReader(geometry, structure.Schema, _notes);
        reader.Expect(PrcTypes.FileStructureGeometry, "the geometry section");
        reader.ReadContentPrcBase(PrcTypes.FileStructureGeometry);
        structure.TopologicalContextCount = (int)geometry.ReadUnsignedInteger();
        if (structure.TopologicalContextCount > 0)
        {
            _notes.Remark("exact geometry - B-rep topology, curves and surfaces of the geometry section - is not read; " +
                        "a representation item shows its tessellation only");
        }
    }

    private void ReadModelFile(byte[] bytes)
    {
        var bits = InflateRange(bytes, _modelFileStart, _modelFileEnd, "model file");
        var schema = PrcSchema.Read(bits);
        var reader = new PrcEntityReader(bits, schema, _notes);
        reader.Expect(PrcTypes.ModelFile, "the model file");
        reader.ReadContentPrcBase(PrcTypes.ModelFile);
        UnitFromCad = bits.ReadBoolean();
        Unit = bits.ReadDouble();
        var roots = bits.ReadCount(6, "root product occurrences");
        for (var index = 0; index < roots; index++)
        {
            var id = reader.ReadCompressedUniqueId();
            var occurrence = (int)bits.ReadUnsignedInteger() - 1;
            bits.ReadBoolean();
            Roots.Add(new PrcRootReference(id, occurrence));
        }

        for (var index = 0; index < FileStructures.Count; index++)
        {
            bits.ReadUnsignedInteger();
        }

        reader.SkipNewData(PrcTypes.ModelFile);
        reader.SkipUserData();
        reader.CheckEnd();
    }

    private static PrcBitReader Inflate(byte[] bytes, uint[] offsets, int section, string name)
    {
        var start = offsets[section];
        var end = section + 1 < offsets.Length ? offsets[section + 1] : (uint)bytes.Length;
        return InflateRange(bytes, start, end, name);
    }

    private static PrcBitReader InflateRange(byte[] bytes, uint start, uint end, string name)
    {
        if (start > end || end > bytes.Length)
        {
            throw new InvalidDataException($"The PRC {name} lies at bytes {start} to {end}, outside the file's {bytes.Length}");
        }

        var isTooLarge = false;
        byte[] inflated;
        try
        {
            using var input = new ZLibStream(new MemoryStream(bytes, (int)start, (int)(end - start), false), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaximumInflatedBytes)
                {
                    isTooLarge = true;
                    break;
                }

                output.Write(buffer, 0, read);
            }

            inflated = output.ToArray();
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"The PRC {name} at byte {start} does not inflate: {exception.Message}", exception);
        }

        if (isTooLarge)
        {
            throw new InvalidDataException($"The PRC {name} inflates past {MaximumInflatedBytes} bytes");
        }

        return new PrcBitReader(inflated, name);
    }
}
