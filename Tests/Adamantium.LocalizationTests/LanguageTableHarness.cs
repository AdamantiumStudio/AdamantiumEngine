using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Adamantium.Localization;
using Adamantium.Localization.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Adamantium.LocalizationTests;

/// <summary>Drives the real <see cref="LanguageTableGenerator"/> over language files in memory and compiles what it makes,
/// as a project of its own.</summary>
internal static class LanguageTableHarness
{
    /// <summary>The project folder the generator is told of, as the build machine's OS writes it.</summary>
    public static readonly string Root = OperatingSystem.IsWindows() ? @"C:\Test\" : "/Test/";

    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(() =>
    {
        _ = typeof(PluralRules);   // what a generated table derives from and calls
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();
        if (AppDomain.CurrentDomain.GetAssemblies().All(a => a.GetName().Name != "System.Runtime"))
        {
            references.Add(MetadataReference.CreateFromFile(
                Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "System.Runtime.dll")));
        }

        return references;
    });

    public static string Errors(IReadOnlyList<Diagnostic> errors) =>
        "generator reported errors: " + string.Join(" | ", errors.Select(e => e.GetMessage()));

    /// <summary>Runs the generator over <paramref name="files"/> (paths relative to the project) with extra build
    /// properties, and compiles the result against <paramref name="references"/> as well.</summary>
    public static GeneratedProject Project(IReadOnlyDictionary<string, string> files,
        IReadOnlyDictionary<string, string> properties = null, IEnumerable<MetadataReference> references = null)
    {
        var options = new Dictionary<string, string>
        {
            ["build_property.RootNamespace"] = "Test.App",
            ["build_property.projectdir"] = Root,
        };
        foreach (var property in properties ?? new Dictionary<string, string>())
        {
            options["build_property." + property.Key] = property.Value;
        }

        var driver = CSharpGeneratorDriver.Create(
            generators: [new LanguageTableGenerator().AsSourceGenerator()],
            additionalTexts: files.Select(f => (AdditionalText)new InMemoryAdditionalText(PathOf(f.Key), f.Value)),
            parseOptions: null,
            optionsProvider: new OptionsProvider(options));

        var compilation = CSharpCompilation.Create("Probe" + Guid.NewGuid().ToString("N"),
            syntaxTrees: null,
            references: References.Value.Concat(references ?? []),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return new GeneratedProject((CSharpCompilation)output, diagnostics);
    }

    private static string PathOf(string relative) => Root + relative.Replace('/', Path.DirectorySeparatorChar);

    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        private readonly SourceText _text = SourceText.From(content);
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }

    private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value);
    }

    private sealed class OptionsProvider(Dictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _global = new Options(values);
        public override AnalyzerConfigOptions GlobalOptions => _global;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _global;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _global;
    }
}

/// <summary>A project the harness generated and compiled: what the generator said, and the assembly to load.</summary>
internal sealed class GeneratedProject
{
    private static readonly Dictionary<string, Assembly> Loaded = new();
    private byte[] _image;
    private Assembly _assembly;

    static GeneratedProject()
    {
        // Each image loads into a context of its own, so one generated assembly referencing another finds it here.
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            lock (Loaded)
            {
                return Loaded.TryGetValue(new AssemblyName(args.Name).Name ?? string.Empty, out var assembly) ? assembly : null;
            }
        };
    }

    public GeneratedProject(CSharpCompilation compilation, IReadOnlyList<Diagnostic> generatorDiagnostics)
    {
        Compilation = compilation;
        GeneratorDiagnostics = generatorDiagnostics;
    }

    public CSharpCompilation Compilation { get; }

    public IReadOnlyList<Diagnostic> GeneratorDiagnostics { get; }

    public IReadOnlyList<Diagnostic> Errors =>
        GeneratorDiagnostics.Concat(Compilation.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

    public IReadOnlyList<Diagnostic> Warnings =>
        GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();

    public string Source => string.Join("\n\n", Compilation.SyntaxTrees.Select(t => t.GetText().ToString()));

    public MetadataReference Reference => MetadataReference.CreateFromImage(Image());

    public Assembly Load()
    {
        if (_assembly != null)
        {
            return _assembly;
        }

        _assembly = Assembly.Load(Image());
        lock (Loaded)
        {
            Loaded[Compilation.AssemblyName ?? string.Empty] = _assembly;
        }

        return _assembly;
    }

    private byte[] Image()
    {
        if (_image != null)
        {
            return _image;
        }

        using var stream = new MemoryStream();
        var result = Compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                "the generated project does not compile: " + string.Join(" | ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        _image = stream.ToArray();
        return _image;
    }
}
