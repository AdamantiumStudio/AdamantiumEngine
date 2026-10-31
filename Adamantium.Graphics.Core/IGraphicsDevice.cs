using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.EffectsCompiler;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;
using Adamantium.Vulkan.Core.Interop;
using Buffer = Adamantium.Vulkan.Core.Buffer;
using EffectTechnique = Adamantium.Graphics.Core.EffectsFramework.EffectTechnique;
using Image = Adamantium.Vulkan.Core.Image;

namespace Adamantium.Graphics.Core;

public interface IDrawableDevice
{
    bool BeginDraw(float depth = 1.0f, uint stencil = 0, Action<CommandBuffer> beforeRenderPass = null);

    /// <summary>Why the last <see cref="BeginDraw"/> returned false (a device/surface error), else null.</summary>
    string LastFrameError { get; }

    void BeginRendering(CommandBuffer commandBuffer, bool continueRendering = false, float depth = 1.0f, uint stencil = 0);
    
    void EndDraw();

    void Submit();

    void FrameEnded();

    /// <summary>Blocks until the device has completed all pending work (e.g. before reading a target back).</summary>
    Result DeviceWaitIdle();

    /// <summary>Blocks until every frame THIS device still has in flight has retired. The VkDevice is shared by every
    /// window and by the resource loader, so <see cref="DeviceWaitIdle"/> to rebuild one window's swapchain also stops
    /// the other windows and any upload in progress; this waits on this device's own frame fences and nothing else.</summary>
    Result WaitForFramesInFlight(ulong timeout);

    void Draw(ulong vertexCount, uint instanceCount, uint firstVertex = 0, uint firstInstance = 0);
    
    void DrawIndexed(IBuffer vertexBuffer, IBuffer indexBuffer, uint instanceCount = 1, uint indexCount = 0);

    /// <summary>Draws with the applied mesh pass: launches this many workgroups of its task shader, or of its mesh
    /// shader when it has no task shader.</summary>
    /// <exception cref="ArgumentOutOfRangeException">More workgroups than the device's limits for that stage.</exception>
    void DrawMeshTasks(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1);
}

public interface IDestroyableDevice
{
    void Destroy(Buffer buffer);
    void Destroy(DeviceMemory deviceMemory);
    void Destroy(Image image);
    void Destroy(ImageView imageView);
    void Destroy(SwapchainKHR swapchain);
    void Destroy(Semaphore semaphore);
}

public interface IDynamicStateDevice
{
    bool DepthTestEnabled { get; set; }
    
    bool PrimitiveRestartEnable { get; set; }
    
    bool ColorBlendEnabled { get; set; }
    
    ColorComponentFlagBits ColorComponentFlags { get; set; }
    
    MSAALevel MSAALevel { get; set; }
        
    bool AlphaToCoverageEnable { get; set; }
    
    PolygonMode PolygonMode { get; set; }
    
    CullModeFlagBits CullMode { get; set; }
    
    bool IsWireFrame { get; set; }
        
    VkSampleMask[] SampleMask { get; set; }

    Single LineWidth { get; set; }

    FrontFace FrontFace { get; set; }

    /// <summary>How many control points make one patch of a <see cref="Vulkan.Core.PrimitiveTopology.PatchList"/>: what
    /// a tessellated pass draws. From 1 to the device's maxTessellationPatchSize, and the size of the input patch of
    /// the pass's hull shader.</summary>
    uint PatchControlPoints { get; set; }

    /// <summary>Where the domain shader's tessellation coordinates start: the corner of the domain at (0, 0).</summary>
    TessellationDomainOrigin TessellationDomainOrigin { get; set; }

    ColorBlendEquationEXT ColorBlendEquation { get; set; }
    
    bool DepthWriteEnable { get; set; }

    CompareOp DepthCompareFunction { get; set; }

    bool DepthBoundsTestEnabled { get; set; }
        
    bool DepthBiasEnabled { get; set; }

    /// <summary>The depth added to every fragment while <see cref="DepthBiasEnabled"/>, in the depth buffer's smallest
    /// steps; negative brings it nearer.</summary>
    float DepthBiasConstantFactor { get; set; }

    /// <summary>The depth added per unit of the polygon's depth slope while <see cref="DepthBiasEnabled"/>.</summary>
    float DepthBiasSlopeFactor { get; set; }

    /// <summary>Whether draws hold each fragment's depth to the depth range instead of clipping at the near and far
    /// planes; only where <see cref="MainGraphicsDevice.IsDepthClampEnabled"/>.</summary>
    bool DepthClampEnable { get; set; }

    bool StencilTestEnabled { get; set; }

    /// <summary>Stencil comparison, the value compared against, and what a passing fragment writes back - for marking
    /// COVERAGE: one pass writes its reference where it drew, a later pass draws only where the mark is absent.</summary>
    CompareOp StencilCompareOp { get; set; }

    StencilOp StencilPassOp { get; set; }

    uint StencilReference { get; set; }

    uint StencilWriteMask { get; set; }



    bool LogicOperationsEnabled { get; set; }
        
    LogicOp LogicOperation { get; set; }
    
    public void SetViewports(params System.ReadOnlySpan<Viewport> viewports);
    
    public void SetScissors(params System.ReadOnlySpan<Rect2D> scissors);

    /// <summary>Where the top-left corner of the target being drawn into lies in the coordinates viewports and scissors
    /// are given in; zero unless the target holds one region of the surface.</summary>
    NativePoint TargetOrigin { get; set; }

    void SetRenderTargets(params IRenderTarget[] renderTargets);

    void SetDepthBuffer(IDepthStencilBuffer depthBuffer);
    
    void SetVertexBuffer(IBuffer vertexBuffer);

    void SetIndexBuffer(IBuffer indexBuffer);

    void SetVertexBuffers(params IBuffer[] vertexBuffers);
    
    Queue GraphicsQueue { get; }

    /// <summary>The lock every submit and present on <see cref="GraphicsQueue"/> goes under: another device may hold the
    /// same queue.</summary>
    object GraphicsQueueLock { get; }

    IRenderTarget CurrentRenderTarget { get; }
    
    IDepthStencilBuffer CurrentDepthStencilBuffer { get; }
    
    UInt32 CurrentFrame { get; }
}

public unsafe interface IGraphicsDevice : IDrawableDevice, IDynamicStateDevice, IDestroyableDevice, IDisposable
{
    Guid DeviceId { get; }
    
    uint MaxFramesInFlight { get; }

    /// <summary>Frames this device has BEGUN, counted after each one's fence wait. Deferred disposal reads it to know
    /// when whatever was in flight at hand-over time has certainly retired. Zero on a device that never draws.</summary>
    ulong FrameTicket { get; }

    /// <summary>Whether every frame this device has submitted has finished on the GPU, so nothing it recorded can still
    /// be read. A device that is not drawing - an off-screen one between two bakes - answers true.</summary>
    bool HasNoFramesInFlight { get; }

    void AddResource(GraphicsResource resource);

    /// <summary>Unregisters a resource (called from its Dispose) so it stops being rooted by the device.</summary>
    void RemoveResource(GraphicsResource resource);
    
    void AddToDeferDisposeQueue(IDisposable obj);

    /// <summary>Copies <paramref name="data"/> into this frame's upload pages and gives the copy's device address, for a
    /// shader to read through a pointer. It lives as long as the frame's constants do. The address differs every frame,
    /// so a draw a recording replays must not take it: the replay would never match its recording.</summary>
    ulong UploadFrameData<T>(ReadOnlySpan<T> data) where T : unmanaged;

    IEffectResourceLinker CreateEffectResourceLinker();
    
    IEffectPass CreateEffectPass(Logger logger, Effect effect, EffectTechnique technique, EffectData.Pass pass, string name);

    void BindShader(CommandBuffer cmd, ShaderStageFlagBits stage, ShaderEXT shader);

    /// <summary>Bind a pass.s stages in ONE call. vkCmdBindShadersEXT takes arrays; binding them one at a time cost a
    /// marshaled call per stage, four times per pass switch, on every frame.</summary>
    void BindShaders(CommandBuffer cmd, ShaderStageFlagBits[] stages, ShaderEXT[] shaders);

    /// <summary>Are these shader objects of a pass already bound to the current command buffer? A run of draws of one
    /// material, or of effects of the same data, re-bound the same handles per draw, and every bind is a marshaled call.
    /// </summary>
    bool ShadersBoundFor(object shaders);

    /// <summary>Remember that these shaders of a pass are now bound to the current command buffer.</summary>
    void ShadersBound(object shaders);

    CommandBuffer CurrentCommandBuffer { get; }

    /// <summary>Whether this device can record draws with <see cref="BeginRecording"/> and execute them in its pass.</summary>
    bool SupportsRecordedCommands { get; }

    /// <summary>GPU time of the last finished frame, from its first command to its last, in milliseconds; work other
    /// queues ran in between counts too. 0 when the device cannot time.</summary>
    double LastGpuFrameMs { get; }

    /// <summary>Sends the draws that follow into a secondary command buffer for this frame slot instead of the frame's
    /// own, until <see cref="EndRecording"/>: <paramref name="into"/> recorded over when it is this device's recording for
    /// this slot, else a new one. Only inside the render pass, and only draws: nothing that ends the pass or changes its
    /// target.</summary>
    IRecordedCommands BeginRecording(IRecordedCommands into);

    /// <summary>Ends the recording <see cref="BeginRecording"/> started; the draws go to the frame's buffer again. Gives
    /// the fingerprint of what was recorded, the same <see cref="EndFingerprint"/> gives for the same draws.</summary>
    ulong EndRecording();

    /// <summary>Takes the draws that follow as a fingerprint only, issuing none of them, until <see cref="EndFingerprint"/>:
    /// what a recording of them would hold - shaders, constants by content, resources, state, draw arguments.</summary>
    void BeginFingerprint();

    /// <summary>Ends the fingerprint <see cref="BeginFingerprint"/> started and gives it. The viewport and scissor are the
    /// ones the draws left, as after executing a recording of them.</summary>
    ulong EndFingerprint();

    /// <summary>Executes recorded draws in the frame's render pass. They must be recorded for this frame slot.</summary>
    void ExecuteRecorded(IRecordedCommands commands);

    /// <summary>
    /// Records a short piece of work with <paramref name="record"/> and submits it at once, ahead of the frame: it waits
    /// for <paramref name="wait"/> to reach <paramref name="waitValue"/> and signals each of <paramref name="signals"/> as
    /// soon as it is done, not when the whole frame is. Outside a render pass.
    /// </summary>
    void SubmitAside(Action<CommandBuffer> record, Semaphore wait, PipelineStageFlagBits2 waitStage, ulong waitValue,
        IReadOnlyList<(Semaphore Semaphore, ulong Value)> signals);

    /// <summary>Register a semaphore the next <see cref="Submit"/> must wait on (timelineValue=0 for binary).</summary>
    void AddWaitSemaphore(Semaphore semaphore, PipelineStageFlagBits2 stage, ulong timelineValue = 0);

    /// <summary>Register a semaphore the next <see cref="Submit"/> must signal (timelineValue=0 for binary).</summary>
    void AddSignalSemaphore(Semaphore semaphore, ulong timelineValue = 0);

    nuint MapMemory(DeviceMemory memory, ulong offset, ulong size, MemoryMapFlagBits flags);

    public void UnmapMemory(DeviceMemory memory);
    
    public IEffectPass CurrentEffectPass { get; set; }

    /// <param name="name">Readable identity of the shader (effect.technique.pass.stage) - names its binary-cache file.</param>
    ShaderEXT CreateShader(ShaderCreateInfoEXT shaderCreateInfo, string name = null);

    void DestroyShader(ShaderEXT shaderObject);

    CommandBuffer BeginSingleTimeCommand();

    void EndSingleTimeCommand(CommandBuffer commandBuffer);

    Color ClearColor { get; set; }
    
    Device LogicalDevice { get; }
    
    GraphicsAdapter Adapter { get; }
    
    GraphicsPresenter Presenter { get; set; }

    // True only after THIS frame acquired + submitted: reset false at BeginDraw start, set true after a successful
    // Submit (which early-returns if the command buffer never started, i.e. acquire failed / the frame was skipped).
    // The present path gates on it so a skipped/aborted frame during resize never presents a stale/unacquired image.
    bool CanPresent { get; }

    /// <summary>Whether this frame holds a swapchain image. It is acquired in EndDraw - the frame renders offscreen and
    /// needs the image only for the blit - so a caller must ask before blitting or presenting.</summary>
    bool HasSwapchainImage { get; }

    /// <summary>Holds this frame back, when asked before EndDraw: no swapchain image is acquired and nothing is presented,
    /// so the window keeps showing the last frame, while the frame's work is still submitted. For a frame drawn
    /// incomplete that the next one draws whole. A render-target presenter owns its image, so its frame is kept.</summary>
    void WithholdFrame();

    /// <summary>Whether this frame is held back (<see cref="WithholdFrame"/>): set by EndDraw, so a caller can draw the next
    /// one at once where nobody will ask for it - a one-shot render into a texture.</summary>
    bool FrameWithheld { get; }

    MainGraphicsDevice MainDevice { get; }

    Fence GetCurrentFence();

    Semaphore GetRenderFinishedSemaphore();

    PrimitiveTopology PrimitiveTopology { get; set; }
    
    Type VertexType { get; set; }

    IDepthStencilBuffer CreateDepthBuffer(uint width, 
        uint height, 
        DepthFormat format, 
        MSAALevel msaa,
        ImageAspectFlagBits imageAspect = ImageAspectFlagBits.DepthBit,
        string name = "");
    
    IRenderTarget CreateRenderTarget(UInt32 width, 
        UInt32 height, 
        MSAALevel msaa, 
        SurfaceFormat format, 
        ImageUsageFlagBits usage = ImageUsageFlagBits.TransferSrcBit,
        ImageLayout desiredLayout = ImageLayout.ColorAttachmentOptimal,
        string name = "");

    ITexture CreateTextureFromImage(Image image, 
        UInt32 width, 
        UInt32 height, 
        MSAALevel msaa, 
        SurfaceFormat format, 
        ImageUsageFlagBits usage = ImageUsageFlagBits.TransferSrcBit,
        ImageLayout desiredLayout = ImageLayout.ColorAttachmentOptimal,
        string name = "");

    ITexture CreateTexture(TextureDescription description, byte[] pixelData);

    /// <summary>One texture whose ARRAY LAYERS are <paramref name="layers"/> (all the same size), uploaded in a single
    /// staging buffer and a single copy - an animation's frames, selected in the shader by layer.</summary>
    ITexture CreateTextureArray(TextureDescription description, IReadOnlyList<byte[]> layers);

    /// <summary>Writes <paramref name="regions"/> into an existing texture in one staging buffer and one copy, leaving
    /// the rest of it as it was - atlas cells, map tiles.</summary>
    void UpdateTextureRegions(ITexture texture, IReadOnlyList<TextureRegionUpdate> regions);

    /// <summary>Imports an externally produced shared surface zero-copy and returns it as a sampleable texture.
    /// The producer hands off the <paramref name="descriptor"/> after exporting its memory/semaphores.</summary>
    ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor);

    SurfaceKHR GetOrCreateSurface(PresentationParameters parameters);

    /// <summary>Lets go of the surface <see cref="GetOrCreateSurface"/> held for this window; the last to let go
    /// destroys it.</summary>
    void ReleaseSurface(IntPtr outputHandle);

    /// <summary>Whether the surface of this window can be composed with its alpha: it offers pre-multiplied composite
    /// alpha. Asked before a presenter is chosen, so a see-through window whose surface cannot keeps its alpha through
    /// <see cref="PresenterType.Compositor"/> instead.</summary>
    bool SurfaceComposesWithAlpha(PresentationParameters parameters);

    void InsertImageMemoryBarrier(CommandBuffer commandBuffer,
        ITexture texture,
        AccessFlagBits2 sourceAccessMask,
        AccessFlagBits2 destinationAccessMask,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        PipelineStageFlagBits2 sourceStageMask,
        PipelineStageFlagBits2 destinationStageMask);

    void SetObjectDebugName(ulong objectHandle, ObjectType objectType, string name);
}
