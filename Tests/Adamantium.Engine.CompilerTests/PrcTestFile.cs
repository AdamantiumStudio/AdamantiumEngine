using System;
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

    /// <summary>A file of one file structure whose root occurrence is the last of its tree, in millimetres.</summary>
    public static PrcTestFile Single(byte[] globals, byte[] tree, byte[] tessellation, int occurrences)
    {
        var file = new PrcTestFile();
        var structure = new PrcTestStructure(1) { Globals = globals, Tree = tree, Tessellation = tessellation };
        file.Structures.Add(structure);
        file.ModelFile = PrcTestEntities.ModelFile(structure.Id, occurrences - 1, 1);
        return file;
    }

    public byte[] ToArray()
    {
        var sections = new List<byte[]>();
        foreach (var structure in Structures)
        {
            sections.Add(StructureHeader(structure));
            sections.Add(Compress(structure.Globals));
            sections.Add(Compress(structure.Tree));
            sections.Add(Compress(structure.Tessellation));
            sections.Add(Compress(structure.Geometry ?? PrcTestEntities.EmptyGeometry(PrcTypes.FileStructureGeometry)));
            sections.Add(Compress(structure.ExtraGeometry ?? PrcTestEntities.EmptyGeometry(PrcTypes.FileStructureExtraGeometry)));
        }

        var model = Compress(ModelFile);
        var headerLength = 3 + 4 * 2 + 16 * 2 + 4 + Structures.Count * (16 + 4 + 4 + 4 * 6) + 4 * 3;
        var output = new MemoryStream();
        var header = new BinaryWriter(output);
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

    private byte[] StructureHeader(PrcTestStructure structure)
    {
        var output = new MemoryStream();
        var header = new BinaryWriter(output);
        header.Write("PRC"u8.ToArray());
        header.Write(MinimalVersion);
        header.Write(AuthoringVersion);
        WriteId(header, structure.Id);
        WriteId(header, [0, 0, 0, 0]);
        header.Write(0u);
        return output.ToArray();
    }

    private static void WriteId(BinaryWriter header, uint[] id)
    {
        foreach (var part in id)
        {
            header.Write(part);
        }
    }

    public static byte[] Compress(byte[] data)
    {
        var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }
}

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
}

/// <summary>Writes the entities of PRC test files as the specification's serialization procedures lay them out:
/// base content, graphics, user data, the globals, the tree, the tessellation section and the model file.</summary>
internal static class PrcTestEntities
{
    public static void ContentPrcBase(PrcTestBitWriter writer, string name, bool referenceable)
    {
        writer.WriteUnsignedInteger(0);
        writer.WriteBoolean(false);
        writer.WriteString(name);
        if (referenceable)
        {
            writer.WriteUnsignedInteger(0);
            writer.WriteUnsignedInteger(0);
            writer.WriteUnsignedInteger(7);
        }
    }

    public static void Graphics(PrcTestBitWriter writer, int style, ushort behavior = 0x0001)
    {
        writer.WriteBoolean(false);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger((uint)(style + 1));
        writer.WriteCharacter((byte)behavior);
        writer.WriteCharacter((byte)(behavior >> 8));
    }

    public static void BaseWithGraphics(PrcTestBitWriter writer, string name, int style, ushort behavior = 0x0001)
    {
        ContentPrcBase(writer, name, true);
        Graphics(writer, style, behavior);
    }

    public static void UserData(PrcTestBitWriter writer) => writer.WriteUnsignedInteger(0);

    /// <summary>A schema: for each entry its type and its tokens.</summary>
    public static void Schema(PrcTestBitWriter writer, IReadOnlyList<PrcTestSchema> schemas)
    {
        writer.WriteUnsignedInteger((uint)schemas.Count);
        foreach (var schema in schemas)
        {
            writer.WriteUnsignedInteger(schema.Type);
            writer.WriteUnsignedInteger((uint)schema.Tokens.Length);
            foreach (var token in schema.Tokens)
            {
                writer.WriteUnsignedInteger(token);
            }
        }
    }

    /// <summary>Globals with the colours given and one plain-colour style per colour, style i of colour i; the schema
    /// first.</summary>
    public static byte[] Globals(IReadOnlyList<double[]> colours, IReadOnlyList<PrcTestSchema> schemas = null,
        Action<PrcTestBitWriter> coordinateSystems = null, int coordinateSystemCount = 0)
    {
        var writer = new PrcTestBitWriter();
        Schema(writer, schemas ?? []);
        writer.WriteUnsignedInteger(PrcTypes.FileStructureGlobals);
        ContentPrcBase(writer, null, false);
        writer.WriteUnsignedInteger(0);
        writer.WriteDouble(0);
        writer.WriteDouble(0);
        writer.WriteString(null);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger((uint)colours.Count);
        foreach (var colour in colours)
        {
            writer.WriteDouble(colour[0]);
            writer.WriteDouble(colour[1]);
            writer.WriteDouble(colour[2]);
        }

        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger((uint)colours.Count);
        for (var colour = 0; colour < colours.Count; colour++)
        {
            writer.WriteUnsignedInteger(PrcTypes.Style);
            ContentPrcBase(writer, $"Style {colour}", true);
            writer.WriteDouble(0);
            writer.WriteBoolean(false);
            writer.WriteUnsignedInteger(0);
            writer.WriteBoolean(false);
            writer.WriteUnsignedInteger((uint)(colour * 3 + 1));
            writer.WriteBoolean(colours[colour].Length > 3);
            if (colours[colour].Length > 3)
            {
                writer.WriteCharacter((byte)colours[colour][3]);
            }

            writer.WriteBoolean(false);
            writer.WriteBoolean(false);
            writer.WriteBoolean(false);
        }

        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger((uint)coordinateSystemCount);
        coordinateSystems?.Invoke(writer);
        UserData(writer);
        return writer.ToArray();
    }

    /// <summary>A tree of the parts and occurrences the writers give, and the file structure's internal data naming
    /// its last occurrence the root.</summary>
    public static byte[] Tree(IReadOnlyList<Action<PrcTestBitWriter>> parts, IReadOnlyList<Action<PrcTestBitWriter>> occurrences,
        PrcTestBitWriter writer = null)
    {
        writer ??= new PrcTestBitWriter();
        writer.WriteUnsignedInteger(PrcTypes.FileStructureTree);
        ContentPrcBase(writer, null, false);
        writer.WriteUnsignedInteger((uint)parts.Count);
        foreach (var part in parts)
        {
            part(writer);
        }

        writer.WriteUnsignedInteger((uint)occurrences.Count);
        foreach (var occurrence in occurrences)
        {
            occurrence(writer);
        }

        writer.WriteUnsignedInteger(PrcTypes.FileStructure);
        ContentPrcBase(writer, null, false);
        writer.WriteUnsignedInteger(100);
        writer.WriteUnsignedInteger((uint)occurrences.Count);
        UserData(writer);
        return writer.ToArray();
    }

    public static void EmptyMarkups(PrcTestBitWriter writer)
    {
        for (var array = 0; array < 4; array++)
        {
            writer.WriteUnsignedInteger(0);
        }
    }

    /// <summary>A part definition of the representation items the writer gives.</summary>
    public static Action<PrcTestBitWriter> Part(string name, int itemCount, Action<PrcTestBitWriter> items,
        Action<PrcTestBitWriter> markups = null, Action<PrcTestBitWriter> beforeUserData = null) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.PartDefinition);
        BaseWithGraphics(writer, name, -1);
        for (var corner = 0; corner < 6; corner++)
        {
            writer.WriteDouble(0);
        }

        writer.WriteUnsignedInteger((uint)itemCount);
        items(writer);
        (markups ?? EmptyMarkups)(writer);
        writer.WriteUnsignedInteger(0);
        beforeUserData?.Invoke(writer);
        UserData(writer);
    };

    /// <summary>A product occurrence of a part (-1 for none) and sons, at a location the writer gives (null for none).</summary>
    public static Action<PrcTestBitWriter> Occurrence(string name, int part, IReadOnlyList<int> sons,
        Action<PrcTestBitWriter> location = null, int prototype = -1, ushort behavior = 0x0001) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.ProductOccurrence);
        BaseWithGraphics(writer, name, -1, behavior);
        writer.WriteUnsignedInteger((uint)(part + 1));
        writer.WriteUnsignedInteger((uint)(prototype + 1));
        if (prototype >= 0)
        {
            writer.WriteBoolean(true);
        }

        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger((uint)sons.Count);
        foreach (var son in sons)
        {
            writer.WriteUnsignedInteger((uint)son);
        }

        writer.WriteCharacter(0);
        writer.WriteBoolean(true);
        writer.WriteDouble(1);
        writer.WriteCharacter(0);
        writer.WriteInteger(3);
        writer.WriteBoolean(location != null);
        location?.Invoke(writer);
        writer.WriteUnsignedInteger(0);
        EmptyMarkups(writer);
        writer.WriteUnsignedInteger(0);
        writer.WriteBoolean(false);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(0);
        UserData(writer);
    };

    /// <summary>A cartesian transformation of a translation and, when given, a rotation's x and y axes.</summary>
    public static Action<PrcTestBitWriter> Cartesian(double[] translation, double[] xAxis = null, double[] yAxis = null) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.CartesianTransformation);
        writer.WriteCharacter((byte)(0x01 | (xAxis != null ? 0x02 : 0)));
        foreach (var value in translation)
        {
            writer.WriteDouble(value);
        }

        if (xAxis != null)
        {
            foreach (var value in xAxis)
            {
                writer.WriteDouble(value);
            }

            foreach (var value in yAxis)
            {
                writer.WriteDouble(value);
            }
        }
    };

    /// <summary>A general transformation: sixteen coefficients, column by column.</summary>
    public static Action<PrcTestBitWriter> General(double[] columns) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.GeneralTransformation);
        foreach (var value in columns)
        {
            writer.WriteDouble(value);
        }
    };

    /// <summary>The content every representation item starts with.</summary>
    public static void ItemContent(PrcTestBitWriter writer, string name, int style, int tessellation, int coordinateSystem = -1,
        ushort behavior = 0x0001)
    {
        BaseWithGraphics(writer, name, style, behavior);
        writer.WriteUnsignedInteger((uint)(coordinateSystem + 1));
        writer.WriteUnsignedInteger((uint)(tessellation + 1));
    }

    public static void PolyBrep(PrcTestBitWriter writer, string name, int style, int tessellation, int coordinateSystem = -1,
        ushort behavior = 0x0001)
    {
        writer.WriteUnsignedInteger(PrcTypes.PolyBrepModel);
        ItemContent(writer, name, style, tessellation, coordinateSystem, behavior);
        writer.WriteBoolean(true);
        UserData(writer);
    }

    public static void PolyWire(PrcTestBitWriter writer, string name, int style, int tessellation)
    {
        writer.WriteUnsignedInteger(PrcTypes.PolyWire);
        ItemContent(writer, name, style, tessellation);
        UserData(writer);
    }

    public static void BrepModel(PrcTestBitWriter writer, string name, int context, int body)
    {
        writer.WriteUnsignedInteger(PrcTypes.BrepModel);
        ItemContent(writer, name, -1, -1);
        writer.WriteBoolean(true);
        writer.WriteUnsignedInteger((uint)context);
        writer.WriteUnsignedInteger((uint)body);
        writer.WriteBoolean(true);
        UserData(writer);
    }

    public static void PointSet(PrcTestBitWriter writer, string name, IReadOnlyList<double[]> points)
    {
        writer.WriteUnsignedInteger(PrcTypes.PointSet);
        ItemContent(writer, name, -1, -1);
        writer.WriteUnsignedInteger((uint)points.Count);
        foreach (var point in points)
        {
            foreach (var value in point)
            {
                writer.WriteDouble(value);
            }
        }

        UserData(writer);
    }

    public static void SetStart(PrcTestBitWriter writer, string name, int count)
    {
        writer.WriteUnsignedInteger(PrcTypes.Set);
        ItemContent(writer, name, -1, -1);
        writer.WriteUnsignedInteger((uint)count);
    }

    public static void SetEnd(PrcTestBitWriter writer) => UserData(writer);

    public static void CoordinateSystem(PrcTestBitWriter writer, Action<PrcTestBitWriter> transformation)
    {
        writer.WriteUnsignedInteger(PrcTypes.CoordinateSystem);
        ItemContent(writer, "System", -1, -1);
        transformation(writer);
        UserData(writer);
    }

    /// <summary>A tessellation section of the entries the writers give.</summary>
    public static byte[] Tessellations(IReadOnlyList<Action<PrcTestBitWriter>> entries, PrcTestBitWriter writer = null)
    {
        writer ??= new PrcTestBitWriter();
        writer.WriteUnsignedInteger(PrcTypes.FileStructureTessellation);
        ContentPrcBase(writer, null, false);
        writer.WriteUnsignedInteger((uint)entries.Count);
        foreach (var entry in entries)
        {
            entry(writer);
        }

        UserData(writer);
        return writer.ToArray();
    }

    public static void Doubles(PrcTestBitWriter writer, IReadOnlyList<double> values)
    {
        writer.WriteUnsignedInteger((uint)values.Count);
        foreach (var value in values)
        {
            writer.WriteDouble(value);
        }
    }

    public static void UnsignedIntegers(PrcTestBitWriter writer, IReadOnlyList<uint> values)
    {
        writer.WriteUnsignedInteger((uint)values.Count);
        foreach (var value in values)
        {
            writer.WriteUnsignedInteger(value);
        }
    }

    /// <summary>A regular 3D tessellation with normals, of one face per entry of <paramref name="faces"/>.</summary>
    public static Action<PrcTestBitWriter> Tessellation3D(IReadOnlyList<double> coordinates, IReadOnlyList<double> normals,
        IReadOnlyList<uint> triangulated, IReadOnlyList<PrcTestFace> faces, IReadOnlyList<double> textures = null) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.Tessellation3D);
        writer.WriteBoolean(false);
        Doubles(writer, coordinates);
        writer.WriteBoolean(true);
        writer.WriteBoolean(false);
        writer.WriteBoolean(normals == null);
        if (normals == null)
        {
            writer.WriteCharacter(0);
            writer.WriteDouble(0.5);
        }

        Doubles(writer, normals ?? []);
        UnsignedIntegers(writer, []);
        UnsignedIntegers(writer, triangulated);
        writer.WriteUnsignedInteger((uint)faces.Count);
        foreach (var face in faces)
        {
            writer.WriteUnsignedInteger(PrcTypes.TessellationFace);
            UnsignedIntegers(writer, face.Style >= 0 ? [(uint)(face.Style + 1)] : []);
            writer.WriteUnsignedInteger(0);
            UnsignedIntegers(writer, []);
            writer.WriteUnsignedInteger(face.UsedEntities);
            writer.WriteUnsignedInteger(face.Start);
            UnsignedIntegers(writer, face.Sizes);
            writer.WriteUnsignedInteger(face.TextureIndexes);
            writer.WriteBoolean(face.Colors != null);
            if (face.Colors != null)
            {
                writer.WriteBoolean(false);
                writer.WriteBoolean(false);
                writer.WriteBoolean(false);
                for (var colour = 0; colour < face.Colors.Count; colour++)
                {
                    if (colour > 0)
                    {
                        writer.WriteBoolean(false);
                    }

                    foreach (var component in face.Colors[colour])
                    {
                        writer.WriteCharacter(component);
                    }
                }
            }

            if (face.Style >= 0)
            {
                writer.WriteUnsignedInteger(1);
            }
        }

        Doubles(writer, textures ?? []);
    };

    public static Action<PrcTestBitWriter> Wire(IReadOnlyList<double> coordinates, IReadOnlyList<uint> wires) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.Tessellation3DWire);
        writer.WriteBoolean(false);
        Doubles(writer, coordinates);
        UnsignedIntegers(writer, wires);
        writer.WriteBoolean(false);
    };

    public static Action<PrcTestBitWriter> Markup(IReadOnlyList<double> coordinates, IReadOnlyList<uint> codes, string label) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.TessellationMarkup);
        writer.WriteBoolean(false);
        Doubles(writer, coordinates);
        UnsignedIntegers(writer, codes);
        writer.WriteUnsignedInteger(1);
        writer.WriteString("Ø 12");
        writer.WriteString(label);
        writer.WriteCharacter(0);
    };

    /// <summary>A highly compressed tessellation's start: its type and a few of its fields, which no reader can step
    /// over.</summary>
    public static Action<PrcTestBitWriter> Compressed() => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.Tessellation3DCompressed);
        writer.WriteBoolean(false);
        writer.WriteBoolean(true);
        writer.WriteDouble(0.01);
    };

    /// <summary>A markup of the part or product, drawn by markup tessellation <paramref name="tessellation"/>.</summary>
    public static Action<PrcTestBitWriter> Markups(string name, int tessellation) => writer =>
    {
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(1);
        writer.WriteUnsignedInteger(PrcTypes.MarkupItem);
        BaseWithGraphics(writer, name, 0);
        writer.WriteUnsignedInteger(2);
        writer.WriteUnsignedInteger(1);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger((uint)(tessellation + 1));
        UserData(writer);
        writer.WriteUnsignedInteger(0);
    };

    public static byte[] EmptyGeometry(uint type)
    {
        var writer = new PrcTestBitWriter();
        writer.WriteUnsignedInteger(type);
        ContentPrcBase(writer, null, false);
        writer.WriteUnsignedInteger(0);
        UserData(writer);
        return writer.ToArray();
    }

    public static byte[] ModelFile(uint[] structure, int root, double unit, IReadOnlyList<PrcTestSchema> schemas = null)
    {
        var writer = new PrcTestBitWriter();
        Schema(writer, schemas ?? []);
        writer.WriteUnsignedInteger(PrcTypes.ModelFile);
        ContentPrcBase(writer, "Model", false);
        writer.WriteBoolean(true);
        writer.WriteDouble(unit);
        writer.WriteUnsignedInteger(1);
        foreach (var part in structure)
        {
            writer.WriteUnsignedInteger(part);
        }

        writer.WriteUnsignedInteger((uint)(root + 1));
        writer.WriteBoolean(true);
        writer.WriteUnsignedInteger(0);
        UserData(writer);
        return writer.ToArray();
    }
}

/// <summary>A face of a test 3D tessellation: its kinds of triangulation, where they start, their counts.</summary>
internal sealed class PrcTestFace
{
    public uint UsedEntities { get; init; }

    public uint Start { get; init; }

    public uint[] Sizes { get; init; } = [];

    public uint TextureIndexes { get; init; }

    public int Style { get; init; } = -1;

    public List<byte[]> Colors { get; init; }
}

/// <summary>A schema entry: the entity type and its tokens.</summary>
internal sealed class PrcTestSchema
{
    public PrcTestSchema(uint type, params uint[] tokens)
    {
        Type = type;
        Tokens = tokens;
    }

    public uint Type { get; }

    public uint[] Tokens { get; }
}
