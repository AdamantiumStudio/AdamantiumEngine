using System.Linq;
using System.Threading;
using Adamantium.Fonts.TextureGeneration;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.FontTests;

// The dynamic atlas is filled from several threads: every request for new letters is rasterized and packed on a worker
// of its own. Two batches packed at once read the same cursor and landed in one cell, and a letter on screen showed
// another letter's pixels.
public class AtlasPackingConcurrencyTests
{
    private const int Threads = 8;
    private const int CellsPerThread = 400;

    [Test]
    public void BatchesPackedAtOnce_NeverShareACell()
    {
        var atlasData = new FontAtlasData(16, new Size<double>(1024, 1024), 8);
        var generator = new TextureAtlasGenerator(null, null, atlasData, FontParameters.Default(16));
        var cells = Enumerable.Range(0, Threads)
            .Select(t => Enumerable.Range(0, CellsPerThread)
                .Select(i => new GlyphTextureData(16, 16, (uint)(t * CellsPerThread + i), 0, ' '))
                .ToArray())
            .ToArray();

        using var start = new Barrier(Threads);
        var workers = Enumerable.Range(0, Threads).Select(t => new Thread(() =>
        {
            start.SignalAndWait();
            foreach (var cell in cells[t])
            {
                generator.CalculateTextureDataForAtlas([cell]);
            }
        })).ToList();
        workers.ForEach(x => x.Start());
        workers.ForEach(x => x.Join());

        var all = cells.SelectMany(x => x).ToArray();
        for (var i = 0; i < all.Length; i++)
        {
            for (var j = i + 1; j < all.Length; j++)
            {
                if (!Overlap(all[i], all[j]))
                {
                    continue;
                }

                Assert.Fail($"glyphs {all[i].GlyphIndex} and {all[j].GlyphIndex} were packed into one cell on layer " +
                            $"{all[i].DepthLayer}, at {all[i].BoundingRect.Left},{all[i].BoundingRect.Top} and " +
                            $"{all[j].BoundingRect.Left},{all[j].BoundingRect.Top}");
            }
        }
    }

    private static bool Overlap(GlyphTextureData a, GlyphTextureData b)
    {
        if (a.DepthLayer != b.DepthLayer)
        {
            return false;
        }

        var aw = (int)a.FullGlyphSize.Width;
        var ah = (int)a.FullGlyphSize.Height;
        var bw = (int)b.FullGlyphSize.Width;
        var bh = (int)b.FullGlyphSize.Height;
        return a.BoundingRect.Left < b.BoundingRect.Left + bw && b.BoundingRect.Left < a.BoundingRect.Left + aw
            && a.BoundingRect.Top < b.BoundingRect.Top + bh && b.BoundingRect.Top < a.BoundingRect.Top + ah;
    }
}
