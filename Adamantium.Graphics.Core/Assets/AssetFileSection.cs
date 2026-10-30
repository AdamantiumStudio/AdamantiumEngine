using System;

namespace Adamantium.Graphics.Core.Assets;

/// <summary>The parts of an <see cref="AssetFile"/>, each readable without the others.</summary>
[Flags]
public enum AssetFileSection
{
    None = 0,

    /// <summary>A PNG picture of the asset.</summary>
    Thumbnail = 1,

    /// <summary>The <see cref="AssetSource"/> the asset was imported from.</summary>
    Source = 2,

    /// <summary>The asset itself.</summary>
    Body = 4,

    All = Thumbnail | Source | Body
}
