using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;

namespace Adamantium.Graphics.Core.Content;

public class ContentManager : DisposableObject, IContentManager
{
    private readonly Dictionary<AssetKey, object> assetLockers;
    private readonly Dictionary<string, byte[]> embedded = new();
    protected readonly Dictionary<AssetKey, object> LoadedAssets;

    public ContentManager(IDependencyResolver serviceProvider, Satellites satellites)
    {
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        Satellites = satellites ?? throw new ArgumentNullException(nameof(satellites));
        RootDirectory = String.Empty;
        Resolvers = new List<IContentResolver>();
        Readers = new Dictionary<Type, IContentReader>();
        LoadedAssets = new Dictionary<AssetKey, object>();
        assetLockers = new Dictionary<AssetKey, object>();
    }

    /// <summary>
    /// Gets the service provider associated with the ContentManager.
    /// </summary>
    /// <value>The service provider.</value>
    public IDependencyResolver ServiceProvider { get; protected set; }

    public Satellites Satellites { get; }

    public String RootDirectory { get; set; }

    public String OutputDirectory { get; set; }

    /// <summary>
    /// Add or remove registered <see cref="IContentResolver"/> to this instance.
    /// </summary>
    public List<IContentResolver> Resolvers { get; }

    /// <summary>
    /// Add or remove registered <see cref="IContentReader"/> to this instance.
    /// </summary>
    public Dictionary<Type, IContentReader> Readers { get; }

    private String CombinePath(String assetPath)
    {
        return Path.Combine(RootDirectory ?? string.Empty, assetPath);
    }

    public void Embed(string assetName, byte[] data)
    {
        if (String.IsNullOrEmpty(assetName)) throw new ArgumentNullException(nameof(assetName));
        if (data == null) throw new ArgumentNullException(nameof(data));

        lock (embedded)
        {
            embedded[assetName] = data;
        }
    }

    private byte[] EmbeddedData(string assetName)
    {
        lock (embedded)
        {
            return embedded.GetValueOrDefault(assetName);
        }
    }

    public bool Exists(string assetName)
    {
        if (String.IsNullOrEmpty(assetName))
        {
            throw new ArgumentNullException(nameof(assetName));
        }

        if (EmbeddedData(assetName) != null)
        {
            return true;
        }

        lock (Resolvers)
        {
            if (Resolvers.Count == 0)
            {
                throw new InvalidOperationException("No content resolver registered to this content manager");
            }
        }
        string assetPath = CombinePath(assetName);

        foreach (var contentResolver in Resolvers)
        {
            if (contentResolver.Exists(assetPath))
            {
                return true;
            }
        }
        return false;
    }

    public async Task<T> LoadAsync<T>(string assetName, object options = null)
    {
        var item = await Task.Run(()=>Load(typeof(T), assetName, options));
        return (T)item;
    }

    public T Load<T>(string assetName, object options = null)
    {
        var asset = (T)Load(typeof(T), assetName, options);
        return asset;
    }

    public object Load(Type assetType, string assetName, object options = null)
    {
        if (assetType == null) throw new ArgumentNullException(nameof(assetType));
        if (assetName == null) throw new ArgumentNullException(nameof(assetName));

        object result = null;
        ContentLoadOptions loadOptions = options as ContentLoadOptions;

        var assetKey = new AssetKey(assetType, assetName);
        lock (GetAssetLocker(assetKey, true))
        {
            lock (LoadedAssets)
            {
                if (loadOptions?.AllowDuplication == false)
                {
                    if (LoadedAssets.TryGetValue(assetKey, out result))
                    {
                        if (result is not DisposableObject { IsDisposed: true })
                        {
                            return result;
                        }

                        LoadedAssets.Remove(assetKey);
                    }
                }
            }

            string assetPath = Path.Combine(RootDirectory ?? string.Empty, assetName);

            if (loadOptions?.IgnoreRootDirectory == true)
            {
                assetPath = assetName;
            }

            var data = EmbeddedData(assetName);
            var finalAssetPath = data != null ? assetName : ResolvePath(assetPath);

            if (String.IsNullOrEmpty(finalAssetPath))
            {
                throw new AssetNotFoundException("there is no such file in " + assetPath);
            }

            ContentReaderParameters parameters = new ContentReaderParameters();
            parameters.AssetName = assetName;
            parameters.AssetPath = finalAssetPath;
            parameters.Data = data;
            parameters.AssetType = assetType;
            parameters.OutputPath = OutputDirectory;

            result = Task.Run(()=>LoadAsset(parameters)).Result;
        }

        if (loadOptions?.AllowDuplication == false)
        {
            lock (LoadedAssets)
            {
                LoadedAssets.Add(assetKey, result);
            }
        }

        return result;
    }

    private string ResolvePath(string assetPath)
    {
        lock (Resolvers)
        {
            if (Resolvers.Count == 0)
            {
                throw new InvalidOperationException("No content resolver registered to this content manager");
            }

            foreach (var contentResolver in Resolvers)
            {
                try
                {
                    if (contentResolver.Exists(assetPath))
                    {
                        return contentResolver.Resolve(assetPath);
                    }
                }
                catch (AssetNotFoundException exception)
                {
                    Debug.WriteLine(exception);
                }
            }
        }

        return string.Empty;
    }

    protected object GetAssetLocker(AssetKey assetKey, bool create)
    {
        object assetLockerRead;
        lock (assetLockers)
        {
            if (!assetLockers.TryGetValue(assetKey, out assetLockerRead) && create)
            {
                assetLockerRead = new object();
                assetLockers.Add(assetKey, assetLockerRead);
            }
        }
        return assetLockerRead;
    }


    private async Task<object> LoadAsset(ContentReaderParameters parameters)
    {
        IContentReader contentReader = null;
        lock (Readers)
        {
            Readers.TryGetValue(parameters.AssetType, out contentReader);
        }

        if (contentReader == null)
        {
            var contentReaderAttribute = parameters.AssetType.GetCustomAttribute<ContentReaderAttribute>(true);

            if (contentReaderAttribute != null)
            {
                contentReader = Activator.CreateInstance(contentReaderAttribute.ContentReaderType) as IContentReader;
                if (contentReader != null)
                {
                    Readers.Add(parameters.AssetType, contentReader);
                }
            }
        }

        if (contentReader == null)
        {
            throw new NotSupportedException(
                $"Type [{parameters.AssetType.FullName}] doesn't provide a ContentReaderAttribute, and there is no registered content reader for it.");
        }

        var result = await contentReader.ReadContentAsync(this, parameters);

        if (result == null)
        {
            throw new NotSupportedException(
                $"Registered ContentReader of type [{contentReader.GetType()}] fails to load content of type [{parameters.AssetType.FullName}] from file [{parameters.AssetPath}].");
        }

        return result;
    }

    public void Unload()
    {
        lock (LoadedAssets)
        {
            foreach (var asset in LoadedAssets.Values)
            {
                var disposableAsset = asset as IDisposable;
                disposableAsset?.Dispose();
            }
            LoadedAssets.Clear();
        }
    }

    public bool Unload<T>(string assetName)
    {
        return Unload(typeof(T), assetName);
    }

    public bool Unload(Type assetType, string assetName)
    {
        if (assetType == null) throw new ArgumentNullException(nameof(assetType));
        if (assetName == null) throw new ArgumentNullException(nameof(assetName));

        object asset;
        var assetKey = new AssetKey(assetType, Path.Combine(RootDirectory, assetName));

        object accessLockerRead = GetAssetLocker(assetKey, false);
        if (accessLockerRead == null)
        {
            return false;
        }

        lock (accessLockerRead)
        {
            lock (LoadedAssets)
            {
                if (!LoadedAssets.TryGetValue(assetKey, out asset))
                {
                    return false;
                }
                LoadedAssets.Remove(assetKey);
            }

            lock (assetLockers)
            {
                assetLockers.Remove(assetKey);
            }
        }

        var disposable = asset as IDisposable;
        disposable?.Dispose();

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

    /// <summary>
    /// Use this key to store loaded assets.
    /// </summary>
    protected struct AssetKey : IEquatable<AssetKey>
    {
        public AssetKey(Type assetType, string assetPath)
        {
            AssetType = assetType;
            AssetPath = assetPath;
        }

        public readonly Type AssetType;

        public readonly string AssetPath;

        public bool Equals(AssetKey other)
        {
            return AssetType == other.AssetType &&
                   string.Equals(AssetPath, other.AssetPath, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is AssetKey && Equals((AssetKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (AssetType.GetHashCode() * 397) ^ AssetPath.GetHashCode();
            }
        }

        public static bool operator ==(AssetKey left, AssetKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(AssetKey left, AssetKey right)
        {
            return !left.Equals(right);
        }
    }
}