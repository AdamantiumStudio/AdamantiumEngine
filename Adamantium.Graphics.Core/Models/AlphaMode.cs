namespace Adamantium.Graphics.Core.Models;

/// <summary>How a material takes the alpha of its base color, as glTF names it.</summary>
public enum AlphaMode
{
    /// <summary>The alpha is ignored: the surface is opaque.</summary>
    Opaque,

    /// <summary>Opaque where the alpha reaches the cutoff, left out below it.</summary>
    Mask,

    /// <summary>Blended over what is behind it by its alpha.</summary>
    Blend
}
