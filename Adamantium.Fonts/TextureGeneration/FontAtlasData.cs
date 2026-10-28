using System;
using Adamantium.Mathematics;
using System.Collections.Generic;
using System.Linq;

namespace Adamantium.Fonts.TextureGeneration
{
    public class FontAtlasData
    {
        private object lockObject = new Object();
        
        public Size<double> AtlasSize { get; set; }
        private List<GlyphTextureData> glyphData { get; }

        public IReadOnlyList<GlyphTextureData> GlyphData => glyphData.AsReadOnly();
        private Dictionary<ulong, GlyphTextureData> glyphDataMap;
        public byte[] ImageData { get; set; }
        public byte[] FontData { get; set; }
        public string Name { get; set; }
        
        public uint GlyphTextureSize { get; }

        // Running cursor for tight shelf packing of glyph bitmaps (shelf height = tallest glyph on it).
        public int PackX { get; set; }
        public int PackY { get; set; }
        public int ShelfHeight { get; set; }

        // Which array-texture LAYER glyphs are currently packed into (0-based). Each layer is one AtlasSize slice; the
        // shelf packer advances to the next layer (AdvanceToNextLayer) when a glyph won't fit in the current one's height.
        public uint CurrentDepthLayer { get; private set; }

        // Number of array layers the atlas texture has. Packing never advances past the last one.
        public uint LayerCount { get; }

        // Set once packing has had to clamp to the last layer (further glyphs overwrite it). The caller can warn; the old
        // behavior instead wrote layer 2 into a 1-layer 2D image, which crashed the GPU past ~256 glyphs.
        public bool LayersExhausted { get; private set; }

        // Move packing to the next array layer: reset the shelf cursor and bump the layer, clamped at the last layer.
        public void AdvanceToNextLayer()
        {
            PackX = 0;
            PackY = 0;
            ShelfHeight = 0;
            if (CurrentDepthLayer + 1 < LayerCount)
                CurrentDepthLayer++;
            else
                LayersExhausted = true;
        }

        public FontAtlasData(uint glyphTextureSize, uint layerCount = 1)
        {
            GlyphTextureSize = glyphTextureSize;
            glyphData = new List<GlyphTextureData>();
            glyphDataMap = new Dictionary<ulong, GlyphTextureData>();
            LayerCount = Math.Max(1, layerCount);
            CurrentDepthLayer = 0;
        }

        public FontAtlasData(uint glyphTextureSize, Size<double> atlasSize, uint layerCount = 1) : this(glyphTextureSize, layerCount)
        {
            AtlasSize = atlasSize;
        }

        public void GenerateGlyphDataMap()
        {
            glyphDataMap = GlyphData.ToDictionary(x => x.Key);
        }

        public void AddGlyphData(GlyphTextureData glyphTextureData)
        {
            lock (lockObject)
            {
                glyphData.Add(glyphTextureData);
                glyphDataMap[glyphTextureData.Key] = glyphTextureData;
            }
        }

        /// <summary>The glyph with this key (<see cref="GlyphTextureData.Key"/>): its index in an atlas of one font.</summary>
        public GlyphTextureData GetGlyphData(ulong key)
        {
            lock (lockObject)
            {
                glyphDataMap.TryGetValue(key, out var data);
                return data;
            }
        }

        public GlyphTextureData[] GetGlyphData(params ulong[] keys)
        {
            var datas = new List<GlyphTextureData>();
            lock (lockObject)
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    if (glyphDataMap.TryGetValue(keys[i], out var data))
                    {
                        datas.Add(data);
                    }
                }
            }

            return datas.ToArray();
        }

        public RectangleF GetUVCoordinatesForGlyph(ulong key)
        {
            lock (lockObject)
            {
                if (glyphDataMap.TryGetValue(key, out var data))
                {
                    return data.UVRect;
                }
            }

            return default;
        }

    }
}