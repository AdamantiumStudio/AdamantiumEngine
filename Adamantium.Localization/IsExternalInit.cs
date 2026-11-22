// Polyfill: `init` accessors need this type, which netstandard2.0 doesn't ship.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
