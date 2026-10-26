using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Core;
using Adamantium.Imaging.Bmp;
using Adamantium.Imaging.Dds;
using Adamantium.Imaging.Gif;
using Adamantium.Imaging.Ico;
using Adamantium.Imaging.Jpeg;
using Adamantium.Imaging.Png;
using Adamantium.Imaging.Tga;
using Adamantium.Imaging.Tiff;

namespace Adamantium.Imaging;

public static class BitmapLoader
{
    public delegate IRawBitmap ImageLoadDelegate(IntPtr dataPointer, ulong dataSize);
    public delegate void ImageSaveDelegate(IRawBitmap image, Stream imageStream);
    
    private static readonly List<LoadSaveDelegates> _loadSaveDelegates;
    
    static BitmapLoader()
    {
        _loadSaveDelegates = new List<LoadSaveDelegates>();
        Register(ImageFileType.Gif, GifHelper.LoadFromMemory, GifHelper.SaveToStream);
        Register(ImageFileType.Png, PngHelper.LoadFromMemory, PngHelper.SaveToStream);
        Register(ImageFileType.Bmp, BmpHelper.LoadFromMemory, BmpHelper.SaveToStream);
        Register(ImageFileType.Dds, DdsHelper.LoadFromMemory, DdsHelper.SaveToStream);
        Register(ImageFileType.Ico, IcoHelper.LoadFromMemory, IcoHelper.SaveToStream);
        Register(ImageFileType.Jpg, JpegHelper.LoadFromMemory, JpegHelper.SaveToStream);
        Register(ImageFileType.Tga, TgaHelper.LoadFromMemory, TgaHelper.SaveToStream);
        Register(ImageFileType.Tiff, TiffHelper.LoadFromMemory, TiffHelper.SaveToStream);
    }

    public static void Register(ImageFileType imageType, ImageLoadDelegate loadDelegate, ImageSaveDelegate saveDelegate)
    {
        var loader = new LoadSaveDelegates() { FileType = imageType, Loader = loadDelegate, Saver = saveDelegate };
        _loadSaveDelegates.Add(loader);
    }
    
    public static IRawBitmap Load(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            throw new ArgumentNullException(nameof(fileName));
        }

        using (var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read))
        {
            return Load(stream);
        }
    }

    /// <summary>
    /// Decode a picture from a stream. The bytes are read into a MANAGED array and pinned for the call rather than
    /// copied into freshly allocated native memory, which is what this used to do.
    /// <para>
    /// That is not a stylistic preference. A DDS cube map decoded fine from a pinned managed buffer and took the whole
    /// PROCESS down - an access violation, no catchable exception - from a native one holding the same bytes, at the
    /// same alignment, with megabytes of slack past the end. The decoder's sensitivity to that is not understood and is
    /// worth chasing separately; meanwhile every caller of the public API gets the path that works. Reading the whole
    /// stream at once also fixes a second latent bug here: <c>Stream.Read</c> is free to return fewer bytes than asked,
    /// and nothing checked.
    /// </para>
    /// </summary>
    public static unsafe IRawBitmap Load(Stream stream)
    {
        try
        {
            var bytes = new byte[stream.Length];
            var read = 0;
            while (read < bytes.Length)
            {
                var got = stream.Read(bytes, read, bytes.Length - read);
                if (got <= 0) break;
                read += got;
            }

            fixed (byte* pinned = bytes)
            {
                return Load((IntPtr)pinned, (ulong)read);
            }
        }
        finally
        {
            stream?.Dispose();
        }
    }
    
    /// <summary>
    /// The format these bytes actually are, by their SIGNATURE - so exactly one decoder is asked to read them.
    /// <para>
    /// Trying every decoder in turn until one stops complaining is not merely wasteful (each failed attempt parses a
    /// whole header, and some get far enough to decompress): a decoder handed a foreign file reads structure that is not
    /// there, and an unsafe one can walk off the end of the buffer. That takes the process down with an access
    /// violation, which no <c>catch</c> around the loop can intercept - a DDS cube map did exactly that.
    /// </para>
    /// <para>Unknown means "no signature matched" - notably TGA, which has none worth trusting at the start of the
    /// file; those still go through the try-everything path.</para>
    /// </summary>
    public static ImageFileType Detect(ReadOnlySpan<byte> header) => header switch
    {
        [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, ..] => ImageFileType.Png,
        [(byte)'G', (byte)'I', (byte)'F', (byte)'8', ..] => ImageFileType.Gif,
        [(byte)'B', (byte)'M', ..] => ImageFileType.Bmp,
        [(byte)'D', (byte)'D', (byte)'S', (byte)' ', ..] => ImageFileType.Dds,
        [0xFF, 0xD8, 0xFF, ..] => ImageFileType.Jpg,
        _ when TiffFile.IsTiff(header) => ImageFileType.Tiff,
        [0x00, 0x00, 0x01, 0x00, ..] => ImageFileType.Ico,
        _ => ImageFileType.Unknown,
    };

    public static unsafe IRawBitmap Load(IntPtr dataPointer, ulong dataSize)
    {
        // Signature first: ask the ONE decoder that owns this format.
        if (dataPointer != IntPtr.Zero && dataSize >= 8)
        {
            var detected = Detect(new ReadOnlySpan<byte>(dataPointer.ToPointer(), 8));
            if (detected != ImageFileType.Unknown)
            {
                var owner = _loadSaveDelegates.FirstOrDefault(x => x.FileType == detected);
                // A decoder that fails on a file whose signature IS its own means the file is broken or uses a variant
                // we do not read - saying so beats silently handing the bytes to a decoder they do not belong to.
                return owner?.Loader?.Invoke(dataPointer, dataSize);
            }
        }

        return LoadByTrying(dataPointer, dataSize);
    }

    private static IRawBitmap LoadByTrying(IntPtr dataPointer, ulong dataSize)
    {
        foreach (var loader in _loadSaveDelegates)
        {
            try
            {
                var img = loader.Loader?.Invoke(dataPointer, dataSize);
                return img;
            }
            catch (Exception)
            {
                // ignore
            }
        }

        return null;
    }

    public static void Save(IRawBitmap bitmap, string path, ImageFileType fileType)
    {
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        try
        {
            var saveDelegate = _loadSaveDelegates.FirstOrDefault(x => x.FileType == fileType);
            saveDelegate?.Saver?.Invoke(bitmap, stream);
        }
        finally
        {
            stream.Flush();
            stream.Dispose();
        }
    }
    
    public static void Save(IRawBitmap bitmap, Stream stream, ImageFileType fileType)
    {
        var saveDelegate = _loadSaveDelegates.FirstOrDefault(x => x.FileType == fileType);
        saveDelegate?.Saver?.Invoke(bitmap, stream);
    }
    
    private class LoadSaveDelegates
    {
        public ImageFileType FileType { get; set; }
        
        public ImageLoadDelegate Loader { get; set; }
        
        public ImageSaveDelegate Saver { get; set; }
    }
}