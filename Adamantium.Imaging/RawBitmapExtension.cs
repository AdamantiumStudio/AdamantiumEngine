using System.IO;

namespace Adamantium.Imaging;

public static class RawBitmapExtension
{
    public static void Save(this IRawBitmap bitmap, string path, ImageFileType fileType)
    {
        BitmapLoader.Save(bitmap, path, fileType);
    }

    public static void Save(this IRawBitmap bitmap, Stream stream, ImageFileType fileType)
    {
        BitmapLoader.Save(bitmap, stream, fileType);
    }
}
