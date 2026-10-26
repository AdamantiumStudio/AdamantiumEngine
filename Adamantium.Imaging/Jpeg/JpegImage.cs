namespace Adamantium.Imaging.Jpeg;

/// <summary>A JPEG's picture: one frame of pixels in one format.</summary>
public class JpegImage : IRawBitmap
{
    private readonly byte[] _pixels;
    private FrameData _frame;

    /// <summary>A picture of <paramref name="width"/> × <paramref name="height"/> <paramref name="pixels"/> in
    /// <paramref name="format"/>, rows top to bottom.</summary>
    public JpegImage(uint width, uint height, SurfaceFormat format, byte[] pixels)
    {
        Width = width;
        Height = height;
        PixelFormat = format;
        _pixels = pixels;
    }

    public uint Width { get; }

    public uint Height { get; }

    public SurfaceFormat PixelFormat { get; }

    public bool IsMultiFrame => false;

    public bool HasMipLevels => false;

    public uint MipLevelsCount => 0;

    public uint NumberOfReplays => 0;

    public uint FramesCount => 1;

    public byte[] GetRawPixels(uint frameIndex) => _pixels;

    public FrameData GetMipLevelData(uint mipLevel) => GetFrameData(0);

    public ImageDescription GetImageDescription() => new()
    {
        Width = Width,
        Height = Height,
        Depth = 1,
        Dimension = TextureDimension.Texture2D,
        ArraySize = 1,
        MipLevels = 1,
        Format = PixelFormat
    };

    public FrameData GetFrameData(uint frameIndex) => _frame ??= new FrameData(_pixels, GetImageDescription());
}
