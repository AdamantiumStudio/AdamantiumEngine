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
/// drawn alone into an output of its own with no surface, through a camera that frames the whole of it, and read back.
/// Inactive while its queue is empty, so the frame passes it by; a universe that runs no frames - paused - takes no
/// screenshots until it runs again. A change of device cancels the screenshots waiting: what their entities were loaded
/// with went with the old one. Made by <see cref="Create"/>.
/// </summary>
public sealed class ScreenshotRenderingService : RenderingService, IScreenshotRenderingService
{
    /// <summary>The side of a screenshot made by <see cref="Create"/> unless told otherwise, in pixels.</summary>
    public const uint DefaultSize = 256;

    private static readonly Vector3<double> ViewDirection = Vector3.Normalize(new Vector3<double>(1, -0.8, -1.2));

    private const double Fill = 0.92;

    private readonly OffscreenOutput _output;
    private readonly ScreenshotRenderingProcessor _processor = new();
    private readonly object _lock = new();
    private readonly Queue<Shot> _queue = new();
    private readonly IUniverse _universe;
    private Shot _current;
    private bool _drawn;
    private volatile bool _deviceLost;
    private volatile bool _deviceChanged;

    public ScreenshotRenderingService(EntityWorld world, OffscreenOutput output) : base(world, output)
    {
        _output = output;
        _universe = world.Satellites.Get<IUniverse>();
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

        _universe.Invalidate(_output);
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
            _universe.Invalidate(_output);
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
                if (Aim(next))
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
        ActiveCamera = _processor.Camera;
        DrawProcessors(appTime);
    }

    public override void Present()
    {
    }

    public override void FrameEnded()
    {
        base.FrameEnded();
        _processor.ReleaseCollectedGeometry();
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
            _universe.Invalidate(_output);
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
        _processor.Target = null;
        _processor.Camera = null;
        ActiveCamera = null;
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

        _processor.Target = null;
        _processor.Camera = null;
    }

    private bool Aim(Shot shot)
    {
        var entity = shot.Entity;
        if (entity.GetComponent<Collider>() is not { } bounds || entity.GetDiameter() <= 0)
        {
            return false;
        }

        var corners = new Vector3<double>[8];
        bounds.Bounds.GetCorners(corners);
        var world = entity.Transform.GetWorldMatrixF();
        for (var i = 0; i < corners.Length; i++)
        {
            corners[i] = Vector3F.TransformCoordinate(corners[i].As<float>(), world).As<double>();
        }

        var center = entity.GetCenterAbsolute();
        var forward = -ViewDirection;
        var right = Vector3.Normalize(Vector3<double>.Cross(-Vector3<double>.Up, forward));
        var up = Vector3<double>.Cross(forward, right);
        var cameraEntity = new CameraTemplate().BuildEntity(null, "Screenshot camera", Vector3<double>.Zero, forward,
            -Vector3<double>.Up, _output.Width, _output.Height, 0.1f, 1000f);
        var camera = cameraEntity.GetComponent<Camera>();
        var reach = Math.Tan(double.DegreesToRadians(camera.Fov) / 2) * Fill;
        double distance = 0;
        double radius = 0;
        foreach (var corner in corners)
        {
            var offset = corner - center;
            var depth = Vector3<double>.Dot(offset, forward);
            var across = Math.Max(Math.Abs(Vector3<double>.Dot(offset, right)), Math.Abs(Vector3<double>.Dot(offset, up)));
            distance = Math.Max(distance, across / reach - depth);
            radius = Math.Max(radius, offset.Length());
        }

        cameraEntity.Transform.Position = center + ViewDirection * distance;
        camera.ZNear = (float)Math.Max(distance - radius * 1.5, distance / 1000);
        camera.ZFar = (float)(distance + radius * 1.5);
        camera.SetFreeLookAt(center);
        camera.Update(default);
        PlaceFor(entity, camera);
        _processor.Camera = camera;
        _processor.Target = entity;
        return true;
    }

    private static void PlaceFor(Entity root, Camera camera)
    {
        var cameraWorld = camera.WorldPosition;
        var rootParent = root.Owner?.Transform?.GetWorldMatrixF() ?? Matrix4x4F.Identity;
        root.TraverseInDepth(current =>
        {
            var parentWorld = current == root || current.Owner?.Transform == null
                ? rootParent
                : current.Owner.Transform.GetMetadata(camera).AbsoluteWorld;
            current.Transform.CalculateFinalTransform(camera, parentWorld, cameraWorld);
            foreach (var collider in current.GetComponents<Collider>())
            {
                collider.UpdateForCamera(camera);
            }
        });
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
