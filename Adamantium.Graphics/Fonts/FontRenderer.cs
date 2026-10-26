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
    private Type vertexType = typeof(FontItem);

    private const float FontSizeThreshold = 14;
    // Glyph weight: a signed-distance contour bias (see FontEffect.fx). 0 = exact outline, > 0 = thicker
    // stems, < 0 = thinner. Normalized units (the field spans PixelRange texels), useful range ~[-0.15, 0.15].
    // Applied inside the screenPxRange term, so it scales with size and does not haze the background.
    private float FontWeight = 0.0f;

    // The text batch: aggregate MANY visible text blocks into ONE
    // DrawBatch - the actual FPS win (collapse ~N per-block draws to ~1). When off, batchable text falls back to the
    // per-block DrawLayoutDirect; the aggregator/fallback live in RenderCache.
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
    private EffectParameter effectLightInkGamma;
    private EffectParameter effectSmallDarkInkGamma;
    private EffectParameter effectLargeDarkInkGamma;
    private EffectParameter effectLargeTextFrom;
    private EffectParameter effectLargeTextAt;
    private EffectParameter effectEdgeWidth;
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

    private static readonly Vector2F[] UVCornerCoords = [Vector2F.Zero, Vector2F.UnitX, Vector2F.UnitY, Vector2F.One];

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
        effectLightInkGamma = fontEffect.LightInkGamma;
        effectSmallDarkInkGamma = fontEffect.SmallDarkInkGamma;
        effectLargeDarkInkGamma = fontEffect.LargeDarkInkGamma;
        effectLargeTextFrom = fontEffect.LargeTextFrom;
        effectLargeTextAt = fontEffect.LargeTextAt;
        effectEdgeWidth = fontEffect.EdgeWidth;
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

    private void SetEffects(ulong effects, int effectHead)
    {
        effectTransforms.SetValue(effects);
        effectEffectHead.SetValue(effects == 0 ? -1f : effectHead);
    }

    /// <summary>Draws one block's glyphs straight into the current pass with a caller-built MVP; the pass's clip, MSAA and
    /// depth state stand. <paramref name="effects"/> is the table its <see cref="GlyphEffect"/>s are in, as
    /// <see cref="GlyphEffect.ToSlot"/> writes them, from the slot <paramref name="effectHead"/> (-1 for none).</summary>
    public void DrawLayoutDirect(SamplerState samplerState, Buffer<FontItem> glyphs, uint count, FontAtlas atlas, float fontSize, Color foreground, Matrix4x4F mvp, float opacity,
        Vector4F clipBox = default, Vector4F clipRadii = default, ulong effects = 0, int effectHead = -1)
    {
        if (count == 0) return;

        GraphicsDevice.ColorBlendEnabled = true;
        // Font shaders output premultiplied color (rgb * alpha) -> premultiplied blend.
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
        SetEdges();
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
        SetEdges();
        effectGlyphInstances.SetValue(instancesAddress);
        effectTransforms.SetValue(transformsAddress);
        SetTargetOrigin();
        SetPaints(atlas);
        GraphicsDevice.VertexType = null;
        GraphicsDevice.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
        fontEffect.FontBatchRenderMsdfBatchInstancedPass.Apply();
        GraphicsDevice.Draw(4, glyphCount, 0, 0);   // 4 strip verts x glyphCount instances; address already at this segment
    }

    private void SetEdges()
    {
        var edges = GraphicsDevice.MainDevice.Settings.TextEdges;
        effectLightInkGamma.SetValue(edges.LightInkGamma);
        effectSmallDarkInkGamma.SetValue(edges.SmallDarkInkGamma);
        effectLargeDarkInkGamma.SetValue(edges.LargeDarkInkGamma);
        effectLargeTextFrom.SetValue(edges.LargeTextFrom);
        effectLargeTextAt.SetValue(edges.LargeTextAt);
        effectEdgeWidth.SetValue(edges.EdgeWidth);
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
}
