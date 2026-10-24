using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Core;

/// <summary>How the engine sets Vulkan up: what it asks of the loader and of the device, the layers it runs with and how
/// shaders reach their resources. The application gives it; <see cref="CreateDefault"/> is what the engine needs on every
/// platform, to add to or change.</summary>
public sealed class GraphicsSettings
{
    /// <summary>The layer that gives shader objects to a device that has none of its own. It turns itself off on a device
    /// that has them.</summary>
    public const string ShaderObjectLayer = "VK_LAYER_KHRONOS_shader_object";

    /// <summary>Instance extensions the engine cannot run without.</summary>
    public List<string> InstanceExtensions { get; } = [];

    /// <summary>Instance extensions enabled when the loader has them.</summary>
    public List<string> OptionalInstanceExtensions { get; } = [];

    /// <summary>Instance layers enabled when they are installed or found in <see cref="LayerFolders"/>.</summary>
    public List<string> Layers { get; } = [];

    /// <summary>Layers enabled on top of <see cref="Layers"/> when graphics debugging is on, when installed.</summary>
    public List<string> DebugLayers { get; } = [];

    /// <summary>Folders of layers that come with the application, full paths: the loader looks there for this process
    /// before it creates the instance.</summary>
    public List<string> LayerFolders { get; } = [];

    /// <summary>Device extensions the engine cannot run without.</summary>
    public List<string> DeviceExtensions { get; } = [];

    /// <summary>Device extensions enabled when the device has them.</summary>
    public List<string> OptionalDeviceExtensions { get; } = [];

    /// <summary>How shaders reach their resources: <see cref="DescriptorModel.Auto"/> takes the heap where the device has
    /// one.</summary>
    public DescriptorModel DescriptorModel { get; set; } = DescriptorModel.Auto;

    /// <summary>How many frames the CPU may record ahead of the GPU before it waits for the oldest.</summary>
    public uint FramesInFlight { get; set; } = 3;

    /// <summary>The settings the engine needs on this platform: its surfaces, shader objects (from the layer beside the
    /// application where the device has none), the descriptor heap or push descriptors, and what it uses when there.</summary>
    public static GraphicsSettings CreateDefault()
    {
        var settings = new GraphicsSettings();
        settings.InstanceExtensions.Add(Constants.VK_KHR_SURFACE_EXTENSION_NAME);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            settings.InstanceExtensions.Add(Constants.VK_KHR_WIN32_SURFACE_EXTENSION_NAME);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            settings.InstanceExtensions.Add(Constants.VK_MVK_MACOS_SURFACE_EXTENSION_NAME);
        }

        settings.OptionalInstanceExtensions.AddRange(
        [
            Constants.VK_EXT_DEBUG_UTILS_EXTENSION_NAME,
            Constants.VK_EXT_HEADLESS_SURFACE_EXTENSION_NAME,
            Constants.VK_KHR_GET_SURFACE_CAPABILITIES_2_EXTENSION_NAME,
            Constants.VK_KHR_SURFACE_MAINTENANCE_1_EXTENSION_NAME,
            Constants.VK_EXT_SURFACE_MAINTENANCE_1_EXTENSION_NAME
        ]);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            settings.OptionalInstanceExtensions.Add(Constants.VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME);
        }

        settings.Layers.Add(ShaderObjectLayer);
        settings.DebugLayers.Add("VK_LAYER_KHRONOS_validation");
        settings.LayerFolders.Add(Path.Combine(AppContext.BaseDirectory, "vulkan", "layers", PlatformFolder()));

        settings.DeviceExtensions.AddRange(
        [
            Constants.VK_KHR_SWAPCHAIN_EXTENSION_NAME,
            Constants.VK_EXT_SHADER_OBJECT_EXTENSION_NAME
        ]);

        settings.OptionalDeviceExtensions.AddRange(
        [
            Constants.VK_EXT_DESCRIPTOR_HEAP_EXTENSION_NAME,
            Constants.VK_KHR_EXTERNAL_MEMORY_WIN32_EXTENSION_NAME,
            Constants.VK_KHR_EXTERNAL_SEMAPHORE_WIN32_EXTENSION_NAME,
            Constants.VK_KHR_EXTERNAL_MEMORY_FD_EXTENSION_NAME,
            Constants.VK_KHR_EXTERNAL_SEMAPHORE_FD_EXTENSION_NAME,
            Constants.VK_EXT_DEVICE_FAULT_EXTENSION_NAME,
            Constants.VK_KHR_FRAGMENT_SHADER_BARYCENTRIC_EXTENSION_NAME,
            Constants.VK_KHR_SWAPCHAIN_MAINTENANCE_1_EXTENSION_NAME,
            Constants.VK_EXT_SWAPCHAIN_MAINTENANCE_1_EXTENSION_NAME,
            Constants.VK_KHR_MAINTENANCE_7_EXTENSION_NAME
        ]);

        return settings;
    }

    private static string PlatformFolder()
    {
        var system = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
            : "linux";
        var architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        return $"{system}-{architecture}";
    }
}
