using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A view (PRC_TYPE_MKP_View): annotations shown together on a plane, with the scene they are seen in, the
/// items they link and the filters they show the model by.</summary>
internal sealed class PrcView : PrcTreeEntity
{
    internal List<PrcReferenceTarget> Annotations { get; } = [];

    /// <summary>Null for none.</summary>
    internal PrcSceneDisplayParameters Scene { get; set; }

    internal bool IsAnnotationView { get; set; }

    internal bool IsDefault { get; set; }

    internal List<PrcReferenceTarget> LinkedItems { get; } = [];

    internal List<PrcFilter> Filters { get; } = [];
}
