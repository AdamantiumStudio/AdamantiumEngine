using Adamantium.ECS.Components;
using Adamantium.Engine.EntityServices;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>An entity drawn as the view is, or in one of the view's own ways in its place - either way round.</summary>
[TestFixture]
public class EditorDisplayTests
{
    [TestCase(ViewDisplay.Shaded, true, false)]
    [TestCase(ViewDisplay.Wireframe, false, true)]
    [TestCase(ViewDisplay.ShadedWireframe, true, true)]
    public void AsView_DrawsAsTheViewIs(ViewDisplay view, bool shaded, bool wired)
    {
        Assert.That(ForwardPlusRenderingProcessor.Drawn(view, EntityDisplay.AsView), Is.EqualTo((shaded, wired)));
    }

    [TestCase(EntityDisplay.Shaded, true, false)]
    [TestCase(EntityDisplay.Wireframe, false, true)]
    [TestCase(EntityDisplay.ShadedWireframe, true, true)]
    [TestCase(EntityDisplay.Collider, false, false)]
    public void AnEntitysOwnWay_TakesTheViewsPlace_WhicheverWayTheViewIs(EntityDisplay entity, bool shaded, bool wired)
    {
        Assert.Multiple(() =>
        {
            foreach (var view in new[] { ViewDisplay.Shaded, ViewDisplay.Wireframe, ViewDisplay.ShadedWireframe })
            {
                Assert.That(ForwardPlusRenderingProcessor.Drawn(view, entity), Is.EqualTo((shaded, wired)), $"{view}");
            }
        });
    }
}
