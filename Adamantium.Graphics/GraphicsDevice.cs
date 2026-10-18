using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core;
using Adamantium.Core.Collections;
using Adamantium.EffectsCompiler;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Graphics.Effects;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.Vulkan.Core;
using Adamantium.Vulkan.Core.Interop;
using QuantumBinding.Utils;
using Serilog;
using EffectTechnique = Adamantium.Graphics.Core.EffectsFramework.EffectTechnique;
using Semaphore = Adamantium.Vulkan.Core.Semaphore;
using Exception = System.Exception;
using Image = Adamantium.Vulkan.Core.Image;

namespace Adamantium.Graphics;

[ViewModel]
public partial class GraphicsDevice : DisposableObject, IGraphicsDevice
{
    private object _locker = new object();
    private bool _withheld;
    private volatile bool _recordingFrame;
    public Guid DeviceId { get; private set; }

    private CommandBuffer[] commandBuffers;
    private uint _graphicsQueueIndex;
    private bool _holdsGraphicsQueue;

    private uint frame;
        
    [Bindable] private Type vertexType;
    [Bindable] private PrimitiveTopology primitiveTopology;
    [Bindable] private IEffectPass currentEffectPass;

    // -- Drawing States
        
    private TrackingCollection<Viewport> viewports;
    private TrackingCollection<Rect2D> scissors;
    // Cached snapshots so SetDrawingState doesn't allocate a fresh array on every draw.
    private Viewport[] viewportsArray = Array.Empty<Viewport>();
    private Rect2D[] scissorsArray = Array.Empty<Rect2D>();
    private int _originX;
    private int _originY;
    private Viewport[] _sentViewports = [];
    private Rect2D[] _sentScissors = [];

    // Dynamic-state cache: last value emitted per state, so a draw only re-emits what changed.
    // Invalidated (_stateInitialized=false) at BeginDraw - a fresh command buffer has undefined dynamic state.
    private bool _stateInitialized;

    // What the command buffer was last told, so unchanged viewport, scissor and blend state is not re-sent on every draw.
    private Viewport _cViewport;
    private uint _cViewportCount;
    private Rect2D _cScissor;
    private uint _cScissorCount;
    private ColorBlendEquationEXT _cBlendEq;
    private bool _cBlendEnable;
    private ColorComponentFlagBits _cWriteMask;
    private bool _cBlendKnown;

    private static bool SameViewport(Viewport a, Viewport b)
        => a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height && a.MinDepth == b.MinDepth && a.MaxDepth == b.MaxDepth;

    private static bool SameScissor(Rect2D a, Rect2D b)
        => a != null && b != null && a.Offset.X == b.Offset.X && a.Offset.Y == b.Offset.Y
           && a.Extent.Width == b.Extent.Width && a.Extent.Height == b.Extent.Height;

    private static bool SameBlend(ColorBlendEquationEXT a, ColorBlendEquationEXT b)
        => a.SrcColorBlendFactor == b.SrcColorBlendFactor && a.DstColorBlendFactor == b.DstColorBlendFactor
           && a.ColorBlendOp == b.ColorBlendOp && a.SrcAlphaBlendFactor == b.SrcAlphaBlendFactor
           && a.DstAlphaBlendFactor == b.DstAlphaBlendFactor && a.AlphaBlendOp == b.AlphaBlendOp;

    private static bool IsStripTopology(PrimitiveTopology topology)
        => topology is PrimitiveTopology.LineStrip or PrimitiveTopology.TriangleStrip or PrimitiveTopology.TriangleFan
            or PrimitiveTopology.LineStripWithAdjacency or PrimitiveTopology.TriangleStripWithAdjacency;
    private Type _cVertexType;
    private bool _cRasterizerDiscard;
    private PrimitiveTopology _cTopology;
    private bool _cPrimitiveRestart;
    private MSAALevel _cMsaa;
    private bool _cAlphaToCoverage;
    private PolygonMode _cPolygonMode;
    private CullModeFlagBits _cCullMode;
    private FrontFace _cFrontFace;
    private bool _cDepthWrite;
    private bool _cDepthTest;
    private CompareOp _cDepthCompare;
    private bool _cDepthBounds;
    private bool _cDepthBias;
    private float _cDepthBiasConstant;
    private float _cDepthBiasSlope;
    private bool _depthBiasKnown;
    private bool _cDepthClamp;
    private const StencilFaceFlagBits BothStencilFaces = StencilFaceFlagBits.FrontAndBack;
    private bool _cStencilTest;
    private bool _cLogicOp;
        
    // --- End of drawing states

    private IRenderTarget[] renderTargets;
    private IDepthStencilBuffer depthBuffer;

    public Device LogicalDevice => MainDevice?.LogicalDevice;
    public GraphicsAdapter Adapter => VulkanInstance?.MainGraphicsAdapter;

    // The device-memory sub-allocator is SHARED (one per logical device), owned by the MAIN device. A render-device
    // wrapper never creates its own - it references the shared one, so no window grabs its own big host-visible BAR block
    // (that per-window block only device.Dispose freed, so a few open windows exhausted the ~214 MB BAR heap). Mirrors
    // the shared DescriptorHeapManager.
    public DeviceMemoryAllocator MemoryAllocator => (DeviceMemoryAllocator)MainDevice.MemoryAllocator;
    public GraphicsPresenter Presenter { get; set; }

    public Queue GraphicsQueue { get; private set; }
    public object GraphicsQueueLock { get; private set; }
    public IRenderTarget CurrentRenderTarget { get; private set; }
    public IDepthStencilBuffer CurrentDepthStencilBuffer { get; private set; }

    internal VulkanInstance VulkanInstance => MainDevice?.VulkanInstance;

    // Per-frame extra GPU sync registered by shared-surface producers/consumers, merged into the next Submit on
    // top of the swapchain semaphores and cleared afterwards. Timeline values are 0 for binary semaphores.
    private readonly System.Collections.Generic.List<Semaphore> _extraWaitSemaphores = new();
    private readonly System.Collections.Generic.List<PipelineStageFlagBits2> _extraWaitStages = new();
    private readonly System.Collections.Generic.List<ulong> _extraWaitValues = new();
    private readonly System.Collections.Generic.List<Semaphore> _extraSignalSemaphores = new();
    private readonly System.Collections.Generic.List<ulong> _extraSignalValues = new();

    /// <summary>Register a semaphore the next <see cref="Submit"/> must wait on (timelineValue=0 for binary).</summary>
    public void AddWaitSemaphore(Semaphore semaphore, PipelineStageFlagBits2 stage, ulong timelineValue = 0)
    {
        _extraWaitSemaphores.Add(semaphore);
        _extraWaitStages.Add(stage);
        _extraWaitValues.Add(timelineValue);
    }

    /// <summary>Register a semaphore the next <see cref="Submit"/> must signal (timelineValue=0 for binary).</summary>
    public void AddSignalSemaphore(Semaphore semaphore, ulong timelineValue = 0)
    {
        _extraSignalSemaphores.Add(semaphore);
        _extraSignalValues.Add(timelineValue);
    }
        
    private SyncObject _submissionSync;
    private static string SyncGuid = Guid.NewGuid().ToString();

    private readonly HashSet<GraphicsResource> _graphicsResources = new HashSet<GraphicsResource>();
    private readonly object _graphicsResourcesLock = new();

    private GraphicsDevice(MainGraphicsDevice mainDevice, GraphicsDeviceType deviceType)
    {
        if (deviceType == GraphicsDeviceType.ResourceLoader)
        {
            CreateResourceLoadingDevice(mainDevice);
        }
        else
        {
            CreateRenderDevice(mainDevice);
        }
    }

    private void CreateResourceLoadingDevice(MainGraphicsDevice mainDevice)
    {
        MainDevice = mainDevice;
        DeviceType = GraphicsDeviceType.ResourceLoader;
        InitializeSyncObject();
        DeviceId = Guid.NewGuid();
        MaxFramesInFlight = 1;
        InitializeResourceLoadingDevice();
        Log.Logger.Debug($"Resource loader device created. Id: {DeviceId}");
    }

    private void CreateRenderDevice(MainGraphicsDevice mainDevice)
    {
        MainDevice = mainDevice;
        DeviceType = GraphicsDeviceType.Rendering;
        InitializeSyncObject();
        DeviceId = Guid.NewGuid();

        EnableDynamicRendering = mainDevice.EnableDynamicRendering;

        EffectPools = new List<EffectPool>();
        DefaultEffectPool = EffectPool.New(this);
        MaxFramesInFlight = mainDevice.BuffersCount;
        InitializeRenderDevice();
        InitializePipeline();
        // One global descriptor heap per logical device, owned by the main device and shared by every render-device
        // wrapper (they share one VkDevice). Just reference it — no per-device allocation.
        DescriptorHeapManager = mainDevice.DescriptorHeapManager;

        // The per-frame constant-buffer arena stays per render device: devices render concurrently (parallel universes)
        // and cycle frames independently, so this can't be shared.
        _dynamicBufferPools = new EffectPass.DynamicBufferPool[MaxFramesInFlight];
        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            _dynamicBufferPools[i] = new EffectPass.DynamicBufferPool(this);
        }

        Log.Logger.Debug($"Primary render device created. Id: {DeviceId}");

        SampleMask = [0xF];
    }

    private void InitializePipeline()
    {
        viewports = new TrackingCollection<Viewport>();
        scissors = new TrackingCollection<Rect2D>();

        Sampler = SamplerStates.Default;
            
        ClearColor = Colors.CornflowerBlue;
    }

    public GraphicsDeviceType DeviceType { get; private set; }

    public bool IsPrimaryDevice => DeviceType == GraphicsDeviceType.Rendering;
        
    public bool IsResourceLoaderDevice => DeviceType == GraphicsDeviceType.ResourceLoader;
        
    public bool EnableDynamicRendering { get; private set; }

    public CommandPool GraphicsCommandPool { get; private set; }
    public CommandPool TransferCommandPool { get; private set; }
    internal Semaphore[] ImageAvailableSemaphores { get; private set; }
    internal Semaphore[] RenderFinishedSemaphores { get; private set; }
    internal Fence[] InFlightFences { get; private set; }

    public uint CurrentFrame => frame;
    
    public IDescriptorHeapManager DescriptorHeapManager { get; private set; }

    // Per render device: the transient per-frame constant-buffer arena (see CreateRenderDevice for why it's not shared).
    private EffectPass.DynamicBufferPool[] _dynamicBufferPools;
    public EffectPass.DynamicBufferPool CurrentBufferPool => _dynamicBufferPools[CurrentFrame];

    public uint MaxFramesInFlight { get; private set; }

    /// <summary>TEMP: how long the last frame spent blocked on its slot's fence - the CPU waiting for the GPU.</summary>
    public static double LastFenceWaitMs;

    public ulong FrameTicket { get; private set; }

    public List<EffectPool> EffectPools { get; private set; }

    public EffectPool DefaultEffectPool { get; private set; }

    public MainGraphicsDevice MainDevice { get; private set; }
    
    public Fence GetCurrentFence()
    {
        return InFlightFences[CurrentFrame];
    }

    public Semaphore GetRenderFinishedSemaphore()
    {
        return RenderFinishedSemaphores[CurrentFrame];
    }

    public IDepthStencilBuffer CreateDepthBuffer(
        uint width, 
        uint height, 
        DepthFormat format, 
        MSAALevel msaa,
        ImageAspectFlagBits imageAspect = ImageAspectFlagBits.DepthBit,
        string name = "")
    {
        return DepthStencilBuffer.New(this, width, height, format, msaa, imageAspect, name);
    }

    public IRenderTarget CreateRenderTarget(
        uint width, 
        uint height, 
        MSAALevel msaa, SurfaceFormat format,
        ImageUsageFlagBits usage = ImageUsageFlagBits.TransferSrcBit,
        ImageLayout desiredLayout = ImageLayout.ColorAttachmentOptimal,
        string name = "")
    {
        return RenderTarget.New(this, width, height, msaa, format, usage, desiredLayout, name);
    }

    public ITexture CreateTexture(TextureDescription description, byte[] pixelData)
    {
        return Texture.CreateFrom(this, description, pixelData);
    }

    public ITexture CreateTextureArray(TextureDescription description, IReadOnlyList<byte[]> layers)
    {
        return Texture.CreateArrayFrom(this, description, layers);
    }

    public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor)
    {
        return SharedSurface.Import(this, descriptor);
    }

    public ITexture CreateTextureFromImage(Image image, 
        uint width, 
        uint height, 
        MSAALevel msaa, 
        SurfaceFormat format,
        ImageUsageFlagBits usage = ImageUsageFlagBits.TransferSrcBit,
        ImageLayout desiredLayout = ImageLayout.ColorAttachmentOptimal,
        string name = "")
    {
        var description = new TextureDescription
        {
            Width = width,
            Height = height,
            Depth = 1,
            Dimension = TextureDimension.Texture2D,
            ArrayLayers = 1,
            Usage = usage,
            Format = format,
            DesiredImageLayout = desiredLayout,
            ImageTiling = ImageTiling.Optimal,
            ImageType = ImageType._2d,
            MipLevels = 1,
            SharingMode = SharingMode.Exclusive,
            ImageAspect = ImageAspectFlagBits.ColorBit,
            Samples = msaa
        };
        return Texture.CreateFrom(this, image, description, usage, name);
    }

    public SurfaceKHR GetOrCreateSurface(PresentationParameters parameters)
    {
        return VulkanInstance.GetOrCreateSurface(parameters);
    }

    public bool CanPresent { get; private set; }
        
    public Color ClearColor { get; set; }
        
    public SamplerState Sampler { get; set; }

    public bool RasterizerDiscardEnabled { get; set; }

    public ColorBlendEquationEXT ColorBlendEquation { get; set; } = new ColorBlendEquationEXT();
        
    public bool PrimitiveRestartEnable { get; set; }

    public MSAALevel MSAALevel { get; set; } = MSAALevel.None;
        
    public bool AlphaToCoverageEnable { get; set; }
        
    public PolygonMode PolygonMode { get; set; }

    public CullModeFlagBits CullMode { get; set; } = CullModeFlagBits.None;
        
    public bool IsWireFrame { get; set; }
        
    public VkSampleMask[] SampleMask { get; set; }

    public Single LineWidth { get; set; } = 1.0f;

    public FrontFace FrontFace { get; set; } = FrontFace.Clockwise;

    public bool DepthTestEnabled { get; set; } = true;

    public bool DepthWriteEnable { get; set; } = true;

    public CompareOp DepthCompareFunction { get; set; } = CompareOp.LessOrEqual;

    public bool DepthBoundsTestEnabled { get; set; } = false;
        
    public bool DepthBiasEnabled { get; set; } = false;

    public float DepthBiasConstantFactor { get; set; }

    public float DepthBiasSlopeFactor { get; set; }

    public bool DepthClampEnable { get; set; } = false;
        
    public bool StencilTestEnabled { get; set; } = false;

    /// <summary>Stencil comparison, reference, and what a passing fragment writes back - for marking COVERAGE: one pass
    /// stamps where it drew, a later pass tests against it.</summary>
    public CompareOp StencilCompareOp { get; set; } = CompareOp.Always;

    public StencilOp StencilPassOp { get; set; } = StencilOp.Keep;

    public uint StencilReference { get; set; }

    public uint StencilWriteMask { get; set; } = 0xFF;

    /// <summary>Is there a depth attachment to borrow at all? Without one the caller draws the plain single-pass way.</summary>
    public bool HasDepthAttachment => depthBuffer != null;

    /// <summary>Resets DEPTH ONLY (the stencil, which carries the instanced fills' coverage marks, is untouched) inside
    /// one rectangle of the attachment, mid-pass. A draw that borrows depth as scratch - the strokes' union coverage -
    /// uses this to start from a known 1.0 over its own bounds, instead of assuming what the rest of the frame left
    /// there. That assumption is what made the first version of this a frame-wide change.</summary>
    public void ClearDepthInRect(Rect2D rect)
    {
        if (depthBuffer == null || rect.Extent.Width == 0 || rect.Extent.Height == 0) return;
        if (!ClampToRenderArea(ref rect)) return;

        var attachment = new ClearAttachment
        {
            AspectMask = ImageAspectFlagBits.DepthBit,
            ClearValue = new ClearValue { DepthStencil = new ClearDepthStencilValue { Depth = 1.0f, Stencil = 0 } }
        };
        var clearRect = new ClearRect { Rect = rect, BaseArrayLayer = 0, LayerCount = 1 };
        CurrentCommandBuffer.ClearAttachments(1, new[] { attachment }, 1, new[] { clearRect });
    }

    // A clear rect outside the render area is invalid use; callers only know the viewport, so it is cut here. False: nothing
    // is left to clear.
    private bool ClampToRenderArea(ref Rect2D rect)
    {
        if (renderTargets is not { Length: > 0 }) return false;

        var limitX = (int)renderTargets[0].Width;
        var limitY = (int)renderTargets[0].Height;
        var left = Math.Clamp(rect.Offset.X, 0, limitX);
        var top = Math.Clamp(rect.Offset.Y, 0, limitY);
        var right = Math.Clamp(rect.Offset.X + (int)rect.Extent.Width, left, limitX);
        var bottom = Math.Clamp(rect.Offset.Y + (int)rect.Extent.Height, top, limitY);
        if (right == left || bottom == top) return false;

        rect = new Rect2D
        {
            Offset = new Offset2D { X = left, Y = top },
            Extent = new Extent2D { Width = (uint)(right - left), Height = (uint)(bottom - top) }
        };
        return true;
    }

    public bool LogicOperationsEnabled { get; set; } = false;
        
    public LogicOp LogicOperation { get; set; }
        
    public bool ColorBlendEnabled { get; set; } = true;

    public ColorComponentFlagBits ColorComponentFlags { get; set; } = ColorComponentFlagBits.RBit |
                                                                      ColorComponentFlagBits.GBit |
                                                                      ColorComponentFlagBits.BBit |
                                                                      ColorComponentFlagBits.ABit;

    // A COPY, from the array the setter fills. The tracking collections these came from were written on every
    // SetViewports/SetScissors and read by nobody.
    public Viewport[] Viewports => viewportsArray.ToArray();

    public Rect2D[] Scissors => scissorsArray.ToArray();

    public bool CommandBufferStarted { get; private set; }

    private void InitializeSyncObject()
    {
        // Always on: devices submit and record from different threads, and a shared queue and command pools need external
        // sync whatever the queue families.
        _submissionSync = new SyncObject(SyncGuid, true);
    }

    public CommandBuffer CurrentCommandBuffer => commandBuffers[CurrentFrame]; 

    private void InitializeRenderDevice()
    {
        CreateCommandPool();
        CreateCommandBuffers();
        CreateSyncObjects();
    }

    private void InitializeResourceLoadingDevice()
    {
        CreateCommandPool();
        CreateCommandBuffers();
    }

    public void AddResource(GraphicsResource resource)
    {
        lock (_graphicsResourcesLock)
        {
            _graphicsResources.Add(resource);
        }
    }

    public void RemoveResource(GraphicsResource resource)
    {
        lock (_graphicsResourcesLock)
        {
            _graphicsResources.Remove(resource);
        }
    }

    /// <summary>Number of live (registered) graphics resources. Used by leak regression tests.</summary>
    public int RegisteredResourceCount
    {
        get
        {
            lock (_graphicsResourcesLock)
            {
                return _graphicsResources.Count;
            }
        }
    }

    /// <summary>Hand a GPU resource over for disposal. Held by the LOGICAL device (see
    /// <see cref="MainGraphicsDevice.RetireResource"/>) until every drawing wrapper has retired the frames it had in
    /// flight - the wrappers share one VkDevice and share resources, so this device's own fences prove too little.</summary>
    public void AddToDeferDisposeQueue(IDisposable obj)
    {
        MainDevice.RetireResource(obj);
    }

    public IEffectResourceLinker CreateEffectResourceLinker()
    {
        return new EffectResourceLinker(DescriptorHeapManager);
    }

    public IEffectPass CreateEffectPass(Logger logger, Effect effect, EffectTechnique technique, EffectData.Pass pass, string name)
    {
        return new EffectPass(logger, effect, technique, pass, name);
    }

    // Which pass's shader objects the CURRENT command buffer already has bound. A pass applied again for the next draw of
    // the same material rebound the same handles, stage by stage, through a marshaled call each - and a frame is mostly
    // runs of the same material. Cleared with the rest of the dynamic state at BeginDraw, because a fresh command buffer
    // has none of it.
    private object _boundShaderPass;

    /// <summary>Are this pass's shaders already bound to the current command buffer?</summary>
    public bool ShadersBoundFor(object pass) => _stateInitialized && ReferenceEquals(_boundShaderPass, pass);

    /// <summary>Remember that this pass's shaders are now bound.</summary>
    public void ShadersBound(object pass) => _boundShaderPass = pass;

    public void BindShader(CommandBuffer cmd, ShaderStageFlagBits stage, ShaderEXT shader)
    {
        cmd.BindShadersEXT(1, stage, shader);
    }

    public void BindShaders(CommandBuffer cmd, ShaderStageFlagBits[] stages, ShaderEXT[] shaders)
    {
        cmd.BindShadersEXT((uint)stages.Length, stages, shaders);
    }

    // Serializes shader creation for parallel callers such as the test runner; static because all render devices share
    // one VkDevice and one cache folder.
    private static readonly object ShaderCreateLock = new();

    public ShaderEXT CreateShader(ShaderCreateInfoEXT shaderCreateInfo, string name = null)
    {
        lock (ShaderCreateLock)
        {
            return CreateShaderCore(shaderCreateInfo, name);
        }
    }

    private ShaderEXT CreateShaderCore(ShaderCreateInfoEXT shaderCreateInfo, string name)
    {
        // Create from the cached driver binary when there is one; otherwise compile from SPIR-V and persist the binary as
        // `name` (effect.technique.pass.stage).
        if (ShaderBinaryCache.TryLoad(this, shaderCreateInfo, name, out var binary))
        {
            var result = LogicalDevice.CreateShaderFromBinary(ShaderBinaryCache.AsBinary(shaderCreateInfo, binary), out var cached);
            if (result == Result.Success) return cached;
            // else: incompatible binary (driver/device change) -> fall through and recompile from SPIR-V (re-caches below).
        }

        // Creation below can fault natively, which no catch intercepts, so a shader recorded as having taken the process
        // down is refused here. Cache hits and the compile pass stay unguarded.
        if (!ShaderPrecompiler.IsCompilePass && ShaderCompileStats.ForDevice(this).ShouldSkipShader(name))
        {
            // Refusing is not giving up - a child can try it while this process keeps drawing.
            ShaderPrecompiler.TryCompileInBackground(this, name);

            throw new InvalidOperationException(
                $"Shader '{name}' is not compiled: building it took the process down before. " +
                "See compile-stats.xml beside the shader cache.");
        }

        // Recorded before creation, so a native fault still leaves the shader's name behind.
        ShaderPrecompiler.NoteShaderInFlight(name);

        LogicalDevice.CreateShadersEXT(1, shaderCreateInfo, null, out var shaderObject);
        ShaderBinaryCache.Save(this, shaderCreateInfo, name, shaderObject[0]);
        return shaderObject[0];
    }

    public void DestroyShader(ShaderEXT shaderObject)
    {
        LogicalDevice.DestroyShaderEXT(shaderObject);
    }

    private void CreateCommandPool()
    {
        var graphicsFamily = MainDevice.QueueFamilyContainer.GetFamilyInfo(QueueFlagBits.GraphicsBit);

        var poolInfo = new CommandPoolCreateInfo
        {
            QueueFamilyIndex = graphicsFamily.FamilyIndex,
            Flags = CommandPoolCreateFlagBits.ResetCommandBufferBit
        };
        GraphicsCommandPool = LogicalDevice.CreateCommandPool(poolInfo);

        poolInfo = new CommandPoolCreateInfo
        {
            QueueFamilyIndex = graphicsFamily.FamilyIndex,
            Flags = CommandPoolCreateFlagBits.TransientBit
        };

        TransferCommandPool = LogicalDevice.CreateCommandPool(poolInfo);
        GraphicsQueue = MainDevice.LeaseGraphicsQueue(out _graphicsQueueIndex, out var queueLock);
        GraphicsQueueLock = queueLock;
        _holdsGraphicsQueue = true;
        unsafe
        {
            Log.Logger.Information($"Graphics Queue address of Logical device {new IntPtr(LogicalDevice.NativePointer)}: 0x{new IntPtr(GraphicsQueue.NativePointer).ToString("X2")}");
        }
    }

    private void CreateCommandBuffers()
    {
        var buffersCount = MaxFramesInFlight;
            
        commandBuffers = new CommandBuffer[buffersCount];

        var allocInfo = new CommandBufferAllocateInfo();
        allocInfo.CommandPool = GraphicsCommandPool;
        //allocInfo.Level = IsPrimaryDevice ? CommandBufferLevel.Primary : CommandBufferLevel.Secondary;
        allocInfo.Level = CommandBufferLevel.Primary;
        allocInfo.CommandBufferCount = buffersCount;

        commandBuffers = LogicalDevice.AllocateCommandBuffers(allocInfo);
    }

    private void CreateSyncObjects()
    {
        var semaphoreInfo = new SemaphoreCreateInfo();
            
        var fenceInfo = new FenceCreateInfo();
        fenceInfo.Flags = FenceCreateFlagBits.SignaledBit;

        ImageAvailableSemaphores = LogicalDevice.CreateSemaphores(semaphoreInfo, MaxFramesInFlight);
        //RenderFinishedSemaphores = LogicalDevice.CreateSemaphores(semaphoreInfo, MaxFramesInFlight);
        InFlightFences ??= LogicalDevice.CreateFences(fenceInfo, MaxFramesInFlight);
    }
    
    public Queue GetDeviceQueue(uint queueFamilyIndex, uint queueIndex)
    {
        return LogicalDevice.GetDeviceQueue(queueFamilyIndex, queueIndex);
    }

    public Result DeviceWaitIdle()
    {
        return MainDevice.DeviceWaitIdle();
    }

    public bool HasNoFramesInFlight
    {
        get
        {
            if (_recordingFrame)
            {
                return false;
            }

            if (InFlightFences == null)
            {
                return true;
            }

            foreach (var fence in InFlightFences)
            {
                if (LogicalDevice.GetFenceStatus(fence) != Result.Success)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public Result WaitForFramesInFlight(ulong timeout)
    {
        if (InFlightFences == null || InFlightFences.Length == 0) return Result.Success;

        return MainDevice.Observe(LogicalDevice.WaitForFences((uint)InFlightFences.Length, InFlightFences, true, timeout));
    }

    public void InsertImageMemoryBarrier(
        CommandBuffer commandBuffer,
        ITexture texture,
        AccessFlagBits2 sourceAccessMask,
        AccessFlagBits2 destinationAccessMask,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        PipelineStageFlagBits2 sourceStageMask,
        PipelineStageFlagBits2 destinationStageMask)
        => InsertImageMemoryBarrier(commandBuffer, texture, sourceAccessMask, destinationAccessMask,
            oldLayout, newLayout, sourceStageMask, destinationStageMask, 0, ~0U);

    /// <summary>The same barrier over one range of mip levels, for building a pyramid. <see cref="ITexture.ImageLayout"/> is
    /// updated only when the range covers the whole image.</summary>
    public void InsertImageMemoryBarrier(
        CommandBuffer commandBuffer,
        ITexture texture,
        AccessFlagBits2 sourceAccessMask,
        AccessFlagBits2 destinationAccessMask,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        PipelineStageFlagBits2 sourceStageMask,
        PipelineStageFlagBits2 destinationStageMask,
        uint baseMipLevel,
        uint levelCount)
    {
        if (texture == null) return;

        var range = new ImageSubresourceRange
        {
            AspectMask = texture.ImageAspect,
            BaseMipLevel = baseMipLevel,
            LevelCount = levelCount,
            BaseArrayLayer = 0,
            LayerCount = (~0U)
        };

        var barrier = new ImageMemoryBarrier2();
        barrier.SrcStageMask = sourceStageMask;
        barrier.DstStageMask = destinationStageMask;
        barrier.SrcQueueFamilyIndex = (~0U);
        barrier.DstQueueFamilyIndex = (~0U);
        barrier.SrcAccessMask = sourceAccessMask;
        barrier.DstAccessMask = destinationAccessMask;
        barrier.OldLayout = oldLayout;
        barrier.NewLayout = newLayout;
        barrier.Image = texture.GetImage();
        barrier.SubresourceRange = range;

        commandBuffer.PipelineBarrier2(new DependencyInfo
        {
            PImageMemoryBarriers = new[] { barrier },
            ImageMemoryBarrierCount = 1
        });

        if (baseMipLevel == 0 && levelCount == ~0U) texture.ImageLayout = newLayout;
    }

    public void TransitionImagesForRendering(CommandBuffer commandBuffer, params ITexture[] inputTargets)
    {
        var barriers = new List<ImageMemoryBarrier2>();
        foreach (var renderTarget in inputTargets)
        {
            var barrier = new ImageMemoryBarrier2();
            barrier.SrcStageMask = PipelineStageFlagBits2.TopOfPipeBit;
            barrier.SrcAccessMask = AccessFlagBits2.None;
            barrier.DstStageMask = PipelineStageFlagBits2.ColorAttachmentOutputBit;
            barrier.DstAccessMask = AccessFlagBits2.ColorAttachmentWriteBit;
            barrier.OldLayout = renderTarget.ImageLayout;
            barrier.NewLayout = ImageLayout.ColorAttachmentOptimal;
            barrier.SrcQueueFamilyIndex = Constants.VK_QUEUE_FAMILY_IGNORED;
            barrier.DstQueueFamilyIndex = Constants.VK_QUEUE_FAMILY_IGNORED;
            barrier.Image = renderTarget.GetImage();
            barrier.SubresourceRange = new ImageSubresourceRange()
            {
                AspectMask = renderTarget.ImageAspect,
                BaseMipLevel = 0,
                LevelCount = (~0U),
                BaseArrayLayer = 0,
                LayerCount = (~0U)
            };
            barriers.Add(barrier);

            renderTarget.ImageLayout = ImageLayout.ColorAttachmentOptimal;
        }

        var dependencyInfo = new DependencyInfo();
        dependencyInfo.PImageMemoryBarriers = barriers.ToArray();
        dependencyInfo.ImageMemoryBarrierCount = (uint)barriers.Count;
        
        commandBuffer.PipelineBarrier2(dependencyInfo);
    }
    
    public void TransitionDepthBufferForRendering(CommandBuffer commandBuffer, IDepthStencilBuffer depthBuffer)
    {
        if (depthBuffer == null) return;
        
        var barriers = new List<ImageMemoryBarrier2>();

        var barrier = new ImageMemoryBarrier2();
        barrier.SrcStageMask = PipelineStageFlagBits2.TopOfPipeBit;
        barrier.SrcAccessMask = AccessFlagBits2.None;
        barrier.DstStageMask = PipelineStageFlagBits2.EarlyFragmentTestsBit | PipelineStageFlagBits2.LateFragmentTestsBit;
        barrier.DstAccessMask = AccessFlagBits2.DepthStencilAttachmentWriteBit;
        barrier.OldLayout = ImageLayout.Undefined;
        barrier.NewLayout = ImageLayout.DepthStencilAttachmentOptimal;
        barrier.SrcQueueFamilyIndex = Constants.VK_QUEUE_FAMILY_IGNORED;
        barrier.DstQueueFamilyIndex = Constants.VK_QUEUE_FAMILY_IGNORED;
        barrier.Image = depthBuffer.GetImage();
        barrier.SubresourceRange = new ImageSubresourceRange()
        {
            AspectMask =  this.depthBuffer.ImageAspect,
            BaseMipLevel = 0,
            LevelCount = (~0U),
            BaseArrayLayer = 0,
            LayerCount = (~0U)
        };
        barriers.Add(barrier);

        depthBuffer.ImageLayout = ImageLayout.DepthStencilAttachmentOptimal;

        var dependencyInfo = new DependencyInfo();
        dependencyInfo.PImageMemoryBarriers = barriers.ToArray();
        dependencyInfo.ImageMemoryBarrierCount = (uint)barriers.Count;
        
        commandBuffer.PipelineBarrier2(dependencyInfo);
    }

    /// <summary>
    /// Records a ColorAttachmentOptimal -> PresentSrcKhr barrier for the swapchain image into the given (main)
    /// command buffer, so no separate single-time submit is needed at present time.
    /// </summary>
    private void TransitionPresenterImageForPresent(CommandBuffer commandBuffer, ITexture image)
    {
        if (image == null || image.ImageLayout == ImageLayout.PresentSrcKhr) return;

        var barrier = new ImageMemoryBarrier2
        {
            // The swapchain image is filled by a blit, so the source side names the transfer stage too.
            SrcStageMask = PipelineStageFlagBits2.ColorAttachmentOutputBit | PipelineStageFlagBits2.AllTransferBit,
            SrcAccessMask = AccessFlagBits2.ColorAttachmentWriteBit | AccessFlagBits2.TransferWriteBit,
            DstStageMask = PipelineStageFlagBits2.BottomOfPipeBit,
            DstAccessMask = AccessFlagBits2.None,
            OldLayout = image.ImageLayout,
            NewLayout = ImageLayout.PresentSrcKhr,
            SrcQueueFamilyIndex = Constants.VK_QUEUE_FAMILY_IGNORED,
            DstQueueFamilyIndex = Constants.VK_QUEUE_FAMILY_IGNORED,
            Image = image.GetImage(),
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = image.ImageAspect,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        var dependencyInfo = new DependencyInfo
        {
            PImageMemoryBarriers = new[] { barrier },
            ImageMemoryBarrierCount = 1
        };

        commandBuffer.PipelineBarrier2(dependencyInfo);
        image.ImageLayout = ImageLayout.PresentSrcKhr;
    }

    public void TransitionImagesAfterRendering(CommandBuffer commandBuffer, params ITexture[] inputTargets)
    {
        var barriers = new List<ImageMemoryBarrier2>();
        foreach (var renderTarget in inputTargets)
        {
            var barrier = new ImageMemoryBarrier2();
            barrier.SrcStageMask = PipelineStageFlagBits2.ColorAttachmentOutputBit;
            barrier.SrcAccessMask = AccessFlagBits2.ColorAttachmentWriteBit;
            barrier.DstStageMask = PipelineStageFlagBits2.FragmentShaderBit;
            barrier.DstAccessMask = AccessFlagBits2.ShaderReadBit;
            barrier.OldLayout = ImageLayout.ColorAttachmentOptimal;
            barrier.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
            barrier.SrcQueueFamilyIndex = Constants.VK_QUEUE_FAMILY_IGNORED;
            barrier.DstQueueFamilyIndex = Constants.VK_QUEUE_FAMILY_IGNORED;
            barrier.Image = renderTarget.GetImage();
            barrier.SubresourceRange = new ImageSubresourceRange()
            {
                AspectMask = renderTarget.ImageAspect,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            };
            barriers.Add(barrier);

            renderTarget.ImageLayout = ImageLayout.ShaderReadOnlyOptimal;
        }
        
        var dependencyInfo = new DependencyInfo();
        dependencyInfo.PImageMemoryBarriers = barriers.ToArray();
        dependencyInfo.ImageMemoryBarrierCount = (uint)barriers.Count;
        
        commandBuffer.PipelineBarrier2(dependencyInfo);
    }
    
    public void TransitionDepthBufferAfterRendering(CommandBuffer commandBuffer, IDepthStencilBuffer depthBuffer)
    {
        if (depthBuffer == null) return;
        
        var barriers = new List<ImageMemoryBarrier2>();

        var barrier = new ImageMemoryBarrier2();
        barrier.SrcStageMask = PipelineStageFlagBits2.EarlyFragmentTestsBit | PipelineStageFlagBits2.LateFragmentTestsBit;
        barrier.SrcAccessMask = AccessFlagBits2.DepthStencilAttachmentWriteBit;
        barrier.DstStageMask = PipelineStageFlagBits2.FragmentShaderBit | PipelineStageFlagBits2.ComputeShaderBit;
        barrier.DstAccessMask = AccessFlagBits2.None;
        barrier.OldLayout = ImageLayout.Undefined;
        barrier.NewLayout = ImageLayout.DepthStencilReadOnlyOptimal;
        barrier.SrcQueueFamilyIndex = Constants.VK_QUEUE_FAMILY_IGNORED;
        barrier.DstQueueFamilyIndex = Constants.VK_QUEUE_FAMILY_IGNORED;
        barrier.Image = depthBuffer.GetImage();
        barrier.SubresourceRange = new ImageSubresourceRange()
        {
            AspectMask = this.depthBuffer.ImageAspect,
            BaseMipLevel = 0,
            LevelCount = (~0U),
            BaseArrayLayer = 0,
            LayerCount = (~0U)
        };
        barriers.Add(barrier);

        var dependencyInfo = new DependencyInfo();
        dependencyInfo.PImageMemoryBarriers = barriers.ToArray();
        dependencyInfo.ImageMemoryBarrierCount = (uint)barriers.Count;
        
        commandBuffer.PipelineBarrier2(dependencyInfo);
    }
    
    /// <summary>Why the last <see cref="BeginDraw"/> returned false (a Vulkan device/surface error). Surfaced so a
    /// caller that only sees the bool (e.g. the designer's headless render) can report the real reason, not "render failed".</summary>
    public string LastFrameError { get; private set; }

    // Last fence-wait error already logged. A persistent device error makes BeginDraw fail EVERY frame; logging it per
    // frame floods the Serilog console sink and the render thread then blocks forever on that sink's lock (a slow/stalled
    // console write, held across all loggers) - which turned a recoverable device hiccup into a hard freeze. Log only on a
    // NEW error transition instead.
    private Result _lastFenceWaitError = Result.Success;

    // The factual error code (e.g. ErrorDeviceLost) is only the SYMPTOM; the validation layers carry the real cause.
    // Append whatever they reported so the report isn't a guess. Empty (with a hint) when validation is off.
    private static string DescribeRecentValidation()
    {
        var messages = Adamantium.Graphics.Core.VulkanInstance.RecentValidationMessages;
        if (messages.Count == 0)
            return " (no validation messages captured — run with graphics debug enabled to get the real cause)";
        return "\nValidation layer messages leading up to it:\n  - " + string.Join("\n  - ", messages);
    }

    // After a device-lost, VK_EXT_device_fault returns the driver's REAL fault description (and how many faulting
    // address regions / vendor records there are) - the diagnostic that works without validation layers. Best-effort:
    // only when the extension was enabled, and never throws back into the caller.
    private string DescribeDeviceFault()
    {
        try
        {
            // Each of these used to return "" - indistinguishable from "the driver reported no fault", which is the one
            // answer that would let a reader stop looking. Say which it was.
            if (MainDevice is not { DeviceFaultSupported: true }) return "\nGPU device fault: VK_EXT_device_fault was NOT enabled on this device";
            // Incomplete means the driver filled what we asked for and held back the vendor blob we opted out of.
            var faultResult = LogicalDevice.GetDeviceFaultInfoEXT(out var fault);
            if (faultResult != Result.Success && faultResult != Result.Incomplete)
                return $"\nGPU device fault: GetDeviceFaultInfoEXT returned {faultResult}";
            if (fault == null) return "\nGPU device fault: GetDeviceFaultInfoEXT succeeded but reported nothing";

            var sb = new System.Text.StringBuilder($"\nGPU device fault (VK_EXT_device_fault): \"{fault.Description}\"");
            var addresses = fault.PAddressInfos.Span;
            for (var i = 0; i < addresses.Length; i++)
            {
                var a = addresses[i];
                sb.Append($"\n  - {a.AddressType}: address 0x{(ulong)a.ReportedAddress:X} (precision 0x{(ulong)a.AddressPrecision:X})");
            }
            var vendors = fault.PVendorInfos.Span;
            for (var i = 0; i < vendors.Length; i++)
            {
                var v = vendors[i];
                sb.Append($"\n  - vendor \"{v.Description}\": code=0x{v.VendorFaultCode:X} data=0x{v.VendorFaultData:X}");
            }
            return sb.ToString();
        }
        catch (Exception e) { return $"\nGPU device fault: querying it threw {e.GetType().Name}: {e.Message}"; }
    }

    public bool BeginDraw(float depth = 1.0f, uint stencil = 0, Action<CommandBuffer> beforeRenderPass = null)
    {
        _recordingFrame = true;
        CanPresent = false;
        LastFrameError = null;
        _withheld = false;
        FrameWithheld = false;
        var renderFence = InFlightFences[CurrentFrame];
        // TEMP: the CPU blocks HERE until the GPU is done with this slot. Timed on its own because "BeginDraw is a third
        // of the frame" does not say whether we are waiting for the GPU or doing work - and those have opposite fixes.
        var fenceStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = MainDevice.Observe(LogicalDevice.WaitForFences(1, renderFence, true, ulong.MaxValue));
        LastFenceWaitMs = System.Diagnostics.Stopwatch.GetElapsedTime(fenceStart).TotalMilliseconds;

        FrameTicket++;
        MainDevice.OnFrameStarted();
        MainDevice.ShaderHotReload.ApplyTo(this);

        if (result != Result.Success && result != Result.Timeout)
        {
            // Only on a NEW error - the per-frame flood on a persistent error froze the render thread on the console sink
            // lock (see _lastFenceWaitError). The expensive fault/validation describe is likewise done once per transition.
            if (result != _lastFenceWaitError)
            {
                _lastFenceWaitError = result;
                LastFrameError = $"WaitForFences returned {result}{DescribeDeviceFault()}{DescribeRecentValidation()}";
                Log.Logger.Information($"Wait for fences result: {result}. DETAIL: {LastFrameError}");
            }
            return false;
        }
        _lastFenceWaitError = Result.Success;   // recovered - re-arm the log for the next new error
        CurrentBufferPool.Reset();

        if (Presenter is { IsReady: false })
        {
            return false;
        }

        // The swapchain image is acquired late, after all fallible recording, so an aborted frame holds no image or semaphore.

        // if (Presenter is SwapChainGraphicsPresenter swapchain)
        // {
        // result = LogicalDevice.AcquireNextImageKHR(swapchain, ulong.MaxValue,
        //     ImageAvailableSemaphores[CurrentFrame], null, ref imageIndex);
        //
        //     if (result == Result.ErrorOutOfDateKhr)
        //     {
        //         return false;
        //     }
        //
        //     if (result != Result.Success && result != Result.SuboptimalKhr)
        //     {
        //         throw new ArgumentException("Failed to acquire swap chain image!");
        //     }
        // }

        var commandBuffer = commandBuffers[CurrentFrame];

        var beginInfo = new CommandBufferBeginInfo();
        beginInfo.Flags = CommandBufferUsageFlagBits.SimultaneousUseBit;
            
        result = commandBuffer.ResetCommandBuffer(0);
            
        if (result != Result.Success)
        {
            throw new Exception("failed to begin recording command buffer!");
        }

        CommandBufferStarted = true;

        // Fresh command buffer => dynamic state is undefined, so the state cache must re-emit everything next draw.
        _stateInitialized = false;
        _depthBiasKnown = false;
        _boundShaderPass = null;
        _originX = 0;
        _originY = 0;

        //Log.Logger.Information($"Begin Command buffer on {DeviceType} device {DeviceId}");
        result = commandBuffer.BeginCommandBuffer(beginInfo);
        // unsafe
        // {
        //     Log.Logger.Debug($"BeginCommandBuffer was called for {new IntPtr(commandBuffer.NativePointer)}");
        // }
            
        if (result != Result.Success)
        {
            throw new Exception("failed to begin recording command buffer!");
        }
        
        TransitionImagesForRendering(commandBuffer, renderTargets);
        TransitionDepthBufferForRendering(commandBuffer, depthBuffer);

        // Out-of-render-pass work (e.g. shared-surface latch copies) must be recorded here, before BeginRendering,
        // so a later in-pass draw can sample the result the SAME frame (no latency).
        beforeRenderPass?.Invoke(commandBuffer);

        BeginRendering(commandBuffer, false, depth, stencil);

        // LATE acquire (see the note near the top of BeginDraw): every fallible step above - CB begin, image transitions,
        // the beforeRenderPass record, BeginRendering - has run WITHOUT touching the swapchain image, so a failure there
        // aborts the frame with no dangling acquired image. Only now, committed to Submit + Present, do we take one.
        return true;
    }

    /// <summary>Suspends the render pass mid-frame so transfer work, such as a backdrop material reading what is behind it,
    /// goes into the same command buffer. Pair with <see cref="ResumeRendering"/>, which loads the attachment.</summary>
    public void SuspendRendering()
    {
        if (!EnableDynamicRendering) return;

        CurrentCommandBuffer.EndRendering();
    }

    /// <summary>Re-open the pass suspended by <see cref="SuspendRendering"/>. LoadOp is Load, so the color and depth
    /// already in the target survive - a Clear here would wipe the frame drawn so far.</summary>
    public void ResumeRendering()
    {
        if (!EnableDynamicRendering) return;

        BeginRendering(CurrentCommandBuffer, continueRendering: true);
    }

    public void BeginRendering(CommandBuffer commandBuffer, bool continueRendering = false, float depth = 1.0f, uint stencil = 0)
    {
        var clearColorValue = new ClearValue
        {
            Color = new ClearColorValue
            {
                Float32 = ClearColor.ToFloatArray()
            }
        };

        var clearDepthValue = new ClearValue
        {
            DepthStencil = new ClearDepthStencilValue
            {
                Depth = depth,
                Stencil = stencil
            }
        };
        
        var loadOperation = continueRendering ? AttachmentLoadOp.Load : AttachmentLoadOp.Clear;

        if (EnableDynamicRendering)
        {
            var colorAttachments = new RenderingAttachmentInfo[renderTargets.Length];
            for (var i = 0; i < renderTargets.Length; i++)
            {
                var renderTarget = renderTargets[i];
                var colorAttachmentInfo = new RenderingAttachmentInfo();
                colorAttachmentInfo.ImageLayout = ImageLayout.ColorAttachmentOptimal;
                colorAttachmentInfo.LoadOp = loadOperation;
                colorAttachmentInfo.StoreOp = AttachmentStoreOp.Store;
                colorAttachmentInfo.ClearValue = clearColorValue;
                if (renderTarget.MSAALevel != MSAALevel.None)
                {
                    colorAttachmentInfo.ImageView = renderTarget.GetImageView();
                    colorAttachmentInfo.ResolveImageView = renderTarget.ResolveTexture.GetImageView();
                    colorAttachmentInfo.ResolveMode = ResolveModeFlagBits.AverageBit;
                    colorAttachmentInfo.ResolveImageLayout = ImageLayout.ColorAttachmentOptimal;
                }
                else
                {
                    colorAttachmentInfo.ImageView = renderTarget.GetImageView();
                }
                colorAttachments[i] = colorAttachmentInfo;
            }

            var width = renderTargets[0].Width;
            var height = renderTargets[0].Height;

            var depthAttachmentInfo = new RenderingAttachmentInfo();
            depthAttachmentInfo.ImageView = depthBuffer?.GetImageView();
            depthAttachmentInfo.ImageLayout = ImageLayout.DepthStencilAttachmentOptimal;
            depthAttachmentInfo.ResolveMode = ResolveModeFlagBits.None;
            depthAttachmentInfo.LoadOp = loadOperation;
            depthAttachmentInfo.StoreOp = AttachmentStoreOp.Store;
            depthAttachmentInfo.ClearValue = clearDepthValue;

            var renderingInfo = new RenderingInfo();
            renderingInfo.RenderArea = new Rect2D();
            renderingInfo.RenderArea.Extent = new Extent2D(){ Width = width, Height = height};
            renderingInfo.RenderArea.Offset = new Offset2D();
            renderingInfo.PColorAttachments = colorAttachments;
            renderingInfo.ColorAttachmentCount = (uint)colorAttachments.Length;
            if (depthBuffer != null)
            {
                renderingInfo.PDepthAttachment = depthAttachmentInfo;
                renderingInfo.PStencilAttachment = depthAttachmentInfo;
            }

            renderingInfo.LayerCount = 1;
            
            // InsertImageMemoryBarrier(commandBuffer,
            //     renderTargets[0],
            //     0,
            //     AccessFlagBits.ColorAttachmentWriteBit,
            //     ImageLayout.Undefined,
            //     ImageLayout.ColorAttachmentOptimal,
            //     PipelineStageFlagBits.TopOfPipeBit,
            //     PipelineStageFlagBits.ColorAttachmentOutputBit
            // );
            //
            // InsertImageMemoryBarrier(commandBuffer,
            //     depthBuffer,
            //     0,
            //     AccessFlagBits.DepthStencilAttachmentWriteBit,
            //     ImageLayout.Undefined,
            //     ImageLayout.DepthStencilAttachmentOptimal,
            //     PipelineStageFlagBits.EarlyFragmentTestsBit | PipelineStageFlagBits.LateFragmentTestsBit,
            //     PipelineStageFlagBits.EarlyFragmentTestsBit | PipelineStageFlagBits.LateFragmentTestsBit
            // );
            
            commandBuffer.BeginRendering(renderingInfo);
            DescriptorHeapManager.BindDescriptorHeaps(this);
        }
    }

    /// <summary>Whether this frame holds a swapchain image. False when the acquire below failed - the caller must then
    /// neither blit nor present, exactly as it must not when Submit never ran.</summary>
    public bool HasSwapchainImage { get; private set; }

    public void WithholdFrame() => _withheld = true;

    public bool FrameWithheld { get; private set; }

    public void EndDraw()
    {
        // Acquired here, after the work: the frame renders offscreen, and acquiring first blocked on the present engine.
        // Everything fallible runs before this, so an aborted frame leaks nothing.
        HasSwapchainImage = false;
        FrameWithheld = _withheld;
        _withheld = false;
        if (Presenter is SwapChainGraphicsPresenter && !FrameWithheld)
        {
            HasSwapchainImage = Presenter.AcquireNextImage(null, ImageAvailableSemaphores[CurrentFrame]);

            if (!HasSwapchainImage)
                LastFrameError = $"swapchain AcquireNextImage failed{DescribeRecentValidation()}";
        }
        else if (Presenter is not SwapChainGraphicsPresenter)
        {
            HasSwapchainImage = true;   // a render-target presenter owns its image outright
        }

        var commandBuffer = commandBuffers[CurrentFrame];

        if (EnableDynamicRendering)
        {
            commandBuffer.EndRendering();

            //InsertMemoryBarrier();
            // TransitionImagesAfterRendering(commandBuffer, renderTargets);
            // TransitionDepthBufferAfterRendering(commandBuffer, depthBuffer);

            // InsertImageMemoryBarrier(commandBuffer,
            //     renderTargets[0],
            //     AccessFlagBits.ColorAttachmentWriteBit,
            //     0,
            //     ImageLayout.ColorAttachmentOptimal,
            //     ImageLayout.PresentSrcKhr,
            //     PipelineStageFlagBits.ColorAttachmentOutputBit,
            //     PipelineStageFlagBits.BottomOfPipeBit);
        }
        else
        {
            // if (DeviceType == GraphicsDeviceType.Primary)
            // {
            //     commandBuffer.EndRenderPass();
            // }
        }
    }

    public void InsertMemoryBarrier()
    {
        var memoryBarrier = new MemoryBarrier2
        {
            SrcStageMask = PipelineStageFlagBits2.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlagBits2.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlagBits2.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlagBits2.ColorAttachmentReadBit
        };

        var dependencyInfo = new DependencyInfo
        {
            MemoryBarrierCount = 1,
            PMemoryBarriers = new[] {memoryBarrier}
        };

        CurrentCommandBuffer.PipelineBarrier2(dependencyInfo);
    }

    public void Submit()
    {
        if (!CommandBufferStarted) return;

        _submissionSync?.Wait();

        //Log.Logger.Debug($"Enter Submit for device {DeviceId}");

        var commandBuffer = CurrentCommandBuffer;
        // No image means the acquire in EndDraw failed (an out-of-date swapchain, i.e. a resize): the frame goes out like a
        // WITHHELD one - its work (stroke expansion, layout transitions, the shared surfaces' copies and semaphores) is
        // submitted, only nothing is shown. Waiting on the ImageAvailable semaphore, which nothing will signal, would hang
        // the device; dropping the frame lost work its semaphore signals stood for. The presenter self-heals on the next
        // frame (OutOfDate rebuild).
        var presents = Presenter is SwapChainGraphicsPresenter && !FrameWithheld && HasSwapchainImage;

        // Transition the swapchain image to PresentSrc inside the MAIN command buffer instead of a separate
        // per-frame single-time submit in Present() (which Present()'s guard then skips).
        if (presents && Presenter is SwapChainGraphicsPresenter presenterForLayout)
        {
            TransitionPresenterImageForPresent(commandBuffer, presenterForLayout.GetCurrentImage());
        }

        var result = commandBuffer.EndCommandBuffer();
        // unsafe
        // {
        //     Log.Logger.Debug($"EndCommandBuffer was called for {new IntPtr(commandBuffer.NativePointer)}");
        // }

        if (result != Result.Success)
        {
            throw new Exception("failed to record command buffer!");
        }

        CommandBufferStarted = false;
            
        var waits = new System.Collections.Generic.List<SemaphoreSubmitInfo>();
        var signals = new System.Collections.Generic.List<SemaphoreSubmitInfo>();

        if (presents && Presenter is SwapChainGraphicsPresenter swapChainGraphicsPresenter)
        {
            waits.Add(new SemaphoreSubmitInfo
            {
                Semaphore = ImageAvailableSemaphores[CurrentFrame],
                // The acquired image is first transitioned and blitted, both transfer work, so the wait covers the transfer stage.
                StageMask = (ulong)(PipelineStageFlagBits2.ColorAttachmentOutputBit | PipelineStageFlagBits2.AllTransferBit)
            });
            signals.Add(new SemaphoreSubmitInfo
            {
                Semaphore = swapChainGraphicsPresenter.CurrentRenderFinishedSemaphore,
                StageMask = (ulong)PipelineStageFlagBits2.AllCommandsBit
            });
        }

        for (int i = 0; i < _extraWaitSemaphores.Count; i++)
        {
            waits.Add(new SemaphoreSubmitInfo
            {
                Semaphore = _extraWaitSemaphores[i],
                Value = _extraWaitValues[i],
                StageMask = (ulong)_extraWaitStages[i]
            });
        }
        for (int i = 0; i < _extraSignalSemaphores.Count; i++)
        {
            signals.Add(new SemaphoreSubmitInfo
            {
                Semaphore = _extraSignalSemaphores[i],
                Value = _extraSignalValues[i],
                StageMask = (ulong)PipelineStageFlagBits2.AllCommandsBit
            });
        }
        _extraWaitSemaphores.Clear(); _extraWaitStages.Clear(); _extraWaitValues.Clear();
        _extraSignalSemaphores.Clear(); _extraSignalValues.Clear();

        var submitInfo = new SubmitInfo2
        {
            WaitSemaphoreInfoCount = (uint)waits.Count,
            PWaitSemaphoreInfos = waits.ToArray(),
            CommandBufferInfoCount = 1,
            PCommandBufferInfos = new[] { new CommandBufferSubmitInfo { CommandBuffer = CurrentCommandBuffer } },
            SignalSemaphoreInfoCount = (uint)signals.Count,
            PSignalSemaphoreInfos = signals.ToArray()
        };

        var renderFence = InFlightFences[CurrentFrame];
            
        result = LogicalDevice.ResetFences(1, renderFence);

        if (result != Result.Success)
        {
            Log.Logger.Error($"failed to reset fences. Result: {result}");
            //throw new Exception($"failed to reset fences. Result: {result}");
        }

        lock (GraphicsQueueLock)
        {
            result = GraphicsQueue.QueueSubmit2(1, submitInfo, renderFence);
        }

        _recordingFrame = false;

        if (result != Result.Success)
        {
            Log.Logger.Error($"failed to submit draw command buffer! Result was {result}");
        }

        CanPresent = !FrameWithheld && HasSwapchainImage;

        _submissionSync?.Release();
        MainDevice.Observe(result);
    }

    public void FrameEnded()
    {
        UpdateCurrentFrameNumber();
    }

    private void UpdateCurrentFrameNumber()
    {
        frame = (CurrentFrame + 1) % MaxFramesInFlight;
    }

    public void SetObjectDebugName(ulong objectHandle, ObjectType objectType, string name)
    {
        if (string.IsNullOrEmpty(name) || !VulkanInstance.IsExtensionEnabled(Constants.VK_EXT_DEBUG_UTILS_EXTENSION_NAME))
        {
            return;
        }

        var nameInfo = new DebugUtilsObjectNameInfoEXT
        {
            ObjectType = objectType,
            ObjectHandle = objectHandle,
            PObjectName = name
        };
        using var ctx = new NativeContext(nameInfo.GetSize(), stackalloc byte[(int)MarshalingUtils.StackAllocThreshold]);
        var infoPtr = nameInfo.MarshalToNative(ctx);
        var result = LogicalDevice.SetDebugUtilsObjectNameEXT(infoPtr);
        ResultHelper.CheckResult(result, nameof(SetObjectDebugName));
    }

    // A SPAN, not `params Viewport[]`. The generated binding underneath already takes a ReadOnlySpan - the whole binding
    // layer is written for zero allocation - and taking an array here allocated a one-element one at every call site,
    // undoing that at the last step. The tracking collections went with it: nothing reads them (Viewports/Scissors below
    // answer from the arrays), so re-filling one per call was work done for nobody on the hottest path in the renderer.
    public void SetViewports(params ReadOnlySpan<Viewport> viewports)
    {
        if (viewports.IsEmpty) return;

        // Unchanged means nothing to say: this used to marshal the viewport to the command buffer on every call, and the
        // draw that follows sends it AGAIN through SetDrawingState.
        if (viewports.Length == 1 && viewportsArray.Length == 1 && SameViewport(viewports[0], viewportsArray[0])) return;

        // Kept in an array WE own, refilled in place: a span cannot be stored, and re-allocating one per change would put
        // back the allocation this exists to remove. See CurrentViewports - it is a live view, not a snapshot.
        if (viewportsArray.Length != viewports.Length) viewportsArray = new Viewport[viewports.Length];
        viewports.CopyTo(viewportsArray);

        CurrentCommandBuffer.SetViewport(0, (uint)viewports.Length, InTarget(viewportsArray));
    }

    public void SetScissors(params ReadOnlySpan<Rect2D> scissors)
    {
        if (scissors.IsEmpty) return;

        // Same as SetViewports: a run of draws under one clip asked for the same rect over and over. Measured at 166
        // bytes a call before the span, and a replayed batch segment makes TWO of them - its own clip, then the full one
        // back - which was 53% of everything a replayed segment allocated.
        if (scissors.Length == 1 && scissorsArray.Length == 1 && SameScissor(scissors[0], scissorsArray[0])) return;

        if (scissorsArray.Length != scissors.Length) scissorsArray = new Rect2D[scissors.Length];
        scissors.CopyTo(scissorsArray);

        CurrentCommandBuffer.SetScissor(0, (uint)scissors.Length, InTarget(scissorsArray));
    }

    // The most recently set viewport/scissor, so code that interrupts the pass to render to another target (the font
    // renderer's offscreen text target) can save and restore the exact clip that was active rather than resetting it
    // to the full attachment (which left the resumed pass drawing unclipped).
    public Rect2D[] CurrentScissors => scissorsArray;
    public Viewport[] CurrentViewports => viewportsArray;

    /// <summary>Where the top-left corner of the target being drawn into lies in the coordinates viewports and scissors
    /// are given in. Zero for a target that is the whole surface; a target that holds one region of it (a layer) sets the
    /// region's corner, and everything keeps drawing in the surface's coordinates. <see cref="CurrentViewports"/> and
    /// <see cref="CurrentScissors"/> stay in those coordinates; only what reaches the GPU is moved. A shader that compares
    /// its fragment position with surface coordinates adds this to the fragment.</summary>
    public NativePoint TargetOrigin
    {
        get => new(_originX, _originY);
        set
        {
            if (value.X == _originX && value.Y == _originY)
            {
                return;
            }

            _originX = value.X;
            _originY = value.Y;
            _cViewportCount = 0;
            _cScissorCount = 0;
        }
    }

    private ReadOnlySpan<Viewport> InTarget(Viewport[] viewports)
    {
        if (_originX == 0 && _originY == 0)
        {
            return viewports;
        }

        if (_sentViewports.Length != viewports.Length)
        {
            _sentViewports = new Viewport[viewports.Length];
        }

        for (var i = 0; i < viewports.Length; i++)
        {
            var sent = _sentViewports[i] ??= new Viewport();
            var viewport = viewports[i];
            sent.X = viewport.X - _originX;
            sent.Y = viewport.Y - _originY;
            sent.Width = viewport.Width;
            sent.Height = viewport.Height;
            sent.MinDepth = viewport.MinDepth;
            sent.MaxDepth = viewport.MaxDepth;
        }

        return _sentViewports;
    }

    private ReadOnlySpan<Rect2D> InTarget(Rect2D[] scissors)
    {
        if (_originX == 0 && _originY == 0)
        {
            return scissors;
        }

        if (_sentScissors.Length != scissors.Length)
        {
            _sentScissors = new Rect2D[scissors.Length];
        }

        for (var i = 0; i < scissors.Length; i++)
        {
            long x = scissors[i].Offset.X - _originX;
            long y = scissors[i].Offset.Y - _originY;
            long width = scissors[i].Extent.Width;
            long height = scissors[i].Extent.Height;
            if (x < 0)
            {
                width += x;
                x = 0;
            }

            if (y < 0)
            {
                height += y;
                y = 0;
            }

            var sent = _sentScissors[i] ??= new Rect2D { Offset = new Offset2D(), Extent = new Extent2D() };
            sent.Offset.X = (int)x;
            sent.Offset.Y = (int)y;
            sent.Extent.Width = (uint)Math.Max(0, width);
            sent.Extent.Height = (uint)Math.Max(0, height);
        }

        return _sentScissors;
    }

    public void SetRenderTargets(params IRenderTarget[] renderTargets)
    {
        this.renderTargets = renderTargets;
        if (renderTargets.Length > 0)
        {
            CurrentRenderTarget = renderTargets[0];
        }
        else
        {
            CurrentRenderTarget = null;
        }
    }

    public void SetDepthBuffer(IDepthStencilBuffer depthBuffer)
    {
        this.depthBuffer = depthBuffer;
        if (depthBuffer != null)
        {
            CurrentDepthStencilBuffer = depthBuffer;
        }
    }

    public void SetVertexBuffer(IBuffer vertexBuffer)
    {
        ulong offset = 0;
        var commandBuffer = commandBuffers[CurrentFrame];
        commandBuffer.BindVertexBuffers(0U, 1U, vertexBuffer.GetBuffer(), offset);
    }

    public void SetVertexBuffers(params IBuffer[] vertexBuffers)
    {
        if (vertexBuffers == null || vertexBuffers.Length == 0) return;

        var offsets = new VkDeviceSize[vertexBuffers.Length];
        var commandBuffer = commandBuffers[CurrentFrame];
        var buffers = vertexBuffers.Select(x=>x.GetBuffer()).ToArray();
        commandBuffer.BindVertexBuffers(0, (uint)buffers.Length, buffers, offsets);
    }

    public void SetIndexBuffer(IBuffer indexBuffer)
    {
        var commandBuffer = commandBuffers[CurrentFrame];
        commandBuffer.BindIndexBuffer(indexBuffer.GetBuffer(), 0, IndexType.Uint32);
    }

    private void SetDrawingState(CommandBuffer commandBuffer)
    {
        // Viewport/scissor: only when they actually differ from what this command buffer was last told. They vary per clip
        // region, but a run of draws under ONE clip - which is what a batch segment is - re-sent the same rect every time.
        if (!_stateInitialized || viewportsArray.Length != _cViewportCount || (viewportsArray.Length == 1 && !SameViewport(viewportsArray[0], _cViewport)) || viewportsArray.Length > 1)
        {
            commandBuffer.SetViewportWithCount((uint)viewportsArray.Length, InTarget(viewportsArray));
            _cViewportCount = (uint)viewportsArray.Length;
            if (viewportsArray.Length == 1) _cViewport = viewportsArray[0];
        }

        if (!_stateInitialized || scissorsArray.Length != _cScissorCount || (scissorsArray.Length == 1 && !SameScissor(scissorsArray[0], _cScissor)) || scissorsArray.Length > 1)
        {
            commandBuffer.SetScissorWithCount((uint)scissorsArray.Length, InTarget(scissorsArray));
            _cScissorCount = (uint)scissorsArray.Length;
            if (scissorsArray.Length == 1) _cScissor = scissorsArray[0];
        }

        // Emit each remaining dynamic state only when it changed since the previous draw. The cache is
        // invalidated at BeginDraw, so the first draw of a command buffer still emits everything.
        if (!_stateInitialized || _cVertexType != VertexType)
        {
            if (VertexType == null)
            {
                // No vertex input: a draw whose vertices come only from SV_VertexID and whose per-instance data is read
                // from a storage buffer by SV_InstanceID (the instanced SDF batch). Bind an EMPTY vertex-input state so
                // the pipeline expects no vertex buffer (else GetBindingDescription2(null) throws + aborts the frame).
                commandBuffer.SetVertexInputEXT(0, default(VertexInputBindingDescription2EXT), 0, System.Array.Empty<VertexInputAttributeDescription2EXT>());
            }
            else
            {
                var bindingDescription = VertexType.GetBindingDescription2();
                var attributes = VertexType.GetVertexAttributeDescription2();
                commandBuffer.SetVertexInputEXT(1, bindingDescription, (uint)attributes.Length, attributes);
            }
            _cVertexType = VertexType;
        }
        if (!_stateInitialized || _cRasterizerDiscard != RasterizerDiscardEnabled)
        { commandBuffer.SetRasterizerDiscardEnable(RasterizerDiscardEnabled); _cRasterizerDiscard = RasterizerDiscardEnabled; }
        if (!_stateInitialized || _cTopology != PrimitiveTopology)
        { commandBuffer.SetPrimitiveTopology(PrimitiveTopology); _cTopology = PrimitiveTopology; }
        var primitiveRestart = PrimitiveRestartEnable && IsStripTopology(PrimitiveTopology);
        if (!_stateInitialized || _cPrimitiveRestart != primitiveRestart)
        { commandBuffer.SetPrimitiveRestartEnable(primitiveRestart); _cPrimitiveRestart = primitiveRestart; }
        if (!_stateInitialized || _cMsaa != MSAALevel)
        {
            commandBuffer.SetRasterizationSamplesEXT((SampleCountFlagBits)MSAALevel);
            commandBuffer.SetSampleMaskEXT((SampleCountFlagBits)MSAALevel, SampleMask);
            _cMsaa = MSAALevel;
        }
        if (!_stateInitialized || _cAlphaToCoverage != AlphaToCoverageEnable)
        { commandBuffer.SetAlphaToCoverageEnableEXT(AlphaToCoverageEnable); _cAlphaToCoverage = AlphaToCoverageEnable; }
        if (!_stateInitialized || _cPolygonMode != PolygonMode)
        { commandBuffer.SetPolygonModeEXT(PolygonMode); _cPolygonMode = PolygonMode; }

        if (PolygonMode == PolygonMode.Line)
        {
            commandBuffer.SetLineWidth(LineWidth);
        }

        if (!_stateInitialized || _cCullMode != CullMode)
        { commandBuffer.SetCullMode(CullMode); _cCullMode = CullMode; }
        if (!_stateInitialized || _cFrontFace != FrontFace)
        { commandBuffer.SetFrontFace(FrontFace); _cFrontFace = FrontFace; }
        if (!_stateInitialized || _cDepthWrite != DepthWriteEnable)
        { commandBuffer.SetDepthWriteEnable(DepthWriteEnable); _cDepthWrite = DepthWriteEnable; }
        if (!_stateInitialized || _cDepthTest != DepthTestEnabled)
        { commandBuffer.SetDepthTestEnable(DepthTestEnabled); _cDepthTest = DepthTestEnabled; }
        if (!_stateInitialized || _cDepthCompare != DepthCompareFunction)
        { commandBuffer.SetDepthCompareOp(DepthCompareFunction); _cDepthCompare = DepthCompareFunction; }
        if (!_stateInitialized || _cDepthBounds != DepthBoundsTestEnabled)
        { commandBuffer.SetDepthBoundsTestEnable(DepthBoundsTestEnabled); _cDepthBounds = DepthBoundsTestEnabled; }
        if (!_stateInitialized || _cDepthBias != DepthBiasEnabled)
        { commandBuffer.SetDepthBiasEnable(DepthBiasEnabled); _cDepthBias = DepthBiasEnabled; }
        if (DepthBiasEnabled && (!_depthBiasKnown || _cDepthBiasConstant != DepthBiasConstantFactor
                                                  || _cDepthBiasSlope != DepthBiasSlopeFactor))
        {
            commandBuffer.SetDepthBias(DepthBiasConstantFactor, 0, DepthBiasSlopeFactor);
            _cDepthBiasConstant = DepthBiasConstantFactor;
            _cDepthBiasSlope = DepthBiasSlopeFactor;
            _depthBiasKnown = true;
        }
        if (!_stateInitialized || _cDepthClamp != DepthClampEnable)
        { commandBuffer.SetDepthClampEnableEXT(DepthClampEnable); _cDepthClamp = DepthClampEnable; }
        if (!_stateInitialized || _cStencilTest != StencilTestEnabled)
        { commandBuffer.SetStencilTestEnable(StencilTestEnabled); _cStencilTest = StencilTestEnabled; }
        if (StencilTestEnabled)
        {
            commandBuffer.SetStencilOp(BothStencilFaces, StencilOp.Keep, StencilPassOp, StencilOp.Keep, StencilCompareOp);
            commandBuffer.SetStencilCompareMask(BothStencilFaces, 0xFF);
            commandBuffer.SetStencilWriteMask(BothStencilFaces, StencilWriteMask);
            commandBuffer.SetStencilReference(BothStencilFaces, StencilReference);
        }
        if (!_stateInitialized || _cLogicOp != LogicOperationsEnabled)
        { commandBuffer.SetLogicOpEnableEXT(LogicOperationsEnabled); _cLogicOp = LogicOperationsEnabled; }

        // Color-blend state DOES vary between UI draws (text uses premultiplied), which is why it used to be sent every
        // time - but comparing six enum fields is nothing against three marshaled calls, and a run of same-material draws
        // sends none of them.
        if (!_stateInitialized || !_cBlendKnown || !SameBlend(_cBlendEq, ColorBlendEquation))
        { commandBuffer.SetColorBlendEquationEXT(0, 1, ColorBlendEquation); _cBlendEq = ColorBlendEquation; }
        if (!_stateInitialized || !_cBlendKnown || _cBlendEnable != (bool)ColorBlendEnabled)
        { commandBuffer.SetColorBlendEnableEXT(0, 1, ColorBlendEnabled); _cBlendEnable = ColorBlendEnabled; }
        if (!_stateInitialized || !_cBlendKnown || _cWriteMask != ColorComponentFlags)
        { commandBuffer.SetColorWriteMaskEXT(0, 1, ColorComponentFlags); _cWriteMask = ColorComponentFlags; }
        _cBlendKnown = true;

        _stateInitialized = true;
    }

    public void Draw(ulong vertexCount, uint instanceCount, uint firstVertex = 0, uint firstInstance = 0)
    {
        if (CurrentEffectPass == null)
        {
            throw new ArgumentNullException("Effect pass should be applied before executing draw");
        }

        var commandBuffer = commandBuffers[CurrentFrame];
        SetDrawingState(commandBuffer);

        commandBuffer.Draw((uint)vertexCount, instanceCount, firstVertex, firstInstance);
    }

    // indexCount lets the caller draw fewer indices than the buffer holds - needed for an over-allocated/reused index
    // buffer (its capacity exceeds the live geometry). 0 means "the whole buffer" (its ElementCount), as before.
    public void DrawIndexed(IBuffer vertexBuffer, IBuffer indexBuffer, uint instanceCount = 1, uint indexCount = 0)
    {
        ulong offset = 0;
        var commandBuffer = commandBuffers[CurrentFrame];

        SetDrawingState(commandBuffer);

        commandBuffer.BindVertexBuffers(0, 1, vertexBuffer.GetBuffer(), offset);

        commandBuffer.BindIndexBuffer(indexBuffer.GetBuffer(), 0, IndexType.Uint32);

        commandBuffer.DrawIndexed(indexCount > 0 ? indexCount : (uint)indexBuffer.ElementCount, instanceCount, 0, 0, 0);
    }

    // Non-indexed instanced draw: binds the vertex buffer and draws vertexCount vertices as instanceCount instances. For
    // a mesh that is an explicit triangle list with no index buffer (how UI shape fills tessellate) - the non-indexed
    // twin of DrawIndexed; instancing (instanceCount) works identically, indices are not required.
    public void Draw(IBuffer vertexBuffer, uint vertexCount, uint instanceCount = 1)
    {
        ulong offset = 0;
        var commandBuffer = commandBuffers[CurrentFrame];

        // Bind the vertex buffer before SetDrawingState, which emits the dynamic vertex input (the SetVertexBuffer()+Draw()
        // order).
        commandBuffer.BindVertexBuffers(0, 1, vertexBuffer.GetBuffer(), offset);

        SetDrawingState(commandBuffer);

        commandBuffer.Draw(vertexCount, instanceCount, 0, 0);
    }

    // --- GPU-driven geometry (compute amplification -> indirect draw) ---------------------------------------------
    // Building blocks for the stroke/line compute pipeline: a compute pass writes vertices + a draw-args buffer into
    // SSBOs, a barrier makes those writes visible, then an indirect draw consumes them. The amount of geometry is
    // decided on the GPU, so the CPU never re-tessellates per frame.

    /// <summary>Dispatches the currently-bound compute shader. The compute pass (its shader + resource bindings) must
    /// be applied first, just as a draw needs an effect pass. Record this OUTSIDE a dynamic-rendering pass.</summary>
    public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
    {
        commandBuffers[CurrentFrame].Dispatch(groupCountX, groupCountY, groupCountZ);
    }

    /// <summary>Indirect draw: vertexCount/instanceCount/firstVertex/firstInstance come from <paramref name="indirectBuffer"/>
    /// (an array of VkDrawIndirectCommand, 16 bytes each), so a compute pass can size the draw on the GPU.
    /// </summary>
    public void DrawIndirect(IBuffer vertexBuffer, IBuffer indirectBuffer, uint drawCount = 1, uint stride = 16)
    {
        var commandBuffer = commandBuffers[CurrentFrame];
        SetDrawingState(commandBuffer);
        commandBuffer.BindVertexBuffers(0, 1, vertexBuffer.GetBuffer(), 0UL);
        commandBuffer.DrawIndirect(indirectBuffer.GetBuffer(), 0, drawCount, stride);
    }

    /// <summary>Buffer memory barrier on the current frame's command buffer (synchronization2). Used to make a compute
    /// SSBO write visible to a later read - e.g. compute write -> vertex-attribute / indirect-command read.
    /// </summary>
    public void BufferBarrier(IBuffer buffer, PipelineStageFlagBits2 srcStage, AccessFlagBits2 srcAccess,
        PipelineStageFlagBits2 dstStage, AccessFlagBits2 dstAccess)
    {
        // BufferMemoryBarrier2 exposes the raw interop flag types (unlike ImageMemoryBarrier2, which takes the friendly
        // enums); they implicitly-convert from ulong, so cast the enums through ulong.
        var barrier = new BufferMemoryBarrier2
        {
            SrcStageMask = (ulong)srcStage,
            SrcAccessMask = (ulong)srcAccess,
            DstStageMask = (ulong)dstStage,
            DstAccessMask = (ulong)dstAccess,
            SrcQueueFamilyIndex = ~0U,
            DstQueueFamilyIndex = ~0U,
            Buffer = buffer.GetBuffer(),
            Offset = 0,
            Size = ~0UL,   // VK_WHOLE_SIZE
        };
        var dependencyInfo = new DependencyInfo
        {
            PBufferMemoryBarriers = new[] { barrier },
            BufferMemoryBarrierCount = 1,
        };
        commandBuffers[CurrentFrame].PipelineBarrier2(dependencyInfo);
    }

    public CommandBuffer BeginSingleTimeCommand()
    {
        // Held across the whole single-time scope (until EndSingleTimeCommand): serializes command-pool access AND the
        // queue submit in EndSingleTimeCommand against the main render submit and other uploads (see _submissionSync).
        _submissionSync?.Wait();
        return LogicalDevice.BeginSingleTimeCommand(TransferCommandPool);
    }

    public void EndSingleTimeCommand(CommandBuffer commandBuffer)
    {
        commandBuffer.EndCommandBuffer();
        var fence = LogicalDevice.CreateFence(new FenceCreateInfo());
        var submitInfo = new SubmitInfo2
        {
            CommandBufferInfoCount = 1,
            PCommandBufferInfos = new[] { new CommandBufferSubmitInfo { CommandBuffer = commandBuffer } }
        };
        Result submitted;
        lock (GraphicsQueueLock)
        {
            submitted = GraphicsQueue.QueueSubmit2(1, submitInfo, fence);
        }

        var waited = LogicalDevice.WaitForFences(1, fence, true, ulong.MaxValue);
        LogicalDevice.FreeCommandBuffers(TransferCommandPool, 1, commandBuffer);
        LogicalDevice.DestroyFence(fence);
        _submissionSync?.Release();
        MainDevice.Observe(submitted);
        MainDevice.Observe(waited);
    }

    public void AddEffectPool(EffectPool pool)
    {
        EffectPools.Add(pool);
    }

    public void RemoveEffectPool(EffectPool pool)
    {
        EffectPools.Remove(pool);
    }

    public void Destroy(Adamantium.Vulkan.Core.Buffer buffer)
    {
        buffer?.Destroy(LogicalDevice);
    }

    public void Destroy(DeviceMemory deviceMemory)
    {
        deviceMemory?.FreeMemory(LogicalDevice);
    }

    public void Destroy(Image image)
    {
        image?.Destroy(LogicalDevice);
    }

    public void Destroy(ImageView imageView)
    {
        imageView?.Destroy(LogicalDevice);
    }

    public void Destroy(SwapchainKHR swapchain)
    {
        swapchain.Destroy(LogicalDevice);
    }
    
    public void Destroy(Semaphore semaphore)
    {
        LogicalDevice?.DestroySemaphore(semaphore);
    }

    public nuint MapMemory(DeviceMemory memory, ulong offset, ulong size, MemoryMapFlagBits flags)
    {
        return LogicalDevice.MapMemory(memory, offset, size, flags);
    }

    public void UnmapMemory(DeviceMemory memory)
    {
        LogicalDevice.UnmapMemory(memory);
    }

    public SamplerState CreateSampler(SamplerCreateInfo samplerInfo, string name)
    {
        return SamplerState.New(name, samplerInfo);
    }

    internal Semaphore GetImageAvailableSemaphoreForCurrentFrame()
    {
        return ImageAvailableSemaphores[CurrentFrame];
    }

    internal Semaphore GetRenderFinishedSemaphoreForCurrentFrame()
    {
        //Log.Logger.Debug($"Current frame index in GetRenderFinishedSemaphoreForCurrentFrame: {CurrentFrame}");
        return RenderFinishedSemaphores[CurrentFrame];
    }

    public static implicit operator Device(GraphicsDevice device)
    {
        return device.LogicalDevice;
    }

    public event EventHandler SurfaceSizeChanged;

    protected void OnSurfaceSizeChanged()
    {
        SurfaceSizeChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        base.Dispose(disposeManagedResources);

        if (_holdsGraphicsQueue)
        {
            _holdsGraphicsQueue = false;
            MainDevice.ReturnGraphicsQueue(_graphicsQueueIndex);
        }

        if (IsResourceLoaderDevice)
        {
            Log.Logger.Debug("Disposing Resource loading device");
            LogicalDevice?.FreeCommandBuffers(GraphicsCommandPool, (uint)commandBuffers.Length, commandBuffers);
            LogicalDevice?.DestroyCommandPool(GraphicsCommandPool);
            LogicalDevice?.DestroyCommandPool(TransferCommandPool);
        }
        else
        {
            Log.Logger.Debug("Disposing render device");
            DefaultEffectPool?.Dispose();

            // Counted by their OWN arrays, not by the command buffers': the three are created together but nothing keeps
            // their lengths tied, and freeing a fence per command buffer is how a device came to be destroyed with its
            // fences still alive.
            if (ImageAvailableSemaphores != null)
                foreach (var semaphore in ImageAvailableSemaphores) LogicalDevice?.DestroySemaphore(semaphore);

            if (InFlightFences != null)
                foreach (var fence in InFlightFences) LogicalDevice?.DestroyFence(fence);

            LogicalDevice?.FreeCommandBuffers(GraphicsCommandPool, (uint)commandBuffers.Length, commandBuffers);
            LogicalDevice?.DestroyCommandPool(GraphicsCommandPool);
            // CreateCommandPool makes TWO pools for every device - graphics and transfer. The render path destroyed only
            // the first, so every render device left a transfer pool behind on the logical device.
            LogicalDevice?.DestroyCommandPool(TransferCommandPool);
        }

        // Snapshot: each Dispose() now calls RemoveResource, which mutates the set.
        GraphicsResource[] resources;
        lock (_graphicsResourcesLock)
        {
            resources = _graphicsResources.ToArray();
        }

        foreach (var disposableObject in resources)
        {
            if (disposableObject.IsDisposed) continue;

            disposableObject?.Dispose();
        }

        // The device-memory allocator is SHARED and owned by the main device (disposed with it) - a render/resource
        // device must not dispose it here.
        _submissionSync?.Dispose();
        lock (_graphicsResourcesLock)
        {
            _graphicsResources.Clear();
        }
    }

    internal static IGraphicsDevice Create(MainGraphicsDevice device, GraphicsDeviceType deviceType)
    {
        return new GraphicsDevice(device, deviceType);
    }
}
