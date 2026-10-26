using System.Collections.Generic;
using System.Linq;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging.Dds;

/// <summary>The formats of DDS files as Vulkan formats: those of the DX10 header by their DXGI_FORMAT number, and the
/// older ones by four-character code or by their channels' bit masks (Microsoft, "DDS" and "DXGI_FORMAT").</summary>
internal static class DdsFormats
{
    private static readonly (uint Dxgi, Format Vulkan)[] Table =
    [
        (1, Format.R32G32B32A32_SFLOAT), (2, Format.R32G32B32A32_SFLOAT), (3, Format.R32G32B32A32_UINT),
        (4, Format.R32G32B32A32_SINT), (5, Format.R32G32B32_SFLOAT), (6, Format.R32G32B32_SFLOAT),
        (7, Format.R32G32B32_UINT), (8, Format.R32G32B32_SINT), (9, Format.R16G16B16A16_UNORM),
        (10, Format.R16G16B16A16_SFLOAT), (11, Format.R16G16B16A16_UNORM), (12, Format.R16G16B16A16_UINT),
        (13, Format.R16G16B16A16_SNORM), (14, Format.R16G16B16A16_SINT), (15, Format.R32G32_SFLOAT),
        (16, Format.R32G32_SFLOAT), (17, Format.R32G32_UINT), (18, Format.R32G32_SINT),
        (20, Format.D32_SFLOAT_S8_UINT), (23, Format.A2B10G10R10_UNORM_PACK32), (24, Format.A2B10G10R10_UNORM_PACK32),
        (25, Format.A2B10G10R10_UINT_PACK32), (26, Format.B10G11R11_UFLOAT_PACK32), (27, Format.R8G8B8A8_UNORM),
        (28, Format.R8G8B8A8_UNORM), (29, Format.R8G8B8A8_SRGB), (30, Format.R8G8B8A8_UINT),
        (31, Format.R8G8B8A8_SNORM), (32, Format.R8G8B8A8_SINT), (33, Format.R16G16_UNORM), (34, Format.R16G16_SFLOAT),
        (35, Format.R16G16_UNORM), (36, Format.R16G16_UINT), (37, Format.R16G16_SNORM), (38, Format.R16G16_SINT),
        (39, Format.R32_SFLOAT), (40, Format.D32_SFLOAT), (41, Format.R32_SFLOAT), (42, Format.R32_UINT),
        (43, Format.R32_SINT), (45, Format.D24_UNORM_S8_UINT), (48, Format.R8G8_UNORM), (49, Format.R8G8_UNORM),
        (50, Format.R8G8_UINT), (51, Format.R8G8_SNORM), (52, Format.R8G8_SINT), (53, Format.R16_UNORM),
        (54, Format.R16_SFLOAT), (55, Format.D16_UNORM), (56, Format.R16_UNORM), (57, Format.R16_UINT),
        (58, Format.R16_SNORM), (59, Format.R16_SINT), (60, Format.R8_UNORM), (61, Format.R8_UNORM),
        (62, Format.R8_UINT), (63, Format.R8_SNORM), (64, Format.R8_SINT), (65, Format.R8_UNORM),
        (67, Format.E5B9G9R9_UFLOAT_PACK32), (70, Format.BC1_RGBA_UNORM_BLOCK), (71, Format.BC1_RGBA_UNORM_BLOCK),
        (72, Format.BC1_RGBA_SRGB_BLOCK), (73, Format.BC2_UNORM_BLOCK), (74, Format.BC2_UNORM_BLOCK),
        (75, Format.BC2_SRGB_BLOCK), (76, Format.BC3_UNORM_BLOCK), (77, Format.BC3_UNORM_BLOCK),
        (78, Format.BC3_SRGB_BLOCK), (79, Format.BC4_UNORM_BLOCK), (80, Format.BC4_UNORM_BLOCK),
        (81, Format.BC4_SNORM_BLOCK), (82, Format.BC5_UNORM_BLOCK), (83, Format.BC5_UNORM_BLOCK),
        (84, Format.BC5_SNORM_BLOCK), (85, Format.R5G6B5_UNORM_PACK16), (86, Format.A1R5G5B5_UNORM_PACK16),
        (87, Format.B8G8R8A8_UNORM), (88, Format.B8G8R8A8_UNORM), (90, Format.B8G8R8A8_UNORM),
        (91, Format.B8G8R8A8_SRGB), (92, Format.B8G8R8A8_UNORM), (93, Format.B8G8R8A8_SRGB),
        (94, Format.BC6H_UFLOAT_BLOCK), (95, Format.BC6H_UFLOAT_BLOCK), (96, Format.BC6H_SFLOAT_BLOCK),
        (97, Format.BC7_UNORM_BLOCK), (98, Format.BC7_UNORM_BLOCK), (99, Format.BC7_SRGB_BLOCK),
        (115, Format.A4R4G4B4_UNORM_PACK16)
    ];

    private static readonly Dictionary<uint, Format> FromDxgi = Table.ToDictionary(entry => entry.Dxgi, entry => entry.Vulkan);

    private static readonly Dictionary<Format, uint> ToDxgi = Table
        .Where(entry => entry.Dxgi is not (1 or 5 or 9 or 15 or 23 or 27 or 33 or 39 or 48 or 53 or 60 or 70 or 73
            or 76 or 79 or 82 or 90 or 92 or 94 or 97 or 65 or 88))
        .GroupBy(entry => entry.Vulkan)
        .ToDictionary(group => group.Key, group => group.First().Dxgi);

    /// <summary>The Vulkan format of DXGI_FORMAT <paramref name="dxgi"/>, or undefined.</summary>
    public static Format OfDxgi(uint dxgi) => FromDxgi.GetValueOrDefault(dxgi, Format.UNDEFINED);

    /// <summary>The DXGI_FORMAT of <paramref name="format"/>, or 0.</summary>
    public static uint Dxgi(Format format) => ToDxgi.GetValueOrDefault(format);

    /// <summary>Whether DXGI_FORMAT <paramref name="dxgi"/> has its alpha left out - read as opaque.</summary>
    public static bool IgnoresAlpha(uint dxgi) => dxgi is 88 or 92 or 93;

    /// <summary>The Vulkan format of a four-character code of the older header (or a D3DFORMAT number in its
    /// place), or undefined.</summary>
    public static Format OfFourCC(uint fourCC) => fourCC switch
    {
        Dxt1 => Format.BC1_RGBA_UNORM_BLOCK,
        Dxt2 or Dxt3 => Format.BC2_UNORM_BLOCK,
        Dxt4 or Dxt5 => Format.BC3_UNORM_BLOCK,
        Ati1 or Bc4U => Format.BC4_UNORM_BLOCK,
        Bc4S => Format.BC4_SNORM_BLOCK,
        Ati2 or Bc5U => Format.BC5_UNORM_BLOCK,
        Bc5S => Format.BC5_SNORM_BLOCK,
        36 => Format.R16G16B16A16_UNORM,
        110 => Format.R16G16B16A16_SNORM,
        111 => Format.R16_SFLOAT,
        112 => Format.R16G16_SFLOAT,
        113 => Format.R16G16B16A16_SFLOAT,
        114 => Format.R32_SFLOAT,
        115 => Format.R32G32_SFLOAT,
        116 => Format.R32G32B32A32_SFLOAT,
        _ => Format.UNDEFINED
    };

    /// <summary>The Vulkan format of an older header's channels by their bit masks - RGB(A), luminance(-alpha),
    /// alpha alone, signed bump maps - and whether alpha it does not have is to be read as opaque; undefined for one not
    /// known.</summary>
    public static (Format Format, bool Opaque) OfMasks(uint flags, uint bits, uint red, uint green, uint blue,
        uint alpha)
    {
        var hasAlpha = (flags & AlphaPixels) != 0;
        alpha = hasAlpha ? alpha : 0;
        if ((flags & Rgb) != 0)
        {
            return (bits, red, green, blue, alpha) switch
            {
                (32, 0xFF, 0xFF00, 0xFF0000, _) => (Format.R8G8B8A8_UNORM, alpha == 0),
                (32, 0xFF0000, 0xFF00, 0xFF, _) => (Format.B8G8R8A8_UNORM, alpha == 0),
                (32, 0x3FF, 0xFFC00, 0x3FF00000, _) => (Format.A2B10G10R10_UNORM_PACK32, alpha == 0),
                (32, 0x3FF00000, 0xFFC00, 0x3FF, _) => (Format.A2R10G10B10_UNORM_PACK32, alpha == 0),
                (32, 0xFFFF, 0xFFFF0000, 0, 0) => (Format.R16G16_UNORM, false),
                (32, 0xFFFFFFFF, 0, 0, 0) => (Format.R32_UINT, false),
                (24, 0xFF0000, 0xFF00, 0xFF, 0) => (Format.B8G8R8_UNORM, false),
                (24, 0xFF, 0xFF00, 0xFF0000, 0) => (Format.R8G8B8_UNORM, false),
                (16, 0xF800, 0x7E0, 0x1F, 0) => (Format.R5G6B5_UNORM_PACK16, false),
                (16, 0x7C00, 0x3E0, 0x1F, _) => (Format.A1R5G5B5_UNORM_PACK16, alpha == 0),
                (16, 0xF00, 0xF0, 0xF, _) => (Format.A4R4G4B4_UNORM_PACK16, alpha == 0),
                _ => (Format.UNDEFINED, false)
            };
        }

        if ((flags & Luminance) != 0)
        {
            return (bits, hasAlpha) switch
            {
                (8, false) => (Format.R8_UNORM, false),
                (16, false) => (Format.R16_UNORM, false),
                (16, true) => (Format.R8G8_UNORM, false),
                _ => (Format.UNDEFINED, false)
            };
        }

        if ((flags & AlphaOnly) != 0 && bits == 8)
        {
            return (Format.R8_UNORM, false);
        }

        if ((flags & BumpDuDv) != 0)
        {
            return (bits, red, green) switch
            {
                (16, 0xFF, 0xFF00) => (Format.R8G8_SNORM, false),
                (32, 0xFF, 0xFF00) when blue == 0xFF0000 => (Format.R8G8B8A8_SNORM, false),
                (32, 0xFFFF, 0xFFFF0000) => (Format.R16G16_SNORM, false),
                _ => (Format.UNDEFINED, false)
            };
        }

        return (Format.UNDEFINED, false);
    }

    /// <summary>The older header's code or masks for formats it has, written so that readers of only it read the file;
    /// null for those the DX10 header carries.</summary>
    public static (uint Flags, uint FourCC, uint Bits, uint Red, uint Green, uint Blue, uint Alpha)? Legacy(Format format)
        => format switch
        {
            Format.R8G8B8A8_UNORM => (Rgb | AlphaPixels, 0, 32, 0xFF, 0xFF00, 0xFF0000, 0xFF000000),
            Format.B8G8R8A8_UNORM => (Rgb | AlphaPixels, 0, 32, 0xFF0000, 0xFF00, 0xFF, 0xFF000000),
            Format.B8G8R8_UNORM => (Rgb, 0, 24, 0xFF0000, 0xFF00, 0xFF, 0),
            Format.R5G6B5_UNORM_PACK16 => (Rgb, 0, 16, 0xF800, 0x7E0, 0x1F, 0),
            Format.R8_UNORM => (Luminance, 0, 8, 0xFF, 0, 0, 0),
            Format.R16_UNORM => (Luminance, 0, 16, 0xFFFF, 0, 0, 0),
            Format.BC1_RGBA_UNORM_BLOCK => (FourCCFlag, Dxt1, 0, 0, 0, 0, 0),
            Format.BC2_UNORM_BLOCK => (FourCCFlag, Dxt3, 0, 0, 0, 0, 0),
            Format.BC3_UNORM_BLOCK => (FourCCFlag, Dxt5, 0, 0, 0, 0, 0),
            _ => null
        };

    public const uint AlphaPixels = 0x1;
    public const uint AlphaOnly = 0x2;
    public const uint FourCCFlag = 0x4;
    public const uint Rgb = 0x40;
    public const uint Luminance = 0x20000;
    public const uint BumpDuDv = 0x80000;

    public static uint Code(string four) => four[0] | (uint)four[1] << 8 | (uint)four[2] << 16 | (uint)four[3] << 24;

    public static readonly uint Dx10 = Code("DX10");
    private const uint Dxt1 = 0x31545844;
    private const uint Dxt2 = 0x32545844;
    private const uint Dxt3 = 0x33545844;
    private const uint Dxt4 = 0x34545844;
    private const uint Dxt5 = 0x35545844;
    private const uint Ati1 = 0x31495441;
    private const uint Ati2 = 0x32495441;
    private const uint Bc4U = 0x55344342;
    private const uint Bc4S = 0x53344342;
    private const uint Bc5U = 0x55354342;
    private const uint Bc5S = 0x53354342;
}
