using System;
using System.Linq;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Core;

public unsafe class GraphicsAdapter
{
    private PhysicalDevice _physicalDevice;
    private VulkanInstance _vkInstance;

    public GraphicsAdapter(PhysicalDevice device, VulkanInstance vkInstance)
    {
        _physicalDevice = device;
        _vkInstance = vkInstance;
        Adapter = device;
        device.GetPhysicalDeviceProperties(out var properties);
        AdapterProperties = properties;
        UpdateData();
    }

    public void UpdateData()
    {
        // ONE call per kind, with everything wanted chained onto it - which is how Vulkan is meant to be asked. This
        // used to be a call PER STRUCTURE, each one hand-marshaled into unmanaged memory and read back with a raw
        // cast, because the binding handed an output chain back as a bare address and dropped the typed objects. It
        // marshals them into the objects the caller supplied now, so the chain can simply be built and read.
        var vulkan12Properties = new PhysicalDeviceVulkan12Properties();
        var vulkan14Properties = new PhysicalDeviceVulkan14Properties { PNext = vulkan12Properties };
        var heapProperties = new PhysicalDeviceDescriptorHeapPropertiesEXT { PNext = vulkan14Properties };
        var properties2 = new PhysicalDeviceProperties2
        {
            Properties = new PhysicalDeviceProperties(),
            PNext = heapProperties
        };

        _physicalDevice.GetPhysicalDeviceProperties2(ref properties2);

        AdapterProperties = properties2.Properties;
        DeviceHeapProperties = heapProperties;
        MaxPushDescriptors = vulkan14Properties.MaxPushDescriptors;
        Vulkan12Properties = vulkan12Properties;

        // swapchainMaintenance1 is a feature, not just an extension: its structures are invalid with the feature off, so it
        // is queried here and enabled at device creation.
        var maintenance7 = new PhysicalDeviceMaintenance7FeaturesKHR();
        var maintenance = new PhysicalDeviceSwapchainMaintenance1FeaturesKHR { PNext = maintenance7 };
        var hostCopy = new PhysicalDeviceVulkan14Features { PNext = maintenance };
        var heapFeatures = new PhysicalDeviceDescriptorHeapFeaturesEXT { PNext = hostCopy };
        var layout12 = new PhysicalDeviceVulkan12Features();
        heapFeatures.PNext = layout12;
        layout12.PNext = hostCopy;
        var features2 = new PhysicalDeviceFeatures2 { PNext = heapFeatures };

        _physicalDevice.GetPhysicalDeviceFeatures2(ref features2);

        SupportsHostImageCopy = hostCopy.HostImageCopy;
        SupportsSwapchainMaintenance1 = maintenance.SwapchainMaintenance1;
        SupportsMaintenance7 = maintenance7.Maintenance7;
        Supports8BitStorage = layout12.StorageBuffer8BitAccess && layout12.ShaderInt8;
        SupportsDescriptorHeap = heapFeatures.DescriptorHeap && HasExtension(Constants.VK_EXT_DESCRIPTOR_HEAP_EXTENSION_NAME);

        Console.WriteLine($"DescriptorHeap supported={SupportsDescriptorHeap}");
        Console.WriteLine($"HostImageCopy supported={SupportsHostImageCopy}");
        Console.WriteLine($"SwapchainMaintenance1 supported={SupportsSwapchainMaintenance1}");
        Console.WriteLine($"8-bit storage supported={Supports8BitStorage} " +
                          $"(storageBuffer8BitAccess={(bool)layout12.StorageBuffer8BitAccess}, shaderInt8={(bool)layout12.ShaderInt8})");
    }

    private bool HasExtension(string name)
    {
        uint count = 0;
        _physicalDevice.EnumerateDeviceExtensionProperties(null, ref count, null);
        var extensions = new ExtensionProperties[count];
        _physicalDevice.EnumerateDeviceExtensionProperties(null, ref count, extensions);
        return extensions.Any(extension => extension.ExtensionName == name);
    }

    public PhysicalDeviceProperties AdapterProperties { get; private set; }

    public PhysicalDeviceDescriptorHeapPropertiesEXT DeviceHeapProperties { get; private set; }

    /// <summary>How many descriptors one push may carry: the most a pass can have on descriptor sets.</summary>
    public uint MaxPushDescriptors { get; private set; }

    /// <summary>The device's Vulkan 1.2 properties: among them the limits of update-after-bind descriptor sets.</summary>
    public PhysicalDeviceVulkan12Properties Vulkan12Properties { get; private set; }

    public bool SupportsHostImageCopy { get; private set; }

    /// <summary>Whether a storage buffer may hold 8-bit members and a shader may compute with them - the pair the
    /// instance records need to carry color as four BYTES rather than as a packed word.</summary>
    public bool Supports8BitStorage { get; private set; }

    /// <summary>Whether the device supports the swapchainMaintenance1 FEATURE, not merely the extension.</summary>
    public bool SupportsSwapchainMaintenance1 { get; private set; }

    /// <summary>Whether the device supports the maintenance7 FEATURE, which lets a render pass mix inline draws with
    /// executed secondary command buffers.</summary>
    public bool SupportsMaintenance7 { get; private set; }

    /// <summary>Whether the device has the descriptor heap (VK_EXT_descriptor_heap), extension and feature: the one way
    /// to resources besides descriptor sets, see <see cref="DescriptorModel"/>.</summary>
    public bool SupportsDescriptorHeap { get; private set; }

    public PhysicalDeviceType DeviceType => AdapterProperties.DeviceType;
    
    public PhysicalDevice Adapter { get; }
    
    public static implicit operator PhysicalDevice(GraphicsAdapter adapter)
    {
        return adapter._physicalDevice;
    }
}