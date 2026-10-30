using System;
using System.Collections.Generic;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.EntityServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Input;

namespace Adamantium.Engine.Tools;

/// <summary>
/// Selects what a click lands on - the whole model, the top of its tree, first, and the part under the pointer once the
/// model is selected (<see cref="Selection.ChosenByClick"/>) - with Shift held adding it to the selection, with
/// Control taking it out; an entity
/// with a <see cref="Marker"/> - its icon or, if it has one, its form - is also dragged in the plane facing the eye. A
/// drag from nothing draws a frame across the screen that selects, CAD style, what lies wholly inside it when drawn to
/// the right and whatever it touches when drawn to the left (<see cref="ScreenFrame"/>), each the whole model it is part
/// of; a
/// click on nothing selects nothing.
/// </summary>
public class SelectTool : ToolProcessor
{
    private const float FrameSlop = 4;
    private const float Depth = 1;
    private const float Dash = 6;
    private const float Gap = 4;
    private static readonly Vector4F WindowColor = new(0.3f, 0.55f, 1f, 1f);
    private static readonly Vector4F CrossingColor = new(0.35f, 0.85f, 0.45f, 1f);
    private const float FillStrength = 0.15f;

    private readonly MoveHandles mover = new();
    private readonly List<Entity> framed = [];
    private readonly Entity frame = new(null, "Frame");
    private readonly Entity fill;
    private readonly Entity border;
    private readonly Mesh borderMesh = new(PrimitiveType.LineList);
    private readonly List<Vector3<double>> borderPoints = [];
    private readonly List<int> borderIndices = [];
    private bool pressed;
    private bool framing;
    private Entity pressedOn;
    private Camera frameCamera;
    private Vector2F frameFrom;
    private Vector2F frameTo;

    public SelectTool()
    {
        fill = Part("Fill", UnitSquare());
        border = Part("Border", borderMesh);
    }

    protected override Handles DragsClicked(Entity entity)
    {
        return entity?.GetComponent<Marker>() is { IsEnabled: true } ? mover : null;
    }

    /// <summary>Whether a press on an entity waits for the release to click it, and draws a frame instead once the
    /// pointer moves away: for a tool whose click cannot be taken back.</summary>
    protected virtual bool ClicksOnRelease => false;

    protected override bool PressedOnNothing(InputWormhole input, Camera camera) => BeginGesture(input, camera, null);

    protected override bool PressedOn(Entity entity, InputWormhole input, Camera camera) =>
        ClicksOnRelease && BeginGesture(input, camera, entity);

    private bool BeginGesture(InputWormhole input, Camera camera, Entity entity)
    {
        pressed = true;
        framing = false;
        pressedOn = entity;
        frameCamera = camera;
        frameFrom = input.RelativePosition;
        frameTo = frameFrom;
        return true;
    }

    protected override bool ContinueGesture(InputWormhole input, Camera camera)
    {
        if (!pressed)
        {
            return false;
        }

        if (camera != frameCamera)
        {
            pressed = false;
            framing = false;
            pressedOn = null;
            return false;
        }

        frameTo = input.RelativePosition;
        framing |= Vector2F.Distance(frameTo, frameFrom) > FrameSlop * camera.PixelsPerPoint;
        if (input.IsMouseButtonDown(MouseButton.Left))
        {
            Tools.TakePointer();
            return true;
        }

        pressed = false;
        if (!framing)
        {
            if (pressedOn != null)
            {
                Clicked(pressedOn);
            }
            else if (!Modifier(Keys.Shift) && !Modifier(Keys.Control))
            {
                Clicked(null);
            }

            pressedOn = null;
            return true;
        }

        pressedOn = null;

        framing = false;
        framed.Clear();
        Tools.PickEntities(camera, new ScreenFrame(frameFrom, frameTo), framed);
        Framed(WholesOf(framed));
        return true;
    }

    // A frame takes the wholes its parts belong to, each once, in the order first met.
    private static List<Entity> WholesOf(List<Entity> parts)
    {
        List<Entity> wholes = [];
        foreach (var part in parts)
        {
            var whole = Selection.WholeOf(part);
            if (!wholes.Contains(whole))
            {
                wholes.Add(whole);
            }
        }

        return wholes;
    }

    /// <summary>What a frame drawn across the screen took: selects it - with Shift adding it to the selection, with
    /// Control taking it out.</summary>
    protected virtual void Framed(IReadOnlyList<Entity> entities)
    {
        if (Modifier(Keys.Shift))
        {
            Tools.Selection.Add(entities);
        }
        else if (Modifier(Keys.Control))
        {
            Tools.Selection.Remove(entities);
        }
        else
        {
            Tools.Selection.Select(entities);
        }
    }

    public override void DrawOverlay(EditorOverlayProcessor overlay)
    {
        base.DrawOverlay(overlay);
        if (!framing || overlay.Camera != frameCamera)
        {
            return;
        }

        var shown = new ScreenFrame(frameFrom, frameTo);
        var color = shown.Crossing ? CrossingColor : WindowColor;
        var size = shown.Max - shown.Min;
        Show(fill, Matrix4x4.CreateScale<float>(size.X, size.Y, 1) * Matrix4x4.CreateTranslation<float>(shown.Min.X, shown.Min.Y, Depth), color,
            FillStrength);
        Outline(shown);
        Show(border, Matrix4x4.CreateTranslation<float>(0, 0, Depth), color, 1);
        overlay.DrawOnScreen(frame);
    }

    protected override void OnDetached()
    {
        pressed = false;
        framing = false;
        pressedOn = null;
        base.OnDetached();
    }

    private void Show(Entity part, Matrix4x4F world, Vector4F color, float strength)
    {
        var metadata = part.Transform.GetMetadata(frameCamera);
        metadata.WorldMatrixF = world;
        metadata.Enabled = true;
        var material = part.GetComponent<Material>();
        material.MeshColor = new Vector3F(color.X, color.Y, color.Z);
        material.Transparency = color.W * strength;
    }

    private void Outline(in ScreenFrame shown)
    {
        borderPoints.Clear();
        borderIndices.Clear();
        var a = shown.Min;
        var b = new Vector2F(shown.Max.X, shown.Min.Y);
        var c = shown.Max;
        var d = new Vector2F(shown.Min.X, shown.Max.Y);
        Edge(a, b, shown.Crossing);
        Edge(b, c, shown.Crossing);
        Edge(c, d, shown.Crossing);
        Edge(d, a, shown.Crossing);
        borderMesh.SetPoints(borderPoints);
        borderMesh.SetIndices(borderIndices);
    }

    private void Edge(Vector2F from, Vector2F to, bool dashed)
    {
        var length = Vector2F.Distance(from, to);
        var step = dashed ? Dash + Gap : length;
        for (var at = 0f; at < length; at += step)
        {
            var end = dashed ? Math.Min(at + Dash, length) : length;
            var start = from + (to - from) * (length > 0 ? at / length : 0);
            var stop = from + (to - from) * (length > 0 ? end / length : 0);
            borderIndices.Add(borderPoints.Count);
            borderPoints.Add(new Vector3<double>(start.X, start.Y, 0));
            borderIndices.Add(borderPoints.Count);
            borderPoints.Add(new Vector3<double>(stop.X, stop.Y, 0));
            if (!dashed)
            {
                break;
            }
        }
    }

    private Entity Part(string name, Mesh mesh)
    {
        var part = new Entity(frame, name);
        part.AddComponent(new MeshData { Mesh = mesh });
        part.AddComponent(new Material { HighlightColor = Vector4F.One, Transparency = 1 });
        return part;
    }

    private static Mesh UnitSquare()
    {
        var mesh = new Mesh(PrimitiveType.TriangleList);
        mesh.SetPoints(new List<Vector3<double>> { new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0) });
        mesh.SetIndices(new List<int> { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 });
        return mesh;
    }
}
