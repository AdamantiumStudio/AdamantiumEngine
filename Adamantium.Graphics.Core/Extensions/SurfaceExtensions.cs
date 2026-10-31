using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Core.Extensions;

public static class SurfaceExtensions
{
    /// <summary>Whether the surface can be composed with its alpha by <paramref name="device"/>: it offers
    /// pre-multiplied composite alpha there.</summary>
    public static bool ComposesWithAlpha(this SurfaceKHR surface, PhysicalDevice device)
    {
        return device.GetPhysicalDeviceSurfaceCapabilitiesKHR(surface).SupportedCompositeAlpha
            .HasFlag(CompositeAlphaFlagBitsKHR.PreMultipliedBitKhr);
    }
}
