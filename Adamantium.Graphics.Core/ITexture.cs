using System;
using Adamantium.Core;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;
using Image = Adamantium.Vulkan.Core.Image;

namespace Adamantium.Graphics.Core;

public interface ITexture : INamedObject
{
    uint Width { get; }
    uint Height { get; }
    uint Depth { get; }
    SurfaceFormat SurfaceFormat { get; }
    ImageLayout ImageLayout { get; set; }
    ulong TotalSizeInBytes { get; }
    unsafe IntPtr ManagedPointer { get; }
    unsafe void* NativePointer { get; }
    MSAALevel MSAALevel { get; }
    
    ImageAspectFlagBits ImageAspect { get; }

    /// <summary>
    /// Gets a value indicating whether this instance is disposed.
    /// </summary>
    /// <value>
    /// <c>true</c> if this instance is disposed; otherwise, <c>false</c>.
    /// </value>
    bool IsDisposed { get; }

    /// <summary>
    /// <see cref="GraphicsDevice"/>
    /// </summary>
    IGraphicsDevice GraphicsDevice { get; }
    
    Image GetImage();
    
    ImageView GetImageView();
    
    void SetImageView(ImageView imageView);

    void Save(string path, ImageFileType fileType);

    /// <summary>Reads the texture back to host memory and writes its RAW pixels (native B8G8R8A8 layout, no color
    /// swap and no image encoding) to <paramref name="path"/>. Far faster than <see cref="Save"/> for repeated
    /// read-back (no per-frame PNG zlib) - used by the live designer's frame transport, which converts the bytes itself.</summary>
    void SaveRaw(string path);

    /// <summary>Reads this texture back into a fresh host-memory image (native B8G8R8A8, no color swap / encode); the
    /// caller owns and disposes it. For off-screen bakes that need CPU pixels (the drag ghost, thumbnails) rather than a
    /// file. <paramref name="layer"/> picks a layer of an array texture.</summary>
    Adamantium.Imaging.Image ReadbackToImage(uint layer = 0);

    ImageViewCreateInfo Info { get; }

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources
    /// </summary>
    void Dispose();

    /// <summary>
    /// Occurs when Dispose is called.
    /// </summary>
    event EventHandler<EventArgs> Disposing;
}