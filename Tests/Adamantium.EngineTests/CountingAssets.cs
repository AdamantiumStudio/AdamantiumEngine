using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Graphics.Core.Models;

namespace Adamantium.EngineTests;

/// <summary>Assets for scene tests: the models it is given, each load counted.</summary>
internal sealed class CountingAssets : IAssetService
{
    public Dictionary<string, SceneData> Models { get; } = new();

    public Dictionary<string, int> Loads { get; } = new();

    public IDependencyResolver ServiceProvider => null;

    public Satellites Satellites { get; } = new();

    public bool Exists(string assetName) => Models.ContainsKey(assetName);

    public string Resolve(string assetName) => null;

    public IReadOnlyList<AssetFileInfo> GetAssets(string folder) => [];

    public void Embed(string assetName, byte[] data)
    {
    }

    public T Load<T>(string assetName, object options = null) => LoadAsync<T>(assetName, options).Result;

    public Task<T> LoadAsync<T>(string assetName, object options = null)
    {
        Loads[assetName] = Loads.GetValueOrDefault(assetName) + 1;
        return Models.TryGetValue(assetName, out var model)
            ? Task.FromResult((T)(object)model)
            : Task.FromException<T>(new FileNotFoundException(assetName));
    }

    public object Load(Type assetType, string assetName, object options = null) => null;

    public void Unload()
    {
    }

    public bool Unload<T>(string assetName) => false;

    public bool Unload(Type assetType, string assetName) => false;
}
