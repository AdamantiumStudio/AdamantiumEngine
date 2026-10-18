using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Fonts.TextureGeneration;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Imaging;
using Adamantium.Imaging.PaletteQuantizer.Extensions;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Fonts
{
    public class FontAtlas : GraphicsResource
    {
        private TextureAtlasGenerator atlasGenerator;
        private HashSet<ulong> processedGlyphs;

        // Glyphs whose MSDF is being generated on a worker RIGHT NOW, and the finished data waiting to be uploaded.
        // Generation is arithmetic and needs no device; the upload does - so the two live on different threads and meet
        // here.
        private readonly HashSet<ulong> _inFlight = new();
        private List<GlyphTextureData> _ready = new();
        private readonly HashSet<ulong> _generated = new();
        private readonly object _asyncGate = new();
        private readonly object _uploadGate = new();
        private readonly object _fillGate = new();

        /// <summary>Bumped whenever glyphs LAND in the atlas. A text block built while some of its glyphs were still
        /// being rasterized compares this against the version it built at and rebuilds when they differ - that is what
        /// makes an asynchronous fill appear without anybody polling for a particular glyph.</summary>
        public int Version { get; private set; }

        /// <summary>True while any glyph for this atlas is still being rasterized or waiting to be uploaded.</summary>
        public bool HasPendingGlyphs
        {
            get
            {
                lock (_asyncGate)
                {
                    return _inFlight.Count > 0 || _ready.Count > 0 || _imagesInFlight.Count > 0 || _imagesDecoding > 0
                           || _imagesWaiting.Count > 0;
                }
            }
        }

        private bool _warnedLayersExhausted;

        private readonly GrowingTextureArray _layers;

        private readonly ColorPaintStore _paints;

        private readonly ColorBitmapAtlas _bitmaps;

        private readonly Dictionary<ulong, (ColorBitmapCell Cell, ImagePlace Place)> _imagesInFlight = new();

        private readonly HashSet<ulong> _imagesRequested = new();

        // The images in the atlas, each with the pump it was last drawn at: text built since asked for it.
        private readonly Dictionary<ulong, ResidentImage> _imagesResident = new();

        // Images with no room left under the budget, landing once the ones no text shows are let go.
        private readonly List<(ulong Key, ColorBitmapImage Image, ImagePlace Place)> _imagesWaiting = [];

        private int _imagesDecoding;

        private bool _imagesFailed;

        private long _pumps;

        // The pump that bumped the version so that every block drawn builds again and marks its images: -1 when no
        // images are being let go. A few frames and a quarter of a second later, every cache drawing has built its
        // blocks again - however many windows and overlays pump each frame; one that drew none since asks again.
        private long _sweepStart = -1;

        private long _sweepStartedAt;

        private const int SweepPumps = 4;

        private static readonly TimeSpan SweepTime = TimeSpan.FromMilliseconds(250);

        protected FontAtlasData AtlasData { get; }

        internal Texture Atlas => _layers.Texture;

        internal Texture ColorAtlas => _bitmaps.Texture ?? Atlas;

        internal ColorBitmapAtlas ColorImages => _bitmaps;

        /// <summary>The layers of the atlas texture in use, and allocated: it starts with <see cref="InitialLayerCount"/>
        /// and doubles as glyphs fill it.</summary>
        public uint LayerCount => _layers.Count;

        public uint LayerCapacity => _layers.Capacity;

        public uint MSDFTextureSize { get; }
        
        public byte SampleRate { get; }
        
        public float PixelRange { get; }
        
        public uint StartGlyphIndex { get; }
        
        public uint GlyphCount { get; }
        
        public GlyphSortingVariant SortingVariant { get; }
        
        public uint GlyphMargin { get; }

        /// <summary>The layers a new atlas allocates, each one atlas-size square holding some 225 glyphs: two, as a
        /// texture of one layer is viewed as a plain 2D image and the shaders read an array.</summary>
        public const uint InitialLayerCount = 2;

        /// <summary>An atlas the glyphs of every font share, rasterized as text asks for them: each glyph is found by its
        /// font and index.</summary>
        public FontAtlas(IGraphicsDevice device, FontParameters parameters, uint atlasSize = 1024) : base(device)
        {
            processedGlyphs = new HashSet<ulong>();

            MSDFTextureSize = parameters.MsdfTextureSize;
            SampleRate = parameters.SampleRate;
            PixelRange = parameters.PixelRange;
            StartGlyphIndex = parameters.StartGlyphIndex;
            GlyphCount = parameters.GlyphCount;
            SortingVariant = parameters.SortingVariant;
            GlyphMargin = parameters.GlyphMargin;

            var description = new TextureDescription
            {
                Width = atlasSize,
                Height = atlasSize,
                Depth = 1,
                ArrayLayers = InitialLayerCount,
                MipLevels = 1,
                Samples = MSAALevel.None,
                Format = Format.R8G8B8A8_UNORM,
                InitialLayout = ImageLayout.Preinitialized,
                DesiredImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageType = ImageType._2d,
                ImageAspect = ImageAspectFlagBits.ColorBit,
                Usage = ImageUsageFlagBits.SampledBit | ImageUsageFlagBits.TransferDstBit | ImageUsageFlagBits.TransferSrcBit,
                Dimension = TextureDimension.Texture2D
            };

            _layers = ToDispose(new GrowingTextureArray(GraphicsDevice, description, name: "Dynamic Font Atlas"));
            _paints = ToDispose(new ColorPaintStore());
            _bitmaps = ToDispose(new ColorBitmapAtlas(device));
            AtlasData = new FontAtlasData(MSDFTextureSize, new Size(atlasSize, atlasSize), _layers.MaxCapacity);
            atlasGenerator = new TextureAtlasGenerator(null, null, AtlasData, parameters);
        }

        /// <summary>Requests the glyphs <paramref name="font"/> draws <paramref name="text"/>'s characters with, the way
        /// <see cref="RequestAsync(IEnumerable{ValueTuple{IFont, Glyph}})"/> requests glyphs.</summary>
        public void RequestAsync(IFont font, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            RequestAsync(font.TranslateIntoGlyphs(new string(text.Distinct().ToArray())).Select(g => (font, g)));
        }

        /// <summary>Requests glyphs of any fonts without waiting: MSDF runs on a worker and <see cref="PumpReady"/>
        /// uploads the result; each landing bumps <see cref="Version"/> so blocks rebuild. Shaped text reaches glyphs no
        /// character maps to, such as ligatures and alternates.</summary>
        public void RequestAsync(IEnumerable<(IFont Font, Glyph Glyph)> glyphs)
        {
            // A render with no "next frame" (a bitmap, a preview, an off-screen test) cannot let its letters arrive later.
            if (FontAtlasStore.SynchronousFill)
            {
                ProcessGlyphs(glyphs.ToList());
                Version++;
                return;
            }

            List<(IFont Font, Glyph Glyph)> toGenerate = null;
            lock (_asyncGate)
            {
                foreach (var pair in glyphs)
                {
                    var key = GlyphTextureData.KeyOf(pair.Font.Typeface, pair.Glyph.Index);
                    if (processedGlyphs.Contains(key) || !_inFlight.Add(key))
                    {
                        continue;
                    }

                    (toGenerate ??= []).Add(pair);
                }
            }

            if (toGenerate == null) return;

            // ONE task for the whole batch: the generator parallelizes across the glyphs it is given, so handing it the
            // batch keeps every core busy - the reason the caller pools a frame's text in the first place. Each glyph
            // is handed over as soon as it is done, so the next pump uploads it without waiting for the slowest one.
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    atlasGenerator.GenerateTextureForGlyphs(toGenerate, (pair, data) =>
                    {
                        lock (_asyncGate)
                        {
                            if (data != null)
                            {
                                _ready.Add(data);
                            }

                            _generated.Add(GlyphTextureData.KeyOf(pair.Font.Typeface, pair.Glyph.Index));
                        }
                    });
                }
                catch
                {
                    // A glyph that cannot be rasterized must not wedge the queue: let it out of flight and go on. The
                    // block that wanted it draws without it, exactly as it does for a glyph the font has no outline for.
                    lock (_asyncGate)
                    {
                        foreach (var pair in toGenerate)
                        {
                            var key = GlyphTextureData.KeyOf(pair.Font.Typeface, pair.Glyph.Index);
                            if (!_generated.Contains(key))
                            {
                                _inFlight.Remove(key);
                            }
                        }
                    }
                }
            });
        }

        /// <summary>Upload whatever the workers finished, on the thread that owns the device. Cheap - the expensive half
        /// (the MSDF itself) already happened elsewhere; measured at 12 ms against 650 for the generation. Returns true
        /// when something landed, which is the caller's cue that text built earlier is now out of date.</summary>
        public bool PumpReady()
        {
            var images = PumpImages(true);
            List<GlyphTextureData> ready;
            ulong[] generated;
            lock (_asyncGate)
            {
                if (_generated.Count == 0)
                {
                    if (images)
                    {
                        Version++;
                    }

                    return images;
                }

                ready = _ready;
                _ready = new List<GlyphTextureData>();
                generated = _generated.ToArray();
                _generated.Clear();
            }

            if (ready.Count > 0)
            {
                ProcessTextureData(ready);
            }

            lock (_asyncGate)
            {
                foreach (var key in generated)
                {
                    processedGlyphs.Add(key);
                    _inFlight.Remove(key);
                }
            }

            Version++;
            return true;
        }

        private List<(IFont Font, Glyph Glyph)> GetNotProcessedGlyphs(IEnumerable<(IFont Font, Glyph Glyph)> glyphs)
        {
            var notProcessed = new List<(IFont Font, Glyph Glyph)>();
            var seen = new HashSet<ulong>();
            lock (_asyncGate)
            {
                foreach (var pair in glyphs)
                {
                    var key = GlyphTextureData.KeyOf(pair.Font.Typeface, pair.Glyph.Index);
                    if (!processedGlyphs.Contains(key) && seen.Add(key))
                    {
                        notProcessed.Add(pair);
                    }
                }
            }

            return notProcessed;
        }

        private void ProcessGlyphs(IReadOnlyList<(IFont Font, Glyph Glyph)> glyphs)
        {
            lock (_fillGate)
            {
                FillInline(glyphs);
            }
        }

        private void FillInline(IReadOnlyList<(IFont Font, Glyph Glyph)> glyphs)
        {
            var glyphsToProcess = GetNotProcessedGlyphs(glyphs);
            var textureDataArray = atlasGenerator.GenerateTextureForGlyphs(glyphsToProcess);

            if (textureDataArray.Count > 0)
            {
                ProcessTextureData(textureDataArray);
            }

            if (AtlasData.LayersExhausted && !_warnedLayersExhausted)
            {
                _warnedLayersExhausted = true;
                System.Console.WriteLine($"[FONT] Dynamic atlas exhausted all {_layers.MaxCapacity} layers the device allows; further glyphs overwrite the last.");
            }

            lock (_asyncGate)
            {
                foreach (var pair in glyphsToProcess)
                {
                    processedGlyphs.Add(GlyphTextureData.KeyOf(pair.Font.Typeface, pair.Glyph.Index));
                }
            }
        }

        private void ProcessTextureData(IReadOnlyList<GlyphTextureData> textureDataArray)
        {
            lock (_uploadGate)
            {
                Upload(textureDataArray);
            }
        }

        private void Upload(IReadOnlyList<GlyphTextureData> textureDataArray)
        {
            GlyphIntegrityProbe.EnterUpload(this);
            foreach (var textureData in textureDataArray) GlyphIntegrityProbe.Inspect(this, textureData);

            var deepest = textureDataArray.Max(x => x.DepthLayer);
            while (_layers.Count <= deepest)
            {
                if (!_layers.TryAdd(out _))
                {
                    break;
                }
            }

            Atlas.TransitionImageLayout(ImageLayout.TransferDstOptimal);
            var commandBuffer = GraphicsDevice.BeginSingleTimeCommand();
            var buffers = new List<Buffer>();

            foreach (var textureData in textureDataArray)
            {
                if (textureData.IsEmpty) continue;

                var buffer = Buffer.New(
                    GraphicsDevice,
                    textureData.Pixels,
                    BufferUsageFlags.TransferSrc,
                    MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent);
                buffers.Add(buffer);

                var region = new BufferImageCopy();
                region.BufferOffset = 0;
                region.BufferRowLength = (uint)textureData.FullGlyphSize.Width;
                region.BufferImageHeight = (uint)textureData.FullGlyphSize.Height;
                region.ImageSubresource = new ImageSubresourceLayers();
                region.ImageSubresource.AspectMask = ImageAspectFlagBits.ColorBit;
                region.ImageSubresource.MipLevel = 0;
                // The glyph goes into its ARRAY LAYER (DepthLayer), at its 2D position within that layer. Depth stays 1
                // (a 2D-array slice is 1 deep); the layer selects the slice via BaseArrayLayer. The old code put the layer
                // in ImageExtent.Depth on a 1-deep 2D image, which the GPU rejected once a second layer appeared.
                region.ImageSubresource.BaseArrayLayer = textureData.DepthLayer;
                region.ImageSubresource.LayerCount = 1;
                region.ImageOffset = new Offset3D()
                {
                    X = textureData.BoundingRect.Left,
                    Y = textureData.BoundingRect.Top,
                    Z = 0
                };
                region.ImageExtent = new Extent3D()
                {
                    Width = (uint)textureData.FullGlyphSize.Width,
                    Height = (uint)textureData.FullGlyphSize.Height,
                    Depth = 1
                };

                commandBuffer.CopyBufferToImage(buffer, Atlas, ImageLayout.TransferDstOptimal, 1, region);
            }

            GraphicsDevice.EndSingleTimeCommand(commandBuffer);
            Atlas.TransitionImageLayout(Atlas.Description.DesiredImageLayout);

            foreach (var textureData in textureDataArray)
            {
                textureData.Pixels = [];
            }
            
            foreach (var buffer in buffers)
            {
                buffer?.Dispose();
            }

            GlyphIntegrityProbe.LeaveUpload(this);
        }

        public RectangleF GetUVCoordinatesForGlyph(IFont font, Glyph glyph)
        {
            return AtlasData.GetUVCoordinatesForGlyph(GlyphTextureData.KeyOf(font.Typeface, glyph.Index));
        }

        /// <summary>Where <paramref name="glyph"/> of <paramref name="font"/> lies in the atlas; null until it has been
        /// rasterized.</summary>
        public GlyphTextureData GetGlyphData(IFont font, Glyph glyph)
        {
            return AtlasData.GetGlyphData(GlyphTextureData.KeyOf(font.Typeface, glyph.Index));
        }

        internal int GetPaintProgram(IFont font, uint colorGlyph, int palette, IReadOnlyList<ColorPaintOperation> operations)
        {
            var key = (ulong)(uint)font.Typeface.Id << 32 | colorGlyph | (ulong)(ushort)palette << 16;
            if (_paints.TryGetProgram(key, out var known))
            {
                return known;
            }

            var masks = new Dictionary<uint, ColorPaintMask>();
            foreach (var operation in operations)
            {
                if (operation.Kind != ColorPaintOperationKind.PushClip || masks.ContainsKey(operation.GlyphIndex))
                {
                    continue;
                }

                var mask = font.GetGlyphByIndex(operation.GlyphIndex);
                var cell = GetGlyphData(font, mask);
                if (cell == null && !mask.IsEmpty && !IsSettled(font, mask))
                {
                    return -1;
                }

                masks[operation.GlyphIndex] = new ColorPaintMask(mask, font.GetLeftSideBearing(mask.Index), cell);
            }

            RectangleF? clipBox = font.TryGetColorClipBox(colorGlyph, out var box) ? box : null;
            return _paints.GetProgram(key, operations, masks, font.UnitsPerEm, MSDFTextureSize, clipBox);
        }

        internal bool TryGetImageProgram(IFont font, uint glyph, out int program)
        {
            var key = GlyphTextureData.KeyOf(font.Typeface, glyph);
            if (_paints.TryGetProgram(key, out program) && program < 0)
            {
                return false;
            }

            program = -1;
            lock (_asyncGate)
            {
                if (_imagesResident.TryGetValue(key, out var resident))
                {
                    resident.LastDrawn = _pumps;
                    program = resident.Program;
                    return true;
                }

                // On its way, or let go and asked for again.
                if (!_imagesRequested.Add(key))
                {
                    return true;
                }
            }

            var bitmap = font.GetColorBitmap(glyph, ColorBitmapAtlas.MaxCellSize);
            if (bitmap == null || bitmap.PixelsPerEm <= 0)
            {
                _paints.AddNone(key);
                return false;
            }

            lock (_asyncGate)
            {
                _imagesDecoding++;
            }

            if (!FontAtlasStore.SynchronousFill)
            {
                System.Threading.Tasks.Task.Run(() => Land(key, bitmap, font.UnitsPerEm));
                return true;
            }

            Land(key, bitmap, font.UnitsPerEm);
            if (PumpImages(false))
            {
                Version++;
            }

            lock (_asyncGate)
            {
                if (_imagesResident.TryGetValue(key, out var landed))
                {
                    program = landed.Program;
                }
            }

            return program >= 0;
        }

        private void Land(ulong key, ColorBitmap bitmap, double unitsPerEm)
        {
            var image = ColorBitmapAtlas.Prepare(bitmap.Png);
            var unitsPerPixel = unitsPerEm / bitmap.PixelsPerEm;
            var place = new ImagePlace(bitmap.Left * unitsPerPixel, (bitmap.Top - bitmap.Height) * unitsPerPixel,
                bitmap.Width * unitsPerPixel, bitmap.Height * unitsPerPixel);
            lock (_asyncGate)
            {
                _imagesDecoding--;
                if (image == null)
                {
                    LandNowhere(key);
                    return;
                }

                // A render with no next frame cannot wait for images to be let go: it takes the room past the budget.
                var cell = _bitmaps.Add(image, FontAtlasStore.SynchronousFill);
                if (cell == null && (_imagesResident.Count > 0 || _sweepStart >= 0))
                {
                    _imagesWaiting.Add((key, image, place));
                    return;
                }

                LandIn(key, cell ?? _bitmaps.Add(image, true), place);
            }
        }

        private void LandIn(ulong key, ColorBitmapCell cell, ImagePlace place)
        {
            if (cell == null)
            {
                LandNowhere(key);
                return;
            }

            _imagesInFlight[key] = (cell, place);
        }

        private void LandNowhere(ulong key)
        {
            _paints.SetNone(key);
            _imagesFailed = true;
        }

        // A frame's pump moves a sweep on; an inline fill only uploads what it landed.
        private bool PumpImages(bool frame)
        {
            bool changed;
            lock (_asyncGate)
            {
                changed = _imagesFailed;
                _imagesFailed = false;
                if (frame)
                {
                    _pumps++;
                    if (_imagesWaiting.Count > 0 && _sweepStart < 0)
                    {
                        // Every block drawn builds again on the new version and marks the images it shows.
                        _sweepStart = _pumps;
                        _sweepStartedAt = Stopwatch.GetTimestamp();
                        changed = true;
                    }
                    else if (_sweepStart >= 0 && _pumps >= _sweepStart + SweepPumps
                             && Stopwatch.GetElapsedTime(_sweepStartedAt) >= SweepTime)
                    {
                        LetGoOfUndrawnImages();
                        changed = true;
                    }
                }
            }

            if (!_bitmaps.HasPending)
            {
                return changed;
            }

            HashSet<ColorBitmapCell> cells;
            lock (_uploadGate)
            {
                cells = new HashSet<ColorBitmapCell>(_bitmaps.Upload());
            }

            lock (_asyncGate)
            {
                foreach (var pair in _imagesInFlight.ToArray())
                {
                    var (cell, place) = pair.Value;
                    if (!cells.Contains(cell))
                    {
                        continue;
                    }

                    _imagesInFlight.Remove(pair.Key);
                    var program = _paints.AddImage(pair.Key, place.Left, place.Bottom, place.Width, place.Height, cell);
                    if (program < 0)
                    {
                        _bitmaps.Free(cell);
                        continue;
                    }

                    _imagesResident[pair.Key] = new ResidentImage(cell, program) { LastDrawn = _pumps };
                }
            }

            return changed || cells.Count > 0;
        }

        // The images no block has asked for since the sweep began give their cells to the ones waiting; those that
        // still find no room take it past the budget.
        private void LetGoOfUndrawnImages()
        {
            foreach (var pair in _imagesResident.ToArray())
            {
                var resident = pair.Value;
                if (resident.LastDrawn >= _sweepStart)
                {
                    continue;
                }

                _imagesResident.Remove(pair.Key);
                _imagesRequested.Remove(pair.Key);
                _paints.ClearImage(resident.Program);
                _bitmaps.Free(resident.Cell);
            }

            foreach (var (key, image, place) in _imagesWaiting)
            {
                LandIn(key, _bitmaps.Add(image, false) ?? _bitmaps.Add(image, true), place);
            }

            _imagesWaiting.Clear();
            _sweepStart = -1;
        }

        private bool IsSettled(IFont font, Glyph glyph)
        {
            lock (_asyncGate)
            {
                return processedGlyphs.Contains(GlyphTextureData.KeyOf(font.Typeface, glyph.Index));
            }
        }

        internal (ulong Programs, ulong Stops) UploadPaints()
        {
            return _paints.Upload(GraphicsDevice);
        }

        private sealed class ResidentImage(ColorBitmapCell cell, int program)
        {
            public ColorBitmapCell Cell { get; } = cell;

            public int Program { get; } = program;

            public long LastDrawn { get; set; }
        }

        // Where an image lies in its glyph's space, in font units.
        private readonly record struct ImagePlace(double Left, double Bottom, double Width, double Height);
    }
}