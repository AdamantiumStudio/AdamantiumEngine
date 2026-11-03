using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Core;
using Adamantium.EffectsCompiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Adamantium.Engine.Generators;

[Generator]
public class EffectsGenerator : IIncrementalGenerator
{
    private const string EffectTypeName = "Adamantium.Graphics.Core.EffectsFramework.Effect";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var effectFiles = context.AdditionalTextsProvider.Where(file => file.Path.EndsWith(".fx"));
        var includeFiles = context.AdditionalTextsProvider.Where(file => file.Path.EndsWith(".fxh"));

        var fxNamesAndContents = effectFiles.Select((text, cancellationToken) => (
            name: Path.GetFileName(text.Path),
            path: text.Path,
            content: text.GetText(cancellationToken).ToString(),
            fxName: Path.GetFileNameWithoutExtension(text.Path)));

        var includesAndContents = includeFiles.Select((text, cancellationToken) => new ShaderFileInfo()
        {
            FileName = Path.GetFileName(text.Path),
            Path = text.Path,
            Content = text.GetText(cancellationToken).ToString()
        });

        var includesProvider = includesAndContents.Collect();

        // STRINGS, not the whole Compilation: combining with CompilationProvider recompiled every shader on every
        // keystroke, because the compilation changes whenever any C# file does.
        var placement = context.CompilationProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select((pair, _) =>
            {
                pair.Right.GlobalOptions.TryGetValue("build_property.projectdir", out var projectDir);
                pair.Right.GlobalOptions.TryGetValue("build_property.adamantiumslangpath", out var slangPath);
                pair.Right.GlobalOptions.TryGetValue("build_property.adamantiumshaderhotreload", out var hotReload);
                return (AssemblyName: pair.Left.AssemblyName, ProjectDir: projectDir, SlangPath: slangPath,
                    HotReload: string.Equals(hotReload, "true", System.StringComparison.OrdinalIgnoreCase),
                    Inherited: GetInheritedMembers(pair.Left));
            });

        var sourceProvider = fxNamesAndContents
            .Combine(placement)
            .Combine(includesProvider);

        context.RegisterSourceOutput(sourceProvider, (spc, provider) =>
        {
            var ((file, place), includes) = provider;

            // Here, not in Initialize: a project with no .fx never loads a shader compiler. Reported rather than
            // thrown - a generator that fails to initialize is only a warning, and the assembly comes out empty.
            var nativeFailure = NativeLibraryLoader.LoadNativeLibraries(place.SlangPath);

            if (nativeFailure != null)
            {
                CreateDiagnostic(ref spc, file.name,
                    $"The shader compiler could not be loaded, so no effect was generated: {nativeFailure} " +
                    "Slang comes from the Vulkan SDK - check that VULKAN_SDK points at an installed one, and that this " +
                    "process was started after the last SDK change (an IDE keeps the environment it started with).",
                    DiagnosticSeverity.Error);
                return;
            }

            try
            {
                var text = file.content;
                var compilerResult = EffectCompiler.Compile(text, file.name, includes);
                if (compilerResult.HasErrors)
                {
                    CreateDiagnostics(ref spc, compilerResult, file.name, file.path);
                    return;
                }

                var parameterNames = GetParameterNames(compilerResult.EffectData);
                var clash = FindNameClash(compilerResult.EffectData, parameterNames, place.Inherited, file.fxName);
                if (clash != null)
                {
                    CreateDiagnostic(ref spc, file.name, clash, DiagnosticSeverity.Error);
                }
                else
                {
                    if (place.HotReload)
                    {
                        compilerResult.EffectData.Description.Arguments = SourceOf(file.path, includes, compilerResult.Includes);
                    }

                    var @namespace = ComposeNamespace(place.AssemblyName, place.ProjectDir, file.path);
                    var result = GenerateEffect(compilerResult, parameterNames, file.fxName, @namespace);
                    spc.AddSource($"{file.fxName}.g.cs", result);
                }
            }
            catch(System.Exception ex)
            {
                CreateDiagnostic(ref spc, file.name, ex.Message, DiagnosticSeverity.Error);
            }
        });
    }

    private static EffectData.CompilerArguments SourceOf(string effectPath, IEnumerable<ShaderFileInfo> offered,
        IReadOnlyList<string> pulledIn)
    {
        return new EffectData.CompilerArguments
        {
            FilePath = effectPath,
            Macros = [],
            IncludeDirectoryList = offered
                .Select(include => Path.GetDirectoryName(include.Path))
                .Where(directory => !string.IsNullOrEmpty(directory))
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Includes = pulledIn.ToList()
        };
    }

    /// <summary>The assembly, then the folders the .fx sits in - the rule a .cs file already follows. The class name
    /// is the file name, so a flat namespace could not hold two effects named alike.</summary>
    private static string ComposeNamespace(string assemblyName, string projectDir, string effectPath)
    {
        if (string.IsNullOrEmpty(projectDir)) return assemblyName;

        var directory = Path.GetDirectoryName(effectPath) ?? string.Empty;
        var relative = directory.StartsWith(projectDir, System.StringComparison.OrdinalIgnoreCase)
            ? directory.Substring(projectDir.Length)
            : string.Empty;

        var folders = relative
            .Split(new[] { '\\', '/' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(Sanitize)
            .Where(part => part.Length > 0);

        return string.Join(".", new[] { assemblyName }.Concat(folders));
    }

    // A folder may be named anything the file system allows; an identifier may not.
    private static string Sanitize(string folder)
    {
        var cleaned = new string(folder.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        return cleaned.Length > 0 && char.IsDigit(cleaned[0]) ? "_" + cleaned : cleaned;
    }

    private string GenerateEffect(EffectCompilerResult result, List<string> parameterNames, string fxName, string @namespace)
    {
        var textGenerator = new TextGenerator();
        textGenerator.WriteLine("using Adamantium.EffectsCompiler;");
        textGenerator.WriteLine("using Adamantium.Graphics.Core;");
        textGenerator.WriteLine("using Adamantium.Graphics.Core.EffectsFramework;");

        textGenerator.NewLine();

        textGenerator.WriteLine($"namespace {@namespace}");
        textGenerator.WriteOpenBraceAndIndent();
        textGenerator.WriteLine($"public partial class {fxName} : Effect");
        textGenerator.WriteOpenBraceAndIndent();

        var bytecodeStream = new MemoryStream();
        result.EffectData.Save(bytecodeStream);
        // The EffectData is already MessagePack+LZ4-compressed. Emit it as a Base64 string literal, NOT a decimal byte[]
        // literal: Base64 is ~1.33 source chars/byte vs ~4-5 for each "123, ", so the generated file shrinks ~3-4x (and
        // ToArray() gives exactly the written Length - GetBuffer() returned the whole zero-padded capacity).
        string base64 = System.Convert.ToBase64String(bytecodeStream.ToArray());
        textGenerator.WriteLine(@$"private static readonly EffectData bytecode = EffectData.Load(System.Convert.FromBase64String(""{base64}""));");

        textGenerator.NewLine();

        textGenerator.WriteLine($"public {fxName}(IGraphicsDevice device) : this(device, bytecode)");
        textGenerator.WriteOpenBraceAndIndent();
        textGenerator.UnindentAndWriteCloseBrace();

        textGenerator.NewLine();
        textGenerator.WriteLine($"private {fxName}(IGraphicsDevice device, EffectData data) : base(device, data)");
        textGenerator.WriteOpenBraceAndIndent();

        var techniques = result.EffectData.Description.Techniques;
        var unnamedTechniques = 0;
        foreach (var technique in techniques)
        {
            var techniqueName = GetNameOrIndex(technique.Name, ref unnamedTechniques);
            var unnamedPasses = 0;
            for (var p = 0; p < technique.Passes.Count; p++)
            {
                var passName = GetNameOrIndex(technique.Passes[p].Name, ref unnamedPasses);
                textGenerator.WriteLine(
                    $"{ToPassProperty(technique, p)} = Techniques[\"{techniqueName}\"].Passes[\"{passName}\"];");
            }
        }

        foreach (var name in parameterNames)
        {
            textGenerator.WriteLine($"{ToProperty(name)} = Parameters[\"{name}\"];");
        }

        textGenerator.UnindentAndWriteCloseBrace();

        textGenerator.NewLine();
        textGenerator.WriteLine($"public override {fxName} Clone()");
        textGenerator.WriteOpenBraceAndIndent();
        textGenerator.WriteLine($"var effect = new {fxName}(GraphicsDevice, Data);");
        textGenerator.WriteLine("effect.CopyValuesFrom(this);");
        textGenerator.WriteLine("return effect;");
        textGenerator.UnindentAndWriteCloseBrace();

        textGenerator.NewLine();

        foreach (var technique in techniques)
        {
            for (var p = 0; p < technique.Passes.Count; p++)
            {
                textGenerator.WriteLine($"public IEffectPass {ToPassProperty(technique, p)} {{get;}}");
            }
        }

        textGenerator.NewLine();

        foreach (var name in parameterNames)
        {
            textGenerator.WriteLine($"public EffectParameter {ToProperty(name)} {{get;}}");
        }

        textGenerator.UnindentAndWriteCloseBrace(); // close class
        textGenerator.UnindentAndWriteCloseBrace(); // close namespace

        return textGenerator.ToString();
    }

    private static List<string> GetParameterNames(EffectData data)
    {
        var names = new List<string>();
        foreach (var shader in data.Shaders)
        {
            foreach (var buffer in shader.ConstantBuffers)
            {
                foreach (var parameter in buffer.Parameters)
                {
                    AddOnce(names, parameter.Name);
                }
            }

            foreach (var resource in shader.ResourceParameters)
            {
                AddOnce(names, resource.Name);
            }
        }

        return names;
    }

    private static void AddOnce(List<string> names, string name)
    {
        if (!names.Contains(name))
        {
            names.Add(name);
        }
    }

    private static string FindNameClash(EffectData data, List<string> parameterNames, string inherited, string className)
    {
        var members = new Dictionary<string, string> { [className] = $"the class {className}" };
        foreach (var name in inherited.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            members[name] = $"the inherited member {name}";
        }

        foreach (var technique in data.Description.Techniques)
        {
            for (var p = 0; p < technique.Passes.Count; p++)
            {
                var clash = AddMember(members, ToPassProperty(technique, p),
                    $"technique '{technique.Name}' pass '{technique.Passes[p].Name}'");
                if (clash != null) return clash;
            }
        }

        foreach (var name in parameterNames)
        {
            var clash = AddMember(members, ToProperty(name), $"parameter '{name}'");
            if (clash != null) return clash;
        }

        return null;
    }

    private static string AddMember(Dictionary<string, string> members, string property, string from)
    {
        if (members.TryGetValue(property, out var taken))
        {
            return $"{taken} and {from} would be one property, {property}, in the generated class; rename one.";
        }

        members.Add(property, from);
        return null;
    }

    private static string GetInheritedMembers(Compilation compilation)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        for (var type = compilation.GetTypeByMetadataName(EffectTypeName); type != null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers())
            {
                if (member.CanBeReferencedByName && (member.DeclaredAccessibility is Accessibility.Public
                        or Accessibility.Protected or Accessibility.ProtectedOrInternal))
                {
                    names.Add(member.Name);
                }
            }
        }

        return string.Join(",", names);
    }

    private static string GetNameOrIndex(string name, ref int unnamed) =>
        string.IsNullOrEmpty(name) ? $"${unnamed++}" : name;

    private static string ToPassProperty(EffectData.Technique technique, int pass) =>
        ToProperty($"{technique.Name}{technique.Passes[pass].Name}Pass");

    private static string ToProperty(string name) => char.ToUpper(name[0]) + name.Substring(1);

    private static void CreateDiagnostics(ref SourceProductionContext spc, EffectCompilerResult result, string fileName,
        string filePath)
    {
        foreach (var message in result.Logger.Messages)
        {
            var descriptor = new DiagnosticDescriptor(
                id: "EffectGenerator",
                title: "Effect compiler",
                messageFormat: "{0}",
                category: "EffectCompiler",
                (DiagnosticSeverity)message.Type,
                isEnabledByDefault: true);
            spc.ReportDiagnostic(Diagnostic.Create(descriptor, GetLocation(message, fileName, filePath), message.Text));
        }
    }

    private static Location GetLocation(LogMessage message, string fileName, string filePath)
    {
        if (message is not EffectCompilerMessage placed || placed.Location.Line < 1)
        {
            return Location.Create(filePath, default, default);
        }

        var path = placed.Location.FilePath == fileName ? filePath : placed.Location.FilePath;
        var position = new LinePosition(placed.Location.Line - 1, Math.Max(0, placed.Location.Column - 1));
        return Location.Create(path, default, new LinePositionSpan(position, position));
    }

    private void CreateDiagnostic(ref SourceProductionContext spc, string filePath, string diagnosticText, DiagnosticSeverity severity)
    {
        var descriptor = new DiagnosticDescriptor(
            id: "EffectGenerator",
            title: "Effects parsing error",
            messageFormat: "{0}: {1}",
            category: "EffectParser",
            severity,
            isEnabledByDefault: true);
        var diagnostic = Diagnostic.Create(descriptor, Location.None, filePath, diagnosticText);
        spc.ReportDiagnostic(diagnostic);

    }
}