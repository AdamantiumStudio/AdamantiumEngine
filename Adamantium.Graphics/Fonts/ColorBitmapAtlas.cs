using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Core;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Imaging.Png;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Fonts;

internal sealed class ColorBitmapAtlas : DisposableObject
{
    public const int MaxCellSize = 256;
    public const int LayerSize = 1024;
    public const int Levels = 9;
    private const int MinCellSize = 32;
    private const int MaxImageSize = 65535;
    private const uint InitialLayerCount = 2;

    private readonly IGraphicsDevice _device;
    private readonly object _gate = new();
    private readonly List<(ColorBitmapCell Cell, byte[] Levels)> _pending = [];
    private readonly Dictionary<(int Width, int Height), (uint Layer, int Next)> _shelves = new();
    private readonly Dictionary<(int Width, int Height), List<ColorBitmapCell>> _freeCells = new();
    private readonly Dictionary<uint, int> _cellsInLayer = new();
    private readonly List<uint> _freeLayers = [];
    private readonly uint _maxLayers;
    private GrowingTextureArray _layers;
    private uint _layersTaken;

    public ColorBitmapAtlas(IGraphicsDevice device)
    {
        _device = device;
        _maxLayers = device.Adapter.AdapterProperties.Limits.MaxImageArrayLayers;
    }

    public Texture Texture => _layers?.Texture;

    public uint LayersTaken
    {
        get
        {
            lock (_gate)
            {
                return _layersTaken;
            }
        }
    }

    public bool HasPending
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count > 0;
            }
        }
    }

    public static ColorBitmapImage Prepare(byte[] png)
    {
        if (!TryDecode(png, out var rgba, out var width, out var height))
        {
            return null;
        }

        var pixels = Premultiply(rgba, width, height);
        while (width > MaxCellSize || height > MaxCellSize)
        {
            pixels = Halve(pixels, width, height);
            width = Math.Max(1, (width + 1) / 2);
            height = Math.Max(1, (height + 1) / 2);
        }

        var cellWidth = CellSize(width);
        var cellHeight = CellSize(height);
        return new ColorBitmapImage(width, height, cellWidth, cellHeight, BuildLevels(pixels, width, height, cellWidth,
            cellHeight));
    }

    // A cell for the image: one given back first, then the next of its size's layer, then a new layer while the atlas
    // takes fewer than the budget (or the device's limit, past the budget). Null when there is none.
    public ColorBitmapCell Add(ColorBitmapImage image, bool pastBudget)
    {
        lock (_gate)
        {
            var size = (image.CellWidth, image.CellHeight);
            ColorBitmapCell cell;
            if (_freeCells.TryGetValue(size, out var free) && free.Count > 0)
            {
                cell = free[^1] with { Width = image.Width, Height = image.Height };
                free.RemoveAt(free.Count - 1);
            }
            else
            {
                var perRow = LayerSize / image.CellWidth;
                var perLayer = perRow * (LayerSize / image.CellHeight);
                if (!_shelves.TryGetValue(size, out var shelf) || shelf.Next == perLayer)
                {
                    if (_freeLayers.Count > 0)
                    {
                        shelf = (_freeLayers[^1], 0);
                        _freeLayers.RemoveAt(_freeLayers.Count - 1);
                    }
                    else if (_layersTaken < LayerLimit(pastBudget))
                    {
                        shelf = (_layersTaken++, 0);
                    }
                    else
                    {
                        return null;
                    }
                }

                cell = new ColorBitmapCell(shelf.Layer, shelf.Next % perRow * image.CellWidth,
                    shelf.Next / perRow * image.CellHeight, image.Width, image.Height, image.CellWidth, image.CellHeight,
                    LevelsOf(image.CellWidth, image.CellHeight));
                _shelves[size] = (shelf.Layer, shelf.Next + 1);
            }

            _cellsInLayer[cell.Layer] = _cellsInLayer.GetValueOrDefault(cell.Layer) + 1;
            _pending.Add((cell, image.Levels));
            return cell;
        }
    }

    // Gives a cell back for another image of its size; a layer with none left taken is given back whole, for any size.
    public void Free(ColorBitmapCell cell)
    {
        lock (_gate)
        {
            var size = (cell.CellWidth, cell.CellHeight);
            var left = _cellsInLayer[cell.Layer] - 1;
            if (left > 0)
            {
                _cellsInLayer[cell.Layer] = left;
                if (!_freeCells.TryGetValue(size, out var free))
                {
                    _freeCells[size] = free = [];
                }

                free.Add(cell);
                return;
            }

            _cellsInLayer.Remove(cell.Layer);
            _freeCells.GetValueOrDefault(size)?.RemoveAll(other => other.Layer == cell.Layer);
            if (_shelves.TryGetValue(size, out var shelf) && shelf.Layer == cell.Layer)
            {
                _shelves.Remove(size);
            }

            _freeLayers.Add(cell.Layer);
        }
    }

    private uint LayerLimit(bool pastBudget) =>
        pastBudget ? _maxLayers : Math.Min(_maxLayers, (uint)Math.Max(1, FontAtlasStore.ColorImageLayerBudget));

    public List<ColorBitmapCell> Upload()
    {
        List<(ColorBitmapCell Cell, byte[] Levels)> pending;
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                return [];
            }

            pending = [.. _pending];
            _pending.Clear();
        }

        var uploaded = new List<ColorBitmapCell>(pending.Count);
        if (!EnsureLayers(pending))
        {
            return uploaded;
        }

        var total = 0;
        foreach (var (_, levels) in pending)
        {
            total += levels.Length;
        }

        var data = new byte[total];
        var regions = new List<BufferImageCopy>();
        var offset = 0;
        foreach (var (cell, levels) in pending)
        {
            Array.Copy(levels, 0, data, offset, levels.Length);
            for (var level = 0; level < cell.Levels; level++)
            {
                var width = (uint)(cell.CellWidth >> level);
                var height = (uint)(cell.CellHeight >> level);
                regions.Add(new BufferImageCopy
                {
                    BufferOffset = (ulong)offset,
                    BufferRowLength = width,
                    BufferImageHeight = height,
                    ImageSubresource = new ImageSubresourceLayers
                    {
                        AspectMask = ImageAspectFlagBits.ColorBit,
                        MipLevel = (uint)level,
                        BaseArrayLayer = cell.Layer,
                        LayerCount = 1,
                    },
                    ImageOffset = new Offset3D { X = cell.X >> level, Y = cell.Y >> level, Z = 0 },
                    ImageExtent = new Extent3D { Width = width, Height = height, Depth = 1 },
                });
                offset += (int)(width * height * 4);
            }

            uploaded.Add(cell);
        }

        using var buffer = Buffer.New(_device, data, BufferUsageFlags.TransferSrc,
            MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent);
        var commandBuffer = _device.BeginSingleTimeCommand();
        _device.InsertImageMemoryBarrier(commandBuffer, Texture, AccessFlagBits2.ShaderReadBit,
            AccessFlagBits2.TransferWriteBit, Texture.ImageLayout, ImageLayout.TransferDstOptimal,
            PipelineStageFlagBits2.FragmentShaderBit, PipelineStageFlagBits2.AllTransferBit);
        commandBuffer.CopyBufferToImage(buffer, Texture, ImageLayout.TransferDstOptimal, (uint)regions.Count,
            regions.ToArray());
        _device.InsertImageMemoryBarrier(commandBuffer, Texture, AccessFlagBits2.TransferWriteBit,
            AccessFlagBits2.ShaderReadBit, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal,
            PipelineStageFlagBits2.AllTransferBit, PipelineStageFlagBits2.FragmentShaderBit);
        _device.EndSingleTimeCommand(commandBuffer);
        return uploaded;
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        if (disposeManagedResources)
        {
            _layers?.Dispose();
            _layers = null;
        }

        base.Dispose(disposeManagedResources);
    }

    private static bool TryDecode(byte[] png, out byte[] rgba, out int width, out int height)
    {
        rgba = null;
        width = 0;
        height = 0;
        if (png.Length < 24)
        {
            return false;
        }

        var declaredWidth = (uint)(png[16] << 24 | png[17] << 16 | png[18] << 8 | png[19]);
        var declaredHeight = (uint)(png[20] << 24 | png[21] << 16 | png[22] << 8 | png[23]);
        if (declaredWidth == 0 || declaredHeight == 0 || declaredWidth > MaxImageSize || declaredHeight > MaxImageSize)
        {
            return false;
        }

        var handle = GCHandle.Alloc(png, GCHandleType.Pinned);
        try
        {
            var image = PngHelper.LoadFromMemory(handle.AddrOfPinnedObject(), (ulong)png.Length);
            width = (int)image.Width;
            height = (int)image.Height;
            rgba = image.GetRgba8Pixels();
            return width > 0 && height > 0 && rgba != null && rgba.Length >= width * height * 4;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            handle.Free();
        }
    }

    private bool EnsureLayers(List<(ColorBitmapCell Cell, byte[] Levels)> pending)
    {
        var deepest = 0u;
        foreach (var (cell, _) in pending)
        {
            deepest = Math.Max(deepest, cell.Layer);
        }

        _layers ??= CreateLayers();

        while (_layers.Count <= deepest)
        {
            if (!_layers.TryAdd(out _))
            {
                return false;
            }
        }

        return true;
    }

    private GrowingTextureArray CreateLayers()
    {
        return new GrowingTextureArray(_device, new TextureDescription
        {
            Width = LayerSize,
            Height = LayerSize,
            Depth = 1,
            ArrayLayers = InitialLayerCount,
            MipLevels = Levels,
            Samples = MSAALevel.None,
            Format = Format.R8G8B8A8_UNORM,
            InitialLayout = ImageLayout.Undefined,
            DesiredImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            ImageType = ImageType._2d,
            ImageAspect = ImageAspectFlagBits.ColorBit,
            Usage = ImageUsageFlagBits.SampledBit | ImageUsageFlagBits.TransferDstBit | ImageUsageFlagBits.TransferSrcBit,
            Dimension = TextureDimension.Texture2D
        }, _maxLayers, "Color Glyph Atlas");
    }

    private static int CellSize(int size)
    {
        var cell = MinCellSize;
        while (cell < size)
        {
            cell *= 2;
        }

        return cell;
    }

    private static int LevelsOf(int cellWidth, int cellHeight)
    {
        var levels = 1;
        for (var side = Math.Min(cellWidth, cellHeight); side > 1; side /= 2)
        {
            levels++;
        }

        return levels;
    }

    private static byte[] Premultiply(byte[] rgba, int width, int height)
    {
        var result = new byte[width * height * 4];
        for (var i = 0; i < result.Length; i += 4)
        {
            var alpha = rgba[i + 3];
            result[i] = (byte)((rgba[i] * alpha + 127) / 255);
            result[i + 1] = (byte)((rgba[i + 1] * alpha + 127) / 255);
            result[i + 2] = (byte)((rgba[i + 2] * alpha + 127) / 255);
            result[i + 3] = alpha;
        }

        return result;
    }

    private static byte[] Halve(byte[] pixels, int width, int height)
    {
        var halfWidth = Math.Max(1, (width + 1) / 2);
        var halfHeight = Math.Max(1, (height + 1) / 2);
        var result = new byte[halfWidth * halfHeight * 4];
        for (var y = 0; y < halfHeight; y++)
        {
            for (var x = 0; x < halfWidth; x++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    var sum = 0;
                    var count = 0;
                    for (var dy = 0; dy < 2; dy++)
                    {
                        for (var dx = 0; dx < 2; dx++)
                        {
                            var sx = x * 2 + dx;
                            var sy = y * 2 + dy;
                            if (sx < width && sy < height)
                            {
                                sum += pixels[(sy * width + sx) * 4 + channel];
                                count++;
                            }
                        }
                    }

                    result[(y * halfWidth + x) * 4 + channel] = (byte)((sum + count / 2) / count);
                }
            }
        }

        return result;
    }

    private static byte[] BuildLevels(byte[] pixels, int width, int height, int cellWidth, int cellHeight)
    {
        var levels = LevelsOf(cellWidth, cellHeight);
        var total = 0;
        for (var level = 0; level < levels; level++)
        {
            total += (cellWidth >> level) * (cellHeight >> level) * 4;
        }

        var data = new byte[total];
        for (var y = 0; y < height; y++)
        {
            Array.Copy(pixels, y * width * 4, data, y * cellWidth * 4, width * 4);
        }

        var source = 0;
        for (var level = 1; level < levels; level++)
        {
            var sourceWidth = cellWidth >> (level - 1);
            var sourceHeight = cellHeight >> (level - 1);
            var targetWidth = sourceWidth / 2;
            var target = source + sourceWidth * sourceHeight * 4;
            for (var y = 0; y < sourceHeight / 2; y++)
            {
                for (var x = 0; x < targetWidth; x++)
                {
                    for (var channel = 0; channel < 4; channel++)
                    {
                        var sum = data[source + (y * 2 * sourceWidth + x * 2) * 4 + channel]
                                  + data[source + (y * 2 * sourceWidth + x * 2 + 1) * 4 + channel]
                                  + data[source + ((y * 2 + 1) * sourceWidth + x * 2) * 4 + channel]
                                  + data[source + ((y * 2 + 1) * sourceWidth + x * 2 + 1) * 4 + channel];
                        data[target + (y * targetWidth + x) * 4 + channel] = (byte)((sum + 2) / 4);
                    }
                }
            }

            source = target;
        }

        return data;
    }
}
