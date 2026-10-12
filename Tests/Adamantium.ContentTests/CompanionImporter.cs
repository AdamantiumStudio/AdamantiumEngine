using System.IO;
using Adamantium.Content.Pipeline;

namespace Adamantium.ContentTests;

/// <summary>Puts beside its artifact the file named on the source's first line, under the name on its second.</summary>
[ContentImporter("Companion", ".companion")]
public sealed class CompanionImporter : IContentImporter
{
    public object Import(string sourcePath, ContentBuildContext context)
    {
        var lines = File.ReadAllLines(sourcePath);
        context.CopyAlongside(lines[0], lines[1]);
        return lines;
    }
}
