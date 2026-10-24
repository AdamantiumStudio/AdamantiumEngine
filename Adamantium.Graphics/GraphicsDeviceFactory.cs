using Adamantium.Graphics.Core;

namespace Adamantium.Graphics;

public class GraphicsDeviceFactory : IGraphicsDeviceFactory
{
    public IGraphicsDevice Create(MainGraphicsDevice mainDevice, GraphicsDeviceType deviceType)
    {
        return GraphicsDevice.Create(mainDevice, deviceType);
    }

    public IDescriptorManager CreateDescriptorManager(IGraphicsDevice device, DescriptorModel model)
    {
        return model == DescriptorModel.Sets ? new DescriptorSetManager(device) : new DescriptorHeapManager(device);
    }

    public IDeviceMemoryAllocator CreateMemoryAllocator(IGraphicsDevice device)
    {
        return new DeviceMemoryAllocator(device);
    }
}