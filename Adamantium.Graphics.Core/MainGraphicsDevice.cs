using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Adamantium.Core;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Vulkan.Core;
using Serilog;

namespace Adamantium.Graphics.Core;

public class MainGraphicsDevice : DisposableBase
{
    private uint _availableComputeQueueIndex;

    private int[] _graphicsQueueHolders;

    private object[] _graphicsQueueLocks;

    private readonly object _queueLeaseLock = new();

    private int _lost;

    private readonly List<IGraphicsDevice> graphicsDevices;

    private readonly Dictionary<Guid, IGraphicsDevice> deviceMap;

    public uint BuffersCount { get; }

    public bool EnableDynamicRendering { get; }

    /// <summary>True when VK_EXT_device_fault was enabled, so a device-lost can be queried for its real cause.</summary>
    public bool DeviceFaultSupported { get; private set; }

    /// <summary>True when VK_KHR_fragment_shader_barycentric was enabled, so a fragment shader can read where in its
    /// triangle it lies.</summary>
    public bool FragmentBarycentricSupported { get; private set; }

    /// <summary>True when the tessellationShader feature was enabled: passes may run hull and domain shaders, and every
    /// graphics pass binds those stages, empty when it has none.</summary>
    public bool IsTessellationEnabled { get; private set; }

    /// <summary>True when VK_EXT_mesh_shader's meshShader feature was enabled: passes may run mesh shaders, and every
    /// graphics pass binds the mesh stage, empty when it has none.</summary>
    public bool IsMeshShaderEnabled { get; private set; }

    /// <summary>True when VK_EXT_mesh_shader's taskShader feature was enabled, as <see cref="IsMeshShaderEnabled"/> is for
    /// the task stage.</summary>
    public bool IsTaskShaderEnabled { get; private set; }

    public VulkanInstance VulkanInstance { get; private set; }

    public GraphicsAdapter GraphicsAdapter { get; private set; }

    /// <summary>The resource-loader device, owned by the main device for its whole lifetime.</summary>
    public IGraphicsDevice ResourceLoaderDevice { get; private set; }

    /// <summary>Recompiles the effects of every device of this one when their sources change; see <see cref="EffectsFramework.ShaderHotReload"/>.</summary>
    public EffectsFramework.ShaderHotReload ShaderHotReload { get; } = new();

    // Deferred disposal for every GPU resource of this logical device, held until every drawing wrapper has retired
    // the frames in flight at hand-over: the wrappers share one VkDevice and share resources.
    private readonly List<(IDisposable Resource, IGraphicsDevice[] Devices, ulong[] Due)> _retired = [];
    private readonly object _retireSync = new();
    private readonly HashSet<IGraphicsDevice> _idle = [];

    /// <summary>Hand a GPU resource over for disposal once no submitted work can still reference it.</summary>
    public void RetireResource(IDisposable resource)
    {
        if (resource == null)
        {
            return;
        }

        lock (_retireSync)
        {
            var devices = graphicsDevices.ToArray();
            var due = new ulong[devices.Length];
            for (var i = 0; i < devices.Length; i++)
            {
                // Each begin waits on the fence of the frame MaxFramesInFlight back.
                due[i] = devices[i].FrameTicket + devices[i].MaxFramesInFlight + 1;
            }

            _retired.Add((resource, devices, due));
        }
    }

    /// <summary>Called by a drawing wrapper as it begins a frame, once its own fence wait has returned.</summary>
    public void OnFrameStarted()
    {
        List<IDisposable> due = null;
        lock (_retireSync)
        {
            if (_retired.Count == 0)
            {
                return;
            }

            _idle.Clear();
            foreach (var device in graphicsDevices)
            {
                if (device.HasNoFramesInFlight)
                {
                    _idle.Add(device);
                }
            }

            for (var i = _retired.Count - 1; i >= 0; i--)
            {
                if (!IsDue(_retired[i].Devices, _retired[i].Due))
                {
                    continue;
                }

                (due ??= []).Add(_retired[i].Resource);
                _retired.RemoveAt(i);
            }
        }

        if (due == null)
        {
            return;
        }

        foreach (var resource in due)
        {
            resource.Dispose();
        }
    }

    /// <summary>Disposes everything retired without waiting for frames; the caller must have waited the device idle.
    /// For teardown: what is retired after the last frame is otherwise never freed.</summary>
    public void FlushRetiredAfterIdle()
    {
        List<IDisposable> due;
        lock (_retireSync)
        {
            if (_retired.Count == 0)
            {
                return;
            }

            due = _retired.Select(r => r.Resource).ToList();
            _retired.Clear();
        }

        foreach (var resource in due)
        {
            resource.Dispose();
        }
    }

    // A wrapper that is gone, or one with no frame in flight, cannot be executing anything, so its vote is dropped.
    private bool IsDue(IGraphicsDevice[] devices, ulong[] due)
    {
        for (var i = 0; i < devices.Length; i++)
        {
            if (graphicsDevices.Contains(devices[i]) && !_idle.Contains(devices[i]) && devices[i].FrameTicket < due[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The descriptor heap or the descriptor sets, as <see cref="DescriptorModel"/> chose: one per logical device,
    /// shared by every render-device wrapper.</summary>
    public IDescriptorManager DescriptorManager { get; private set; }

    // Shared like the heap, so no render device grabs its own BAR block. Created on first use.
    private IDeviceMemoryAllocator _memoryAllocator;
    public IDeviceMemoryAllocator MemoryAllocator => _memoryAllocator ??= _graphicsDeviceFactory.CreateMemoryAllocator(ResourceLoaderDevice);

    public Device LogicalDevice { get; private set; }

    public uint AvailableQueuesCount { get; private set; }

    public QueueFamilyContainer QueueFamilyContainer { get; private set; }

    /// <summary>What the device was asked for: its extensions and how shaders reach their resources.</summary>
    public GraphicsSettings Settings { get; }

    /// <summary>How shaders reach their resources on this device: the heap, or descriptor sets where the device has no
    /// heap or the settings ask for sets.</summary>
    public DescriptorModel DescriptorModel { get; private set; }

    private static readonly Dictionary<string, string> OptionalExtensionInstancePrerequisites = new()
    {
        [Constants.VK_KHR_SWAPCHAIN_MAINTENANCE_1_EXTENSION_NAME] = Constants.VK_KHR_SURFACE_MAINTENANCE_1_EXTENSION_NAME,
        [Constants.VK_EXT_SWAPCHAIN_MAINTENANCE_1_EXTENSION_NAME] = Constants.VK_EXT_SURFACE_MAINTENANCE_1_EXTENSION_NAME,
    };

    /// <summary>Device extensions this device actually enabled: the required ones plus the supported optional ones.</summary>
    public IReadOnlySet<string> EnabledDeviceExtensions { get; private set; } = new HashSet<string>();

    public bool IsExtensionEnabled(string extension) => EnabledDeviceExtensions.Contains(extension);

    /// <summary>A present fence per present, present-mode changes without a rebuild, defined scaling on resize. The
    /// extension (KHR or EXT) and the feature both have to be there.</summary>
    public bool SupportsSwapchainMaintenance =>
        GraphicsAdapter.SupportsSwapchainMaintenance1
        && (IsExtensionEnabled(Constants.VK_KHR_SWAPCHAIN_MAINTENANCE_1_EXTENSION_NAME)
            || IsExtensionEnabled(Constants.VK_EXT_SWAPCHAIN_MAINTENANCE_1_EXTENSION_NAME));

    /// <summary>Whether a render pass can draw and execute recorded secondary command buffers in the same instance.</summary>
    public bool SupportsRecordedCommands =>
        GraphicsAdapter.SupportsMaintenance7 && IsExtensionEnabled(Constants.VK_KHR_MAINTENANCE_7_EXTENSION_NAME);

    internal Fence[] InFlightFences { get; private set; }

    public uint CurrentFrame { get; private set; }

    public IReadOnlyList<IGraphicsDevice> GraphicsDevices => graphicsDevices.AsReadOnly();

    private IGraphicsDeviceFactory _graphicsDeviceFactory;

    public bool IsInDebugMode
    {
        get => VulkanInstance.IsInDebugMode;
        set => VulkanInstance.IsInDebugMode = value;
    }

    private MainGraphicsDevice(IGraphicsDeviceFactory deviceFactory, GraphicsSettings settings, string name,
        bool enableDebug)
    {
        _graphicsDeviceFactory = deviceFactory;
        graphicsDevices = new List<IGraphicsDevice>();
        deviceMap = new Dictionary<Guid, IGraphicsDevice>();
        EnableDynamicRendering = true;
        Settings = settings;
        VulkanInstance = VulkanInstance.Create(name, enableDebug, settings);
        GraphicsAdapter = VulkanInstance.MainGraphicsAdapter;
        QueueFamilyContainer = GraphicsAdapter.FindQueueFamilies();
        BuffersCount = settings.FramesInFlight;
        CreateLogicalDevice();
        unsafe
        {
            if (LogicalDevice != null)
            {
                Log.Logger.Debug(
                    $"Main device created. Vulkan Instance addr: {VulkanInstance.NativePointer} LogicalDevice addr: {new IntPtr(LogicalDevice.NativePointer)}");
            }
        }

        ResourceLoaderDevice = CreateResourceLoaderDevice();
        DescriptorManager = _graphicsDeviceFactory.CreateDescriptorManager(ResourceLoaderDevice, DescriptorModel);
    }

    public void RemoveDevice(IGraphicsDevice device)
    {
        lock (_retireSync)
        {
            deviceMap.Remove(device.DeviceId);
            graphicsDevices.Remove(device);
        }

        device?.Dispose();
    }

    public void RemoveDeviceById(Guid deviceId)
    {
        IGraphicsDevice device;
        lock (_retireSync)
        {
            if (!deviceMap.TryGetValue(deviceId, out device)) return;

            deviceMap.Remove(deviceId);
            graphicsDevices.Remove(device);
        }

        device?.Dispose();
    }

    public IGraphicsDevice GetDeviceById(Guid deviceId)
    {
        return graphicsDevices.FirstOrDefault(x => x.DeviceId == deviceId);
    }

    public IGraphicsDevice UpdateDevice(Guid deviceId)
    {
        IGraphicsDevice device;
        lock (_retireSync)
        {
            if (!deviceMap.TryGetValue(deviceId, out device)) return null;

            deviceMap.Remove(deviceId);
            graphicsDevices.Remove(device);
        }

        device?.Dispose();
        return CreateRenderDevice();
    }

    private void CreateLogicalDevice()
    {
        var queueFamilies = GraphicsAdapter.Adapter.GetQueueFamilyProperties();

        for (int i = 0; i < queueFamilies.Length; ++i)
        {
            Console.WriteLine(
                $"Queue family {i}. QueueFlags: {queueFamilies[i].QueueFlags}. Queue count: {queueFamilies[i].QueueCount}");
        }

        var graphicsQueues = queueFamilies.FirstOrDefault(x => x.QueueFlags.HasFlag(QueueFlagBits.GraphicsBit));
        AvailableQueuesCount = graphicsQueues?.QueueCount ?? 0;
        var computeQueues = queueFamilies.FirstOrDefault(x => x.QueueFlags.HasFlag(QueueFlagBits.ComputeBit));
        var computeQueuesCount = computeQueues?.QueueCount ?? 0;

        Console.WriteLine($"{AvailableQueuesCount} queues available for graphics");
        Console.WriteLine($"{computeQueuesCount} queues available for compute");
        var queuePriorities = new float[AvailableQueuesCount];
        _graphicsQueueHolders = new int[AvailableQueuesCount];
        _graphicsQueueLocks = new object[AvailableQueuesCount];
        for (var i = 0; i < queuePriorities.Length; i++)
        {
            queuePriorities[i] = 1.0f;
            _graphicsQueueLocks[i] = new object();
        }

        var graphicsFamily = QueueFamilyContainer.GetFamilyInfo(QueueFlagBits.GraphicsBit);

        var queueInfos = new List<DeviceQueueCreateInfo>();
        var queueCreateInfo = new DeviceQueueCreateInfo();
        queueCreateInfo.QueueFamilyIndex = graphicsFamily.FamilyIndex;
        queueCreateInfo.QueueCount = AvailableQueuesCount;
        queueCreateInfo.PQueuePriorities = queuePriorities;
        queueInfos.Add(queueCreateInfo);

        var apiVersion = GraphicsAdapter.AdapterProperties.ApiVersion;
        if (apiVersion < Constants.VK_MAKE_API_VERSION(0, 1, 4, 0))
        {
            throw new NotSupportedException(
                $"{GraphicsAdapter.AdapterProperties.DeviceName} supports Vulkan {apiVersion >> 22}.{(apiVersion >> 12) & 0x3FF}; " +
                "the engine needs Vulkan 1.4. Choose another adapter or update drivers");
        }

        uint propCount = 0;
        GraphicsAdapter.Adapter.EnumerateDeviceExtensionProperties(null, ref propCount, null);
        var supportedDeviceExtensions = new ExtensionProperties[propCount];
        GraphicsAdapter.Adapter.EnumerateDeviceExtensionProperties(null, ref propCount, supportedDeviceExtensions);

        var availableDeviceExtensions = supportedDeviceExtensions.Select(x => x.ExtensionName).ToArray();
        var hasHeap = GraphicsAdapter.SupportsDescriptorHeap;
        DescriptorModel = Settings.DescriptorModel switch
        {
            DescriptorModel.Heap when !hasHeap => throw new ExtensionNotSupportedException(
                GraphicsAdapter.AdapterProperties.DeviceName, [Constants.VK_EXT_DESCRIPTOR_HEAP_EXTENSION_NAME]),
            DescriptorModel.Auto => hasHeap ? DescriptorModel.Heap : DescriptorModel.Sets,
            var asked => asked
        };

        var finalDeviceExtensions = new List<string>();
        foreach (var extension in Settings.DeviceExtensions.Distinct())
        {
            if (availableDeviceExtensions.Contains(extension))
            {
                finalDeviceExtensions.Add(extension);
            }
        }

        var missing = Settings.DeviceExtensions.Except(finalDeviceExtensions).ToArray();
        if (missing.Length > 0)
        {
            throw new ExtensionNotSupportedException(GraphicsAdapter.AdapterProperties.DeviceName, missing);
        }

        foreach (var extension in Settings.OptionalDeviceExtensions)
        {
            if (availableDeviceExtensions.Contains(extension) && !finalDeviceExtensions.Contains(extension)
                                                              && (!OptionalExtensionInstancePrerequisites.TryGetValue(extension, out var instanceExtension)
                                                                  || VulkanInstance.IsExtensionEnabled(instanceExtension)))
            {
                finalDeviceExtensions.Add(extension);
            }
        }

        if (GraphicsAdapter.SupportsMeshShaders && !finalDeviceExtensions.Contains(Constants.VK_EXT_MESH_SHADER_EXTENSION_NAME))
        {
            finalDeviceExtensions.Add(Constants.VK_EXT_MESH_SHADER_EXTENSION_NAME);
        }

        if (DescriptorModel == DescriptorModel.Sets)
        {
            finalDeviceExtensions.Remove(Constants.VK_EXT_DESCRIPTOR_HEAP_EXTENSION_NAME);
        }
        else if (!finalDeviceExtensions.Contains(Constants.VK_EXT_DESCRIPTOR_HEAP_EXTENSION_NAME))
        {
            finalDeviceExtensions.Add(Constants.VK_EXT_DESCRIPTOR_HEAP_EXTENSION_NAME);
        }

        EnabledDeviceExtensions = new HashSet<string>(finalDeviceExtensions, StringComparer.Ordinal);

        var vulkan11Features = new PhysicalDeviceVulkan11Features
        {
            ShaderDrawParameters = true,
        };

        var vulkan12Features = new PhysicalDeviceVulkan12Features
        {
            TimelineSemaphore = true,
            BufferDeviceAddress = true,
            SamplerMirrorClampToEdge = true,
            ScalarBlockLayout = true,
            StorageBuffer8BitAccess = true,
            ShaderInt8 = true,
            RuntimeDescriptorArray = true,
            ShaderSampledImageArrayNonUniformIndexing = true,
        };
        if (DescriptorModel == DescriptorModel.Sets)
        {
            vulkan12Features.DescriptorBindingSampledImageUpdateAfterBind = true;
            vulkan12Features.DescriptorBindingUpdateUnusedWhilePending = true;
            vulkan12Features.DescriptorBindingPartiallyBound = true;
            vulkan12Features.DescriptorBindingVariableDescriptorCount = true;
        }

        var vulkan13Features = new PhysicalDeviceVulkan13Features
        {
            Synchronization2 = true,
            DynamicRendering = true,
            ShaderDemoteToHelperInvocation = true,
        };

        var features14 = new PhysicalDeviceVulkan14Features
        {
            HostImageCopy = GraphicsAdapter.SupportsHostImageCopy,
            PushDescriptor = DescriptorModel == DescriptorModel.Sets
        };

        var deviceFeatures2 = GraphicsAdapter.Adapter.GetPhysicalDeviceFeatures2();

        deviceFeatures2.PNext = vulkan11Features;
        if (finalDeviceExtensions.Contains(Constants.VK_KHR_FRAGMENT_SHADER_BARYCENTRIC_EXTENSION_NAME))
        {
            deviceFeatures2.PNext = new PhysicalDeviceFragmentShaderBarycentricFeaturesKHR
            {
                FragmentShaderBarycentric = true,
                PNext = vulkan11Features
            };
            FragmentBarycentricSupported = true;
        }

        vulkan11Features.PNext = vulkan12Features;
        vulkan12Features.PNext = vulkan13Features;
        vulkan13Features.PNext = features14;
        if (SupportsRecordedCommands)
        {
            vulkan13Features.PNext = new PhysicalDeviceMaintenance7FeaturesKHR { Maintenance7 = true, PNext = features14 };
        }

        List<object> tail = [];
        if (EnableDynamicRendering && finalDeviceExtensions.Contains(Constants.VK_EXT_SHADER_OBJECT_EXTENSION_NAME))
        {
            tail.Add(new PhysicalDeviceShaderObjectFeaturesEXT { ShaderObject = true });
        }

        if (DescriptorModel == DescriptorModel.Heap)
        {
            tail.Add(new PhysicalDeviceDescriptorHeapFeaturesEXT { DescriptorHeap = true });
        }

        IsMeshShaderEnabled = GraphicsAdapter.SupportsMeshShaders;
        IsTaskShaderEnabled = GraphicsAdapter.SupportsTaskShaders;
        if (IsMeshShaderEnabled)
        {
            tail.Add(new PhysicalDeviceMeshShaderFeaturesEXT { MeshShader = true, TaskShader = IsTaskShaderEnabled });
        }

        if (finalDeviceExtensions.Contains(Constants.VK_EXT_DEVICE_FAULT_EXTENSION_NAME))
        {
            tail.Add(new PhysicalDeviceFaultFeaturesEXT { DeviceFault = true });
            DeviceFaultSupported = true;
        }

        if (SupportsSwapchainMaintenance)
        {
            tail.Add(new PhysicalDeviceSwapchainMaintenance1FeaturesKHR { SwapchainMaintenance1 = true });
        }

        object previous = features14;
        foreach (var next in tail)
        {
            Chain(previous, next);
            previous = next;
        }

        deviceFeatures2.Features.SamplerAnisotropy = true;
        deviceFeatures2.Features.ImageCubeArray = GraphicsAdapter.SupportsImageCubeArray;
        IsTessellationEnabled = GraphicsAdapter.SupportsTessellationShaders;
        deviceFeatures2.Features.TessellationShader = IsTessellationEnabled;
        // The stroke expander's compute shader dereferences BDA pointers (uint64_t).
        deviceFeatures2.Features.ShaderInt64 = true;

        var createInfo = new DeviceCreateInfo();
        createInfo.QueueCreateInfoCount = (uint)queueInfos.Count;
        createInfo.PQueueCreateInfos = queueInfos.ToArray();
        createInfo.EnabledExtensionCount = (uint)finalDeviceExtensions.Count;
        createInfo.PEnabledExtensionNames = finalDeviceExtensions.ToArray();
        createInfo.PNext = deviceFeatures2;

        LogicalDevice = GraphicsAdapter.Adapter.CreateDevice(createInfo);
        var fenceInfo = new FenceCreateInfo();
        fenceInfo.Flags = FenceCreateFlagBits.SignaledBit;
        InFlightFences ??= LogicalDevice.CreateFences(fenceInfo, BuffersCount);
    }

    private static void Chain(object feature, object next)
    {
        switch (feature)
        {
            case PhysicalDeviceVulkan14Features vulkan14:
                vulkan14.PNext = next;
                break;
            case PhysicalDeviceShaderObjectFeaturesEXT shaderObject:
                shaderObject.PNext = next;
                break;
            case PhysicalDeviceDescriptorHeapFeaturesEXT heap:
                heap.PNext = next;
                break;
            case PhysicalDeviceFaultFeaturesEXT fault:
                fault.PNext = next;
                break;
            case PhysicalDeviceMeshShaderFeaturesEXT meshShader:
                meshShader.PNext = next;
                break;
            default:
                throw new InvalidOperationException($"{feature.GetType().Name} is not chained before another feature.");
        }
    }

    /// <summary>True once the GPU has lost this device: nothing it is asked to do runs any more, and only a new device
    /// draws again.</summary>
    public bool IsLost => Volatile.Read(ref _lost) != 0;

    /// <summary>Raised once, on whichever thread first finds the device lost.</summary>
    public event Action Lost;

    /// <summary>Hands back <paramref name="result"/> of a call to this device, reporting the device lost the first time a
    /// call says so.</summary>
    public Result Observe(Result result)
    {
        if (result == Result.ErrorDeviceLost && Interlocked.Exchange(ref _lost, 1) == 0)
        {
            Log.Logger.Error("The GPU lost the device; a new one has to be made.");
            Lost?.Invoke();
        }

        return result;
    }

    /// <summary>Waits until the device has finished all its work. Vulkan has every queue of the device externally
    /// synchronized meanwhile, so each queue's lock is held, taken in queue order.</summary>
    public Result DeviceWaitIdle()
    {
        return LogicalDevice == null ? Result.Success : Observe(WaitIdleHoldingQueuesFrom(0));
    }

    private Result WaitIdleHoldingQueuesFrom(int index)
    {
        if (index == _graphicsQueueLocks.Length)
        {
            return LogicalDevice.DeviceWaitIdle();
        }

        lock (_graphicsQueueLocks[index])
        {
            return WaitIdleHoldingQueuesFrom(index + 1);
        }
    }

    public IGraphicsDevice CreateRenderDevice()
    {
        var renderDevice = _graphicsDeviceFactory.Create(this, GraphicsDeviceType.Rendering);
        lock (_retireSync)
        {
            deviceMap.Add(renderDevice.DeviceId, renderDevice);
            graphicsDevices.Add(renderDevice);
        }

        if (renderDevice is DisposableObject disposable)
        {
            disposable.Disposing += (_, _) => Unregister(renderDevice);
        }

        return renderDevice;
    }

    private void Unregister(IGraphicsDevice device)
    {
        lock (_retireSync)
        {
            deviceMap.Remove(device.DeviceId);
            graphicsDevices.Remove(device);
        }
    }

    private IGraphicsDevice CreateResourceLoaderDevice()
    {
        return _graphicsDeviceFactory.Create(this, GraphicsDeviceType.ResourceLoader);
    }

    public static MainGraphicsDevice Create(IGraphicsDeviceFactory deviceFactory, GraphicsSettings settings, string name,
        bool enableDebug)
    {
        return new(deviceFactory, settings, name, enableDebug);
    }

    /// <summary>Leases the graphics queue the fewest devices hold, so a queue is shared only once every one is taken.
    /// Vulkan leaves a queue's synchronization to its users: every submit and present on it goes under
    /// <paramref name="queueLock"/>. Give it back with <see cref="ReturnGraphicsQueue"/>.</summary>
    public Queue LeaseGraphicsQueue(out uint index, out object queueLock)
    {
        lock (_queueLeaseLock)
        {
            index = 0;
            for (var i = 1u; i < _graphicsQueueHolders.Length; i++)
            {
                if (_graphicsQueueHolders[i] < _graphicsQueueHolders[index])
                {
                    index = i;
                }
            }

            _graphicsQueueHolders[index]++;
        }

        queueLock = _graphicsQueueLocks[index];
        var graphicsFamily = QueueFamilyContainer.GetFamilyInfo(QueueFlagBits.GraphicsBit);
        return LogicalDevice.GetDeviceQueue(graphicsFamily.FamilyIndex, index);
    }

    /// <summary>Gives back a queue leased with <see cref="LeaseGraphicsQueue"/>.</summary>
    public void ReturnGraphicsQueue(uint index)
    {
        lock (_queueLeaseLock)
        {
            _graphicsQueueHolders[index]--;
        }
    }

    public Queue GetAvailableComputeQueue()
    {
        var computeFamily = QueueFamilyContainer.GetFamilyInfo(QueueFlagBits.ComputeBit);

        var queue = LogicalDevice.GetDeviceQueue(computeFamily.FamilyIndex, _availableComputeQueueIndex);
        _availableComputeQueueIndex++;
        if (_availableComputeQueueIndex >= computeFamily.Count)
        {
            _availableComputeQueueIndex = 0;
        }

        return queue;
    }

    public static implicit operator PhysicalDevice(MainGraphicsDevice device)
    {
        return device.GraphicsAdapter;
    }

    protected override void Dispose(bool disposeManaged)
    {
        Log.Logger.Debug("Start disposing main device");
        ShaderHotReload.Dispose();
        DeviceWaitIdle();
        FlushRetiredAfterIdle();
        // A copy: a device takes itself off this list as it is disposed.
        foreach (var device in graphicsDevices.ToArray())
        {
            device?.Dispose();
        }

        // What the devices retired as they went.
        FlushRetiredAfterIdle();
        lock (_retireSync)
        {
            graphicsDevices.Clear();
            deviceMap.Clear();
        }

        // The heap's buffers were allocated through the resource-loader device, so they go first.
        DescriptorManager?.Dispose();
        DescriptorManager = null;

        ResourceLoaderDevice?.Dispose();
        ResourceLoaderDevice = null;

        // After every device returned its sub-ranges, before the logical device goes.
        _memoryAllocator?.Dispose();
        _memoryAllocator = null;

        if (InFlightFences != null)
        {
            foreach (var fence in InFlightFences) LogicalDevice?.DestroyFence(fence);
            InFlightFences = null;
        }

        LogicalDevice?.Dispose();
        LogicalDevice = null;
        VulkanInstance?.Dispose();
        VulkanInstance = null;
        _availableComputeQueueIndex = 0;
        Log.Logger.Debug("End disposing main device");
    }

    public void OnFrameFinished()
    {
        FrameFinished?.Invoke();
    }

    public event Action FrameFinished;
}