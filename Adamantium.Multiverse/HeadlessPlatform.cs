using System;
using System.Threading;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;

namespace Adamantium.Multiverse;

/// <summary>A platform with no surfaces and no windows, for a universe drawing only into outputs added to it directly,
/// such as an <see cref="OffscreenOutput"/>. Its <see cref="Run"/> waits until the universe stops.</summary>
public sealed class HeadlessPlatform : IOutputFactory, IWindowingPlatform
{
    public UniverseOutput Create(OutputContext context, IUniverseEventAggregator events) =>
        throw new NotSupportedException("A headless platform has no surfaces.");

    public UniverseOutput Create(OutputContext context, IUniverseEventAggregator events, SurfaceFormat pixelFormat,
        DepthFormat depthFormat, MSAALevel msaaLevel) =>
        throw new NotSupportedException("A headless platform has no surfaces.");

    public UniverseOutput CreateWindow(uint width, uint height, IUniverseEventAggregator events) =>
        throw new NotSupportedException("A headless platform opens no windows.");

    public void Run(CancellationToken token) => token.WaitHandle.WaitOne();
}
