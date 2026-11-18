using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Adamantium.ECS;

/// <summary>Where a part of a model is by the names from the model's root, "Frame/Wheel[1]": how the model's edits find
/// it again after a new import. A name met before among its siblings carries its count, and a part with no name always
/// does, "[0]"; the root's path is "".</summary>
public static class ModelPartPath
{
    private const char Separator = '/';
    private const char Escape = '\\';
    private const char CountStart = '[';
    private const char CountEnd = ']';

    /// <summary>The path of <paramref name="part"/> under <paramref name="model"/>; null when it is not under it.</summary>
    public static string Of(Entity model, Entity part)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(part);
        var segments = new List<string>();
        var at = part;
        while (at != model)
        {
            if (at?.Owner == null)
            {
                return null;
            }

            segments.Add(SegmentOf(at));
            at = at.Owner;
        }

        segments.Reverse();
        return string.Join(Separator, segments);
    }

    /// <summary>The part of <paramref name="model"/> at <paramref name="path"/>; null when it has none there.</summary>
    public static Entity Find(Entity model, string path)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(path);
        var at = model;
        var position = 0;
        while (at != null && position < path.Length)
        {
            var name = new StringBuilder();
            var count = 0;
            var counted = false;
            while (position < path.Length && path[position] != Separator)
            {
                var symbol = path[position];
                if (symbol == Escape && position + 1 < path.Length)
                {
                    name.Append(path[position + 1]);
                    position += 2;
                    continue;
                }

                if (symbol == CountStart)
                {
                    var end = path.IndexOf(CountEnd, position);
                    if (end < 0 || !int.TryParse(path.AsSpan(position + 1, end - position - 1), out count) || count < 0)
                    {
                        return null;
                    }

                    counted = true;
                    position = end + 1;
                    if (position < path.Length && path[position] != Separator)
                    {
                        return null;
                    }

                    continue;
                }

                name.Append(symbol);
                position++;
            }

            // A part with no name is always counted; an empty segment, a trailing one too, names no part.
            if ((name.Length == 0 && !counted) || position == path.Length - 1)
            {
                return null;
            }

            position++;
            at = FindChild(at, name.ToString(), count);
        }

        return at;
    }

    /// <summary>The path of the part named <paramref name="name"/> under the part at <paramref name="ownerPath"/>, the
    /// <paramref name="count"/>-th of its siblings so named: for its id before it is made.</summary>
    public static string Child(string ownerPath, string name, int count)
    {
        var segment = Segment(name ?? string.Empty, count);
        return string.IsNullOrEmpty(ownerPath) ? segment : ownerPath + Separator + segment;
    }

    /// <summary>The id of the part at <paramref name="path"/> of the model whose root's id is <paramref name="model"/>:
    /// the same each time the model is made, so what refers to the part finds it again.</summary>
    public static UInt128 UidOf(UInt128 model, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var length = 16 + Encoding.UTF8.GetByteCount(path);
        Span<byte> input = length <= 512 ? stackalloc byte[length] : new byte[length];
        BinaryPrimitives.WriteUInt128LittleEndian(input, model);
        Encoding.UTF8.GetBytes(path, input[16..]);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return BinaryPrimitives.ReadUInt128LittleEndian(hash);
    }

    private static string SegmentOf(Entity part)
    {
        var name = part.Name ?? string.Empty;
        var count = 0;
        foreach (var sibling in part.Owner.Dependencies)
        {
            if (sibling == part)
            {
                break;
            }

            if (sibling != null && string.Equals(sibling.Name ?? string.Empty, name, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return Segment(name, count);
    }

    private static string Segment(string name, int count)
    {
        var segment = new StringBuilder(name.Length + 4);
        foreach (var symbol in name)
        {
            if (symbol is Separator or Escape or CountStart)
            {
                segment.Append(Escape);
            }

            segment.Append(symbol);
        }

        if (count > 0 || name.Length == 0)
        {
            segment.Append(CountStart).Append(count).Append(CountEnd);
        }

        return segment.ToString();
    }

    private static Entity FindChild(Entity owner, string name, int count)
    {
        foreach (var child in owner.Dependencies)
        {
            if (child == null || !string.Equals(child.Name ?? string.Empty, name, StringComparison.Ordinal))
            {
                continue;
            }

            if (count == 0)
            {
                return child;
            }

            count--;
        }

        return null;
    }
}
