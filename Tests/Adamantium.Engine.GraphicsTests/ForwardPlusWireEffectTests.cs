using System.IO;
using Adamantium.Graphics.Core.EffectsFramework;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>The Forward+ effect that draws a mesh's wireframe over its shading from where a pixel lies in its triangle.</summary>
[TestFixture]
public class ForwardPlusWireEffectTests
{
    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void TheWireEffect_MakesItsShaders_OnADeviceWithBarycentrics()
    {
        var device = GpuFixture.CreateRenderDevice();
        Assume.That(device.MainDevice.FragmentBarycentricSupported, "the GPU has no VK_KHR_fragment_shader_barycentric");

        using var effect = Effect.CompileFromFile(Path.Combine("EffectsData", "ForwardPlusWireEffect.fx"), device);

        var technique = effect.Techniques["ForwardPlusWire"];
        Assert.That(technique.Passes["Colored"], Is.Not.Null);
        Assert.That(technique.Passes["Textured"], Is.Not.Null);
    }
}
