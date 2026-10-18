using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Multiverse;
using Adamantium.Mathematics;
using Serilog;

namespace Adamantium.Engine.EntityServices;

public class TransformService : EntityService
{
    private IUniverse universe;
    private readonly List<Camera> cameras = [];

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
    }

    public override void Update(AppTime appTime)
    {
        var entities = Entities;
        try
        {
            universe.CollectCurrentCameras(cameras);
            foreach (var entity in entities)
            {
                Transform(entity, appTime);
            }
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Transform update failed");
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
            Collider[] colliders = null;

            foreach (var camera in cameras)
            {
                if (camera.Owner == current)
                {
                    continue;
                }

                var metadata = transform.GetMetadata(camera);
                // Only when an input to the world matrix changed - the node or its parent, the camera position -
                // so a static scene skips the pass. World position: a parented camera's local offset barely changes.
                if (!dirty && metadata.Computed
                    && metadata.LastCameraPosition == camera.WorldPosition)
                {
                    continue;
                }

                // The owner's world for THIS camera, computed already (the walk is top-down); identity for a root.
                var parentWorld = current.Owner?.Transform != null
                    ? current.Owner.Transform.GetMetadata(camera).AbsoluteWorld
                    : Matrix4x4F.Identity;
                transform.CalculateFinalTransform(camera, parentWorld);

                // Collider bounds ride the same world matrix, so refresh them exactly when it was recomputed (fetch the
                // list lazily so a fully-static node allocates nothing).
                colliders ??= current.GetComponents<Collider>();
                for (int i = 0; i < colliders.Length; ++i)
                {
                    colliders[i].UpdateForCamera(camera);
                }
            }

            transform.IsWorldDirty = false;
            // A moved node changes its children's parent-world, so dirty them for this same top-down pass (a camera move
            // needs no propagation - every node detects that independently above).
            if (dirty)
            {
                for (int i = 0; i < current.Dependencies.Count; ++i)
                {
                    var childTransform = current.Dependencies[i].Transform;
                    if (childTransform != null)
                    {
                        childTransform.IsWorldDirty = true;
                    }
                }
            }

            current.GetComponent<AnimationComponent>()?.Update(appTime);
            current.GetComponent<AnimationController>()?.Update(appTime);
        });
    }
}