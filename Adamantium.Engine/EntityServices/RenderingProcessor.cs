using System;
using System.Collections.Generic;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Input;
using Adamantium.Multiverse.Payloads;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Content;

namespace Adamantium.Engine.EntityServices;

public class RenderingProcessor : EntityProcessor<RenderingService>, IDisposable
{
    protected IGraphicsDeviceService GraphicsDeviceService;
    protected IGraphicsDevice GraphicsDevice { get; set; }
    protected EntityWorld EntityWorld { get; set; }

    protected IContentManager Content { get; set; }
    protected UniverseOutput Window { get; set; }

    protected InputWormhole InputManager => Window.Input;

    protected SpriteBatch SpriteBatch { get; set; }

    protected Camera ActiveCamera { get; set; }
    protected bool ShowDebugOutput { get; set; }

    public IReadOnlyList<Entity> Entities => EntityWorld.RootEntities;

    public RenderingProcessor()
    {
    }

    protected override void OnAttached()
    {
        Initialize();
    }

    protected override void OnDetached()
    {
        Dispose();
    }

    protected void Initialize()
    {
        EntityWorld = AssociatedService.EntityWorld;
        GraphicsDeviceService = AssociatedService.GraphicsDeviceService;
        GraphicsDevice = AssociatedService.GraphicsDevice;
        Content = EntityWorld.Satellites.Get<IContentManager>();
        Window = AssociatedService.Window;
        CreateDeviceResources();
    }

    /// <summary>Makes what this processor draws with on the current device, on attach and on every device change - not
    /// the lifecycle's LoadContent, which a device change never repeats.</summary>
    protected virtual void CreateDeviceResources()
    {

    }

    internal void OnOutputDeviceChanged()
    {
        GraphicsDevice = AssociatedService.GraphicsDevice;
        CreateDeviceResources();
    }

    protected virtual void OnDeviceChangeBegin()
    {
        SpriteBatch?.Dispose();
    }

    public override void EndDraw()
    {
        Window.CopyOutput(GraphicsDevice);
    }

    protected virtual void OnDeviceChangeEnd()
    {
        SpriteBatch = new SpriteBatch(GraphicsDevice, 25000);
    }

    public void Dispose()
    {
    }
}