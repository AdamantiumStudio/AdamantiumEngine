using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Adamantium.ContentTests;

public sealed class Options : AnalyzerConfigOptions
{
    private readonly Dictionary<string, string> _values;

    public Options(Dictionary<string, string> values)
    {
        _values = values;
    }

    public override bool TryGetValue(string key, [NotNullWhen(true)] out string value)
    {
        return _values.TryGetValue(key, out value);
    }
}
