using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.Graphics.Core;
using Adamantium.MVVM;

namespace Adamantium.Multiverse;

[ViewModel]
public sealed partial class GraphicsDeviceService : IGraphicsDeviceService
{
    public IGraphicsDeviceFactory GraphicsDeviceFactory { get; }

    [Bindable] private bool isInDebugMode;

    private volatile bool _deviceUpdateNeeded;

    public MainGraphicsDevice MainGraphicsDevice { get; private set; }

    // Owned and created by the main device itself; just expose it.
    public IGraphicsDevice ResourceLoaderDevice => MainGraphicsDevice?.ResourceLoaderDevice;

    public IReadOnlyList<IGraphicsDevice> GraphicsDevices => MainGraphicsDevice.GraphicsDevices;
        
    public bool DeviceUpdateNeeded
    {
        get => _deviceUpdateNeeded;
        set => _deviceUpdateNeeded = value;
    }

    public GraphicsDeviceService(IGraphicsDeviceFactory deviceFactory, bool isInDebug)
    {
        IsInDebugMode = isInDebug;
        GraphicsDeviceFactory = deviceFactory;
    }

    void IGraphicsDeviceService.CreateMainDevice(string name)
    {
        CreateMainDevice(name, IsInDebugMode);
        DeviceUpdateNeeded = MainGraphicsDevice.IsLost;
        DeviceCreated?.Invoke(this, EventArgs.Empty);
    }

    public IGraphicsDevice CreateRenderDevice()
    {
        return MainGraphicsDevice.CreateRenderDevice();
    }

    public void RaiseFrameFinished()
    {
        MainGraphicsDevice?.OnFrameFinished();
    }

    public bool IsReady => MainGraphicsDevice != null;

    public void ChangeOrCreateMainDevice(string name, bool forceUpdate)
    {
        if (DeviceUpdateNeeded || forceUpdate)
        {
            MainGraphicsDevice.DeviceWaitIdle();
            OnDeviceChangeBegin();
            // The old resource-loader device is disposed when the old main device is disposed inside CreateMainDevice.
            CreateMainDevice(name, IsInDebugMode);

            DeviceUpdateNeeded = MainGraphicsDevice.IsLost;
            OnDeviceChangeEnd();
        }
    }

    private void OnDeviceCreated()
    {
        RaiseEvent(DeviceCreated, this, EventArgs.Empty);
    }

    private void OnDeviceChangeBegin()
    {
        RaiseEvent(DeviceChangeBegin, this, EventArgs.Empty);
    }

    private void OnDeviceChangeEnd()
    {
        RaiseEvent(DeviceChangeEnd, this, EventArgs.Empty);
    }

    private void OnDeviceDisposing()
    {
        RaiseEvent(DeviceDisposing, this, EventArgs.Empty);
    }

    private void OnDeviceLost()
    {
        RaiseEvent(DeviceLost, this, EventArgs.Empty);
    }

    private void RaiseEvent<T>(EventHandler<T> handler, object sender, T args) where T : EventArgs
    {
        handler?.Invoke(sender, args);
    }

    partial void OnIsInDebugModeChanged(bool value) => DeviceUpdateNeeded = true;

    internal void CreateMainDevice(string name, bool debugEnabled = true)
    {
        if (MainGraphicsDevice != null)
        {
            MainGraphicsDevice.Lost -= MainDeviceLost;
            MainGraphicsDevice.Dispose();
        }

        // TEMP knob for the in-flight depth measurement (ADAM_FRAMES_IN_FLIGHT). This is what MaxFramesInFlight is taken
        // from, i.e. how far the CPU may run ahead of the GPU before it blocks on a slot's fence.
        var inFlight = Environment.GetEnvironmentVariable("ADAM_FRAMES_IN_FLIGHT") is { } f && uint.TryParse(f, out var n) ? n : 3;
        MainGraphicsDevice = MainGraphicsDevice.Create(GraphicsDeviceFactory, inFlight, name, debugEnabled);
        // The main device now creates and owns its resource-loader device + shared descriptor heap; we only read them.
        MainGraphicsDevice.Disposing += GraphicsDeviceDisposing;
        MainGraphicsDevice.Lost += MainDeviceLost;

        OnDeviceCreated();
    }

    private void MainDeviceLost()
    {
        DeviceUpdateNeeded = true;
        OnDeviceLost();
    }

    private void GraphicsDeviceDisposing(object sender, EventArgs e)
    {
        OnDeviceDisposing();
    }

    #region Events

    public event EventHandler<EventArgs> DeviceCreated;

    public event EventHandler<EventArgs> DeviceChangeBegin;

    public event EventHandler<EventArgs> DeviceChangeEnd;

    public event EventHandler<EventArgs> DeviceLost;

    public event EventHandler<EventArgs> DeviceDisposing;

    #endregion
}