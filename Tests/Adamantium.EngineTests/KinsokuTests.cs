using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Japanese line breaking by the kinsoku rules of JIS X 4051, as the line breaking algorithm (UAX #14) keeps
/// them: no line starts with closing punctuation, a small kana or a prolonged sound mark, and none ends with opening
/// punctuation - a line at a time and a paragraph at a time, at many widths.</summary>
[TestFixture]
public class KinsokuTests
{
    private const string NotAtStart = "、。，．・：；？！ー」』）】〕〉》ぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ々";
    private const string NotAtEnd = "「『（【〔〈《";

    // Natsume Sōseki, "I Am a Cat" (1905).
    private const string Text =
        "「吾輩は猫である。名前はまだ無い。」どこで生れたかとんと見当がつかぬ。何でも薄暗いじめじめした所でニャーニャー" +
        "泣いていた事だけは記憶している。吾輩はここで始めて人間というものを見た。しかもあとで聞くとそれは書生という人間中で" +
        "一番獰悪な種族であったそうだ。（この書生というのは時々我々を捕えて煮て食うという話である。）";

    private static readonly Lazy<Typeface> SourceSans = new(() =>
        Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf")));

    [Test]
    public void LinesKeepTheKinsokuRules([Values] LineBreaking breaking)
    {
        var typeface = SourceSans.Value;
        var breaks = 0;
        for (var width = 60; width <= 400; width += 7)
        {
            var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, LineBreaking = breaking };
            layout.ProcessText(Text, 20, new Size<double>(width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
                HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            for (var line = 1; line < layout.LineCount; line++)
            {
                var start = layout.GetLine(line).Start;
                breaks++;
                Assert.That(NotAtStart, Does.Not.Contain(Text[start].ToString()), $"width {width}: line {line} starts with {Text[start]}");
                Assert.That(NotAtEnd, Does.Not.Contain(Text[start - 1].ToString()), $"width {width}: line {line - 1} ends with {Text[start - 1]}");
            }
        }

        Assert.That(breaks, Is.GreaterThan(100), "the text broke into lines often enough to tell");
    }
}
