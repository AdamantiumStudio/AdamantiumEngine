using System.Collections.Generic;
using System.Linq;
using Adamantium.EffectsCompiler;
using Adamantium.Vulkan.Slang;
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
            Assert.That(StagesOf(result.EffectData, techniques[0].Passes[0]), Is.EqualTo(new[] { "Vertex VS", "Pixel PS" }));
            Assert.That(StagesOf(result.EffectData, techniques[0].Passes[1]), Is.EqualTo(new[] { "Vertex VS" }));
            Assert.That(StagesOf(result.EffectData, techniques[1].Passes[0]), Is.EqualTo(new[] { "Compute CS" }));
            Assert.That(result.EffectData.Shaders, Has.Count.EqualTo(3), "a stage of two passes is compiled once");
        });
    }

    private static IEnumerable<string> StagesOf(EffectData data, EffectData.Pass pass) =>
        pass.Stages.Select(link => $"{link.Stage} {data.Shaders[link.Index].EntryPoint}");

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

    // A misspelt entry point keeps Slang's guess at what was meant.
    [Test]
    public void AMisspeltEntryPoint_IsSaidWithWhatItMayHaveMeant()
    {
        var result = Compile(Shaders + "technique T { pass { VertexShader = VSS; } }");

        Assert.That(Messages(result), Does.Contain("Vertex shader 'VSS'").And.Contain("did you mean 'VS'"));
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

    [Test]
    public void ATessellationPass_CompilesItsHullAndDomainShaders()
    {
        var result = Compile("""
            struct ControlPoint { float4 position : POSITION; };
            struct PatchConstants { float edges[3] : SV_TessFactor; float inside : SV_InsideTessFactor; };

            ControlPoint VS(ControlPoint point) { return point; }

            PatchConstants Constants(InputPatch<ControlPoint, 3> patch)
            {
                PatchConstants constants;
                constants.edges[0] = constants.edges[1] = constants.edges[2] = 4;
                constants.inside = 4;
                return constants;
            }

            [domain("tri")]
            [partitioning("integer")]
            [outputtopology("triangle_cw")]
            [outputcontrolpoints(3)]
            [patchconstantfunc("Constants")]
            ControlPoint HS(InputPatch<ControlPoint, 3> patch, uint id : SV_OutputControlPointID) { return patch[id]; }

            [domain("tri")]
            float4 DS(PatchConstants constants, float3 at : SV_DomainLocation, const OutputPatch<ControlPoint, 3> patch)
                : SV_Position
            {
                return patch[0].position * at.x + patch[1].position * at.y + patch[2].position * at.z;
            }

            float4 PS() : SV_Target { return 1; }

            technique T { pass { VertexShader = VS; HullShader = HS; DomainShader = DS; PixelShader = PS; } }
            """);

        Assert.That(result.HasErrors, Is.False, Messages(result));
        Assert.That(StagesOf(result.EffectData, result.EffectData.Description.Techniques[0].Passes[0]),
            Is.EqualTo(new[] { "Vertex VS", "Hull HS", "Domain DS", "Pixel PS" }));
    }

    private const string MeshAndRayTracingShaders = """
        struct MeshVertex { float4 position : SV_Position; };
        struct TaskPayload { uint count; };
        struct RayPayload { float4 color; };

        groupshared TaskPayload payload;

        [shader("amplification")]
        [numthreads(1, 1, 1)]
        void TS()
        {
            payload.count = 1;
            DispatchMesh(1, 1, 1, payload);
        }

        [shader("mesh")]
        [outputtopology("triangle")]
        [numthreads(1, 1, 1)]
        void MS(out vertices MeshVertex vertices[3], out indices uint3 triangles[1])
        {
            SetMeshOutputCounts(3, 1);
            vertices[0].position = float4(0, 0, 0, 1);
            vertices[1].position = float4(1, 0, 0, 1);
            vertices[2].position = float4(0, 1, 0, 1);
            triangles[0] = uint3(0, 1, 2);
        }

        float4 PS() : SV_Target { return 1; }

        void RG() {}

        void Miss(inout RayPayload payload) { payload.color = MISS_COLOR; }

        void Hit(inout RayPayload payload, BuiltInTriangleIntersectionAttributes attributes) { payload.color = 1; }

        technique T
        {
            pass Mesh { TaskShader = TS; MeshShader = MS; PixelShader = PS; }
            pass Trace { RayGenerationShader = RG; MissShader = Miss; HitGroup Triangles { ClosestHit = Hit; } }
        }
        """;

    // The compiler compiles every stage the language describes and the effect data holds them all, a hit group's too;
    // what Slang finds wrong in a mesh or a ray-tracing shader is said as in any other.
    [Test]
    public void MeshAndRayTracingPasses_Compile_AndTheirShadersMistakesAreSaid()
    {
        var clean = Compile(MeshAndRayTracingShaders, [new EffectData.ShaderMacro("MISS_COLOR", "0")]);
        var broken = Compile(MeshAndRayTracingShaders, [new EffectData.ShaderMacro("MISS_COLOR", "missColour")]);

        Assert.That(clean.HasErrors, Is.False, Messages(clean));
        var passes = clean.EffectData.Description.Techniques[0].Passes;
        var hitGroup = passes[1].HitGroups.Single();
        Assert.Multiple(() =>
        {
            Assert.That(StagesOf(clean.EffectData, passes[0]), Is.EqualTo(new[] { "Task TS", "Mesh MS", "Pixel PS" }));
            Assert.That(StagesOf(clean.EffectData, passes[1]), Is.EqualTo(new[] { "RayGeneration RG", "Miss Miss" }));
            Assert.That(hitGroup.Name, Is.EqualTo("Triangles"));
            Assert.That(hitGroup.Shaders.Select(link => $"{link.Stage} {clean.EffectData.Shaders[link.Index].EntryPoint}"),
                Is.EqualTo(new[] { "ClosestHit Hit" }));
            Assert.That(Messages(broken), Does.Contain("missColour"));
        });
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

    [Test]
    public void EveryStage_IsReadWithTheStageSlangCompilesItFor()
    {
        var source = EffectSource.Read(@"
technique T
{
    pass Draw { VertexShader = VS; HullShader = HS; DomainShader = DS; PixelShader = PS; }
    pass Mesh { TaskShader = TS; MeshShader = MS; PixelShader = PS; }
    pass Trace { RayGenerationShader = RG; MissShader = M; CallableShader = C; HitGroup H { AnyHit = AH; ClosestHit = CH; Intersection = I; } }
}", "Test.fx");

        var passes = source.Techniques.Single().Passes;
        var group = passes[2].HitGroups.Single();
        Assert.Multiple(() =>
        {
            Assert.That(source.Errors, Is.Empty);
            Assert.That(passes.SelectMany(pass => pass.Stages).Select(stage => (stage.EntryPoint, stage.SlangStage)),
                Is.EqualTo(new[]
                {
                    ("VS", SlangcStage.Vertex), ("HS", SlangcStage.Hull), ("DS", SlangcStage.Domain),
                    ("PS", SlangcStage.Fragment), ("TS", SlangcStage.Amplification), ("MS", SlangcStage.Mesh),
                    ("PS", SlangcStage.Fragment), ("RG", SlangcStage.RayGeneration), ("M", SlangcStage.Miss),
                    ("C", SlangcStage.Callable)
                }));
            Assert.That(group.Name, Is.EqualTo("H"));
            Assert.That(group.Stages.Select(stage => (stage.EntryPoint, stage.SlangStage)), Is.EqualTo(new[]
            {
                ("AH", SlangcStage.AnyHit), ("CH", SlangcStage.ClosestHit), ("I", SlangcStage.Intersection)
            }));
            Assert.That(group.Stages[1].Location.ToString(), Is.EqualTo("Test.fx(6,106)"));
        });
    }

    // What follows a technique that breaks the rules is read as ever: its own techniques, its code on its own lines.
    [Test]
    public void ATechniqueThatBreaksTheRules_IsBlankedToItsClose_AndTheFileReadOn()
    {
        const string text = "technique A { pass { NotAStage = X; } }\nfloat4 Kept;\ntechnique B { pass { VertexShader = VS; } }";

        var source = EffectSource.Read(text, "Test.fx");

        Assert.Multiple(() =>
        {
            Assert.That(source.Errors.Select(error => error.ToString()), Is.EqualTo(new[]
            {
                "Test.fx(1,22): error: 'NotAStage' is not a stage of a pass: VertexShader, HullShader, DomainShader, " +
                "PixelShader, TaskShader, MeshShader, ComputeShader, RayGenerationShader, MissShader, CallableShader or HitGroup"
            }));
            Assert.That(source.Techniques.Select(technique => technique.Name), Is.EqualTo(new[] { "B" }));
            Assert.That(source.Code.Split('\n'), Is.EqualTo(new[] { new string(' ', 39), "float4 Kept;", new string(' ', 43) }));
        });
    }

    // A technique left open ends at the next technique or the end of the file; a misspelt pass or a stray mark inside
    // one is blanked with it, to its close; a brace it did not open is the code's, and ends it; a directive inside is
    // no brace of it.
    [TestCase("technique T { pass { VertexShader = VS;\nfloat4 Gone;", "                                       \n            ")]
    [TestCase("technique T { pass A { VertexShader = VS; } Pass B { VertexShader = VS; } }\nint Kept;",
        "                                                                           \nint Kept;")]
    [TestCase("technique T { pass A { VertexShader = VS; } ; }\nint Kept;", "                                               \nint Kept;")]
    [TestCase("technique A { pass { VertexShader = VS;\ntechnique B { pass { VertexShader = VS; } }\nint Kept;",
        "                                       \n                                           \nint Kept;")]
    [TestCase("technique T { pass { NotAStage = X; }\n#define A {\n}\nint Kept;", "                                     \n           \n \nint Kept;")]
    [TestCase("void F() { technique }\nint Kept;", "void F() {           }\nint Kept;")]
    public void ATechniqueBrokenOffEarly_IsBlankedAsFarAsItGoes(string text, string code)
    {
        var source = EffectSource.Read(text, "Test.fx");

        Assert.Multiple(() =>
        {
            Assert.That(source.Errors, Is.Not.Empty);
            Assert.That(source.Code, Is.EqualTo(code));
        });
    }

    [Test]
    public void ATechniqueLeftOpen_LeavesTheNextOneRead()
    {
        var source = EffectSource.Read("technique A { pass { VertexShader = VS; }\ntechnique B { pass { VertexShader = VS; } }",
            "Test.fx");

        Assert.Multiple(() =>
        {
            Assert.That(source.Errors.Select(error => error.ToString()),
                Is.EqualTo(new[] { "Test.fx(2,1): error: 'technique' where a pass was expected" }));
            Assert.That(source.Techniques.Select(technique => technique.Name), Is.EqualTo(new[] { "B" }));
        });
    }

    // A directive after a technique that breaks the rules is still a directive: the word technique in it is not one.
    [Test]
    public void ADirectiveAfterATechniqueThatBreaksTheRules_IsADirective()
    {
        const string text = "technique T { pass A { NotAStage = VS; } }\n#define T2 technique\nfloat4 x;\n";

        var source = EffectSource.Read(text, "Test.fx");

        Assert.Multiple(() =>
        {
            Assert.That(source.Errors, Has.Count.EqualTo(1));
            Assert.That(source.Code.Split('\n').Skip(1), Is.EqualTo(new[] { "#define T2 technique", "float4 x;", "" }));
        });
    }

    // A lone CR ends a line, as for Slang: errors stand on the line after it, a comment ends at it.
    [Test]
    public void ALoneCarriageReturn_EndsALine()
    {
        var misspelt = EffectSource.Read("float4 VS();\rtechnique T { pass { VertexShadr = VS; } }", "Test.fx");
        var commented = EffectSource.Read("// a comment\rtechnique T { pass { VertexShader = VS; } }\r", "Test.fx");

        Assert.Multiple(() =>
        {
            Assert.That(misspelt.Errors.Single().Location.ToString(), Is.EqualTo("Test.fx(2,22)"));
            Assert.That(commented.Errors, Is.Empty);
            Assert.That(commented.Techniques, Has.Count.EqualTo(1));
            Assert.That(commented.Code, Does.Not.Contain("technique"));
        });
    }

    [Test]
    public void Errors_AreInTheOrderOfTheFile()
    {
        var source = EffectSource.Read(
            "technique T { pass { VertexShader = VS; } }\ntechnique T { pass P { PixelShader = PS; } pass P { VertexShader = VS; } }",
            "Test.fx");

        Assert.That(source.Errors.Select(error => error.Location.ToString()),
            Is.EqualTo(new[] { "Test.fx(2,1)", "Test.fx(2,15)", "Test.fx(2,44)" }));
    }
}
