using System;

namespace Adamantium.MacOS.CoreGraphics;

/// <summary>Images made from pixels, for a layer to show.</summary>
public static unsafe class CGImage
{
    private const uint PremultipliedFirst = 2;
    private const uint ByteOrder32Little = 2 << 12;

    /// <summary>An image of <paramref name="width"/> by <paramref name="height"/> premultiplied BGRA pixels, top row
    /// first, owned by the caller (<see cref="CoreGraphicsInterop.Release"/>).</summary>
    public static IntPtr FromPremultipliedBgra(byte[] pixels, int width, int height)
    {
        IntPtr data;
        fixed (byte* bytes = pixels)
        {
            data = CoreGraphicsInterop.CreateData(IntPtr.Zero, bytes, pixels.Length);
        }

        var provider = CoreGraphicsInterop.CreateDataProvider(data);
        var colorSpace = CoreGraphicsInterop.CreateDeviceRgbColorSpace();
        var image = CoreGraphicsInterop.CreateImage((nuint)width, (nuint)height, 8, 32, (nuint)(width * 4), colorSpace,
            PremultipliedFirst | ByteOrder32Little, provider, null, false, 0);
        CoreGraphicsInterop.Release(colorSpace);
        CoreGraphicsInterop.Release(provider);
        CoreGraphicsInterop.Release(data);
        return image;
    }
}
