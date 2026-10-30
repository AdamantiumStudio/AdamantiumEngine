using System;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Core.Presentation;

public class PresentationParameters
{
    public PresentationParameters(PresenterType presenterType)
    {
        PresenterType = presenterType;

        MSAALevel = MSAALevel.None;
        ImageFormat = Format.B8G8R8A8_UNORM;
        DepthFormat = DepthFormat.Depth32Stencil8X24;
        BuffersCount = 3;
        ImageColorSpace = ColorSpace.SRGBNonlinear;
        PresentPolicy = PresentPolicy.Adaptive;
    }

    public PresentationParameters(PresentationParameters parameters)
    {
        PresenterType = parameters.PresenterType;

        Width = parameters.Width;
        Height = parameters.Height;
        OutputHandle = parameters.OutputHandle;
        HInstanceHandle = parameters.HInstanceHandle;
        DisplayHandle = parameters.DisplayHandle;
        ImageFormat = parameters.ImageFormat;
        DepthFormat = parameters.DepthFormat;
        MSAALevel = parameters.MSAALevel;
        BuffersCount = parameters.BuffersCount;

        MinImageCount = parameters.MinImageCount;
        ImageColorSpace = parameters.ImageColorSpace;

        // Should be 1 for monoscopic and 2 for stereoscopic swapchain
        ImageArrayLayers = parameters.ImageArrayLayers;
        ImageUsage = parameters.ImageUsage;
        ImageSharingMode = parameters.ImageSharingMode;
        PreTransform = parameters.PreTransform;
        PresentMode = parameters.PresentMode;
        Clipped = parameters.Clipped;
        TransparentComposition = parameters.TransparentComposition;
        PresentPolicy = parameters.PresentPolicy;
        FrameSink = parameters.FrameSink;
    }

    /// <summary>What a <see cref="PresenterType.Readback"/> presenter hands its frames to.</summary>
    public IFrameSink FrameSink { get; set; }

    /// <summary>How this surface's frames should reach the screen. An intent, not a Vulkan mode - the presenter maps
    /// it onto whatever the surface actually offers, and Fifo is always there as the floor.</summary>
    public PresentPolicy PresentPolicy { get; set; }

    /// <summary>Ask the desktop to compose this surface with per-pixel alpha. Honored only when the surface offers it
    /// (<c>supportedCompositeAlpha</c>); otherwise the swapchain stays opaque and the caller is told.</summary>
    public bool TransparentComposition { get; set; }

    public PresentationParameters(
        PresenterType presenterType, 
        UInt32 width, 
        UInt32 height, 
        IntPtr handle, 
        MSAALevel msaaLevel = MSAALevel.None,
        Format imageFormat = Format.B8G8R8A8_UNORM,
        DepthFormat depthFormat = DepthFormat.Depth32Stencil8X24,
        UInt32 buffersCount = 3)
    {
        PresenterType = presenterType;

        Width = width;
        Height = height;
        OutputHandle = handle;

        MSAALevel = msaaLevel;
        ImageFormat = imageFormat;
        DepthFormat = depthFormat;
        BuffersCount = buffersCount;
    }

    public PresenterType PresenterType { get; }
    public UInt32 Width { get; set; }
    public UInt32 Height { get; set; }
    /// <summary>What is drawn into: on Windows the HWND, on Wayland the wl_surface, on Apple's platforms a CAMetalLayer -
    /// the view's layer, not the view.</summary>
    public IntPtr OutputHandle { get; set; }
    public IntPtr HInstanceHandle { get; set; }

    /// <summary>The connection the output's window belongs to, where the platform's surface needs one besides the
    /// window: on Wayland the wl_display, with the wl_surface as <see cref="OutputHandle"/>.</summary>
    public IntPtr DisplayHandle { get; set; }
    public SurfaceFormat ImageFormat { get; set; }
    public DepthFormat DepthFormat { get; set; }
    public MSAALevel MSAALevel { get; set; }
    public UInt32 BuffersCount { get; set; }
        
    public SwapchainCreateFlags Flags { get; set; }
        
    public uint MinImageCount { get; set; }
    public ColorSpace ImageColorSpace { get; set; }

    // Should be 1 for monoscopic and 2 for stereoscopic swapchain
    public uint ImageArrayLayers { get; set; }
    public ImageUsage ImageUsage { get; set; }
    public SharingMode ImageSharingMode { get; set; }
    public SurfaceTransform PreTransform { get; set; }
    public PresentMode PresentMode { get; set; }
    public bool Clipped { get; set; }

    public PresentationParameters Clone()
    {
        return new PresentationParameters(this);
    }
        
    public static PresentationParameters SwapchainParameters(
        uint width, 
        uint height, 
        IntPtr handle,
        MSAALevel msaaLevel,
        Format imageFormat = Format.B8G8R8A8_UNORM, 
        DepthFormat depthFormat = DepthFormat.Depth32Stencil8X24, 
        uint buffers = 3)
    {
        return new PresentationParameters(
            PresenterType.Swapchain, 
            width, 
            height, 
            handle, 
            msaaLevel,
            imageFormat, 
            depthFormat, 
            buffers);
    }

    public static PresentationParameters RenderTargetParameters(
        uint width, 
        uint height, 
        MSAALevel msaaLevel,
        Format imageFormat = Format.B8G8R8A8_UNORM, 
        DepthFormat depthFormat = DepthFormat.Depth32Stencil8X24, 
        uint buffers = 3)
    {
        return new PresentationParameters(
            PresenterType.RenderTarget, 
            width, 
            height, 
            IntPtr.Zero, 
            msaaLevel,
            imageFormat, 
            depthFormat, 
            buffers);
    }
}