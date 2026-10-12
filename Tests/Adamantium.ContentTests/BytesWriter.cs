using System;
using System.IO;
using Adamantium.Content.Pipeline;

namespace Adamantium.ContentTests;

/// <summary>Writes a few bytes, then fails if told to.</summary>
public sealed class BytesWriter : IContentWriter
{
    public BytesWriter(bool fails)
    {
        Fails = fails;
    }

    public bool Fails { get; }

    public string OutputExtension => ".bytes";

    public void Write(Stream stream, object content)
    {
        stream.Write([1, 2, 3]);
        if (Fails)
        {
            throw new InvalidOperationException("The writer failed halfway.");
        }
    }
}
