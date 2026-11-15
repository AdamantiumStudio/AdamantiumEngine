using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Reads a file structure's tessellation section (PRC_TYPE_ASM_FileStructureTessellation): regular 3D
/// tessellations with their faces, wire tessellations and markup tessellations. A highly compressed tessellation
/// (PRC_TYPE_TESS_3D_Compressed) is not read: its arrays are Huffman coded in a form the specification does not give,
/// so not even its length is known, and the section is read no further.</summary>
internal static class PrcTessellationReader
{
    /// <summary>The tessellation types of a face in the order their counts follow one another: triangles, fans and
    /// stripes, each with one normal a vertex or one a face, plain or textured.</summary>
    internal const uint Triangle = 0x0002;
    internal const uint TriangleFan = 0x0004;
    internal const uint TriangleStripe = 0x0008;
    internal const uint TriangleOneNormal = 0x0020;
    internal const uint TriangleFanOneNormal = 0x0040;
    internal const uint TriangleStripeOneNormal = 0x0080;
    internal const uint TriangleTextured = 0x0200;
    internal const uint TriangleFanTextured = 0x0400;
    internal const uint TriangleStripeTextured = 0x0800;
    internal const uint TriangleOneNormalTextured = 0x2000;
    internal const uint TriangleFanOneNormalTextured = 0x4000;
    internal const uint TriangleStripeOneNormalTextured = 0x8000;

    /// <summary>The flag a one-normal fan or stripe count carries when the whole run has a single normal.</summary>
    internal const uint SingleNormal = 0x40000000;

    internal static void Read(PrcBitReader bits, PrcFileStructure structure, PrcNotes notes)
    {
        var reader = new PrcEntityReader(bits, structure.Schema, notes);
        reader.Expect(PrcTypes.FileStructureTessellation, "the tessellation section");
        reader.ReadContentPrcBase(PrcTypes.FileStructureTessellation);
        structure.DeclaredTessellationCount = bits.ReadCount(8, "tessellations");
        for (var index = 0; index < structure.DeclaredTessellationCount; index++)
        {
            var type = bits.ReadUnsignedInteger();
            switch (type)
            {
                case PrcTypes.Tessellation3D:
                    structure.Tessellations.Add(Read3D(reader));
                    break;
                case PrcTypes.Tessellation3DWire:
                    structure.Tessellations.Add(ReadWire(reader));
                    break;
                case PrcTypes.TessellationMarkup:
                    structure.Tessellations.Add(ReadMarkup(reader));
                    break;
                case PrcTypes.Tessellation3DCompressed:
                    structure.FirstCompressedTessellation = index;
                    return;
                default:
                    reader.SkipUnknown(type, "a tessellation");
                    structure.Tessellations.Add(new PrcUnreadTessellation { Type = type });
                    break;
            }
        }

        reader.EndOf(PrcTypes.FileStructureTessellation);
        reader.SkipUserData();
        reader.CheckEnd();
    }

    /// <summary>A PRC_TYPE_TESS_Markup, its type code read: its coordinates, codes, texts, label and behaviour.</summary>
    internal static PrcMarkupTessellation ReadMarkup(PrcEntityReader reader)
    {
        var bits = reader.Bits;
        var markup = new PrcMarkupTessellation { Type = PrcTypes.TessellationMarkup };
        markup.Coordinates = ReadBase(reader);
        markup.Codes = ReadUnsignedIntegers(bits, "markup codes");
        var texts = bits.ReadCount(1, "markup texts");
        for (var text = 0; text < texts; text++)
        {
            markup.Texts.Add(bits.ReadString() ?? string.Empty);
        }

        markup.Label = bits.ReadString();
        markup.Behavior = bits.ReadCharacter();
        reader.EndOf(PrcTypes.TessellationMarkup);
        return markup;
    }

    private static double[] ReadBase(PrcEntityReader reader)
    {
        var bits = reader.Bits;
        bits.ReadBoolean();
        var coordinates = ReadDoubles(bits, "coordinates");
        reader.EndOf(PrcTypes.TessellationBase);
        return coordinates;
    }

    private static Prc3DTessellation Read3D(PrcEntityReader reader)
    {
        var bits = reader.Bits;
        var tessellation = new Prc3DTessellation { Type = PrcTypes.Tessellation3D };
        tessellation.Coordinates = ReadBase(reader);
        tessellation.HasFaces = bits.ReadBoolean();
        bits.ReadBoolean();
        tessellation.MustRecalculateNormals = bits.ReadBoolean();
        if (tessellation.MustRecalculateNormals)
        {
            bits.ReadCharacter();
            bits.ReadDouble();
        }

        tessellation.Normals = ReadDoubles(bits, "normal coordinates");
        tessellation.WireIndices = ReadUnsignedIntegers(bits, "wire indices");
        tessellation.TriangulatedIndices = ReadUnsignedIntegers(bits, "triangulated indices");
        var faces = bits.ReadCount(8, "face tessellations");
        for (var face = 0; face < faces; face++)
        {
            reader.Expect(PrcTypes.TessellationFace, "a face tessellation");
            tessellation.Faces.Add(ReadFace(reader));
        }

        tessellation.TextureCoordinates = ReadDoubles(bits, "texture coordinates");
        reader.EndOf(PrcTypes.Tessellation3D);
        return tessellation;
    }

    private static PrcFaceTessellation ReadFace(PrcEntityReader reader)
    {
        var bits = reader.Bits;
        var face = new PrcFaceTessellation
        {
            LineAttributes = ReadUnsignedIntegers(bits, "line attributes"),
            StartWire = bits.ReadUnsignedInteger(),
            SizesWire = ReadUnsignedIntegers(bits, "wire sizes"),
            UsedEntities = bits.ReadUnsignedInteger(),
            StartTriangulated = bits.ReadUnsignedInteger(),
            SizesTriangulated = ReadUnsignedIntegers(bits, "triangulated sizes"),
            TextureCoordinateIndexes = bits.ReadUnsignedInteger()
        };
        if (bits.ReadBoolean())
        {
            face.VertexColors = ReadVertexColors(bits, CountFaceVertices(face, reader));
        }

        if (face.LineAttributes.Length > 0)
        {
            bits.ReadUnsignedInteger();
        }

        reader.EndOf(PrcTypes.TessellationFace);
        return face;
    }

    private static PrcWireTessellation ReadWire(PrcEntityReader reader)
    {
        var bits = reader.Bits;
        var wire = new PrcWireTessellation { Type = PrcTypes.Tessellation3DWire };
        wire.Coordinates = ReadBase(reader);
        wire.WireIndices = ReadUnsignedIntegers(bits, "wire indices");
        if (bits.ReadBoolean())
        {
            var vertices = 0;
            for (var index = 0; index < wire.WireIndices.Length;)
            {
                var count = (int)(wire.WireIndices[index] & 0x0FFFFFFF);
                vertices += count + ((wire.WireIndices[index] & 0x10000000) != 0 ? 1 : 0);
                index += count + 1;
            }

            if (wire.WireIndices.Length == 0)
            {
                vertices = wire.Coordinates.Length / 3;
            }

            ReadVertexColors(bits, vertices);
            reader.Tell("the vertex colours of wire tessellations are left out");
        }

        reader.EndOf(PrcTypes.Tessellation3DWire);
        return wire;
    }

    private static int CountFaceVertices(PrcFaceTessellation face, PrcEntityReader reader)
    {
        var vertices = 0;
        var sizes = face.SizesTriangulated;
        var at = 0;
        for (var bit = 0; bit < 32; bit++)
        {
            var kind = 1u << bit;
            if ((face.UsedEntities & kind) == 0)
            {
                continue;
            }

            if (kind is Triangle or TriangleOneNormal or TriangleTextured or TriangleOneNormalTextured)
            {
                vertices += (int)Take(sizes, ref at, reader) * 3;
                continue;
            }

            var runs = Take(sizes, ref at, reader);
            for (var run = 0u; run < runs; run++)
            {
                vertices += (int)(Take(sizes, ref at, reader) & ~SingleNormal);
            }
        }

        return vertices;
    }

    private static uint Take(uint[] sizes, ref int at, PrcEntityReader reader)
    {
        if (at >= sizes.Length)
        {
            throw reader.Bits.Corrupt("a face tessellation has fewer counts than its kinds of triangulation need");
        }

        return sizes[at++];
    }

    private static List<Color> ReadVertexColors(PrcBitReader bits, int vertices)
    {
        var rgba = bits.ReadBoolean();
        var perSegment = bits.ReadBoolean();
        if (bits.ReadBoolean())
        {
            throw bits.Corrupt("vertex colours are stored in the optimised form the specification reserves for future use");
        }

        var count = perSegment ? vertices / 2 : vertices;
        bits.CheckCount((ulong)count, 1, "vertex colours");
        var colors = new List<Color>(count);
        for (var index = 0; index < count; index++)
        {
            if (index > 0 && bits.ReadBoolean())
            {
                colors.Add(colors[^1]);
                continue;
            }

            var red = bits.ReadCharacter();
            var green = bits.ReadCharacter();
            var blue = bits.ReadCharacter();
            colors.Add(new Color(red, green, blue, rgba ? bits.ReadCharacter() : byte.MaxValue));
        }

        return colors;
    }

    private static double[] ReadDoubles(PrcBitReader bits, string what)
    {
        var count = bits.ReadCount(PrcBitReader.ShortestDouble, what);
        var values = new double[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = bits.ReadDouble();
        }

        return values;
    }

    private static uint[] ReadUnsignedIntegers(PrcBitReader bits, string what)
    {
        var count = bits.ReadCount(1, what);
        var values = new uint[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = bits.ReadUnsignedInteger();
        }

        return values;
    }
}
