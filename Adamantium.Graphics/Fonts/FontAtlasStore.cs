using System.Collections.Generic;
using Adamantium.Fonts;
using Adamantium.Fonts.TextureGeneration;
using Adamantium.Graphics.Core;

namespace Adamantium.Graphics.Fonts
{
    public static class FontAtlasStore
    {
        // Concurrent: a virtualizing panel lays text out across cores.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<FontParameters, FontAtlas> _fontAtlasMap = new();
        /// <summary>Rasterizes glyphs inline instead of on a worker, for a one-shot render that has no next frame for the
        /// text to fill in. Threads filling inline at once upload into an atlas one at a time.</summary>
        public static bool SynchronousFill { get; set; }

        /// <summary>The layers of video memory, some 5.6 MB each, the images of color glyphs (emoji of 'sbix' and 'CBDT')
        /// take before the ones no text shows any longer are let go for new ones: 16 by default, some 90 MB - about 500
        /// of Noto Color Emoji's. When the images text shows take more, the atlas grows past it.</summary>
        public static int ColorImageLayerBudget { get; set; } = 16;

        /// <summary>The images of color glyphs every atlas holds.</summary>
        public static int ColorImages
        {
            get
            {
                var images = 0;
                foreach (var atlas in _fontAtlasMap.Values)
                {
                    images += atlas.ColorImageCount;
                }

                return images;
            }
        }

        /// <summary>The layers the images of color glyphs take in every atlas, against
        /// <see cref="ColorImageLayerBudget"/>.</summary>
        public static int ColorImageLayers
        {
            get
            {
                var layers = 0;
                foreach (var atlas in _fontAtlasMap.Values)
                {
                    layers += (int)atlas.ColorImages.LayersTaken;
                }

                return layers;
            }
        }

        /// <summary>The atlas rasterized with <paramref name="fontParameters"/>, shared by all text in every font: a glyph
        /// is found in it by its typeface and index. Made on the resource loader of <paramref name="graphicsDevice"/>'s
        /// main device, which lives as long as the application: the device of the window that asks first may close
        /// before the others.</summary>
        public static FontAtlas GetOrCreateFrom(IGraphicsDevice graphicsDevice, FontParameters fontParameters)
        {
            return _fontAtlasMap.GetOrAdd(fontParameters,
                _ => new FontAtlas(graphicsDevice.MainDevice?.ResourceLoaderDevice ?? graphicsDevice, fontParameters));
        }

        /// <summary>Drops every atlas while its device is still alive: an atlas belongs to the device that made it, and
        /// after a device swap the next text asks for one on the new device.</summary>
        public static void Reset()
        {
            foreach (var atlas in _fontAtlasMap.Values)
            {
                atlas.Dispose();
            }

            _fontAtlasMap.Clear();
        }

        /// <summary>Upload every atlas's finished glyphs, on the thread that owns the device. Called once a frame by the
        /// renderer; true when something landed, which is the caller's cue that text built before it is out of date.</summary>
        public static bool PumpReadyGlyphs()
        {
            var landed = false;
            foreach (var atlas in _fontAtlasMap.Values)
            {
                landed |= atlas.PumpReady();
            }

            if (landed) LandedVersion++;

            return landed;
        }

        /// <summary>Bumped every time letters land, so each render cache can tell "anything since I last looked"
        /// independently of who drained the queue.</summary>
        public static int LandedVersion { get; private set; }

        /// <summary>Is any atlas still rasterizing? While this is true the renderer keeps asking for frames, so the
        /// glyphs that arrive have a frame to appear in.</summary>
        public static bool HasPendingGlyphs
        {
            get
            {
                foreach (var pair in _fontAtlasMap)
                {
                    if (pair.Value.HasPendingGlyphs)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
