using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Adamantium.Vulkan.Core;
using Adamantium.Vulkan.Core.Interop;

namespace Adamantium.Graphics.Core;

/// <summary>KosmicKrisp, the Vulkan driver of Apple's platforms, loaded by the engine itself and handed to the Vulkan
/// loader as its only driver (VK_LUNARG_direct_driver_loading), so no other driver installed beside it takes part.</summary>
internal sealed unsafe class KosmicKrispDriver
{
    private const string ManifestName = "libkosmickrisp_icd.json";

    private readonly delegate* unmanaged<VkInstance_T, sbyte*, nuint> _getInstanceProcAddr;

    private KosmicKrispDriver(IntPtr library)
    {
        _getInstanceProcAddr = (delegate* unmanaged<VkInstance_T, sbyte*, nuint>)NativeLibrary.GetExport(library,
            "vk_icdGetInstanceProcAddr");
        InstanceExtensions = ReadInstanceExtensions();
    }

    /// <summary>The instance extensions of the driver itself, without those of other drivers the loader lists.</summary>
    internal IReadOnlySet<string> InstanceExtensions { get; }

    /// <summary>The <see cref="InstanceCreateInfo"/> chain entry that makes this driver the loader's only one.</summary>
    internal DirectDriverLoadingListLUNARG CreateLoadingList()
    {
        DirectDriverLoadingInfoLUNARG[] drivers = [new DirectDriverLoadingInfoLUNARG { PfnGetInstanceProcAddr = _getInstanceProcAddr }];

        return new DirectDriverLoadingListLUNARG
        {
            Mode = DirectDriverLoadingModeLUNARG.ExclusiveLunarg,
            DriverCount = (uint)drivers.Length,
            PDrivers = drivers
        };
    }

    /// <summary>Loads the driver its manifest names: beside the application first (vulkan/icd.d), then the Vulkan SDK and
    /// the system folders it and Homebrew install to.</summary>
    internal static KosmicKrispDriver Load()
    {
        foreach (var directory in GetManifestDirectories())
        {
            var manifest = Path.Combine(directory, ManifestName);
            if (File.Exists(manifest))
                return new KosmicKrispDriver(NativeLibrary.Load(ReadLibraryPath(manifest)));
        }

        throw new NotSupportedException(
            "KosmicKrisp, the Vulkan driver for Apple's platforms, is not installed. Install the Vulkan SDK, or ship the " +
            $"driver and its {ManifestName} in vulkan/icd.d beside the application");
    }

    private static IEnumerable<string> GetManifestDirectories()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "vulkan", "icd.d");

        var sdk = Environment.GetEnvironmentVariable("VULKAN_SDK");
        if (!string.IsNullOrEmpty(sdk))
            yield return Path.Combine(sdk, "share", "vulkan", "icd.d");

        yield return "/usr/local/share/vulkan/icd.d";
        yield return "/opt/homebrew/share/vulkan/icd.d";
    }

    private static string ReadLibraryPath(string manifest)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var library = document.RootElement.GetProperty("ICD").GetProperty("library_path").GetString();

        return library.Contains('/') && !Path.IsPathRooted(library)
            ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest), library))
            : library;
    }

    private IReadOnlySet<string> ReadInstanceExtensions()
    {
        var enumerate = (delegate* unmanaged<sbyte*, uint*, VkExtensionProperties*, Result>)GetProcAddress(
            "vkEnumerateInstanceExtensionProperties");

        uint count = 0;
        ResultHelper.CheckResult(enumerate(null, &count, null), "vkEnumerateInstanceExtensionProperties");

        var properties = new VkExtensionProperties[count];
        fixed (VkExtensionProperties* pointer = properties)
        {
            ResultHelper.CheckResult(enumerate(null, &count, pointer), "vkEnumerateInstanceExtensionProperties");
        }

        return properties.Take((int)count).Select(native => new ExtensionProperties(in native).ExtensionName)
            .ToHashSet(StringComparer.Ordinal);
    }

    private nuint GetProcAddress(string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name + '\0');
        fixed (byte* pointer = bytes)
        {
            return _getInstanceProcAddr(default, (sbyte*)pointer);
        }
    }
}
