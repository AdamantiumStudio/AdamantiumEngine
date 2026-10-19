using System;

namespace Adamantium.Graphics.Core.Content;

public struct ContentReaderParameters
{
   public String DeviceId;  
   /// <summary>
   /// Name of the asset currently loaded when using <see cref="IContentManager.Load{T}"/>.
   /// </summary>
   public String AssetName;

   /// <summary>
   /// Path to currently loaded asset when using <see cref="IContentManager.Load{T}"/>.
   /// </summary>
   public String AssetPath;

   /// <summary>
   /// The asset's bytes when it was <see cref="IContentManager.Embed">embedded</see> rather than found in a file; then
   /// <see cref="AssetPath"/> is its name.
   /// </summary>
   public byte[] Data;

   /// <summary>
   /// Type of the asset currently loaded when using <see cref="IContentManager.Load{T}"/>.
   /// </summary>
   public Type AssetType;

   /// <summary>
   /// Path to copy additional asset data and serialized asset itself
   /// </summary>
   public String OutputPath;

   /// <summary>
   /// Custom options provided when using <see cref="IContentManager.Load{T}"/>.
   /// </summary>
   public object Options;
}