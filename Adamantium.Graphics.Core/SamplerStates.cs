using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Core;

/// <summary>
/// The standard samplers. A sampler is only a description, so one set serves every device.
/// </summary>
public static class SamplerStates
{
    /// <summary>
    /// Linear filtering with texture coordinate wrapping.
    /// </summary>
    public static readonly SamplerState LinearRepeat =
        GetSamplerSate(nameof(LinearRepeat), Filter.Linear, SamplerAddressMode.Repeat, false);

    /// <summary>
    /// Linear filtering with texture coordinate clamping to border.
    /// </summary>
    public static readonly SamplerState LinearClampToBorder =
        GetSamplerSate(nameof(LinearClampToBorder), Filter.Linear, SamplerAddressMode.ClampToBorder, false);

    /// <summary>
    /// Linear filtering with texture coordinate clamping to edge.
    /// </summary>
    public static readonly SamplerState LinearClampToEdge =
        GetSamplerSate(nameof(LinearClampToEdge), Filter.Linear, SamplerAddressMode.ClampToEdge, false);

    /// <summary>
    /// A picture enlarged in hard squares - the nearest texel, no blend between them - and reduced smoothly, so it does
    /// not shimmer; texture coordinates clamped to the edge. CSS's <c>image-rendering: pixelated</c>.
    /// </summary>
    public static readonly SamplerState PixelatedClampToEdge =
        GetSamplerSate(nameof(PixelatedClampToEdge), Filter.Linear, SamplerAddressMode.ClampToEdge, false,
            magFilter: Filter.Nearest);

    /// <summary>
    /// Linear filtering with texture coordinate mirroring.
    /// </summary>
    public static readonly SamplerState LinearMirror =
        GetSamplerSate(nameof(LinearMirror), Filter.Linear, SamplerAddressMode.MirrorClampToEdge, false);

    /// <summary>
    /// Anisotropic filtering with texture coordinate wrapping.
    /// </summary>
    public static readonly SamplerState AnisotropicRepeat =
        GetSamplerSate(nameof(AnisotropicRepeat), Filter.Linear, SamplerAddressMode.Repeat);

    /// <summary>
    /// Anisotropic filtering with texture coordinate clamping.
    /// </summary>
    public static readonly SamplerState AnisotropicClampToBorder =
        GetSamplerSate(nameof(AnisotropicClampToBorder), Filter.Linear, SamplerAddressMode.ClampToBorder);

    /// <summary>
    /// Anisotropic filtering with texture coordinate clamping.
    /// </summary>
    public static readonly SamplerState AnisotropicClampToEdge =
        GetSamplerSate(nameof(AnisotropicClampToEdge), Filter.Linear, SamplerAddressMode.ClampToEdge);

    /// <summary>
    /// Anisotropic filtering with texture coordinate mirroring.
    /// </summary>
    public static readonly SamplerState AnisotropicMirror =
        GetSamplerSate(nameof(AnisotropicMirror), Filter.Linear, SamplerAddressMode.MirrorClampToEdge);

    /// <summary>
    /// Plain bilinear with anisotropy off: an MSDF atlas has no mips, and anisotropic taps average the distance field
    /// into soft glyph edges.
    /// </summary>
    public static readonly SamplerState LinearFont =
        GetSamplerSate(nameof(LinearFont), Filter.Linear, SamplerAddressMode.Repeat, false);

    public static readonly SamplerState NearestFont =
        GetSamplerSate(nameof(NearestFont), Filter.Nearest, SamplerAddressMode.Repeat);

    /// <summary>
    /// Default state is using linear filtering with texture coordinate clamping.
    /// </summary>
    public static readonly SamplerState Default = AnisotropicClampToBorder;

    private static SamplerState GetSamplerSate(
        string name,
        Filter filter,
        SamplerAddressMode samplerMode,
        bool anisotropyEnabled = true,
        float maxAnisotropy = 16,
        bool unnormalizedCoordinates = false,
        bool compareEnabled = false,
        CompareOp compareOp = CompareOp.Always,
        Filter? magFilter = null)
    {
        var samplerInfo = new SamplerCreateInfo();
        samplerInfo.MagFilter = magFilter ?? filter;
        samplerInfo.MinFilter = filter;
        samplerInfo.AddressModeU = samplerMode;
        samplerInfo.AddressModeV = samplerMode;
        samplerInfo.AddressModeW = samplerMode;
        samplerInfo.AnisotropyEnable = anisotropyEnabled;
        samplerInfo.MaxAnisotropy = maxAnisotropy;
        samplerInfo.BorderColor = BorderColor.IntOpaqueWhite;
        samplerInfo.UnnormalizedCoordinates = unnormalizedCoordinates;
        samplerInfo.CompareEnable = compareEnabled;
        samplerInfo.CompareOp = compareOp;
        samplerInfo.MipmapMode = SamplerMipmapMode.Linear;

        // WITHOUT THIS ONLY LEVEL 0 IS EVER READ: MaxLod defaults to zero, which clamps every sample to the top of
        // the pyramid however high a level the shader asks for. A texture with one level is unaffected - it has
        // nothing else to reach.
        samplerInfo.MaxLod = 32.0f;

        return SamplerState.New(name, samplerInfo);
    }
}