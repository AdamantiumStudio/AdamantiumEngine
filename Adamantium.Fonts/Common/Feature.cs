using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.Fonts;
using Adamantium.Fonts.Tables.Layout;

namespace Adamantium.Fonts.Common;

/// <summary>An OpenType feature a font offers: its tag and registered name, the table it lives in, and its parameters.</summary>
public sealed class Feature
{
    private readonly List<ushort> lookups = [];

    internal Feature(FeatureInfo info, FeatureKind kind, FeatureParametersTable parameters, Func<ushort, string> names)
    {
        Info = info;
        Kind = kind;
        FeatureParameters = parameters;
        if (parameters == null)
        {
            return;
        }

        Label = Named(names, parameters.FeatUiLabelNameId);
        Tooltip = Named(names, parameters.FeatUiTooltipTextNameId);
        SampleText = Named(names, parameters.SampleTextNameId);
        ParameterLabels = Enumerable.Range(0, parameters.NumNamedParameters)
            .Select(i => Named(names, (ushort)(parameters.FirstParamUiLabelNameId + i)) ?? string.Empty)
            .ToArray();
        Characters = parameters.Character?.Select(c => (int)c).ToArray() ?? [];
    }

    public FeatureInfo Info { get; }

    /// <summary>Substitution (GSUB) or positioning (GPOS).</summary>
    public FeatureKind Kind { get; }

    /// <summary>Names and characters of a stylistic set or a character variant; null when the font gives none.</summary>
    public FeatureParametersTable FeatureParameters { get; }

    /// <summary>What to call the feature: the font's own label for a stylistic set or a character variant, otherwise
    /// the registered name.</summary>
    public string Name => Label ?? Info.FriendlyName;

    /// <summary>The font's own label for a stylistic set or a character variant; null when it gives none.</summary>
    public string Label { get; }

    /// <summary>A character variant's tooltip text; null when the font gives none.</summary>
    public string Tooltip { get; }

    /// <summary>Text that shows a character variant's effect; null when the font gives none.</summary>
    public string SampleText { get; }

    /// <summary>The labels of a character variant's alternates, the first for value 1.</summary>
    public IReadOnlyList<string> ParameterLabels { get; } = [];

    /// <summary>The characters a character variant has alternates for.</summary>
    public IReadOnlyList<int> Characters { get; } = [];

    /// <summary>How many values beyond off the feature takes: 1 for one that is on or off, the most alternates any of
    /// its glyphs has for one that offers a choice, as <c>salt</c> or <c>aalt</c>.</summary>
    public int ValueCount { get; internal set; } = 1;

    /// <summary>The lookups the feature applies, across every script and language that lists it.</summary>
    internal IReadOnlyList<ushort> Lookups => lookups;

    internal void AddLookups(IEnumerable<ushort> indices)
    {
        foreach (var index in indices)
        {
            if (!lookups.Contains(index))
            {
                lookups.Add(index);
            }
        }
    }

    public override string ToString()
    {
        return $"{Info.Tag} ({Kind}): {Name}";
    }

    private static string Named(Func<ushort, string> names, ushort nameId) =>
        nameId == 0 || nameId == 0xFFFF ? null : names(nameId);
}
