using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core;

namespace Adamantium.EngineTests;

/// <summary>A graphics service with no device at all: an output added to a platform gets none and draws nothing.</summary>
public class NoDeviceGraphicsService : IGraphicsDeviceService
{
    public bool IsInDebugMode { get; set; }

    public IGraphicsDeviceFactory GraphicsDeviceFactory => null;

    public bool IsReady => false;

    public bool DeviceUpdateNeeded { get; set; }

    public MainGraphicsDevice MainGraphicsDevice => null;

    public IGraphicsDevice ResourceLoaderDevice => null;

    public IReadOnlyList<IGraphicsDevice> GraphicsDevices => [];

    public void CreateMainDevice(string name)
    {
    }

    public IGraphicsDevice CreateRenderDevice()
    {
        return null;
    }

    public void ChangeOrCreateMainDevice(string name, bool forceUpdate)
    {
    }

    public void RaiseFrameFinished()
    {
    }

    public event EventHandler<EventArgs> DeviceCreated { add { } remove { } }

    public event EventHandler<EventArgs> DeviceChangeBegin { add { } remove { } }

    public event EventHandler<EventArgs> DeviceChangeEnd { add { } remove { } }

    public event EventHandler<EventArgs> DeviceLost { add { } remove { } }

    public event EventHandler<EventArgs> DeviceDisposing { add { } remove { } }
}
