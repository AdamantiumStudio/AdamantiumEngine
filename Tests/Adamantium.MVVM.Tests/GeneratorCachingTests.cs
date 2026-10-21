using System;
using System.IO;
using System.Linq;
using Adamantium.MVVM.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Adamantium.MVVM.Tests;

/// <summary>
/// The performance guarantee (user's #1 requirement): an edit that doesn't touch any MVVM model must NOT re-run
/// codegen. We run the generator, then again after a no-op change (an unrelated added tree), and assert every
/// SourceOutput step was served from cache. If this ever goes red, the generator would re-generate on every
/// keystroke and could hang the IDE at scale.
/// </summary>
[TestFixture]
public class GeneratorCachingTests
{
    private const string Source = @"
namespace Adamantium.MVVM
{
    [System.AttributeUsage(System.AttributeTargets.Field)] public sealed class BindableAttribute : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class ViewModelAttribute : System.Attribute {}
    public static class PropertyAccessors { public static void Register(System.Type type, System.Func<PropertyAccessor[]> declared) {} }
    public sealed class PropertyAccessor
    {
        public PropertyAccessor(string name, System.Type valueType, System.Func<object, object> get, System.Action<object, object> set) {}
    }
}
namespace Demo
{
    [Adamantium.MVVM.ViewModel]
    public partial class SampleVm
    {
        [Adamantium.MVVM.Bindable] private int _count;
        [Adamantium.MVVM.Bindable] private string _name;
    }

    public class HandWrittenVm : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        public int Size { get; set; }
        private int Twice() => Size * 2;
    }
}";

    [Test]
    public void NoOpEdit_LeavesGeneratorOutputsCached()
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray();

        var c1 = CSharpCompilation.Create("CacheProbe",
            new[] { CSharpSyntaxTree.ParseText(Source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            new[] { new MvvmGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = (CSharpGeneratorDriver)driver.RunGenerators(c1);
        Assert.That(driver.GetRunResult().Results[0].GeneratedSources.Length, Is.GreaterThan(0),
            "generator produced nothing on the first run — the cache assertion would be meaningless");
        Assert.That(driver.GetRunResult().Results[0].GeneratedSources.Select(s => s.HintName),
            Has.Some.EndsWith(".Properties.g.cs"), "the descriptions are part of what must stay cached");

        // No-op edit: changes the compilation but none of the MVVM models.
        var c2 = c1.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace Other { class Unrelated { } }"));
        driver = (CSharpGeneratorDriver)driver.RunGenerators(c2);
        AssertCached(driver, "an unrelated tree");

        // A keystroke inside a described class: its part is scanned again, its description is the same.
        var tree = c2.SyntaxTrees.First();
        var text = tree.GetText();
        var at = text.ToString().IndexOf("Size * 2", StringComparison.Ordinal);
        var c3 = c2.ReplaceSyntaxTree(tree, tree.WithChangedText(text.Replace(at, 8, "Size * 3")));
        driver = (CSharpGeneratorDriver)driver.RunGenerators(c3);
        AssertCached(driver, "an edit inside a method body");
    }

    private static void AssertCached(GeneratorDriver driver, string edit)
    {
        var outputs = driver.GetRunResult().Results[0].TrackedSteps
            .Where(step => step.Key.Contains("SourceOutput"))
            .SelectMany(step => step.Value)
            .SelectMany(run => run.Outputs)
            .ToArray();

        Assert.That(outputs, Is.Not.Empty, "no SourceOutput steps were tracked");
        Assert.That(
            outputs.All(o => o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged),
            $"{edit} re-ran codegen (cache miss): " + string.Join(", ", outputs.Select(o => o.Reason)));
    }
}
