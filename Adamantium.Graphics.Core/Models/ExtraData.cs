using System.Collections.Generic;

namespace Adamantium.Graphics.Core.Models;

/// <summary>What a source file says of an object beyond what the engine models of it, kept as the file has it: the
/// object's own data (glTF's <c>extras</c>) and each of its extensions by name, all as JSON text.</summary>
public class ExtraData
{
    /// <summary>The object's own data as JSON; null when the file gives none.</summary>
    public string Data { get; set; }

    /// <summary>Each extension on the object by its name, its JSON as the file has it.</summary>
    public Dictionary<string, string> Extensions { get; set; } = new();
}
