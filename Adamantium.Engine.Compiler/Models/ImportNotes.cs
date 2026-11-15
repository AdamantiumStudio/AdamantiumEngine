using System;
using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models;

/// <summary>What a model file's reader found that the import leaves out, each said once.</summary>
internal sealed class ImportNotes
{
    private readonly HashSet<string> _told = new(StringComparer.Ordinal);

    /// <summary>The notes in the order they were first told.</summary>
    internal List<string> All { get; } = [];

    /// <summary>Adds <paramref name="note"/> unless it was told already.</summary>
    internal void Tell(string note)
    {
        if (_told.Add(note))
        {
            All.Add(note);
        }
    }
}
