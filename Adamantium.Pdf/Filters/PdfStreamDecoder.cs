using System;
using System.Collections.Generic;
using Adamantium.Pdf.Content;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Filters;

/// <summary>Applies a stream's <c>/Filter</c> chain with its <c>/DecodeParms</c> (ISO 32000-2, 7.4), stopping at an
/// image filter. Decryption comes before: the bytes given here are already decrypted.</summary>
public static class PdfStreamDecoder
{
    public static PdfDecodedStream Decode(PdfDictionary dictionary, ReadOnlyMemory<byte> data,
        IPdfObjectResolver resolver, PdfDecodeLimits limits = null)
    {
        limits ??= PdfDecodeLimits.Default;
        var filters = new List<string>();
        var parameters = new List<PdfDictionary>();
        ReadChain(dictionary, resolver, filters, parameters);
        if (filters.Count > limits.MaxFilters)
        {
            throw new PdfException("A stream chains " + filters.Count + " filters.");
        }

        var current = data;
        for (var i = 0; i < filters.Count; i++)
        {
            var name = filters[i];
            var options = parameters[i];
            switch (name)
            {
                case PdfFilterNames.FlateDecode:
                case PdfAbbreviations.FlateDecode:
                    current = Predict(PdfFlateFilter.Decode(current, limits), options, resolver);
                    break;
                case PdfFilterNames.LZWDecode:
                case PdfAbbreviations.LZWDecode:
                    var earlyChange = Integer(options, PdfKeys.EarlyChange, resolver, 1) != 0;
                    current = Predict(PdfLzwFilter.Decode(current.Span, earlyChange, limits), options, resolver);
                    break;
                case PdfFilterNames.ASCIIHexDecode:
                case PdfAbbreviations.ASCIIHexDecode:
                    current = PdfAsciiHexFilter.Decode(current.Span);
                    break;
                case PdfFilterNames.ASCII85Decode:
                case PdfAbbreviations.ASCII85Decode:
                    current = PdfAscii85Filter.Decode(current.Span, limits);
                    break;
                case PdfFilterNames.RunLengthDecode:
                case PdfAbbreviations.RunLengthDecode:
                    current = PdfRunLengthFilter.Decode(current.Span, limits);
                    break;
                case PdfFilterNames.Crypt:
                    break;
                case PdfFilterNames.DCTDecode:
                case PdfAbbreviations.DCTDecode:
                    return new PdfDecodedStream(current, PdfFilterNames.DCTDecode, options);
                case PdfFilterNames.CCITTFaxDecode:
                case PdfAbbreviations.CCITTFaxDecode:
                    return new PdfDecodedStream(current, PdfFilterNames.CCITTFaxDecode, options);
                case PdfFilterNames.JPXDecode:
                case PdfFilterNames.JBIG2Decode:
                    return new PdfDecodedStream(current, name, options);
                default:
                    throw new PdfException("Unknown stream filter /" + name + ".");
            }
        }

        return new PdfDecodedStream(current);
    }

    private static void ReadChain(PdfDictionary dictionary, IPdfObjectResolver resolver, List<string> filters,
        List<PdfDictionary> parameters)
    {
        var filter = resolver.Resolve(dictionary[PdfKeys.Filter]);
        var options = resolver.Resolve(dictionary[PdfKeys.DecodeParms]);
        if (filter is PdfName single)
        {
            filters.Add(single.Value);
            parameters.Add(OptionsAt(options, 0, resolver));
            return;
        }

        if (filter is not PdfArray chain)
        {
            return;
        }

        for (var i = 0; i < chain.Count; i++)
        {
            if (resolver.Resolve(chain[i]) is PdfName name)
            {
                filters.Add(name.Value);
                parameters.Add(OptionsAt(options, i, resolver));
            }
        }
    }

    private static PdfDictionary OptionsAt(PdfObject options, int index, IPdfObjectResolver resolver)
    {
        if (options is PdfDictionary dictionary)
        {
            return index == 0 ? dictionary : null;
        }

        if (options is PdfArray array && index < array.Count)
        {
            return resolver.Resolve(array[index]) as PdfDictionary;
        }

        return null;
    }

    private static Memory<byte> Predict(Memory<byte> data, PdfDictionary options, IPdfObjectResolver resolver)
    {
        if (options == null)
        {
            return data;
        }

        var predictor = Integer(options, PdfKeys.Predictor, resolver, 1);
        if (predictor <= 1)
        {
            return data;
        }

        return PdfPredictor.Undo(data, predictor, Integer(options, PdfKeys.Colors, resolver, 1),
            Integer(options, PdfKeys.BitsPerComponent, resolver, 8), Integer(options, PdfKeys.Columns, resolver, 1));
    }

    private static int Integer(PdfDictionary options, string key, IPdfObjectResolver resolver, int fallback)
    {
        if (options == null)
        {
            return fallback;
        }

        return resolver.Resolve(options[key]) switch
        {
            PdfInteger integer => (int)Math.Clamp(integer.Value, int.MinValue, int.MaxValue),
            PdfReal real => (int)real.Value,
            _ => fallback,
        };
    }
}
