using Adamantium.ECS.Components;
using Adamantium.Engine.Templates.Markers;
using Adamantium.Graphics.Core;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class MarkerIconTemplateTests
{
    private static readonly MarkerIconTemplate[] Templates = [new EmptyIconTemplate(), new GroupIconTemplate()];

    [TestCaseSource(nameof(Templates))]
    public void AMarkerIcon_IsAFilledFlatShape_ThatCanBeClicked(MarkerIconTemplate template)
    {
        var icon = template.BuildEntity(null, "Icon");
        var mesh = icon.GetComponent<MeshData>().Mesh;

        Assert.Multiple(() =>
        {
            Assert.That(mesh.MeshTopology, Is.EqualTo(PrimitiveType.TriangleList));
            Assert.That(mesh.Indices.Length, Is.GreaterThan(0).And.Matches<int>(count => count % 3 == 0));
            Assert.That(mesh.Bounds.Depth, Is.EqualTo(0).Within(1e-6));
            Assert.That(mesh.Bounds.Width, Is.InRange(0.5, 1));
            Assert.That(icon.GetComponent<BoxCollider>(), Is.Not.Null);
            Assert.That(icon.GetComponent<Material>().Transparency, Is.EqualTo(1), "an icon not selected would be invisible");
        });
    }
}
