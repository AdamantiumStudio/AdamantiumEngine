using System;
using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Reads a file structure's globals section (PRC_TYPE_ASM_FileStructureGlobals): the schema at its start,
/// then the markup fonts, colours, pictures, texture definitions, materials, line patterns, styles, fill patterns and
/// reference coordinate systems its tree draws with. A texture definition is read as the errata lay it out: a mapping
/// operator's transformation only for mapping type 3, a destination blend after each non-zero source blend, no wrapping
/// mode before the s, t and r ones; its 2D transformation's rotation is its x axis alone - the y axis that turned a
/// quarter, or back when mirrored - as the errata measure it, while two non-orthogonal axes are both stored.</summary>
internal static class PrcGlobalsReader
{
    internal static void Read(PrcBitReader bits, PrcFileStructure structure, PrcNotes notes)
    {
        structure.Schema = PrcSchema.Read(bits);
        var reader = new PrcEntityReader(bits, structure.Schema, notes) { AuthoringVersion = structure.AuthoringVersion };
        reader.Expect(PrcTypes.FileStructureGlobals, "the globals");
        reader.ReadContentPrcBase(PrcTypes.FileStructureGlobals);
        var referenced = bits.ReadCount(4, "referenced file structures");
        for (var index = 0; index < referenced; index++)
        {
            structure.ReferencedFileStructures.Add(reader.ReadCompressedUniqueId());
        }

        bits.ReadDouble();
        bits.ReadDouble();
        ReadFonts(reader, structure);
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

    private static void ReadFonts(PrcEntityReader reader, PrcFileStructure structure)
    {
        var bits = reader.Bits;
        bits.ReadString();
        var fonts = bits.ReadCount(3, "fonts");
        for (var font = 0; font < fonts; font++)
        {
            var name = bits.ReadString();
            bits.ReadUnsignedInteger();
            var keys = bits.ReadCount(9, "font keys");
            for (var key = 0; key < keys; key++)
            {
                var size = (int)bits.ReadUnsignedInteger() - 1;
                structure.Fonts.Add(new PrcFont { Name = name, Size = size, Attributes = bits.ReadCharacter() });
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
        var bits = reader.Bits;
        reader.Expect(PrcTypes.TextureDefinition, "a texture definition");
        var name = reader.ReadContentPrcBase(PrcTypes.TextureDefinition);
        var picture = (int)bits.ReadUnsignedInteger() - 1;
        var dimension = bits.ReadCharacter();
        var mapping = bits.ReadInteger();
        var mappingOperator = 0;
        if (mapping == PrcTextureDefinition.MappingOperator)
        {
            mappingOperator = bits.ReadInteger();
            if (bits.ReadBoolean())
            {
                reader.ReadTransformation3d();
            }
        }

        var attributes = bits.ReadUnsignedInteger();
        var intensities = bits.ReadCount(PrcBitReader.ShortestDouble, "texture intensities");
        reader.SkipDoubles(intensities);
        var components = bits.ReadCount(8, "texture components");
        for (var component = 0; component < components; component++)
        {
            bits.ReadCharacter();
        }

        var function = bits.ReadInteger();
        if (function == PrcTextureDefinition.FunctionBlend)
        {
            reader.SkipDoubles(4);
        }

        if (bits.ReadInteger() != 0)
        {
            bits.ReadInteger();
        }

        if (bits.ReadInteger() != 0)
        {
            bits.ReadInteger();
        }

        var applying = bits.ReadCharacter();
        var alphaReference = 0d;
        if ((applying & PrcTextureDefinition.ApplyingAlphaTest) != 0)
        {
            bits.ReadInteger();
            alphaReference = bits.ReadDouble();
        }

        var wrapS = bits.ReadInteger();
        var wrapT = dimension > 1 ? bits.ReadInteger() : PrcTextureDefinition.WrapUnknown;
        if (dimension > 2)
        {
            bits.ReadInteger();
        }

        PrcTextureTransformation transformation = null;
        if (bits.ReadBoolean())
        {
            reader.Expect(PrcTypes.TextureTransformation, "a texture transformation");
            var flipS = bits.ReadBoolean();
            var flipT = bits.ReadBoolean();
            bits.ReadBoolean();
            transformation = new PrcTextureTransformation { FlipS = flipS, FlipT = flipT, Matrix = ReadTransformation2d(reader) };
            reader.EndOf(PrcTypes.TextureTransformation);
        }

        reader.EndOf(PrcTypes.TextureDefinition);
        return new PrcTextureDefinition
        {
            Name = name,
            PictureIndex = picture,
            Dimension = dimension,
            MappingType = mapping,
            MappingOperatorKind = mappingOperator,
            MappingAttributes = attributes,
            Function = function,
            ApplyingMode = applying,
            AlphaTestReference = alphaReference,
            WrapS = wrapS,
            WrapT = wrapT,
            Transformation = transformation
        };
    }

    private static Matrix4x4<double> ReadTransformation2d(PrcEntityReader reader)
    {
        const int translate = 0x01;
        const int rotate = 0x02;
        const int mirror = 0x04;
        const int scale = 0x08;
        const int nonUniformScale = 0x10;
        const int nonOrthogonal = 0x20;
        const int homogeneous = 0x40;

        var bits = reader.Bits;
        var behavior = bits.ReadCharacter();
        var originX = 0d;
        var originY = 0d;
        if ((behavior & translate) != 0)
        {
            originX = bits.ReadDouble();
            originY = bits.ReadDouble();
        }

        var xAxisX = 1d;
        var xAxisY = 0d;
        var yAxisX = 0d;
        var yAxisY = 1d;
        if ((behavior & nonOrthogonal) != 0)
        {
            xAxisX = bits.ReadDouble();
            xAxisY = bits.ReadDouble();
            yAxisX = bits.ReadDouble();
            yAxisY = bits.ReadDouble();
        }
        else if ((behavior & rotate) != 0)
        {
            xAxisX = bits.ReadDouble();
            xAxisY = bits.ReadDouble();
            var turn = (behavior & mirror) != 0 ? -1 : 1;
            yAxisX = -xAxisY * turn;
            yAxisY = xAxisX * turn;
        }

        var scaleX = 1d;
        var scaleY = 1d;
        if ((behavior & nonUniformScale) != 0)
        {
            scaleX = bits.ReadDouble();
            scaleY = bits.ReadDouble();
        }
        else if ((behavior & scale) != 0)
        {
            scaleX = scaleY = bits.ReadDouble();
        }

        var homogeneousX = 0d;
        var homogeneousY = 0d;
        var homogeneousW = 1d;
        if ((behavior & homogeneous) != 0)
        {
            homogeneousX = bits.ReadDouble();
            homogeneousY = bits.ReadDouble();
            homogeneousW = bits.ReadDouble();
        }

        return new Matrix4x4<double>(
            xAxisX * scaleX, xAxisY * scaleX, 0, homogeneousX,
            yAxisX * scaleY, yAxisY * scaleY, 0, homogeneousY,
            0, 0, 1, 0,
            originX, originY, 0, homogeneousW);
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
                    TextureDefinitionIndex = (int)bits.ReadUnsignedInteger() - 1,
                    NextTextureIndex = (int)bits.ReadUnsignedInteger() - 1,
                    TextureCoordinateSet = Math.Max(0, (int)bits.ReadUnsignedInteger() - 1)
                };
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
