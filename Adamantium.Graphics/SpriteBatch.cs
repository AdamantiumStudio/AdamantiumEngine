using System;
using Adamantium.Core;
using Adamantium.FX;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics;

/// <summary>Sprites drawn in batches: each names its texture by its place in the device's texture array of no length,
/// so a batch of any number of textures is one draw - four vertices a sprite, one instance each. A batch holds as many
/// sprites as it is given, sorted whole; batches drawn in one frame each keep their own sprites, so any number may be.
/// Depth is the layer depth as it is: 0 nearest, 1 deepest.</summary>
public class SpriteBatch : DisposableObject
{
    private const UInt16 MinBatchSize = 1024;
    private const uint VerticesPerSprite = 4;

    private static readonly Vector2F[] UVCornerCoords = [Vector2F.Zero, Vector2F.UnitX, Vector2F.UnitY, Vector2F.One];

    private readonly IGraphicsDevice graphicsDevice;
    private readonly IDescriptorManager descriptors;
    private readonly Buffer<SpriteBatchItem>[] frameBuffers;
    private readonly Type vertexType = typeof(SpriteBatchItem);
    private readonly IEffectPass spriteBatchEffectPass;
    private readonly EffectParameter effectMatrixTransform;
    private readonly EffectParameter effectSampler;
    private readonly EffectParameter effectUVCornerCoords;

    private SpriteBatchItem[] spriteItems;
    private (float Depth, int Order)[] sortKeys;
    private Texture[] textures;
    private ulong frameTicket = ulong.MaxValue;
    private uint usedThisFrame;
    private UInt32 spriteItemsCount;
    private SpriteSortMode spriteSortMode;
    private SamplerState assignedSamplerState;
    private Effect customEffect;
    private EffectParameter customEffectMatrixTransform;
    private EffectParameter customEffectTextureSampler;
    private EffectParameter customTextureCornerCoords;
    private Matrix4x4F transformMatrix;
    private Boolean beginCalled;
    private Vector4F? nullRect;
    private Vector2F vector2Zero = Vector2F.Zero;
    private Vector2F currentScreenSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpriteBatch"/> class
    /// </summary>
    /// <param name="graphicsDevice">The graphics device</param>
    /// <param name="initialBatchSize">How many sprites a batch holds before it grows; 4096 by default, at least 1024.
    /// </param>
    public SpriteBatch(IGraphicsDevice graphicsDevice, UInt32 initialBatchSize = 4096)
    {
        this.graphicsDevice = graphicsDevice;
        descriptors = ((GraphicsDevice)graphicsDevice).DescriptorManager;
        var capacity = (int)Math.Max(initialBatchSize, MinBatchSize);
        spriteItems = new SpriteBatchItem[capacity];
        sortKeys = new (float, int)[capacity];
        textures = new Texture[capacity];
        frameBuffers = new Buffer<SpriteBatchItem>[Math.Max(1, graphicsDevice.MaxFramesInFlight)];

        var effect = ToDispose(new SpriteEffect(graphicsDevice));
        effect.CurrentTechnique = effect.Techniques[0];
        spriteBatchEffectPass = effect.SpriteBatchRenderPass;
        effectSampler = effect.TextureSampler;
        effectMatrixTransform = effect.MatrixTransform;
        effectUVCornerCoords = effect.TextureCornerCoords;
    }

    /// <summary>Begins a batch drawn to a target of <paramref name="screenSize"/>, sorted by
    /// <paramref name="sortMode"/>, sampled anisotropically and repeated, with <paramref name="effect"/> or the
    /// sprite effect for null. A custom effect draws as the sprite effect does: its pass "Render" takes
    /// <c>MatrixTransform</c>, <c>TextureSampler</c> and <c>TextureCornerCoords</c>, and reads its textures from an
    /// array of no length by the index each sprite carries.</summary>
    public void Begin(Vector2 screenSize, SpriteSortMode sortMode = SpriteSortMode.NoSort, Effect effect = null)
    {
        SaveState(screenSize, sortMode, null, effect, Matrix4x4F.Identity);
    }

    /// <summary>Begins a batch as <see cref="Begin(Vector2, SpriteSortMode, Effect)"/> does, sampled with
    /// <paramref name="samplerState"/> - anisotropic and repeated for null.</summary>
    public void Begin(
        Vector2 screenSize,
        SpriteSortMode sortMode,
        SamplerState samplerState,
        Effect effect = null)
    {
        SaveState(screenSize, sortMode, samplerState, effect, Matrix4x4F.Identity);
    }

    /// <summary>Begins a batch as <see cref="Begin(Vector2, SpriteSortMode, SamplerState, Effect)"/> does, every
    /// sprite moved by <paramref name="transformationMatrix"/> before it is drawn.</summary>
    public void Begin(
        Vector2 screenSize,
        SpriteSortMode sortMode,
        SamplerState samplerState,
        Effect effect,
        Matrix4x4F transformationMatrix)
    {
        SaveState(screenSize, sortMode, samplerState, effect, transformationMatrix);
    }

    /// <summary>Draws what the batch holds, in one draw, and ends it.</summary>
    /// <exception cref="InvalidOperationException">No batch was begun.</exception>
    public void End()
    {
        if (!beginCalled)
        {
            throw new InvalidOperationException("Begin must be called before end");
        }

        if (spriteItemsCount > 0)
        {
            FlushBatch();
        }

        customEffect = null;
        customTextureCornerCoords = null;
        customEffectTextureSampler = null;
        customEffectMatrixTransform = null;
        beginCalled = false;
    }

    /// <summary>
    /// Adds sprite to batch with certain position and color
    /// </summary>
    /// <param name="texture">The texture</param>
    /// <param name="position">Sprite position in 2D space</param>
    /// <param name="color">Color to apply to texture</param>
    public void Draw(Texture texture, Vector2F position, Color color)
    {
        var destination = new Vector4F(position.X, position.Y, 1f, 1f);
        DrawSprite(texture, ref destination, true, ref nullRect, color, 0, ref vector2Zero);
    }

    /// <summary>
    /// Adds sprite to batch with certain destination and color
    /// </summary>
    /// <param name="texture">The texture</param>
    /// <param name="destination">A rectangle that specifies (in screen coordinates) the destination for drawing the sprite. If this rectangle is not the same size as the source rectangle, the sprite will be scaled to fit.</param>
    /// <param name="color">Color to apply to texture</param>
    public void Draw(Texture texture, RectangleF destination, Color color)
    {
        Vector4F dst = RectangleToVector4(destination);
        DrawSprite(texture, ref dst, false, ref nullRect, color, 0f, ref vector2Zero);
    }

    /// <summary>
    /// Adds sprite to batch with certain position, source rectangle and color
    /// </summary>
    /// <param name="texture">The texture</param>
    /// <param name="position">Sprite position in 2D space</param>
    /// <param name="sourceRectangle">A rectangle that specifies (in texels) the source texels from a texture. Use null to draw the entire texture.</param>
    /// <param name="color">Color to apply to texture</param>
    public void Draw(Texture texture, Vector2F position, RectangleF? sourceRectangle, Color color)
    {
        var destination = new Vector4F(position.X, position.Y, 1f, 1f);
        var source = RectangleToVector4(sourceRectangle);
        DrawSprite(texture, ref destination, true, ref source, color, 0, ref vector2Zero);
    }

    /// <summary>
    /// Adds sprite to batch with certain position color and scale
    /// </summary>
    /// <param name="texture">The texture</param>
    /// <param name="position">Sprite position in 2D space</param>
    /// <param name="color">Color to apply to texture</param>
    /// <param name="scale">Scale factor</param>
    public void Draw(Texture texture, Vector2F position, Color color, Vector2F scale)
    {
        var destination = new Vector4F(position.X, position.Y, scale.X, scale.Y);
        DrawSprite(texture, ref destination, true, ref nullRect, color, 0f, ref vector2Zero);
    }

    /// <summary>
    /// Adds sprite to batch with certain destination, source and color
    /// </summary>
    /// <param name="texture">The texture</param>
    /// <param name="destination">A rectangle that specifies (in screen coordinates) the destination for drawing the sprite. If this rectangle is not the same size as the source rectangle, the sprite will be scaled to fit.</param>
    /// <param name="sourceRectangle">A rectangle that specifies (in texels) the source texels from a texture. Use null to draw the entire texture.</param>
    /// <param name="color">Color to apply to texture</param>
    public void Draw(Texture texture, RectangleF destination, RectangleF? sourceRectangle, Color color)
    {
        Vector4F dst = RectangleToVector4(destination);
        var source = RectangleToVector4(sourceRectangle);
        DrawSprite(texture, ref dst, false, ref source, color, 0f, ref vector2Zero);
    }

    /// <summary>
    /// Adds sprite to batch with certain destination, source rectangle, color, rotation, rotation origin, sprite effects and layer depth
    /// </summary>
    /// <param name="texture">The texture</param>
    /// <param name="destination">A rectangle that specifies (in screen coordinates) the destination for drawing the sprite. If this rectangle is not the same size as the source rectangle, the sprite will be scaled to fit.</param>
    /// <param name="sourceRectangle">A rectangle that specifies (in texels) the source texels from a texture. Use null to draw the entire texture.</param>
    /// <param name="color">Color to apply to texture</param>
    /// <param name="rotation">Rotation angle</param>
    /// <param name="origin">Origin for rotation</param>
    /// <param name="effects"><see cref="SpriteEffects"/> to apply to texture</param>
    /// <param name="layerDepth">Depth of the sprite</param>
    public void Draw(
        Texture texture,
        RectangleF destination,
        RectangleF? sourceRectangle,
        Color color,
        Single rotation,
        Vector2F origin,
        SpriteEffects effects = SpriteEffects.None,
        Single layerDepth = 0.0f)
    {
        var dst = RectangleToVector4(destination);
        var source = RectangleToVector4(sourceRectangle);
        DrawSprite(texture, ref dst, false, ref source, color, rotation, ref origin, effects, layerDepth);
    }

    /// <summary>
    /// Adds sprite to batch with certain position, source rectangle, color, rotation, rotation origin, scale, sprite effects and layer depth
    /// </summary>
    /// <param name="texture">The texture</param>
    /// <param name="position">Position of the sprite in 2D space</param>
    /// <param name="sourceRectangle">A rectangle that specifies (in texels) the source texels from a texture. Use null to draw the entire texture.</param>
    /// <param name="color">Color to apply to texture</param>
    /// <param name="rotation">Rotation angle</param>
    /// <param name="origin">Origin for rotation</param>
    /// <param name="scale">Scale factor</param>
    /// <param name="effects"><see cref="SpriteEffects"/> to apply to texture</param>
    /// <param name="layerDepth">Depth of the sprite</param>
    public void Draw(
        Texture texture,
        Vector2F position,
        RectangleF? sourceRectangle,
        Color color,
        Single rotation,
        Vector2F origin,
        Single scale,
        SpriteEffects effects = SpriteEffects.None,
        Single layerDepth = 0.0f)
    {
        var destination = new Vector4F(position.X, position.Y, scale, scale);
        var sourceRect = RectangleToVector4(sourceRectangle);
        DrawSprite(texture, ref destination, true, ref sourceRect, color, rotation, ref origin, effects, layerDepth);
    }

    /// <summary>
    /// Adds sprite to batch with certain position, source rectangle, color, rotation, rotation origin, scale, sprite effects and layer depth
    /// </summary>
    /// <param name="destination">A rectangle that specifies the size of the sprite</param>
    /// <param name="color">Color to apply to texture</param>
    /// <param name="rotation">Rotation angle</param>
    /// <param name="origin">The sprite origin; the default is (0,0) which represents the upper-left corner</param>
    /// <param name="effects"><see cref="SpriteEffects"/> to apply to texture</param>
    /// <param name="layerDepth">Depth of the sprite</param>
    public void Draw(
        RectangleF destination,
        Color color,
        Single rotation,
        Vector2F origin,
        SpriteEffects effects = SpriteEffects.None,
        Single layerDepth = 0.0f)
    {
        var dst = RectangleToVector4(destination);
        Vector4F? src = null;
        DrawSprite(null, ref dst, false, ref src, color, rotation, ref origin, effects, layerDepth);
    }

    /// <summary>
    /// Adds sprite to batch with certain position, source rectangle, color, rotation, rotation origin, scale, sprite effects and layer depth
    /// </summary>
    /// <param name="texture">The texture</param>
    /// <param name="position">Position of the sprite in 2D space</param>
    /// <param name="sourceRectangle">A rectangle that specifies (in texels) the source texels from a texture. Use null to draw the entire texture.</param>
    /// <param name="color">Color to apply to texture</param>
    /// <param name="rotation">Rotation angle</param>
    /// <param name="origin">Origin for rotation</param>
    /// <param name="scale">Scale factor</param>
    /// <param name="effects"><see cref="SpriteEffects"/> to apply to texture</param>
    /// <param name="layerDepth">Depth of the sprite</param>
    public void Draw(
        Texture texture,
        Vector2F position,
        RectangleF? sourceRectangle,
        Color color,
        Single rotation,
        Vector2F origin,
        Vector2F scale,
        SpriteEffects effects = SpriteEffects.None,
        Single layerDepth = 0.0f)
    {
        var destination = new Vector4F(position.X, position.Y, scale.X, scale.Y);
        var sourceRect = RectangleToVector4(sourceRectangle);
        DrawSprite(texture, ref destination, true, ref sourceRect, color, rotation, ref origin, effects, layerDepth);
    }

    internal unsafe void DrawSprite(
        Texture texture,
        ref Vector4F destination,
        bool scaleDestination,
        ref Vector4F? sourceRectangle,
        Color color,
        Single rotation,
        ref Vector2F origin,
        SpriteEffects spriteEffects = SpriteEffects.None,
        Single layerDepth = 0.0f)
    {
        if (spriteItemsCount == spriteItems.Length)
        {
            Grow();
        }

        var textured = texture != null;
        var width = textured ? texture.Width : (uint)destination.Z;
        var height = textured ? texture.Height : (uint)destination.W;
        textures[spriteItemsCount] = texture;

        fixed (SpriteBatchItem* item = &spriteItems[spriteItemsCount])
        {
            item->Color = color;
            item->TextureInfo = new Int4((int)width, (int)height, textured ? 0 : -1, 0);
            item->Depth = layerDepth;

            if (sourceRectangle.HasValue)
            {
                item->Source = sourceRectangle.Value;
                width = (uint)item->Source.Z;
                height = (uint)item->Source.W;
            }
            else
            {
                item->Source = new Vector4F(0, 0, width, height);
            }

            if (scaleDestination)
            {
                destination.Z *= width;
                destination.W *= height;
            }

            item->ArrangeRect = destination;
            item->Origin = origin;
            item->SpriteEffects = (int)spriteEffects;
            item->Rotation = rotation;
        }

        spriteItemsCount++;
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        if (disposeManagedResources)
        {
            foreach (var buffer in frameBuffers)
            {
                if (buffer != null)
                {
                    graphicsDevice.AddToDeferDisposeQueue(buffer);
                }
            }
        }

        base.Dispose(disposeManagedResources);
    }

    private void Grow()
    {
        var capacity = spriteItems.Length * 2;
        Array.Resize(ref spriteItems, capacity);
        Array.Resize(ref sortKeys, capacity);
        Array.Resize(ref textures, capacity);
    }

    private void SaveState(
        Vector2 screenSize,
        SpriteSortMode sortMode,
        SamplerState samplerState,
        Effect effect,
        Matrix4x4F transformationMatrix)
    {
        if (beginCalled)
        {
            throw new InvalidOperationException("You need to call End() before you can call Begin() again");
        }

        currentScreenSize = screenSize;
        spriteSortMode = sortMode;
        transformMatrix = transformationMatrix;
        assignedSamplerState = samplerState ?? SamplerStates.AnisotropicRepeat;
        customEffect = effect;
        if (customEffect != null)
        {
            customTextureCornerCoords = customEffect.Parameters["TextureCornerCoords"];
            customEffectMatrixTransform = customEffect.Parameters["MatrixTransform"];
            customEffectTextureSampler = customEffect.Parameters["TextureSampler"];
        }

        beginCalled = true;
    }

    private static Vector4F RectangleToVector4(RectangleF rectangle) =>
        new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

    private static Vector4F? RectangleToVector4(RectangleF? rectangle) =>
        rectangle is { } value ? RectangleToVector4(value) : null;

    private void SortSprites()
    {
        if (spriteSortMode == SpriteSortMode.NoSort)
        {
            return;
        }

        var count = (int)spriteItemsCount;
        var backFirst = spriteSortMode == SpriteSortMode.BackToFront;
        for (var i = 0; i < count; i++)
        {
            sortKeys[i] = (backFirst ? -spriteItems[i].Depth : spriteItems[i].Depth, i);
        }

        Array.Sort(sortKeys, spriteItems, 0, count);
    }

    private void SetTextureSlots()
    {
        for (var i = 0; i < spriteItemsCount; i++)
        {
            if (textures[i] is { } texture)
            {
                spriteItems[i].TextureInfo.W = (int)(texture.IsDisposed
                    ? descriptors.FallbackTextureIndex
                    : descriptors.TextureIndex(texture));
                textures[i] = null;
            }
        }
    }

    private (Buffer<SpriteBatchItem> Buffer, uint First) RoomThisFrame(uint count)
    {
        var ticket = graphicsDevice.FrameTicket;
        if (ticket != frameTicket)
        {
            frameTicket = ticket;
            usedThisFrame = 0;
        }

        var slot = (int)(graphicsDevice.CurrentFrame % (uint)frameBuffers.Length);
        var buffer = frameBuffers[slot];
        var needed = usedThisFrame + count;
        if (buffer == null || buffer.ElementCount < needed)
        {
            if (buffer != null)
            {
                graphicsDevice.AddToDeferDisposeQueue(buffer);
            }

            var size = Math.Max(needed, buffer == null ? (uint)spriteItems.Length : (uint)buffer.ElementCount * 2);
            buffer = frameBuffers[slot] = Buffer.Vertex.New<SpriteBatchItem>(graphicsDevice, size,
                BufferMemoryUsage.UploadFromCpuToGpu);
        }

        var first = usedThisFrame;
        usedThisFrame = needed;
        return (buffer, first);
    }

    private void FlushBatch()
    {
        SetTextureSlots();
        SortSprites();
        var transform = transformMatrix *
                        Matrix4x4F.OrthoOffCenter(0, currentScreenSize.X, 0, currentScreenSize.Y, 0.0f, 1.0f);
        var (vertexBuffer, first) = RoomThisFrame(spriteItemsCount);
        vertexBuffer.SetData(spriteItems, 0, spriteItemsCount, first * (uint)Utilities.SizeOf<SpriteBatchItem>());

        IEffectPass pass;
        if (customEffect != null)
        {
            pass = customEffect.CurrentTechnique.Passes["Render"];
            customEffectTextureSampler.SetResource(assignedSamplerState);
            customEffectMatrixTransform.SetValue(transform);
            customTextureCornerCoords.SetValue(UVCornerCoords);
        }
        else
        {
            pass = spriteBatchEffectPass;
            effectSampler.SetResource(assignedSamplerState);
            effectMatrixTransform.SetValue(transform);
            effectUVCornerCoords.SetValue(UVCornerCoords);
        }

        graphicsDevice.VertexType = vertexType;
        graphicsDevice.SetVertexBuffer(vertexBuffer);
        pass.Apply();
        graphicsDevice.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
        graphicsDevice.Draw(VerticesPerSprite, spriteItemsCount, 0, first);
        pass.UnApply(true);
        spriteItemsCount = 0;
    }
}
