using System;
using System.Collections.Generic;
using Adamantium.Core.Collections;

namespace Adamantium.Core.TypeParsing;

public static class ParserRegistry
{
    private static readonly Dictionary<Type, Type> _parsers;

    static ParserRegistry()
    {
        _parsers = new Dictionary<Type, Type>();
        Register(typeof(Uri), typeof(UriParser));
        Register(typeof(TimeSpan), typeof(TimeSpanParser));
        Register(typeof(TrackingCollection<double>), typeof(DoubleCollectionParser));
    }

    public static void Register(Type targetType, Type parserType)
    {
        _parsers[targetType] = parserType;
    }

    public static Type GetParserFor(Type targetType)
    {
        _parsers.TryGetValue(targetType, out var parserType);
        return parserType;
    }
}