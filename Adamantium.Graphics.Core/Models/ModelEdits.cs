using System;
using System.Collections.Generic;

namespace Adamantium.Graphics.Core.Models;

/// <summary>The edits made to a model in the editor, kept in its asset file apart from what its import gives, so a new
/// import keeps them.</summary>
public sealed class ModelEdits
{
    /// <summary>The edits of the model's parts, by the path of their names from the model's root, which is "".
    /// </summary>
    public Dictionary<String, PartEdits> Parts { get; set; } = new(StringComparer.Ordinal);
}
