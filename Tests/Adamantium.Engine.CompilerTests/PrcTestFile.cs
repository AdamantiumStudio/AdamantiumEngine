using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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

    /// <summary>A file of one file structure whose root occurrence is the last of its tree, in millimetres unless
    /// <paramref name="unit"/> says otherwise, a unit of a CAD system unless <paramref name="unitFromCad"/> says not.</summary>
    public static PrcTestFile Single(byte[] globals, byte[] tree, byte[] tessellation, int occurrences, double unit = 1,
        bool unitFromCad = true)
    {
        var file = new PrcTestFile();
        var structure = new PrcTestStructure(1) { Globals = globals, Tree = tree, Tessellation = tessellation };
        file.Structures.Add(structure);
        file.ModelFile = PrcTestEntities.ModelFile(structure.Id, occurrences - 1, unit, unitFromCad: unitFromCad);
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

    /// <summary>The private files the file structure's header embeds, the pictures' data.</summary>
    public List<byte[]> UncompressedFiles { get; } = [];
}

/// <summary>Writes the entities of PRC test files as the specification's serialization procedures lay them out:
/// base content, graphics, user data, the globals, the tree, the tessellation section and the model file.</summary>
internal static class PrcTestEntities
{
    /// <summary>The PRC unique identifier the writers give an entity that names none of its own.</summary>
    public const uint AnyId = 7;

    public static void ContentPrcBase(PrcTestBitWriter writer, string name, bool referenceable, uint uniqueId = AnyId)
    {
        writer.WriteUnsignedInteger(0);
        writer.WriteBoolean(false);
        writer.WriteString(name);
        if (referenceable)
        {
            writer.WriteUnsignedInteger(0);
            writer.WriteUnsignedInteger(0);
            writer.WriteUnsignedInteger(uniqueId);
        }
    }

    public static void Graphics(PrcTestBitWriter writer, int style, ushort behavior = 0x0001, int layer = -1)
    {
        writer.WriteBoolean(false);
        writer.WriteUnsignedInteger((uint)(layer + 1));
        writer.WriteUnsignedInteger((uint)(style + 1));
        writer.WriteCharacter((byte)behavior);
        writer.WriteCharacter((byte)(behavior >> 8));
    }

    public static void BaseWithGraphics(PrcTestBitWriter writer, string name, int style, ushort behavior = 0x0001, uint uniqueId = AnyId,
        int layer = -1)
    {
        ContentPrcBase(writer, name, true, uniqueId);
        Graphics(writer, style, behavior, layer);
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

    /// <summary>Globals with the colours given and one plain-colour style per colour, style i of colour i, then the
    /// <paramref name="materialStyles"/>; the schema first. <paramref name="fonts"/> are markup fonts of one key each,
    /// of size 10; <paramref name="pictures"/>, <paramref name="textures"/> and <paramref name="materials"/> write their
    /// arrays, counts first, and none are written when they are null.</summary>
    public static byte[] Globals(IReadOnlyList<double[]> colours, IReadOnlyList<PrcTestSchema> schemas = null,
        Action<PrcTestBitWriter> coordinateSystems = null, int coordinateSystemCount = 0, IReadOnlyList<string> fonts = null,
        Action<PrcTestBitWriter> pictures = null, Action<PrcTestBitWriter> textures = null, Action<PrcTestBitWriter> materials = null,
        IReadOnlyList<Action<PrcTestBitWriter>> materialStyles = null)
    {
        var writer = new PrcTestBitWriter();
        Schema(writer, schemas ?? []);
        writer.WriteUnsignedInteger(PrcTypes.FileStructureGlobals);
        ContentPrcBase(writer, null, false);
        writer.WriteUnsignedInteger(0);
        writer.WriteDouble(0);
        writer.WriteDouble(0);
        writer.WriteString(null);
        writer.WriteUnsignedInteger((uint)(fonts?.Count ?? 0));
        foreach (var font in fonts ?? [])
        {
            writer.WriteString(font);
            writer.WriteUnsignedInteger(0);
            writer.WriteUnsignedInteger(1);
            writer.WriteUnsignedInteger(11);
            writer.WriteCharacter(2);
        }

        writer.WriteUnsignedInteger((uint)colours.Count);
        foreach (var colour in colours)
        {
            writer.WriteDouble(colour[0]);
            writer.WriteDouble(colour[1]);
            writer.WriteDouble(colour[2]);
        }

        foreach (var array in new[] { pictures, textures, materials })
        {
            if (array == null)
            {
                writer.WriteUnsignedInteger(0);
            }
            else
            {
                array(writer);
            }
        }

        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger((uint)(colours.Count + (materialStyles?.Count ?? 0)));
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

        foreach (var style in materialStyles ?? [])
        {
            style(writer);
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

    /// <summary>A product occurrence of a part (-1 for none) and sons, at a location the writer gives (null for none),
    /// in millimetres of a CAD system unless <paramref name="unit"/> and <paramref name="unitFromCad"/> say otherwise,
    /// with the entity references, views, display filters and scene display parameters the writers give.</summary>
    public static Action<PrcTestBitWriter> Occurrence(string name, int part, IReadOnlyList<int> sons,
        Action<PrcTestBitWriter> location = null, int prototype = -1, ushort behavior = 0x0001, uint uniqueId = AnyId,
        double unit = 1, bool unitFromCad = true, IReadOnlyList<Action<PrcTestBitWriter>> references = null,
        IReadOnlyList<Action<PrcTestBitWriter>> views = null, IReadOnlyList<Action<PrcTestBitWriter>> displayFilters = null,
        IReadOnlyList<Action<PrcTestBitWriter>> scenes = null) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.ProductOccurrence);
        BaseWithGraphics(writer, name, -1, behavior, uniqueId);
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
        writer.WriteBoolean(unitFromCad);
        writer.WriteDouble(unit);
        writer.WriteCharacter(0);
        writer.WriteInteger(3);
        writer.WriteBoolean(location != null);
        location?.Invoke(writer);
        Entities(writer, references);
        EmptyMarkups(writer);
        Entities(writer, views);
        writer.WriteBoolean(false);
        Entities(writer, displayFilters);
        Entities(writer, scenes);
        UserData(writer);
    };

    /// <summary>A count, then the entities the writers give.</summary>
    public static void Entities(PrcTestBitWriter writer, IReadOnlyList<Action<PrcTestBitWriter>> entities)
    {
        writer.WriteUnsignedInteger((uint)(entities?.Count ?? 0));
        foreach (var entity in entities ?? [])
        {
            entity(writer);
        }
    }

    /// <summary>A PRC_TYPE_MISC_EntityReference: its own graphics - the style and behaviour it gives its target - a
    /// reference coordinate system of the globals (-1 for none), and a reference on a PRC base to the entity of type
    /// <paramref name="targetType"/> and unique identifier <paramref name="targetId"/> in the same file structure.</summary>
    public static Action<PrcTestBitWriter> EntityReference(uint targetType, uint targetId, int style = -1, ushort behavior = 0xFFFF,
        int coordinateSystem = -1) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.EntityReference);
        BaseWithGraphics(writer, null, style, behavior);
        writer.WriteUnsignedInteger((uint)(coordinateSystem + 1));
        writer.WriteBoolean(true);
        writer.WriteUnsignedInteger(PrcTypes.ReferenceOnPrcBase);
        writer.WriteUnsignedInteger(targetType);
        writer.WriteBoolean(true);
        writer.WriteUnsignedInteger(targetId);
        UserData(writer);
    };

    /// <summary>A PRC_TYPE_ASM_Filter: whether it is active, the layers it keeps (or takes away), the entities it keeps
    /// (or takes away) - representation items of this file structure by their unique identifiers.</summary>
    public static Action<PrcTestBitWriter> Filter(string name, bool active, bool layersInclusive, IReadOnlyList<uint> layers,
        bool entitiesInclusive = false, IReadOnlyList<uint> entities = null) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.Filter);
        ContentPrcBase(writer, name, true);
        writer.WriteBoolean(active);
        writer.WriteBoolean(layersInclusive);
        UnsignedIntegers(writer, layers);
        writer.WriteBoolean(entitiesInclusive);
        writer.WriteUnsignedInteger((uint)(entities?.Count ?? 0));
        foreach (var entity in entities ?? [])
        {
            EntityReference(PrcTypes.PolyBrepModel, entity)(writer);
        }

        UserData(writer);
    };

    /// <summary>A PRC_TYPE_GRAPH_Camera: perspective with its fields of view in radians, or orthographic with its scale.</summary>
    public static Action<PrcTestBitWriter> Camera(string name, bool orthographic, double[] position, double[] lookAt, double[] up,
        double fieldOfView, double near = 1, double far = 1000) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.Camera);
        ContentPrcBase(writer, name, true);
        writer.WriteBoolean(orthographic);
        foreach (var value in position.Concat(lookAt).Concat(up))
        {
            writer.WriteDouble(value);
        }

        writer.WriteDouble(fieldOfView);
        writer.WriteDouble(fieldOfView);
        writer.WriteDouble(1.5);
        writer.WriteDouble(near);
        writer.WriteDouble(far);
        writer.WriteDouble(1);
    };

    /// <summary>A directional light of three colour indices - ambient, diffuse, specular, as the errata have them -
    /// shining along <paramref name="direction"/>, with the intensity of authoring version 8030 on.</summary>
    public static Action<PrcTestBitWriter> DirectionalLight(string name, int diffuseColour, double[] direction, double intensity) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.DirectionalLight);
        ContentPrcBase(writer, name, true);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger((uint)(diffuseColour * 3 + 1));
        writer.WriteUnsignedInteger(0);
        foreach (var value in direction)
        {
            writer.WriteDouble(value);
        }

        writer.WriteDouble(intensity);
    };

    /// <summary>A point light at <paramref name="location"/> of a diffuse colour of the globals, fading as given.</summary>
    public static Action<PrcTestBitWriter> PointLight(string name, int diffuseColour, double[] location, double linear) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.PointLight);
        ContentPrcBase(writer, name, true);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger((uint)(diffuseColour * 3 + 1));
        writer.WriteUnsignedInteger(0);
        foreach (var value in location)
        {
            writer.WriteDouble(value);
        }

        writer.WriteDouble(1);
        writer.WriteDouble(linear);
        writer.WriteDouble(0);
    };

    /// <summary>PRC_TYPE_GRAPH_SceneDisplayParameters of the lights and camera the writers give, and a clipping plane
    /// when asked for.</summary>
    public static Action<PrcTestBitWriter> Scene(string name, IReadOnlyList<Action<PrcTestBitWriter>> lights,
        Action<PrcTestBitWriter> camera, bool clippingPlane = false) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.SceneDisplayParameters);
        ContentPrcBase(writer, name, true);
        writer.WriteBoolean(true);
        Entities(writer, lights);
        writer.WriteBoolean(camera != null);
        camera?.Invoke(writer);
        writer.WriteBoolean(false);
        writer.WriteUnsignedInteger(clippingPlane ? 1u : 0u);
        if (clippingPlane)
        {
            PlaneSurface(writer);
        }

        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(0);
        writer.WriteBoolean(false);
    };

    /// <summary>A PRC_TYPE_SURF_Plane as views and clipping planes have it: no base content, a translation, its domain
    /// and parameterization.</summary>
    public static void PlaneSurface(PrcTestBitWriter writer)
    {
        writer.WriteUnsignedInteger(PrcTypes.SurfacePlane);
        writer.WriteBoolean(false);
        writer.WriteUnsignedInteger(0);
        writer.WriteCharacter(0x01);
        foreach (var value in new double[] { 0, 0, 2, -1, -1, 1, 1, 1, 0, 1, 0 })
        {
            writer.WriteDouble(value);
        }
    }

    /// <summary>A PRC_TYPE_MKP_View of the scene the writer gives (null for none), default or not.</summary>
    public static Action<PrcTestBitWriter> View(string name, uint uniqueId, Action<PrcTestBitWriter> scene, bool isDefault) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.View);
        BaseWithGraphics(writer, name, -1, 0x0001, uniqueId);
        writer.WriteUnsignedInteger(0);
        PlaneSurface(writer);
        writer.WriteBoolean(scene != null);
        scene?.Invoke(writer);
        writer.WriteBoolean(false);
        writer.WriteBoolean(isDefault);
        writer.WriteBoolean(false);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(0);
        UserData(writer);
    };

    /// <summary>The pictures of the globals: each a PNG in the uncompressed file of its own index.</summary>
    public static Action<PrcTestBitWriter> Pictures(int count) => writer =>
    {
        writer.WriteUnsignedInteger((uint)count);
        for (var picture = 0; picture < count; picture++)
        {
            writer.WriteUnsignedInteger(PrcTypes.Picture);
            ContentPrcBase(writer, $"Picture {picture}", false);
            writer.WriteInteger(0);
            writer.WriteUnsignedInteger((uint)(picture + 1));
            writer.WriteUnsignedInteger(0);
            writer.WriteUnsignedInteger(0);
        }
    };

    /// <summary>A texture definition of the globals: a 2D picture on stored coordinates, laid as the given mapping
    /// attributes say by the given function, wrapping as given, its coordinates transformed by a translation, a
    /// rotation by its x axis alone and a uniform scale when <paramref name="scale"/> is not 1.</summary>
    public static void TextureDefinition(PrcTestBitWriter writer, int picture, uint mappingAttributes, int function, int wrapS, int wrapT,
        double scale = 1, double[] xAxis = null, double[] translation = null)
    {
        writer.WriteUnsignedInteger(PrcTypes.TextureDefinition);
        ContentPrcBase(writer, $"Texture of {picture}", true);
        writer.WriteUnsignedInteger((uint)(picture + 1));
        writer.WriteCharacter(2);
        writer.WriteInteger(1);
        writer.WriteUnsignedInteger(mappingAttributes);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(1);
        writer.WriteCharacter(0x0F);
        writer.WriteInteger(function);
        writer.WriteInteger(0);
        writer.WriteInteger(0);
        writer.WriteCharacter(0);
        writer.WriteInteger(wrapS);
        writer.WriteInteger(wrapT);
        var transformed = scale != 1 || xAxis != null || translation != null;
        writer.WriteBoolean(transformed);
        if (!transformed)
        {
            return;
        }

        writer.WriteUnsignedInteger(PrcTypes.TextureTransformation);
        writer.WriteBoolean(false);
        writer.WriteBoolean(false);
        writer.WriteBoolean(true);
        writer.WriteCharacter((byte)((translation != null ? 0x01 : 0) | (xAxis != null ? 0x02 : 0) | (scale != 1 ? 0x08 : 0)));
        foreach (var value in (translation ?? []).Concat(xAxis ?? []))
        {
            writer.WriteDouble(value);
        }

        if (scale != 1)
        {
            writer.WriteDouble(scale);
        }
    }

    /// <summary>A generic material of the globals of the given colours' indices.</summary>
    public static void Material(PrcTestBitWriter writer, string name, int diffuseColour)
    {
        writer.WriteUnsignedInteger(PrcTypes.Material);
        ContentPrcBase(writer, name, true);
        writer.WriteUnsignedInteger((uint)(diffuseColour * 3 + 1));
        writer.WriteUnsignedInteger((uint)(diffuseColour * 3 + 1));
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(0);
        writer.WriteDouble(0.5);
        writer.WriteDouble(1);
        writer.WriteDouble(1);
        writer.WriteDouble(1);
        writer.WriteDouble(1);
    }

    /// <summary>A texture application of the globals: a generic material, a texture definition, the next level (-1 for
    /// none) and the first set of texture coordinates.</summary>
    public static void TextureApplication(PrcTestBitWriter writer, string name, int material, int definition, int next = -1)
    {
        writer.WriteUnsignedInteger(PrcTypes.TextureApplication);
        ContentPrcBase(writer, name, true);
        writer.WriteUnsignedInteger((uint)(material + 1));
        writer.WriteUnsignedInteger((uint)(definition + 1));
        writer.WriteUnsignedInteger((uint)(next + 1));
        writer.WriteUnsignedInteger(1);
    }

    /// <summary>A style of the globals drawn by material <paramref name="material"/>.</summary>
    public static Action<PrcTestBitWriter> MaterialStyle(string name, int material) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.Style);
        ContentPrcBase(writer, name, true);
        writer.WriteDouble(0);
        writer.WriteBoolean(false);
        writer.WriteUnsignedInteger(0);
        writer.WriteBoolean(true);
        writer.WriteUnsignedInteger((uint)(material + 1));
        writer.WriteBoolean(false);
        writer.WriteBoolean(false);
        writer.WriteBoolean(false);
        writer.WriteBoolean(false);
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
        ushort behavior = 0x0001, uint uniqueId = AnyId, int layer = -1)
    {
        BaseWithGraphics(writer, name, style, behavior, uniqueId, layer);
        writer.WriteUnsignedInteger((uint)(coordinateSystem + 1));
        writer.WriteUnsignedInteger((uint)(tessellation + 1));
    }

    public static void PolyBrep(PrcTestBitWriter writer, string name, int style, int tessellation, int coordinateSystem = -1,
        ushort behavior = 0x0001, uint uniqueId = AnyId, int layer = -1)
    {
        writer.WriteUnsignedInteger(PrcTypes.PolyBrepModel);
        ItemContent(writer, name, style, tessellation, coordinateSystem, behavior, uniqueId, layer);
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

    /// <summary>A regular 3D tessellation of one face per entry of <paramref name="faces"/>: with normals, or, when they
    /// are null, normals to be recalculated by <paramref name="creaseAngle"/> in degrees.</summary>
    public static Action<PrcTestBitWriter> Tessellation3D(IReadOnlyList<double> coordinates, IReadOnlyList<double> normals,
        IReadOnlyList<uint> triangulated, IReadOnlyList<PrcTestFace> faces, IReadOnlyList<double> textures = null,
        double creaseAngle = 0.5) => writer =>
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
            writer.WriteDouble(creaseAngle);
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

    public static byte[] ModelFile(uint[] structure, int root, double unit, IReadOnlyList<PrcTestSchema> schemas = null,
        bool unitFromCad = true)
    {
        var writer = new PrcTestBitWriter();
        Schema(writer, schemas ?? []);
        writer.WriteUnsignedInteger(PrcTypes.ModelFile);
        ContentPrcBase(writer, "Model", false);
        writer.WriteBoolean(unitFromCad);
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
