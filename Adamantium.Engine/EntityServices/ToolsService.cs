using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.Engine.Tools;

namespace Adamantium.Engine.EntityServices;

/// <summary>
/// Runs the editor's processors once per frame: what sits over the scene first, then the one tool in use. Only an editor
/// creates it; a universe without the editor has no tools.
/// </summary>
public class ToolsService : EntityService
{
    private ToolProcessor tool;
    private volatile ToolProcessor requested;

    public ToolsService(EntityWorld world)
        : base(world)
    {
        Priority = 1;
    }

    public override bool IsUpdateService => true;
    public override bool IsRenderingService => false;
    public override EntityServiceType ServiceType => EntityServiceType.Update;

    public Observatory Observatory { get; private set; }

    public Selection Selection { get; private set; }

    /// <summary>The tool in use: one at a time, or none. Set from any thread; the change takes effect with the next frame.</summary>
    public ToolProcessor Tool
    {
        get => requested;
        set => requested = value;
    }

    /// <summary>Whether something over the scene has taken the pointer this frame; the tool then leaves it alone.</summary>
    public bool IsPointerTaken { get; private set; }

    /// <summary>What a click would select where the pointer is now; the tool in use finds it every frame.</summary>
    public Entity Hovered { get; set; }

    public void TakePointer()
    {
        IsPointerTaken = true;
    }

    /// <summary>What a click along <paramref name="ray"/> selects: the nearest part of the scene, or of what the editor shows over it.</summary>
    public PickHit PickEntity(in PickRay ray)
    {
        var nearest = Picking.Pick(EntityWorld.RootEntities, ray, PickMode.Triangles, withDisabled: true);
        var processors = Processors;
        for (int i = 0; i < processors.Count; i++)
        {
            if (processors[i] is EditorProcessor editor)
            {
                nearest = PickHit.Nearest(nearest, editor.PickEntity(ray));
            }
        }

        return nearest;
    }

    /// <summary>What <paramref name="frame"/> on <paramref name="camera"/>'s output selects, as a click would select it:
    /// the parts of the scene with a form - all their points inside the frame, or for a crossing one any of them or the
    /// frame's middle on them - and what the editor shows over the scene, into <paramref name="into"/>.</summary>
    public void PickEntities(CameraBase camera, in ScreenFrame frame, List<Entity> into)
    {
        var viewProjection = camera.ViewMatrix * camera.ProjectionMatrix;
        var middle = PickRay.FromCamera(camera, frame.Center);
        var roots = EntityWorld.RootEntities;
        for (int i = 0; i < roots.Count; i++)
        {
            FrameTree(roots[i], camera, in frame, in viewProjection, in middle, into);
        }

        var processors = Processors;
        for (int i = 0; i < processors.Count; i++)
        {
            if (processors[i] is EditorProcessor editor)
            {
                editor.PickEntities(camera, in frame, into);
            }
        }
    }

    private readonly List<(Vector2F Pixel, bool InFront)> shown = [];

    private void FrameTree(Entity entity, CameraBase camera, in ScreenFrame frame, in Matrix4x4F viewProjection,
        in PickRay middle, List<Entity> into)
    {
        if (!entity.Visible)
        {
            return;
        }

        if (!entity.IgnoreInCollisionDetection && Framed(entity, camera, in frame, in viewProjection, in middle))
        {
            into.Add(entity);
        }

        var children = entity.Dependencies;
        for (int i = 0; i < children.Count; i++)
        {
            FrameTree(children[i], camera, in frame, in viewProjection, in middle, into);
        }
    }

    private bool Framed(Entity entity, CameraBase camera, in ScreenFrame frame, in Matrix4x4F viewProjection,
        in PickRay middle)
    {
        var metadata = entity.Transform.GetMetadata(camera);
        var mesh = entity.GetComponent<MeshData>() is { IsEnabled: true } data ? data.Mesh : null;
        if (!metadata.Enabled || mesh == null || !mesh.HasPoints)
        {
            return false;
        }

        var toClip = metadata.WorldMatrixF * viewProjection;
        var points = mesh.Points;
        shown.Clear();
        for (int i = 0; i < points.Length; i++)
        {
            var point = (Vector3F)points[i];
            var w = point.X * toClip.M14 + point.Y * toClip.M24 + point.Z * toClip.M34 + toClip.M44;
            var pixel = Vector3F.Project(point, 0, 0, camera.Width, camera.Height, 0, 1, toClip);
            var inFront = w > 1e-6f;
            shown.Add((new Vector2F(pixel.X, pixel.Y), inFront));
            var inside = inFront && frame.Contains(shown[i].Pixel);
            if (frame.Crossing && inside)
            {
                return true;
            }

            if (!frame.Crossing && !inside)
            {
                return false;
            }
        }

        if (!frame.Crossing)
        {
            return true;
        }

        var indices = mesh.Indices;
        var count = indices.Length > 0 ? indices.Length : points.Length;
        var topology = mesh.MeshTopology;
        var partlyBehind = false;
        if (topology == PrimitiveType.TriangleList)
        {
            for (int i = 0; i + 2 < count; i += 3)
            {
                var front = Seen(indices, i, out var a) & Seen(indices, i + 1, out var b) & Seen(indices, i + 2, out var c);
                if (front && frame.Touches(a, b, c))
                {
                    return true;
                }

                partlyBehind |= !front;
            }
        }
        else if (topology == PrimitiveType.LineList)
        {
            for (int i = 0; i + 1 < count; i += 2)
            {
                var front = Seen(indices, i, out var a) & Seen(indices, i + 1, out var b);
                if (front && frame.Touches(a, b))
                {
                    return true;
                }

                partlyBehind |= !front;
            }
        }
        else
        {
            partlyBehind = true;
        }

        return partlyBehind && HitThroughFrame(entity, camera, in frame, in middle);
    }

    private static bool HitThroughFrame(Entity entity, CameraBase camera, in ScreenFrame frame, in PickRay middle)
    {
        if (Hits(entity, in middle))
        {
            return true;
        }

        var corners = new[] { frame.Min, frame.Max, new Vector2F(frame.Min.X, frame.Max.Y), new Vector2F(frame.Max.X, frame.Min.Y) };
        foreach (var corner in corners)
        {
            if (Hits(entity, PickRay.FromCamera(camera, corner)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Hits(Entity entity, in PickRay ray) =>
        entity.Pick(ray, PickMode.Triangles, withDisabled: true) is { IsHit: true } hit && hit.Entity == entity;

    private bool Seen(int[] indices, int at, out Vector2F pixel)
    {
        var index = indices.Length > 0 ? indices[at] : at;
        var point = index >= 0 && index < shown.Count ? shown[index] : default;
        pixel = point.Pixel;
        return point.InFront;
    }

    public override void Initialize()
    {
        Observatory = EntityWorld.Satellites.Get<Observatory>();
        Selection = EntityWorld.Satellites.Get<Selection>();
    }

    public override void Update(AppTime appTime)
    {
        var next = requested;
        if (next != tool)
        {
            DetachProcessor(tool);
            tool = next;
            AttachProcessor(tool);
        }

        IsPointerTaken = false;
        Hovered = null;
        base.Update(appTime);

        var cameras = Observatory?.CurrentCameras ?? [];
        for (int i = 0; i < cameras.Count; i++)
        {
            cameras[i].Reorbit();
        }
    }
}
