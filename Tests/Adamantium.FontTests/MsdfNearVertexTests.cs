using System.Collections.Generic;
using System.Linq;
using Adamantium.Fonts.TextureGeneration;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.FontTests;

/// <summary>Points a variable font's deltas leave a hair apart, where the outline means one corner: the distance field
/// must see one corner, joined, or the outside leaks in along a row of texels.</summary>
public class MsdfNearVertexTests
{
    // The cut end of a stroke as Bahnschrift's "3" has it at weight 380: the corner at (90, 360) twice, 1e-4 apart, and
    // a segment 1e-4 long at the other end.
    [Test]
    public void CornersAHairApart_AreJoinedIntoOne()
    {
        List<LineSegment2D> segments =
        [
            new(new Vector2<double>(98, 314), new Vector2<double>(90, 359.9999)),
            new(new Vector2<double>(90, 359.99987792968750), new Vector2<double>(285.3992, 359.9999)),
            new(new Vector2<double>(285.3992, 359.9999), new Vector2<double>(285.39923095703125, 359.99987792968750)),
            new(new Vector2<double>(285.39923095703125, 359.99987792968750), new Vector2<double>(293, 323)),
            new(new Vector2<double>(293, 323), new Vector2<double>(98, 314.00002)),
        ];

        var joined = MSDFGenerator.MsdfSegments(segments).Select(s => s.Segment).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(joined, Has.Length.EqualTo(4), "the hair-long segment is gone");
            for (var i = 0; i < joined.Length; i++)
            {
                Assert.That(joined[i].End, Is.EqualTo(joined[(i + 1) % joined.Length].Start), $"segment {i} ends where the next starts");
            }
        });
    }
}
