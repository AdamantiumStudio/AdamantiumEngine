using System;
using System.IO;
using Adamantium.Core.DependencyInjection;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;

namespace Adamantium.Engine.Snapshots;

/// <summary>Takes snapshots in a small universe of its own, with one output that has no surface, on the device of the
/// universe it serves. The universe is made with the first snapshot and lives until the service is disposed; each model
/// leaves it, with what it loaded, once its snapshot is taken. Runs on the calling thread.</summary>
public sealed class ModelSnapshotService : IModelSnapshotService
{
    /// <summary>The size of a snapshot unless told otherwise: the size of an asset's thumbnail.</summary>
    public const uint DefaultSize = 256;

    private readonly IDependencyContainer _container;
    private readonly IGraphicsDeviceService _devices;
    private readonly object _lock = new();
    private SnapshotUniverse _universe;
    private bool _disposed;

    /// <param name="container">The process's services, as the universe served has them.</param>
    /// <param name="devices">The graphics devices of the universe served.</param>
    /// <param name="size">The width and height of a snapshot, in pixels.</param>
    public ModelSnapshotService(IDependencyContainer container, IGraphicsDeviceService devices, uint size = DefaultSize)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentOutOfRangeException.ThrowIfZero(size);

        _container = container;
        _devices = devices;
        Size = size;
    }

    public uint Size { get; }

    public byte[] TakePng(string assetPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetPath);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _universe ??= new SnapshotUniverse(_container, _devices, Size);
            using var frame = _universe.Take(assetPath);
            if (frame == null)
            {
                return null;
            }

            using var png = new MemoryStream();
            frame.Save(png, ImageFileType.Png);
            return png.ToArray();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _universe?.ShutDown();
            _universe = null;
        }
    }
}
