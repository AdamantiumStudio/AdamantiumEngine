using System;
using System.Buffers.Binary;
using System.IO;
using Adamantium.Core.Shared.Fonts;
using Adamantium.Fonts;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Fonts;

/// <summary>Finds the font program a PDF font draws with: the one embedded in the file, or a system font standing in
/// for it.</summary>
internal static class PdfFontPrograms
{
    /// <summary>The embedded program, or null when the font embeds none or it cannot be read.</summary>
    public static Typeface Embedded(PdfFontDescriptor descriptor, PdfDocument document)
    {
        if (descriptor == null)
        {
            return null;
        }

        try
        {
            if (descriptor.FontFile is { } type1)
            {
                return Typeface.LoadType1(document.Decode(type1).Data.ToArray());
            }

            if (descriptor.FontFile2 is { } trueType)
            {
                return Typeface.LoadTrueTypeProgram(document.Decode(trueType).Data.ToArray());
            }

            if (descriptor.FontFile3 is { } program)
            {
                var data = document.Decode(program).Data.ToArray();
                return PdfValues.Name(program.Dictionary[PdfFontKeys.Subtype], document) == PdfFontTypes.OpenType
                    ? OpenType(data)
                    : Typeface.LoadCff(data);
            }
        }
        catch (PdfException)
        {
        }
        catch (InvalidDataException)
        {
        }

        return null;
    }

    private static Typeface OpenType(byte[] data)
    {
        if (data.Length < 12 || !data.AsSpan(0, 4).SequenceEqual("OTTO"u8))
        {
            return Typeface.LoadTrueTypeProgram(data);
        }

        var tables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        for (var i = 0; i < tables && 12 + (i + 1) * 16 <= data.Length; i++)
        {
            var record = data.AsSpan(12 + i * 16, 16);
            if (!record[..4].SequenceEqual("CFF "u8))
            {
                continue;
            }

            var offset = BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            var length = BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
            if (offset > (uint)data.Length || length > (uint)data.Length - offset)
            {
                break;
            }

            return Typeface.LoadCff(data.AsSpan((int)offset, (int)length).ToArray());
        }

        throw new InvalidDataException("The OpenType program has no CFF table.");
    }

    /// <summary>A system font for a font the file does not embed: its own family by name, else one of the same
    /// metrics; null when the system has none of them.</summary>
    public static IFont Substitute(string name, PdfFontDescriptor descriptor)
    {
        var weight = PdfFontNames.WeightOf(name, descriptor);
        var style = PdfFontNames.StyleOf(name, descriptor);
        foreach (var family in PdfFontNames.CandidatesOf(name, descriptor))
        {
            var face = FontCollection.System.Match(family, weight, style);
            if (face == null)
            {
                continue;
            }

            try
            {
                return FontCollection.Load(face, weight, FontStretch.Normal, style);
            }
            catch (IOException)
            {
            }
            catch (InvalidDataException)
            {
            }
        }

        return null;
    }
}
