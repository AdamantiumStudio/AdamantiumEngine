using NUnit.Framework;
using System.IO;
using Adamantium.EffectsCompiler;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Graphics.Effects;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;

namespace Adamantium.Engine.GraphicsTests
{
    [TestFixture]
    public class EffectTests
    {
        [TearDown]
        public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

        [Test]
        public void EffectLoadingTest()
        {
            var main = GpuFixture.Main;
            var device = GpuFixture.CreateRenderDevice();
            var effect = Effect.CompileFromFile(Path.Combine("EffectsData", "FontEffect.fx"), device);
        }

        // Line-rendering Step A3a (cheap de-risk before the full dispatch harness): just compiling+loading the
        // compute effect exercises the two biggest unknowns - that Slang compiles the BDA compute (uint* from a
        // uint64 device address) and that the driver creates a COMPUTE shader-object (vkCreateShadersEXT) on this GPU.
        // If this throws, the dispatch harness isn't worth building yet; if it passes, the rest is plumbing.
        [Test]
        public void ComputeShaderCompilesAndCreates()
        {
            var main = GpuFixture.Main;
            var device = GpuFixture.CreateRenderDevice();
            var effect = Effect.CompileFromFile(Path.Combine("EffectsData", "ComputeSmoke.fx"), device);

            Assert.That(effect, Is.Not.Null);
            Assert.That(effect.Techniques.Count, Is.GreaterThan(0), "compute technique should be present");
        }

        // A render device's constants live until ITS next frame. The main device's frame end used to rewind every device's
        // pool, so a device recording beside the window's loop got its earlier draws' constants overwritten by its later
        // ones.
        [Test]
        public void TheMainDevicesFrameEnd_LeavesARenderDevicesConstantsAlone()
        {
            var device = (GraphicsDevice)GpuFixture.CreateRenderDevice();
            using var effect = Effect.CompileFromFile(Path.Combine("EffectsData", "FontEffect.fx"), device);
            var pool = device.CurrentBufferPool;
            var first = pool.Allocate(64, 16);

            GpuFixture.Main.OnFrameFinished();
            var second = pool.Allocate(64, 16);

            Assert.That(second.Page != first.Page || second.Offset >= first.Offset + 64, Is.True,
                "the second constants landed on the first ones");
        }

        // More than a page holds gets a buffer of its own; the pages go on where they were, and after a reset the frame
        // starts on the first page again.
        [Test]
        public void AnUploadLargerThanAPage_GetsABufferOfItsOwn_AndThePagesGoOn()
        {
            var device = GpuFixture.CreateRenderDevice();
            using var pool = new EffectPass.DynamicBufferPool(device, 1024);
            var first = pool.Allocate(64, 16);

            var large = pool.Allocate(4096, 16);
            var next = pool.Allocate(64, 16);
            pool.Reset();
            var afterReset = pool.Allocate(64, 16);

            Assert.Multiple(() =>
            {
                Assert.That(large.Page, Is.Not.SameAs(first.Page));
                Assert.That(large.Page.TotalSize, Is.GreaterThanOrEqualTo(4096UL));
                Assert.That(next.Page, Is.SameAs(first.Page), "the large upload took no room of the pages");
                Assert.That(next.Offset, Is.GreaterThanOrEqualTo(first.Offset + 64));
                Assert.That(afterReset.Page, Is.SameAs(first.Page));
                Assert.That(afterReset.Offset, Is.Zero);
            });
        }

        // A texture a pass declares but nobody bound reads the stand-in. One never set wrote no index at all, so the shader
        // read heap slot 0 - and once the texture there was freed, the device was lost. One set to null, or whose texture
        // was disposed, kept pointing at that texture's slot.
        [TestCase("never set")]
        [TestCase("set to null")]
        [TestCase("disposed")]
        public void ATextureNotBoundToALiveOne_ReadsTheStandIn(string how)
        {
            const string source = @"
Texture2D Source;
uint64_t OutputAddress;
[shader(""compute"")]
[numthreads(1, 1, 1)]
void CS(uint3 tid : SV_DispatchThreadID) { float4* o = (float4*)OutputAddress; o[0] = Source.Load(int3(0, 0, 0)); }
technique T { pass P { EffectName = ""UnboundSource""; ComputeShader = CS; } }
";
            var compiled = EffectCompiler.Compile(source, "UnboundSource.fx");
            Assert.That(compiled.HasErrors, Is.False);
            var device = GpuFixture.CreateRenderDevice();
            using var effect = new Effect(device, compiled.EffectData);
            var pass = effect.Techniques[0].Passes[0];

            var texture = device.CreateTexture(new TextureDescription
            {
                Width = 1, Height = 1, Depth = 1, ArrayLayers = 1, MipLevels = 1, Samples = MSAALevel.None,
                Format = Format.R8G8B8A8_UNORM, InitialLayout = ImageLayout.Undefined,
                DesiredImageLayout = ImageLayout.ShaderReadOnlyOptimal, ImageType = ImageType._2d,
                ImageAspect = ImageAspectFlagBits.ColorBit, ImageTiling = ImageTiling.Optimal,
                Usage = ImageUsageFlagBits.SampledBit | ImageUsageFlagBits.TransferDstBit,
                Dimension = TextureDimension.Texture2D
            }, [0, 255, 0, 255]);

            if (how != "never set") effect.Parameters["Source"].SetResource(texture);
            if (how == "set to null") effect.Parameters["Source"].SetResource<ITexture>((ITexture)null);
            if (how == "disposed") texture.Dispose();

            var prms = new PresentationParameters(PresenterType.RenderTarget, 16, 16, System.IntPtr.Zero);
            using var presenter = GraphicsPresenter.Create(device, prms, "unbound_source");
            device.SetRenderTargets(presenter.RenderTarget);
            device.SetDepthBuffer(presenter.DepthBuffer);
            device.Presenter = presenter;

            Assert.That(device.BeginDraw(beforeRenderPass: _ => pass.Apply()), Is.True);
            device.EndDraw();
            device.Submit();
            device.FrameEnded();
            device.DeviceWaitIdle();
            if (how != "disposed") texture.Dispose();

            Assert.That(pass.ResourcesBound, Is.False, "the pass has to know it read the stand-in");
        }

        // The pool keeps one copy of a stage two effects share word for word. It used to refuse the second effect: every
        // shader was named after its effect, and one shader under two names read as a clash.
        [Test]
        public void TwoEffectsSharingAStage_ShareOneShader()
        {
            const string source = @"
float4 VS(float4 p : POSITION) : SV_Position { return p; }
float4 PS(float4 p : SV_Position) : SV_Target0 { return p; }
technique T { pass P { VertexShader = VS; PixelShader = PS; } }
";
            var first = EffectCompiler.Compile(source, "First.fx");
            var second = EffectCompiler.Compile(source.Replace("SV_Target0 { return p; }", "SV_Target0 { return p * 0.5; }"), "Second.fx");
            Assert.That(first.HasErrors || second.HasErrors, Is.False);
            var device = GpuFixture.CreateRenderDevice();
            using var firstEffect = new Effect(device, first.EffectData);
            var registered = device.DefaultEffectPool.RegisteredShaders.Count;

            using var secondEffect = new Effect(device, second.EffectData);

            Assert.That(device.DefaultEffectPool.RegisteredShaders.Count - registered, Is.EqualTo(1), "only the new pixel shader");
        }
    }
}
