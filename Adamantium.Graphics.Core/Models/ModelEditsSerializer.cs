using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core.Assets;
using MessagePack;
using MessagePack.Resolvers;

namespace Adamantium.Graphics.Core.Models;

/// <summary>(De)serializes <see cref="ModelEdits"/>, the <see cref="AssetFile.Edits"/> section of a model's asset file.
/// </summary>
public static class ModelEditsSerializer
{
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    public static byte[] Serialize(ModelEdits edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        return MessagePackSerializer.Serialize(edits, Options);
    }

    /// <summary>The edits <paramref name="data"/> holds; throws <see cref="AssetFormatException"/> when they cannot be
    /// read.</summary>
    public static ModelEdits Deserialize(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ModelEdits edits;
        try
        {
            edits = MessagePackSerializer.Deserialize<ModelEdits>(data, Options);
        }
        catch (MessagePackSerializationException exception)
        {
            throw new AssetFormatException("its edits cannot be read", exception);
        }

        if (edits != null)
        {
            edits.Parts = edits.Parts == null
                ? new Dictionary<string, PartEdits>(StringComparer.Ordinal)
                : new Dictionary<string, PartEdits>(edits.Parts, StringComparer.Ordinal);
        }

        return edits;
    }
}
