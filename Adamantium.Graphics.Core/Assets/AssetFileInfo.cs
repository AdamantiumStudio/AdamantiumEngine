using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.Imaging;

namespace Adamantium.Graphics.Core.Assets;

/// <summary>An asset in a folder, known without loading it: its name, its file and its kind by the file; its
/// <see cref="Source"/> is read on the first ask, once, from any thread.</summary>
public sealed class AssetFileInfo
{
    private static readonly Dictionary<string, ImageFileType> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = ImageFileType.Png, [".jpg"] = ImageFileType.Jpg, [".jpeg"] = ImageFileType.Jpg,
        [".gif"] = ImageFileType.Gif, [".bmp"] = ImageFileType.Bmp, [".tga"] = ImageFileType.Tga,
        [".tif"] = ImageFileType.Tiff, [".tiff"] = ImageFileType.Tiff, [".ico"] = ImageFileType.Ico,
        [".dds"] = ImageFileType.Dds
    };

    private static readonly HashSet<string> FontExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".ttf", ".otf", ".ttc", ".otc", ".woff", ".woff2" };

    private static readonly HashSet<string> SoundExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".wav", ".ogg", ".mp3", ".flac" };

    private readonly Lazy<AssetSource> source;

    public AssetFileInfo(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        FilePath = Path.GetFullPath(filePath);
        var fileName = Path.GetFileName(FilePath);
        var extension = Path.GetExtension(FilePath);
        Kind = GetKind(extension);
        Name = Kind == AssetKind.Model ? fileName[..^AssetFile.FileExtension.Length] : fileName;
        source = new Lazy<AssetSource>(ReadSource);
    }

    /// <summary>The asset's name: its file's, an asset file's without its extension - that of the source it is made
    /// from.</summary>
    public string Name { get; }

    /// <summary>The full path of the asset's file.</summary>
    public string FilePath { get; }

    public AssetKind Kind { get; }

    /// <summary>Where a model was imported from; null for any other kind or an asset file that does not say.</summary>
    public AssetSource Source => source.Value;

    /// <summary>The asset's picture, decoded anew on each call and kept by no one here - the view shrinks what it shows:
    /// a model's from its asset file, an image's first frame. Null for a kind with none, an asset file with none yet,
    /// or a file that cannot be read.</summary>
    public IRawBitmap ReadThumbnail()
    {
        try
        {
            return Kind switch
            {
                AssetKind.Model => AssetFile.Read(FilePath, AssetFileSection.Thumbnail).Thumbnail is { } png
                    ? CopyFirstFrame(BitmapLoader.Load(new MemoryStream(png, false)))
                    : null,
                AssetKind.Image => CopyFirstFrame(BitmapLoader.Load(FilePath, ImageTypes[Path.GetExtension(FilePath)])),
                _ => null
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static AssetKind GetKind(string extension)
    {
        if (string.Equals(extension, AssetFile.FileExtension,
                OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
        {
            return AssetKind.Model;
        }

        if (ImageTypes.ContainsKey(extension))
        {
            return AssetKind.Image;
        }

        if (FontExtensions.Contains(extension))
        {
            return AssetKind.Font;
        }

        return SoundExtensions.Contains(extension) ? AssetKind.Sound : AssetKind.Other;
    }

    private static IRawBitmap CopyFirstFrame(IRawBitmap bitmap)
    {
        return bitmap == null
            ? null
            : new FrameBitmap(bitmap.Width, bitmap.Height, bitmap.PixelFormat, [bitmap.GetRawPixels(0)]);
    }

    private AssetSource ReadSource()
    {
        if (Kind != AssetKind.Model)
        {
            return null;
        }

        try
        {
            return AssetFile.Read(FilePath, AssetFileSection.Source).Source;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or AssetFormatException)
        {
            return null;
        }
    }
}
