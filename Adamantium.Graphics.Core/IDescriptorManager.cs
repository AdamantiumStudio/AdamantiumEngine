using System;

namespace Adamantium.Graphics.Core;

/// <summary>The descriptor heap or the descriptor sets of one logical device, shared by its render devices.</summary>
public interface IDescriptorManager : IDisposable
{
    /// <summary>Where <paramref name="texture"/> is in a shader's texture array of no length; kept until it is
    /// disposed.</summary>
    uint TextureIndex(ITexture texture);

    /// <summary>Binds what every draw on <paramref name="device"/>'s command buffer reads: the heaps; nothing for
    /// sets.</summary>
    void BindTo(IGraphicsDevice device);
}
