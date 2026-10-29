using System;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Input;
using Adamantium.Multiverse.Payloads;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Assets;
using Keys = Adamantium.Multiverse.Input.Keys;

namespace Adamantium.Engine.EntityServices;

public class RenderingService : EntityService
{
    private long drawStarted;

    public override bool IsUpdateService => false;
    public override bool IsRenderingService => true;
    public override EntityServiceType ServiceType => EntityServiceType.Render;

    // Present is a phase of its own that a skipped BeginDraw does not skip: a hidden output must not present its last frame.
    public override bool CanDisplayContent => Window.IsVisible;
    protected IAssetService Assets { get; }
    public UniverseOutput Window { get; }

    protected InputWormhole InputManager => Window.Input;

    protected Camera ActiveCamera { get; set; }
    protected bool ShowDebugOutput { get; set; }

    public RenderingService(EntityWorld world, UniverseOutput window) : base(world)
    {
        GraphicsDeviceService = world.Satellites.Get<IGraphicsDeviceService>();
        GraphicsDeviceService.DeviceChangeBegin += DeviceChangeBegin;
        GraphicsDeviceService.DeviceChangeEnd += DeviceChangeEnd;
        GraphicsDevice = window.GraphicsDevice;
        Assets = world.Satellites.Get<IAssetService>();
        Window = window;
        Window.ParametersChanging += OnWindowParametersChanging;
        Window.ParametersChanged += OnWindowParametersChanged;
    }

    /// <summary>Unloads the processors and lets go of the shared graphics device service. Called once the service is
    /// removed, or when its universe shuts down.</summary>
    public override void UnloadContent()
    {
        base.UnloadContent();
        GraphicsDeviceService.DeviceChangeBegin -= DeviceChangeBegin;
        GraphicsDeviceService.DeviceChangeEnd -= DeviceChangeEnd;
    }

    private void OnWindowParametersChanged(UniverseOutputParametersPayload payload)
    {
        // A full update gives the output a new device: the service and its processors move to it.
        if (payload.Reason == ChangeReason.FullUpdate)
        {
            GraphicsDevice = Window.GraphicsDevice;
            for (int i = 0; i < Processors.Count; i++)
            {
                if (Processors[i] is RenderingProcessor processor)
                {
                    processor.OnOutputDeviceChanged();
                }
            }
        }

        OnWindowParametersChanged(payload.Reason);
    }

    private void OnWindowParametersChanging(UniverseOutputParametersPayload payload)
    {
        OnWindowParametersChanging(payload.Reason);
    }

    protected virtual void OnWindowParametersChanging(ChangeReason reason)
    { }

    protected virtual void OnWindowParametersChanged(ChangeReason reason)
    { }

    private void DeviceChangeBegin(object sender, EventArgs e)
    {
        OnDeviceChangeBegin();
    }

    private void DeviceChangeEnd(object sender, EventArgs e)
    {
        OnDeviceChangeEnd();
    }

    protected virtual void OnDeviceChangeBegin()
    {
    }

    protected virtual void OnDeviceChangeEnd()
    {
    }

    public virtual void CreateSystemResources()
    { }

    public override bool BeginDraw()
    {
        // False skips Draw, EndDraw and Submit - and a present of a frame nobody sees.
        if (!Window.IsDrawnThisFrame || !Window.IsVisible || !Window.IsUpToDate())
        {
            return false;
        }

        // Before GraphicsDevice.BeginDraw, which transitions the targets: a null one fails the very first frame.
        GraphicsDevice.SetRenderTargets(Window.Presenter.RenderTarget);
        GraphicsDevice.SetDepthBuffer(Window.Presenter.DepthBuffer);
        // Must match the presenter's MSAA, or the model is not rasterized.
        GraphicsDevice.MSAALevel = Window.Presenter.MSAALevel;
        // Alpha is not written, so the cleared 1 stays and the panel can fade the frame with its own Opacity.
        GraphicsDevice.ColorComponentFlags = Adamantium.Vulkan.Core.ColorComponentFlagBits.RBit |
                                             Adamantium.Vulkan.Core.ColorComponentFlagBits.GBit |
                                             Adamantium.Vulkan.Core.ColorComponentFlagBits.BBit;
        // An opaque scene must not inherit the blend the UI pass left set.
        GraphicsDevice.ColorBlendEquation = Adamantium.Graphics.Core.ColorBlendEquations.Opaque;

        if (!GraphicsDevice.BeginDraw(beforeRenderPass: _ =>
            {
                drawStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                PreRenderProcessors();
            }))
        {
            return false;
        }

        // Recorded into the command buffer, so only after BeginDraw.
        GraphicsDevice.SetViewports(Window.Viewport);
        GraphicsDevice.SetScissors(Window.Scissor);
        return true;
    }

    public override void Draw(AppTime appTime)
    {
        AppTime = appTime;

        if (InputManager.IsKeyPressed(Keys.P))
        {
            ShowDebugOutput = !ShowDebugOutput;
        }

        ActiveCamera = Window.Camera;

        DrawProcessors(appTime);
    }

    protected virtual void Debug() { }

    public override void EndDraw()
    {
        GraphicsDevice.EndDraw();
        EndDrawProcessors();
    }

    public override void Submit()
    {
        GraphicsDevice.Submit();
        Window.CountDraw(System.Diagnostics.Stopwatch.GetElapsedTime(drawStarted).TotalMilliseconds);
    }

    public override void Present()
    {
        Window.DisplayContent();
    }

    public override void FrameEnded()
    {
        GraphicsDevice.FrameEnded();
    }
}
