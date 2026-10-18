using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.EntityServices;
using Adamantium.Mathematics;
using Adamantium.Multiverse.Input;

namespace Adamantium.Engine.Tools;

/// <summary>
/// A tool: a modifier or creator driven by the mouse, one in use at a time. A drag goes to the nearest of its handles; a
/// click anywhere else selects what is under the pointer. A selected light brings its own handles to whichever tool is in
/// use, so a light is moved and sized without switching tools. A drag is measured from where the eye stood when it began,
/// so a camera riding on what is dragged does not carry the pointer, and the target after it, further every frame; it
/// stops when the selection or the output's camera changes under it.
/// </summary>
public abstract class ToolProcessor : EditorProcessor
{
    private readonly Handles[] own;
    private readonly Handles[] lightHandles = [new PointLightHandles(), new SpotLightHandles()];
    private readonly List<Handles> active = [];
    private const float ClickSlop = 4;
    private Handles dragged;
    private Entity dragTarget;
    private Camera dragCamera;
    private Vector3 dragEye;
    private bool dragBegun;
    private PickRay pressRay;
    private Vector2F pressedAt;
    private readonly Entity group = new(null, "Selection");
    private readonly List<Held> held = [];
    private IReadOnlyList<Entity> groupItems;
    private Vector3 groupStart;

    protected ToolProcessor(params Handles[] own)
    {
        this.own = own;
    }

    public override int Order => 100;

    /// <summary>How close to a line of a handle the pointer has to be to take it, in points: pixels at 100% scale.</summary>
    public float PickAperture { get; set; } = 6;

    /// <summary>Whether the tool's handles work several selected entities at once, about their common center: a hidden
    /// stand-in at the center takes the handles, and each entity follows what is done to it.</summary>
    protected virtual bool WorksGroups => false;

    public override void Update(AppTime appTime)
    {
        var target = TargetOf(Tools.Selection);
        CollectActive(target);

        var cameras = Tools.Observatory.CurrentCameras;
        for (int i = 0; i < active.Count; i++)
        {
            for (int j = 0; j < cameras.Count; j++)
            {
                active[i].Place(target, cameras[j]);
            }
        }

        if (Tools.Observatory.PointerOutput is not { Camera: { } camera, Input: { CanLocatePointer: true } input })
        {
            Highlight(null, null);
            return;
        }

        var ray = PickRay.FromCamera(camera, input.RelativePosition);

        if (ContinueGesture(input, camera))
        {
            Highlight(null, null);
            return;
        }

        if (dragged != null)
        {
            if (input.IsMouseButtonDown(MouseButton.Left) && target == dragTarget && camera == dragCamera
                && (target != group || ReferenceEquals(Tools.Selection.Items, groupItems))
                && (active.Contains(dragged) || dragged == DragsClicked(target)))
            {
                var fromEye = (Vector3F)(dragEye - camera.WorldPosition);
                if (!dragBegun)
                {
                    // A press on the entity is a click until the pointer moves away from where it went down.
                    if (Vector2F.Distance(input.RelativePosition, pressedAt) <= ClickSlop * camera.PixelsPerPoint)
                    {
                        return;
                    }

                    dragged.BeginDrag(target, null, pressRay.Moved(fromEye));
                    dragBegun = true;
                }

                dragged.Drag(target, ray.Moved(fromEye));
                if (target == group)
                {
                    FollowGroup();
                }

                return;
            }

            dragged = null;
        }

        if (Tools.IsPointerTaken || input.IsMouseButtonDown(MouseButton.Right))
        {
            Highlight(null, null);
            return;
        }

        var hit = PickHandles(ray, PickAperture * camera.PixelsPerPoint, out var owner);
        Highlight(owner, hit.Entity);
        var under = hit.IsHit ? default : Tools.PickEntity(ray);
        Tools.Hovered = under.Entity;

        if (!input.IsMouseButtonPressed(MouseButton.Left))
        {
            return;
        }

        if (hit.IsHit)
        {
            dragged = owner;
            dragTarget = target;
            dragCamera = camera;
            dragEye = camera.WorldPosition;
            dragBegun = true;
            if (target == group)
            {
                HoldGroup();
            }

            dragged.BeginDrag(target, hit.Entity, ray);
            return;
        }

        if (under.Entity == null && PressedOnNothing(input, camera))
        {
            return;
        }

        if (Modifier(Keys.Shift))
        {
            Tools.Selection.Add([under.Entity]);
        }
        else if (Modifier(Keys.Control))
        {
            Tools.Selection.Remove([under.Entity]);
        }
        else
        {
            Tools.Selection.Current = under.Entity;
            if (DragsClicked(under.Entity) is { } mover)
            {
                dragged = mover;
                dragTarget = under.Entity;
                dragCamera = camera;
                dragEye = camera.WorldPosition;
                dragBegun = false;
                pressRay = ray;
                pressedAt = input.RelativePosition;
            }
        }
    }

    private Entity TargetOf(Selection selection)
    {
        var items = selection.Items;
        if (!WorksGroups || items.Count < 2)
        {
            return LookedThrough(selection.Current) ? null : selection.Current;
        }

        if (dragged == null || dragTarget != group)
        {
            var center = Vector3.Zero;
            for (int i = 0; i < items.Count; i++)
            {
                center += Handles.PivotOf(items[i]);
            }

            group.Transform.Position = center / items.Count;
            group.Transform.Rotation = QuaternionF.Identity;
            group.Transform.ScaleFactor = Vector3F.One;
        }

        return group;
    }

    private bool LookedThrough(Entity entity)
    {
        if (entity == null)
        {
            return false;
        }

        var cameras = Tools.Observatory.CurrentCameras;
        for (int i = 0; i < cameras.Count; i++)
        {
            if (cameras[i].Owner == entity)
            {
                return true;
            }
        }

        return false;
    }

    private void HoldGroup()
    {
        groupItems = Tools.Selection.Items;
        groupStart = group.Transform.Position;
        held.Clear();
        for (int i = 0; i < groupItems.Count; i++)
        {
            var entity = groupItems[i];
            if (!UnderSelected(entity))
            {
                var transform = entity.Transform;
                held.Add(new Held(entity, Handles.PivotOf(entity), transform.Position, transform.Rotation, transform.ScaleFactor,
                    Matrix4x4F.RotationQuaternion(Handles.WorldRotationOf(entity, transform.Rotation))));
            }
        }
    }

    private void FollowGroup()
    {
        var transform = group.Transform;
        var turn = transform.Rotation;
        var scale = transform.ScaleFactor;
        var moved = transform.Position - groupStart;
        for (int i = 0; i < held.Count; i++)
        {
            var item = held[i];
            var fromCenter = (Vector3F)(item.Pivot - groupStart) * scale;
            var pivot = groupStart + (Vector3)Vector3F.Transform(fromCenter, turn) + moved;
            var entity = item.Entity;
            entity.Transform.Position = item.Position + (Vector3)Handles.ParentVector(entity, (Vector3F)(pivot - item.Pivot));
            entity.Transform.Rotation = turn.Angle > 1e-6f
                ? QuaternionF.Multiply(QuaternionF.RotationAxis(Vector3F.Normalize(Handles.ParentVector(entity, turn.Axis)), turn.Angle), item.Rotation)
                : item.Rotation;
            entity.Transform.ScaleFactor = item.Scale * AlongOwnAxes(item.Axes, scale);
        }
    }

    private bool UnderSelected(Entity entity)
    {
        for (var owner = entity.Owner; owner != null; owner = owner.Owner)
        {
            if (Tools.Selection.Contains(owner))
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct Held(Entity Entity, Vector3 Pivot, Vector3 Position, QuaternionF Rotation, Vector3F Scale,
        Matrix4x4F Axes);

    private static Vector3F AlongOwnAxes(in Matrix4x4F axes, Vector3F worldScale) => new(
        (new Vector3F(axes.M11, axes.M12, axes.M13) * worldScale).Length(),
        (new Vector3F(axes.M21, axes.M22, axes.M23) * worldScale).Length(),
        (new Vector3F(axes.M31, axes.M32, axes.M33) * worldScale).Length());

    /// <summary>The handles that drag an entity by a press on the entity itself, with no handle taken, once the pointer
    /// moves; null when a press only selects it.</summary>
    protected virtual Handles DragsClicked(Entity entity) => null;

    /// <summary>A press of the left button on nothing - no handle, no entity: true when the tool takes it as the start of
    /// a gesture of its own, which <see cref="ContinueGesture"/> then carries on; false lets the press clear the
    /// selection.</summary>
    protected virtual bool PressedOnNothing(InputWormhole input, Camera camera) => false;

    /// <summary>Carries on a gesture of the tool's own each frame, before anything else the pointer does; true while it
    /// lasts.</summary>
    protected virtual bool ContinueGesture(InputWormhole input, Camera camera) => false;

    /// <summary>Whether the key is held on the output taking the keyboard: Shift adds a click's entity to the selection,
    /// Control takes it out.</summary>
    protected bool Modifier(Keys key) =>
        Tools.Observatory.PointerInput?.IsKeyDown(key) == true || Tools.Observatory.KeyboardInput?.IsKeyDown(key) == true;

    public override void DrawOverlay(EditorOverlayProcessor overlay)
    {
        for (int i = 0; i < active.Count; i++)
        {
            active[i].Draw(overlay);
        }
    }

    protected override void OnDetached()
    {
        Highlight(null, null);
        dragged = null;
        active.Clear();
    }

    private void CollectActive(Entity target)
    {
        active.Clear();
        AddApplying(own, target);
        AddApplying(lightHandles, target);
    }

    private void AddApplying(Handles[] handles, Entity target)
    {
        for (int i = 0; i < handles.Length; i++)
        {
            if (handles[i].AppliesTo(target))
            {
                active.Add(handles[i]);
            }
        }
    }

    private PickHit PickHandles(in PickRay ray, float aperture, out Handles owner)
    {
        owner = null;
        var nearest = default(PickHit);
        for (int i = 0; i < active.Count; i++)
        {
            var hit = active[i].Pick(ray, aperture);
            if (hit.IsHit && (!nearest.IsHit || hit.Depth < nearest.Depth))
            {
                nearest = hit;
                owner = active[i];
            }
        }

        return nearest;
    }

    private void Highlight(Handles owner, Entity handle)
    {
        for (int i = 0; i < active.Count; i++)
        {
            active[i].Highlight(active[i] == owner ? handle : null);
        }
    }
}
