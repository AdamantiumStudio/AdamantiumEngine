using Adamantium.Core;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;

namespace Adamantium.Multiverse
{
    public class UniverseOutputDescription
    {
        public UniverseOutputDescription(PresenterType presenterType)
        {
            PresenterType = presenterType;

            MsaaLevel = MSAALevel.None;
            PixelFormat = Format.B8G8R8A8_SNORM;
            DepthFormat = DepthFormat.Depth32Stencil8X24;
            BuffersCount = 3;
        }

        public UniverseOutputDescription(PresentationParameters parameters)
        {
            PresenterType = parameters.PresenterType;

            Width = parameters.Width;
            Height = parameters.Height;
            Handle = parameters.OutputHandle;
            DisplayHandle = parameters.DisplayHandle;
            PixelFormat = parameters.ImageFormat;
            DepthFormat = parameters.DepthFormat;
            MsaaLevel = parameters.MSAALevel;

            BuffersCount = parameters.BuffersCount;
        }

        public UniverseOutputDescription(
            PresenterType presenterType, 
            UInt32 width, 
            UInt32 height, 
            IntPtr handle)
        {
            PresenterType = presenterType;

            Width = width;
            Height = height;
            Handle = handle;

            MsaaLevel = MSAALevel.None;
            PixelFormat = Format.R8G8B8A8_UNORM;
            DepthFormat = DepthFormat.Depth32Stencil8X24;
            
            BuffersCount = 3;
        }

        public UniverseOutputDescription(
            PresenterType presenterType, 
            UInt32 width, 
            UInt32 height, 
            IntPtr handle, 
            MSAALevel msaa,
            Format pixelFormat = Format.R8G8B8A8_UNORM, 
            DepthFormat depthFormat = DepthFormat.Depth32Stencil8X24, 
            UInt32 buffersCount = 3)
        {
           PresenterType = presenterType;

           Width = width;
           Height = height;
           Handle = handle;
           PixelFormat = pixelFormat;
           DepthFormat = depthFormat;
           MsaaLevel = msaa;
           BuffersCount = buffersCount;
        }

        public UniverseOutputDescription(UniverseOutputDescription copy)
        {
            Width = copy.Width;
            Height = copy.Height;
            Handle = copy.Handle;
            DisplayHandle = copy.DisplayHandle;
            MsaaLevel = copy.MsaaLevel;
            PixelFormat = copy.PixelFormat;
            DepthFormat = copy.DepthFormat;
            BuffersCount = copy.BuffersCount;
            PresenterType = copy.PresenterType;
        }

        public UInt32 Width { get; set; }
        public UInt32 Height { get; set; }
        public IntPtr Handle { get; set; }

        /// <summary>The window's connection where its surface needs one: the wl_display on Wayland.</summary>
        public IntPtr DisplayHandle { get; set; }
        public SurfaceFormat PixelFormat { get; set; }
        public DepthFormat DepthFormat { get; set; }
        public MSAALevel MsaaLevel { get; set; }
        public UInt32 BuffersCount { get; set; }

        public PresenterType PresenterType { get; }

        public PresentInterval PresentInterval { get; set; }
        
        public UniverseOutputDescription Clone()
        {
            return new UniverseOutputDescription(this);
        }

        public PresentationParameters ToPresentationParameters()
        {
            return new PresentationParameters(
                PresenterType,
                Width,
                Height,
                Handle,
                MsaaLevel,
                PixelFormat,
                DepthFormat,
                BuffersCount)
            {
                DisplayHandle = DisplayHandle,
            };
        }
        
        public static implicit operator PresentationParameters(UniverseOutputDescription description)
        {
            return description.ToPresentationParameters();
        }
    }
}
