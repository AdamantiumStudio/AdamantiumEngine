using System.Collections.Generic;
using System.Linq;
using Adamantium.EffectsCompiler;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests;

/// <summary>An effect file's techniques and passes as the effect language has them: every stage a pass may name, one
/// kind of pipeline a pass, what breaks the rules said where it stands; the code around them goes to Slang on its own
/// lines, with the macros given.</summary>
[TestFixture]
public class EffectSourceTests
{
    private const string Shaders = @"
float4 VS(float4 position : POSITION) : SV_Position { return position; }
float4 PS() : SV_Target { return float4(1, 0, 0, 1); }
[numthreads(1, 1, 1)]
void CS() {}
";

    private static EffectCompilerResult Compile(string source, List<EffectData.ShaderMacro> macros = null) =>
        EffectCompiler.Compile(source, "Test.fx", macrosArgs: macros);

    private static string Messages(EffectCompilerResult result) =>
        string.Join("\n", result.Logger.Messages.Select(message => message.ToString()));

    [Test]
    public void TechniquesAndPasses_AreReadWithTheirStages_ByName()
    {
        var result = Compile(Shaders + @"
technique Draw
{
    pass First { VertexShader = VS; PixelShader = PS; }
    pass Second { VertexShader = VS; }
}
technique { pass { ComputeShader = CS; } }");

        Assert.That(result.HasErrors, Is.False, Messages(result));
        var techniques = result.EffectData.Description.Techniques;
        Assert.Multiple(() =>
        {
            Assert.That(result.EffectData.Description.Name, Is.EqualTo("Test"), "the file's name");
            Assert.That(techniques.Select(t => t.Name), Is.EqualTo(new[] { "Draw", null }));
            Assert.That(techniques[0].Passes.Select(p => p.Name), Is.EqualTo(new[] { "First", "Second" }));
            Assert.That(techniques[0].Passes[0].Pipeline[EffectShaderType.Fragment].EntryPoint, Is.EqualTo("PS"));
            Assert.That(techniques[1].Passes[0].Pipeline[EffectShaderType.Compute].EntryPoint, Is.EqualTo("CS"));
        });
    }

    // A technique quoted in a comment, a string or a directive - one spliced over lines, one with a comment running on
    // past its line - is code, not a technique.
    [TestCase("\n")]
    [TestCase("\r\n")]
    public void TheWordTechnique_InACommentAStringOrADirective_IsLeftAlone(string lineBreak)
    {
        var source = string.Join(lineBreak,
            "// technique Commented { pass { NotAStage = X; } }",
            "// a comment spliced \\",
            "technique Spliced { pass { NotAStage = X; } }",
            "/* technique Blocked { pass { NotAStage = X; } } */",
            "#define QUOTED \"technique Quoted { pass { NotAStage = X; } }\"",
            "#define LONG 1 \\",
            "technique Continued { pass { NotAStage = X; } }",
            "#define OPENED 1 /* opened on the directive's line",
            "technique InTheComment { pass { NotAStage = X; } }",
            "*/",
            "/* before */ #define AFTER_A_COMMENT \\",
            "technique AfterAComment { pass { NotAStage = X; } }",
            "#define APOSTROPHE '\"' /* a quote in quotes, then a comment",
            "technique Quoted2 { pass { NotAStage = X; } }",
            "*/",
            "#define STRING \"/* // not comments\"",
            "static const int technique_count = 1;",
            Shaders,
            "technique Real { pass { VertexShader = VS; } }");

        var result = Compile(source);

        Assert.That(result.HasErrors, Is.False, Messages(result));
        Assert.That(result.EffectData.Description.Techniques.Single().Name, Is.EqualTo("Real"));
    }

    [TestCase("\n")]
    [TestCase("\r\n")]
    public void AFileWithoutATechnique_IsAnError(string lineBreak)
    {
        var result = Compile(Shaders.Replace("\n", lineBreak) + "technique11 T { pass P { VertexShader = VS; } }");

        Assert.That(result.HasErrors, Is.True);
        Assert.That(Messages(result), Does.Contain("Test.fx(1,1)").And.Contain("has no technique"));
    }

    [TestCase("pass { EffectName = \"X\"; VertexShader = VS; }", "(3,22)", "'EffectName' is not a stage")]
    [TestCase("pass { GeometryShader = VS; }", "(3,22)", "'GeometryShader' is not a stage")]
    [TestCase("pass { VertexShader = VS }", "(3,40)", "';' expected")]
    [TestCase("pass { VertexShader = ; }", "(3,37)", "An entry point's name")]
    [TestCase("pass { VertexShader = VS; ComputeShader = CS; }", "(3,15)", "one kind of pipeline, not graphics and compute")]
    [TestCase("pass { VertexShader = VS; VertexShader = PS; }", "(3,41)", "names its Vertex shader twice")]
    [TestCase("pass { PixelShader = PS; }", "(3,15)", "A graphics pass has a vertex shader")]
    [TestCase("pass { VertexShader = VS; HullShader = VS; }", "(3,15)", "Hull and domain shaders go together")]
    [TestCase("pass { TaskShader = CS; }", "(3,15)", "A mesh pass has a mesh shader")]
    [TestCase("pass { MissShader = CS; }", "(3,15)", "A ray-tracing pass has one ray generation shader")]
    [TestCase("pass { ComputeShader = CS; PixelShader = PS; }", "(3,15)", "A compute pass has no pixel shader")]
    [TestCase("pass { }", "(3,15)", "The pass runs no shader")]
    [TestCase("pass P { VertexShader = VS; } pass P { VertexShader = VS; }", "(3,45)", "two passes named 'P'")]
    [TestCase("pass { RayGenerationShader = CS; HitGroup H { Closest = CS; } }", "(3,61)", "not a shader of a hit group")]
    [TestCase("pass { RayGenerationShader = CS; HitGroup H { } }", "(3,48)", "names no shader")]
    [TestCase("pass { RayGenerationShader = CS; HitGroup H { AnyHit = CS; } HitGroup H { AnyHit = CS; } }", "(3,76)",
        "two hit groups named 'H'")]
    [TestCase("pass { VertexShader = VS; MeshShader = CS; }", "(3,15)", "not graphics and mesh")]
    [TestCase("pass { RayGenerationShader = CS; PixelShader = PS; }", "(3,15)", "A ray-tracing pass has no pixel shader")]
    [TestCase("pass { VertexShader = VS; } pass { VertexShader = VS; }", "(3,43)", "two passes without a name")]
    [TestCase("pass { VertexShader = VS;\n#if 1\n}", "(4,1)", "'#' where a stage")]
    public void WhatBreaksTheRules_IsSaidWhereItStands(string pass, string where, string what)
    {
        var result = Compile($"\n\ntechnique T {{ {pass} }}\n" + Shaders);

        Assert.That(result.HasErrors, Is.True);
        Assert.That(Messages(result), Does.Contain("Test.fx" + where).And.Contain(what));
    }

    [TestCase("T", "two techniques named 'T'")]
    [TestCase("", "two techniques without a name")]
    public void TwoTechniquesOfOneName_AreAnError(string name, string what)
    {
        var result = Compile(Shaders + $"technique {name} {{ pass {{ VertexShader = VS; }} }}\n" +
                             $"technique {name} {{ pass {{ VertexShader = VS; }} }}");

        Assert.That(Messages(result), Does.Contain(what));
    }

    // A file read as bytes may keep its byte order mark; it is not code.
    [Test]
    public void AByteOrderMark_IsNotCode()
    {
        var result = Compile((char)0xFEFF + Shaders + "technique T { pass { VertexShader = VS; } }");

        Assert.That(result.HasErrors, Is.False, Messages(result));
    }

    [TestCase("1A", "1")]
    [TestCase("A-B", "1")]
    [TestCase("A\\", "1")]
    [TestCase("A", "1 \\")]
    [TestCase("A", "1 /* comment")]
    [TestCase("A", "1 // comment")]
    [TestCase("A", "1\n2")]
    public void AMacroNotANameWithAValueOfOneLine_IsAnError(string name, string value)
    {
        var result = Compile(Shaders + "technique T { pass { VertexShader = VS; } }", [new EffectData.ShaderMacro(name, value)]);

        Assert.That(result.HasErrors, Is.True);
        Assert.That(Messages(result), Does.Contain("The macro"));
    }

    // An error in the code is said once, where it stands, however many entry points Slang compiled the code for.
    [Test]
    public void AnErrorInTheCode_IsSaidOnce_WhereItStands()
    {
        var result = Compile(Shaders + "float4 Bad() : SV_Position { return undefinedName; }\n" +
                             "technique T { pass A { VertexShader = VS; PixelShader = PS; } pass B { ComputeShader = CS; } }");

        var messages = result.Logger.Messages.Select(message => message.ToString()).ToList();
        Assert.That(messages.Count(message => message.Contains("undefinedName")), Is.EqualTo(1), Messages(result));
        Assert.That(messages.Single(message => message.Contains("undefinedName")), Does.StartWith("Test.fx(6,37)"));
    }

    // Each of Slang's errors is said - those it numbers E39999 too - and its warnings are not errors.
    [Test]
    public void EverySlangError_IsSaid_AndAWarningIsNotAnError()
    {
        var result = Compile(Shaders + "float4 Bad() : SV_Position { int i = 1.5; float4 x = float3(1, 2, 3) * float2(1, 2); " +
                             "return undefinedQ; }\ntechnique T { pass { VertexShader = VS; } }");

        var messages = Messages(result);
        Assert.Multiple(() =>
        {
            Assert.That(messages, Does.Contain("undefinedQ"));
            Assert.That(messages, Does.Contain("[E39999]"), "the operator error");
            Assert.That(messages, Does.Not.Contain("warning["));
            Assert.That(messages, Does.Not.Contain("compilation ceased"));
        });
    }

    // Words of the code Slang quotes are not its message: an error beside a comment about an entry point is still said
    // where it stands.
    [Test]
    public void AnErrorBesideACommentOnEntryPoints_IsSaidWhereItStands()
    {
        var result = Compile(Shaders + "float4 Bad() : SV_Position { return undefinedP; } // the entry point of nothing\n" +
                             "technique T { pass { VertexShader = VS; } }");

        Assert.That(Messages(result), Does.Contain("Test.fx(6,37)"));
    }

    // The same missing entry point named by two stages, or by one stage in two passes, is said at each - and only the
    // error that says so, not Slang's nameless "undefined identifier" beside it.
    [Test]
    public void AMissingEntryPoint_IsSaidAtEveryStageThatNamesIt()
    {
        var result = Compile(Shaders + "technique T\n{\n" +
                             "    pass A { VertexShader = Nope; PixelShader = Nope; }\n" +
                             "    pass B { VertexShader = VS; PixelShader = Nope; }\n}");

        var messages = result.Logger.Messages.Select(message => message.ToString()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(messages.Count(message => message.Contains("Vertex shader 'Nope'")), Is.EqualTo(1));
            Assert.That(messages.Count(message => message.Contains("Pixel shader 'Nope'")), Is.EqualTo(2));
            Assert.That(messages, Has.None.Contains("[E30015]"), string.Join("\n", messages));
        });
    }

    // Words of the quoted code are not Slang's message: a comment about failed imports does not hide its error.
    [Test]
    public void AnErrorBesideACommentOnFailedImports_IsSaid()
    {
        var result = Compile(Shaders + "float4 A() : SV_Target { return undefinedA; } // import failed due to compilation error\n" +
                             "float4 B() : SV_Target { return undefinedB; }\n" +
                             "technique T { pass { VertexShader = VS; } }");

        Assert.That(Messages(result), Does.Contain("undefinedA").And.Contain("undefinedB"));
    }

    // A stage refused does not take the stages after it down with it: they compile, or say what is wrong with them.
    [Test]
    public void ARefusedStage_LeavesTheOthersOfItsPassCompiled()
    {
        var result = Compile(Shaders + "technique T { pass { VertexShader = VS; HullShader = VS; DomainShader = VS; PixelShader = PS; } }");

        Assert.That(result.HasErrors, Is.True);
        Assert.That(result.EffectData.Description.Techniques[0].Passes[0].Pipeline[EffectShaderType.Fragment]?.EntryPoint,
            Is.EqualTo("PS"));
    }

    [TestCase("/no/such/folder/Test.fx")]
    [TestCase("folder with \"quotes\"/Test.fx")]
    public void AnyPath_IsTheFilesName_AndNoReasonToFail(string path)
    {
        var good = EffectCompiler.Compile(Shaders + "technique T { pass { VertexShader = VS; } }", path);
        var bad = EffectCompiler.Compile(Shaders + "float4 Bad() : SV_Position { return undefinedName; }\n" +
                                         "technique T { pass { VertexShader = VS; } }", path);

        Assert.That(good.HasErrors, Is.False, Messages(good));
        Assert.That(Messages(bad), Does.Contain("undefinedName").And.Contain("Test.fx(6,37)"));
    }

    [TestCase("technique T { pass { VertexShader = VS; }", "(6,1)", "The technique is not closed")]
    [TestCase("technique T { pass { VertexShader = VS;", "(6,15)", "The pass is not closed")]
    [TestCase("technique T { pass { RayGenerationShader = CS; HitGroup H { AnyHit = CS;", "(6,48)",
        "The hit group is not closed")]
    public void AnUnclosedBlock_IsSaidWhereItOpens(string tail, string where, string what)
    {
        var result = Compile(Shaders + tail);

        Assert.That(result.HasErrors, Is.True);
        Assert.That(Messages(result), Does.Contain("Test.fx" + where).And.Contain(what));
    }

    // The language describes tessellation, mesh and ray-tracing passes; until effects run them, compiling one is an
    // error that says so.
    [TestCase("pass { VertexShader = VS; HullShader = CS; DomainShader = CS; PixelShader = PS; }", "Hull shaders are described")]
    [TestCase("pass { TaskShader = CS; MeshShader = CS; PixelShader = PS; }", "Task shaders are described")]
    [TestCase("pass { RayGenerationShader = CS; MissShader = CS; MissShader = CS; CallableShader = CS; " +
              "HitGroup H { ClosestHit = CS; AnyHit = CS; Intersection = CS; } }", "RayGeneration shaders are described")]
    public void APassOfAPipelineEffectsDoNotRunYet_IsReadAndRefused(string pass, string what)
    {
        var result = Compile($"technique T {{ {pass} }}\n" + Shaders);

        Assert.That(result.HasErrors, Is.True);
        Assert.That(Messages(result), Does.Contain(what).And.Not.Contain("is not a stage"));
    }

    // Slang reports on the file's own lines and names the file: the technique before the code takes none of them away,
    // whatever the line breaks.
    [TestCase("\n")]
    [TestCase("\r\n")]
    public void AnErrorInTheCode_IsOnItsLine_AfterATechnique(string lineBreak)
    {
        var result = Compile(string.Join(lineBreak, "technique T", "{", "    pass { VertexShader = VS; }", "}",
            "float4 VS() : SV_Position { return undefinedName; }", ""));

        Assert.That(result.HasErrors, Is.True);
        Assert.That(Messages(result), Does.Contain("Test.fx:5:36").And.Contain("undefinedName"));
    }

    // An entry point the code does not have is said at the pass's stage that names it.
    [Test]
    public void AMissingEntryPoint_IsSaidAtTheStageThatNamesIt()
    {
        var result = Compile(Shaders + "technique T\n{\n    pass { VertexShader = Nope; }\n}");

        Assert.That(result.HasErrors, Is.True);
        Assert.That(Messages(result), Does.Contain("Test.fx(8,12)").And.Contain("Vertex shader 'Nope'"));
    }

    [Test]
    public void TheMacrosGiven_AreDefinedForSlang()
    {
        const string source = @"
#ifdef WITH_ENTRY
float4 VS(float4 position : POSITION) : SV_Position { return position * SCALE; }
#endif
technique T { pass { VertexShader = VS; } }";

        var without = Compile(source);
        var with = Compile(source, [new EffectData.ShaderMacro("WITH_ENTRY", "1"), new EffectData.ShaderMacro("SCALE", "2")]);

        Assert.Multiple(() =>
        {
            Assert.That(without.HasErrors, Is.True, "no entry point without the macro");
            Assert.That(with.HasErrors, Is.False, Messages(with));
        });
    }
}
