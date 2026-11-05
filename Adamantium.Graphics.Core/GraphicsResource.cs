using System;
using Adamantium.Core;

namespace Adamantium.Graphics.Core;

/// <summary>
/// Base class for all <see cref="GraphicsResource"/>.
/// </summary>
public abstract class GraphicsResource: DisposableObject
{
   /// <summary>
   /// <see cref="GraphicsDevice"/>
   /// </summary>
   public IGraphicsDevice GraphicsDevice { get; }

   /// <summary>The device memory this resource holds: its own allocation, or its share of a pooled block; 0 when it
   /// holds none.</summary>
   public virtual ulong DeviceMemoryBytes => 0;

   internal GraphicsResource()
   {
         
   }

   /// <summary>
   /// Constructor for <see cref="GraphicsResource"/>
   /// </summary>
   /// <param name="graphicsDevice"></param>
   /// <exception cref="ArgumentNullException"></exception>
   protected GraphicsResource(IGraphicsDevice graphicsDevice, string name = "")
   {
      ArgumentNullException.ThrowIfNull(graphicsDevice);
      GraphicsDevice = graphicsDevice;
      GraphicsDevice.AddResource(this);
      // Unregister on dispose so the wrapper isn't rooted by the device forever. Hooked via the Disposing event
      // because subclasses (Buffer, Texture, ...) override Dispose(bool) without calling base.
      Disposing += (_, _) => GraphicsDevice.RemoveResource(this);
      Name = name;
   }
}