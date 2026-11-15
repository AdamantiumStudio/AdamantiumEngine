using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A run of one entity type's instructions: where it is, its values and its loops.</summary>
internal sealed class PrcSchemaCall
{
    internal PrcSchemaCall(PrcSchemaProgram program, bool wholeEntity)
    {
        Program = program;
        IsWholeEntity = wholeEntity;
    }

    internal PrcSchemaProgram Program { get; }

    /// <summary>True when the entity is read whole from the schema, false when only blocks newer than the reader run.</summary>
    internal bool IsWholeEntity { get; }

    internal int Position { get; set; }

    internal Dictionary<long, double> Variables { get; } = new();

    internal Stack<PrcSchemaLoop> Loops { get; } = new();
}
