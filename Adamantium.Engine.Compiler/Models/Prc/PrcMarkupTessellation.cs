using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A markup tessellation (PRC_TYPE_TESS_Markup): its codes, which say what each run of coordinates draws.</summary>
internal sealed class PrcMarkupTessellation : PrcTessellation
{
    internal uint[] Codes { get; set; } = [];

    internal List<string> Texts { get; } = [];

    internal string Label { get; set; }

    internal byte Behavior { get; set; }
}
