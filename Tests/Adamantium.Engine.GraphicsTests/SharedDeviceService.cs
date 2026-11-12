using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>The graphics service of a universe under test: the assembly's one main device, never made again.</summary>
public class SharedDeviceService : IGraphicsDeviceService
{
    public bool IsInDebugMode { get; set; }

    public IGraphicsDeviceFactory GraphicsDeviceFactory => null;

    public bool IsReady => GpuFixture.Main != null;

    public bool DeviceUpdateNeeded { get; set; }

    public MainGraphicsDevice MainGraphicsDevice => GpuFixture.Main;

    public IGraphicsDevice ResourceLoaderDevice => GpuFixture.Main.ResourceLoaderDevice;

    public IReadOnlyList<IGraphicsDevice> GraphicsDevices => GpuFixture.Main.GraphicsDevices;

    public void CreateMainDevice(string name)
    {
    }

    public IGraphicsDevice CreateRenderDevice()
    {
        return GpuFixture.Main.CreateRenderDevice();
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
