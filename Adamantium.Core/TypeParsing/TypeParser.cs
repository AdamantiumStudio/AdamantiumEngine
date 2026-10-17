using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Adamantium.Core.TypeParsing;

public static class TypeParser
{
    public static TTarget Parse<TTarget>(string value) => (TTarget)Parse(value, typeof(TTarget));

    // Cached per target type: reflecting on every call froze virtualized lists that re-parse per realized item.
    private static readonly ConcurrentDictionary<Type, Func<string, object>> _parsers = new();

    /// <summary>
    /// Non-generic counterpart of <see cref="Parse{TTarget}"/>: parses <paramref name="value"/> into
    /// <paramref name="targetType"/> using its registered parser (ParserRegistry) or its <c>[TypeParser]</c>
    /// attribute. Lets callers that only have a runtime <see cref="Type"/> (e.g. value-to-property coercion) use
    /// the same conversion a compiled build does.
    /// </summary>
    public static object Parse(string value, Type targetType)
    {
        // A Nullable<T> property (e.g. ToggleButton.IsChecked = bool?) has no parser of its own: an empty / "null"
        // value is the null state, otherwise parse the underlying T - the boxed result unboxes straight into the
        // nullable (so IsChecked="True" in markup works instead of throwing "parser not found for Nullable<Boolean>").
        var underlying = Nullable.GetUnderlyingType(targetType);
        if (underlying != null)
        {
            if (string.IsNullOrEmpty(value) || string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
                return null;
            targetType = underlying;
        }

        return _parsers.GetOrAdd(targetType, BuildParser)(value);
    }

    // Resolve the parser for a type ONCE (attribute reflection + Activator.CreateInstance + GetMethod happen here, cached
    // by the caller); the returned delegate then just invokes on each parse.
    private static Func<string, object> BuildParser(Type targetType)
    {
        var parserType = ParserRegistry.GetParserFor(targetType)
                         ?? targetType.GetCustomAttribute<TypeParserAttribute>()?.ParserType;

        if (parserType == null)
        {
            // Primitives (bool/int/double/enum...) are emitted inline by codegen and normally never reach here - EXCEPT
            // as the underlying T of a Nullable<T> (e.g. IsChecked="True" -> bool?, which has no parser of its own).
            // Fall back to the framework converter for those instead of failing.
            if (targetType.IsEnum)
                return v => Enum.Parse(targetType, v, ignoreCase: true);
            if (typeof(IConvertible).IsAssignableFrom(targetType))
                return v => Convert.ChangeType(v, targetType, System.Globalization.CultureInfo.InvariantCulture);

            var ownParse = targetType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, null,
                [typeof(string)], null);
            if (ownParse != null && ownParse.ReturnType == targetType)
            {
                return value =>
                {
                    try
                    {
                        return ownParse.Invoke(null, [value]);
                    }
                    catch (TargetInvocationException e)
                    {
                        throw new InvalidOperationException($"Cannot parse {value} as input for {targetType.FullName}",
                            e.InnerException);
                    }
                };
            }

            return _ => throw new InvalidOperationException($"Type parser not found for {targetType.FullName}");
        }

        var parser = Activator.CreateInstance(parserType);
        var parse = parserType.GetMethod(nameof(ITypeParser<object>.Parse), [typeof(string)])
                    ?? throw new InvalidOperationException($"{parserType.FullName} has no Parse(string) method");

        // A direct delegate avoids per-call reflection and allocation on the hot parse path; a value-type return cannot
        // bind covariantly and falls back to Invoke. Asked without throwing: a debugger stops on every one it sees.
        if (Delegate.CreateDelegate(typeof(Func<string, object>), parser, parse, throwOnBindFailure: false)
            is Func<string, object> direct)
        {
            return direct;
        }

        return value =>
        {
            try
            {
                return parse.Invoke(parser, [value]);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"Cannot parse {value} as input for {targetType.FullName}",
                    e is TargetInvocationException tie ? tie.InnerException : e);
            }
        };
    }
}