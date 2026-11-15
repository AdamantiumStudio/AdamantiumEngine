using System.Collections.Generic;

namespace Adamantium.Content.Pipeline;

/// <summary>
/// Turns a raw source asset file into an engine-neutral intermediate object model
/// (e.g. a model file into <c>SceneData</c>).
/// </summary>
public interface IContentImporter
{
    /// <summary>The settings the importer reads from <see cref="ContentBuildContext.Parameters"/>.</summary>
    IReadOnlyList<ImportSetting> Settings => [];

    /// <summary>Which make of the importer this is: raised whenever what it makes of the same source changes, so an
    /// asset file an earlier one made is imported again.</summary>
    int Revision => 1;

    object Import(string sourcePath, ContentBuildContext context);
}
