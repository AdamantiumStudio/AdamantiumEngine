using Adamantium.Graphics.Core.Presentation;

namespace Adamantium.Graphics.Core;

public interface IGraphicsDeviceFactory
{
    IGraphicsDevice Create(MainGraphicsDevice mainDevice, GraphicsDeviceType deviceType);

    // Bridges Graphics.Core -> Graphics: lets MainGraphicsDevice (Core) own the heap or the descriptor sets without
    // referencing the impl.
    IDescriptorManager CreateDescriptorManager(IGraphicsDevice device, DescriptorModel model);

    // Same bridge for the shared device-memory allocator (one per logical device, owned by MainGraphicsDevice).
    IDeviceMemoryAllocator CreateMemoryAllocator(IGraphicsDevice device);
}