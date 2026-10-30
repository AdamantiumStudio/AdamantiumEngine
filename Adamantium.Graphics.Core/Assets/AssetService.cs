using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;

namespace Adamantium.Graphics.Core.Assets;

/// <summary>Finds assets through its <see cref="Resolvers"/> and reads them with its <see cref="Readers"/>, both
/// registered before anything is loaded. An asset loaded with <see cref="AssetLoadOptions.AllowDuplication"/> off is
/// kept and read once: loads of it at the same time wait for the one reading, and a load after it was disposed reads
/// it again.</summary>
public class AssetService : DisposableObject, IAssetService
{
    private const string WritingExtension = ".writing";

    private static readonly StringComparer FileNameComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly object sync = new();
    private readonly Dictionary<AssetKey, Task<object>> kept = new();
    private readonly Dictionary<string, byte[]> embedded = new(StringComparer.Ordinal);

    public AssetService(IDependencyResolver serviceProvider, Satellites satellites)
    {
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        Satellites = satellites ?? throw new ArgumentNullException(nameof(satellites));
    }

    public IDependencyResolver ServiceProvider { get; }

    public Satellites Satellites { get; }

    /// <summary>The folder asset names are taken from, unless a load says
    /// <see cref="AssetLoadOptions.IgnoreRootDirectory"/>.</summary>
    public string RootDirectory { get; set; } = string.Empty;

    /// <summary>Where assets are looked for, in turn; the first that finds one wins.</summary>
    public List<IAssetResolver> Resolvers { get; } = new();

    /// <summary>The reader of each type of asset.</summary>
    public Dictionary<Type, IAssetReader> Readers { get; } = new();

    public void Embed(string assetName, byte[] data)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetName);
        ArgumentNullException.ThrowIfNull(data);

        lock (sync)
        {
            embedded[assetName] = data;
        }
    }

    public bool Exists(string assetName)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetName);

        lock (sync)
        {
            if (embedded.ContainsKey(assetName))
            {
                return true;
            }
        }

        return Find(PathOf(assetName, null)) != null;
    }

    public string Resolve(string assetName)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetName);

        return Find(PathOf(assetName, null)) is { } found ? Path.GetFullPath(found) : null;
    }

    public IReadOnlyList<AssetFileInfo> GetAssets(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);

        var files = new DirectoryInfo(folder).EnumerateFiles()
            .Where(file => !file.Name.StartsWith('.') && (file.Attributes & FileAttributes.Hidden) == 0
                           && !file.Name.EndsWith(WritingExtension, StringComparison.OrdinalIgnoreCase))
            .Select(file => file.FullName)
            .ToList();
        var paths = new HashSet<string>(files, FileNameComparer);

        return files
            .Where(file => !paths.Contains(file + AssetFile.FileExtension))
            .Select(file => new AssetFileInfo(file))
            .OrderBy(asset => asset.Name, FileNameComparer)
            .ToList();
    }

    public T Load<T>(string assetName, object options = null) => (T)Load(typeof(T), assetName, options);

    public async Task<T> LoadAsync<T>(string assetName, object options = null) =>
        (T)await LoadAsync(typeof(T), assetName, options);

    public object Load(Type assetType, string assetName, object options = null)
    {
        return Task.Run(() => LoadAsync(assetType, assetName, options)).GetAwaiter().GetResult();
    }

    public void Unload()
    {
        List<Task<object>> loads;
        lock (sync)
        {
            loads = new List<Task<object>>(kept.Values);
            kept.Clear();
        }

        foreach (var load in loads)
        {
            DisposeOnceRead(load);
        }
    }

    public bool Unload<T>(string assetName) => Unload(typeof(T), assetName);

    public bool Unload(Type assetType, string assetName)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        ArgumentNullException.ThrowIfNull(assetName);

        Task<object> load;
        lock (sync)
        {
            if (!kept.Remove(new AssetKey(assetType, assetName), out load))
            {
                return false;
            }
        }

        DisposeOnceRead(load);
        return true;
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        if (disposeManagedResources)
        {
            Unload();
        }

        base.Dispose(disposeManagedResources);
    }

    private Task<object> LoadAsync(Type assetType, string assetName, object options)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        ArgumentNullException.ThrowIfNull(assetName);

        if (options is not AssetLoadOptions { AllowDuplication: false })
        {
            return Task.Run(() => Read(assetType, assetName, options));
        }

        var key = new AssetKey(assetType, assetName);
        lock (sync)
        {
            if (kept.TryGetValue(key, out var load) && !IsGone(load))
            {
                return load;
            }

            load = Task.Run(() => Read(assetType, assetName, options));
            kept[key] = load;
            return load;
        }
    }

    private async Task<object> Read(Type assetType, string assetName, object options)
    {
        if (!Readers.TryGetValue(assetType, out var reader))
        {
            throw new NotSupportedException($"No reader is registered for {assetType.FullName}");
        }

        byte[] data;
        lock (sync)
        {
            embedded.TryGetValue(assetName, out data);
        }

        var path = data != null ? assetName : Find(PathOf(assetName, options as AssetLoadOptions));
        if (path == null)
        {
            throw new AssetNotFoundException($"{assetName} is found by none of the resolvers");
        }

        var parameters = new AssetReaderParameters
        {
            AssetName = assetName,
            AssetPath = path,
            Data = data,
            AssetType = assetType,
            Options = options
        };

        return await reader.ReadAssetAsync(this, parameters).ConfigureAwait(false)
               ?? throw new NotSupportedException($"{reader.GetType().Name} gave nothing for {path}");
    }

    private string PathOf(string assetName, AssetLoadOptions options) =>
        options?.IgnoreRootDirectory == true ? assetName : Path.Combine(RootDirectory ?? string.Empty, assetName);

    private string Find(string assetPath)
    {
        if (Resolvers.Count == 0)
        {
            throw new InvalidOperationException("The asset service has no resolver to look for assets with");
        }

        foreach (var resolver in Resolvers)
        {
            if (resolver.Resolve(assetPath) is { Length: > 0 } found)
            {
                return found;
            }
        }

        return null;
    }

    private static bool IsGone(Task<object> load) =>
        load.IsFaulted || load.IsCanceled || load.IsCompletedSuccessfully && load.Result is DisposableObject { IsDisposed: true };

    private static void DisposeOnceRead(Task<object> load)
    {
        if (load.IsCompletedSuccessfully)
        {
            (load.Result as IDisposable)?.Dispose();
            return;
        }

        load.ContinueWith(read => (read.Result as IDisposable)?.Dispose(), CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    private readonly record struct AssetKey(Type Type, string Name);
}
