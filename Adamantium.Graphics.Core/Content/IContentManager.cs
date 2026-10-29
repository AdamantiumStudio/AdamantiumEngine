using System;
using System.Threading.Tasks;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;

namespace Adamantium.Graphics.Core.Content;

/// <summary>Loads assets by name, safe to call from several threads at once.</summary>
public interface IContentManager
{
   /// <summary>The process-wide services, for a reader that needs one to build its content.</summary>
   IDependencyResolver ServiceProvider { get; }

   /// <summary>The one-of-a-kind objects of the content's owner - its graphics device service, say - for a reader that
   /// needs them.</summary>
   Satellites Satellites { get; }

   /// <summary>Whether the asset, named with its extension, is embedded or found by a resolver.</summary>
   bool Exists(string assetName);

   /// <summary>Makes <paramref name="data"/> loadable as the asset <paramref name="assetName"/>, as an asset carried
   /// inside another (an image inside a model) is: it then loads, is kept and is loaded again for a new device like one
   /// read from a file. An asset already kept stays as it was when other bytes are embedded under its name, so a name
   /// should say what the bytes hold.</summary>
   void Embed(string assetName, byte[] data);

   /// <summary>Loads an asset by its name with its extension; <see cref="ContentLoadOptions"/> say whether it is kept.
   /// Throws <see cref="AssetNotFoundException"/> when it is neither embedded nor found, and
   /// <see cref="NotSupportedException"/> when no reader reads its type.</summary>
   T Load<T>(string assetName, object options = null);

   /// <summary><see cref="Load{T}"/>, asynchronously.</summary>
   Task<T> LoadAsync<T>(string assetName, object options = null);

   /// <summary><see cref="Load{T}"/> with the type given at run time.</summary>
   object Load(Type assetType, string assetName, object options = null);

   /// <summary>Forgets and disposes every asset kept; one still being read is disposed once it is.</summary>
   void Unload();

   /// <summary>Forgets and disposes an asset kept; false when it is not.</summary>
   bool Unload<T>(string assetName);

   /// <summary>Forgets and disposes an asset kept; false when it is not.</summary>
   bool Unload(Type assetType, string assetName);
}
