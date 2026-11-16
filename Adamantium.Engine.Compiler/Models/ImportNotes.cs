using System;
using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models;

/// <summary>What a model file's reader found worth saying, each said once: what the import leaves out of the model, and
/// remarks on what it read as the file says or had no place for, which leave nothing of the model out.</summary>
internal sealed class ImportNotes
{
    private readonly HashSet<string> _told = new(StringComparer.Ordinal);

    /// <summary>What of the model is not drawn, or drawn otherwise than the file says, in the order first told.</summary>
    internal List<string> Unsupported { get; } = [];

    /// <summary>What was read as the file says, or kept out of the model's drawing by the file itself, or had no place in
    /// the scene data, in the order first told: nothing of what the model shows is missing for them.</summary>
    internal List<string> Remarks { get; } = [];

    /// <summary>Adds <paramref name="note"/> to <see cref="Unsupported"/> unless it was told already.</summary>
    internal void Tell(string note)
    {
        if (_told.Add(note))
        {
            Unsupported.Add(note);
        }
    }

    /// <summary>Adds <paramref name="note"/> to <see cref="Remarks"/> unless it was told already.</summary>
    internal void Remark(string note)
    {
        if (_told.Add(note))
        {
            Remarks.Add(note);
        }
    }
}
