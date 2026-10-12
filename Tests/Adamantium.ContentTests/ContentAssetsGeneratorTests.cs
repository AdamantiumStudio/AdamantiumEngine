using System.Collections.Generic;
using System.Linq;
using Adamantium.Content.Build;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Adamantium.ContentTests;

[TestFixture]
public class ContentAssetsGeneratorTests
{
    private const string ProjectDir = "C:/Work/Game.SandboxContent/";

    [Test]
    public void EachContentFile_IsAConstantOfItsPathFromTheApplicationsFolder()
    {
        var code = Generate("SandboxContent", ["Models/F15C/F-15C_Eagle.dae", "Textures/tile.png"]);

        Assert.That(code, Does.Contain("public static class SandboxContentAssets"));
        Assert.That(code, Does.Contain("public const string F_15C_Eagle = \"Content/SandboxContent/Models/F15C/F-15C_Eagle.dae\";"));
        Assert.That(code, Does.Contain("public const string Tile = \"Content/SandboxContent/Textures/tile.png\";"));
        Assert.That(code, Does.Contain("public static class F15C"));
    }

    [Test]
    public void AFileNamedAsItsFolder_TakesItsFormatToo()
    {
        var code = Generate("Demo", ["Models/monkey/monkey.dae"]);

        Assert.That(code, Does.Contain("public const string Monkey_dae = \"Content/Demo/Models/monkey/monkey.dae\";"));
    }

    [Test]
    public void AFileNotMarkedAsContent_IsLeftOut()
    {
        var code = Generate("Demo", ["a.png"], ["Views/Page.auml"]);

        Assert.That(code, Does.Contain("A = \"Content/Demo/a.png\""));
        Assert.That(code, Does.Not.Contain("Page"));
    }

    [Test]
    public void AFileLinkedFromOutside_GoesUnderItsLink()
    {
        var outside = "C:/Work/Tests/Fonts/Roboto.ttf";
        var global = new Dictionary<string, string>
        {
            ["build_property.RootNamespace"] = "Game",
            ["build_property.ProjectDir"] = ProjectDir,
            ["build_property.AdamantiumContentName"] = "Demo"
        };
        var driver = CSharpGeneratorDriver.Create(
            [new ContentAssetsGenerator().AsSourceGenerator()],
            [new FileText(outside)],
            optionsProvider: new OptionsProvider(global, [outside],
                new Dictionary<string, string> { [outside] = "Fonts\\Roboto.ttf" }));

        var code = string.Concat(driver.RunGenerators(CSharpCompilation.Create("Probe")).GetRunResult().GeneratedTrees
            .Select(tree => tree.ToString()));

        Assert.That(code, Does.Contain("public const string Roboto = \"Content/Demo/Fonts/Roboto.ttf\";"));
    }

    [Test]
    public void AQuoteInAName_IsEscapedInTheConstant()
    {
        var code = Generate("Demo", ["say \"hi\".txt"]);

        Assert.That(code, Does.Contain("= \"Content/Demo/say \\\"hi\\\".txt\";"));
    }

    [Test]
    public void AProjectWithNoContentName_GetsNoClass()
    {
        Assert.That(Generate(null, ["a.png"]), Is.Empty);
    }

    private static string Generate(string name, string[] content, string[] other = null)
    {
        var global = new Dictionary<string, string>
        {
            ["build_property.RootNamespace"] = "Game.SandboxContent",
            ["build_property.ProjectDir"] = ProjectDir
        };
        if (name != null)
        {
            global["build_property.AdamantiumContentName"] = name;
        }

        var contentPaths = content.Select(path => ProjectDir + path).ToList();
        var texts = contentPaths.Concat((other ?? []).Select(path => ProjectDir + path))
            .Select(path => (AdditionalText)new FileText(path));

        var driver = CSharpGeneratorDriver.Create(
            [new ContentAssetsGenerator().AsSourceGenerator()],
            texts,
            optionsProvider: new OptionsProvider(global, contentPaths));

        var result = driver.RunGenerators(CSharpCompilation.Create("Probe")).GetRunResult();
        return string.Concat(result.GeneratedTrees.Select(tree => tree.ToString()));
    }
}
