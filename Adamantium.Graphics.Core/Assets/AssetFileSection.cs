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

    /// <summary>The <see cref="Models.ModelEdits"/> made to a model in the editor, as
    /// <see cref="Models.ModelEditsSerializer"/> writes them; a new import keeps them.</summary>
    Edits = 8,

    All = Thumbnail | Source | Body | Edits
}
