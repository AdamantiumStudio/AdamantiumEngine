using System;
using Adamantium.FX;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Fonts;

public class FontRenderer : GraphicsResource
{
    private FontEffect fontEffect;
    private bool beginCalled;
    private Matrix4x4F transformMatrix;
    private Type vertexType = typeof(FontItem);

    private const float FontSizeThreshold = 14;
    // Glyph weight: a signed-distance contour bias (see FontEffect.fx). 0 = exact outline, > 0 = thicker
    // stems, < 0 = thinner. Normalized units (the field spans PixelRange texels), useful range ~[-0.15, 0.15].
    // Applied inside the screenPxRange term, so it scales with size and does not haze the background.
    private float FontWeight = 0.0f;

    // Supersampling factor for text. The text render target is RenderScale x bigger than the logical text
    // area, so the ortho is divided by it (the viewport stays full size) -> glyphs rasterize RenderScale x
    // larger and get downsampled when the texture is composited onto the control = SSAA. 1 = off.
    public float RenderScale { get; set; } = 1f;

    // The LIVE text path: text draws straight into the main pass via a single CPU-built MVP
    // (Translation(TextArea) x World x Projection). Off = the old private-RT rasterize + composite fallback (A/B).
    public static bool UseDirectTextDraw = true;

    // The text batch: aggregate MANY visible text blocks into ONE
    // DrawBatch - the actual FPS win (collapse ~N per-block draws to ~1). When off, batchable text falls back to the
    // per-block DrawLayoutDirect. Requires UseDirectTextDraw too; the aggregator/fallback live in RenderCache.
    public static bool UseTextBatch = true;

    // The glyph pixel shader used to be selectable - canonical MSDF against a gradient-derivative variant, plus an
    // outline pass that existed to PROVE the widened distance field carried data beyond the contour. Both switches were
    // fixed at their defaults everywhere and nothing ever flipped them, so the two passes they chose were dead code in
    // an effect whose shader compiler is unusually easy to upset; they and their parameters are gone.

    // True-SDF blend band, in atlas texels per screen pixel. Below Lo the glyph is magnified -> MSDF median
    // (sharp corners); above Hi it is minified -> single-channel true SDF (crisp small text where the median
    // would soften); blended between. Defaults are the physical 1:1 .. 2:1 minification points. Set Lo >= Hi
    // to disable the blend (stay pure MSDF) without touching the shader.
    public float SdfBlendLo { get; set; } = 1.0f;
    public float SdfBlendHi { get; set; } = 2.0f;

    private EffectParameter effectSampler;
    private EffectParameter effectTexture;
    private EffectParameter effectMatrixTransform;
    private EffectParameter effectUVCornerCoords;
    private EffectParameter effectForegroundColor;
    private EffectParameter effectFontSize;
    private EffectParameter effectFontSizeThreshold;
    private EffectParameter effectFontWeight;
    private EffectParameter effectPixelRange;
    private EffectParameter effectAtlasSize;
    private EffectParameter effectSdfBlendLo;
    private EffectParameter effectSdfBlendHi;
    private EffectParameter effectGlyphFade;
    private EffectParameter effectDirectClipBox;
    private EffectParameter effectDirectClipRadii;
    private EffectParameter effectTargetOrigin;
    private EffectParameter effectGlyphInstances;   // BDA address of the per-instance GlyphItem storage buffer (instanced batch)
    private EffectParameter effectTransforms;       // BDA address of the transform table the glyph VS indexes by slot
    private EffectParameter effectEffectHead;
    private EffectParameter effectPaintPrograms;
    private EffectParameter effectPaintStops;
    private EffectParameter effectColorTexture;

    private Vector2F currentScreenSize;
    private static readonly Vector2F[] UVCornerCoords = [Vector2F.Zero, Vector2F.UnitX, Vector2F.UnitY, Vector2F.One];
    private Matrix4x4F finalMatrix;
    private SamplerState assignedSamplerState;
    private IRenderTarget renderTarget;

    private Color _oldClearColor;
    private IRenderTarget _oldRenderTargets;
    private IDepthStencilBuffer _oldDepthStencilBuffer;
    private Rect2D[] _savedScissors;
    private Viewport[] _savedViewports;

    public FontRenderer(IGraphicsDevice device) : base(device)
    {
        fontEffect = new FontEffect(device);

        effectSampler = fontEffect.TextureSampler;
        effectTexture = fontEffect.Texture;
        effectMatrixTransform = fontEffect.MatrixTransform;
        effectUVCornerCoords = fontEffect.TextureCornerCoords;
        effectForegroundColor = fontEffect.ForegroundColor;
        effectFontSize = fontEffect.FontSize;
        effectFontSizeThreshold = fontEffect.FontSizeThreshold;
        effectFontWeight = fontEffect.FontWeight;
        effectPixelRange = fontEffect.PxRange;
        effectAtlasSize = fontEffect.MSDFAtlasSize;
        effectSdfBlendLo = fontEffect.SdfBlendLo;
        effectSdfBlendHi = fontEffect.SdfBlendHi;
        effectGlyphFade = fontEffect.GlyphFade;
        effectDirectClipBox = fontEffect.DirectClipBox;
        effectDirectClipRadii = fontEffect.DirectClipRadii;
        effectTargetOrigin = fontEffect.TargetOrigin;
        effectGlyphInstances = fontEffect.GlyphInstancesAddress;
        effectTransforms = fontEffect.TransformsAddress;
        effectEffectHead = fontEffect.EffectHead;
        effectPaintPrograms = fontEffect.PaintProgramsAddress;
        effectPaintStops = fontEffect.PaintStopsAddress;
        effectColorTexture = fontEffect.ColorTexture;
    }

    /// <summary>Draws one block's glyphs into the target <see cref="SetState"/> set; <paramref name="effects"/> is the table
    /// its <see cref="GlyphEffect"/>s are in, as <see cref="GlyphEffect.ToSlot"/> writes them, from the slot
    /// <paramref name="effectHead"/> (-1 for none).</summary>
    public void DrawLayout(Buffer<FontItem> glyphs, uint count, FontAtlas atlas, float fontSize, Color foreground,
        ulong effects = 0, int effectHead = -1)
    {
        DrawInternal(glyphs, count, atlas, fontSize, foreground, effects, effectHead);
    }

    private void SetEffects(ulong effects, int effectHead)
    {
        effectTransforms.SetValue(effects);
        effectEffectHead.SetValue(effects == 0 ? -1f : effectHead);
    }

    public void SetState(
        SamplerState samplerState,
        Vector3F translation,
        IRenderTarget renderTarget,
        bool outerPassActive = true)
    {
        if (beginCalled)
        {
            throw new Exception("You need to call RestoreState() before you can call Begin() again");
        }

        assignedSamplerState = samplerState;
        this.renderTarget = renderTarget;
        currentScreenSize = new Vector2F(renderTarget.Width, renderTarget.Height);
        transformMatrix = Matrix4x4F.Translation(translation);

        // Copies of the interrupted pass's clip for RestoreState: the device refills these arrays in place, so a reference
        // would restore whatever the clip is by then.
        _savedScissors = (Rect2D[])((GraphicsDevice)GraphicsDevice).CurrentScissors.Clone();
        _savedViewports = (Viewport[])((GraphicsDevice)GraphicsDevice).CurrentViewports.Clone();

        // Inline (called mid main pass): end it so we can render into the text target. Pre-pass (called from PreRender,
        // before the main pass begins): there's no outer pass to end - skip it.
        if (outerPassActive) GraphicsDevice.EndDraw();

        _oldClearColor = GraphicsDevice.ClearColor;
        _oldRenderTargets = GraphicsDevice.CurrentRenderTarget;
        _oldDepthStencilBuffer = GraphicsDevice.CurrentDepthStencilBuffer;

        GraphicsDevice.ClearColor = Colors.Transparent;
        GraphicsDevice.SetRenderTargets(renderTarget);
        GraphicsDevice.SetDepthBuffer(null);
        var device = (GraphicsDevice)GraphicsDevice;
        device.TransitionImagesForRendering(device.CurrentCommandBuffer, device.CurrentRenderTarget, device.CurrentRenderTarget.ResolveTexture);
        GraphicsDevice.BeginRendering(GraphicsDevice.CurrentCommandBuffer);

        beginCalled = true;
    }

    private void DrawInternal(Buffer<FontItem> glyphs, uint count, FontAtlas atlas, float fontSize, Color foreground,
        ulong effects, int effectHead)
    {
        if (count == 0) return;

        var vp = new Viewport();
        vp.Width = currentScreenSize.X;
        vp.Height = currentScreenSize.Y;
        vp.MinDepth = 0;
        vp.MaxDepth = 1;

        var scissor = new Rect2D();
        scissor.Offset = new Offset2D();
        scissor.Extent = new Extent2D();
        scissor.Extent.Width = (uint)currentScreenSize.X;
        scissor.Extent.Height = (uint)currentScreenSize.Y;

        // Divide the ortho extent by RenderScale while the viewport stays the full (supersampled) target:
        // the logical-size layout then maps across the whole target and rasterizes RenderScale x larger.
        // Without this division RenderScale did nothing - the text drew at 1x in a corner of the 2x target.
        var orthoProjection = Matrix4x4F.OrthoOffCenter(0, currentScreenSize.X / RenderScale, 0, currentScreenSize.Y / RenderScale, 0f, 100000f);
        finalMatrix = transformMatrix * orthoProjection;
        GraphicsDevice.SetViewports(vp);
        GraphicsDevice.SetScissors(scissor);
        GraphicsDevice.ColorBlendEnabled = true;
        // The font pixel shaders output premultiplied color (rgb * alpha), so the target must use a
        // premultiplied blend. The old straight-alpha "Fonts" blend multiplied by alpha a second time,
        // darkening the anti-aliased edges into a rim around every glyph.
        GraphicsDevice.ColorBlendEquation = ColorBlendEquations.Premultiplied;
        GraphicsDevice.PrimitiveRestartEnable = true;
        GraphicsDevice.MSAALevel = renderTarget.MSAALevel;
        GraphicsDevice.DepthTestEnabled = false;
        GraphicsDevice.DepthWriteEnable = false;

        effectSampler.SetResource(assignedSamplerState);
        effectTexture.SetResource(atlas.Atlas);
        effectMatrixTransform.SetValue(finalMatrix);
        effectUVCornerCoords.SetValue(UVCornerCoords);
        effectForegroundColor.SetValue(foreground.ToVector4());
        effectGlyphFade.SetValue(1f);
        effectFontSize.SetValue(fontSize);
        effectFontSizeThreshold.SetValue(FontSizeThreshold);
        effectFontWeight.SetValue(FontWeight);
        effectPixelRange.SetValue(atlas.PixelRange);
        effectAtlasSize.SetValue(new Vector2F(atlas.Atlas.Width, atlas.Atlas.Height));
        effectSdfBlendLo.SetValue(SdfBlendLo);
        effectSdfBlendHi.SetValue(SdfBlendHi);
        // NO clip on this path, and it has to be SAID: the glyphs go into a private target, so their SV_Position is
        // target-local while the clip box is in screen device pixels - the two do not describe the same space. Left
        // unset, the pass would keep whatever the last per-block draw put there and cut the target by a stranger's rect.
        effectDirectClipBox.SetValue(Vector4F.Zero);
        effectDirectClipRadii.SetValue(Vector4F.Zero);
        SetEffects(effects, effectHead);
        SetPaints(atlas);
        GraphicsDevice.VertexType = vertexType;
        GraphicsDevice.SetVertexBuffer(glyphs);   // the component's own buffer, uploaded from the FROZEN glyph run (no live layout)
        // Instanced quad: 4-vertex triangle strip per glyph (corners from SV_VertexID), one instance per glyph.
        GraphicsDevice.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
        fontEffect.FontBatchRenderMsdfPass.Apply();

        GraphicsDevice.Draw(4, count);   // 4 strip verts x glyph instances (composite path)
    }

    // Draws one block's glyphs straight into the current main pass with a caller-built MVP, instead of a private target
    // and a composite; the main pass's clip, MSAA and depth state stand. The effects as DrawLayout takes them.
    public void DrawLayoutDirect(SamplerState samplerState, Buffer<FontItem> glyphs, uint count, FontAtlas atlas, float fontSize, Color foreground, Matrix4x4F mvp, float opacity,
        Vector4F clipBox = default, Vector4F clipRadii = default, ulong effects = 0, int effectHead = -1)
    {
        if (count == 0) return;

        GraphicsDevice.ColorBlendEnabled = true;
        // Font shaders output premultiplied color (rgb * alpha) -> premultiplied blend (same as the RT path + composite).
        GraphicsDevice.ColorBlendEquation = ColorBlendEquations.Premultiplied;
        GraphicsDevice.PrimitiveRestartEnable = true;
        GraphicsDevice.DepthTestEnabled = true;
        GraphicsDevice.DepthWriteEnable = true;
        GraphicsDevice.DepthCompareFunction = CompareOp.Always;

        var fg = foreground.ToVector4();
        // The fade goes in raised to 2.2 so the pixel shader's gamma boost hands it back linear; the color's own alpha stays
        // under the boost.
        fg.W *= MathF.Pow(opacity, 2.2f);

        effectSampler.SetResource(samplerState);
        effectTexture.SetResource(atlas.Atlas);
        effectMatrixTransform.SetValue(mvp);
        effectUVCornerCoords.SetValue(UVCornerCoords);
        effectForegroundColor.SetValue(fg);
        effectGlyphFade.SetValue(MathF.Pow(opacity, 2.2f));
        effectFontSize.SetValue(fontSize);
        effectFontSizeThreshold.SetValue(FontSizeThreshold);
        effectFontWeight.SetValue(FontWeight);
        effectPixelRange.SetValue(atlas.PixelRange);
        effectAtlasSize.SetValue(new Vector2F(atlas.Atlas.Width, atlas.Atlas.Height));
        effectSdfBlendLo.SetValue(SdfBlendLo);
        effectSdfBlendHi.SetValue(SdfBlendHi);
        effectDirectClipBox.SetValue(clipBox);       // zero size = no rounded clip
        effectDirectClipRadii.SetValue(clipRadii);
        SetEffects(effects, effectHead);
        SetTargetOrigin();
        SetPaints(atlas);
        GraphicsDevice.VertexType = vertexType;
        GraphicsDevice.SetVertexBuffer(glyphs);   // the component's own buffer, uploaded from the FROZEN glyph run (no live layout)
        GraphicsDevice.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
        fontEffect.FontBatchRenderMsdfPass.Apply();   // direct text is never stroked - both text paths pass no stroke

        GraphicsDevice.Draw(4, count);   // 4 strip verts x glyph instances
    }

    // Aggregated text batch: glyphs of many blocks sharing one atlas in one instanced draw, node-local rects moved to world
    // on the GPU by the transform table. instancesAddress is pre-offset to this segment's slice.
    public void DrawBatch(SamplerState samplerState, FontAtlas atlas, ulong instancesAddress, ulong transformsAddress,
        uint glyphCount, Matrix4x4F projection)
    {
        if (glyphCount == 0) return;

        GraphicsDevice.ColorBlendEnabled = true;
        GraphicsDevice.ColorBlendEquation = ColorBlendEquations.Premultiplied;
        GraphicsDevice.PrimitiveRestartEnable = true;
        GraphicsDevice.DepthTestEnabled = true;
        GraphicsDevice.DepthWriteEnable = true;
        GraphicsDevice.DepthCompareFunction = CompareOp.Always;

        effectSampler.SetResource(samplerState);
        effectTexture.SetResource(atlas.Atlas);
        effectMatrixTransform.SetValue(projection);
        effectUVCornerCoords.SetValue(UVCornerCoords);
        effectFontWeight.SetValue(FontWeight);
        effectPixelRange.SetValue(atlas.PixelRange);
        effectAtlasSize.SetValue(new Vector2F(atlas.Atlas.Width, atlas.Atlas.Height));
        effectSdfBlendLo.SetValue(SdfBlendLo);
        effectSdfBlendHi.SetValue(SdfBlendHi);
        effectGlyphInstances.SetValue(instancesAddress);
        effectTransforms.SetValue(transformsAddress);
        SetTargetOrigin();
        SetPaints(atlas);
        GraphicsDevice.VertexType = null;
        GraphicsDevice.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
        fontEffect.FontBatchRenderMsdfBatchInstancedPass.Apply();
        GraphicsDevice.Draw(4, glyphCount, 0, 0);   // 4 strip verts x glyphCount instances; address already at this segment
    }

    private void SetTargetOrigin()
    {
        var origin = GraphicsDevice.TargetOrigin;
        effectTargetOrigin.SetValue(new Vector2F(origin.X, origin.Y));
    }

    private void SetPaints(FontAtlas atlas)
    {
        var (programs, stops) = atlas.UploadPaints();
        effectPaintPrograms.SetValue(programs);
        effectPaintStops.SetValue(stops);
        effectColorTexture.SetResource(atlas.ColorAtlas);
    }

    public void RestoreState(bool outerPassActive = true)
    {
        if (!beginCalled)
        {
            throw new Exception("SetState must be called before end");
        }

        GraphicsDevice.EndDraw();
        var device = (GraphicsDevice)GraphicsDevice;

        device.TransitionImagesAfterRendering(device.CurrentCommandBuffer, device.CurrentRenderTarget.ResolveTexture);

        GraphicsDevice.ClearColor = _oldClearColor;
        GraphicsDevice.MSAALevel = _oldRenderTargets.MSAALevel;

        // Restore the viewport + scissor that were active when SetState interrupted the pass - NOT the full attachment.
        // Resetting to full left the resumed pass (and the text composite that immediately follows) drawing unclipped,
        // so a clipped caller (a virtualized list item) had its text poke ~half an item past the viewport edge on a
        // fast scroll. Fall back to the full attachment only if nothing was captured.
        if (_savedViewports is { Length: > 0 })
            GraphicsDevice.SetViewports(_savedViewports);
        else
            GraphicsDevice.SetViewports(new Viewport { Width = _oldRenderTargets.Width, Height = _oldRenderTargets.Height, MaxDepth = 1 });

        if (_savedScissors is { Length: > 0 })
            GraphicsDevice.SetScissors(_savedScissors);
        else
            GraphicsDevice.SetScissors(new Rect2D { Offset = new Offset2D(), Extent = new Extent2D { Width = (uint)_oldRenderTargets.Width, Height = (uint)_oldRenderTargets.Height } });

        GraphicsDevice.SetRenderTargets(_oldRenderTargets);
        GraphicsDevice.SetDepthBuffer(_oldDepthStencilBuffer);
        GraphicsDevice.DepthTestEnabled = true;
        GraphicsDevice.DepthWriteEnable = true;
        GraphicsDevice.DepthCompareFunction = CompareOp.Always;
        GraphicsDevice.ColorBlendEnabled = true;
        GraphicsDevice.ColorBlendEquation = ColorBlendEquations.AlphaBlend;
        // Inline: resume the main pass we interrupted. Pre-pass (standalone): don't - the main pass hasn't begun yet
        // (BeginDraw begins it right after PreRender), and the outer render target is already restored above.
        if (outerPassActive) GraphicsDevice.BeginRendering(GraphicsDevice.CurrentCommandBuffer, true);

        beginCalled = false;
    }
}
