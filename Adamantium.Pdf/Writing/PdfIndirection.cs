using System;
using System.Collections.Generic;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Writing;

/// <summary>Streams held inside other objects - an image in a form's resources, a soft mask's group in a graphics
/// state - made indirect: a stream can only be written as an indirect object (ISO 32000-2, 7.3.8). The objects given are
/// not changed; a container is copied only where something in it is.</summary>
internal static class PdfIndirection
{
    /// <summary><paramref name="value"/> with each stream inside it - not itself - replaced by the reference
    /// <paramref name="add"/> gives it. <paramref name="add"/> gets the stream as it was and lifts what is inside it
    /// itself, so it can tell a stream it has written before by the stream, not by a copy.</summary>
    public static PdfObject Lift(PdfObject value, Func<PdfStream, PdfReference> add)
    {
        ArgumentNullException.ThrowIfNull(add);
        return value is PdfStream stream ? LiftStream(stream, add) : Inner(value, add, 0);
    }

    /// <summary>The reference to <paramref name="stream"/>, written by <paramref name="write"/> unless
    /// <paramref name="written"/> has it - once, however many objects hold it. A stream that holds itself, directly or
    /// through others, is refused: it has no end to write.</summary>
    public static PdfReference Once(PdfStream stream, Dictionary<PdfStream, PdfReference> written,
        HashSet<PdfStream> lifting, Func<PdfStream, PdfReference> write)
    {
        if (written.TryGetValue(stream, out var reference))
        {
            return reference;
        }

        if (!lifting.Add(stream))
        {
            throw new PdfException("A stream to write holds itself; it has no end to write.");
        }

        try
        {
            reference = write(stream);
        }
        finally
        {
            lifting.Remove(stream);
        }

        written[stream] = reference;
        return reference;
    }

    private const int MaxDepth = 256;

    private static PdfStream LiftStream(PdfStream stream, Func<PdfStream, PdfReference> add)
    {
        var dictionary = (PdfDictionary)Inner(stream.Dictionary, add, 0);
        // The copy keeps the number it was read as: a file's encrypted bytes are told by it.
        return ReferenceEquals(dictionary, stream.Dictionary)
            ? stream
            : new PdfStream(dictionary, stream.Data) { Id = stream.Id };
    }

    private static PdfObject Inner(PdfObject value, Func<PdfStream, PdfReference> add, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new PdfException("An object to write nests deeper than any file needs.");
        }

        switch (value)
        {
            case PdfStream stream:
                return add(stream);
            case PdfDictionary dictionary:
            {
                PdfDictionary copy = null;
                foreach (var (key, item) in dictionary)
                {
                    var lifted = Inner(item, add, depth + 1);
                    if (copy == null && !ReferenceEquals(lifted, item))
                    {
                        copy = new PdfDictionary();
                        foreach (var (before, kept) in dictionary)
                        {
                            if (before == key)
                            {
                                break;
                            }

                            copy.Set(before, kept);
                        }
                    }

                    copy?.Set(key, lifted);
                }

                return copy ?? dictionary;
            }
            case PdfArray array:
            {
                List<PdfObject> copy = null;
                for (var index = 0; index < array.Count; index++)
                {
                    var lifted = Inner(array[index], add, depth + 1);
                    if (copy == null && !ReferenceEquals(lifted, array[index]))
                    {
                        copy = new List<PdfObject>(array.Count);
                        for (var before = 0; before < index; before++)
                        {
                            copy.Add(array[before]);
                        }
                    }

                    copy?.Add(lifted);
                }

                return copy != null ? new PdfArray(copy) : array;
            }
            default:
                return value;
        }
    }
}
