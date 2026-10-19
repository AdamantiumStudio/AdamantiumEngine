using System;
using System.Threading.Tasks;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;

namespace Adamantium.Graphics.Core.Content;

public interface IContentManager
{
   /// <summary>
   /// The process-wide services, for a reader that needs one to build its content.
   /// </summary>
   IDependencyResolver ServiceProvider { get; }

   /// <summary>
   /// The one-of-a-kind objects of the content's owner - its graphics device service, say - for a reader that needs them.
   /// </summary>
   Satellites Satellites { get; }

   /// <summary>
   /// Whether the asset (name with extension) exists.
   /// </summary>
   bool Exists(string assetName);

   /// <summary>
   /// Makes <paramref name="data"/> loadable as the asset <paramref name="assetName"/>, as an asset carried inside
   /// another (an image inside a model) is: it then loads, is kept and is loaded again for a new device like one read
   /// from a file. Like a file's, an asset already loaded stays as it was when other bytes are embedded under its name,
   /// so a name should say what the bytes hold.
   /// </summary>
   void Embed(string assetName, byte[] data);

   /// <summary>
   /// Loads an asset by its full name (with extension). Throws <see cref="AssetNotFoundException"/> when no resolver
   /// finds it and <see cref="NotSupportedException"/> when no reader decodes it.
   /// </summary>
   T Load<T>(string assetName, object options = null);

   /// <summary>
   /// <see cref="Load{T}"/>, asynchronously.
   /// </summary>
   Task<T> LoadAsync<T>(string assetName, object options = null);

   /// <summary>
   /// <see cref="Load{T}"/> with the type given at run time.
   /// </summary>
   object Load(Type assetType, string assetName, object options = null);

   /// <summary>
   /// Unloads and disposes everything this manager loaded. Not thread safe, unlike Load.
   /// </summary>
   void Unload();

   /// <summary>
   /// Unloads and disposes an asset; false when it was not loaded.
   /// </summary>
   bool Unload<T>(string assetName);

   /// <summary>
   /// Unloads and disposes an asset; false when it was not loaded.
   /// </summary>
   bool Unload(Type assetType, string assetName);
}