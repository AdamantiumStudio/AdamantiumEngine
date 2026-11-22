using Microsoft.CodeAnalysis;

namespace Adamantium.Localization.Generators;

/// <summary>Turns language files (<c>Table.language.alang</c>) into string-table classes: a table is built from the files
/// of every language together, not one file at a time.</summary>
[Generator]
public sealed class LanguageTableGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var settings = context.AnalyzerConfigOptionsProvider.Select((options, _) =>
        {
            options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespace);
            options.GlobalOptions.TryGetValue("build_property.projectdir", out var projectDir);
            options.GlobalOptions.TryGetValue("build_property.NeutralLanguage", out var neutralLanguage);
            options.GlobalOptions.TryGetValue("build_property.OutputType", out var outputType);
            return new LanguageSettings(rootNamespace, projectDir, neutralLanguage, outputType);
        });

        var files = context.AdditionalTextsProvider
            .Where(text => text.Path.EndsWith(LanguageFileParser.Extension, StringComparison.OrdinalIgnoreCase))
            .Select((text, cancellationToken) => (text.Path, Content: text.GetText(cancellationToken)?.ToString() ?? string.Empty))
            .Combine(settings)
            .Select((pair, _) => LanguageFileParser.Parse(pair.Left.Path, pair.Left.Content, pair.Right.ProjectDir))
            .Collect();

        context.RegisterSourceOutput(files.Combine(context.CompilationProvider).Combine(settings),
            (output, source) => LanguageTableEmitter.Emit(output, source.Left.Left, source.Left.Right, source.Right));
    }
}
