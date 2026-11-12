using System;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.EntityServices;
using Adamantium.Engine.Managers;
using Adamantium.Engine.Templates;
using Adamantium.Engine.Templates.Lights;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Graphics.Core.Models;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Events;

namespace Adamantium.Engine.Snapshots;

/// <summary>The universe <see cref="ModelSnapshotService"/> draws in: lights of its own, no tools, one output without a
/// surface, and one model at a time, framed by a camera made for it. Driven frame by frame by the service.</summary>
internal sealed class SnapshotUniverse : Universe
{
    private static readonly Vector3<double> ViewDirection = Vector3.Normalize(new Vector3<double>(1, -0.8, -1.2));

    private const double Fill = 0.92;

    private readonly OffscreenOutput _output;
    private LightManager _lights;

    public SnapshotUniverse(IDependencyContainer container, IGraphicsDeviceService devices, uint size)
        : base(UniverseMode.Slave, false, container, devices)
    {
        Title = "Model snapshots";
        EventAggregator.GetEvent<UniverseOutputCreatedEvent>().Subscribe(OnOutputCreated);
        InitializeUniverse();
        _output = new OffscreenOutput(size, size);
        AddOutput(_output);
        RunFrame();
    }

    /// <summary>The model drawn into the output and read back; null when it has nothing to draw. The model and what it
    /// loaded are gone again when this returns.</summary>
    public Image Take(string assetPath)
    {
        Entity model = null;
        Entity cameraEntity = null;
        try
        {
            var options = new AssetLoadOptions { AllowDuplication = false, IgnoreRootDirectory = true };
            var scene = Assets.Load<SceneData>(assetPath, options);
            model = EntityWorld.CreateEntityFromTemplate(new EntityImportTemplate(scene, Assets, Vector3<double>.Zero))
                .GetAwaiter().GetResult();
            if (model == null)
            {
                return null;
            }

            EntityWorld.EntityManager.AddEntity(model);
            RunFrame();

            if (model.GetDiameter() <= 0)
            {
                return null;
            }

            cameraEntity = Frame(model);
            RunFrame();
            return _output.ReadFrame();
        }
        finally
        {
            if (model != null)
            {
                EntityWorld.EntityManager.RemoveEntity(model);
            }

            if (cameraEntity != null)
            {
                _output.Camera = _output.OwnCamera;
                _output.RemoveCamera(cameraEntity.GetComponent<Camera>());
                EntityWorld.EntityManager.RemoveEntity(cameraEntity);
            }

            RunFrame();
            GraphicsDeviceService.MainGraphicsDevice.DeviceWaitIdle();
            Assets.Unload();
        }
    }

    protected override void Initialize()
    {
        base.Initialize();
        EntityWorld.CreateService<TransformService>(EntityWorld);
        _lights = new LightManager(this, EntityWorld);
        Satellites.Add(_lights);

        var key = new LightTemplate().BuildEntity(null, "Key light", LightType.Directional);
        key.Transform.Position = new Vector3<double>(4, -8, -6);
        key.Transform.Rotation = Quaternion.CreateFromYawPitchRoll<float>(float.DegreesToRadians(-30),
            float.DegreesToRadians(140), 0);
        _lights.AddLight(key);

        var fill = new LightTemplate().BuildEntity(null, "Fill light", LightType.Directional);
        fill.Transform.Position = new Vector3<double>(-6, -2, -4);
        fill.Transform.Rotation = Quaternion.CreateFromYawPitchRoll<float>(float.DegreesToRadians(50),
            float.DegreesToRadians(170), 0);
        fill.GetComponent<Light>().Intensity = 0.4f;
        _lights.AddLight(fill);
    }

    private void RunFrame()
    {
        RunOnce(default);
        Submit();
    }

    private void OnOutputCreated(UniverseOutput output)
    {
        var rendering = CreateRenderService<RenderingService>(output);
        rendering.WritesAlpha = true;
        rendering.AttachProcessor(new ForwardPlusRenderingProcessor
        {
            ClearColor = Colors.Transparent,
            Ambient = new Vector3F(0.25f, 0.25f, 0.27f)
        });
    }

    private Entity Frame(Entity model)
    {
        var corners = WorldCorners(model);
        var center = model.GetCenterAbsolute();
        var forward = -ViewDirection;
        var right = Vector3.Normalize(Vector3<double>.Cross(-Vector3<double>.Up, forward));
        var up = Vector3<double>.Cross(forward, right);
        var reach = Math.Tan(double.DegreesToRadians(_output.OwnCamera.Fov) / 2) * Fill;
        double distance = 0;
        double radius = 0;
        foreach (var corner in corners)
        {
            var offset = corner - center;
            var depth = Vector3<double>.Dot(offset, forward);
            var across = Math.Max(Math.Abs(Vector3<double>.Dot(offset, right)),
                Math.Abs(Vector3<double>.Dot(offset, up)));
            distance = Math.Max(distance, across / reach - depth);
            radius = Math.Max(radius, offset.Length());
        }

        var position = center + ViewDirection * distance;
        var near = (float)Math.Max(distance - radius * 1.5, distance / 1000);
        var far = (float)(distance + radius * 1.5);
        var entity = new CameraTemplate().BuildEntity(null, "Snapshot camera", position, -ViewDirection,
            -Vector3<double>.Up, _output.Width, _output.Height, near, far);
        var camera = entity.GetComponent<Camera>();
        EntityWorld.EntityManager.AddEntity(entity);
        _output.Camera = camera;
        camera.SetFreeLookAt(center);
        camera.Update(default);
        return entity;
    }

    private static Vector3<double>[] WorldCorners(Entity model)
    {
        var corners = new Vector3<double>[8];
        model.GetComponent<Collider>().Bounds.GetCorners(corners);
        var world = model.Transform.GetWorldMatrixF();
        for (var i = 0; i < corners.Length; i++)
        {
            corners[i] = Vector3F.TransformCoordinate(corners[i].As<float>(), world).As<double>();
        }

        return corners;
    }
}
