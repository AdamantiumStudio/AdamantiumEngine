using System;
using System.Collections.Generic;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Functions;

/// <summary>Reads functions out of a file. A function that cannot be read - an unknown type, entries missing, samples
/// cut short - is null; stitching nests no deeper than <see cref="MaxDepth"/>, and an indirect function is read once
/// however often it is referenced, so a file cannot make the tree grow exponentially.</summary>
public static class PdfFunctions
{
    public const int MaxDepth = 16;

    /// <summary>The most samples a type 0 function may hold, outputs included.</summary>
    public const int MaxSamples = 1 << 22;

    /// <summary>A function, or an array of one-output functions read as one.</summary>
    public static PdfFunction Create(PdfObject value, PdfDocument document)
    {
        var read = new Dictionary<int, PdfFunction>();
        if (document.Resolve(value) is not PdfArray array)
        {
            return Create(value, document, 0, read);
        }

        var parts = new List<PdfFunction>();
        foreach (var item in array)
        {
            var part = document.Resolve(item) is PdfArray ? null : Create(item, document, 1, read);
            if (part == null)
            {
                return null;
            }

            parts.Add(part);
        }

        return parts.Count > 0 ? new PdfFunctionArray(parts) : null;
    }

    private static PdfFunction Create(PdfObject value, PdfDocument document, int depth,
        Dictionary<int, PdfFunction> read)
    {
        if (value is PdfReference reference)
        {
            if (read.TryGetValue(reference.Id.Number, out var known))
            {
                return known;
            }

            read[reference.Id.Number] = null;
            var function = Create(document.Resolve(value), document, depth, read);
            read[reference.Id.Number] = function;
            return function;
        }

        if (depth > MaxDepth)
        {
            return null;
        }

        var stream = value as PdfStream;
        var dictionary = stream?.Dictionary ?? value as PdfDictionary;
        if (dictionary == null)
        {
            return null;
        }

        var domain = PdfValues.Numbers(dictionary["Domain"], document);
        var range = PdfValues.Numbers(dictionary["Range"], document);
        if (domain == null || domain.Length < 2 || domain.Length % 2 != 0)
        {
            return null;
        }

        try
        {
            return PdfValues.Integer(dictionary["FunctionType"], document, -1) switch
            {
                0 when stream != null => Sampled(dictionary, stream, domain, range, document),
                2 => Exponential(dictionary, domain, range, document),
                3 => Stitching(dictionary, domain, range, document, depth, read),
                4 when stream != null && range is { Length: >= 2 } =>
                    new PdfPostScriptFunction(domain, range, document.Decode(stream).Data.Span),
                _ => null,
            };
        }
        catch (PdfException)
        {
            return null;
        }
    }

    private static PdfFunction Exponential(PdfDictionary dictionary, double[] domain, double[] range,
        PdfDocument document)
    {
        var c0 = PdfValues.Numbers(dictionary["C0"], document) ?? [0];
        var c1 = PdfValues.Numbers(dictionary["C1"], document) ?? [1];
        if (c0.Length != c1.Length)
        {
            return null;
        }

        return new PdfExponentialFunction(domain, range, c0, c1, PdfValues.Number(dictionary["N"], document, 1));
    }

    private static PdfFunction Stitching(PdfDictionary dictionary, double[] domain, double[] range,
        PdfDocument document, int depth, Dictionary<int, PdfFunction> read)
    {
        if (document.Resolve(dictionary["Functions"]) is not PdfArray items || items.Count == 0)
        {
            return null;
        }

        var functions = new List<PdfFunction>();
        foreach (var item in items)
        {
            var function = Create(item, document, depth + 1, read);
            if (function == null)
            {
                return null;
            }

            functions.Add(function);
        }

        var bounds = PdfValues.Numbers(dictionary["Bounds"], document) ?? [];
        var encode = PdfValues.Numbers(dictionary["Encode"], document) ?? [];
        return bounds.Length == functions.Count - 1
            ? new PdfStitchingFunction(domain, range, functions, bounds, encode)
            : null;
    }

    private static PdfFunction Sampled(PdfDictionary dictionary, PdfStream stream, double[] domain, double[] range,
        PdfDocument document)
    {
        var sizes = PdfValues.Numbers(dictionary["Size"], document);
        var bits = PdfValues.Integer(dictionary["BitsPerSample"], document);
        var inputs = domain.Length / 2;
        if (range == null || range.Length < 2 || sizes == null || sizes.Length != inputs ||
            inputs > PdfSampledFunction.MaxInputs || bits is not (1 or 2 or 4 or 8 or 12 or 16 or 24 or 32))
        {
            return null;
        }

        var most = Math.Min(MaxSamples, document.DecodeLimits.MaxDecodedLength / sizeof(float));
        var size = new int[inputs];
        long total = range.Length / 2;
        for (var i = 0; i < inputs; i++)
        {
            if (sizes[i] < 1 || sizes[i] > most)
            {
                return null;
            }

            size[i] = (int)sizes[i];
            total *= size[i];
            if (total > most)
            {
                return null;
            }
        }

        var encode = PdfValues.Numbers(dictionary["Encode"], document);
        if (encode == null || encode.Length < 2 * inputs)
        {
            encode = new double[2 * inputs];
            for (var i = 0; i < inputs; i++)
            {
                encode[2 * i + 1] = size[i] - 1;
            }
        }

        var decode = PdfValues.Numbers(dictionary["Decode"], document);
        if (decode == null || decode.Length < range.Length)
        {
            decode = range;
        }

        var data = document.Decode(stream).Data.Span;
        if (data.Length * 8L < total * bits)
        {
            return null;
        }

        var samples = new float[total];
        var max = (double)((1L << bits) - 1);
        for (long k = 0; k < total; k++)
        {
            samples[k] = (float)(ReadBits(data, k * bits, bits) / max);
        }

        return new PdfSampledFunction(domain, range, size, encode, decode, samples);
    }

    private static long ReadBits(ReadOnlySpan<byte> data, long bitOffset, int bits)
    {
        long value = 0;
        for (var i = 0; i < bits; i++)
        {
            var bit = bitOffset + i;
            value = (value << 1) | (uint)((data[(int)(bit >> 3)] >> (7 - (int)(bit & 7))) & 1);
        }

        return value;
    }
}
