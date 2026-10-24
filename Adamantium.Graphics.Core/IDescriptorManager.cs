using System;

namespace Adamantium.Graphics.Core;

/// <summary>Where the shaders of one logical device find their resources: the descriptor heap or descriptor sets, as
/// <see cref="MainGraphicsDevice.DescriptorModel"/> chose. One per logical device, owned by
/// <see cref="MainGraphicsDevice"/> and shared by its render devices.</summary>
public interface IDescriptorManager : IDisposable
{
    /// <summary>Where <paramref name="texture"/> is in a shader's texture array of no length: what that array is indexed
    /// by. Takes the texture a slot if it has none yet; the slot stays the texture's until it is disposed.</summary>
    uint TextureIndex(ITexture texture);

    /// <summary>Binds what every draw on <paramref name="device"/>'s current command buffer reads from: the heaps. Nothing
    /// for descriptor sets, which each pass binds as it is applied.</summary>
    void BindTo(IGraphicsDevice device);
}
