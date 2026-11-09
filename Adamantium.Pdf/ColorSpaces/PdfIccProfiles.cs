using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.Imaging.Icc;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.ColorSpaces;

/// <summary>The ICC profiles of one document, each read once: those of ICCBased spaces by their stream, and the
/// destination profile of the document's output intent (ISO 32000-2, 14.11.5), which device colors of as many
/// components are shown through. Colors go relative colorimetric, the default intent (8.6.5.8), with black point
/// compensation, as a reader shows a page.</summary>
internal sealed class PdfIccProfiles
{
    private readonly PdfDocument _document;
    private readonly Dictionary<PdfStream, IccTransform> _byStream = new(ReferenceEqualityComparer.Instance);
    private IccTransform _outputIntent;
    private bool _outputIntentRead;

    public PdfIccProfiles(PdfDocument document)
    {
        _document = document;
    }

    /// <summary>The transform of the profile in <paramref name="stream"/>, or null when it cannot be read or is not of
    /// <paramref name="components"/> device components.</summary>
    public IccTransform Of(PdfStream stream, int components)
    {
        if (!_byStream.TryGetValue(stream, out var transform))
        {
            transform = Read(stream);
            _byStream[stream] = transform;
        }

        return Fits(transform, components);
    }

    /// <summary>The transform of the profile <paramref name="data"/> holds - one embedded in an image - or null, as
    /// <see cref="Of"/>.</summary>
    public static IccTransform Of(ReadOnlySpan<byte> data, int components) => Fits(Transform(data), components);

    /// <summary>The transform of the output intent's profile, when it has <paramref name="components"/> components.
    /// </summary>
    public IccTransform OutputIntent(int components)
    {
        if (!_outputIntentRead)
        {
            _outputIntentRead = true;
            _outputIntent = ReadOutputIntent();
        }

        return Fits(_outputIntent, components);
    }

    private static IccTransform Fits(IccTransform transform, int components) =>
        transform?.Components == components ? transform : null;

    private IccTransform ReadOutputIntent()
    {
        if (_document.Resolve(_document.Catalog?[PdfKeys.OutputIntents]) is not PdfArray intents)
        {
            return null;
        }

        foreach (var entry in intents)
        {
            if (_document.Resolve(entry) is PdfDictionary intent &&
                _document.Resolve(intent[PdfKeys.DestOutputProfile]) is PdfStream profile)
            {
                return Read(profile);
            }
        }

        return null;
    }

    private IccTransform Read(PdfStream stream)
    {
        try
        {
            var data = _document.Decode(stream);
            return data.ImageFilter != null ? null : Transform(data.Data.Span);
        }
        catch (PdfException)
        {
            return null;
        }
    }

    private static IccTransform Transform(ReadOnlySpan<byte> data)
    {
        try
        {
            var profile = IccProfile.Read(data);
            return profile.ColorSpace is IccColorSpace.Gray or IccColorSpace.Rgb or IccColorSpace.Cmyk
                ? IccTransform.ToSrgb(profile, IccRenderingIntent.RelativeColorimetric, true)
                : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
