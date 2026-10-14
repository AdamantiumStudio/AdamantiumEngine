using System;
using System.Threading;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Multiverse;

namespace Adamantium.EngineTests;

/// <summary>A host with no surfaces and no windows: a platform needs one to exist, and a test adds its outputs itself.</summary>
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
