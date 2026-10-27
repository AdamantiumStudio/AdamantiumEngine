using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.Fonts;
using Adamantium.Fonts.Tables.Layout;

namespace Adamantium.Fonts.Common;

/// <summary>Every OpenType feature a font offers, per script and language system. Read-only: which features apply is
/// a property of the text being shaped, not of the font.</summary>
public sealed class FeatureCatalog
{
    private readonly List<FontScript> _scripts = [];
    private readonly List<Feature> _features = [];

    public IReadOnlyList<FontScript> Scripts => _scripts;

    /// <summary>Each feature once per table, across all scripts and languages.</summary>
    public IReadOnlyList<Feature> Features => _features;

    public IEnumerable<Feature> GSUBFeatures => _features.Where(f => f.Kind == FeatureKind.GSUB);

    public IEnumerable<Feature> GPOSFeatures => _features.Where(f => f.Kind == FeatureKind.GPOS);

    public FontScript GetScript(string tag) => _scripts.FirstOrDefault(s => s.Tag == tag);

    public bool HasFeature(string tag) => _features.Any(f => f.Info.Tag == tag);

    internal FontScript GetOrAddScript(string tag)
    {
        var script = GetScript(tag);
        if (script == null)
        {
            script = new FontScript(tag);
            _scripts.Add(script);
        }

        return script;
    }

    internal Feature GetOrAddFeature(FeatureTable table, FeatureKind kind, Func<ushort, string> names)
    {
        var feature = _features.FirstOrDefault(f => f.Info.Tag == table.Name && f.Kind == kind);
        if (feature == null)
        {
            feature = new Feature(FeatureInfos.GetFeature(table.Name), kind, table.FeatureParameters, names);
            _features.Add(feature);
        }

        feature.AddLookups(table.LookupListIndices ?? []);
        return feature;
    }
}
