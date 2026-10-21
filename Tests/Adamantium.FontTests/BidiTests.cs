using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Fonts.Text;
using NUnit.Framework;

namespace Adamantium.FontTests;

/// <summary>The Unicode Bidirectional Algorithm against the conformance tests of Unicode 16.0.0: every case of
/// BidiTest.txt (sequences of bidi classes) and BidiCharacterTest.txt (text with brackets), levels and visual order.</summary>
public class BidiTests
{
    private const int FailuresShown = 20;

    [Test]
    public void ClassSequences_ResolveAsUnicodeSays()
    {
        var failures = new List<string>();
        var cases = 0;
        string[] levels = [];
        int[] order = [];
        var number = 0;
        foreach (var line in File.ReadLines(UnicodeFile("BidiTest.txt")))
        {
            number++;
            var data = line.Split('#')[0].Trim();
            if (data.Length == 0)
            {
                continue;
            }

            if (data.StartsWith("@Levels:", StringComparison.Ordinal))
            {
                levels = Tokens(data.Substring("@Levels:".Length));
                continue;
            }

            if (data.StartsWith("@Reorder:", StringComparison.Ordinal))
            {
                order = Tokens(data.Substring("@Reorder:".Length)).Select(int.Parse).ToArray();
                continue;
            }

            if (data.StartsWith('@'))
            {
                continue;
            }

            var fields = data.Split(';');
            var classes = Tokens(fields[0]).Select(t => (BidiClass)Enum.Parse(typeof(BidiClass), t)).ToArray();
            var paragraphs = int.Parse(fields[1].Trim(), NumberStyles.HexNumber);
            foreach (var (bit, level) in new[] { (1, -1), (2, 0), (4, 1) })
            {
                if ((paragraphs & bit) == 0)
                {
                    continue;
                }

                cases++;
                var resolver = new BidiResolver(classes, null, level);
                Check(resolver, levels, order, $"line {number}, {fields[0].Trim()} at {level}", failures);
            }
        }

        Assert.That(failures, Is.Empty, $"{failures.Count} of {cases} cases:\n{string.Join("\n", failures.Take(FailuresShown))}");
    }

    [Test]
    public void Text_ResolvesAsUnicodeSays()
    {
        var failures = new List<string>();
        var cases = 0;
        var number = 0;
        foreach (var line in File.ReadLines(UnicodeFile("BidiCharacterTest.txt")))
        {
            number++;
            var data = line.Split('#')[0].Trim();
            if (data.Length == 0)
            {
                continue;
            }

            var fields = data.Split(';');
            var codepoints = Tokens(fields[0]).Select(t => int.Parse(t, NumberStyles.HexNumber)).ToArray();
            var direction = int.Parse(fields[1]);
            var classes = codepoints.Select(BidiProperties.Class).ToArray();
            var resolver = new BidiResolver(classes, codepoints, direction == 2 ? -1 : direction);
            cases++;
            if (resolver.ParagraphLevel != int.Parse(fields[2]))
            {
                failures.Add($"line {number}: paragraph level {resolver.ParagraphLevel}, expected {fields[2]}");
                continue;
            }

            Check(resolver, Tokens(fields[3]), Tokens(fields[4]).Select(int.Parse).ToArray(), $"line {number}", failures);
        }

        Assert.That(failures, Is.Empty, $"{failures.Count} of {cases} cases:\n{string.Join("\n", failures.Take(FailuresShown))}");
    }

    // Every case of the text the conformance tests leave to decide its own direction.
    [Test]
    public void TheDirectionOfText_IsTheLevelItsParagraphResolvesTo()
    {
        var failures = new List<string>();
        var cases = 0;
        var number = 0;
        foreach (var line in File.ReadLines(UnicodeFile("BidiCharacterTest.txt")))
        {
            number++;
            var fields = line.Split('#')[0].Trim().Split(';');
            if (fields.Length < 3 || fields[1].Trim() != "2")
            {
                continue;
            }

            var text = string.Concat(Tokens(fields[0]).Select(t => char.ConvertFromUtf32(int.Parse(t, NumberStyles.HexNumber))));
            var expected = fields[2].Trim() == "1" ? TextDirection.RightToLeft : TextDirection.LeftToRight;
            cases++;
            if (BidiParagraph.DirectionOf(text.AsSpan()) != expected)
            {
                failures.Add($"line {number}: expected {expected}");
            }
        }

        Assert.That(cases, Is.GreaterThan(0));
        Assert.That(failures, Is.Empty, $"{failures.Count} of {cases} cases:\n{string.Join("\n", failures.Take(FailuresShown))}");
    }

    [TestCase("שלום world", TextDirection.RightToLeft)]
    [TestCase("123 שלום", TextDirection.RightToLeft)]
    [TestCase("(world) שלום", TextDirection.LeftToRight)]
    [TestCase("⁧world⁩ שלום", TextDirection.RightToLeft)]
    [TestCase("⁧world שלום", TextDirection.LeftToRight)]
    [TestCase("123\nשלום", TextDirection.LeftToRight)]
    [TestCase("", TextDirection.LeftToRight)]
    public void ADirection_IsByTheFirstStrongCharacterOutsideIsolates(string text, TextDirection direction)
    {
        Assert.That(BidiParagraph.DirectionOf(text.AsSpan()), Is.EqualTo(direction));
    }

    private static void Check(BidiResolver resolver, string[] levels, int[] order, string name, List<string> failures)
    {
        var line = resolver.LineLevels(0, levels.Length);
        var kept = new List<int>();
        for (var i = 0; i < levels.Length; i++)
        {
            var expected = levels[i];
            if (resolver.IsRemoved(i))
            {
                if (expected != "x")
                {
                    failures.Add($"{name}: character {i} removed, expected level {expected}");
                    return;
                }

                continue;
            }

            if (expected == "x" || line[i] != int.Parse(expected))
            {
                failures.Add($"{name}: levels {Levels(resolver, line)}, expected {string.Join(" ", levels)}");
                return;
            }

            kept.Add(i);
        }

        var visual = BidiResolver.Reorder(kept.Select(i => line[i]).ToArray()).Select(k => kept[k]).ToArray();
        if (!visual.SequenceEqual(order))
        {
            failures.Add($"{name}: order {string.Join(" ", visual)}, expected {string.Join(" ", order)}");
        }
    }

    private static string Levels(BidiResolver resolver, byte[] line) =>
        string.Join(" ", line.Select((level, i) => resolver.IsRemoved(i) ? "x" : level.ToString(CultureInfo.InvariantCulture)));

    private static string[] Tokens(string text) => text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

    private static string UnicodeFile(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "Unicode", name);
}
