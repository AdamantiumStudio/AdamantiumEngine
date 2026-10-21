using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.Core;
using Adamantium.Core.Collections;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Vulkan.Core;
using Adamantium.Vulkan.Core.Interop;
using Adamantium.Vulkan.Linux;
using Adamantium.Vulkan.Loader;
using Adamantium.Vulkan.MacOS;
using Adamantium.Vulkan.Windows;
using Serilog;

namespace Adamantium.Graphics.Core;

public unsafe class VulkanInstance : DisposableBase
{
    private const string EngineName = "AdamantiumEngine";
        
    private delegate* unmanaged<DebugUtilsMessageSeverityFlagBitsEXT, DebugUtilsMessageTypeFlagBitsEXT, VkDebugUtilsMessengerCallbackDataEXT*, void*, VkBool32> debugCallback;
    private DebugUtilsMessengerEXT debugMessenger;
        
    internal Instance VkInstance { get; private set; }

    public string ApplicationName { get; set; }

    public bool IsInDebugMode { get; set; }

    public static ReadOnlyCollection<string> ValidationLayers { get; private set; }

    /// <summary>Instance extensions the engine cannot run without.</summary>
    public static ReadOnlyCollection<string> InstanceExtensions { get; private set; }

    /// <summary>Instance extensions enabled when the loader has them, never required.</summary>
    public static ReadOnlyCollection<string> OptionalInstanceExtensions { get; private set; }

    /// <summary>Instance extensions this instance actually enabled.</summary>
    public IReadOnlySet<string> EnabledInstanceExtensions { get; private set; } = new HashSet<string>();

    public bool IsExtensionEnabled(string extension) => EnabledInstanceExtensions.Contains(extension);

    private readonly Dictionary<IntPtr, SurfaceKHR> availableSurfaces;

    // How many presenters hold each surface: a headless one is shared by every headless presenter, all under handle 0.
    private readonly Dictionary<IntPtr, int> surfaceHolders = new();

    static VulkanInstance()
    {
        // The Vulkan loader by the name each platform installs it under - vulkan-1 is only Windows' - before the first
        // call reaches it. Here rather than in an application, so the engine finds Vulkan with or without one around it.
        VulkanDllMap.Register();

        var validationLayers = new List<string>();

        validationLayers.Add("VK_LAYER_KHRONOS_validation");
        //validationLayers.Add("VK_LAYER_LUNARG_monitor");
        ValidationLayers = new ReadOnlyCollection<string>(validationLayers);

        var instanceExt = new List<string> { Constants.VK_KHR_SURFACE_EXTENSION_NAME };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            instanceExt.Add(Constants.VK_KHR_WIN32_SURFACE_EXTENSION_NAME);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            instanceExt.Add(Constants.VK_MVK_MACOS_SURFACE_EXTENSION_NAME);
        }
        InstanceExtensions = new ReadOnlyCollection<string>(instanceExt);

        var optionalExt = new List<string>
        {
            Constants.VK_EXT_DEBUG_UTILS_EXTENSION_NAME,
            Constants.VK_EXT_HEADLESS_SURFACE_EXTENSION_NAME,
            Constants.VK_KHR_GET_SURFACE_CAPABILITIES_2_EXTENSION_NAME,
            Constants.VK_KHR_SURFACE_MAINTENANCE_1_EXTENSION_NAME,
            Constants.VK_EXT_SURFACE_MAINTENANCE_1_EXTENSION_NAME,
        };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Optional, unlike the other platforms' surfaces: a Linux machine may have no Wayland - a server, a CI
            // runner - and still render off-screen. A window asked for without it is refused when its surface is made.
            optionalExt.Add(Constants.VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME);
        }
        OptionalInstanceExtensions = new ReadOnlyCollection<string>(optionalExt);
    }

    public AdamantiumCollection<GraphicsAdapter> GraphicsAdapters { get; private set; }

    public GraphicsAdapter MainGraphicsAdapter { get; set; }

    private GraphicsAdapter FindBestSuitableAdapter()
    {
        var discreteAdapter = GraphicsAdapters.FirstOrDefault(x => x.DeviceType == PhysicalDeviceType.DiscreteGpu);
        if (discreteAdapter == null)
        {
            discreteAdapter =
                GraphicsAdapters.FirstOrDefault(x => x.DeviceType == PhysicalDeviceType.IntegratedGpu) ??
                GraphicsAdapters[0];
        }

        // Hybrid-graphics laptops expose both an integrated (Intel, tiny device-local heap shared with system RAM)
        // and a discrete GPU; pick the discrete one. Log the choice so an unexpected fallback to integrated (which
        // OOMs on modest allocations) is visible at a glance.
        foreach (var adapter in GraphicsAdapters)
        {
            Console.WriteLine($"[GPU] available: {adapter.AdapterProperties.DeviceName} ({adapter.DeviceType})");
        }
        Console.WriteLine($"[GPU] selected: {discreteAdapter.AdapterProperties.DeviceName} ({discreteAdapter.DeviceType})");

        // Dump memory heaps/types so we can see the host-visible-DEVICE_LOCAL window size: ~256 MB heap => no
        // Resizable BAR (the BAR window is the bottleneck); a heap ~= total VRAM with a DEVICE_LOCAL|HOST_VISIBLE
        // type pointing at it => ReBAR is on (CPU can write the whole VRAM directly, no staging needed).
        discreteAdapter.Adapter.GetPhysicalDeviceMemoryProperties(out var memProps);
        for (uint h = 0; h < memProps.MemoryHeapCount; h++)
        {
            var heap = memProps.MemoryHeaps.Span[(int)h];
            Console.WriteLine($"[MEM] heap {h}: {(ulong)heap.Size / (1024 * 1024)} MB flags={(MemoryHeapFlagBits)heap.Flags}");
        }
        for (uint t = 0; t < memProps.MemoryTypeCount; t++)
        {
            var mt = memProps.MemoryTypes.Span[(int)t];
            Console.WriteLine($"[MEM] type {t}: heap {mt.HeapIndex} {(MemoryPropertyFlags)mt.PropertyFlags}");
        }

        return discreteAdapter;
    }

    private VulkanInstance(string appName, bool enableDebug)
    {
        availableSurfaces = new Dictionary<IntPtr, SurfaceKHR>();
        debugCallback = &DebugCallback;
        ApplicationName = appName;
        IsInDebugMode = enableDebug;
        CreateInstance(appName);
        GraphicsAdapters = new AdamantiumCollection<GraphicsAdapter>();
        EnumerateGraphicAdapters();
        MainGraphicsAdapter = FindBestSuitableAdapter();
    }
        
    private void CreateInstance(string appName)
    {
        var appInfo = new ApplicationInfo();
        appInfo.PApplicationName = appName;
        appInfo.ApplicationVersion = Constants.VK_MAKE_API_VERSION(1, 0, 0, 0);
        appInfo.PEngineName = EngineName;
        appInfo.EngineVersion = Constants.VK_MAKE_API_VERSION(1, 0, 0, 0);
        appInfo.ApiVersion = Constants.VK_MAKE_API_VERSION(1, 4, 309, 0);

        var createInfo = new InstanceCreateInfo();
        createInfo.PApplicationInfo = appInfo;

        var layersAvailable = Instance.EnumerateInstanceLayerProperties();
        var available = Instance.EnumerateInstanceExtensionProperties().Select(x => x.ExtensionName).ToHashSet();

        var missing = InstanceExtensions.Where(x => !available.Contains(x)).ToArray();
        if (missing.Length > 0)
        {
            throw new ExtensionNotSupportedException("Vulkan loader", missing);
        }

        var enabled = InstanceExtensions.Concat(OptionalInstanceExtensions.Where(available.Contains)).ToArray();
        EnabledInstanceExtensions = new HashSet<string>(enabled, StringComparer.Ordinal);
        createInfo.PEnabledExtensionNames = enabled;
        createInfo.EnabledExtensionCount = (uint)enabled.Length;

        if (IsInDebugMode)
        {
            // Only enable validation layers that are actually installed - asking for a missing layer makes
            // Instance.Create fail outright, which would take down the whole (e.g. designer) host instead of just
            // running without validation.
            var availableLayers = new HashSet<string>(layersAvailable.Select(x => x.LayerName));
            var enabledLayers = ValidationLayers.Where(availableLayers.Contains).ToArray();
            createInfo.EnabledLayerCount = (uint)enabledLayers.Length;
            createInfo.PEnabledLayerNames = enabledLayers;
            if (enabledLayers.Length == 0)
                Console.Error.WriteLine("[vk] graphics debug requested but no validation layers are installed (Vulkan SDK?) - running without them");
        }

        VkInstance = Instance.Create(createInfo);
        NativePointer = new IntPtr(VkInstance.NativePointer);

        if (IsInDebugMode && IsExtensionEnabled(Constants.VK_EXT_DEBUG_UTILS_EXTENSION_NAME))
        {
            EnableDebug();
        }
    }

    private void EnableDebug()
    {
        DebugUtilsMessengerCreateInfoEXT debugInfo = new DebugUtilsMessengerCreateInfoEXT();
        debugInfo.MessageSeverity = (DebugUtilsMessageSeverityFlagBitsEXT.InfoBitExt |
                                     DebugUtilsMessageSeverityFlagBitsEXT.WarningBitExt |
                                     DebugUtilsMessageSeverityFlagBitsEXT.ErrorBitExt);
        debugInfo.MessageType = (DebugUtilsMessageTypeFlagBitsEXT.GeneralBitExt |
                                 DebugUtilsMessageTypeFlagBitsEXT.ValidationBitExt |
                                 DebugUtilsMessageTypeFlagBitsEXT.PerformanceBitExt);
        debugInfo.PfnUserCallback = debugCallback;
        CreateDebugUtilsMessenger(debugInfo, out debugMessenger);
    }

    public IntPtr NativePointer { get; private set; }

    public void EnumerateGraphicAdapters()
    {
        var devices = VkInstance.EnumeratePhysicalDevices();
        GraphicsAdapters.Clear();
        foreach (var physicalDevice in devices)
        {
            GraphicsAdapters.Add(new GraphicsAdapter(physicalDevice, this));
        }
    }

    public static VulkanInstance Create(string appName, bool enableDebug)
    {
        return new VulkanInstance(appName, enableDebug);
    }

    /// <summary>The surface for the output's window, made the first time it is asked for. Each call holds it once more,
    /// and <see cref="ReleaseSurface"/> lets go of it.</summary>
    public SurfaceKHR GetOrCreateSurface(PresentationParameters parameters)
    {
        // Presenters are made and disposed on more than one thread: the render thread, a universe's own.
        lock (surfaceHolders)
        {
            var surface = SurfaceFor(parameters);
            surfaceHolders[parameters.OutputHandle] = surfaceHolders.GetValueOrDefault(parameters.OutputHandle) + 1;
            return surface;
        }
    }

    /// <summary>
    /// Lets go of the surface held for this window, and destroys it when nothing holds it any more - its swapchain must
    /// be gone by then. A surface kept past its window is kept on a handle that may come back for another: a wl_surface is
    /// memory libwayland frees, and the next window's can be at the same address, which then got the old surface back,
    /// made for a window that no longer existed.
    /// </summary>
    public void ReleaseSurface(IntPtr outputHandle)
    {
        lock (surfaceHolders)
        {
            if (!surfaceHolders.TryGetValue(outputHandle, out var holders))
            {
                return;
            }

            if (holders > 1)
            {
                surfaceHolders[outputHandle] = holders - 1;
                return;
            }

            surfaceHolders.Remove(outputHandle);
            if (availableSurfaces.Remove(outputHandle, out var surface))
            {
                VkInstance.DestroySurfaceKHR(surface);
            }
        }
    }

    private SurfaceKHR SurfaceFor(PresentationParameters parameters)
    {
        if (availableSurfaces.TryGetValue(parameters.OutputHandle, out var createSurface))
        {
            return createSurface;
        }

        // Headless: a window-less surface (VK_EXT_headless_surface) with a normal swapchain on top, so the
        // rest of the presentation path is unchanged. The extension isn't present everywhere - guard it so a
        // missing one is a clean managed error instead of a native null-function-pointer crash.
        if (parameters.PresenterType == PresenterType.Headless)
        {
            if (!IsExtensionEnabled(Constants.VK_EXT_HEADLESS_SURFACE_EXTENSION_NAME))
            {
                throw new NotSupportedException(
                    $"{Constants.VK_EXT_HEADLESS_SURFACE_EXTENSION_NAME} is not available on this Vulkan loader. " +
                    "Use PresenterType.RenderTarget for off-screen rendering instead.");
            }

            var headlessSurface = VkInstance.CreateHeadlessSurface(new HeadlessSurfaceCreateInfoEXT());
            availableSurfaces.Add(parameters.OutputHandle, headlessSurface);
            return headlessSurface;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var surfaceInfo = new Win32SurfaceCreateInfoKHR();
            surfaceInfo.Hwnd = parameters.OutputHandle;
            surfaceInfo.Hinstance = parameters.HInstanceHandle;
            var surface = VkInstance.CreateWin32Surface(surfaceInfo);

            availableSurfaces.Add(parameters.OutputHandle, surface);

            return surface;
        }
            
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var surfaceInfo = new MacOSSurfaceCreateInfoMVK();
            surfaceInfo.PView = (nuint)parameters.OutputHandle;
            var surface = VkInstance.CreateMacOSSurfaceMVK(surfaceInfo);

            availableSurfaces.Add(parameters.OutputHandle, surface);

            return surface;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (!IsExtensionEnabled(Constants.VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME))
            {
                throw new NotSupportedException(
                    $"{Constants.VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME} is not available on this Vulkan loader, so no " +
                    "window can be presented to. Use PresenterType.Headless or RenderTarget for off-screen rendering.");
            }

            if (parameters.DisplayHandle == IntPtr.Zero)
            {
                throw new ArgumentException(
                    "A Wayland surface needs the wl_display as DisplayHandle, besides the wl_surface as OutputHandle.",
                    nameof(parameters));
            }

            var surfaceInfo = new WaylandSurfaceCreateInfoKHR();
            surfaceInfo.Display = parameters.DisplayHandle;
            surfaceInfo.Surface = parameters.OutputHandle;
            var surface = VkInstance.CreateWaylandSurface(surfaceInfo);

            availableSurfaces.Add(parameters.OutputHandle, surface);

            return surface;
        }

        throw new NotSupportedException("Current platform is not supported yet for Surface creation");
    }
        
    private Result CreateDebugUtilsMessenger(DebugUtilsMessengerCreateInfoEXT pCreateInfo, out DebugUtilsMessengerEXT pDebugMessenger)
    {
        pDebugMessenger = null;
        var result = VkInstance.CreateDebugUtilsMessengerEXT(pCreateInfo, null, out pDebugMessenger);
        // var ptr = VkInstance.GetInstanceProcAddr("vkCreateDebugUtilsMessengerEXT");
        // var func = new PFN_vkCreateDebugUtilsMessengerEXT(ptr);
        // var infoPtr = NativeUtils.StructOrEnumToPointer(pCreateInfo.ToNative());
        // var result = func.Invoke(VkInstance, infoPtr, null, out var pDebugMessenger_t);
        // NativeUtils.Free(infoPtr);
        // pDebugMessenger = new DebugUtilsMessengerEXT(pDebugMessenger_t);
        return result;
    }

    private void DestroyDebugUtilsMessenger(DebugUtilsMessengerEXT debugMessenger)
    {
        var ptr = VkInstance.GetInstanceProcAddr("vkDestroyDebugUtilsMessengerEXT");
        var func = new PFN_vkDestroyDebugUtilsMessengerEXT(ptr);
        func.Invoke(VkInstance, debugMessenger, null);
    }

    // The most recent validation/error/warning messages from the layers, kept so a later failure (e.g. a
    // device-lost surfacing only as ErrorDeviceLost at the next WaitForFences) can report the REAL Vulkan cause
    // instead of a guess. Bounded ring buffer; thread-safe (the callback fires on arbitrary threads).
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _recentValidation = new();

    /// <summary>A snapshot of the recent validation-layer error/warning messages (empty when validation is off).</summary>
    public static IReadOnlyList<string> RecentValidationMessages => _recentValidation.ToArray();

    [UnmanagedCallersOnly]
    private static VkBool32 DebugCallback(DebugUtilsMessageSeverityFlagBitsEXT messageSeverity, DebugUtilsMessageTypeFlagBitsEXT messageTypes, VkDebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
    {
        try
        {
            var message = new string((*pCallbackData).pMessage);
            Log.Logger.Debug(message);

            // Errors/warnings are the actionable ones: echo them to the console (the designer host redirects it into
            // its per-PID log) and remember them so a follow-up device error can quote the real validation message.
            var isError = (messageSeverity & DebugUtilsMessageSeverityFlagBitsEXT.ErrorBitExt) != 0;
            var isWarning = (messageSeverity & DebugUtilsMessageSeverityFlagBitsEXT.WarningBitExt) != 0;
            if (isError || isWarning)
            {
                Console.Error.WriteLine($"[vk-{(isError ? "error" : "warn")}] {message}");
                _recentValidation.Enqueue(message);
                while (_recentValidation.Count > 30) _recentValidation.TryDequeue(out _);
            }
        }
        catch { /* a diagnostic callback must never throw back into the driver */ }
        return 0;
    }

    public static implicit operator Instance(VulkanInstance vkInstance)
    {
        return vkInstance.VkInstance;
    }

    protected override void Dispose(bool disposeManaged)
    {
        foreach (var surface in availableSurfaces)
        {
            VkInstance?.DestroySurfaceKHR(surface.Value);
        }

        if (debugMessenger != null)
        {
            DestroyDebugUtilsMessenger(debugMessenger);
        }

        VkInstance?.Dispose();
        VkInstance = null;
        GraphicsAdapters.Clear();
    }
}