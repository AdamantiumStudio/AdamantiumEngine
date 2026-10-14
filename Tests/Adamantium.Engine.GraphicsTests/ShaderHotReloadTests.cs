using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Adamantium.EffectsCompiler;
using Adamantium.FX;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

[TestFixture]
public class ShaderHotReloadTests
{
    private const int Count = 64;

    private const string Bumped = @"
#include ""Bump.fxh""

uint64_t OutputAddress;
uint Count;

[shader(""compute"")]
[numthreads(64, 1, 1)]
void BumpCS(uint3 tid : SV_DispatchThreadID)
{
    if (tid.x >= Count)
        return;

    uint* output = (uint*)OutputAddress;
    output[tid.x] = tid.x + Bump();
}

technique Bumped { pass Run { ComputeShader = BumpCS; } }
";

    private static readonly string SmokePath = Path.Combine("EffectsData", "ComputeSmoke.fx");

    private readonly List<string> folders = [];

    [TearDown]
    public void TearDown()
    {
        GpuFixture.ReleaseRenderDevices();
        foreach (var folder in folders)
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (IOException)
            {
            }
        }

        folders.Clear();
    }

    [Test]
    public void AnEditedHeader_ReachesTheRunningEffect()
    {
        var (device, effect, pass, output, header, _) = Bumping();
        using (effect)
        using (output)
        {
            File.WriteAllText(header, "uint Bump() { return 5; }\n");

            Assert.That(RunUntil(device, pass, output, written => written[0] == 5)[0], Is.EqualTo(5));
        }
    }

    [Test]
    public void ASlowEffect_DoesNotHoldUpTheOthers()
    {
        var folder = NewFolder();
        var device = GpuFixture.CreateRenderDevice();
        var (slowEffect, slowPass, slowOutput, _) = Following(device, folder, "Slow.fx", Slow(60));
        var (fastEffect, fastPass, fastOutput, _) = Following(device, folder, "Fast.fx", Bumped);
        using (slowEffect)
        using (slowOutput)
        using (fastEffect)
        using (fastOutput)
        {
            File.WriteAllText(Path.Combine(folder, "Bump.fxh"), "uint Bump() { return 5; }\n");

            Assert.That(RunUntil(device, fastPass, fastOutput, written => written[0] == 5)[0], Is.EqualTo(5));
            Assert.That(Run(device, slowPass, slowOutput)[0], Is.EqualTo(1), "the fast effect waited for the slow one");
            Assert.That(RunUntil(device, slowPass, slowOutput, written => written[0] == 5)[0], Is.EqualTo(5));
        }
    }

    [Test]
    public void ASourceThatStopsCompiling_IsReported_AndTheRunningVersionStays()
    {
        var (device, effect, pass, output, _, source) = Bumping();
        using (effect)
        using (output)
        {
            string failedSource = null;
            using var failed = new ManualResetEventSlim();
            Action<string, string> onFailed = (path, _) =>
            {
                failedSource = path;
                failed.Set();
            };

            GpuFixture.Main.ShaderHotReload.Failed += onFailed;
            try
            {
                File.WriteAllText(source, Bumped.Replace("tid.x + Bump()", "tid.x + Bump("));
                Assert.That(failed.Wait(TimeSpan.FromSeconds(15)), Is.True, "no compile failure was reported");
            }
            finally
            {
                GpuFixture.Main.ShaderHotReload.Failed -= onFailed;
            }

            Assert.That(Path.GetFullPath(failedSource), Is.EqualTo(Path.GetFullPath(source)));
            Assert.That(Run(device, pass, output)[0], Is.EqualTo(1));
        }
    }

    [Test]
    public void InManualMode_ASavedChange_WaitsForApply()
    {
        var (device, effect, pass, output, header, source) = Bumping();
        using (effect)
        using (output)
        {
            Manually(() =>
            {
                File.WriteAllText(header, "uint Bump() { return 5; }\n");
                WaitUntil(() => Changed(header), "the watcher did not report the save");
                Thread.Sleep(500);
                Assert.That(Changed(header), Is.True, "a save was taken for compiling in Manual mode");
                Assert.That(Run(device, pass, output)[0], Is.EqualTo(1));

                var reports = Reload.ApplyAsync().GetAwaiter().GetResult();

                Assert.That(ReportOn(reports, source).Compiled, Is.True);
                Assert.That(Run(device, pass, output)[0], Is.EqualTo(5));
                Assert.That(Changed(header), Is.False);
            });
        }
    }

    [Test]
    public void FilesAppliedRightAfterTheirSaves_AreNotLeftAsChanged()
    {
        var (device, effect, pass, output, header, source) = Bumping();
        using (effect)
        using (output)
        {
            Manually(() =>
            {
                for (var bump = 5; bump <= 7; bump++)
                {
                    File.WriteAllText(header, $"uint Bump() {{ return {bump}; }}\n");

                    var reports = Reload.ApplyAsync([header]).GetAwaiter().GetResult();

                    Assert.That(ReportOn(reports, source).Compiled, Is.True);
                    Assert.That(Run(device, pass, output)[0], Is.EqualTo(bump));
                }

                Thread.Sleep(500);
                Assert.That(Changed(header), Is.False, "a save already applied is still listed as changed");
            });
        }
    }

    // Two saves within one tick of the file clock share a write time, and the second was taken for the first and lost.
    [Test]
    public void ASaveWithTheWriteTimeOfTheOneBefore_IsStillCompiled()
    {
        var (device, effect, pass, output, header, source) = Bumping();
        using (effect)
        using (output)
        {
            Manually(() =>
            {
                File.WriteAllText(header, "uint Bump() { return 5; }\n");
                Reload.ApplyAsync([header]).GetAwaiter().GetResult();
                var applied = File.GetLastWriteTimeUtc(header);

                File.WriteAllText(header, "uint Bump() { return 6; }\n");
                File.SetLastWriteTimeUtc(header, applied);
                Reload.NotifyChanged(header);

                Assert.That(Changed(header), Is.True, "the second save is gathered");
                var reports = Reload.ApplyAsync().GetAwaiter().GetResult();
                Assert.That(ReportOn(reports, source).Compiled, Is.True);
                Assert.That(Run(device, pass, output)[0], Is.EqualTo(6));
            });
        }
    }

    [Test]
    public void AnApplyThatDoesNotCompile_ReportsTheErrors_AndTheRunningVersionStays()
    {
        var (device, effect, pass, output, _, source) = Bumping();
        using (effect)
        using (output)
        {
            Manually(() =>
            {
                File.WriteAllText(source, Bumped.Replace("tid.x + Bump()", "tid.x + Bump("));

                var report = ReportOn(Reload.ApplyAsync([source]).GetAwaiter().GetResult(), source);

                Assert.That(report.Compiled, Is.False);
                Assert.That(report.Messages, Is.Not.Empty);
                Assert.That(Run(device, pass, output)[0], Is.EqualTo(1));
            });
        }
    }

    [Test]
    public void SwitchingBackToOnSave_CompilesWhatManualGathered()
    {
        var (device, effect, pass, output, header, _) = Bumping();
        using (effect)
        using (output)
        {
            Manually(() =>
            {
                File.WriteAllText(header, "uint Bump() { return 5; }\n");
                WaitUntil(() => Changed(header), "the watcher did not report the save");
            });

            Assert.That(RunUntil(device, pass, output, written => written[0] == 5)[0], Is.EqualTo(5));
        }
    }

    [Test]
    public void AnEffect_KnowsItsSource_OnlyInADebugBuild()
    {
        var effect = new SpriteEffect(GpuFixture.CreateRenderDevice());

#if DEBUG
        Assert.That(effect.IsSupportingDynamicCompilation, Is.True);
        Assert.That(Path.GetFileName(effect.RawEffectData.Arguments.FilePath), Is.EqualTo("SpriteEffect.fx"));
        Assert.That(File.Exists(effect.RawEffectData.Arguments.FilePath), Is.True);
#else
        Assert.That(effect.IsSupportingDynamicCompilation, Is.False);
#endif
    }

    [Test]
    public void AReloadedPass_RunsTheNewShader_WithTheValuesSetBefore()
    {
        var device = GpuFixture.CreateRenderDevice();
        using var effect = Effect.CompileFromFile(SmokePath, device);
        var pass = effect.Techniques[0].Passes[0];
        var address = effect.Parameters["OutputAddress"];
        using var output = NewOutput(device);
        address.SetValue(output.GetDeviceAddress());
        effect.Parameters["Count"].SetValue((uint)Count);

        var refused = effect.Reload(Recompiled(source => source.Replace("tid.x + 1;", "tid.x + 2;")));

        Assert.That(refused, Is.Null);
        Assert.That(effect.Techniques[0].Passes[0], Is.SameAs(pass));
        Assert.That(effect.Parameters["OutputAddress"], Is.SameAs(address));
        var written = Run(device, pass, output);
        for (int i = 0; i < Count; i++)
        {
            Assert.That(written[i], Is.EqualTo(i + 2), $"output[{i}]");
        }
    }

    [Test]
    public void AVersionWithoutAPass_IsRefused_AndTheOldOneKeepsRunning()
    {
        var device = GpuFixture.CreateRenderDevice();
        using var effect = Effect.CompileFromFile(SmokePath, device);
        var pass = effect.Techniques[0].Passes[0];
        using var output = NewOutput(device);
        effect.Parameters["OutputAddress"].SetValue(output.GetDeviceAddress());
        effect.Parameters["Count"].SetValue((uint)Count);

        var refused = effect.Reload(Recompiled(source => source.Replace("pass Run", "pass Go")));

        Assert.That(refused, Does.Contain("ComputeSmoke.Run"));
        var written = Run(device, pass, output);
        Assert.That(written[Count - 1], Is.EqualTo(Count));
    }

    [Test]
    public void AValueWhoseDeclarationChanged_StartsFromItsDefault()
    {
        var device = GpuFixture.CreateRenderDevice();
        using var effect = Effect.CompileFromFile(SmokePath, device);
        var count = effect.Parameters["Count"];
        Assert.That(count.ParameterType, Is.EqualTo(EffectParameterType.UInt));
        count.SetValue((uint)Count);

        var refused = effect.Reload(Recompiled(source => source
            .Replace("uint Count;", "float Count;")
            .Replace("tid.x >= Count", "tid.x >= (uint)Count")));

        Assert.That(refused, Is.Null);
        Assert.That(effect.Parameters["Count"], Is.SameAs(count));
        Assert.That(count.ParameterType, Is.EqualTo(EffectParameterType.Float));
        Assert.That(count.GetValue<float>(), Is.EqualTo(0f));
    }

    [Test]
    public void ABoundSampler_IsStillBoundAfterAReload()
    {
        const string sampling = @"
uint64_t OutputAddress;
Texture2D Picture;
SamplerState PictureSampler;

[shader(""compute"")]
[numthreads(1, 1, 1)]
void SampleCS(uint3 tid : SV_DispatchThreadID)
{
    uint* output = (uint*)OutputAddress;
    output[0] = (uint)(Picture.SampleLevel(PictureSampler, float2(0.5, 0.5), 0).x * VALUE);
}

technique Sampling { pass Run { ComputeShader = SampleCS; } }
";
        var device = GpuFixture.CreateRenderDevice();
        using var effect = new Effect(device, Compiled(sampling.Replace("VALUE", "255")));
        var samplerParameter = effect.Parameters["PictureSampler"];
        var sampler = SamplerStates.LinearClampToEdge;
        samplerParameter.SetResource(sampler);

        var refused = effect.Reload(Compiled(sampling.Replace("VALUE", "100")));

        Assert.That(refused, Is.Null);
        Assert.That(effect.Parameters["PictureSampler"], Is.SameAs(samplerParameter));
        Assert.That(effect.ResourceLinker.GetBoundValues(samplerParameter.ParameterDescription)[0], Is.SameAs(sampler));
    }

    private (IGraphicsDevice Device, Effect Effect, IEffectPass Pass, Adamantium.Graphics.Buffer Output, string Header, string Source) Bumping()
    {
        var folder = NewFolder();
        var device = GpuFixture.CreateRenderDevice();
        var (effect, pass, output, source) = Following(device, folder, "Bumped.fx", Bumped);
        return (device, effect, pass, output, Path.Combine(folder, "Bump.fxh"), source);
    }

    private static ShaderHotReload Reload => GpuFixture.Main.ShaderHotReload;

    private static void Manually(Action test)
    {
        Reload.Mode = ShaderReloadMode.Manual;
        try
        {
            test();
        }
        finally
        {
            Reload.Mode = ShaderReloadMode.OnSave;
        }
    }

    private static bool Changed(string file)
    {
        return Reload.ChangedFiles.Contains(Path.GetFullPath(file), StringComparer.OrdinalIgnoreCase);
    }

    private static ShaderCompileReport ReportOn(IReadOnlyList<ShaderCompileReport> reports, string source)
    {
        return reports.Single(report => string.Equals(report.Source, Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase));
    }

    private static void WaitUntil(Func<bool> done, string message)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!done() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(50);
        }

        Assert.That(done(), Is.True, message);
    }

    private string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "AdamantiumHotReload", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        folders.Add(folder);
        File.WriteAllText(Path.Combine(folder, "Bump.fxh"), "uint Bump() { return 1; }\n");
        return folder;
    }

    private static (Effect Effect, IEffectPass Pass, Adamantium.Graphics.Buffer Output, string Source) Following(
        IGraphicsDevice device, string folder, string name, string text)
    {
        var source = Path.Combine(folder, name);
        File.WriteAllText(source, text);
        var compiled = EffectCompiler.CompileFromFile(source, allowDynamicCompiling: true);
        Assert.That(compiled.HasErrors, Is.False, string.Join(Environment.NewLine, compiled.Logger.Messages));
        var effect = new Effect(device, compiled.EffectData);
        var output = NewOutput(device);
        effect.Parameters["OutputAddress"].SetValue(output.GetDeviceAddress());
        effect.Parameters["Count"].SetValue((uint)Count);
        var pass = effect.Techniques[0].Passes[0];
        Assert.That(Run(device, pass, output)[0], Is.EqualTo(1));
        return (effect, pass, output, source);
    }

    private static string Slow(int passes)
    {
        var text = new StringBuilder(Bumped[..Bumped.IndexOf("technique", StringComparison.Ordinal)]);
        for (var i = 0; i < passes; i++)
        {
            text.Append($"[shader(\"compute\")] [numthreads(64, 1, 1)] void Extra{i}CS(uint3 tid : SV_DispatchThreadID) ");
            text.Append($"{{ uint* output = (uint*)OutputAddress; output[tid.x] = Bump() + {i}; }}\n");
        }

        text.Append("technique Bumped { pass Run { ComputeShader = BumpCS; }");
        for (var i = 0; i < passes; i++)
        {
            text.Append($" pass Extra{i} {{ ComputeShader = Extra{i}CS; }}");
        }

        return text.Append(" }\n").ToString();
    }

    private static int[] RunUntil(IGraphicsDevice device, IEffectPass pass, Adamantium.Graphics.Buffer output, Func<int[], bool> done)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        var written = Run(device, pass, output);
        while (!done(written) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(100);
            written = Run(device, pass, output);
        }

        return written;
    }

    private static EffectData Compiled(string source)
    {
        var result = EffectCompiler.Compile(source, "Sampling.fx");
        Assert.That(result.HasErrors, Is.False, string.Join(Environment.NewLine, result.Logger.Messages));
        return result.EffectData;
    }

    private static EffectData Recompiled(Func<string, string> edit)
    {
        var result = EffectCompiler.Compile(edit(File.ReadAllText(SmokePath)), SmokePath);
        Assert.That(result.HasErrors, Is.False, string.Join(Environment.NewLine, result.Logger.Messages));
        return result.EffectData;
    }

    private static Adamantium.Graphics.Buffer NewOutput(IGraphicsDevice device)
    {
        return Adamantium.Graphics.Buffer.New((GraphicsDevice)device, Count * sizeof(uint),
            BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
            MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal);
    }

    private static int[] Run(IGraphicsDevice device, IEffectPass pass, Adamantium.Graphics.Buffer output)
    {
        var graphicsDevice = (GraphicsDevice)device;
        var parameters = new PresentationParameters(PresenterType.RenderTarget, 16, 16, IntPtr.Zero);
        using var presenter = GraphicsPresenter.Create(device, parameters, "hot_reload");
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.MSAALevel = presenter.MSAALevel;
        device.Presenter = presenter;

        Assert.That(device.BeginDraw(beforeRenderPass: _ =>
        {
            pass.Apply();
            graphicsDevice.Dispatch((Count + 63) / 64);
            graphicsDevice.BufferBarrier(output,
                PipelineStageFlagBits2.ComputeShaderBit, AccessFlagBits2.ShaderWriteBit,
                PipelineStageFlagBits2.HostBit, AccessFlagBits2.HostReadBit);
        }), Is.True);
        device.EndDraw();
        device.Submit();
        presenter.Present();
        device.FrameEnded();
        device.DeviceWaitIdle();

        var written = new int[Count];
        var memory = output.MapMemory();
        Marshal.Copy((IntPtr)(nint)memory, written, 0, Count);
        output.UnmapMemory();
        return written;
    }
}
