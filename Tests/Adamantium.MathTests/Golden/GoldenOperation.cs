using System.Collections.Generic;

namespace Adamantium.MathTests.Golden;

/// <summary>One recorded operation: its key - type, method, parameter types - its place in the file, and its calls.</summary>
public sealed record GoldenOperation(string Key, int Index, IReadOnlyList<GoldenCase> Cases)
{
    /// <inheritdoc />
    public override string ToString() => $"{Key} #{Index}";
}
