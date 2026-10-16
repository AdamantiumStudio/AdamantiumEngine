using System;
using System.Threading.Tasks;
using Adamantium.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;
using Adamantium.MVVM;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Core;

[ViewModel]
public abstract partial class GraphicsPresenter : DisposableObject
{
    // One color target and depth buffer per frame in flight, indexed by the fenced frame slot: a shared pair was redrawn
    // while the GPU still blitted the previous frame, which flickered.
    protected IRenderTarget[] renderTargets;
    protected IDepthStencilBuffer[] depthBuffers;
        
    [Bindable] private PresentInterval presentInterval;
    protected uint currentImageIndex;
        
    public IGraphicsDevice GraphicsDevice { get; private set; }

    public PresentationParameters Description { get; private set; }

    public uint BuffersCount => Description.BuffersCount;

    public uint Width => Description.Width;

    public uint Height => Description.Height;

    public SurfaceFormat SurfaceFormat => Description.ImageFormat;

    public DepthFormat DepthFormat => Description.DepthFormat;

    public MSAALevel MSAALevel => Description.MSAALevel;

    /// <summary>How many copies of the frame surfaces to keep - the pipeline depth. A presenter that is read back
    /// rather than presented (the designer's render-target presenter) overrides this to 1: it renders one frame and
    /// waits, so a ring would only make "which copy holds the result" ambiguous.</summary>
    protected virtual int FrameCopies => (int)Math.Max(1u, GraphicsDevice.MaxFramesInFlight);

    protected int FrameSlot => renderTargets == null || renderTargets.Length == 0
        ? 0
        : (int)(GraphicsDevice.CurrentFrame % (uint)renderTargets.Length);

    public IRenderTarget RenderTarget => renderTargets?[FrameSlot];

    public IDepthStencilBuffer DepthBuffer =>
        depthBuffers == null || depthBuffers.Length == 0 ? null : depthBuffers[FrameSlot % depthBuffers.Length];
    public Viewport Viewport { get; protected set; }
        
    public PresenterType PresenterType { get; private set; }
        
    public uint CurrentImageIndex => currentImageIndex;
        
    public bool CanPresent { get; protected set; }

    /// <summary>Whether every surface a frame draws into exists. False while a rebuild is under way and after one failed;
    /// the device draws nothing into the presenter until a rebuild succeeds.</summary>
    public bool IsReady { get; protected set; } = true;

    public ITexture[] BackBuffers { get; protected set; }

    protected GraphicsPresenter(IGraphicsDevice graphicsDevice, PresentationParameters description, String name = "")
    {
        Name = name;
        GraphicsDevice = graphicsDevice;
        Description = description.Clone();
        PresenterType = Description.PresenterType;
        CreateDepthBuffer();
        CreateViewPort();
    }

    protected void CreateDepthBuffer()
    {
        depthBuffers = new IDepthStencilBuffer[FrameCopies];
        for (var i = 0; i < depthBuffers.Length; i++)
        {
            depthBuffers[i] = ToDispose(GraphicsDevice.CreateDepthBuffer(Width, Height, DepthFormat, MSAALevel,
                name: $"{Name}+DepthBuffer{i}"));
        }
    }

    /// <summary>Frees every per-frame surface (both rings). Callers idle the device first - these images may still be
    /// read by a frame in flight.</summary>
    protected void DisposeFrameSurfaces()
    {
        if (depthBuffers != null)
        {
            for (var i = 0; i < depthBuffers.Length; i++)
            {
                RemoveAndDispose(ref depthBuffers[i]);
            }
        }

        if (renderTargets != null)
        {
            for (var i = 0; i < renderTargets.Length; i++)
            {
                RemoveAndDispose(ref renderTargets[i]);
            }
        }
    }

    private void CreateViewPort()
    {
        Viewport = new Viewport
        {
            X = 0,
            Y = 0,
            Width = Description.Width,
            Height = Description.Height,
            MinDepth = 0.0f,
            MaxDepth = 1.0f
        };
    }
        
    /// <summary>
    /// Resize graphics presenter backBuffer according to width and height
    /// </summary>
    public bool Resize(UInt32 width = 0, UInt32 height = 0)
    {
        Description.Width = width;
        Description.Height = height;
        return Resize(Description);
    }

    /// <summary>
    /// Resize graphics presenter backbuffer according to width and height
    /// </summary>
    /// <param name="parameters"></param>
    public virtual bool Resize(PresentationParameters parameters)
    {
        Description = parameters.Clone();
        CreateViewPort();
            
        return true;
    }
        
    /// <summary>Whether the images no longer match the surface they are presented to. Asked every frame: a swapchain that
    /// scales a stale image reports no error to listen for.</summary>
    public virtual bool NeedsRebuild => false;

    /// <summary>The size the surface says it is right now, or 0x0 when it has no opinion / there is no surface.</summary>
    public virtual Extent2D SurfaceExtent => new Extent2D();

    public abstract ITexture GetImageByIndex(uint index);

    public abstract ITexture GetCurrentImage();

    public virtual bool AcquireNextImage(Fence fence, Semaphore semaphore)
    {
        CanPresent = true;
        return true;
    }

    /// <summary>
    /// Present rendered image on screen
    /// </summary>
    public abstract PresenterState Present();
        
    public PresenterState LastPresenterState { get; protected set; }

    protected PresenterState ConvertState(Result result)
    {
        switch (result)
        {
            case Result.Success:
                return PresenterState.Success;
            case Result.SuboptimalKhr:
                return PresenterState.Suboptimal;
            case Result.ErrorDeviceLost:
                return PresenterState.DeviceLost;
            case Result.ErrorOutOfHostMemory:
                return PresenterState.OutOfHostMemory;
            case Result.ErrorOutOfDeviceMemory:
                return PresenterState.OutOfDeviceMemory;
            case Result.ErrorOutOfDateKhr:
                return PresenterState.OutOfDate;
            case Result.ErrorSurfaceLostKhr:
                return PresenterState.SurfaceLost;
            case Result.ErrorFullScreenExclusiveModeLostExt:
                return PresenterState.FullScreenExclusiveModeLost;
            default:
                return PresenterState.Unknown;
        }
    }
        
    public static GraphicsPresenter Create(IGraphicsDevice graphicsDevice, PresentationParameters parameters, string name = "")
    {
        switch (parameters.PresenterType)
        {
            case PresenterType.Swapchain:
                return new SwapChainGraphicsPresenter(graphicsDevice, parameters, name);
            case PresenterType.RenderTarget:
                return new RenderTargetGraphicsPresenter(graphicsDevice, parameters, name);
            case PresenterType.Headless:
                return new HeadlessPresenter(graphicsDevice, parameters, name);
            default:
                throw new NotSupportedException($"Presenter type: {parameters.PresenterType} is not supported");
        }
    }

    /// <summary>
    /// Takes screenshot from current backbuffer frame
    /// </summary>
    /// <param name="fileName">File path for image to save</param>
    /// <param name="fileType">Type of the saving image</param>
    public async Task TakeScreenshotAsync(String fileName, ImageFileType fileType)
    {
        await Task.Factory.StartNew(() =>
        {
            RenderTarget.Save(fileName, fileType);
        }, TaskCreationOptions.LongRunning);
    }

    protected virtual void CleanupSwapChain()
    {
            
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        base.Dispose(disposeManagedResources);
        CleanupSwapChain();
    }
}