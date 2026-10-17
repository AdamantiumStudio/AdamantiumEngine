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

        /// <summary>The atlas rasterized with <paramref name="fontParameters"/>, shared by all text in every font: a glyph
        /// is found in it by its typeface and index.</summary>
        public static FontAtlas GetOrCreateFrom(IGraphicsDevice graphicsDevice, FontParameters fontParameters)
        {
            return _fontAtlasMap.GetOrAdd(fontParameters, _ => new FontAtlas(graphicsDevice, fontParameters));
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
                foreach (var atlas in _fontAtlasMap.Values)
                {
                    if (atlas.HasPendingGlyphs) return true;
                }

                return false;
            }
        }
    }
}
