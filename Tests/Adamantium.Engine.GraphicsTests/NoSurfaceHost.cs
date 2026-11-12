using System;
using System.Threading;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Multiverse;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>A host with no surfaces and no windows: a universe's platform needs one, and its outputs are added to it
/// directly.</summary>
public class NoSurfaceHost : IOutputFactory, IWindowingPlatform
{
    public UniverseOutput Create(OutputContext context, IUniverseEventAggregator events)
    {
        throw new NotSupportedException();
    }

    public UniverseOutput Create(OutputContext context, IUniverseEventAggregator events, SurfaceFormat pixelFormat,
        DepthFormat depthFormat, MSAALevel msaaLevel)
    {
        throw new NotSupportedException();
    }

    public UniverseOutput CreateWindow(uint width, uint height, IUniverseEventAggregator events)
    {
        throw new NotSupportedException();
    }

    public void Run(CancellationToken token)
    {
    }
}
