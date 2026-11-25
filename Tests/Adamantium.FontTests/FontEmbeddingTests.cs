using Adamantium.Fonts;
using NUnit.Framework;

namespace Adamantium.FontTests;

public class FontEmbeddingTests
{
    // OpenType 'OS/2' fsType: 2 restricted, 4 preview and print, 8 editable, 0x100 no subsetting, 0x200 bitmaps only.
    [TestCase(0x0000, true, true)]
    [TestCase(0x0002, false, true)]
    [TestCase(0x0004, true, true)]
    [TestCase(0x0008, true, true)]
    [TestCase(0x0006, true, true)]
    [TestCase(0x0104, true, false)]
    [TestCase(0x0200, false, true)]
    [TestCase(0x0208, false, true)]
    public void WhatTheLicenseSays(int flags, bool allowed, bool subset) =>
        Assert.That(FontEmbedding.From((ushort)flags), Is.EqualTo(new FontEmbedding(allowed, subset)));

    [Test]
    public void AFontsOwnLicense_IsRead() =>
        Assert.That(Typeface.LoadFont("OTFFonts/SourceSans3-Regular.otf", 3).GetFont(0).Embedding, Is.EqualTo(FontEmbedding.Free));
}
