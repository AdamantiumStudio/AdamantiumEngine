using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A product occurrence (PRC_TYPE_ASM_ProductOccurence): a node of the assembly, its part, prototype or
/// external data, its sons and where it stands in its father.</summary>
internal sealed class PrcProductOccurrence : PrcTreeEntity
{
    /// <summary>The occurrence is suppressed.</summary>
    internal const byte Suppressed = 0x01;

    internal int PartIndex { get; init; } = -1;

    internal int PrototypeIndex { get; init; } = -1;

    /// <summary>The file structure the prototype lies in; null for this one.</summary>
    internal PrcUniqueId PrototypeFileStructure { get; init; }

    internal int ExternalIndex { get; init; } = -1;

    internal PrcUniqueId ExternalFileStructure { get; init; }

    internal List<int> Sons { get; } = [];

    internal byte Behavior { get; init; }

    internal bool UnitFromCad { get; set; }

    /// <summary>The unit in millimeters.</summary>
    internal double Unit { get; set; }

    /// <summary>Where it stands in its father, as a row-vector matrix; null for none.</summary>
    internal Matrix4x4F? Location { get; set; }

    /// <summary>Entities under it it redefines - their place, style or visibility.</summary>
    internal List<PrcEntityReference> EntityReferences { get; } = [];

    internal PrcMarkupData Markups { get; set; }

    internal List<PrcView> Views { get; } = [];

    /// <summary>The filter applied to what its prototype loads; null for none.</summary>
    internal PrcFilter EntityFilter { get; set; }

    /// <summary>The filters it can be shown by, at most one of them active.</summary>
    internal List<PrcFilter> DisplayFilters { get; } = [];

    internal List<PrcSceneDisplayParameters> SceneDisplayParameters { get; } = [];
}
