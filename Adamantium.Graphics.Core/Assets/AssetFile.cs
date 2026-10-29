using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MessagePack;
using MessagePack.Resolvers;

namespace Adamantium.Graphics.Core.Assets;

/// <summary>A cooked asset file (.aemf): a header with its format version and a table of sections, then the sections -
/// <see cref="Thumbnail"/>, <see cref="Source"/> and <see cref="Body"/> - each read without the others.</summary>
public sealed class AssetFile
{
    /// <summary>The version of the format this engine writes.</summary>
    public const ushort FormatVersion = 1;

    /// <summary>The oldest version this engine still reads: the one before <see cref="FormatVersion"/> once there is one.
    /// A file of a version outside them is refused with <see cref="AssetFormatException.WrittenVersion"/> set.</summary>
    public const ushort OldestReadableVersion = 1;

    private const int SectionEntrySize = sizeof(int) + sizeof(long) + sizeof(long);

    private static readonly byte[] Signature = "AEMF"u8.ToArray();

    private static readonly MessagePackSerializerOptions SourceOptions =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    /// <summary>The format version the file was read in; <see cref="FormatVersion"/> for one made here.</summary>
    public ushort Version { get; private set; } = FormatVersion;

    /// <summary>A PNG picture of the asset; null when it has none.</summary>
    public byte[] Thumbnail { get; set; }

    /// <summary>Where the asset was imported from; null when that is not known.</summary>
    public AssetSource Source { get; set; }

    /// <summary>The asset itself, as its writer gave it.</summary>
    public byte[] Body { get; set; }

    /// <summary>Reads the <paramref name="sections"/> of the file at <paramref name="path"/>; the others stay null.
    /// Throws <see cref="AssetFormatException"/> for a file this engine does not read.</summary>
    public static AssetFile Read(string path, AssetFileSection sections)
    {
        using var stream = File.OpenRead(path);
        try
        {
            return Read(stream, sections);
        }
        catch (AssetFormatException exception)
        {
            throw new AssetFormatException($"{path}: {exception.Message}", exception);
        }
    }

    /// <summary>Reads the <paramref name="sections"/> of the asset file that starts at the stream's position; the
    /// others stay null. The stream must seek.</summary>
    public static AssetFile Read(Stream stream, AssetFileSection sections)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var start = stream.Position;
        var size = stream.Length - start;
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);

        if (size < GetHeaderSize(0) || !reader.ReadBytes(Signature.Length).AsSpan().SequenceEqual(Signature))
        {
            throw new AssetFormatException("not an asset file of this engine; import its source again");
        }

        var version = reader.ReadUInt16();
        if (version is < OldestReadableVersion or > FormatVersion)
        {
            var readable = OldestReadableVersion == FormatVersion
                ? $"{FormatVersion}"
                : $"{OldestReadableVersion} to {FormatVersion}";
            throw new AssetFormatException(
                $"written in format version {version}, this engine reads {readable}; import its source again", version);
        }

        var count = reader.ReadUInt16();
        if (size < GetHeaderSize(count))
        {
            throw CreateCutShortException();
        }

        List<(AssetFileSection Section, long Offset, long Length)> entries = [];
        for (var i = 0; i < count; i++)
        {
            entries.Add(((AssetFileSection)reader.ReadInt32(), reader.ReadInt64(), reader.ReadInt64()));
        }

        var file = new AssetFile { Version = version };
        var read = AssetFileSection.None;
        foreach (var (section, offset, length) in entries)
        {
            if (!IsKnown(section) || (sections & section) == 0)
            {
                continue;
            }

            if ((read & section) != 0)
            {
                throw new AssetFormatException($"the section {section} is given twice; import its source again");
            }

            if (offset < GetHeaderSize(count) || length < 0 || length > int.MaxValue || offset > size - length)
            {
                throw CreateCutShortException();
            }

            stream.Position = start + offset;
            var data = reader.ReadBytes((int)length);
            if (data.Length != length)
            {
                throw CreateCutShortException();
            }

            read |= section;
            switch (section)
            {
                case AssetFileSection.Thumbnail:
                    file.Thumbnail = data;
                    break;
                case AssetFileSection.Source:
                    file.Source = DeserializeSource(data);
                    break;
                case AssetFileSection.Body:
                    file.Body = data;
                    break;
            }
        }

        return file;
    }

    /// <summary>Writes the file at the stream's position; a section that is null is left out.</summary>
    public void WriteTo(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        List<(AssetFileSection Section, byte[] Data)> sections = [];
        AddSection(sections, AssetFileSection.Thumbnail, Thumbnail);
        AddSection(sections, AssetFileSection.Source,
            Source == null ? null : MessagePackSerializer.Serialize(Source, SourceOptions));
        AddSection(sections, AssetFileSection.Body, Body);

        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(Signature);
        writer.Write(FormatVersion);
        writer.Write((ushort)sections.Count);

        var offset = GetHeaderSize(sections.Count);
        foreach (var (section, data) in sections)
        {
            writer.Write((int)section);
            writer.Write(offset);
            writer.Write((long)data.Length);
            offset += data.Length;
        }

        foreach (var (_, data) in sections)
        {
            writer.Write(data);
        }
    }

    private static void AddSection(List<(AssetFileSection Section, byte[] Data)> sections, AssetFileSection section,
        byte[] data)
    {
        if (data != null)
        {
            sections.Add((section, data));
        }
    }

    private static bool IsKnown(AssetFileSection section)
    {
        return section is AssetFileSection.Thumbnail or AssetFileSection.Source or AssetFileSection.Body;
    }

    private static long GetHeaderSize(int sectionCount)
    {
        return Signature.Length + sizeof(ushort) + sizeof(ushort) + (long)sectionCount * SectionEntrySize;
    }

    private static AssetSource DeserializeSource(byte[] data)
    {
        AssetSource source;
        try
        {
            source = MessagePackSerializer.Deserialize<AssetSource>(data, SourceOptions);
        }
        catch (MessagePackSerializationException exception)
        {
            throw new AssetFormatException("its import source cannot be read; import its source again", exception);
        }

        if (source != null)
        {
            source.Parameters ??= new();
            source.Companions ??= [];
        }

        return source;
    }

    private static AssetFormatException CreateCutShortException()
    {
        return new AssetFormatException("the file is cut short; import its source again");
    }
}
