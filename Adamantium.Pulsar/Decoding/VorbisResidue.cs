namespace Adamantium.Pulsar.Decoding;

/// <summary>A Vorbis residue (Vorbis I, 8): vectors in partitions, each partition's class read first, then its values
/// in up to eight passes of vector codebooks. Type 0 interleaves a partition's values by the book's dimensions, type 1
/// does not, type 2 is type 1 of the vectors interleaved into one.</summary>
internal sealed class VorbisResidue
{
    private const int Passes = 8;

    private readonly int _type;
    private readonly int _begin;
    private readonly int _end;
    private readonly int _partitionSize;
    private readonly int _classifications;
    private readonly int _classBook;
    // By class and pass: class * 8 + pass; -1 for none.
    private readonly int[] _books;
    // By vector and partition, the classes read in the first pass.
    private readonly int[][] _classes;
    // Type 2's one vector of all the channels', decoded as type 1's are.
    private readonly float[][] _interleaved;
    private readonly bool[] _none = [false];

    public VorbisResidue(ref VorbisBits bits, int type, VorbisCodebook[] books, int channels, int longestHalf)
    {
        _type = type;
        _begin = (int)bits.Read(24);
        _end = (int)bits.Read(24);
        _partitionSize = (int)bits.Read(24) + 1;
        _classifications = (int)bits.Read(6) + 1;
        _classBook = (int)bits.Read(8);
        var cascade = new int[_classifications];
        for (var type0 = 0; type0 < _classifications; type0++)
        {
            var low = (int)bits.Read(3);
            var high = bits.ReadFlag() ? (int)bits.Read(5) : 0;
            cascade[type0] = high * 8 + low;
        }

        _books = new int[_classifications * Passes];
        for (var type0 = 0; type0 < _classifications; type0++)
        {
            for (var pass = 0; pass < Passes; pass++)
            {
                var book = -1;
                if ((cascade[type0] >> pass & 1) != 0)
                {
                    book = (int)bits.Read(8);
                    if (book >= books.Length || !books[book].HasValues)
                    {
                        throw Damaged($"a residue names codebook {book}, which there is not, or has no values");
                    }
                }

                _books[type0 * Passes + pass] = book;
            }
        }

        // 8.6.1: the classes of a codeword can not be more than the book's entries.
        if (bits.IsEnded || _classBook >= books.Length || books[_classBook].Dimensions == 0 ||
            Math.Pow(_classifications, books[_classBook].Dimensions) > books[_classBook].Entries)
        {
            throw Damaged("a residue's classes can not be read");
        }

        // A vector of type 2 is all the channels'; the most partitions of one, those of the longest block.
        var longest = type == 2 ? longestHalf * channels : longestHalf;
        var partitions = longest / _partitionSize + books[_classBook].Dimensions;
        _classes = [.. Enumerable.Range(0, type == 2 ? 1 : channels).Select(_ => new int[partitions])];
        if (type == 2)
        {
            _interleaved = [new float[longest]];
        }
    }

    /// <summary>Decodes into <paramref name="vectors"/>, each <paramref name="half"/> long and cleared first, those
    /// marked in <paramref name="skipped"/> left silent. The packet's end within it leaves what was read.</summary>
    public void Decode(ref VorbisBits bits, VorbisCodebook[] books, float[][] vectors, bool[] skipped, int count, int half)
    {
        for (var vector = 0; vector < count; vector++)
        {
            vectors[vector].AsSpan(0, half).Clear();
        }

        if (_type != 2)
        {
            DecodeVectors(ref bits, books, vectors, skipped, count, half);
            return;
        }

        // Type 2: none decoded where all are skipped; otherwise all, as one, then taken apart.
        var all = true;
        for (var vector = 0; vector < count; vector++)
        {
            all &= skipped[vector];
        }

        if (all)
        {
            return;
        }

        var interleaved = _interleaved[0].AsSpan(0, half * count);
        interleaved.Clear();
        DecodeVectors(ref bits, books, _interleaved, _none, 1, half * count);
        for (var index = 0; index < half; index++)
        {
            for (var vector = 0; vector < count; vector++)
            {
                vectors[vector][index] = interleaved[index * count + vector];
            }
        }
    }

    // 8.6.2, as type 1 or 0.
    private void DecodeVectors(ref VorbisBits bits, VorbisCodebook[] books, float[][] vectors, bool[] skipped, int count,
        int size)
    {
        var begin = Math.Min(_begin, size);
        var end = Math.Min(_end, size);
        var partitions = (end - begin) / _partitionSize;
        if (partitions <= 0)
        {
            return;
        }

        var classBook = books[_classBook];
        var perCodeword = classBook.Dimensions;
        for (var pass = 0; pass < Passes; pass++)
        {
            var partition = 0;
            while (partition < partitions)
            {
                if (pass == 0)
                {
                    for (var vector = 0; vector < count; vector++)
                    {
                        if (skipped[vector])
                        {
                            continue;
                        }

                        var entry = classBook.ReadEntry(ref bits);
                        if (entry < 0)
                        {
                            return;
                        }

                        var classes = _classes[vector];
                        for (var at = perCodeword - 1; at >= 0; at--)
                        {
                            classes[at + partition] = entry % _classifications;
                            entry /= _classifications;
                        }
                    }
                }

                for (var at = 0; at < perCodeword && partition < partitions; at++, partition++)
                {
                    for (var vector = 0; vector < count; vector++)
                    {
                        if (skipped[vector])
                        {
                            continue;
                        }

                        var book = _books[_classes[vector][partition] * Passes + pass];
                        if (book >= 0 && !DecodePartition(ref bits, books[book], vectors[vector],
                            begin + partition * _partitionSize))
                        {
                            return;
                        }
                    }
                }
            }
        }
    }

    // 8.6.3 and 8.6.4; false where the packet ended.
    private bool DecodePartition(ref VorbisBits bits, VorbisCodebook book, float[] vector, int offset)
    {
        var dimensions = book.Dimensions;
        if (_type == 0)
        {
            var step = _partitionSize / dimensions;
            for (var index = 0; index < step; index++)
            {
                var values = book.ReadVector(ref bits);
                if (values.IsEmpty)
                {
                    return false;
                }

                for (var dimension = 0; dimension < dimensions; dimension++)
                {
                    vector[offset + index + dimension * step] += values[dimension];
                }
            }

            return true;
        }

        for (var index = 0; index < _partitionSize;)
        {
            var values = book.ReadVector(ref bits);
            if (values.IsEmpty)
            {
                return false;
            }

            for (var dimension = 0; dimension < dimensions && index < _partitionSize; dimension++)
            {
                vector[offset + index++] += values[dimension];
            }
        }

        return true;
    }

    private static InvalidDataException Damaged(string how) => new($"The Vorbis stream is damaged: {how}.");
}
