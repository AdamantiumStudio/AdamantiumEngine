using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A PRC file (PRC Format Specification 8137) as far as the import reads it: the header with the offsets of
/// every section, each file structure's schema, globals, tree and tessellations, and the model file naming the root
/// occurrences. Each section is inflated (zlib) on its own and read bit by bit. The exact geometry and extra geometry
/// sections - B-rep topology, curves and surfaces - are not read: only their counts are looked at, and what lives there
/// is told. Header data is uncompressed, little-endian 32-bit.</summary>
internal sealed class PrcFile
{
    private const int SectionsOfAFileStructure = 6;
    private const int GlobalsSection = 1;
    private const int TreeSection = 2;
    private const int TessellationSection = 3;
    private const int GeometrySection = 4;
    private const int MaximumInflatedBytes = 1 << 30;

    private readonly PrcNotes notes = new();
    private readonly List<uint[]> sectionOffsets = [];
    private uint modelFileStart;
    private uint modelFileEnd;

    private PrcFile()
    {
    }

    internal uint MinimalVersionForRead { get; private set; }

    internal uint AuthoringVersion { get; private set; }

    internal List<PrcFileStructure> FileStructures { get; } = [];

    /// <summary>The root product occurrences of the model file: the file structure each lies in and its index there.</summary>
    internal List<PrcRootReference> Roots { get; } = [];

    internal bool UnitFromCad { get; private set; }

    /// <summary>The model file's unit in millimetres; 0 when it gives none.</summary>
    internal double Unit { get; private set; }

    internal List<byte[]> UncompressedFiles { get; } = [];

    internal IReadOnlyList<string> Notes => notes.All;

    /// <summary>Reads <paramref name="bytes"/>; corrupt data throws <see cref="InvalidDataException"/> naming the
    /// section and where in it.</summary>
    internal static PrcFile Read(byte[] bytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var file = new PrcFile();
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

    private void ReadHeader(PrcHeaderReader header)
    {
        if (header.Length < 3 || header.Byte(0) != 'P' || header.Byte(1) != 'R' || header.Byte(2) != 'C')
        {
            throw new InvalidDataException("This is not a PRC file: it does not start with \"PRC\"");
        }

        header.Position = 3;
        MinimalVersionForRead = header.UnsignedInteger("the minimal version for read");
        AuthoringVersion = header.UnsignedInteger("the authoring version");
        if (MinimalVersionForRead > PrcSchema.ReaderVersion)
        {
            throw new InvalidDataException(
                $"The PRC file needs a reader of version {MinimalVersionForRead}; this one reads version {PrcSchema.ReaderVersion}");
        }

        header.UniqueId("the file's identifier");
        header.UniqueId("the application's identifier");
        var count = header.UnsignedInteger("the count of file structures");
        header.CheckCount(count, 24, "file structures");
        for (var index = 0; index < count; index++)
        {
            var id = header.UniqueId("a file structure's identifier");
            header.UnsignedInteger("a reserved value");
            var sections = header.UnsignedInteger("a file structure's count of sections");
            header.CheckCount(sections, 4, "section offsets");
            var offsets = new uint[sections];
            for (var section = 0; section < sections; section++)
            {
                offsets[section] = header.UnsignedInteger("a section offset");
            }

            if (sections < SectionsOfAFileStructure)
            {
                throw new InvalidDataException($"PRC file structure {index} has {sections} sections, not the six it needs");
            }

            sectionOffsets.Add(offsets);
            FileStructures.Add(new PrcFileStructure { Index = index, Id = id });
        }

        modelFileStart = header.UnsignedInteger("the model file's start");
        modelFileEnd = header.UnsignedInteger("the model file's end");
        var files = header.UnsignedInteger("the count of uncompressed files");
        header.CheckCount(files, 4, "uncompressed files");
        for (var index = 0; index < files; index++)
        {
            var size = header.UnsignedInteger("an uncompressed file's size");
            UncompressedFiles.Add(header.Bytes(size, "an uncompressed file"));
        }
    }

    private void ReadFileStructure(byte[] bytes, PrcFileStructure structure)
    {
        var offsets = sectionOffsets[structure.Index];
        var name = $"file structure {structure.Index}";
        var header = new PrcHeaderReader(bytes) { Position = (int)Math.Min(offsets[0], int.MaxValue) };
        if (header.Position + 3 > header.Length || header.Byte(header.Position) != 'P')
        {
            throw new InvalidDataException($"The header of PRC {name} at byte {offsets[0]} does not start with \"PRC\"");
        }

        header.Position += 3;
        structure.MinimalVersionForRead = header.UnsignedInteger($"the minimal version of {name}");
        structure.AuthoringVersion = header.UnsignedInteger($"the authoring version of {name}");
        if (structure.MinimalVersionForRead > PrcSchema.ReaderVersion)
        {
            throw new InvalidDataException(
                $"PRC {name} needs a reader of version {structure.MinimalVersionForRead}; this one reads version {PrcSchema.ReaderVersion}");
        }

        var globalsBits = Inflate(bytes, offsets, GlobalsSection, $"globals of {name}");
        PrcGlobalsReader.Read(globalsBits, structure, notes);
        PrcTreeReader.Read(Inflate(bytes, offsets, TreeSection, $"tree of {name}"), structure, notes);
        PrcTessellationReader.Read(Inflate(bytes, offsets, TessellationSection, $"tessellation of {name}"), structure, notes);
        var geometry = Inflate(bytes, offsets, GeometrySection, $"geometry of {name}");
        var reader = new PrcEntityReader(geometry, structure.Schema, notes);
        reader.Expect(PrcTypes.FileStructureGeometry, "the geometry section");
        reader.ReadContentPrcBase(PrcTypes.FileStructureGeometry);
        structure.TopologicalContextCount = (int)geometry.ReadUnsignedInteger();
        if (structure.TopologicalContextCount > 0)
        {
            notes.Tell("exact geometry - B-rep topology, curves and surfaces of the geometry section - is not read; " +
                       "a representation item shows its tessellation only");
        }
    }

    private void ReadModelFile(byte[] bytes)
    {
        var bits = InflateRange(bytes, modelFileStart, modelFileEnd, "model file");
        var schema = PrcSchema.Read(bits);
        var reader = new PrcEntityReader(bits, schema, notes);
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

        reader.EndOf(PrcTypes.ModelFile);
        reader.SkipUserData();
        reader.CheckEnd();
    }

    private PrcBitReader Inflate(byte[] bytes, uint[] offsets, int section, string name)
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
                    throw new InvalidDataException($"The PRC {name} inflates past {MaximumInflatedBytes} bytes");
                }

                output.Write(buffer, 0, read);
            }

            return new PrcBitReader(output.ToArray(), name);
        }
        catch (InvalidDataException exception) when (!exception.Message.StartsWith("The PRC", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The PRC {name} at byte {start} does not inflate: {exception.Message}", exception);
        }
    }
}

/// <summary>A root product occurrence of the model file: the file structure it lies in and its index among that file
/// structure's occurrences.</summary>
internal sealed class PrcRootReference
{
    internal PrcRootReference(PrcUniqueId fileStructure, int occurrence)
    {
        FileStructure = fileStructure;
        Occurrence = occurrence;
    }

    internal PrcUniqueId FileStructure { get; }

    internal int Occurrence { get; }
}

/// <summary>Reads the uncompressed header data of a PRC file: 32-bit little-endian unsigned integers and identifiers,
/// each read checked against the end of the file.</summary>
internal sealed class PrcHeaderReader
{
    private readonly byte[] bytes;

    internal PrcHeaderReader(byte[] bytes)
    {
        this.bytes = bytes;
    }

    internal int Position { get; set; }

    internal int Length => bytes.Length;

    internal byte Byte(int at) => bytes[at];

    internal uint UnsignedInteger(string what)
    {
        if (Position < 0 || Position + 4 > bytes.Length)
        {
            throw new InvalidDataException($"The PRC header ends at byte {Position} before {what}");
        }

        var value = BitConverter.ToUInt32(bytes, Position);
        Position += 4;
        return value;
    }

    internal PrcUniqueId UniqueId(string what) =>
        new(UnsignedInteger(what), UnsignedInteger(what), UnsignedInteger(what), UnsignedInteger(what));

    internal byte[] Bytes(uint count, string what)
    {
        CheckCount(count, 1, what);
        var read = bytes.AsSpan(Position, (int)count).ToArray();
        Position += (int)count;
        return read;
    }

    internal void CheckCount(uint count, int bytesEach, string what)
    {
        if ((long)count * bytesEach > bytes.Length - (long)Position)
        {
            throw new InvalidDataException($"The PRC header names {count} {what} at byte {Position}, more than the file holds");
        }
    }
}
