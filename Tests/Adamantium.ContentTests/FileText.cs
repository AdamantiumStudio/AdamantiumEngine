using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Adamantium.ContentTests;

public sealed class FileText : AdditionalText
{
    public FileText(string path)
    {
        Path = path;
    }

    public override string Path { get; }

    public override SourceText GetText(CancellationToken cancellationToken = default)
    {
        return SourceText.From(string.Empty);
    }
}
