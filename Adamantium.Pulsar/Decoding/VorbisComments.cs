using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Adamantium.Pulsar.Decoding;

/// <summary>A list of comments as Vorbis writes them, FLAC's VORBIS_COMMENT block too: a vendor string, then fields
/// "NAME=value", each of a 32-bit length. Of them, what a game takes: LOOPSTART, and LOOPLENGTH or LOOPEND where the loop
/// does not end at the end of the sound, as game music is written with; and the speakers of
/// WAVEFORMATEXTENSIBLE_CHANNEL_MASK where they are not the usual.</summary>
internal readonly struct VorbisComments
{
    private VorbisComments(long loopStart, long loopLength, long loopEnd, Speakers speakers)
    {
        LoopStart = loopStart;
        LoopLength = loopLength;
        LoopEnd = loopEnd;
        Speakers = speakers;
    }

    /// <summary>-1 where it is not said, as the two after it.</summary>
    public long LoopStart { get; }

    public long LoopLength { get; }

    public long LoopEnd { get; }

    /// <summary>None where they are not said.</summary>
    public Speakers Speakers { get; }

    /// <summary>The comments of <paramref name="block"/>; what can not be read of a damaged one is not said.</summary>
    public static VorbisComments Read(ReadOnlySpan<byte> block)
    {
        long start = -1;
        long length = -1;
        long end = -1;
        var speakers = Speakers.None;
        if (TryTake(ref block, out _) && block.Length >= 4)
        {
            block = block[4..];
            while (TryTake(ref block, out var comment))
            {
                var text = Encoding.UTF8.GetString(comment);
                ReadNumber(text, "LOOPSTART=", ref start);
                ReadNumber(text, "LOOPLENGTH=", ref length);
                ReadNumber(text, "LOOPEND=", ref end);
                ReadSpeakers(text, ref speakers);
            }
        }

        return new VorbisComments(start, length, end, speakers);
    }

    /// <summary>The loop they say, within a sound of <paramref name="frames"/>, -1 where it is not known; null for none
    /// or one that can not be.</summary>
    public SoundLoop? LoopWithin(long frames)
    {
        var end = LoopEnd >= 0 ? LoopEnd : LoopLength > 0 ? LoopStart + LoopLength : frames;
        return LoopStart >= 0 && end > LoopStart && end <= int.MaxValue && (frames < 0 || end <= frames)
            ? new SoundLoop((int)LoopStart, (int)end)
            : null;
    }

    private static bool TryTake(ref ReadOnlySpan<byte> rest, out ReadOnlySpan<byte> taken)
    {
        taken = default;
        if (rest.Length < 4)
        {
            return false;
        }

        var length = BinaryPrimitives.ReadUInt32LittleEndian(rest);
        if (length > rest.Length - 4)
        {
            return false;
        }

        taken = rest.Slice(4, (int)length);
        rest = rest[(4 + (int)length)..];
        return true;
    }

    private static void ReadNumber(string comment, string name, ref long value)
    {
        if (comment.StartsWith(name, StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(comment.AsSpan(name.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            value = number;
        }
    }

    // In hexadecimal, after 0x.
    private static void ReadSpeakers(string comment, ref Speakers speakers)
    {
        const string name = "WAVEFORMATEXTENSIBLE_CHANNEL_MASK=0x";
        if (comment.StartsWith(name, StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(comment.AsSpan(name.Length), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
                out var mask))
        {
            speakers = (Speakers)mask;
        }
    }
}
