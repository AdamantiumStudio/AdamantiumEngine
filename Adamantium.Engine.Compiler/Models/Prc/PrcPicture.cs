namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A picture of the globals (PRC_TYPE_GRAPH_Picture), held in one of the file's uncompressed files: PNG,
/// JPEG, or flate-compressed pixels of one to four components.</summary>
internal sealed class PrcPicture
{
    internal const int Png = 0;
    internal const int Jpeg = 1;
    internal const int RgbBytes = 2;
    internal const int RgbaBytes = 3;
    internal const int GrayBytes = 4;
    internal const int GrayAlphaBytes = 5;

    internal string Name { get; init; }

    internal int Format { get; init; }

    internal int UncompressedFileIndex { get; init; }

    internal uint Width { get; init; }

    internal uint Height { get; init; }
}
