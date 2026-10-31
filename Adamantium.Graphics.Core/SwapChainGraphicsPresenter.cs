using System;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Vulkan.Core;
using Serilog;

namespace Adamantium.Graphics.Core;

public class SwapChainGraphicsPresenter : GraphicsPresenter
{
    private SwapchainKHR swapchain;
    private SurfaceKHR surface;
    private IntPtr surfaceHandle;
    private ITexture[] swapchainTextures;
    private readonly Queue presentQueue;
    private readonly object _presentQueueLock;
    private readonly SwapchainKHR[] _swapchains;
    private Semaphore[] _waitSemaphores;
    private uint[] _imageIndicesArray;
        
    private Semaphore[] imageAvailableSemaphores;
    private Semaphore[] renderFinishedSemaphores;

    // ONE FENCE PER SWAPCHAIN IMAGE, signaled when the present engine is finished with that image AND with the
    // semaphores the present waited on - the thing an acquire alone does not tell us. Null when the device has no
    // swapchain_maintenance1, and then every path below falls back to idling the device.
    private Fence[] presentFences;
    private readonly Fence[] _presentFenceArray = new Fence[1];
    private readonly uint[] _releaseIndicesArray = new uint[1];
    private bool UsePresentFences => presentFences != null;

    // The modes THIS swapchain was created knowing about, and the one its presents currently ask for. Null when the
    // surface offered no alternatives, and then a policy change goes the long way round - a full rebuild.
    private PresentModeKHR[] _compatibleModes;
    private PresentModeKHR _presentMode;
    private bool _presentModeKnown;
    private readonly PresentModeKHR[] _presentModeArray = new PresentModeKHR[1];

    // What the live swapchain and its frame surfaces were actually built for. Resize is handed the SAME parameters
    // object the presenter already owns, so "did anything change" cannot be answered by comparing the two.
    private Extent2D _createdExtent;
    private MSAALevel _createdMsaa;
    private bool _createdTransparency;
    private bool _presentInfoReported;
    private bool _rebuildFailing;

    public SwapChainGraphicsPresenter(
        IGraphicsDevice graphicsDevice, 
        PresentationParameters description,
        string name = "") : base(graphicsDevice, description, name)
    {
        BackBuffers = new ITexture[BuffersCount];
        presentQueue = graphicsDevice.GraphicsQueue;
        _presentQueueLock = graphicsDevice.GraphicsQueueLock;
        _swapchains = new SwapchainKHR[1];
        _waitSemaphores = new Semaphore[1];
        _imageIndicesArray = new uint[1];
            
        CreateSurface();
        CreateSwapchain();
        // The base constructor sized the depth buffers from the WINDOW, before there was a swapchain to ask. The
        // swapchain may have chosen LARGER images than that - an image with margin is what lets a resize skip the
        // rebuild entirely - and a depth attachment smaller than the render area is invalid. Freed and rebuilt at
        // the size actually chosen; the render targets do not exist yet, so this frees only the depth buffers.
        DisposeFrameSurfaces();
        CreateRenderTarget();
        CreateDepthBuffer();
    }

    class SwapChainSupportDetails
    {
        public SurfaceCapabilitiesKHR Capabilities;
        public SurfaceFormatKHR[] Formats;
        public PresentModeKHR[] PresentModes;
    };
        
    /// <summary>Whether the desktop composes the swapchain with its alpha: transparency was asked for
    /// (<see cref="PresentationParameters.TransparentComposition"/>) and the surface offers it.</summary>
    public bool ComposesWithAlpha { get; private set; }

    public Semaphore CurrentRenderFinishedSemaphore => renderFinishedSemaphores[currentImageIndex];
    public Semaphore CurrentImageAvailableSemaphore => imageAvailableSemaphores[currentImageIndex];

    private void CreateSurface()
    {
        surface = GraphicsDevice.GetOrCreateSurface(Description);
        surfaceHandle = Description.OutputHandle;
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        base.Dispose(disposeManagedResources);

        if (surface != null)
        {
            GraphicsDevice.ReleaseSurface(surfaceHandle);
            surface = null;
        }
    }

    private void CreateRenderTarget()
    {
        renderTargets = new IRenderTarget[FrameCopies];
        for (var i = 0; i < renderTargets.Length; i++)
        {
            renderTargets[i] = ToDispose(GraphicsDevice.CreateRenderTarget(Width, Height, MSAALevel, SurfaceFormat,
                name: $"{Name}+RenderTarget{i}"));
        }
    }

    SwapChainSupportDetails QuerySwapChainSupport(PhysicalDevice device)
    {
        SwapChainSupportDetails details = new SwapChainSupportDetails();
        details.Capabilities = device.GetPhysicalDeviceSurfaceCapabilitiesKHR(surface);
        details.Formats = device.GetPhysicalDeviceSurfaceFormatsKHR(surface);
        details.PresentModes = device.GetPhysicalDeviceSurfacePresentModesKHR(surface);
        return details;
    }

    private void CreateSwapchain()
    {
        PhysicalDevice physicalDevice = GraphicsDevice.MainDevice;
        Device logicalDevice = GraphicsDevice.LogicalDevice;
        var swapChainSupport = QuerySwapChainSupport(physicalDevice);
        SurfaceFormatKHR surfaceFormat = ChooseSwapSurfaceFormat(swapChainSupport.Formats);
        PresentModeKHR presentMode = ChooseSwapPresentMode(swapChainSupport.PresentModes);
        // Asked BEFORE the extent is chosen: whether the images may be oversized is exactly what it answers, and a
        // swapchain that declares its scaling is no longer nailed to the surface's current size.
        var presentChain = QuerySurfacePresentInfo(physicalDevice, presentMode, out _compatibleModes);
        Extent2D extent = ChooseSwapExtent(swapChainSupport.Capabilities);
        uint imageCount = swapChainSupport.Capabilities.MinImageCount;
        if (imageCount < Description.BuffersCount)
        {
            imageCount = Description.BuffersCount;
        }

        // One image per frame in flight plus one for the present engine, or the last frame blocks in acquire; the
        // surface still clamps it below.
        var wanted = GraphicsDevice.MaxFramesInFlight + 1;
        if (imageCount < wanted)
        {
            imageCount = wanted;
        }

        if (swapChainSupport.Capabilities.MaxImageCount > 0 && imageCount > swapChainSupport.Capabilities.MaxImageCount)
        {
            imageCount = swapChainSupport.Capabilities.MaxImageCount;
        }

        var createInfo = new SwapchainCreateInfoKHR();
        createInfo.Surface = surface;

        createInfo.MinImageCount = imageCount;
        createInfo.ImageFormat = surfaceFormat.Format;
        createInfo.ImageColorSpace = surfaceFormat.ColorSpace;
        createInfo.ImageExtent = extent;
        createInfo.ImageArrayLayers = 1;
        createInfo.ImageUsage = ImageUsageFlagBits.ColorAttachmentBit | ImageUsageFlagBits.TransferDstBit;

        var graphicsFamily =
            GraphicsDevice.MainDevice.QueueFamilyContainer.GetFamilyInfo(QueueFlagBits.GraphicsBit);
        var presentFamilyIndex = GraphicsDevice.MainDevice.QueueFamilyContainer.GetPresentFamilyIndex(surface);
            
        if (graphicsFamily.FamilyIndex != presentFamilyIndex)
        {
            var queueFamilyIndices = new[] { graphicsFamily.FamilyIndex, presentFamilyIndex };
            createInfo.ImageSharingMode = SharingMode.Concurrent;
            createInfo.QueueFamilyIndexCount = (uint)queueFamilyIndices.Length;
            createInfo.PQueueFamilyIndices = queueFamilyIndices;
        }
        else
        {
            createInfo.ImageSharingMode = SharingMode.Exclusive;
        }

        createInfo.PreTransform = swapChainSupport.Capabilities.CurrentTransform;

        // What the SURFACE says it can do, not what we assume: composite alpha is a property of the surface, and in
        // one measured run two surfaces in the SAME process answered differently - one offered pre-multiplied, the
        // other opaque only. So it is asked for, checked, and fallen back on out loud.
        var supportedAlpha = swapChainSupport.Capabilities.SupportedCompositeAlpha;
        var wantsAlpha = Description.TransparentComposition;
        var canDoAlpha = surface.ComposesWithAlpha(physicalDevice);

        ComposesWithAlpha = wantsAlpha && canDoAlpha;
        createInfo.CompositeAlpha = ComposesWithAlpha
            ? CompositeAlphaFlagBitsKHR.PreMultipliedBitKhr
            : CompositeAlphaFlagBitsKHR.OpaqueBitKhr;

        if (wantsAlpha && !canDoAlpha)
        {
            Console.WriteLine("[Swapchain] transparency requested but this surface only offers " +
                              $"{supportedAlpha} - the window will be opaque.");
        }
        createInfo.PresentMode = presentMode;
        createInfo.Clipped = true;
        createInfo.PNext = presentChain;
        SetPresentMode(presentMode, "rebuilt the swapchain");
        _createdExtent = extent;
        _createdMsaa = Description.MSAALevel;
        _createdTransparency = Description.TransparentComposition;

        swapchain = logicalDevice.CreateSwapchainKHR(createInfo);
        Description.Width = extent.Width;
        Description.Height = extent.Height;
        Description.ImageFormat = surfaceFormat.Format;

        var images = logicalDevice.GetSwapchainImagesKHR(swapchain);
        swapchainTextures = new ITexture[images.Length];
        for (int i = 0; i < images.Length; i++)
        {
            swapchainTextures[i] =
                GraphicsDevice.CreateTextureFromImage(
                    images[i], 
                    Width, 
                    Height, 
                    Description.MSAALevel,
                    SurfaceFormat,
                    desiredLayout: ImageLayout.Undefined,
                    name:$"SwapchainImage_{i}");
        }
        CreateImageViews();
        _swapchains[0] = swapchain;
            
        var semaphoreInfo = new SemaphoreCreateInfo();
        imageAvailableSemaphores = new Semaphore[swapchainTextures.Length];
        renderFinishedSemaphores = new Semaphore[swapchainTextures.Length];

        for (int i = 0; i < swapchainTextures.Length; i++)
        {
            imageAvailableSemaphores[i] = logicalDevice.CreateSemaphore(semaphoreInfo);
            renderFinishedSemaphores[i] = logicalDevice.CreateSemaphore(semaphoreInfo);
        }

        if (GraphicsDevice.MainDevice.SupportsSwapchainMaintenance)
        {
            // Created ALREADY SIGNALED: the first present of an image has no earlier present of it to wait for.
            var fenceInfo = new FenceCreateInfo { Flags = FenceCreateFlagBits.SignaledBit };
            presentFences = logicalDevice.CreateFences(fenceInfo, (uint)swapchainTextures.Length);
        }
    }

    /// <summary>Everything the swapchain wants to know that depends on the SURFACE AND THE PRESENT MODE TOGETHER -
    /// the same surface answers differently for Fifo and for Immediate, so the mode travels with the question.
    /// Returns the head of a pNext chain for the swapchain create info, and hands back the modes this swapchain
    /// will be allowed to switch between afterwards.</summary>
    private object QuerySurfacePresentInfo(PhysicalDevice physicalDevice, PresentModeKHR presentMode,
        out PresentModeKHR[] compatibleModes)
    {
        compatibleModes = null;

        if (!GraphicsDevice.MainDevice.SupportsSwapchainMaintenance) return null;

        var scaling = new SurfacePresentScalingCapabilitiesKHR();
        var compatibility = new SurfacePresentModeCompatibilityKHR();
        scaling.PNext = compatibility;

        var surfaceInfo = new PhysicalDeviceSurfaceInfo2KHR
        {
            Surface = surface,
            PNext = new SurfacePresentModeKHR { PresentMode = presentMode }
        };
        var capabilities = new SurfaceCapabilities2KHR { PNext = scaling };

        // First pass answers the scaling flags outright and, for the mode list, only HOW MANY there are.
        var result = physicalDevice.GetPhysicalDeviceSurfaceCapabilities2KHR(surfaceInfo, ref capabilities);

        if (result != Result.Success)
        {
            Log.Logger.Error($"Could not ask the surface about present scaling. Result: {result}");
            return null;
        }

        if (compatibility.PresentModeCount > 1)
        {
            // Second pass with room to write them into. One mode means "only the one we asked for", and a list of
            // one is not worth carrying - the swapchain would have nothing to switch to.
            compatibility.PresentModes = new PresentModeKHR[compatibility.PresentModeCount];
            result = physicalDevice.GetPhysicalDeviceSurfaceCapabilities2KHR(surfaceInfo, ref capabilities);

            if (result == Result.Success)
            {
                compatibleModes = compatibility.PresentModes.ToArray();
            }
        }

        object chain = null;

        if (compatibleModes != null)
        {
            // Declaring the whole set UP FRONT is what buys the later switch: a swapchain may only be asked to
            // present in a mode it was created knowing about.
            chain = new SwapchainPresentModesCreateInfoKHR
            {
                PresentModeCount = (uint)compatibleModes.Length,
                PresentModes = compatibleModes
            };
        }

        // Stretch, always: during a drag it scales the stale image by the frame lag, a fraction of a percent. One-to-one
        // leaves bare bands whenever the drag reverses, since the choice is fixed for the swapchain's life.
        var oneToOne = false;

        if (oneToOne)
        {
            // Anchored top-left, which is where a window's content is anchored anyway: growing a window from its
            // bottom-right corner then leaves the gap where the new space appeared, not across the whole picture.
            chain = new SwapchainPresentScalingCreateInfoKHR
            {
                ScalingBehavior = PresentScalingFlagBitsKHR.OneToOneBitKhr,
                PresentGravityX = PresentGravityFlagBitsKHR.MinBitKhr,
                PresentGravityY = PresentGravityFlagBitsKHR.MinBitKhr,
                PNext = chain
            };
        }
        else if (scaling.SupportedPresentScaling.HasFlag(PresentScalingFlagBitsKHR.StretchBitKhr))
        {
            // Gravity has no corner to pick when the image fills the window, so both axes are left alone.
            chain = new SwapchainPresentScalingCreateInfoKHR
            {
                ScalingBehavior = PresentScalingFlagBitsKHR.StretchBitKhr,
                PNext = chain
            };
        }
        else
        {
            Console.WriteLine($"[Swapchain] this surface offers {scaling.SupportedPresentScaling} scaling - " +
                              "neither one-to-one nor stretch, so resize will look as it did before.");
        }

        if (!_presentInfoReported)
        {
            // Said out loud ONCE per presenter, because everything above is a capability that may quietly not be
            // there: a feature that turned itself off without a word is indistinguishable from one that works.
            // Resize recreates the swapchain, so this reports the first build and then stays quiet.
            _presentInfoReported = true;
            var modes = compatibleModes == null ? "none" : string.Join(", ", compatibleModes);
            Console.WriteLine($"[Swapchain] scaling offered={scaling.SupportedPresentScaling}, " +
                              $"chosen={(oneToOne ? "OneToOne+topLeft" : "Stretch")}, " +
                              $"switchable present modes for {presentMode}: {modes}");
        }

        return chain;
    }

    private void CreateImageViews()
    {
        Device logicalDevice = GraphicsDevice.LogicalDevice;

        for (int i = 0; i < swapchainTextures.Length; i++)
        {
            var createInfo = new ImageViewCreateInfo();
            createInfo.Image = swapchainTextures[i].GetImage();
            createInfo.ViewType = ImageViewType._2d;
            createInfo.Format = SurfaceFormat;
            ComponentMapping componentMapping = new ComponentMapping();
            componentMapping.R = ComponentSwizzle.Identity;
            componentMapping.G = ComponentSwizzle.Identity;
            componentMapping.B = ComponentSwizzle.Identity;
            componentMapping.A = ComponentSwizzle.Identity;
            createInfo.Components = componentMapping;
            ImageSubresourceRange subresourceRange = new ImageSubresourceRange();
            subresourceRange.AspectMask = ImageAspectFlagBits.ColorBit;
            subresourceRange.BaseMipLevel = 0;
            subresourceRange.LevelCount = 1;
            subresourceRange.BaseArrayLayer = 0;
            subresourceRange.LayerCount = 1;
            createInfo.SubresourceRange = subresourceRange;

            swapchainTextures[i].SetImageView(logicalDevice.CreateImageView(createInfo));
        }
    }
        
    SurfaceFormatKHR ChooseSwapSurfaceFormat(SurfaceFormatKHR[] availableFormats)
    {
        if (availableFormats.Length == 1 && availableFormats[0].Format == Format.UNDEFINED)
        {
            return new SurfaceFormatKHR() { Format = Format.B8G8R8A8_UNORM, ColorSpace = ColorSpaceKHR.SrgbNonlinearKhr };
        }

        foreach (var availableFormat in availableFormats)
        {
            if (availableFormat.Format == Description.ImageFormat && availableFormat.ColorSpace == (ColorSpaceKHR)Description.ImageColorSpace)
            {
                return availableFormat;
            }
        }

        return availableFormats[0];
    }

    /// <summary>The window's policy expressed as a present mode, before anything checks it is on offer.</summary>
    private PresentModeKHR WantedPresentMode()
    {
        return Description.PresentPolicy switch
        {
            PresentPolicy.Immediate => PresentModeKHR.ImmediateKhr,
            PresentPolicy.Adaptive => PresentModeKHR.MailboxKhr,
            _ => PresentModeKHR.FifoKhr
        };
    }

    PresentModeKHR ChooseSwapPresentMode(PresentModeKHR[] availablePresentModes)
    {
        // Immediate first, then Mailbox, then Fifo: Mailbox returns images on the display's schedule, and the blocking
        // acquire then paces the whole frame loop.
        var wanted = WantedPresentMode();

        foreach (var availablePresentMode in availablePresentModes)
        {
            if (availablePresentMode == wanted) return wanted;
        }

        // Second choice: an Immediate that is not offered falls to Mailbox - still unthrottled from the application's
        // side. Anything else falls to Fifo, which every driver must support, rather than to tearing nobody asked for.
        if (Description.PresentPolicy == PresentPolicy.Immediate)
        {
            foreach (var availablePresentMode in availablePresentModes)
            {
                if (availablePresentMode == PresentModeKHR.MailboxKhr) return PresentModeKHR.MailboxKhr;
            }
        }

        return PresentModeKHR.FifoKhr;
    }

    Extent2D ChooseSwapExtent(SurfaceCapabilitiesKHR capabilities)
    {
        var actualExtent = new Extent2D() { Width = Description.Width, Height = Description.Height };

        actualExtent.Width = Math.Max(capabilities.MinImageExtent.Width, Math.Min(capabilities.MaxImageExtent.Width, actualExtent.Width));
        actualExtent.Height = Math.Max(capabilities.MinImageExtent.Height, Math.Min(capabilities.MaxImageExtent.Height, actualExtent.Height));

        return actualExtent;
    }

    public override Extent2D SurfaceExtent
    {
        get
        {
            // 0x0 rather than null for "no answer": Extent2D is a wrapper class, and a null here would make every
            // caller guard a value that is only ever read for its two numbers.
            if (swapchain == null) return new Extent2D();

            PhysicalDevice physicalDevice = GraphicsDevice.MainDevice;
            var current = physicalDevice.GetPhysicalDeviceSurfaceCapabilitiesKHR(surface).CurrentExtent;

            // 0xFFFFFFFF means "the surface has no opinion, you choose" - no answer to give.
            if (current == null || current.Width == uint.MaxValue || current.Height == uint.MaxValue)
            {
                return new Extent2D();
            }

            return current;
        }
    }

    public override bool NeedsRebuild
    {
        get
        {
            var current = SurfaceExtent;

            // A window collapsed to nothing has no size to match; rebuilding into a zero extent is invalid.
            if (current.Width == 0 || current.Height == 0) return false;

            return current.Width != Description.Width || current.Height != Description.Height;
        }
    }

    public override ITexture GetImageByIndex(uint index)
    {
        return swapchainTextures[index];
    }

    public override ITexture GetCurrentImage()
    {
        return swapchainTextures[CurrentImageIndex];
    }

    public override bool AcquireNextImage(Fence fence, Semaphore semaphore)
    {
        // Finite timeout (1s), NOT UINT64_MAX: the spec forbids an infinite timeout when forward progress cannot be
        // guaranteed (VUID-vkAcquireNextImageKHR-surface-07783), and an infinite wait turned a transient swapchain
        // stall into a HARD freeze - the render thread blocked in AcquireNextImage forever.
        var result = GraphicsDevice.MainDevice.Observe(
            GraphicsDevice.LogicalDevice.AcquireNextImageKHR(
                this,
                1_000_000_000UL,   // 1 second, in nanoseconds
                semaphore, null,
                ref currentImageIndex));

        if (result == Result.ErrorOutOfDateKhr)
        {
            LastPresenterState = ConvertState(result);
            CanPresent = false;
            return false;
        }

        if (result != Result.Success && result != Result.SuboptimalKhr)
        {
            // Timeout or a real error: flag the swapchain OutOfDate so the render service recreates it next frame
            // (the existing self-heal at WindowRenderService.BeginDraw), instead of the render thread spinning on a
            // permanently-failing acquire.
            Log.Logger.Error($"Failed to acquire swap chain image. Result: {result}. Flagging OutOfDate to self-heal.");
            LastPresenterState = PresenterState.OutOfDate;
            CanPresent = false;
            return false;
        }

        CanPresent = true;
        return CanPresent;
    }

    /// <summary>
    /// Present rendered image on screen
    /// </summary>
    public override PresenterState Present()
    {
        //var fence = GraphicsDevice.GetCurrentFence();
        // GraphicsDevice.LogicalDevice.WaitForFences(1U, fence, true, ulong.MaxValue);
        // GraphicsDevice.LogicalDevice.ResetFences(1u, fence);

        if (!CanPresent) return LastPresenterState;
        // One present per acquire. CanPresent is set by AcquireNextImage; consume it here so a later frame that did
        // NOT re-acquire (a skip/recreate during resize churn) can't re-present this already-presented, not-
        // reacquired image. That was the "image not acquired since last present" + "semaphore has no way to be
        // signaled" pair that lost the device on drag-resize.
        CanPresent = false;

        var presenterImage = GetCurrentImage();

        if (presenterImage.ImageLayout != ImageLayout.PresentSrcKhr)
        {
            presenterImage.TransitionImageLayout(ImageLayout.PresentSrcKhr);
        }
            
        //GraphicsDevice.LogicalDevice.WaitForFences(1U, fence, true, ulong.MaxValue);
        var presentInfo = FillPresentInfo(CurrentImageIndex);
        Result result;
        lock (_presentQueueLock)
        {
            result = presentQueue.QueuePresentKHR(presentInfo);
        }

        GraphicsDevice.MainDevice.Observe(result);

        if (result != Result.Success && result != Result.SuboptimalKhr)
        {
            Log.Logger.Error($"Failed to present swap chain image. Operation result was: {result}");
        }
            
        LastPresenterState = ConvertState(result);

        return LastPresenterState;
    }
        
    private PresentInfoKHR FillPresentInfo(uint imageIndex)
    {
        _waitSemaphores[0] = renderFinishedSemaphores[imageIndex];
        _imageIndicesArray[0] = imageIndex;

        var presentInfo = new PresentInfoKHR();
        presentInfo.WaitSemaphoreCount = 1;
        presentInfo.PWaitSemaphores = _waitSemaphores;

        presentInfo.SwapchainCount = (uint)_swapchains.Length;
        presentInfo.PSwapchains = _swapchains;
        presentInfo.PImageIndices = _imageIndicesArray;

        object chain = null;

        if (_compatibleModes != null)
        {
            // Sent on EVERY present once the swapchain was created with a mode list, not only when the mode has
            // just changed: the present engine is then told the mode by the present itself, and there is no state
            // to keep in step between here and it.
            _presentModeArray[0] = _presentMode;
            chain = new SwapchainPresentModeInfoKHR
            {
                SwapchainCount = (uint)_swapchains.Length,
                PresentModes = _presentModeArray
            };
        }

        if (UsePresentFences)
        {
            // The fence must be unsignaled when the present is queued, so the PREVIOUS present of this same image
            // is settled first. It practically always is - we only got here because the image was re-acquired - so
            // the wait is a formality that costs nothing, and it is what makes renderFinishedSemaphores[imageIndex]
            // safe to hand to the queue again.
            var fence = presentFences[imageIndex];
            GraphicsDevice.MainDevice.Observe(GraphicsDevice.LogicalDevice.WaitForFences(1, fence, true, 1_000_000_000UL));
            GraphicsDevice.LogicalDevice.ResetFences(1, fence);

            _presentFenceArray[0] = fence;
            chain = new SwapchainPresentFenceInfoKHR
            {
                SwapchainCount = (uint)_swapchains.Length,
                PFences = _presentFenceArray,
                PNext = chain
            };
        }

        presentInfo.PNext = chain;

        return presentInfo;
    }

    /// <summary>
    /// Resize graphics presenter backbuffer according to width and height
    /// </summary>
    /// <param name="parameters"></param>
    public override bool Resize(PresentationParameters parameters)
    {
        if (!base.Resize(parameters))
        {
            return false;
        }

        if (TrySwitchPresentModeInPlace())
        {
            return true;
        }

        try
        {
            RecreateSwapchain();
        }
        catch(Exception ex)
        {
            if (!_rebuildFailing)
            {
                Log.Logger.Error($"Swapchain rebuild failed; frames are skipped until a rebuild succeeds: {ex}");
            }

            _rebuildFailing = true;
            CanPresent = false;
            LastPresenterState = PresenterState.OutOfDate;
            return false;
        }

        if (_rebuildFailing)
        {
            Log.Logger.Information("Swapchain rebuild succeeded after failing; drawing again.");
        }

        _rebuildFailing = false;
        return true;
    }

    /// <summary>Answers the rebuild request without rebuilding, when the only thing it really asks for is a
    /// different present mode - a V-Sync toggle used to cost a device wait, a swapchain, every render target and
    /// every depth buffer. False means the request is a real one and the long road has to be taken.</summary>
    private bool TrySwitchPresentModeInPlace()
    {
        if (_compatibleModes == null) return false;

        // A swapchain the surface has already disowned has to be rebuilt whatever else the caller wanted - this is
        // the self-heal path, where the sizes match and only the presenter's own state says anything is wrong.
        if (LastPresenterState is PresenterState.OutOfDate or PresenterState.SurfaceLost) return false;

        // Everything the images themselves depend on must be untouched - a size, sample count or transparency
        // change is not a present-mode question and no chained structure can answer it.
        if (Width != _createdExtent.Width || Height != _createdExtent.Height) return false;
        if (Description.MSAALevel != _createdMsaa) return false;
        if (Description.TransparentComposition != _createdTransparency) return false;

        var wanted = WantedPresentMode();

        if (wanted == _presentMode) return true;
        if (Array.IndexOf(_compatibleModes, wanted) < 0) return false;

        SetPresentMode(wanted, "switched it live");
        return true;
    }

    /// <summary>Said out loud because it is a change the USER asked for and cannot otherwise confirm: a policy that
    /// silently kept the old mode is indistinguishable from one that took effect. Rare - only on a policy change or
    /// a swapchain rebuild that lands on a different mode - so it cannot become noise.</summary>
    private void SetPresentMode(PresentModeKHR mode, string how)
    {
        var had = _presentMode;
        var wasKnown = _presentModeKnown;

        _presentMode = mode;
        _presentModeKnown = true;

        // The first swapchain has no previous mode to have moved away from - reporting one would be a fiction.
        if (!wasKnown || had == mode) return;

        Console.WriteLine($"[Swapchain] present mode {had} -> {mode} ({how}).");
    }

    private void RecreateSwapchain()
    {
        WaitForFramesToRetire();
        ReleaseAcquiredImage();
        IsReady = false;
        CleanupSwapChain();
        CreateSwapchain();
        CreateRenderTarget();
        CreateDepthBuffer();
        IsReady = true;
    }

    /// <summary>Waits until nothing is still reading the images this rebuild is about to destroy.</summary>
    private void WaitForFramesToRetire()
    {
        if (!UsePresentFences)
        {
            GraphicsDevice.DeviceWaitIdle();
            return;
        }

        // Two waits rather than one: the present fences cover every image the present engine still holds, the
        // device's own frame fences cover a frame that rendered into an image and never reached its present.
        // Between them nothing is left reading the images - and neither wait stops the OTHER windows, which share
        // this VkDevice and were all being stalled by the DeviceWaitIdle this replaces.
        var result = GraphicsDevice.MainDevice.Observe(GraphicsDevice.LogicalDevice.WaitForFences(
            (uint)presentFences.Length, presentFences, true, 1_000_000_000UL));

        if (result != Result.Success)
        {
            // A fence that never signaled leaves the images' state unknown, and destroying a fence still in use is
            // invalid - so fall back to the blunt wait rather than carry on with a guess.
            Log.Logger.Error($"Present fences did not settle before a swapchain rebuild ({result}); idling the device.");
            GraphicsDevice.DeviceWaitIdle();
            return;
        }

        GraphicsDevice.WaitForFramesInFlight(1_000_000_000UL);
    }

    /// <summary>Hands back an image that was acquired but never presented - a resize landing between the two.</summary>
    private void ReleaseAcquiredImage()
    {
        // CanPresent is set by the acquire and consumed by the present, so it standing here means exactly that.
        if (!UsePresentFences || !CanPresent) return;

        CanPresent = false;
        _releaseIndicesArray[0] = CurrentImageIndex;

        var releaseInfo = new ReleaseSwapchainImagesInfoKHR
        {
            Swapchain = swapchain,
            ImageIndexCount = 1,
            PImageIndices = _releaseIndicesArray
        };

        var result = GraphicsDevice.LogicalDevice.ReleaseSwapchainImagesKHR(releaseInfo);

        if (result != Result.Success)
        {
            Log.Logger.Error($"Failed to release swapchain image {_releaseIndicesArray[0]}. Result: {result}");
        }
    }

    protected override void CleanupSwapChain()
    {
        if (swapchainTextures != null)
        {
            foreach (var texture in swapchainTextures)
            {
                texture?.Dispose();
            }

            swapchainTextures = null;
        }

        DisposeFrameSurfaces();

        if (imageAvailableSemaphores != null)
        {
            for (int i = 0; i < imageAvailableSemaphores.Length; i++)
            {
                if (imageAvailableSemaphores[i] != null)
                {
                    GraphicsDevice.Destroy(imageAvailableSemaphores[i]);
                }

                if (renderFinishedSemaphores[i] != null)
                {
                    GraphicsDevice.Destroy(renderFinishedSemaphores[i]);
                }
            }

            imageAvailableSemaphores = null;
            renderFinishedSemaphores = null;
        }

        if (presentFences != null)
        {
            for (int i = 0; i < presentFences.Length; i++)
            {
                GraphicsDevice.LogicalDevice.DestroyFence(presentFences[i]);
            }

            presentFences = null;
        }

        if (swapchain != null)
        {
            GraphicsDevice.Destroy(swapchain);
            swapchain = null;
        }
    }

    public static implicit operator SwapchainKHR(SwapChainGraphicsPresenter presenter)
    {
        return presenter.swapchain;
    }
}