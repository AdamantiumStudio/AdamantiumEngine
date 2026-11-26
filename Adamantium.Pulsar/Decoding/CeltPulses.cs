namespace Adamantium.Pulsar.Decoding;

/// <summary>The pyramid vector quantizer's codewords (RFC 6716, 4.3.4.2): of K pulses in N bins, the one an index names.
/// V(N, K), how many there are, by its recursion, once for every N and K a band can have.</summary>
internal static class CeltPulses
{
    // The widest band and the most pulses a band is given.
    private const int MostBins = 176;
    private const int MostPulses = 128;
    private const int Columns = MostPulses + 2;
    // V(N, K) by N and K, as large as a ulong goes: those past 32 bits no band is coded by.
    private static readonly ulong[] Counts = CountsOf();

    /// <summary>V(N, K).</summary>
    public static ulong CountOf(int bins, int pulses) => Counts[bins * Columns + pulses];

    /// <summary>The vector of <paramref name="pulses"/> pulses in <paramref name="vector"/>'s bins that the next index
    /// names.</summary>
    public static void Decode(Span<int> vector, int pulses, ref OpusRangeDecoder decoder)
    {
        var bins = vector.Length;
        var index = (ulong)decoder.DecodeUniform((uint)CountOf(bins, pulses));
        var left = pulses;
        for (var at = 0; at < bins; at++)
        {
            var rest = bins - at;
            var half = (CountOf(rest - 1, left) + CountOf(rest, left)) / 2;
            var sign = 1;
            if (index >= half)
            {
                sign = -1;
                index -= half;
            }

            var before = left;
            half -= CountOf(rest - 1, left);
            while (half > index)
            {
                left--;
                half -= CountOf(rest - 1, left);
            }

            vector[at] = sign * (before - left);
            index -= half;
        }
    }

    // V(N, K) = V(N - 1, K) + V(N, K - 1) + V(N - 1, K - 1), V(N, 0) = 1, V(0, K) = 0.
    private static ulong[] CountsOf()
    {
        var counts = new ulong[(MostBins + 1) * Columns];
        counts[0] = 1;
        for (var bins = 1; bins <= MostBins; bins++)
        {
            counts[bins * Columns] = 1;
            for (var pulses = 1; pulses < Columns; pulses++)
            {
                var sum = counts[(bins - 1) * Columns + pulses] + counts[bins * Columns + pulses - 1];
                sum = sum < counts[(bins - 1) * Columns + pulses] ? ulong.MaxValue : sum;
                var total = sum + counts[(bins - 1) * Columns + pulses - 1];
                counts[bins * Columns + pulses] = total < sum ? ulong.MaxValue : total;
            }
        }

        return counts;
    }
}
