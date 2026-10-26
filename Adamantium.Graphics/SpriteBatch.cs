using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.FX;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics
{
    public partial class SpriteBatch : DisposableObject
    {
        private const UInt32 MaxBatchSize = 1000000;
        private const UInt16 MinBatchSize = 1024;

        // One sprite = a 4-vertex triangle strip (corners from SV_VertexID), drawn instanced - one instance per item.
        private const uint VERTICES_PER_SPRITE = 4;

        private IGraphicsDevice graphicsDevice;

        private Buffer<SpriteBatchItem> vertexBuffer;

        private Type vertexType;

        private static readonly Vector2F[] UVCornerCoords = [Vector2F.Zero, Vector2F.UnitX, Vector2F.UnitY, Vector2F.One];
        private int textureId;

        private readonly Int32 initialBatchSize;

        private UInt32 spriteItemsCount; // current sprite items count between begin and end calls

        private SpriteBatchItem[] spriteItems;
        private Dictionary<IntPtr, int> textureIds;
        private Dictionary<int, Texture> texturesById;

        private TextureComparer textureComparer;
        private BackToFrontComparer backToFrontComparer;
        private FrontToBackComparer frontToBackComparer;

        private SpriteSortMode spriteSortMode;

        private SamplerState assignedSamplerState;

        private readonly Effect spriteBatchEffect;
        private IEffectPass spriteBatchEffectPass;

        private Effect customEffect;

        private Matrix4x4F transformMatrix;
        private Matrix4x4F final;

        private Boolean beginCalled;

        private EffectParameter effectMatrixTransform;
        private EffectParameter efffectTexture;
        private EffectParameter effectSampler;
        private EffectParameter effectUVCornerCoords;

        private EffectParameter customEffectMatrixTransform;
        private EffectParameter customEffectTexture;
        private EffectParameter customEffectTextureSampler;
        private EffectParameter customTextureCornerCoords;

        Vector4F? nullRect;
        Vector2F vector2Zero = Vector2F.Zero;
        Vector2F baseScale = Vector2F.One;

        private Vector2F currentScreenSize;

        /// <summary>
        /// Initializes a new instance of the <see cref="SpriteBatch"/> class
        /// </summary>
        /// <param name="graphicsDevice">The graphics device</param>
        /// <param name="initialBatchSize">The batch capacity default to 4096 (minimum 4096).</param>
        public SpriteBatch(IGraphicsDevice graphicsDevice, UInt32 initialBatchSize = 4096)
        {
            this.graphicsDevice = graphicsDevice;

            if (initialBatchSize > MaxBatchSize)
            {
                this.initialBatchSize = (int)MaxBatchSize;
            }
            else if (initialBatchSize < MinBatchSize)
            {
                this.initialBatchSize = MinBatchSize;
            }
            else
            {
                this.initialBatchSize = (int)initialBatchSize;
            }

            textureComparer = new TextureComparer();
            frontToBackComparer = new FrontToBackComparer();
            backToFrontComparer = new BackToFrontComparer();

            spriteItems = new SpriteBatchItem[this.initialBatchSize];

            textureIds = new Dictionary<IntPtr, int>();
            texturesById = new Dictionary<int, Texture>();

            // Rewritten via SetData every frame → CPU-to-GPU upload (fast BAR window), not device-local.
            vertexBuffer = ToDispose(Buffer.Vertex.New<SpriteBatchItem>(graphicsDevice, (uint)spriteItems.Length,
                BufferMemoryUsage.UploadFromCpuToGpu));
            vertexType = typeof(SpriteBatchItem);

            var effect = new SpriteEffect(graphicsDevice);
            spriteBatchEffect = effect;

            spriteBatchEffectPass = effect.SpriteBatchRenderPass;
            spriteBatchEffect.CurrentTechnique = effect.Techniques[0];
            effectSampler = effect.TextureSampler;
            efffectTexture = effect.Texture;
            effectMatrixTransform = effect.MatrixTransform;
            effectUVCornerCoords = effect.TextureCornerCoords;
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
                throw new Exception("You need to call End() before you can call Begin() again");
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
                customEffectTexture = customEffect.Parameters["Texture"];
            }
            beginCalled = true;
        }

        /// <summary>Begins a batch drawn to a target of <paramref name="screenSize"/>, sorted by
        /// <paramref name="sortMode"/>, sampled anisotropically and repeated, with <paramref name="effect"/> or the
        /// sprite effect for null.</summary>
        public void Begin(Vector2 screenSize, SpriteSortMode sortMode = SpriteSortMode.NoSort, Effect effect = null)
        {
            spriteSortMode = sortMode;
            SaveState(screenSize, sortMode, null, effect, Matrix4x4F.Identity);
        }

        /// <summary>Begins a batch as <see cref="Begin(Vector2, SpriteSortMode, Effect)"/> does, sampled with
        /// <paramref name="samplerState"/> - anisotropic and repeated for null.</summary>
        public void Begin(
            Vector2 screenSize,
            SpriteSortMode sortMode,
            SamplerState samplerState = null,
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

        /// <summary>
        /// Directly perform draw operation and set back all custom states
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void End()
        {
            if (!beginCalled)
            {
                throw new Exception("Begin must be called before end");
            }

            if (spriteItemsCount > 0)
            {
                PrepareForFlushing();
                FlushBatch();
            }

            if (customEffect != null)
            {
                customTextureCornerCoords = null;
                customEffectTexture = null;
                customEffectTextureSampler = null;
                customEffectMatrixTransform = null;
                customEffect = null;
            }

            textureIds.Clear();
            texturesById.Clear();
            textureId = 0;

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
            Vector4F? sourceRect = null;
            if (sourceRectangle.HasValue)
            {
                sourceRect = new Vector4F(sourceRectangle.Value.X, sourceRectangle.Value.Y, sourceRectangle.Value.Width,
                    sourceRectangle.Value.Height);
            }

            DrawSprite(texture, ref destination, true, ref sourceRect, color, rotation, ref origin, effects,
                layerDepth);
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
            Vector4F? sourceRect = null;
            if (sourceRectangle.HasValue)
            {
                sourceRect = new Vector4F(sourceRectangle.Value.X, sourceRectangle.Value.Y, sourceRectangle.Value.Width, sourceRectangle.Value.Height);
            }
            DrawSprite(texture, ref destination, true, ref sourceRect, color, rotation, ref origin, effects,
               layerDepth);
        }

        private Vector4F RectangleToVector4(RectangleF rectangle)
        {
            return new Vector4F(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
        }

        private Vector4F? RectangleToVector4(RectangleF? rectangle)
        {
            Vector4F? vector = null;
            if (rectangle.HasValue)
            {
                vector = new Vector4F(rectangle.Value.X, rectangle.Value.Y, rectangle.Value.Width, rectangle.Value.Height);
            }
            return vector;
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
            int id = -1;
            uint width;
            uint height;
            int currentIndex;
            if (texture != null)
            {
                id = 0;
                width = texture.Width;
                height = texture.Height;
                if (!textureIds.TryGetValue(texture.ManagedPointer, out currentIndex))
                {
                    textureIds.Add(texture.ManagedPointer, textureId);
                    texturesById.Add(textureId, texture);
                    currentIndex = textureId;
                    textureId++;
                }
            }
            else
            {
                currentIndex = -1;
                width = (uint)destination.Z;
                height = (uint)destination.W;
            }

            fixed (SpriteBatchItem* item = &spriteItems[spriteItemsCount])
            {
                item->Color = color;
                item->TextureInfo = new Int4((int)width, (int)height, id, currentIndex);
                item->Depth = 1.0f - layerDepth;

                if (sourceRectangle.HasValue)
                {
                    item->Source = sourceRectangle.Value;
                    width = (uint)item->Source.Z;
                    height = (uint)item->Source.W;
                }
                else
                {
                    item->Source.X = 0f;
                    item->Source.Y = 0f;
                    item->Source.Z = width;
                    item->Source.W = height;
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

            if (spriteItemsCount < spriteItems.Length)
            {
                spriteItemsCount++;
            }

            if (spriteItemsCount != initialBatchSize) return;
            
            PrepareForFlushing();
            FlushBatch();
        }

        private void SortSprites()
        {
            IComparer<SpriteBatchItem> comparer;

            switch (spriteSortMode)
            {
                case SpriteSortMode.Texture:
                    comparer = textureComparer;
                    break;
                case SpriteSortMode.BackToFront:
                    comparer = backToFrontComparer;
                    break;
                case SpriteSortMode.FrontToBack:
                    comparer = frontToBackComparer;
                    break;
                default:
                    return;
            }

            Array.Sort(spriteItems, 0, (int)spriteItemsCount, comparer);
        }

        private void FlushBatch()
        {
            switch (spriteSortMode)
            {
                case SpriteSortMode.Texture:
                case SpriteSortMode.BackToFront:
                case SpriteSortMode.FrontToBack:
                    SortSprites();
                    break;
            }

            UpdateSpriteItemsBuffer(0, spriteItemsCount);

            Texture previousTexture = null;
            int prevIndex = spriteItems[0].TextureInfo.W;
            uint offset = 0;
            for (uint i = 0; i < spriteItemsCount; i++)
            {
                var current = spriteItems[i].TextureInfo.W;
                if (current != prevIndex && current != -1)
                {
                    texturesById.TryGetValue(prevIndex, out previousTexture);
                    if (i > offset)
                    {
                        DrawBatch(previousTexture, offset, i - offset);
                    }
                    offset = i;

                    prevIndex = current;
                }
            }
            texturesById.TryGetValue(prevIndex, out previousTexture);
            DrawBatch(previousTexture, offset, spriteItemsCount - offset);

            textureId = 0;
            textureIds.Clear();
            texturesById.Clear();

            spriteItemsCount = 0;
        }

        private void UpdateSpriteItemsBuffer(uint startIndex, uint elementCount)
        {
            var offsetInBytes = startIndex * Utilities.SizeOf<SpriteBatchItem>();
            vertexBuffer.SetData(spriteItems, startIndex, elementCount, (uint)offsetInBytes);
        }

        private void PrepareForFlushing()
        {
            Matrix4x4F ortho = Matrix4x4F.OrthoOffCenter(0, currentScreenSize.X, currentScreenSize.Y, 0, 0.0f, 1.0f);
            Matrix4x4F.Multiply(ref transformMatrix, ref ortho, out final);
            
        }

        private void DrawBatch(Texture texture, uint startIndex, uint elementsCount)
        {
            IEffectPass pass;
            if (customEffect != null)
            {
                pass = customEffect.CurrentTechnique.Passes["Render"];
                customEffectTexture.SetResource(texture);
                customEffectTextureSampler.SetResource(assignedSamplerState);
                customEffectMatrixTransform.SetValue(final);
                customTextureCornerCoords.SetValue(UVCornerCoords);
            }
            else
            {
                pass = spriteBatchEffectPass;
                effectSampler.SetResource(assignedSamplerState);
                efffectTexture.SetResource(texture);
                effectMatrixTransform.SetValue(final);
                effectUVCornerCoords.SetValue(UVCornerCoords);
            }

            graphicsDevice.VertexType = vertexType;
            graphicsDevice.SetVertexBuffer(vertexBuffer);
            pass.Apply();
            graphicsDevice.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
            graphicsDevice.Draw(VERTICES_PER_SPRITE, elementsCount, 0, startIndex);
            pass.UnApply(true);
        }

        class BackToFrontComparer : IComparer<SpriteBatchItem>
        {
            public int Compare(SpriteBatchItem left, SpriteBatchItem right)
            {
                return left.Depth.CompareTo(right.Depth);
            }
        }

        class FrontToBackComparer : IComparer<SpriteBatchItem>
        {
            public int Compare(SpriteBatchItem left, SpriteBatchItem right)
            {
                return right.Depth.CompareTo(left.Depth);
            }
        }

        class TextureComparer : IComparer<SpriteBatchItem>
        {
            public int Compare(SpriteBatchItem left, SpriteBatchItem right)
            {
                return left.TextureInfo.W.CompareTo(right.TextureInfo.W);
            }
        }
    }
}
