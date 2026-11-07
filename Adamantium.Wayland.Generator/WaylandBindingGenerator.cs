using System.Runtime.CompilerServices;
using Adamantium.Wayland.Generator.CodeGeneration;
using Adamantium.Wayland.Generator.Parser;
using Adamantium.Wayland.Generator.Processors;
using QuantumBinding.Generator;
using QuantumBinding.Generator.CodeGeneration;
using QuantumBinding.Generator.Processors;

namespace Adamantium.Wayland.Generator;

/// <summary>Generates Adamantium.Wayland/Generated from the protocol XML files in Protocols/: a file per protocol and
/// WaylandInterfaces.cs.</summary>
public sealed class WaylandBindingGenerator : QuantumBindingGenerator
{
    private Module module;
    private WaylandXmlParser parser;

    public override void OnSetup(BindingOptions options)
    {
        var source = SourceDirectory();
        parser = new WaylandXmlParser();

        module = new Module("wayland-client")
        {
            Name = "Wayland",
            MetadataProvider = parser,
            GeneratorSpecializations = GeneratorSpecializations.None,
            CleanPreviousGeneration = true,
            OutputPath = Path.GetFullPath(Path.Combine(source, "..", "Adamantium.Wayland", "Generated")),
            OutputFileName = "Adamantium.Wayland",
            OutputNamespace = "Adamantium.Wayland",
            InteropSubNamespace = "Interop",
            MethodClassName = "WaylandNative",
            InteropClassName = "WaylandInterop",
        };

        module.Files.AddRange(Directory.GetFiles(Path.Combine(source, "Protocols"), "*.xml").Order(StringComparer.Ordinal));
        if (module.Files.Count == 0)
        {
            throw new InvalidDataException($"No protocol XML in {Path.Combine(source, "Protocols")}");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { WaylandInterfacesGenerator.FileName };
        foreach (var file in module.Files)
        {
            if (!names.Add(Naming.FileName(file)))
            {
                throw new InvalidDataException($"{Path.GetFileName(file)} is written to {Naming.FileName(file)}, as another file is");
            }
        }

        options.AddModule(module);
    }

    /// <summary>The folder the files are written to.</summary>
    public string OutputPath => module.OutputPath;

    public override void OnSetupPostProcessing(ProcessingContext context)
    {
        foreach (var file in module.Files)
        {
            context.AddCodeGenerationPass(new WaylandProtocolPass(parser, file), ExecutionPassKind.Once, module);
        }

        context.AddCodeGenerationPass(new WaylandInterfacesPass(parser), ExecutionPassKind.Once, module);
    }

    /// <summary>The protocols read and their interfaces, once <see cref="QuantumBindingGenerator.Run"/> is done.</summary>
    public IReadOnlyList<Protocol> Protocols => parser.Protocols;

    private static string SourceDirectory([CallerFilePath] string path = "")
    {
        return Path.GetDirectoryName(path);
    }
}
