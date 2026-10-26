using System;
using System.Linq;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;
using Image = Adamantium.Imaging.Image;

namespace Adamantium.Graphics;

public class TextureCube : Texture
{
    internal TextureCube(GraphicsDevice device, Image image, ImageUsageFlagBits usage, ImageLayout desiredLayout)
        : base(device, image, usage, desiredLayout)
    {
    }

    /// <summary>A cube of the six square pictures in <paramref name="files"/>, one face each, of one size and format.
    /// </summary>
    public static TextureCube Load(GraphicsDevice device,
        string[] files,
        ImageUsageFlagBits usage = ImageUsageFlagBits.SampledBit,
        ImageLayout desiredLayout = ImageLayout.ColorAttachmentOptimal)
    {
        if (files.Length != 6)
            throw new ArgumentOutOfRangeException(nameof(files), "Texture cube should have exactly 6 files");

        var faces = files.Select(BitmapLoader.Load).ToArray();
        var first = faces[0];
        if (first.Width != first.Height || faces.Any(face => face.Width != first.Width || face.Height != first.Height
                                                             || face.PixelFormat != first.PixelFormat))
        {
            throw new ArgumentException("The faces of a texture cube must be square, of one size and pixel format.",
                nameof(files));
        }

        using var image = Image.NewCube(first.Width, 1, first.PixelFormat);
        for (var face = 0; face < faces.Length; face++)
        {
            image.GetPixelBuffer(face, 0).SetPixels(faces[face].GetRawPixels(0));
        }

        return new TextureCube(device, image, usage, desiredLayout);
    }
}
