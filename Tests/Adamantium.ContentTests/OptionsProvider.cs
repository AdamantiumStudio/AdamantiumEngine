using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Adamantium.ContentTests;

public sealed class OptionsProvider : AnalyzerConfigOptionsProvider
{
    private static readonly Options None = new(new Dictionary<string, string>());

    private readonly Options _global;
    private readonly HashSet<string> _content;
    private readonly Dictionary<string, string> _links;

    public OptionsProvider(Dictionary<string, string> global, IEnumerable<string> content,
        Dictionary<string, string> links = null)
    {
        _global = new Options(global);
        _content = [..content];
        _links = links ?? new Dictionary<string, string>();
    }

    public override AnalyzerConfigOptions GlobalOptions => _global;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
    {
        return None;
    }

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        if (!_content.Contains(textFile.Path))
        {
            return None;
        }

        var values = new Dictionary<string, string> { ["build_metadata.AdditionalFiles.AdamantiumContent"] = "true" };
        if (_links.TryGetValue(textFile.Path, out var link))
        {
            values["build_metadata.AdditionalFiles.Link"] = link;
        }

        return new Options(values);
    }
}
