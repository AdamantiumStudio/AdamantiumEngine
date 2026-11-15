using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Reads a file structure's globals section (PRC_TYPE_ASM_FileStructureGlobals): the schema at its start,
/// then the colours, pictures, texture definitions, materials, line patterns, styles, fill patterns and reference
/// coordinate systems its tree draws with.</summary>
internal static class PrcGlobalsReader
{
    internal static void Read(PrcBitReader bits, PrcFileStructure structure, PrcNotes notes)
    {
        structure.Schema = PrcSchema.Read(bits);
        var reader = new PrcEntityReader(bits, structure.Schema, notes);
        reader.Expect(PrcTypes.FileStructureGlobals, "the globals");
        reader.ReadContentPrcBase(PrcTypes.FileStructureGlobals);
        var referenced = bits.ReadCount(4, "referenced file structures");
        for (var index = 0; index < referenced; index++)
        {
            structure.ReferencedFileStructures.Add(reader.ReadCompressedUniqueId());
        }

        bits.ReadDouble();
        bits.ReadDouble();
        ReadFonts(reader);
        var colours = bits.ReadCount(3 * PrcBitReader.ShortestDouble, "colours");
        for (var index = 0; index < colours; index++)
        {
            structure.Colors.Add(new Vector3<double>(bits.ReadDouble(), bits.ReadDouble(), bits.ReadDouble()));
        }

        var pictures = bits.ReadCount(8, "pictures");
        for (var index = 0; index < pictures; index++)
        {
            structure.Pictures.Add(ReadPicture(reader));
        }

        var textures = bits.ReadCount(8, "texture definitions");
        for (var index = 0; index < textures; index++)
        {
            structure.TextureDefinitions.Add(ReadTextureDefinition(reader));
        }

        var materials = bits.ReadCount(8, "materials");
        for (var index = 0; index < materials; index++)
        {
            structure.Materials.Add(ReadMaterial(reader));
        }

        var patterns = bits.ReadCount(8, "line patterns");
        for (var index = 0; index < patterns; index++)
        {
            ReadLinePattern(reader);
        }

        var styles = bits.ReadCount(8, "styles");
        for (var index = 0; index < styles; index++)
        {
            structure.Styles.Add(ReadStyle(reader));
        }

        var fills = bits.ReadCount(8, "fill patterns");
        for (var index = 0; index < fills; index++)
        {
            ReadFillPattern(reader);
        }

        var systems = bits.ReadCount(8, "reference coordinate systems");
        for (var index = 0; index < systems; index++)
        {
            reader.Expect(PrcTypes.CoordinateSystem, "a reference coordinate system");
            structure.CoordinateSystems.Add(PrcTreeReader.ReadCoordinateSystem(reader));
        }

        reader.EndOf(PrcTypes.FileStructureGlobals);
        reader.SkipUserData();
        reader.CheckEnd();
    }

    private static void ReadFonts(PrcEntityReader reader)
    {
        var bits = reader.Bits;
        bits.ReadString();
        var fonts = bits.ReadCount(3, "fonts");
        for (var font = 0; font < fonts; font++)
        {
            bits.ReadString();
            bits.ReadUnsignedInteger();
            var keys = bits.ReadCount(9, "font keys");
            for (var key = 0; key < keys; key++)
            {
                bits.ReadUnsignedInteger();
                bits.ReadCharacter();
            }
        }
    }

    private static PrcPicture ReadPicture(PrcEntityReader reader)
    {
        reader.Expect(PrcTypes.Picture, "a picture");
        var picture = new PrcPicture
        {
            Name = reader.ReadContentPrcBase(PrcTypes.Picture),
            Format = reader.Bits.ReadInteger(),
            UncompressedFileIndex = (int)reader.Bits.ReadUnsignedInteger() - 1,
            Width = reader.Bits.ReadUnsignedInteger(),
            Height = reader.Bits.ReadUnsignedInteger()
        };
        reader.EndOf(PrcTypes.Picture);
        return picture;
    }

    private static PrcTextureDefinition ReadTextureDefinition(PrcEntityReader reader)
    {
        const int mappingOperator = 4;
        const int blendFunction = 4;
        const int alphaTest = 0x2;

        var bits = reader.Bits;
        reader.Expect(PrcTypes.TextureDefinition, "a texture definition");
        var name = reader.ReadContentPrcBase(PrcTypes.TextureDefinition);
        var picture = (int)bits.ReadUnsignedInteger() - 1;
        var dimension = bits.ReadCharacter();
        var mapping = bits.ReadInteger();
        if (mapping == mappingOperator)
        {
            bits.ReadInteger();
            if (bits.ReadBoolean())
            {
                reader.ReadTransformation3d();
            }
        }

        bits.ReadUnsignedInteger();
        var intensities = bits.ReadCount(2, "texture intensities");
        reader.SkipDoubles(intensities);
        var components = bits.ReadCount(8, "texture components");
        for (var component = 0; component < components; component++)
        {
            bits.ReadCharacter();
        }

        if (bits.ReadInteger() == blendFunction)
        {
            reader.SkipDoubles(4);
        }

        bits.ReadInteger();
        bits.ReadInteger();
        var applying = bits.ReadCharacter();
        if ((applying & alphaTest) != 0)
        {
            bits.ReadInteger();
            bits.ReadDouble();
        }

        bits.ReadInteger();
        if (dimension > 1)
        {
            bits.ReadInteger();
        }

        if (dimension > 2)
        {
            bits.ReadInteger();
        }

        if (bits.ReadBoolean())
        {
            reader.Expect(PrcTypes.TextureTransformation, "a texture transformation");
            bits.ReadBoolean();
            bits.ReadBoolean();
            bits.ReadBoolean();
            ReadTransformation2d(reader);
            reader.EndOf(PrcTypes.TextureTransformation);
        }

        reader.EndOf(PrcTypes.TextureDefinition);
        reader.Tell("the texture definitions of the globals are read past; their textures are not laid");
        return new PrcTextureDefinition { Name = name, PictureIndex = picture };
    }

    private static void ReadTransformation2d(PrcEntityReader reader)
    {
        var bits = reader.Bits;
        var behavior = bits.ReadCharacter();
        if ((behavior & 0x01) != 0)
        {
            reader.SkipDoubles(2);
        }

        if ((behavior & 0x20) != 0 || (behavior & 0x02) != 0)
        {
            reader.SkipDoubles(4);
        }

        if ((behavior & 0x10) != 0)
        {
            reader.SkipDoubles(2);
        }
        else if ((behavior & 0x08) != 0)
        {
            reader.SkipDoubles(1);
        }

        if ((behavior & 0x40) != 0)
        {
            reader.SkipDoubles(3);
        }
    }

    private static PrcMaterial ReadMaterial(PrcEntityReader reader)
    {
        var bits = reader.Bits;
        var type = bits.ReadUnsignedInteger();
        switch (type)
        {
            case PrcTypes.Material:
                var material = new PrcMaterial
                {
                    Name = reader.ReadContentPrcBase(type),
                    AmbientIndex = (int)bits.ReadUnsignedInteger() - 1,
                    DiffuseIndex = (int)bits.ReadUnsignedInteger() - 1,
                    EmissiveIndex = (int)bits.ReadUnsignedInteger() - 1,
                    SpecularIndex = (int)bits.ReadUnsignedInteger() - 1,
                    Shininess = bits.ReadDouble(),
                    AmbientAlpha = bits.ReadDouble(),
                    DiffuseAlpha = bits.ReadDouble(),
                    EmissiveAlpha = bits.ReadDouble(),
                    SpecularAlpha = bits.ReadDouble()
                };
                reader.EndOf(type);
                return material;
            case PrcTypes.TextureApplication:
                var application = new PrcMaterial
                {
                    Name = reader.ReadContentPrcBase(type),
                    IsTextureApplication = true,
                    MaterialGenericIndex = (int)bits.ReadUnsignedInteger() - 1,
                    TextureDefinitionIndex = (int)bits.ReadUnsignedInteger() - 1
                };
                bits.ReadUnsignedInteger();
                bits.ReadUnsignedInteger();
                reader.EndOf(type);
                return application;
            default:
                reader.SkipUnknown(type, "a material");
                return new PrcMaterial();
        }
    }

    private static void ReadLinePattern(PrcEntityReader reader)
    {
        var bits = reader.Bits;
        reader.Expect(PrcTypes.LinePattern, "a line pattern");
        reader.ReadContentPrcBase(PrcTypes.LinePattern);
        reader.SkipDoubles(bits.ReadCount(PrcBitReader.ShortestDouble, "dashes"));
        bits.ReadDouble();
        bits.ReadBoolean();
        reader.EndOf(PrcTypes.LinePattern);
    }

    private static PrcStyle ReadStyle(PrcEntityReader reader)
    {
        var bits = reader.Bits;
        reader.Expect(PrcTypes.Style, "a style");
        var name = reader.ReadContentPrcBase(PrcTypes.Style);
        var width = bits.ReadDouble();
        bits.ReadBoolean();
        bits.ReadUnsignedInteger();
        var isMaterial = bits.ReadBoolean();
        var index = (int)bits.ReadUnsignedInteger() - 1;
        byte? transparency = bits.ReadBoolean() ? bits.ReadCharacter() : null;
        if (bits.ReadBoolean())
        {
            bits.ReadCharacter();
        }

        bits.ReadBoolean();
        bits.ReadBoolean();
        reader.EndOf(PrcTypes.Style);
        return new PrcStyle
        {
            Name = name, LineWidth = width, IsMaterial = isMaterial, ColorOrMaterialIndex = index, Transparency = transparency
        };
    }

    private static void ReadFillPattern(PrcEntityReader reader)
    {
        var bits = reader.Bits;
        var type = bits.ReadUnsignedInteger();
        if (type is < PrcTypes.DottingPattern or > PrcTypes.VPicturePattern)
        {
            reader.SkipUnknown(type, "a fill pattern");
            return;
        }

        reader.ReadContentPrcBase(type);
        bits.ReadUnsignedInteger();
        switch (type)
        {
            case PrcTypes.DottingPattern:
                bits.ReadDouble();
                bits.ReadBoolean();
                bits.ReadInteger();
                break;
            case PrcTypes.HatchingPattern:
                var lines = bits.ReadCount(5 * PrcBitReader.ShortestDouble, "hatching lines");
                for (var line = 0; line < lines; line++)
                {
                    reader.SkipDoubles(5);
                    bits.ReadInteger();
                }

                break;
            case PrcTypes.SolidPattern:
                bits.ReadBoolean();
                bits.ReadUnsignedInteger();
                break;
            case PrcTypes.VPicturePattern:
                reader.SkipDoubles(2);
                reader.Expect(PrcTypes.TessellationMarkup, "the picture of a pattern");
                PrcTessellationReader.ReadMarkup(reader);
                break;
        }

        reader.EndOf(type);
    }
}
