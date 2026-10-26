namespace Adamantium.Imaging;

public interface IRawBitmap
{
    public uint Width { get; }
    
    public uint Height { get; }
    
    public SurfaceFormat PixelFormat { get; }

    /// <summary>The bytes of one frame - whole blocks for a compressed format.</summary>
    public ulong TotalSizeInBytes
    {
        get
        {
            var (row, rows) = TexelBlocks.Rows(PixelFormat, Width, Height);
            return (ulong)(row * rows);
        }
    }
    
    public bool IsMultiFrame { get; } 
    
    public bool HasMipLevels { get; }
    
    public uint MipLevelsCount { get; }
    
    public uint NumberOfReplays { get; }
        
    public uint FramesCount { get; }
        
    public byte[] GetRawPixels(uint frameIndex);

    /// <summary>Writes one frame's pixels into <paramref name="destination"/> (<see cref="TotalSizeInBytes"/> long). A
    /// decoder that can lay frames out without keeping them does so here, so frames read in order share one buffer.
    /// </summary>
    public void CopyFrame(uint frameIndex, System.Span<byte> destination)
    {
        var pixels = GetRawPixels(frameIndex);
        new System.ReadOnlySpan<byte>(pixels).CopyTo(destination);
        destination[pixels.Length..].Clear();
    }

    public FrameData GetMipLevelData(uint mipLevel);

    public ImageDescription GetImageDescription();

    public FrameData GetFrameData(uint frameIndex);

    /// <summary>Drops whatever decoded pixel data is being held for the frames, keeping only what is needed to decode
    /// them again. For an animation handed to the GPU as one texture there is nothing left to read them for, and a
    /// long one holds hundreds of megabytes this way. Decoders that keep nothing can ignore this.</summary>
    public void ReleaseDecodedFrames() { }
}