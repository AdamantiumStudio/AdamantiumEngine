using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core.Models;
using Adamantium.Multiverse;
using Adamantium.Mathematics;
using Serilog;

namespace Adamantium.Engine.EntityServices;

public class TransformService : EntityService
{
    private IUniverse universe;
    private readonly List<Viewpoint> viewpoints = [];
    private int viewpointCount;
    private readonly Vector3F[] corners = new Vector3F[8];
    private readonly List<(Entity Entity, Mesh Mesh, Skin Skin)> skins = [];
    private bool worldMoved;
    private SceneIndex index;

    public Boolean IsPaused { get; set; }

    public TransformService(EntityWorld world)
        : base(world)
    {
            
    }

    public override bool IsUpdateService => true;
    public override bool IsRenderingService => false;
    public override EntityServiceType ServiceType => EntityServiceType.Update;

    public override void Initialize()
    {
        universe = EntityWorld.Satellites.Get<IUniverse>();
        if (!EntityWorld.Satellites.TryGet(out index))
        {
            index = new SceneIndex();
            EntityWorld.Satellites.Add(index);
        }
    }

    public override void Update(AppTime appTime)
    {
        var entities = Entities;
        try
        {
            CollectViewpoints();
            worldMoved = false;
            skins.Clear();
            index.BeginFrame();
            foreach (var entity in entities)
            {
                Transform(entity, appTime);
            }

            IndexSkins();
            index.EndFrame();
            InvalidateWhatMoved();
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Transform update failed");
        }
        finally
        {
            ForgetViewpoints();
        }
    }
        
    private void Transform(Entity entity, AppTime appTime)
    {
        entity.TraverseInDepth(current =>
        {
            // First, so a shape made again this frame gets its collider placed in the same pass.
            current.GetComponent<ProceduralShape>()?.RebuildWhenChanged();

            var transform = current.Transform;
            var dirty = transform.IsWorldDirty;
            var ownViewpoint = dirty ? ViewpointOf(current) : null;
            if (ownViewpoint != null)
            {
                ownViewpoint.Moved = true;
            }

            if (dirty)
            {
                transform.ForgetComputedMetadata();
            }

            Collider[] colliders = null;

            for (int v = 0; v < viewpointCount; v++)
            {
                var viewpoint = viewpoints[v];
                var camera = viewpoint.Camera;
                if (camera.Owner == current)
                {
                    continue;
                }

                var metadata = transform.GetMetadata(camera);
                // Only when an input to the world matrix changed - the node or its parent, the camera position -
                // so a static scene skips the pass. World position: a parented camera's local offset barely changes.
                if (!dirty && metadata.Computed
                    && metadata.LastCameraPosition == viewpoint.Position)
                {
                    continue;
                }

                // The owner's world for THIS camera, computed already (the walk is top-down); identity for a root.
                var parentWorld = current.Owner?.Transform != null
                    ? current.Owner.Transform.GetMetadata(camera).AbsoluteWorld
                    : Matrix4x4F.Identity;
                transform.CalculateFinalTransform(camera, parentWorld, viewpoint.Position);
                if (!dirty)
                {
                    viewpoint.Moved = true;
                }
                else if (ownViewpoint == null)
                {
                    worldMoved = true;
                }

                // Collider bounds ride the same world matrix, so refresh them exactly when it was recomputed (fetch the
                // list lazily so a fully-static node allocates nothing).
                colliders ??= current.GetComponents<Collider>();
                for (int i = 0; i < colliders.Length; ++i)
                {
                    colliders[i].UpdateForCamera(camera);
                }
            }

            transform.IsWorldDirty = false;
            Index(current, dirty);
            // A moved node changes its children's parent-world, so dirty them for this same top-down pass (a camera move
            // needs no propagation - every node detects that independently above).
            if (dirty)
            {
                var children = current.Dependencies.AsSpan();
                for (int i = 0; i < children.Length; ++i)
                {
                    var childTransform = children[i]?.Transform;
                    if (childTransform != null)
                    {
                        childTransform.IsWorldDirty = true;
                    }
                }
            }

            current.GetComponent<AnimationComponent>()?.Update(appTime);
        });
    }

    private void Index(Entity entity, bool changed)
    {
        var mesh = entity.GetComponent<MeshData>()?.Mesh;
        if (mesh == null || !mesh.HasPoints)
        {
            return;
        }

        var skin = Skin.DrawnOn(entity);
        if (skin != null)
        {
            skins.Add((entity, mesh, skin));
            return;
        }

        if (!changed && index.IsCurrent(entity, mesh))
        {
            index.Keep(entity);
            return;
        }

        var camera = CameraFor(entity);
        if (camera == null)
        {
            return;
        }

        var world = entity.Transform.GetMetadata(camera).AbsoluteWorld;
        mesh.Bounds.ToBoundingBox().As<float>().GetCorners(corners);
        var placed = Vector3F.TransformCoordinate(corners[0], world);
        var box = new BoundingBox<float>(placed, placed);
        for (int i = 1; i < corners.Length; i++)
        {
            placed = Vector3F.TransformCoordinate(corners[i], world);
            box.Minimum = Vector3F.Min(box.Minimum, placed);
            box.Maximum = Vector3F.Max(box.Maximum, placed);
        }

        index.Place(entity, mesh, box);
    }

    private void CollectViewpoints()
    {
        viewpointCount = 0;
        var outputs = universe.Outputs;
        for (int i = 0; i < outputs.Count; i++)
        {
            var output = outputs[i];
            var camera = output.Camera;
            if (!output.IsVisible || camera == null)
            {
                continue;
            }

            var viewpoint = ViewpointOf(camera);
            if (viewpoint == null)
            {
                if (viewpointCount == viewpoints.Count)
                {
                    viewpoints.Add(new Viewpoint());
                }

                viewpoint = viewpoints[viewpointCount++];
                viewpoint.Camera = camera;
                viewpoint.Position = camera.WorldPosition;
                viewpoint.Moved = false;
            }

            viewpoint.Outputs.Add(output);
        }
    }

    private Viewpoint ViewpointOf(Camera camera)
    {
        for (int v = 0; v < viewpointCount; v++)
        {
            if (viewpoints[v].Camera == camera)
            {
                return viewpoints[v];
            }
        }

        return null;
    }

    private Viewpoint ViewpointOf(Entity entity)
    {
        var camera = entity.GetComponent<Camera>();
        return camera != null ? ViewpointOf(camera) : null;
    }

    private void InvalidateWhatMoved()
    {
        if (worldMoved)
        {
            universe.Invalidate();
            return;
        }

        for (int v = 0; v < viewpointCount; v++)
        {
            if (!viewpoints[v].Moved) continue;

            foreach (var output in viewpoints[v].Outputs)
            {
                universe.Invalidate(output);
            }
        }
    }

    private void ForgetViewpoints()
    {
        foreach (var viewpoint in viewpoints)
        {
            viewpoint.Camera = null;
            viewpoint.Moved = false;
            viewpoint.Outputs.Clear();
        }

        viewpointCount = 0;
    }

    private void IndexSkins()
    {
        foreach (var (entity, mesh, skin) in skins)
        {
            var camera = CameraFor(entity);
            if (camera != null)
            {
                index.Place(entity, mesh, skin.WorldBounds(mesh, camera), bent: true);
            }
        }

        skins.Clear();
    }

    private Camera CameraFor(Entity entity)
    {
        for (int v = 0; v < viewpointCount; v++)
        {
            if (viewpoints[v].Camera.Owner != entity)
            {
                return viewpoints[v].Camera;
            }
        }

        return null;
    }

    private sealed class Viewpoint
    {
        public readonly List<UniverseOutput> Outputs = [];
        public Camera Camera;
        public Vector3<double> Position;
        public bool Moved;
    }
}