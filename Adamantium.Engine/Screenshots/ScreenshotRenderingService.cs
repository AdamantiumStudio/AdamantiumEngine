using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.EntityServices;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Image = Adamantium.Imaging.Image;

namespace Adamantium.Engine.Screenshots;

/// <summary>
/// Takes screenshots of entities, one a frame, in the universe whose world it is a service of: an entity put in its
/// queue - one that belongs to no world, so nothing else draws it - is handed to its <see cref="ScreenshotRenderingProcessor"/>,
/// which takes the screenshot into an output of the service's own with no surface; the service reads the frame back
/// once it was submitted.
/// Inactive while its queue is empty, so the frame passes it by; a universe that runs no frames - paused - takes no
/// screenshots until it runs again. A change of device cancels the screenshots waiting: what their entities were loaded
/// with went with the old one. Made by <see cref="Create"/>.
/// </summary>
public sealed class ScreenshotRenderingService : RenderingService, IScreenshotRenderingService
{
    /// <summary>The side of a screenshot made by <see cref="Create"/> unless told otherwise, in pixels.</summary>
    public const uint DefaultSize = 256;

    private readonly OffscreenOutput _output;
    private readonly ScreenshotRenderingProcessor _processor = new();
    private readonly object _lock = new();
    private readonly Queue<Shot> _queue = new();
    private readonly IUniverseWatcher _frames;
    private Shot _current;
    private bool _drawn;
    private volatile bool _deviceLost;
    private volatile bool _deviceChanged;

    public ScreenshotRenderingService(EntityWorld world, OffscreenOutput output) : base(world, output)
    {
        _output = output;
        _frames = world.Satellites.Get<IUniverseWatcher>();
        WritesAlpha = true;
        IsActive = false;
        AttachProcessor(_processor);
    }

    public ScreenshotLighting Lighting
    {
        get => _processor.Lighting;
        set => _processor.Lighting = value;
    }

    /// <summary>Makes the service in <paramref name="world"/>, with an output of its own, square, of
    /// <paramref name="size"/> pixels a side, on the world's graphics devices; it joins the world at the end of a
    /// frame.</summary>
    public static ScreenshotRenderingService Create(EntityWorld world, uint size = DefaultSize)
    {
        ArgumentNullException.ThrowIfNull(world);
        var output = new OffscreenOutput(size, size);
        output.SetGraphicsDevice(world.Satellites.Get<IGraphicsDeviceService>().CreateRenderDevice());
        return world.CreateService<ScreenshotRenderingService>(world, output);
    }

    public Task<Image> Enqueue(Entity entity, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var shot = new Shot(entity);
        if (cancellation.CanBeCanceled)
        {
            shot.Registration = cancellation.Register(() => shot.Picture.TrySetCanceled(cancellation));
        }

        lock (_lock)
        {
            _queue.Enqueue(shot);
            IsActive = true;
        }

        _frames.Invalidate(_output);
        return shot.Picture.Task;
    }

    public void Remove()
    {
        IsActive = false;
        EntityWorld.RemoveService(this);
    }

    public override void Initialize()
    {
        base.Initialize();
        bool waiting;
        lock (_lock)
        {
            waiting = _queue.Count > 0;
        }

        if (waiting)
        {
            _frames.Invalidate(_output);
        }
    }

    public override bool BeginDraw()
    {
        _drawn = false;
        if (_deviceLost)
        {
            return false;
        }

        if (_deviceChanged)
        {
            _deviceChanged = false;
            _output.SetGraphicsDevice(GraphicsDeviceService.CreateRenderDevice());
            GraphicsDevice = _output.GraphicsDevice;
            _processor.OnOutputDeviceChanged();
        }

        while (_current == null && TryTake(out var next))
        {
            if (next.Picture.Task.IsCompleted)
            {
                next.Forget();
                continue;
            }

            try
            {
                if (_processor.Take(next.Entity))
                {
                    _current = next;
                }
                else
                {
                    next.Picture.TrySetResult(null);
                    next.Forget();
                }
            }
            catch (Exception exception)
            {
                next.Picture.TrySetException(exception);
                next.Forget();
            }
        }

        if (_current == null)
        {
            return false;
        }

        _drawn = base.BeginDraw();
        return _drawn;
    }

    public override void Draw(AppTime appTime)
    {
        AppTime = appTime;
        DrawProcessors(appTime);
    }

    public override void Present()
    {
    }

    public override void FrameEnded()
    {
        base.FrameEnded();
        if (_drawn && _current != null)
        {
            var shot = _current;
            _current = null;
            Finish(shot);
        }

        _drawn = false;
        bool more;
        lock (_lock)
        {
            more = _current != null || _queue.Count > 0;
            IsActive = more;
        }

        if (more)
        {
            _frames.Invalidate(_output);
        }
    }

    public override void UnloadContent()
    {
        CancelAll();
        base.UnloadContent();
        GraphicsDevice?.WaitForFramesInFlight(ulong.MaxValue);
        _output.Dispose();
    }

    protected override void OnDeviceChangeBegin()
    {
        _deviceLost = true;
        CancelAll();
    }

    protected override void OnDeviceChangeEnd()
    {
        _deviceChanged = true;
        _deviceLost = false;
    }

    private bool TryTake(out Shot shot)
    {
        lock (_lock)
        {
            return _queue.TryDequeue(out shot);
        }
    }

    private void Finish(Shot shot)
    {
        var drew = _processor.DrewFrame;
        _processor.Release();
        try
        {
            if (!drew)
            {
                shot.Picture.TrySetException(new InvalidOperationException(
                    "the screenshot's frame drew nothing: its pass was not ready or drawing failed"));
                return;
            }

            _output.DisplayContent();
            shot.Picture.TrySetResult(_output.ReadFrame());
        }
        catch (Exception exception)
        {
            shot.Picture.TrySetException(exception);
        }
        finally
        {
            shot.Forget();
        }
    }

    private void CancelAll()
    {
        lock (_lock)
        {
            if (_current != null)
            {
                _current.Picture.TrySetCanceled();
                _current.Forget();
                _current = null;
            }

            while (_queue.TryDequeue(out var left))
            {
                left.Picture.TrySetCanceled();
                left.Forget();
            }

            IsActive = false;
        }

        _processor.Release();
    }

    private sealed class Shot
    {
        public Shot(Entity entity) => Entity = entity;

        public Entity Entity { get; private set; }

        public TaskCompletionSource<Image> Picture { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationTokenRegistration Registration { get; set; }

        public void Forget()
        {
            Registration.Dispose();
            Entity = null;
        }
    }
}
