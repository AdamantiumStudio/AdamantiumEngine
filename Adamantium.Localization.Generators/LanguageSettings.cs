namespace Adamantium.Localization.Generators;

internal sealed class LanguageSettings : IEquatable<LanguageSettings>
{
    public LanguageSettings(string rootNamespace, string projectDir, string neutralLanguage, string outputType)
    {
        RootNamespace = rootNamespace;
        ProjectDir = projectDir;
        NeutralLanguage = LanguageFileParser.BaseLanguage(neutralLanguage);
        IsApplication = string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase);
    }

    public string RootNamespace { get; }

    public string ProjectDir { get; }

    public string NeutralLanguage { get; }

    public bool IsApplication { get; }

    public bool Equals(LanguageSettings other) =>
        other != null && RootNamespace == other.RootNamespace && ProjectDir == other.ProjectDir &&
        NeutralLanguage == other.NeutralLanguage && IsApplication == other.IsApplication;

    public override bool Equals(object obj) => Equals(obj as LanguageSettings);

    public override int GetHashCode() => (RootNamespace ?? string.Empty).GetHashCode() ^ NeutralLanguage.GetHashCode();
}
