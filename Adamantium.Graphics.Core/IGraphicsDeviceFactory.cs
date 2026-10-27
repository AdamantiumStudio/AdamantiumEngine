using Adamantium.Graphics.Core.Presentation;

namespace Adamantium.Graphics.Core;

public interface IGraphicsDeviceFactory
{
    IGraphicsDevice Create(MainGraphicsDevice mainDevice, GraphicsDeviceType deviceType);

    /// <summary>The heap or the descriptor sets for <paramref name="model"/>, which MainGraphicsDevice owns.</summary>
    IDescriptorManager CreateDescriptorManager(IGraphicsDevice device, DescriptorModel model);

    // Same bridge for the shared device-memory allocator (one per logical device, owned by MainGraphicsDevice).
    IDeviceMemoryAllocator CreateMemoryAllocator(IGraphicsDevice device);

    /// <summary>The shaders of effect passes for <paramref name="model"/>, which MainGraphicsDevice owns.</summary>
    IEffectPassShaderCache CreateEffectPassShaderCache(IGraphicsDevice device, DescriptorModel model);
}