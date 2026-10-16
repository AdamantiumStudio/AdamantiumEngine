using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

[TestFixture]
public class MeshMetadataTests
{
    // The default line ran from its end to nowhere: the end was written into the start a second time.
    [Test]
    public void TheDefaultLine_RunsFromLeftToRight()
    {
        var metadata = MeshMetadata.Default();

        Assert.That(metadata.LineStart, Is.EqualTo(new Vector3(-0.5f, 0, 0)));
        Assert.That(metadata.LineEnd, Is.EqualTo(new Vector3(0.5f, 0, 0)));
    }
}
