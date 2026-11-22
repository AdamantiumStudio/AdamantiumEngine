namespace Adamantium.Localization;

/// <summary>The languages a generated string table is written in, its base language first. Read at build time by the
/// generator of an application that translates the table.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LanguageTableAttribute : Attribute
{
    public LanguageTableAttribute(params string[] languages)
    {
        Languages = languages;
    }

    public string[] Languages { get; }
}
