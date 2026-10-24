using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;
using VulkanImage = Adamantium.Vulkan.Core.Image;
using Image = Adamantium.Imaging.Image;
using VkBuffer = Adamantium.Vulkan.Core.Buffer;

namespace Adamantium.Graphics;

public unsafe class Texture : GraphicsResource, ITexture
{
    private VulkanImage vulkanImage;
    private DeviceMemory vulkanImageMemory;
        
    public TextureDescription Description;

    public uint Width => Description.Width;

    public uint Height => Description.Height;
    public uint Depth => Description.Depth;

    public SurfaceFormat SurfaceFormat => Description.Format;
    public ImageLayout ImageLayout { get; set; }
    public ulong TotalSizeInBytes { get; private set; }

    public IntPtr ManagedPointer => new IntPtr(NativePointer);

    public void* NativePointer => VulkanImage.NativePointer;
    public MSAALevel MSAALevel => Description.Samples;
    public ImageAspectFlagBits ImageAspect => Description.ImageAspect;

    private bool _disposeImage = true;

    protected VulkanImage VulkanImage => vulkanImage;
    protected DeviceMemory ImageMemory => vulkanImageMemory;
    protected ImageView ImageView { get; set; }

    protected Texture(IGraphicsDevice device, TextureDescription description, string name = "") : base(device, name)
    {
        Description = description;
        ImageLayout = description.InitialLayout;
        Initialize();
        this.TransitionImageLayout(description.DesiredImageLayout);
    }

    /// <summary>Creates a texture whose image and memory take part in a cross-API shared-surface chain. The
    /// <paramref name="imagePNext"/> (external-memory image create info) and the <paramref name="allocPNextFactory"/>
    /// (export/import + dedicated allocation, given the created image) make the backing memory exportable or
    /// imported. The caller (see <see cref="SharedSurface"/>) owns layout transitions and handle export.</summary>
    protected Texture(IGraphicsDevice device, TextureDescription description, object imagePNext, Func<VulkanImage, object> allocPNextFactory, string name = "") : base(device, name)
    {
        Description = description;
        ImageLayout = description.InitialLayout;
        CreateImage(Description, MemoryPropertyFlags.DeviceLocal, out vulkanImage, out vulkanImageMemory, imagePNext, allocPNextFactory);
        CreateImageView(Description);
        if (GraphicsDevice.MainDevice.IsInDebugMode)
        {
            GraphicsDevice.SetObjectDebugName((ulong)VulkanImage.NativePointer, ObjectType.Image, Name);
        }
    }

    protected Texture(IGraphicsDevice device,
        VulkanImage vulkanImage, 
        TextureDescription description,
        ImageUsageFlagBits usage,
        string name = "") : 
        base(device, name)
    {
        Description = description;
        Description.Usage |= ImageUsageFlagBits.TransferDstBit | usage;
        ImageLayout = description.DesiredImageLayout;
        TotalSizeInBytes = CalculateTextureSize(Width, Height, Depth, Description.MipLevels,
            (uint)description.Format.SizeOfInBytes());
        this.vulkanImage = vulkanImage;
        _disposeImage = false;
        if (GraphicsDevice.MainDevice.IsInDebugMode)
        {
            GraphicsDevice.SetObjectDebugName((ulong)VulkanImage.NativePointer, ObjectType.Image, Name);
        }
    }

    protected Texture(IGraphicsDevice device, Image img, ImageUsageFlagBits usage, ImageLayout desiredLayout) : 
        base(device)
    {
        Description = img.Description;
        Description.Usage |= ImageUsageFlagBits.TransferDstBit | usage;
        Description.DesiredImageLayout = desiredLayout;
        TotalSizeInBytes = img.TotalSizeInBytes;

        CopyDataToImage(TotalSizeInBytes, img.DataPointer);
    }
        
    protected Texture(IGraphicsDevice device, IRawBitmap bitmap, ImageUsageFlagBits usage, ImageLayout desiredLayout) : 
        base(device)
    {
        Description = bitmap.GetImageDescription();
        Description.Usage |= ImageUsageFlagBits.TransferDstBit | usage;
        Description.DesiredImageLayout = desiredLayout;
        TotalSizeInBytes = bitmap.TotalSizeInBytes;

        var pixelData = bitmap.GetRawPixels(0);
        var handle = GCHandle.Alloc(pixelData, GCHandleType.Pinned);

        CopyDataToImage(TotalSizeInBytes, handle.AddrOfPinnedObject());
            
        handle.Free();
    }
        
    protected Texture(IGraphicsDevice device, ImageDescription description, byte[] data, ImageUsageFlagBits usage, ImageLayout desiredLayout) :
        base(device)
    {
        Description = description;
        Description.Usage |= ImageUsageFlagBits.TransferDstBit | usage;
        Description.DesiredImageLayout = desiredLayout;
        TotalSizeInBytes = (ulong)data.Length;

        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        CopyDataToImage(TotalSizeInBytes, handle.AddrOfPinnedObject());
        handle.Free();
    }

    // One image, one LAYER per element of <paramref name="layers"/> - an animation's frames, so playing it is choosing a
    // layer instead of swapping textures. All of it goes up in ONE staging buffer and ONE copy (a region per layer):
    // uploading layer by layer would mean a command buffer, a submit and a fence WAIT each time, which is what made a
    // 200-frame GIF cost hundreds of separate uploads.
    protected Texture(IGraphicsDevice device, ImageDescription description, IReadOnlyList<byte[]> layers, ImageUsageFlagBits usage, ImageLayout desiredLayout) :
        base(device)
    {
        Description = description;
        Description.Usage |= ImageUsageFlagBits.TransferDstBit | usage;
        Description.DesiredImageLayout = desiredLayout;

        var layerSize = (ulong)layers[0].Length;
        TotalSizeInBytes = layerSize * (ulong)layers.Count;

        CreateBuffer(TotalSizeInBytes,
            BufferUsageFlagBits.TransferSrcBit,
            MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent,
            out var stagingBuffer,
            out var stagingBufferMemory);

        var mapped = GraphicsDevice.MapMemory(stagingBufferMemory, 0, TotalSizeInBytes, 0);
        for (var i = 0; i < layers.Count; i++)
        {
            var handle = GCHandle.Alloc(layers[i], GCHandleType.Pinned);
            System.Buffer.MemoryCopy(handle.AddrOfPinnedObject().ToPointer(),
                (byte*)mapped + layerSize * (ulong)i, layerSize, layerSize);
            handle.Free();
        }
        GraphicsDevice.UnmapMemory(stagingBufferMemory);

        CreateImage(Description, MemoryPropertyFlags.DeviceLocal, out vulkanImage, out vulkanImageMemory);
        this.TransitionImageLayout(ImageLayout.TransferDstOptimal);
        CopyBufferToLayers(stagingBuffer, vulkanImage, Description, layerSize, layers.Count);
        this.TransitionImageLayout(Description.DesiredImageLayout);
        CreateImageView(Description);

        GraphicsDevice.Destroy(stagingBuffer);
        GraphicsDevice.Destroy(stagingBufferMemory);
    }

    private void CopyDataToImage(ulong totalSizeInBytes, IntPtr pointerToPixelData)
    {
        CreateBuffer(TotalSizeInBytes, 
            BufferUsageFlagBits.TransferSrcBit,
            MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent, 
            out var stagingBuffer,
            out var stagingBufferMemory);
            
        var data = GraphicsDevice.MapMemory(stagingBufferMemory, 0, totalSizeInBytes, 0);
        System.Buffer.MemoryCopy(pointerToPixelData.ToPointer(), (void*)data, totalSizeInBytes, totalSizeInBytes);
        GraphicsDevice.UnmapMemory(stagingBufferMemory);
            
        CreateImage(Description, MemoryPropertyFlags.DeviceLocal, out vulkanImage, out vulkanImageMemory);
        this.TransitionImageLayout(ImageLayout.TransferDstOptimal);
        CopyBufferToImage(stagingBuffer, vulkanImage, Description);
        this.TransitionImageLayout(Description.DesiredImageLayout);
        CreateImageView(Description);
        
        GraphicsDevice.Destroy(stagingBuffer);
        GraphicsDevice.Destroy(stagingBufferMemory);
    }
        
    private void CreateBuffer(ulong size, BufferUsageFlagBits usage, MemoryPropertyFlags memoryProperties, out VkBuffer buffer, out DeviceMemory bufferMemory)
    {
        var device = GraphicsDevice.LogicalDevice;
            
        var bufferInfo = new BufferCreateInfo();
        bufferInfo.Size = size;
        bufferInfo.Usage = usage;
        bufferInfo.SharingMode = SharingMode.Exclusive;

        buffer = GraphicsDevice.LogicalDevice.CreateBuffer(bufferInfo);

        var memoryRequirements = GraphicsDevice.LogicalDevice.GetBufferMemoryRequirements(buffer);

        var allocInfo = new MemoryAllocateInfo
        {
            AllocationSize = memoryRequirements.Size,
            MemoryTypeIndex = GraphicsDevice.Adapter.FindMemoryIndex(memoryRequirements.MemoryTypeBits, memoryProperties)
        };

        if (device.AllocateMemory(allocInfo, null, out bufferMemory) != Result.Success)
        {
            throw new Exception("failed to allocate buffer memory!");
        }

        if (GraphicsDevice.LogicalDevice.BindBufferMemory(buffer, bufferMemory, 0) != Result.Success)
        {
            throw new Exception("failed to bind buffer memory!");
        }
    }

    private void CopyBufferToImage(VkBuffer buffer, VulkanImage image, TextureDescription description)
    {
        var commandBuffer = GraphicsDevice.BeginSingleTimeCommand();

        var region = new BufferImageCopy();
        region.BufferOffset = 0;
        region.BufferRowLength = 0;
        region.BufferImageHeight = 0;
        region.ImageSubresource = new ImageSubresourceLayers();
        region.ImageSubresource.AspectMask = ImageAspectFlagBits.ColorBit;
        region.ImageSubresource.MipLevel = 0;
        region.ImageSubresource.BaseArrayLayer = 0;
        region.ImageSubresource.LayerCount = 1;
        region.ImageOffset = new Offset3D() { X = 0, Y = 0, Z = 0};
        region.ImageExtent = new Extent3D() {Width = description.Width, Height = Description.Height, Depth = 1}; 
            
        commandBuffer.CopyBufferToImage(buffer, image, ImageLayout.TransferDstOptimal, 1, region);
        GraphicsDevice.EndSingleTimeCommand(commandBuffer);
    }
        
    // Every layer in ONE command and ONE submit: region i reads its slice of the staging buffer and writes array layer i.
    // That is the point of the array - uploading an animation costs one fence wait, not one per frame.
    private void CopyBufferToLayers(VkBuffer buffer, VulkanImage image, TextureDescription description, ulong layerSize, int layerCount)
    {
        var commandBuffer = GraphicsDevice.BeginSingleTimeCommand();

        var regions = new BufferImageCopy[layerCount];
        for (var i = 0; i < layerCount; i++)
        {
            var region = new BufferImageCopy();
            region.BufferOffset = layerSize * (ulong)i;
            region.BufferRowLength = 0;
            region.BufferImageHeight = 0;
            region.ImageSubresource = new ImageSubresourceLayers();
            region.ImageSubresource.AspectMask = ImageAspectFlagBits.ColorBit;
            region.ImageSubresource.MipLevel = 0;
            region.ImageSubresource.BaseArrayLayer = (uint)i;
            region.ImageSubresource.LayerCount = 1;
            region.ImageOffset = new Offset3D { X = 0, Y = 0, Z = 0 };
            region.ImageExtent = new Extent3D { Width = description.Width, Height = description.Height, Depth = 1 };
            regions[i] = region;
        }

        commandBuffer.CopyBufferToImage(buffer, image, ImageLayout.TransferDstOptimal, (uint)layerCount, regions);
        GraphicsDevice.EndSingleTimeCommand(commandBuffer);
    }

    private void CopyImageToBuffer(VkBuffer buffer, uint layer)
    {
        var commandBuffer = GraphicsDevice.BeginSingleTimeCommand();

        var region = new BufferImageCopy();
        region.BufferOffset = 0;
        region.BufferRowLength = 0;
        region.BufferImageHeight = 0;
        region.ImageSubresource = new ImageSubresourceLayers();
        region.ImageSubresource.AspectMask = ImageAspectFlagBits.ColorBit;
        region.ImageSubresource.MipLevel = 0;
        region.ImageSubresource.BaseArrayLayer = layer;
        region.ImageSubresource.LayerCount = 1;
        region.ImageOffset = new Offset3D() { X = 0, Y = 0, Z = 0};
        region.ImageExtent = new Extent3D() {Width = Width, Height = Height, Depth = 1}; 
            
        commandBuffer.CopyImageToBuffer(vulkanImage, ImageLayout, buffer, 1, region);
        GraphicsDevice.EndSingleTimeCommand(commandBuffer);
    }
        
    private MemoryPropertyFlags GetStagingMemoryFlags()
    {
        var stagingMemoryFlags = MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent; // Windows
        // if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        // {
        //     stagingMemoryFlags = MemoryPropertyFlags.DeviceLocal | 
        //                          MemoryPropertyFlags.HostVisible | 
        //                          MemoryPropertyFlags.HostCoherent | 
        //                          MemoryPropertyFlags.HostCached; // MacOS
        // }

        return stagingMemoryFlags;
    }

    protected void Initialize()
    {
        CreateImage(Description, MemoryPropertyFlags.DeviceLocal, out vulkanImage, out vulkanImageMemory);
        CreateImageView(Description);
        if (GraphicsDevice.MainDevice.IsInDebugMode)
        {
            GraphicsDevice.SetObjectDebugName((ulong)VulkanImage.NativePointer, ObjectType.Image, Name);
        }
    }

    protected void CreateImage(TextureDescription description, MemoryPropertyFlags memoryProperties, out VulkanImage image, out DeviceMemory imageMemory,
        object imagePNext = null, Func<VulkanImage, object> allocPNextFactory = null)
    {
        var device = GraphicsDevice.LogicalDevice;
        var imageInfo = description.ToImageCreateInfo();
        if (imagePNext != null)
        {
            imageInfo.PNext = imagePNext;
        }
        var result = device.CreateImage(imageInfo, null, out image);
        if (result != Result.Success)
        {
            throw new Exception("failed to create image!");
        }

        device.GetImageMemoryRequirements(image, out var memRequirements);
        TotalSizeInBytes = CalculateTextureSize(description.Width,
            description.Height,
            description.Depth,
            description.MipLevels,
            (uint)description.Format.SizeOfInBytes());

        var allocInfo = new MemoryAllocateInfo
        {
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = GraphicsDevice.Adapter.FindMemoryIndex(memRequirements.MemoryTypeBits, memoryProperties)
        };
        // The factory is given the freshly created image so it can build a dedicated-allocation pNext (export and
        // import of an image's memory both require a dedicated allocation that names the image).
        if (allocPNextFactory != null)
        {
            allocInfo.PNext = allocPNextFactory(image);
        }

        var allocResult = device.AllocateMemory(allocInfo, null, out imageMemory);
        if (allocResult != Result.Success)
        {
            var mb = memRequirements.Size / (1024.0 * 1024.0);
            throw new Exception(
                $"failed to allocate image memory: {allocResult}. Requested {mb:F1} MB " +
                $"(memoryTypeIndex {allocInfo.MemoryTypeIndex}, typeBits 0x{memRequirements.MemoryTypeBits:X}); " +
                $"image {description.Width}x{description.Height} {description.Format} " +
                $"samples={description.Samples} mips={description.MipLevels}.");
        }

        device.BindImageMemory(image, imageMemory, 0);
        ImageLayout = imageInfo.InitialLayout;
    }

    protected void CreateImageView(TextureDescription description)
    {
        var createInfo = new ImageViewCreateInfo();
        createInfo.Image = VulkanImage;
        // A 2D image with ArrayLayers > 1 (a Texture2DArray) MUST use a 2D-ARRAY view: the spec forbids layerCount > 1 on
        // a plain VK_IMAGE_VIEW_TYPE_2D view (validation VUID-VkImageViewCreateInfo-imageViewType-04973). The shader then
        // selects a slice by the layer index.
        createInfo.ViewType = description.Dimension == TextureDimension.Texture2D && description.ArrayLayers > 1
            ? ImageViewType._2dArray
            : (ImageViewType)description.Dimension;
        createInfo.Format = SurfaceFormat;
        var componentMapping = new ComponentMapping
        {
            R = ComponentSwizzle.Identity,
            G = ComponentSwizzle.Identity,
            B = ComponentSwizzle.Identity,
            A = ComponentSwizzle.Identity
        };
        createInfo.Components = componentMapping;
        var subresourceRange = new ImageSubresourceRange
        {
            AspectMask = description.ImageAspect,
            BaseMipLevel = 0,
            // EVERY level the image has, not one. A view over level 0 alone makes the rest unreachable: a shader
            // sampling an explicit level past the view's range is out of bounds, which is a crash rather than a clamp.
            LevelCount = Math.Max(1u, description.MipLevels),
            BaseArrayLayer = 0,
            LayerCount = description.ArrayLayers > 1 ? description.ArrayLayers : 1
        };
        createInfo.SubresourceRange = subresourceRange;

        ImageView = GraphicsDevice.LogicalDevice.CreateImageView(createInfo);
        Info = createInfo;
    }

    private ImageViewCreateInfo[] _levelInfos;
    private ImageView[] _levelViews;

    /// <summary>A view of ONE mip level, made on first use and kept. The texture's own view spans every level, which is
    /// what SAMPLING wants; writing into a level - a blur pyramid filled by a shader, a target attached per level -
    /// needs a view that is exactly that level and nothing else.</summary>
    public ImageView GetLevelView(uint level)
    {
        var levels = Math.Max(1u, Description.MipLevels);
        if (level >= levels) level = levels - 1;

        _levelViews ??= new ImageView[levels];
        _levelInfos ??= new ImageViewCreateInfo[levels];
        if (_levelViews[level] != null) return _levelViews[level];

        var info = new ImageViewCreateInfo
        {
            Image = GetImage(),
            ViewType = Info.ViewType,
            Format = Info.Format,
            Components = Info.Components,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = Description.ImageAspect,
                BaseMipLevel = level,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = Description.ArrayLayers > 1 ? Description.ArrayLayers : 1
            }
        };

        _levelInfos[level] = info;
        _levelViews[level] = GraphicsDevice.LogicalDevice.CreateImageView(info);
        return _levelViews[level];
    }

    /// <summary>The create-info of <see cref="GetLevelView"/>'s view - what a descriptor write is handed.</summary>
    public ImageViewCreateInfo GetLevelViewInfo(uint level)
    {
        GetLevelView(level);
        return _levelInfos[Math.Min(level, (uint)_levelInfos.Length - 1)];
    }

    private Texture[] _levelTextures;

    /// <summary>One mip level as a texture of its own, shader-read-only, disposed with this texture: what a shader reads
    /// while another level of the same image is drawn into.</summary>
    public Texture GetLevelTexture(uint level)
    {
        var levels = Math.Max(1u, Description.MipLevels);
        if (level >= levels) level = levels - 1;

        _levelTextures ??= new Texture[levels];
        if (_levelTextures[level] is not { IsDisposed: false })
        {
            _levelTextures[level] = new LevelTexture(this, level);
        }

        return _levelTextures[level];
    }

    private sealed class LevelTexture : Texture
    {
        public LevelTexture(Texture image, uint level)
            : base(image.GraphicsDevice, image.GetImage(), LevelDescription(image.Description, level), 0, image.Name)
        {
            ImageView = image.GetLevelView(level);
            Info = image.GetLevelViewInfo(level);
        }

        private static TextureDescription LevelDescription(TextureDescription description, uint level)
        {
            description.Width = Math.Max(1u, description.Width >> (int)level);
            description.Height = Math.Max(1u, description.Height >> (int)level);
            description.MipLevels = 1;
            description.DesiredImageLayout = ImageLayout.ShaderReadOnlyOptimal;
            return description;
        }

        protected override void Dispose(bool disposeManaged)
        {
            ImageView = null;
            base.Dispose(disposeManaged);
        }
    }


    public static uint CalculateMipLevels(int width, int height, MipMapCount mipLevels)
    {
        return CalculateMipLevels(width, height, 1, mipLevels);
    }

    public static uint CalculateMipLevels(int width, int height, int depth, MipMapCount mipLevels)
    {
        var maxMipLevels = CountMipLevels(width, height, depth);
        if (mipLevels > 1 && maxMipLevels > mipLevels)
        {
            throw new InvalidOperationException($"MipLevels must be <= {maxMipLevels}");
        }
        return mipLevels;
    }

    private static int CountMipLevels(int width, int height, int depth)
    {
        /*
         * Math.Max function selects the largest dimension.
         * Math.Log2 function calculates how many times that dimension can be divided by 2.
         * Math.Floor function handles cases where the largest dimension is not a power of 2.
         * 1 is added so that the original image has a mip level.
         */
        var max = Math.Max(Math.Max(width, height), depth);
        var levels = (int)(Math.Floor(Math.Log2(max)) + 1);
        return levels;
    }

    /// <summary>
    /// Creates a new texture with the specified generic texture description.
    /// </summary>
    /// <param name="graphicsDevice">The graphics device.</param>
    /// <param name="description">The description.</param>
    /// <returns>A Texture instance, either a RenderTarget or DepthStencilBuffer or Texture, depending on Binding flags.</returns>
    /// <exception cref="ArgumentNullException"></exception>
    public static Texture New(IGraphicsDevice graphicsDevice, TextureDescription description, string name = "")
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        // TODO: check how this could be implemented
        if (description.Usage.HasFlag(ImageUsageFlagBits.ColorAttachmentBit))
        {
            return new RenderTarget(graphicsDevice, description, name);
        }
        else if (description.Usage.HasFlag(ImageUsageFlagBits.DepthStencilAttachmentBit))
        {
            return new DepthStencilBuffer(graphicsDevice, description, name);
        }
        else
        {
            return new Texture(graphicsDevice, description, name);
        }
    }
        
    public static Texture CreateFrom(
        IGraphicsDevice graphicsDevice, 
        TextureDescription description, 
        byte[] pixelData,
        ImageUsageFlagBits usage = ImageUsageFlagBits.SampledBit, 
        ImageLayout desiredLayout = ImageLayout.ShaderReadOnlyOptimal)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        return new Texture(graphicsDevice, description, pixelData, usage, desiredLayout);
    }
    
    /// <summary>One texture whose ARRAY LAYERS are <paramref name="layers"/> (all of the same size), uploaded in a single
    /// staging buffer and a single copy. The shader picks a layer, so nothing is uploaded again to show another one.</summary>
    public static Texture CreateArrayFrom(
        IGraphicsDevice graphicsDevice,
        TextureDescription description,
        IReadOnlyList<byte[]> layers,
        ImageUsageFlagBits usage = ImageUsageFlagBits.SampledBit,
        ImageLayout desiredLayout = ImageLayout.ShaderReadOnlyOptimal)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0) throw new ArgumentException("An array texture needs at least one layer.", nameof(layers));

        return new Texture(graphicsDevice, description, layers, usage, desiredLayout);
    }

    public static Texture CreateFrom(
        IGraphicsDevice graphicsDevice,
        VulkanImage vulkanImage,
        TextureDescription description, 
        ImageUsageFlagBits usage = ImageUsageFlagBits.SampledBit,
        string name = "")
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        return new Texture(graphicsDevice, vulkanImage, description, usage, name);
    }

    /// <summary>
    /// Loads a texture from a stream.
    /// </summary>
    /// <param name="device">The <see cref="GraphicsDevice"/>.</param>
    /// <param name="stream">The stream to load the texture from.</param>
    /// <param name="usage">Texture flags</param>
    /// <param name="initialLayout">Resource usage</param>
    /// <returns>A <see cref="Texture"/></returns>
    public static Texture Load(GraphicsDevice device, Stream stream, ImageUsageFlagBits usage = ImageUsageFlagBits.SampledBit, ImageLayout initialLayout = ImageLayout.ShaderReadOnlyOptimal)
    {
        var image = BitmapLoader.Load(stream);
        // No catch: the one here swallowed whatever went wrong and threw "Dimension not supported" in its place, so
        // every failure of this call arrived as the same wrong answer.
        return new Texture(device, image, usage, initialLayout);
    }

    /// <summary>
    /// Loads a texture from a file.
    /// </summary>
    /// <param name="device">Specify the <see cref="GraphicsDevice"/> used to load and create a texture from a file.</param>
    /// <param name="filePath">The file to load the texture from.</param>
    /// <param name="usage">Resource usage</param>
    /// <param name="layout">Desired image layout</param>
    /// <returns>A <see cref="Texture"/></returns>
    public static Texture Load(GraphicsDevice device, String filePath, ImageUsageFlagBits usage = ImageUsageFlagBits.SampledBit, ImageLayout layout = ImageLayout.ShaderReadOnlyOptimal)
    {
        var rawBitmap = BitmapLoader.Load(filePath);
        try
        {
            return new Texture(device, rawBitmap, usage, layout);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            throw;
        }
    }

    public VulkanImage GetImage()
    {
        return VulkanImage;
    }

    public ImageView GetImageView()
    {
        return ImageView;
    }

    public void SetImageView(ImageView imageView)
    {
        ImageView = imageView;
    }

    public void Save(string path, ImageFileType fileType)
    {
        var img = Image.New2D(Width, Height, 1, Description.Format);
        ReadbackInto(img);

        // The render target is stored as B8G8R8A8; the image encoders read pixels as RGBA, so swap R<->B.
        SwapRedBlueIfBgra(img);

        img.Save(path, fileType);
    }

    // Reusable host-side read-back buffer for SaveRaw, so the per-frame live preview doesn't allocate an Image each
    // frame. Owned by this texture (so it tracks the texture's size and is disposed with it); a resize recreates the
    // whole render target, hence a fresh texture and buffer.
    private Image _rawReadback;

    public void SaveRaw(string path)
    {
        // (Re)create the read-back buffer if missing or sized for a previous resolution (in case the texture was
        // resized in place rather than recreated), so the read-back always copies into a correctly-sized buffer.
        if (_rawReadback == null || _rawReadback.Description.Width != Width || _rawReadback.Description.Height != Height)
        {
            _rawReadback?.Dispose();
            _rawReadback = ToDispose(Image.New2D(Width, Height, 1, Description.Format));
        }
        ReadbackInto(_rawReadback);

        // Write the native bytes straight out (no R<->B swap, no encode); the consumer interprets them as B8G8R8A8.
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        fs.Write(new ReadOnlySpan<byte>((void*)_rawReadback.DataPointer, (int)_rawReadback.TotalSizeInBytes));
    }

    public Image ReadbackToImage(uint layer = 0)
    {
        var img = Image.New2D(Width, Height, 1, Description.Format);
        ReadbackInto(img, layer);
        return img;
    }

    /// <summary>Reads this texture's pixels into <paramref name="destination"/> via host image copy (fast path) or a
    /// staging buffer, with no color swap or encoding.</summary>
    private void ReadbackInto(Image destination, uint layer = 0)
    {
        if (GraphicsDevice.Adapter.SupportsHostImageCopy &&
            Description.Usage.HasFlag(ImageUsageFlagBits.HostTransferBit))
        {
            CopyImageToHostMemory(destination, layer);
        }
        else
        {
            CopyImageThroughStagingBuffer(destination, layer);
        }
    }

    /// <summary>
    /// Reads the image straight into host memory via host image copy (Vulkan 1.4 <c>vkCopyImageToMemory</c>) -
    /// no staging buffer and no queue submit. Requires the device feature and the <c>HostTransferBit</c> usage;
    /// <see cref="CopyImageThroughStagingBuffer"/> is the fallback.
    /// </summary>
    private void CopyImageToHostMemory(Image destination, uint layer)
    {
        this.TransitionImageLayout(ImageLayout.General);

        var region = new ImageToMemoryCopy
        {
            PHostPointer = (nuint)destination.DataPointer.ToPointer(),
            MemoryRowLength = Width,
            MemoryImageHeight = Height,
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlagBits.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = layer,
                LayerCount = 1
            },
            ImageOffset = new Offset3D { X = 0, Y = 0, Z = 0 },
            ImageExtent = new Extent3D { Width = Width, Height = Height, Depth = 1 }
        };

        var copy = new CopyImageToMemoryInfo
        {
            SrcImage = this,
            SrcImageLayout = ImageLayout.General,
            RegionCount = 1,
            PRegions = new[] { region }
        };

        GraphicsDevice.LogicalDevice.CopyImageToMemory(copy);
        this.TransitionImageLayout(Description.DesiredImageLayout);
    }

    /// <summary>Fallback read-back: copy the image into a host-visible staging buffer on the GPU, then memcpy it out.</summary>
    private void CopyImageThroughStagingBuffer(Image destination, uint layer)
    {
        CreateBuffer(destination.TotalSizeInBytes, BufferUsageFlagBits.TransferSrcBit | BufferUsageFlagBits.TransferDstBit,
            ReadbackMemory(), out var stagingBuffer, out var stagingBufferMemory);
        this.TransitionImageLayout(ImageLayout.TransferSrcOptimal);
        CopyImageToBuffer(stagingBuffer, layer);
        this.TransitionImageLayout(Description.DesiredImageLayout);

        var data = GraphicsDevice.MapMemory(stagingBufferMemory, 0, destination.TotalSizeInBytes, 0);
        System.Buffer.MemoryCopy((void*)data, destination.DataPointer.ToPointer(),
            destination.TotalSizeInBytes, destination.TotalSizeInBytes);
        GraphicsDevice.UnmapMemory(stagingBufferMemory);

        GraphicsDevice.Destroy(stagingBuffer);
        GraphicsDevice.Destroy(stagingBufferMemory);
    }

    // Cached host memory where the device has it: the CPU reads an uncached mapping about six times slower - 80 ms against
    // 13 for a 3200x1800 frame, which is what a zoomed designer preview reads back every frame.
    private MemoryPropertyFlags ReadbackMemory()
    {
        const MemoryPropertyFlags cached = MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent | MemoryPropertyFlags.HostCached;
        var types = GraphicsDevice.Adapter.Adapter.GetPhysicalDeviceMemoryProperties();
        for (var i = 0; i < types.MemoryTypeCount; i++)
        {
            if (((MemoryPropertyFlags)types.MemoryTypes.Span[i].PropertyFlags).HasFlag(cached))
            {
                return cached;
            }
        }

        return MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent;
    }

    /// <summary>Swaps red and blue in place for a B8G8R8A8 image so the RGBA-oriented encoders save correct colors.</summary>
    private static void SwapRedBlueIfBgra(Image image)
    {
        if (image.Description.Format != Format.B8G8R8A8_UNORM)
            return;

        var pixels = (byte*)image.DataPointer.ToPointer();
        var pixelCount = image.TotalSizeInBytes / 4;
        for (ulong i = 0; i < pixelCount; i++)
        {
            (pixels[0], pixels[2]) = (pixels[2], pixels[0]);
            pixels += 4;
        }
    }

    public ImageViewCreateInfo Info { get; private set; }

    public static implicit operator VulkanImage (Texture texture)
    {
        return texture.VulkanImage;
    }

    public static implicit operator ImageView(Texture texture)
    {
        return texture.ImageView;
    }

    protected override void Dispose(bool disposeManaged)
    {
        if (_levelTextures != null)
        {
            foreach (var levelTexture in _levelTextures)
            {
                levelTexture?.Dispose();
            }

            _levelTextures = null;
        }

        if (_levelViews != null)
        {
            foreach (var levelView in _levelViews)
            {
                GraphicsDevice.Destroy(levelView);
            }

            _levelViews = null;
            _levelInfos = null;
        }

        if (_disposeImage)
        {
            GraphicsDevice.Destroy(VulkanImage);
        }

        GraphicsDevice.Destroy(ImageView);
        GraphicsDevice.Destroy(ImageMemory);

        // Dispose ToDispose'd children too (e.g. a RenderTarget's resolve texture): without calling base the
        // DisposeCollector never runs and they leak.
        base.Dispose(disposeManaged);
    }
        
    static ulong CalculateTextureSize(uint width, uint height, uint depth, uint mipLevels, uint blockSize)
    {
        ulong totalSize = 0;

        for (uint level = 0; level < mipLevels; level++)
        {
            uint mipWidth = Math.Max(1u, width >> (int)level);
            uint mipHeight = Math.Max(1u, height >> (int)level);
            uint mipDepth = Math.Max(1u, depth >> (int)level);

            ulong levelSize = (ulong)(mipWidth * mipHeight * mipDepth) * blockSize;
            totalSize += levelSize;
        }

        return totalSize;
    }
}