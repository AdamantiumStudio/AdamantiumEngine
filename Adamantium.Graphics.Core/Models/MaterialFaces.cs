namespace Adamantium.Graphics.Core.Models;

/// <summary>Which faces of a surface are drawn - its front, the side its triangles wind towards, its back, or both, the
/// default: a model whose triangles wind either way still shows whole.</summary>
public enum MaterialFaces
{
    /// <summary>Both faces, nothing culled.</summary>
    Both,

    /// <summary>The front faces only, the back ones culled.</summary>
    Front,

    /// <summary>The back faces only, the front ones culled.</summary>
    Back
}
