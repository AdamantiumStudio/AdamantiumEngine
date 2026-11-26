using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Adamantium.Pulsar.Decoding;

/// <summary>What Layer III fixes: the tables of ISO/IEC 11172-3 annex B and 13818-3, and what the standard's formulas
/// make once.</summary>
internal static class Mp3Tables
{
    // By rate: MPEG-1 44.1, 48, 32 kHz; MPEG-2 22.05, 24, 16; MPEG 2.5 11.025, 12, 8.
    private static readonly Mp3Bands[] Bands =
    [
        new([0, 4, 8, 12, 16, 20, 24, 30, 36, 44, 52, 62, 74, 90, 110, 134, 162, 196, 238, 288, 342, 418, 576],
            [0, 4, 8, 12, 16, 22, 30, 40, 52, 66, 84, 106, 136, 192]),
        new([0, 4, 8, 12, 16, 20, 24, 30, 36, 42, 50, 60, 72, 88, 106, 128, 156, 190, 230, 276, 330, 384, 576],
            [0, 4, 8, 12, 16, 22, 28, 38, 50, 64, 80, 100, 126, 192]),
        new([0, 4, 8, 12, 16, 20, 24, 30, 36, 44, 54, 66, 82, 102, 126, 156, 194, 240, 296, 364, 448, 550, 576],
            [0, 4, 8, 12, 16, 22, 30, 42, 58, 78, 104, 138, 180, 192]),
        new([0, 6, 12, 18, 24, 30, 36, 44, 54, 66, 80, 96, 116, 140, 168, 200, 238, 284, 336, 396, 464, 522, 576],
            [0, 4, 8, 12, 18, 24, 32, 42, 56, 74, 100, 132, 174, 192]),
        new([0, 6, 12, 18, 24, 30, 36, 44, 54, 66, 80, 96, 114, 136, 162, 194, 232, 278, 332, 394, 464, 540, 576],
            [0, 4, 8, 12, 18, 26, 36, 48, 62, 80, 104, 136, 180, 192]),
        new([0, 6, 12, 18, 24, 30, 36, 44, 54, 66, 80, 96, 116, 140, 168, 200, 238, 284, 336, 396, 464, 522, 576],
            [0, 4, 8, 12, 18, 26, 36, 48, 62, 80, 104, 134, 174, 192]),
        new([0, 6, 12, 18, 24, 30, 36, 44, 54, 66, 80, 96, 116, 140, 168, 200, 238, 284, 336, 396, 464, 522, 576],
            [0, 4, 8, 12, 18, 26, 36, 48, 62, 80, 104, 134, 174, 192]),
        new([0, 6, 12, 18, 24, 30, 36, 44, 54, 66, 80, 96, 116, 140, 168, 200, 238, 284, 336, 396, 464, 522, 576],
            [0, 4, 8, 12, 18, 26, 36, 48, 62, 80, 104, 134, 174, 192]),
        new([0, 12, 24, 36, 48, 60, 72, 88, 108, 132, 160, 192, 232, 280, 336, 400, 476, 566, 568, 570, 572, 574, 576],
            [0, 8, 16, 24, 36, 52, 72, 96, 124, 160, 162, 164, 166, 192]),
    ];

    // The length of a long block's lines before the transform: 36, by its type, windowed.
    private static readonly float[][] LongWindows = [Window(0), Window(1), null, Window(3)];
    private static readonly float[] LongCosines = Cosines(36, 18);
    private static readonly float[] ShortCosines = Cosines(12, 6);
    private static readonly float[] ShortWindow = [.. Enumerable.Range(0, 12).Select(at => MathF.Sin(MathF.PI / 12 * (at + 0.5f)))];
    // 2.4.3.4.10.3: N[i][k], i of 64, k of 32.
    private static readonly float[] Synthesis = MatrixOf();
    private static readonly float[] Powers = [.. Enumerable.Range(0, 8207).Select(value => (float)Math.Pow(value, 4.0 / 3))];

    /// <summary>The lengths of the scalefactors of MPEG-1, by scalefac_compress.</summary>
    public static readonly int[] Slen1 = [0, 0, 0, 0, 3, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4];

    public static readonly int[] Slen2 = [0, 1, 2, 3, 0, 1, 2, 3, 1, 2, 3, 1, 2, 3, 2, 3];

    /// <summary>What preflag adds to the long bands' scalefactors.</summary>
    public static readonly int[] Pretab = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 3, 3, 3, 2, 0];

    /// <summary>13818-3: how many scalefactors each of four parts has, by table, by long, short or mixed block.</summary>
    public static readonly int[,,] LowerPartitions =
    {
        { { 6, 5, 5, 5 }, { 9, 9, 9, 9 }, { 6, 9, 9, 9 } },
        { { 6, 5, 7, 3 }, { 9, 9, 12, 6 }, { 6, 9, 12, 6 } },
        { { 11, 10, 0, 0 }, { 18, 18, 0, 0 }, { 15, 18, 0, 0 } },
        { { 7, 7, 7, 0 }, { 12, 12, 12, 0 }, { 6, 15, 12, 0 } },
        { { 6, 6, 6, 3 }, { 12, 9, 9, 6 }, { 6, 12, 9, 6 } },
        { { 8, 8, 5, 0 }, { 15, 12, 9, 0 }, { 6, 18, 9, 0 } },
    };

    // Table 3-B.9: c(i), made the butterflies' cs and ca.
    private static readonly float[] AliasCoefficients = [-0.6f, -0.535f, -0.33f, -0.185f, -0.095f, -0.041f, -0.0142f, -0.0037f];

    public static readonly float[] AliasCs =
        [.. AliasCoefficients.Select(coefficient => 1 / MathF.Sqrt(1 + coefficient * coefficient))];

    public static readonly float[] AliasCa =
        [.. AliasCoefficients.Select(coefficient => coefficient / MathF.Sqrt(1 + coefficient * coefficient))];

    /// <summary>Table 3-B.3: D[i], the synthesis window.</summary>
    public static readonly float[] SynthesisWindow = ReadWindow();

    public static Mp3Bands BandsOf(Mp3FrameHeader header) =>
        Bands[(header.IsFirst ? 0 : header.IsLowest ? 6 : 3) + header.RateIndex];

    public static float Power43(int value) => value < 0 ? -Powers[-value] : Powers[value];

    /// <summary>MPEG-1's: tan(position * pi / 12).</summary>
    public static float IntensityRatio(int position) => MathF.Tan(position * MathF.PI / 12);

    /// <summary>The inverse transform of 18 lines to 36 samples, windowed by the block's type.</summary>
    public static void TransformLong(ReadOnlySpan<float> lines, Span<float> samples, int blockType)
    {
        var window = LongWindows[blockType];
        for (var at = 0; at < 36; at++)
        {
            var sum = 0f;
            var cosines = LongCosines.AsSpan(at * 18, 18);
            for (var line = 0; line < 18; line++)
            {
                sum += lines[line] * cosines[line];
            }

            samples[at] = sum * window[at];
        }
    }

    /// <summary>Three of 6 lines to 12 samples each, windowed and overlapped into 36, the first 6 and last 6 silent.
    /// </summary>
    public static void TransformShort(ReadOnlySpan<float> lines, Span<float> samples)
    {
        samples[..36].Clear();
        for (var window = 0; window < 3; window++)
        {
            var source = lines.Slice(window * 6, 6);
            for (var at = 0; at < 12; at++)
            {
                var sum = 0f;
                var cosines = ShortCosines.AsSpan(at * 6, 6);
                for (var line = 0; line < 6; line++)
                {
                    sum += source[line] * cosines[line];
                }

                samples[6 + window * 6 + at] += sum * ShortWindow[at];
            }
        }
    }

    /// <summary>The 64 values the synthesis filter takes in for 32 subband samples.</summary>
    public static void Matrix(ReadOnlySpan<float> subbands, Span<float> values)
    {
        var input = MemoryMarshal.Cast<float, Vector<float>>(subbands);
        for (var at = 0; at < 64; at++)
        {
            var row = MemoryMarshal.Cast<float, Vector<float>>(Synthesis.AsSpan(at * 32, 32));
            var sum = Vector<float>.Zero;
            for (var index = 0; index < row.Length; index++)
            {
                sum += row[index] * input[index];
            }

            values[at] = Vector.Sum(sum);
        }
    }

    // cos(pi / (2n) * (2i + 1 + n / 2) * (2k + 1)).
    private static float[] Cosines(int samples, int lines)
    {
        var cosines = new float[samples * lines];
        for (var at = 0; at < samples; at++)
        {
            for (var line = 0; line < lines; line++)
            {
                cosines[at * lines + line] =
                    (float)Math.Cos(Math.PI / (2 * samples) * (2 * at + 1 + samples / 2.0) * (2 * line + 1));
            }
        }

        return cosines;
    }

    // 2.4.3.4.10.2: of block types 0 (normal), 1 (start) and 3 (stop).
    private static float[] Window(int blockType)
    {
        var window = new float[36];
        for (var at = 0; at < 36; at++)
        {
            var normal = (float)Math.Sin(Math.PI / 36 * (at + 0.5));
            window[at] = blockType switch
            {
                1 => at < 18 ? normal : at < 24 ? 1 : at < 30 ? (float)Math.Sin(Math.PI / 12 * (at - 18 + 0.5)) : 0,
                3 => at < 6 ? 0 : at < 12 ? (float)Math.Sin(Math.PI / 12 * (at - 6 + 0.5)) : at < 18 ? 1 : normal,
                _ => normal,
            };
        }

        return window;
    }

    private static float[] MatrixOf()
    {
        var matrix = new float[64 * 32];
        for (var at = 0; at < 64; at++)
        {
            for (var subband = 0; subband < 32; subband++)
            {
                matrix[at * 32 + subband] = (float)Math.Cos((16 + at) * (2 * subband + 1) * Math.PI / 64);
            }
        }

        return matrix;
    }

    private static float[] ReadWindow()
    {
        using var resource = typeof(Mp3Tables).Assembly.GetManifestResourceStream("Mp3SynthesisWindow.txt");
        using var reader = new StreamReader(resource);
        var window = new float[512];
        var index = 0;
        while (reader.ReadLine() is { } line)
        {
            if (line.Length != 0 && !line.StartsWith('#'))
            {
                window[index++] = float.Parse(line, CultureInfo.InvariantCulture);
            }
        }

        return window;
    }
}
