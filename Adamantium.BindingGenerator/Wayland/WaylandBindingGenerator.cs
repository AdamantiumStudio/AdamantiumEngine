using System.Runtime.CompilerServices;
using Adamantium.BindingGenerator.Wayland.CodeGeneration;
using Adamantium.BindingGenerator.Wayland.Parser;
using Adamantium.BindingGenerator.Wayland.Processors;
using QuantumBinding.Generator;
using QuantumBinding.Generator.CodeGeneration;
using QuantumBinding.Generator.Processors;

namespace Adamantium.BindingGenerator.Wayland;

/// <summary>Generates Adamantium.Wayland/Generated from the protocol XML files in Protocols/: a file per protocol and
/// WaylandInterfaces.cs.</summary>
public sealed class WaylandBindingGenerator : QuantumBindingGenerator
{
    private Module module;
    private WaylandXmlParser parser;

    public override void OnSetup(BindingOptions options)
    {
        var source = SourceDirectory();
        var protocols = Path.Combine(source, "Protocols");
        parser = new WaylandXmlParser();

        module = new Module("wayland-client")
        {
            Name = "Wayland",
            MetadataProvider = parser,
            GeneratorSpecializations = GeneratorSpecializations.None,
            CleanPreviousGeneration = true,
            OutputPath = Path.GetFullPath(Path.Combine(source, "..", "..", "Adamantium.Wayland", "Generated")),
            OutputFileName = "Adamantium.Wayland",
            OutputNamespace = "Adamantium.Wayland",
            InteropSubNamespace = "Interop",
            MethodClassName = "WaylandNative",
            InteropClassName = "WaylandInterop",
        };

        module.Files.AddRange(Directory.GetFiles(protocols, "*.xml").Order(StringComparer.Ordinal));
        if (module.Files.Count == 0)
        {
            throw new InvalidDataException($"No protocol XML in {protocols}");
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

    public override void OnSetupPostProcessing(ProcessingContext context)
    {
        foreach (var file in module.Files)
        {
            context.AddCodeGenerationPass(new WaylandProtocolPass(parser, file), ExecutionPassKind.Once, module);
        }

        context.AddCodeGenerationPass(new WaylandInterfacesPass(parser), ExecutionPassKind.Once, module);
    }

    private static string SourceDirectory([CallerFilePath] string path = "")
    {
        return Path.GetDirectoryName(path);
    }
}
