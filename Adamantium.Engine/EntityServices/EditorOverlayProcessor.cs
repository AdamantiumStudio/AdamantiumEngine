using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Rendering;
using Adamantium.Engine.Tools;
using Adamantium.FX;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Graphics.Core.Models;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;

namespace Adamantium.Engine.EntityServices;

/// <summary>
/// Draws the editor's processors into one output's frame, after its scene: in the scene, in front of what it drew, and
/// on the screen.
/// </summary>
public class EditorOverlayProcessor : RenderingProcessor
{
    private const int OutlineDirections = 8;

    private static readonly Mesh RingSquare = new Mesh(PrimitiveType.TriangleList)
        .SetPoints(new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0) })
        .SetIndices(new[] { 0, 1, 2, 2, 1, 3 });

    private readonly ToolsService tools;
    private readonly List<Part> parts = [];
    private GizmoInstance[] instances = [];
    private readonly Dictionary<Mesh, Ribbon> ribbons = [];
    private readonly Vector4F[] gridPhases = new Vector4F[GroundGrid.Levels / 2];
    private Matrix4x4F[] palette = [];
    private Skeleton paletteSkeleton;
    private Matrix4x4F paletteMesh;
    private bool[] drawn = [];
    private uint outlineMark;
    private BasicEffect effect;
    private GridEffect gridEffect;
    private MeshGeometryCache geometry;

    public EditorOverlayProcessor(ToolsService tools)
    {
        this.tools = tools;
    }

    public override int Order => 100;

    /// <summary>The camera the frame is drawn with.</summary>
    public Camera Camera => ActiveCamera;

    /// <summary>How wide lines are drawn, in points: pixels at 100% scale, always a whole number of pixels on screen.</summary>
    public float LineWidth { get; set; } = 2;

    private float LinePixels => MathF.Max(1, MathF.Round(LineWidth * ActiveCamera.PixelsPerPoint));

    protected override void CreateDeviceResources()
    {
        geometry?.Dispose();
        gridEffect?.Dispose();
        effect = new BasicEffect(GraphicsDevice);
        gridEffect = new GridEffect(GraphicsDevice);
        geometry = new MeshGeometryCache(GraphicsDevice);
        base.CreateDeviceResources();
    }

    protected override void OnDetached()
    {
        geometry?.Dispose();
        gridEffect?.Dispose();
        base.OnDetached();
    }

    public override void Draw(AppTime appTime)
    {
        ActiveCamera = Window.Camera;
        if (ActiveCamera == null)
        {
            return;
        }

        outlineMark = 0;
        paletteSkeleton = null;
        var processors = tools.Processors;
        for (int i = 0; i < processors.Count; i++)
        {
            if (processors[i].IsEnabled && processors[i] is EditorProcessor editor)
            {
                editor.DrawOverlay(this);
            }
        }
    }

    public override void EndDraw()
    {
    }

    /// <summary>Draws an entity and what is under it in the scene, in front of everything the scene drew.</summary>
    public void DrawInScene(Entity root)
    {
        Collect(root);
        effect.View.SetValue(ActiveCamera.ViewMatrix);
        DrawParts(ActiveCamera.ViewProjectionMatrix, true, Look.Shaded);
    }

    /// <summary>Draws an entity and what is under it in the scene as a see-through ghost of one tint, hidden where the
    /// scene stands in front of it; the tint's alpha is how much of it shows.</summary>
    public void DrawGhost(Entity root, Vector4F tint)
    {
        Collect(root);
        for (var i = 0; i < parts.Count; i++)
        {
            parts[i] = parts[i] with { Color = tint };
        }

        effect.View.SetValue(ActiveCamera.ViewMatrix);
        DrawParts(ActiveCamera.ViewProjectionMatrix, false, Look.Shaded);
    }

    /// <summary>Draws an entity and what is under it in the scene in its flat colors, in front of everything the scene
    /// drew: for pictograms, which have no form to shade.</summary>
    public void DrawFlatInScene(Entity root)
    {
        Collect(root);
        DrawParts(ActiveCamera.ViewProjectionMatrix, true, Look.Flat);
    }

    /// <summary>Draws an entity in its colors, unlit, where the scene does not stand in front of it: its meshes' own
    /// depth test and write, as the ground grid's lines are hidden behind what stands on the ground.</summary>
    public void DrawFlatBehindScene(Entity root)
    {
        Collect(root);
        DrawParts(ActiveCamera.ViewProjectionMatrix, false, Look.Flat);
    }

    /// <summary>
    /// Draws a ring - a line mesh round a circle, flat in its own space - as the circle itself, in front of everything
    /// the scene drew: each pixel's distance to the true circle, so the edge is even at any turn. The center lands on the
    /// pixel grid and the radius is whole pixels, so a line along it covers whole pixels. With
    /// <paramref name="frontHalf"/> only the half on the eye's side of its center shows, as the rings round a ball do.
    /// </summary>
    public void DrawRing(Entity ring, bool frontHalf)
    {
        var transformation = ring.Transform.GetMetadata(ActiveCamera);
        var mesh = ring.GetComponent<MeshData>()?.Mesh;
        if (!transformation.Enabled || !ring.Visible || mesh == null)
        {
            return;
        }

        var world = transformation.WorldMatrixF;
        var extent = (Vector3F)mesh.Bounds.HalfExtent;
        var axis = extent.X <= extent.Y && extent.X <= extent.Z ? Vector3F.UnitX
            : extent.Y <= extent.Z ? Vector3F.UnitY
            : Vector3F.UnitZ;
        var inPlane = axis == Vector3F.UnitX ? Vector3F.UnitY : Vector3F.UnitX;
        var pixels = LinePixels;
        var center = ScreenSpace.OnPixelGrid(
            Vector3F.TransformCoordinate((Vector3F)mesh.Bounds.Center, world), ActiveCamera, pixels % 2 == 0 ? 0 : 0.5f);
        var unitsPerPixel = ScreenSpace.UnitsPerPixel(ActiveCamera, (Vector3)center + ActiveCamera.WorldPosition);
        var normal = Vector3F.Normalize(Vector3F.TransformNormal(axis, world));
        var radius = Math.Max(extent.X, Math.Max(extent.Y, extent.Z)) * Vector3F.TransformNormal(inPlane, world).Length();
        radius = MathF.Round(radius / unitsPerPixel) * unitsPerPixel;

        var near = Math.Min(radius / center.Length(), 0.9f);
        var reach = radius / MathF.Sqrt(1 - near * near) + (pixels + 4) * unitsPerPixel;
        var placement = Matrix4x4F.Scaling(reach)
                        * Matrix4x4F.RotationQuaternion(ScreenSpace.Facing(ActiveCamera))
                        * Matrix4x4F.Translation(center);
        effect.ViewProjection.SetValue(ActiveCamera.ViewProjectionMatrix);
        effect.LineWidth.SetValue(pixels);
        effect.Ring.SetValue(new Vector4F(normal, radius));
        effect.FrontHalfOnly.SetValue(frontHalf ? 1u : 0u);
        UploadInstances([new GizmoInstance(placement, ColorOf(ring, transformation))]);
        effect.BasicSdfRingInstancedPass.Apply();

        var blend = GraphicsDevice.ColorBlendEquation;
        GraphicsDevice.ColorBlendEquation = ColorBlendEquations.AlphaBlend;
        geometry.GetOrCreate(RingSquare, typeof(MeshVertex))
            .Draw(GraphicsDevice, new RenderState(false, CullModeFlagBits.None, false, false, null), 1);
        GraphicsDevice.ColorBlendEquation = blend;
    }

    /// <summary>Draws the ground grid behind what stands on it: the scene's depth hides it, and it leaves no depth of its
    /// own, so nothing drawn after it is hidden by it.</summary>
    public void DrawGrid(GroundGrid grid)
    {
        var layout = grid.LayoutFor(ActiveCamera);
        var pointPixels = ActiveCamera.PixelsPerPoint;
        grid.PhasesFor(ActiveCamera, gridPhases);

        gridEffect.Placement.SetValue(layout.Placement);
        gridEffect.ViewProjection.SetValue(ActiveCamera.ViewProjectionMatrix);
        gridEffect.Phases.SetValue(gridPhases);
        gridEffect.Layout.SetValue(new Vector4F(layout.Cell, layout.Reach, layout.Center.X, layout.Center.Y));
        gridEffect.Lines.SetValue(new Vector4F(layout.Axes, MathF.Max(1, MathF.Round(grid.LineWidth * pointPixels)),
            grid.ShownCellPointsOrDefault * pointPixels));
        gridEffect.MinorColor.SetValue(grid.MinorColor);
        gridEffect.MajorColor.SetValue(grid.MajorColor);
        gridEffect.AxisXColor.SetValue(grid.AxisXColor);
        gridEffect.AxisZColor.SetValue(grid.AxisZColor);
        gridEffect.GridGroundPass.Apply();

        var blend = GraphicsDevice.ColorBlendEquation;
        GraphicsDevice.ColorBlendEquation = ColorBlendEquations.AlphaBlend;
        geometry.GetOrCreate(RingSquare, typeof(MeshVertex))
            .Draw(GraphicsDevice, new RenderState(false, CullModeFlagBits.None, true, false, null), 1);
        GraphicsDevice.ColorBlendEquation = blend;
    }

    /// <summary>Draws an entity laid out in the output's pixels.</summary>
    public void DrawOnScreen(Entity root)
    {
        Collect(root);
        effect.View.SetValue(Matrix4x4F.Identity);
        DrawParts(ActiveCamera.UiProjection, false, Look.Shaded);
    }

    /// <summary>
    /// Outlines an entity of the scene and what is under it: a band <paramref name="points"/> wide round their joint
    /// silhouette, in front of everything the scene drew. One the scene has not placed for this camera yet - added this
    /// frame - is outlined from the next.
    /// </summary>
    public void DrawOutline(Entity root, Vector4F color, float points)
    {
        parts.Clear();
        if (IsPlaced(root))
        {
            CollectInto(root);
        }

        DrawCollectedOutline(color, points);
    }

    /// <summary>
    /// Outlines several entities of the scene and what is under each: a band <paramref name="points"/> wide round their
    /// joint silhouette, in front of everything the scene drew. Those the scene has not placed for this camera yet are
    /// outlined from the next frame.
    /// </summary>
    public void DrawOutline(IReadOnlyList<Entity> roots, Vector4F color, float points)
    {
        parts.Clear();
        for (int i = 0; i < roots.Count; i++)
        {
            if (IsPlaced(roots[i]))
            {
                CollectInto(roots[i]);
            }
        }

        DrawCollectedOutline(color, points);
    }

    /// <summary>
    /// Outlines an entity alone, without what is under it - an icon without its rays: a band <paramref name="points"/>
    /// wide round its silhouette, in front of everything the scene drew.
    /// </summary>
    public void DrawOutlineAlone(Entity entity, Vector4F color, float points)
    {
        parts.Clear();
        CollectInto(entity, descend: false);
        DrawCollectedOutline(color, points);
    }

    private void DrawCollectedOutline(Vector4F color, float points)
    {
        if (parts.Count == 0)
        {
            return;
        }

        for (int i = 0; i < parts.Count; i++)
        {
            parts[i] = parts[i] with { Color = color };
        }

        var colorMask = GraphicsDevice.ColorComponentFlags;
        GraphicsDevice.StencilTestEnabled = true;
        GraphicsDevice.StencilReference = ++outlineMark;
        GraphicsDevice.StencilCompareOp = CompareOp.Always;
        GraphicsDevice.StencilPassOp = StencilOp.Replace;
        GraphicsDevice.StencilWriteMask = 0xFF;
        GraphicsDevice.ColorComponentFlags = 0;
        DrawParts(ActiveCamera.ViewProjectionMatrix, true, Look.Mask);

        GraphicsDevice.StencilCompareOp = CompareOp.NotEqual;
        GraphicsDevice.StencilPassOp = StencilOp.Keep;
        GraphicsDevice.StencilWriteMask = 0;
        GraphicsDevice.ColorComponentFlags = colorMask;
        var pixels = points * ActiveCamera.PixelsPerPoint;
        effect.OutlineStep.SetValue(new Vector2F(2 * pixels / ActiveCamera.Width, 2 * pixels / ActiveCamera.Height));
        DrawParts(ActiveCamera.ViewProjectionMatrix, true, Look.Outline);

        GraphicsDevice.StencilTestEnabled = false;
    }

    private bool IsPlaced(Entity entity) => entity.Transform.GetMetadata(ActiveCamera).Computed;

    private void Collect(Entity root)
    {
        parts.Clear();
        CollectInto(root);
    }

    private void CollectInto(Entity root, bool descend = true)
    {
        root.VisitInDepth(current =>
        {
            if (!current.Visible)
            {
                return false;
            }

            var transformation = current.Transform.GetMetadata(ActiveCamera);
            if (!transformation.Enabled)
            {
                return descend;
            }

            var color = ColorOf(current, transformation);

            foreach (var data in current.GetComponents<MeshData>())
            {
                if (data?.Mesh == null || !data.IsEnabled)
                {
                    continue;
                }

                var topology = data.Mesh.MeshTopology;
                var line = topology == PrimitiveType.LineList || topology == PrimitiveType.LineStrip;
                var skin = data.RenderMode == MeshRenderMode.Skinned ? current.GetComponent<Skin>() : null;
                parts.Add(new Part(data, transformation.WorldMatrixF, color, line || !data.Mesh.IsNormalsPresent, line, skin));
            }

            return descend;
        });
    }

    private static Vector4F ColorOf(Entity current, TransformMetaData transformation)
    {
        var material = current.GetComponent<Material>();
        if (material == null)
        {
            return Colors.White.ToVector4();
        }

        return transformation.IsSelected || current.IsSelected
            ? material.HighlightColor
            : new Vector4F(material.MeshColor, material.Transparency);
    }

    private void DrawParts(Matrix4x4F viewProjection, bool inFront, Look look)
    {
        if (parts.Count == 0)
        {
            return;
        }

        var directions = look == Look.Outline ? OutlineDirections : 1;
        effect.ViewProjection.SetValue(viewProjection);

        if (drawn.Length < parts.Count)
        {
            drawn = new bool[parts.Count];
        }

        Array.Clear(drawn, 0, parts.Count);

        var blend = GraphicsDevice.ColorBlendEquation;
        if (look is Look.Shaded or Look.Flat)
        {
            GraphicsDevice.ColorBlendEquation = ColorBlendEquations.AlphaBlend;
        }

        for (var i = 0; i < parts.Count; ++i)
        {
            if (drawn[i])
            {
                continue;
            }

            var head = parts[i];
            var count = 0;

            // A skin bends its mesh with its own palette, so its part is drawn alone.
            for (var j = i; j < parts.Count && (head.Skin == null || count < 1); ++j)
            {
                if (drawn[j] || !ReferenceEquals(parts[j].Data.Mesh, head.Data.Mesh) || parts[j].Skin != head.Skin)
                {
                    continue;
                }

                if (count == instances.Length)
                {
                    Array.Resize(ref instances, Math.Max(count * 2, 1));
                }

                instances[count] = new GizmoInstance(parts[j].World, parts[j].Color);
                drawn[j] = true;
                ++count;
            }

            UploadInstances(instances.AsSpan(0, count));
            var drawnInstances = (uint)(count * directions);

            if (head.Line && look is Look.Shaded or Look.Flat)
            {
                DrawRibbon(head.Data, inFront, (uint)count);
                continue;
            }

            if (head.Skin != null)
            {
                FillPalette(head.Skin);
            }

            PassFor(look, head.Flat, head.Skin != null).Apply();
            if (inFront)
            {
                var data = head.Data;
                var state = new RenderState(data.IsWireFrame, data.CullMode, false, false, data.TopologyOverride);
                geometry.GetOrCreate(data.Mesh, data.ResolveVertexType()).Draw(GraphicsDevice, state, drawnInstances);
            }
            else
            {
                geometry.DrawMesh(GraphicsDevice, head.Data, drawnInstances);
            }
        }

        GraphicsDevice.ColorBlendEquation = blend;
    }

    private void FillPalette(Skin skin)
    {
        var mesh = skin.Owner.Transform.GetWorldMatrixF(ActiveCamera);
        if (ReferenceEquals(skin.Skeleton, paletteSkeleton) && mesh == paletteMesh)
        {
            return;
        }

        if (palette.Length < skin.Joints.Count)
        {
            palette = new Matrix4x4F[skin.Joints.Count];
        }

        var filled = skin.FillPalette(palette, ActiveCamera);
        paletteSkeleton = skin.Skeleton;
        paletteMesh = mesh;
        effect.JointsAddress.SetValue(GraphicsDevice.UploadFrameData<Matrix4x4F>(palette.AsSpan(0, filled)));
        effect.JointCount.SetValue((uint)filled);
    }

    private void UploadInstances(ReadOnlySpan<GizmoInstance> batch)
    {
        effect.InstancesAddress.SetValue(GraphicsDevice.UploadFrameData(batch));
    }

    private void DrawRibbon(MeshData data, bool inFront, uint instances)
    {
        effect.ViewportSize.SetValue(new Vector2F(ActiveCamera.Width, ActiveCamera.Height));
        effect.LineWidth.SetValue(LinePixels);
        effect.BasicSdfLineInstancedPass.Apply();

        var state = inFront
            ? new RenderState(false, CullModeFlagBits.None, false, false, null)
            : new RenderState(false, CullModeFlagBits.None, data.DepthTestEnabled, data.DepthWriteEnabled, null);
        var blend = GraphicsDevice.ColorBlendEquation;
        GraphicsDevice.ColorBlendEquation = ColorBlendEquations.AlphaBlend;
        geometry.GetOrCreate(RibbonOf(data.Mesh), data.ResolveVertexType()).Draw(GraphicsDevice, state, instances);
        GraphicsDevice.ColorBlendEquation = blend;
    }

    private Mesh RibbonOf(Mesh lines)
    {
        if (!ribbons.TryGetValue(lines, out var ribbon) || !ReferenceEquals(ribbon.Points, lines.Points)
            || !ReferenceEquals(ribbon.Indices, lines.Indices))
        {
            ribbon = new Ribbon(lines.Points, lines.Indices, LineRibbon.Build(lines));
            ribbons[lines] = ribbon;
        }

        return ribbon.Mesh;
    }

    private IEffectPass PassFor(Look look, bool flat, bool skinned)
    {
        if (skinned)
        {
            return look switch
            {
                Look.Outline => effect.BasicSkinnedOutlineInstancedPass,
                Look.Mask or Look.Flat => effect.BasicSkinnedFlatInstancedPass,
                _ => flat ? effect.BasicSkinnedFlatInstancedPass : effect.BasicSkinnedLitInstancedPass
            };
        }

        return look switch
        {
            Look.Outline => effect.BasicOutlineInstancedPass,
            Look.Mask or Look.Flat => effect.BasicFlatInstancedPass,
            _ => flat ? effect.BasicFlatInstancedPass : effect.BasicLitInstancedPass
        };
    }

    private enum Look
    {
        Shaded,
        Flat,
        Mask,
        Outline
    }

    private readonly record struct Part(MeshData Data, Matrix4x4F World, Vector4F Color, bool Flat, bool Line, Skin Skin);

    private readonly record struct Ribbon(Vector3[] Points, int[] Indices, Mesh Mesh);
}
