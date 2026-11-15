using System;
using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>An entity of the tree, the part all of them share: its name, attributes, graphics and the identifier other
/// entities refer to it by.</summary>
internal class PrcTreeEntity
{
    internal uint Type { get; init; }

    internal string Name { get; set; }

    internal PrcGraphics Graphics { get; set; } = PrcGraphics.Default;

    /// <summary>The entity's PRC unique identifier in its file structure; 0 for none.</summary>
    internal uint UniqueId { get; set; }

    internal Dictionary<string, string> Attributes { get; } = new(StringComparer.Ordinal);
}
