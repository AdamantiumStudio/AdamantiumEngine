using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Adamantium.Graphics.Core;

namespace Adamantium.Graphics;

/// <summary>
/// What device memory the live resources of one logical device hold, counted at one moment: by kind, and the largest
/// groups of resources alike in name and shape. What the driver keeps for itself - swapchain images, shaders, pipelines -
/// is not among them; the difference from what the system reports for the process is that.
/// </summary>
public sealed class DeviceMemoryCensus
{
    private readonly List<(string Kind, int Count, ulong Bytes)> _kinds;
    private readonly List<(string Kind, string Name, string Shape, int Count, ulong Bytes)> _groups;

    private DeviceMemoryCensus(List<(string Kind, int Count, ulong Bytes)> kinds,
        List<(string Kind, string Name, string Shape, int Count, ulong Bytes)> groups, ulong blockBytes,
        ulong listedImageBytes, long imageBytes, long stagingBytes)
    {
        _kinds = kinds;
        _groups = groups;
        BlockBytes = blockBytes;
        ListedImageBytes = listedImageBytes;
        ImageBytes = imageBytes;
        StagingBytes = stagingBytes;
        foreach (var kind in kinds)
        {
            TotalBytes += kind.Bytes;
            ResourceCount += kind.Count;
        }
    }

    /// <summary>The bytes every counted resource holds.</summary>
    public ulong TotalBytes { get; }

    /// <summary>The live resources counted.</summary>
    public int ResourceCount { get; }

    /// <summary>The bytes the pooled blocks take, the buffers' share of them and the room left in them together.</summary>
    public ulong BlockBytes { get; }

    /// <summary>The image bytes of the textures the devices list.</summary>
    public ulong ListedImageBytes { get; }

    /// <summary>The image bytes allocated and not yet freed, listed or not: more than <see cref="ListedImageBytes"/>
    /// is textures nobody lists any more - lost with a device, or never listed.</summary>
    public long ImageBytes { get; }

    /// <summary>The bytes textures keep in staging buffers for reading back.</summary>
    public long StagingBytes { get; }

    /// <summary>Counts the resources of every device made on <paramref name="main"/>.</summary>
    public static DeviceMemoryCensus Take(MainGraphicsDevice main)
    {
        ArgumentNullException.ThrowIfNull(main);
        var resources = new List<GraphicsResource>();
        main.ResourceLoaderDevice?.CollectResources(resources);
        var devices = new List<IGraphicsDevice>();
        main.CollectDevices(devices);
        foreach (var device in devices)
        {
            if (!ReferenceEquals(device, main.ResourceLoaderDevice))
            {
                device.CollectResources(resources);
            }
        }

        var kinds = new Dictionary<string, (int Count, ulong Bytes)>();
        var groups = new Dictionary<(string Kind, string Name, string Shape), (int Count, ulong Bytes)>();
        var seen = new HashSet<GraphicsResource>(ReferenceEqualityComparer.Instance);
        var listedImageBytes = 0UL;
        foreach (var resource in resources)
        {
            if (!seen.Add(resource) || resource.IsDisposed)
            {
                continue;
            }

            var bytes = resource.DeviceMemoryBytes;
            if (resource is Texture)
            {
                listedImageBytes += bytes;
            }

            var kind = resource.GetType().Name;
            var counted = kinds.GetValueOrDefault(kind);
            kinds[kind] = (counted.Count + 1, counted.Bytes + bytes);
            var key = (kind, string.IsNullOrEmpty(resource.Name) ? "-" : resource.Name, ShapeOf(resource));
            var grouped = groups.GetValueOrDefault(key);
            groups[key] = (grouped.Count + 1, grouped.Bytes + bytes);
        }

        var blockBytes = main.MemoryAllocator is DeviceMemoryAllocator allocator ? allocator.BytesInBlocks : 0;
        return new DeviceMemoryCensus(
            kinds.Select(pair => (pair.Key, pair.Value.Count, pair.Value.Bytes)).OrderByDescending(kind => kind.Bytes).ToList(),
            groups.Select(pair => (pair.Key.Kind, pair.Key.Name, pair.Key.Shape, pair.Value.Count, pair.Value.Bytes))
                .OrderByDescending(group => group.Bytes).ToList(),
            blockBytes, listedImageBytes, main.ImageBytes, main.StagingBytes);
    }

    /// <summary>The census as text: the totals, every kind, and the <paramref name="largest"/> largest groups.</summary>
    public string Report(int largest = 25)
    {
        var text = new StringBuilder();
        text.AppendLine(Invariant($"device memory {Mb(TotalBytes)} MB in {ResourceCount} resources; pooled blocks {Mb(BlockBytes)} MB"));
        text.AppendLine(Invariant(
            $"images allocated {Mb((ulong)Math.Max(0, ImageBytes))} MB, of them listed {Mb(ListedImageBytes)} MB; staging {Mb((ulong)Math.Max(0, StagingBytes))} MB"));
        foreach (var kind in _kinds)
        {
            text.AppendLine(Invariant($"  {kind.Kind,-28} {kind.Count,7} {Mb(kind.Bytes),10} MB"));
        }

        text.AppendLine("largest:");
        foreach (var group in _groups.Take(largest))
        {
            if (group.Bytes == 0)
            {
                break;
            }

            text.AppendLine(Invariant($"  {Mb(group.Bytes),10} MB {group.Count,6} x {group.Kind} '{group.Name}' {group.Shape}"));
        }

        return text.ToString();
    }

    private static string ShapeOf(GraphicsResource resource) => resource switch
    {
        Texture texture => ShapeOf(texture.Description),
        Buffer buffer => buffer.Usage.ToString(),
        _ => string.Empty,
    };

    private static string ShapeOf(TextureDescription description)
    {
        var shape = new StringBuilder();
        shape.Append(CultureInfo.InvariantCulture, $"{description.Width}x{description.Height}");
        if (description.Depth > 1) shape.Append(CultureInfo.InvariantCulture, $"x{description.Depth}");
        if (description.ArrayLayers > 1) shape.Append(CultureInfo.InvariantCulture, $" [{description.ArrayLayers}]");
        if (description.MipLevels > 1) shape.Append(CultureInfo.InvariantCulture, $" mips {description.MipLevels}");
        shape.Append(' ').Append(description.Format);
        if (description.Samples != MSAALevel.None) shape.Append(' ').Append(description.Samples);
        return shape.ToString();
    }

    private static string Mb(ulong bytes) => (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture);

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
