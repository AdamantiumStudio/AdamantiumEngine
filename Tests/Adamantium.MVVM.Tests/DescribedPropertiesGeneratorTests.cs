using System;
using System.IO;
using System.Linq;
using Adamantium.MVVM.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Adamantium.MVVM.Tests;

// The descriptions are written into the user's own compilation, so whatever the user may write in a described class
// must come out as code that compiles without a warning - or be left to reflection.
[TestFixture]
public class DescribedPropertiesGeneratorTests
{
    [Test]
    public void AnObsoleteProperty_IsDescribed_WithoutAWarningInTheGeneratedCode()
    {
        var (generated, diagnostics) = Run(@"
namespace Demo
{
    [System.Obsolete(""old"")]
    public sealed class Legacy {}

    public sealed class Vm : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        [System.Obsolete(""old"")] public int Old { get; set; }
        public Legacy Kind { get; set; }
    }
}");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(generated, Does.Contain("GetOld").And.Contain("GetKind"));
        });
    }

    [Test]
    public void AUseThatIsAnError_IsLeftToReflection()
    {
        var (generated, diagnostics) = Run(@"
namespace Demo
{
    [System.Obsolete(""gone"", true)]
    public sealed class Gone {}

    [System.Diagnostics.CodeAnalysis.Experimental(""DEMO001"")]
    public sealed class Trial {}

    public sealed class Vm : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        [System.Obsolete(""gone"", true)] public int Removed { get; set; }
        [System.Diagnostics.CodeAnalysis.Experimental(""DEMO001"")] public int Tried { get; set; }
#pragma warning disable DEMO001
        public System.Collections.Generic.List<Trial> Trials { get; set; }
#pragma warning restore DEMO001
        public unsafe int*[] Pointers { get; set; }
        public int Kept { get; set; }
    }

    [System.Obsolete(""gone"", true)]
    public sealed class GoneVm : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        public int Value { get; set; }
    }
}");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(generated, Does.Contain("GetKept"));
            Assert.That(generated, Does.Not.Contain("Removed").And.Not.Contain("Tried").And.Not.Contain("Trials")
                .And.Not.Contain("Pointers").And.Not.Contain("GoneVm"));
        });
    }

    [Test]
    public void AGeneratedMemberWhoseUseIsAnError_IsLeftToReflection()
    {
        var (generated, diagnostics) = Run(@"
namespace Demo
{
    [Adamantium.MVVM.ViewModel]
    public partial class Vm
    {
        [Adamantium.MVVM.Bindable, System.Obsolete(""gone"", true)] public partial int Removed { get; set; }
        [Adamantium.MVVM.Bindable, System.Diagnostics.CodeAnalysis.Experimental(""DEMO002"")] public partial int Tried { get; set; }
        [Adamantium.MVVM.Bindable] private int _kept;
        [Adamantium.MVVM.Command, System.Obsolete(""gone"", true)] private void Old() { }
    }

    [Adamantium.MVVM.ViewModel, System.Obsolete(""gone"", true)]
    public partial class GoneVm
    {
        [Adamantium.MVVM.Bindable] private int _value;
        [Adamantium.MVVM.Command] private void Run() { }
    }
}");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(generated, Does.Contain("GetKept"));
            Assert.That(generated, Does.Not.Contain("Removed").And.Not.Contain("Tried").And.Not.Contain("OldCommand")
                .And.Not.Contain("GoneVm"));
        });
    }

    [TestCase(false, TestName = "WhatWarnsWithItsOwnId_OrIsUnsafeToTrim_IsLeftToReflection_ProjectReference")]
    [TestCase(true, TestName = "WhatWarnsWithItsOwnId_OrIsUnsafeToTrim_IsLeftToReflection_AssemblyReference")]
    public void WhatWarnsWithItsOwnId_OrIsUnsafeToTrim_IsLeftToReflection(bool built)
    {
        var compiled = CSharpCompilation.Create("TrialLibrary",
            [CSharpSyntaxTree.ParseText(@"
[assembly: System.Diagnostics.CodeAnalysis.Experimental(""DEMO003"")]
namespace Trial { public sealed class Thing {} }")],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var image = new MemoryStream();
        compiled.Emit(image);
        MetadataReference library = built ? MetadataReference.CreateFromImage(image.ToArray()) : compiled.ToMetadataReference();

        var (generated, diagnostics) = Run(@"
namespace Demo
{
    [System.Obsolete(""old"", DiagnosticId = ""DEMO004"")]
    public sealed class Legacy {}

    public sealed class Vm : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
#pragma warning disable DEMO003, DEMO004, DEMO005
        [System.Obsolete(""old"", DiagnosticId = ""DEMO005"")] public int Marked { get; set; }
        public Legacy Kind { get; set; }
        public Trial.Thing Thing { get; set; }
#pragma warning restore DEMO003, DEMO004, DEMO005
        public int Accessor { [System.Diagnostics.CodeAnalysis.Experimental(""DEMO006"")] get; set; }
        public int Trimmed { [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(""reflects"")] get; set; }
        public int Kept { get; set; }
    }
}", library);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(generated, Does.Contain("GetKept").And.Contain("SetAccessor").And.Contain("SetTrimmed"));
            Assert.That(generated, Does.Not.Contain("Marked").And.Not.Contain("Kind").And.Not.Contain("Thing")
                .And.Not.Contain("GetAccessor").And.Not.Contain("GetTrimmed"));
        });
    }

    [Test]
    public void TypesWhoseNamesDifferInCaseOnly_AreBothDescribed()
    {
        var (generated, diagnostics) = Run(@"
namespace Demo
{
    public sealed class Foo : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        public int Upper { get; set; }
    }

    public sealed class foo : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        public int Lower { get; set; }
    }
}");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(generated, Does.Contain("GetUpper").And.Contain("GetLower"));
        });
    }

    [Test]
    public void ViewModelsWhoseNamesDifferInCaseOnly_BothGetTheirMembers()
    {
        var (generated, diagnostics) = Run(@"
namespace Demo
{
    [Adamantium.MVVM.ViewModel]
    public partial class Foo
    {
        [Adamantium.MVVM.Bindable] private int _x;
        [Adamantium.MVVM.Command] private void Run() { }
    }

    [Adamantium.MVVM.ViewModel]
    public partial class foo
    {
        [Adamantium.MVVM.Bindable] private int _x;
        [Adamantium.MVVM.Command] private void Run() { }
    }
}");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(generated, Does.Contain("Demo.Foo)").And.Contain("Demo.foo)"));
        });
    }

    [Test]
    public void EveryPartOfAType_AndARecordsPositionalProperties_AreDescribed()
    {
        var (generated, diagnostics) = Run(@"
namespace Demo
{
    public partial class Split : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        public int First { get; set; }
    }
}", @"
namespace Demo
{
    public partial class Split
    {
        public int Second { get; set; }
        public int Hidden { private get; set; }
        public int Computed => First + Second;
        public static int Shared { get; set; }
    }
}", @"
namespace Demo
{
    [System.ComponentModel.Description(""a record"")]
    public record Point(int X, int Y) : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        public int X { get; } = X;
    }
}");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(generated, Does.Contain("GetFirst").And.Contain("GetSecond").And.Contain("SetHidden")
                .And.Contain("GetComputed").And.Contain("InitY").And.Contain("GetX"));
            Assert.That(generated, Does.Not.Contain("GetHidden").And.Not.Contain("Shared").And.Not.Contain("SetComputed")
                .And.Not.Contain("SetX").And.Not.Contain("InitX"));
        });
    }

    [Test]
    public void APositionalParameter_IsDescribed_OnlyWhereTheCompilerMadeItsProperty()
    {
        var (generated, diagnostics) = Run(@"
namespace Demo
{
    public abstract record Notifying : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    public record Base(int A) : Notifying;

    public record Derived(int A, int B) : Base(A);

    public partial record Split(int C) : Notifying;

    public partial record Split
    {
        public int C { get; } = C;
    }

    public record Field(int D) : Notifying
    {
        public int D = D;
    }

    public partial record Hidden(int E) : Notifying;

    public partial record Hidden
    {
        private int E { get; init; } = E;
    }

    public record Guarded : Notifying
    {
        protected int F { get; init; }
    }

    public record Heir(int F) : Guarded;
}");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(generated, Does.Contain("Demo.Base)o).A").And.Contain("Demo.Derived)o).B").And.Contain("GetC"));
            Assert.That(generated, Does.Not.Contain("Demo.Derived)o).A").And.Not.Contain("SetC").And.Not.Contain("Demo.Field)o).D")
                .And.Not.Contain("Demo.Hidden)o).E").And.Not.Contain("Demo.Heir)o).F"));
        });
    }

    [Test]
    public void APropertyNamedLikeTheGeneratedClass_DoesNotCollideWithIt()
    {
        var (generated, diagnostics) = Run(@"
namespace Demo
{
    public sealed class GetX : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        public int XProperties { get; set; }
    }
}");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(generated, Does.Contain("GetXProperties("));
        });
    }

    private static MetadataReference[] References()
    {
        var mvvm = typeof(PropertyAccessors).Assembly.Location;
        return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0 && !string.Equals(Path.GetFileName(p), Path.GetFileName(mvvm), StringComparison.OrdinalIgnoreCase))
            .Append(mvvm)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray();
    }

    private static (string Generated, Diagnostic[] Diagnostics) Run(params string[] sources) => Run(sources, []);

    private static (string Generated, Diagnostic[] Diagnostics) Run(string source, params MetadataReference[] more) =>
        Run([source], more);

    private static (string Generated, Diagnostic[] Diagnostics) Run(string[] sources, MetadataReference[] more)
    {
        var compilation = CSharpCompilation.Create("DescribedProbe",
            sources.Select(s => CSharpSyntaxTree.ParseText(s)),
            [.. References(), .. more],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MvvmGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var generatorDiagnostics);

        var generated = string.Join("\n", driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .Where(s => s.HintName.EndsWith(".Properties.g.cs"))
            .Select(s => s.SourceText.ToString()));
        var diagnostics = generatorDiagnostics
            .Concat(updated.GetDiagnostics().Where(d => d.Location.SourceTree?.FilePath.EndsWith(".Properties.g.cs") == true))
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .ToArray();

        return (generated, diagnostics);
    }
}
