using Adamantium.BindingGenerator.Wayland.CodeGeneration;
using Adamantium.BindingGenerator.Wayland.Parser;
using QuantumBinding.Generator.AST;
using QuantumBinding.Generator.CodeGeneration;
using QuantumBinding.Generator.Processors;

namespace Adamantium.BindingGenerator.Wayland.Processors;

/// <summary>Writes WaylandInterfaces.cs over every protocol read.</summary>
public sealed class WaylandInterfacesPass : CodeGeneratorPass
{
    private readonly WaylandXmlParser parser;

    public WaylandInterfacesPass(WaylandXmlParser parser)
    {
        this.parser = parser;
        CodeGeneratorPassKind = ExecutionPassKind.Once;
        GeneratorSpecializations = GeneratorSpecializations.None;
    }

    protected override CodeGenerator OnCreateGenerator(GeneratorCategory category, params TranslationUnit[] units)
    {
        return new WaylandInterfacesGenerator(ProcessingContext, units, parser.Protocols);
    }

    protected override List<CodeGenerator> ProcessPerTypeCodeGeneration(TranslationUnit unit,
        GeneratorSpecializations specializations)
    {
        return [];
    }
}
