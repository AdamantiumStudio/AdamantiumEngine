using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Adamantium.Core;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging
{
    /// <summary>
    /// Provides method to instantiate an image 1D/2D/3D supporting TextureArray and mipmaps on the CPU or to load/save an image from/to the disk.
    /// </summary>
    //[ContentReader(typeof(ImageContentReader))]
    public sealed class Image : DisposableObject
    {
        public delegate Image ImageLoadDelegate(IntPtr dataPointer, ulong dataSize, bool makeACopy, GCHandle? handle);
        public delegate void ImageSaveDelegate(Image img, PixelBuffer[] pixelBuffers, int count, ImageDescription description, Stream imageStream);

        /// <summary>
        /// Pixel buffers.
        /// </summary>
        internal PixelBuffer[] pixelBuffers;
        private DataBox[] dataBoxArray;
        private List<int> mipMapToZIndex;
        private int zBufferCountPerArraySlice;
        private MipMapDescription[] mipmapDescriptions;
        private bool isAnimated;
        
        /// <summary>
        /// Provides access to all pixel buffers.
        /// </summary>
        /// <remarks>
        /// For Texture3D, each z slice of the Texture3D has a pixelBufferArray * by the number of mipmaps.
        /// For other textures, there is Description.MipLevels * Description.ArraySize pixel buffers.
        /// </remarks>
        private PixelBufferArray pixelBufferArray;

        /// <summary>
        /// Gets the total number of bytes occupied by this image in memory.
        /// </summary>
        private ulong totalSizeInBytes;

        /// <summary>
        /// Pointer to the buffer.
        /// </summary>
        private IntPtr buffer;

        /// <summary>
        /// True if the buffer must be disposed.
        /// </summary>
        private bool bufferIsDisposable;

        /// <summary>
        /// Handle != null if the buffer is a pinned managed object on the LOH (Large Object Heap).
        /// </summary>
        private GCHandle? handle;

        /// <summary>
        /// Description of this image.
        /// </summary>
        public ImageDescription Description;

        public bool IsAnimated => isAnimated;

        /// <summary>
        /// Gets a pointer to the image buffer in memory.
        /// </summary>
        /// <value>A pointer to the image buffer in memory.</value>
        public IntPtr DataPointer => this.buffer;
        
        public IRawBitmap Frames { get; private set; }

        /// <summary>
        /// Provides access to all pixel buffers.
        /// </summary>
        /// <remarks>
        /// For Texture3D, each z slice of the Texture3D has a pixelBufferArray * by the number of mipmaps.
        /// For other textures, there is Description.MipLevels * Description.ArraySize pixel buffers.
        /// </remarks>
        public PixelBufferArray PixelBuffer => pixelBufferArray;

        /// <summary>
        /// Gets or sets default image. Actual for APNG if you want to exclude some <see cref="PixelBuffer"/> from animation sequence
        /// </summary>
        public PixelBuffer DefaultImage { get; set; }

        /// <summary>
        /// Gets the total number of bytes occupied by this image in memory.
        /// </summary>
        public ulong TotalSizeInBytes => totalSizeInBytes;

        private Image()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Image" /> class.
        /// </summary>
        /// <param name="description">The image description.</param>
        /// <param name="dataPointer">The pointer to the data buffer.</param>
        /// <param name="offset">The offset from the beginning of the data buffer.</param>
        /// <param name="handle">The handle (optional).</param>
        /// <param name="bufferIsDisposable">if set to <c>true</c> [buffer is disposable].</param>
        /// <exception cref="System.InvalidOperationException">If the format is invalid, or width/height/depth/arraysize is invalid with respect to the dimension.</exception>
        internal Image(ImageDescription description, IntPtr dataPointer, ulong offset, GCHandle? handle, bool bufferIsDisposable, PitchFlags pitchFlags = PitchFlags.None)
        {
            Initialize(description, dataPointer, offset, handle, bufferIsDisposable, pitchFlags);
        }
        
        internal Image(ImageDescription description, IRawBitmap multiFrameImage)
        {
            InitializeMultiFrame(description, multiFrameImage);
        }

        internal Image(IRawBitmap rawBitmap)
        {
            if (rawBitmap.IsMultiFrame)
            {
                InitializeMultiFrame(rawBitmap.GetImageDescription(), rawBitmap);
            }
            else
            {
                var data = rawBitmap.GetRawPixels(0);
                var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
                Initialize(rawBitmap.GetImageDescription(), handle.AddrOfPinnedObject(), 0, handle, true);
            }
        }

        protected override void Dispose(bool disposeManagedResources)
        {
            if (handle.HasValue)
            {
                handle.Value.Free();
            }

            if (bufferIsDisposable)
            {
                Utilities.FreeMemory(buffer);
            }

            base.Dispose(disposeManagedResources);
        }

        /// <summary>
        /// Gets the mipmap description of this instance for the specified mipmap level.
        /// </summary>
        /// <param name="mipmap">The mipmap.</param>
        /// <returns>A description of a particular mipmap for this texture.</returns>
        public MipMapDescription GetMipMapDescription(int mipmap)
        {
            return mipmapDescriptions[mipmap];
        }

        /// <summary>
        /// Gets the pixel buffer for the specified array/z slice and mipmap level.
        /// </summary>
        /// <param name="arrayOrZSliceIndex">For 3D image, the parameter is the Z slice, otherwise it is an index into the texture array.</param>
        /// <param name="mipmap">The mipmap.</param>
        /// <returns>A <see cref="pixelBufferArray"/>.</returns>
        /// <exception cref="System.ArgumentException">If arrayOrZSliceIndex or mipmap are out of range.</exception>
        public PixelBuffer GetPixelBuffer(int arrayOrZSliceIndex, int mipmap)
        {
            // Check for parameters, as it is easy to mess up things...
            if (mipmap > Description.MipLevels)
                throw new ArgumentException("Invalid mipmap level", "mipmap");

            if (Description.Dimension == TextureDimension.Texture3D)
            {
                if (arrayOrZSliceIndex > Description.Depth)
                    throw new ArgumentException("Invalid z slice index", "arrayOrZSliceIndex");

                // For 3D textures
                return GetPixelBufferUnsafe(0, arrayOrZSliceIndex, mipmap);
            }

            if (arrayOrZSliceIndex > Description.ArraySize)
            {
                throw new ArgumentException("Invalid array slice index", "arrayOrZSliceIndex");
            }

            // For 1D, 2D textures
            return GetPixelBufferUnsafe(arrayOrZSliceIndex, 0, mipmap);
        }

        /// <summary>
        /// Gets the pixel buffer for the specified array/z slice and mipmap level.
        /// </summary>
        /// <param name="arrayIndex">Index into the texture array. Must be set to 0 for 3D images.</param>
        /// <param name="zIndex">Z index for 3D image. Must be set to 0 for all 1D/2D images.</param>
        /// <param name="mipmap">The mipmap.</param>
        /// <returns>A <see cref="pixelBufferArray"/>.</returns>
        /// <exception cref="System.ArgumentException">If arrayIndex, zIndex or mipmap are out of range.</exception>
        public PixelBuffer GetPixelBuffer(int arrayIndex, int zIndex, int mipmap)
        {
            // Check for parameters, as it is easy to mess up things...
            if (mipmap > Description.MipLevels)
                throw new ArgumentException("Invalid mipmap level", "mipmap");

            if (arrayIndex > Description.ArraySize)
                throw new ArgumentException("Invalid array slice index", "arrayIndex");

            if (zIndex > Description.Depth)
                throw new ArgumentException("Invalid z slice index", "zIndex");

            return this.GetPixelBufferUnsafe(arrayIndex, zIndex, mipmap);
        }

        /// <summary>
        /// Gets the databox from this image.
        /// </summary>
        /// <returns>The databox of this image.</returns>
        public DataBox[] ToDataBox()
        {
            return (DataBox[])dataBoxArray.Clone();
        }

        /// <summary>
        /// Gets the databox from this image.
        /// </summary>
        /// <returns>The databox of this image.</returns>
        private DataBox[] ComputeDataBox()
        {
            dataBoxArray = new DataBox[Description.ArraySize * Description.MipLevels];
            int i = 0;
            for (int arrayIndex = 0; arrayIndex < Description.ArraySize; arrayIndex++)
            {
                for (int mipIndex = 0; mipIndex < Description.MipLevels; mipIndex++)
                {
                    // Get the first z-slice (A DataBox for a Texture3D is pointing to the whole texture).
                    var pixelBuffer = this.GetPixelBufferUnsafe(arrayIndex, 0, mipIndex);

                    dataBoxArray[i].DataPointer = pixelBuffer.DataPointer;
                    dataBoxArray[i].RowPitch = pixelBuffer.RowStride;
                    dataBoxArray[i].SlicePitch = pixelBuffer.BufferStride;
                    i++;
                }
            }
            return dataBoxArray;
        }

        /// <summary>
        /// Gets the databox from this image.
        /// </summary>
        /// <returns>The databox of this image.</returns>
        private DataBox[] ComputeAnimatedDataBox()
        {
            dataBoxArray = new DataBox[Description.ArraySize * Description.MipLevels];
            int i = 0;
            for (int arrayIndex = 0; arrayIndex < PixelBuffer.Count; arrayIndex++)
            {
                dataBoxArray[i].DataPointer = PixelBuffer[i].DataPointer;
                dataBoxArray[i].RowPitch = PixelBuffer[i].RowStride;
                dataBoxArray[i].SlicePitch = PixelBuffer[i].BufferStride;
            }
            return dataBoxArray;
        }

        /// <summary>
        /// Creates a new instance of <see cref="Image"/> from an image description.
        /// </summary>
        /// <param name="description">The image description.</param>
        /// <returns>A new image.</returns>
        public static Image New(ImageDescription description)
        {
            return New(description, IntPtr.Zero);
        }
        
        public static Image New(ImageDescription description, byte[] data)
        {
            return New(description, IntPtr.Zero);
        }

        /// <summary>
        /// Creates a new instance of animated <see cref="Image"/> from an image description.
        /// </summary>
        /// <param name="description">The image description.</param>
        /// <returns>A new image.</returns>
        public static Image New(ImageDescription description, IRawBitmap multiFrameImage)
        {
            return new Image(description, multiFrameImage);
        }

        /// <summary>
        /// Creates a new instance of a 1D <see cref="Image"/>.
        /// </summary>
        /// <param name="width">The width.</param>
        /// <param name="mipMapCount">The mip map count.</param>
        /// <param name="format">The format.</param>
        /// <param name="arraySize">Size of the array.</param>
        /// <returns>A new image.</returns>
        public static Image New1D(uint width, MipMapCount mipMapCount, SurfaceFormat format, uint arraySize = 1)
        {
            return New1D(width, mipMapCount, format, arraySize, IntPtr.Zero);
        }
        
        /// <summary>
        /// Creates a new instance of a 2D <see cref="Image"/>.
        /// </summary>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        /// <param name="format">The format.</param>
        /// <param name="arraySize">Size of the array.</param>
        /// <returns>A new image.</returns>
        public static Image New2D(uint width, uint height, SurfaceFormat format, uint arraySize = 1)
        {
            return New2D(width, height, 1, format, arraySize);
        }

        /// <summary>
        /// Creates a new instance of a 2D <see cref="Image"/>.
        /// </summary>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        /// <param name="mipMapCount">The mip map count.</param>
        /// <param name="format">The format.</param>
        /// <param name="arraySize">Size of the array.</param>
        /// <returns>A new image.</returns>
        public static Image New2D(uint width, uint height, MipMapCount mipMapCount, SurfaceFormat format, uint arraySize = 1)
        {
            return New2D(width, height, mipMapCount, format, arraySize, IntPtr.Zero);
        }

        /// <summary>
        /// Creates a new instance of a Cube <see cref="Image"/>.
        /// </summary>
        /// <param name="width">The width.</param>
        /// <param name="mipMapCount">The mip map count.</param>
        /// <param name="format">The format.</param>
        /// <returns>A new image.</returns>
        public static Image NewCube(uint width, MipMapCount mipMapCount, SurfaceFormat format)
        {
            return NewCube(width, mipMapCount, format, IntPtr.Zero);
        }

        /// <summary>
        /// Creates a new instance of a 3D <see cref="Image"/>.
        /// </summary>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        /// <param name="depth">The depth.</param>
        /// <param name="mipMapCount">The mip map count.</param>
        /// <param name="format">The format.</param>
        /// <returns>A new image.</returns>
        public static Image New3D(uint width, uint height, uint depth, MipMapCount mipMapCount, SurfaceFormat format)
        {
            return New3D(width, height, depth, mipMapCount, format, IntPtr.Zero);
        }

        /// <summary>
        /// Creates a new instance of <see cref="Image"/> from an image description.
        /// </summary>
        /// <param name="description">The image description.</param>
        /// <param name="dataPointer">Pointer to an existing buffer.</param>
        /// <returns>A new image.</returns>
        public static Image New(ImageDescription description, IntPtr dataPointer)
        {
            return new Image(description, dataPointer, 0, null, false);
        }

        /// <summary>
        /// Creates a new instance of a 1D <see cref="Image"/>.
        /// </summary>
        /// <param name="width">The width.</param>
        /// <param name="mipMapCount">The mip map count.</param>
        /// <param name="format">The format.</param>
        /// <param name="arraySize">Size of the array.</param>
        /// <param name="dataPointer">Pointer to an existing buffer.</param>
        /// <returns>A new image.</returns>
        public static Image New1D(uint width, MipMapCount mipMapCount, SurfaceFormat format, uint arraySize, IntPtr dataPointer)
        {
            return new Image(CreateDescription(TextureDimension.Texture1D, width, 1, 1, mipMapCount, format, arraySize), dataPointer, 0, null, false);
        }

        /// <summary>
        /// Creates a new instance of a 2D <see cref="Image"/>.
        /// </summary>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        /// <param name="mipMapCount">The mip map count.</param>
        /// <param name="format">The format.</param>
        /// <param name="arraySize">Size of the array.</param>
        /// <param name="dataPointer">Pointer to an existing buffer.</param>
        /// <returns>A new image.</returns>
        public static Image New2D(uint width, uint height, MipMapCount mipMapCount, SurfaceFormat format, uint arraySize, IntPtr dataPointer)
        {
            return new Image(CreateDescription(TextureDimension.Texture2D, width, height, 1, mipMapCount, format, arraySize), dataPointer, 0, null, false);
        }

        /// <summary>
        /// Creates a new instance of a Cube <see cref="Image"/>.
        /// </summary>
        /// <param name="width">The width.</param>
        /// <param name="mipMapCount">The mip map count.</param>
        /// <param name="format">The format.</param>
        /// <param name="dataPointer">Pointer to an existing buffer.</param>
        /// <returns>A new image.</returns>
        public static Image NewCube(uint width, MipMapCount mipMapCount, SurfaceFormat format, IntPtr dataPointer)
        {
            return new Image(CreateDescription(TextureDimension.TextureCube, width, width, 1, mipMapCount, format, 6), dataPointer, 0, null, false);
        }

        /// <summary>
        /// Creates a new instance of a 3D <see cref="Image"/>.
        /// </summary>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        /// <param name="depth">The depth.</param>
        /// <param name="mipMapCount">The mip map count.</param>
        /// <param name="format">The format.</param>
        /// <param name="dataPointer">Pointer to an existing buffer.</param>
        /// <returns>A new image.</returns>
        public static Image New3D(uint width, uint height, uint depth, MipMapCount mipMapCount, SurfaceFormat format, IntPtr dataPointer)
        {
            return new Image(CreateDescription(TextureDimension.Texture3D, width, height, depth, mipMapCount, format, 1), dataPointer, 0, null, false);
        }

        /// <summary>
        /// Loads an image from an unmanaged memory pointer.
        /// </summary>
        /// <param name="dataBuffer">Pointer to an unmanaged memory. If <paramref name="makeACopy"/> is false, this buffer must be allocated with <see cref="Utilities.AllocateMemory"/>.</param>
        /// <param name="makeACopy">True to copy the content of the buffer to a new allocated buffer, false otherwhise.</param>
        /// <returns>An new image.</returns>
        /// <remarks>If <paramref name="makeACopy"/> is set to false, the returned image is now the holder of the unmanaged pointer and will release it on Dispose. </remarks>
        public static Image Load(DataPointer dataBuffer, bool makeAcopy = false)
        {
            return Load(dataBuffer.Pointer, dataBuffer.Size, makeAcopy);
        }

        /// <summary>
        /// Loads an image from an unmanaged memory pointer.
        /// </summary>
        /// <param name="dataPointer">Pointer to an unmanaged memory. If <paramref name="makeACopy"/> is false, this buffer must be allocated with <see cref="Utilities.AllocateMemory"/>.</param>
        /// <param name="dataSize">Size of the unmanaged buffer.</param>
        /// <param name="makeACopy">True to copy the content of the buffer to a new allocated buffer, false otherwise.</param>
        /// <returns>An new image.</returns>
        /// <remarks>If <paramref name="makeACopy"/> is set to false, the returned image is now the holder of the unmanaged pointer and will release it on Dispose. </remarks>
        public static Image Load(IntPtr dataPointer, ulong dataSize, bool makeAcopy = false)
        {
            var rawBitmap = BitmapLoader.Load(dataPointer, dataSize);

            return new Image(rawBitmap);
        }

        /// <summary>
        /// Loads an image from a managed buffer.
        /// </summary>
        /// <param name="buffer">Reference to a managed buffer.</param>
        /// <returns>An new image.</returns>
        /// <remarks>This method support the following format: <c>dds, bmp, jpg, png, gif, tiff, wmp, tga</c>.</remarks>
        public static unsafe Image Load(byte[] buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            ulong size = (ulong)buffer.Length;

            // If buffer is allocated on Large Object Heap, then we are going to pin it instead of making a copy.
            if (size > (85 * 1024))
            {
                var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                var result = Load(handle.AddrOfPinnedObject(), size);
                handle.Free();
                return result;
            }

            fixed (void* pBuffer = buffer)
            {
                return Load((IntPtr)pBuffer, size, true);
            }
        }

        /// <summary>
        /// Loads the specified image from a stream.
        /// </summary>
        /// <param name="imageStream">The image stream.</param>
        /// <param name="fileType"></param>
        /// <returns>An new image.</returns>
        /// <remarks>This method support the following format: <c>dds, bmp, jpg, png, gif, tiff, wmp, tga</c>.</remarks>
        public static Image Load(Stream imageStream, ImageFileType fileType = ImageFileType.Unknown)
        {
            return Load(Utilities.ReadStream(imageStream));
        }

        /// <summary>
        /// Loads the specified image from a file.
        /// </summary>
        /// <param name="fileName">The filename.</param>
        /// <returns>An new image.</returns>
        /// <remarks>This method support the following format: <c>dds, bmp, jpg, png, gif, tiff, wmp, tga</c>.</remarks>
        public static Image Load(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                throw new ArgumentNullException(nameof(fileName));
            }

            FileStream stream = null;
            IntPtr memoryPtr = IntPtr.Zero;
            ulong size;
            try
            {
                unsafe
                {
                    stream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
                    size = (ulong)stream.Length;
                    memoryPtr = Utilities.AllocateMemory((uint)size);
                    Span<byte> bytes = new Span<byte>(memoryPtr.ToPointer(), (int)size);
                    stream.ReadExactly(bytes);
                }
            }
            catch (Exception)
            {
                if (memoryPtr != IntPtr.Zero)
                    Utilities.FreeMemory(memoryPtr);
                throw;
            }
            finally
            {
                try
                {
                    stream?.Dispose();
                }
                catch { }
            }

            var type = GetImageTypeFromFileName(fileName);
            // If everything was fine, load the image from memory
            return Load(memoryPtr, size);
        }

        /// <summary>
        /// Returns <see cref="ImageFileType"/> from file name based on its extension to predict its type
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public static ImageFileType GetImageTypeFromFileName(string fileName)
        {
            ImageFileType fileType;
            var extension = Path.GetExtension(fileName);
            extension ??= string.Empty;
            extension = extension.TrimStart('.').ToLower();
            switch (extension)
            {
                case "jpg":
                    fileType = ImageFileType.Jpg;
                    break;
                case "dds":
                    fileType = ImageFileType.Dds;
                    break;
                case "gif":
                    fileType = ImageFileType.Gif;
                    break;
                case "bmp":
                    fileType = ImageFileType.Bmp;
                    break;
                case "png":
                    fileType = ImageFileType.Png;
                    break;
                case "tga":
                    fileType = ImageFileType.Tga;
                    break;
                case "tif":
                case "tiff":
                    fileType = ImageFileType.Tiff;
                    break;
                default:
                    fileType = ImageFileType.Unknown;
                    break;
            }
            return fileType;
        }

        /// <summary>
        /// Saves this instance to a file.
        /// </summary>
        /// <param name="fileName">The destination file.</param>
        /// <param name="fileType">Specify the output format.</param>
        /// <remarks>This method support the following format: <c>dds, bmp, jpg, png, gif, tiff, wmp, tga</c>.</remarks>
        public void Save(string fileName, ImageFileType fileType)
        {
            using (var imageStream = new FileStream(fileName, FileMode.Create, FileAccess.Write))
            {
                Save(imageStream, fileType);
            }
        }

        /// <summary>
        /// Saves this instance to a stream.
        /// </summary>
        /// <param name="imageStream">The destination stream.</param>
        /// <param name="fileType">Specify the output format.</param>
        /// <remarks>This method support the following format: <c>dds, bmp, jpg, png, gif, tiff, wmp, tga</c>.</remarks>
        public void Save(Stream imageStream, ImageFileType fileType)
        {
            var rawBitmap = this.ConvertToRawBitmap();
            rawBitmap.Save(imageStream, fileType);
        }

        public void ApplyPixelBuffer(PixelBuffer pixelBuffer, int index, bool freeBuffer)
        {
            var destinationBuffer = PixelBuffer[index];
            if (destinationBuffer == null)
            {
                throw new ArgumentOutOfRangeException($"Parameter index is outside pixel array bounds. Maximum numbers of elemnts in array: {PixelBuffer.Count}");
            }

            if (destinationBuffer.BufferStride != pixelBuffer.BufferStride)
            {
                throw new ArgumentOutOfRangeException($"Source pixel buffer stride is not the same as destination buffer stride. Source is {pixelBuffer.BufferStride}, destination - is {destinationBuffer.BufferStride}. Buffer size shuld be the same");
            }

            Utilities.CopyMemory(destinationBuffer.DataPointer, pixelBuffer.DataPointer, pixelBuffer.BufferStride);

            destinationBuffer.Format = pixelBuffer.Format;
            destinationBuffer.Width = pixelBuffer.Width;
            destinationBuffer.Height = pixelBuffer.Height;

            if (freeBuffer)
            {
                Utilities.FreeMemory(pixelBuffer.DataPointer);
            }
        }

        internal unsafe void Initialize(ImageDescription description, IntPtr dataPointer, ulong offset, GCHandle? handle, bool bufferIsDisposable, PitchFlags pitchFlags = PitchFlags.None)
        {
            if (!FormatHelper.IsValid(description.Format))
                throw new InvalidOperationException("Unsupported Image Format");

            this.handle = handle;

            switch (description.Dimension)
            {
                case TextureDimension.Texture1D:
                    if (description.Width <= 0 || description.Height != 1 || description.Depth != 1 || description.ArraySize == 0)
                        throw new InvalidOperationException("Invalid Width/Height/Depth/ArraySize for Image 1D");

                    // Check that miplevels are fine
                    description.MipLevels = CalculateMipLevels(description.Width, 1, description.MipLevels);
                    break;

                case TextureDimension.Texture2D:
                case TextureDimension.TextureCube:
                    if (description.Width <= 0 || description.Height <= 0 || description.Depth != 1 || description.ArraySize == 0)
                        throw new InvalidOperationException("Invalid Width/Height/Depth/ArraySize for Image 2D");

                    if (description.Dimension == TextureDimension.TextureCube)
                    {
                        if ((description.ArraySize % 6) != 0)
                            throw new InvalidOperationException("TextureCube must have an arraysize = 6");
                    }

                    // Check that miplevels are fine
                    description.MipLevels = CalculateMipLevels(description.Width, description.Height, description.MipLevels);
                    break;

                case TextureDimension.Texture3D:
                    if (description.Width <= 0 || description.Height <= 0 || description.Depth <= 0 || description.ArraySize != 1)
                        throw new InvalidOperationException("Invalid Width/Height/Depth/ArraySize for Image 3D");

                    // Check that miplevels are fine
                    description.MipLevels = CalculateMipLevels(description.Width, description.Height, description.Depth, description.MipLevels);
                    break;
            }

            // Calculate mipmaps
            mipMapToZIndex = CalculateImageArray(description, pitchFlags, out var pixelBufferCount, out totalSizeInBytes);
            mipmapDescriptions = CalculateMipMapDescription(description, pitchFlags);
            zBufferCountPerArraySlice = mipMapToZIndex[^1];

            // Allocate all pixel buffers
            pixelBuffers = new PixelBuffer[pixelBufferCount];
            pixelBufferArray = new PixelBufferArray(this);

            // Setup all pointers
            // only release buffer that is not pinned and is asked to be disposed.
            this.bufferIsDisposable = !handle.HasValue && bufferIsDisposable;
            buffer = dataPointer;

            if (dataPointer == IntPtr.Zero)
            {
                buffer = Utilities.AllocateMemory((int)totalSizeInBytes);
                offset = 0;
                this.bufferIsDisposable = true;
            }

            SetupImageArray((IntPtr)((byte*)buffer + offset), description, pitchFlags, pixelBuffers, mipmapDescriptions);

            Description = description;

            // PreCompute databoxes
            dataBoxArray = ComputeDataBox();
        }


        internal unsafe void InitializeMultiFrame(ImageDescription description, IRawBitmap multiFrameImage)
        {
            if (!FormatHelper.IsValid(description.Format))
                throw new InvalidOperationException("Unsupported Image Format");

            PitchFlags pitchFlags = PitchFlags.None;
            Frames = multiFrameImage;

            isAnimated = true;

            // Calculate mipmaps
            mipMapToZIndex = CalculateImageArray(description, pitchFlags, out var pixelBufferCount, out totalSizeInBytes);
            mipmapDescriptions = CalculateMipMapDescription(description, pitchFlags);
            zBufferCountPerArraySlice = mipMapToZIndex[^1];

            // Allocate all pixel buffers
            pixelBuffers = new PixelBuffer[pixelBufferCount];
            pixelBufferArray = new PixelBufferArray(this);

            // Setup all pointers
            // only release buffer that is not pinned and is asked to be disposed.
            bufferIsDisposable = !handle.HasValue && bufferIsDisposable;
            
            if (buffer == IntPtr.Zero)
            {
                buffer = Utilities.AllocateMemory((int)totalSizeInBytes);
                bufferIsDisposable = true;
            }

            SetupImageArray((IntPtr)((byte*)buffer), description, pitchFlags, pixelBuffers, mipmapDescriptions);

            Description = description;

            // PreCompute databoxes
            dataBoxArray = ComputeDataBox();
        }

        private PixelBuffer GetPixelBufferUnsafe(int arrayIndex, int zIndex, int mipmap)
        {
            var depthIndex = this.mipMapToZIndex[mipmap];
            var pixelBufferIndex = arrayIndex * this.zBufferCountPerArraySlice + depthIndex + zIndex;
            return pixelBuffers[pixelBufferIndex];
        }

        private static ImageDescription CreateDescription(TextureDimension dimension, uint width, uint height, uint depth, MipMapCount mipMapCount, SurfaceFormat format, uint arraySize)
        {
            return new ImageDescription()
            {
                Width = width,
                Height = height,
                Depth = depth,
                ArraySize = arraySize,
                Dimension = dimension,
                Format = format,
                MipLevels = mipMapCount,
            };
        }

        internal static void ComputePitch(Format format, int width, int height, out int rowPitch, out int slicePitch, out int widthCount, out int heightCount, PitchFlags flags = PitchFlags.None)
        {
            widthCount = width;
            heightCount = height;

            if (format.IsCompressed())
            {
                //int bpb = (format == Format.BC1_Typeless
                //            || format == Format.BC1_UNorm
                //            || format == Format.BC1_UNorm_SRgb
                //            || format == Format.BC4_Typeless
                //            || format == Format.BC4_UNorm
                //            || format == Format.BC4_SNorm) ? 8 : 16;
                int bytesPerPixel = format is 
                    Format.BC1_RGBA_SRGB_BLOCK or 
                    Format.BC1_RGBA_UNORM_BLOCK or
                    Format.BC2_UNORM_BLOCK or
                    Format.BC3_UNORM_BLOCK or
                    Format.BC4_UNORM_BLOCK or 
                    Format.BC4_SNORM_BLOCK ? 1 : 2;
                // widthCount = Math.Max(1, (width + 3) / 4);
                // heightCount = Math.Max(1, (height + 3) / 4);
                // rowPitch = widthCount * bpb;
                //
                // slicePitch = rowPitch * heightCount;
                rowPitch = width * bytesPerPixel;
                slicePitch = rowPitch * height;
            }
            else if (format.IsPacked())
            {
                rowPitch = ((width + 1) >> 1) * 4;

                slicePitch = rowPitch * height;
            }
            else
            {
                int bpp;

                if (flags.HasFlag(PitchFlags.Bpp24))
                    bpp = 24;
                else if (flags.HasFlag(PitchFlags.Bpp16))
                    bpp = 16;
                else if (flags.HasFlag(PitchFlags.Bpp8))
                    bpp = 8;
                else
                    bpp = format.SizeOfInBits();

                if ((flags & PitchFlags.LegacyDword) != 0)
                {
                    // Special computation for some incorrectly created DDS files based on
                    // legacy DirectDraw assumptions about pitch alignment
                    rowPitch = ((width * bpp + 31) / 32) * sizeof(int);
                    slicePitch = rowPitch * height;
                }
                else
                {
                    rowPitch = (width * bpp + 7) / 8;
                    slicePitch = rowPitch * height;
                }
            }
        }

        internal static MipMapDescription[] CalculateMipMapDescription(ImageDescription metadata, PitchFlags cpFlags = PitchFlags.None)
        {
            return CalculateMipMapDescription(metadata, cpFlags, out var nImages, out var pixelSize);
        }

        internal static MipMapDescription[] CalculateMipMapDescription(ImageDescription metadata, PitchFlags cpFlags, out int nImages, out int pixelSize)
        {
            pixelSize = 0;
            nImages = 0;

            var w = metadata.Width;
            var h = metadata.Height;
            var d = metadata.Depth;

            var mipmaps = new MipMapDescription[metadata.MipLevels];

            for (int level = 0; level < metadata.MipLevels; ++level)
            {
                int rowPitch, slicePitch;
                int widthPacked;
                int heightPacked;
                ComputePitch(metadata.Format, (int)w, (int)h, out rowPitch, out slicePitch, out widthPacked, out heightPacked, PitchFlags.None);

                mipmaps[level] = new MipMapDescription(
                    w,
                    h,
                    d,
                    rowPitch,
                    slicePitch,
                    widthPacked,
                    heightPacked
                    );

                pixelSize += (int)d * slicePitch;
                nImages += (int)d;

                if (h > 1)
                    h >>= 1;

                if (w > 1)
                    w >>= 1;

                if (d > 1)
                    d >>= 1;
            }
            return mipmaps;
        }

        private static ulong CalculateAnimatedImageArray(params AnimatedImageDescription[] descriptions)
        {
            if (descriptions == null)
                return 0;

            ulong allocateSize = 0;
            foreach (var desc in descriptions)
            {
                allocateSize += desc.Width * desc.Height * (uint)desc.BytesPerPixel;
            }

            return allocateSize;
        }

        /// <summary>
        /// Determines number of image array entries and pixel size.
        /// </summary>
        /// <param name="imageDesc">Description of the image to create.</param>
        /// <param name="pitchFlags">Pitch flags.</param>
        /// <param name="bufferCount">Output number of mipmap.</param>
        /// <param name="pixelSizeInBytes">Output total size to allocate pixel buffers for all images.</param>
        internal static List<int> CalculateImageArray(ImageDescription imageDesc, PitchFlags pitchFlags, out int bufferCount, out ulong pixelSizeInBytes)
        {
            pixelSizeInBytes = 0;
            bufferCount = 0;

            var mipmapToZIndex = new List<int>();

            for (int j = 0; j < imageDesc.ArraySize; j++)
            {
                var w = imageDesc.Width;
                var h = imageDesc.Height;
                var d = imageDesc.Depth;

                for (int i = 0; i < imageDesc.MipLevels; i++)
                {
                    int rowPitch, slicePitch;
                    int widthPacked;
                    int heightPacked;
                    ComputePitch(imageDesc.Format, (int)w, (int)h, out rowPitch, out slicePitch, out widthPacked, out heightPacked, pitchFlags);

                    // Store the number of z-slices per miplevel
                    if (j == 0)
                        mipmapToZIndex.Add(bufferCount);

                    // Keep a trace of indices for the 1st array size, for each mip levels
                    pixelSizeInBytes += d * (ulong)slicePitch;
                    bufferCount += (int)d;

                    if (h > 1)
                        h >>= 1;

                    if (w > 1)
                        w >>= 1;

                    if (d > 1)
                        d >>= 1;
                }

                // For the last mipmaps, store just the number of zbuffers in total
                if (j == 0)
                    mipmapToZIndex.Add(bufferCount);
            }
            return mipmapToZIndex;
        }

        /// <summary>
        /// Allocates PixelBuffers 
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="imageDesc"></param>
        /// <param name="pitchFlags"></param>
        /// <param name="output"></param>
        internal static unsafe void SetupImageArray(IntPtr buffer, ImageDescription imageDesc, PitchFlags pitchFlags, PixelBuffer[] output, MipMapDescription[] mipMapDescriptions)
        {
            int index = 0;
            var pixels = buffer;
            for (uint item = 0; item < imageDesc.ArraySize; ++item)
            {
                var width = imageDesc.Width;
                var height = imageDesc.Height;
                var depth = imageDesc.Depth;

                for (uint level = 0; level < imageDesc.MipLevels; ++level)
                {
                    int rowPitch, slicePitch;
                    int widthPacked;
                    int heightPacked;
                    ComputePitch(imageDesc.Format, (int)width, (int)height, out rowPitch, out slicePitch, out widthPacked, out heightPacked, pitchFlags);

                    for (uint zSlice = 0; zSlice < depth; ++zSlice)
                    {
                        // We use the same memory organization that Direct3D 11 needs for D3D11_SUBRESOURCE_DATA
                        // with all slices of a given miplevel being continuous in memory
                        var pixelBuffer = new PixelBuffer(width, height, imageDesc.Format, rowPitch, slicePitch, pixels);
                        pixelBuffer.MipLevel = level;
                        pixelBuffer.MipMapDescription = mipMapDescriptions[level];
                        output[index] = pixelBuffer;
                        ++index;

                        pixels += slicePitch;
                    }

                    if (height > 1)
                        height >>= 1;

                    if (width > 1)
                        width >>= 1;

                    if (depth > 1)
                        depth >>= 1;
                }
            }
        }

        public static uint CalculateMipLevels(uint width, uint height, MipMapCount mipLevels)
        {
            return CalculateMipLevels(width, height, 1, mipLevels);
        }


        public static uint CalculateMipLevels(uint width, uint height, uint depth, MipMapCount mipLevels)
        {
            var maxMipLevels = CountMipLevels(width, height, depth);
            if (mipLevels > 1 && maxMipLevels > mipLevels)
            {
                throw new InvalidOperationException($"MipLevels must be <= {maxMipLevels}");
            }
            return mipLevels;
        }

        private static int CountMipLevels(uint width, uint height, uint depth)
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

        private class LoadSaveDelegate
        {
            public LoadSaveDelegate(ImageFileType fileType, ImageLoadDelegate load, ImageSaveDelegate save)
            {
                FileType = fileType;
                Load = load;
                Save = save;
            }

            public ImageFileType FileType;

            public ImageLoadDelegate Load;

            public ImageSaveDelegate Save;
        }

    }
}