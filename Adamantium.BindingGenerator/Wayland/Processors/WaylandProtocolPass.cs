using Adamantium.BindingGenerator.Wayland.CodeGeneration;
using Adamantium.BindingGenerator.Wayland.Parser;
using QuantumBinding.Generator.AST;
using QuantumBinding.Generator.CodeGeneration;
using QuantumBinding.Generator.Processors;

namespace Adamantium.BindingGenerator.Wayland.Processors;

/// <summary>Writes the file of the protocol read from <c>file</c>.</summary>
public sealed class WaylandProtocolPass : CodeGeneratorPass
{
    private readonly WaylandXmlParser parser;
    private readonly string fileName;

    public WaylandProtocolPass(WaylandXmlParser parser, string file)
    {
        this.parser = parser;
        fileName = Path.GetFileName(file);
        CodeGeneratorPassKind = ExecutionPassKind.Once;
        GeneratorSpecializations = GeneratorSpecializations.None;
    }

    protected override CodeGenerator OnCreateGenerator(GeneratorCategory category, params TranslationUnit[] units)
    {
        var protocol = parser.Protocols.Single(p => p.FileName == fileName);
        return new WaylandProtocolGenerator(ProcessingContext, units, parser.Protocols, protocol);
    }

    protected override List<CodeGenerator> ProcessPerTypeCodeGeneration(TranslationUnit unit,
        GeneratorSpecializations specializations)
    {
        return [];
    }
}
