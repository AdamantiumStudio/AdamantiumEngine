using System.Collections.Generic;
using System.Linq;
using Adamantium.Core;

namespace Adamantium.EffectsCompiler;

/// <summary>Reads the techniques of an effect file by the effect language's grammar:
/// <code>
/// technique = "technique" [Name] "{" { pass } "}"
/// pass      = "pass" [Name] "{" { stage | hitGroup } "}"
/// stage     = StageKey "=" EntryPoint ";"
/// hitGroup  = "HitGroup" Name "{" { HitShaderKey "=" EntryPoint ";" } "}"
/// </code>
/// StageKey is VertexShader, HullShader, DomainShader, PixelShader, TaskShader, MeshShader, ComputeShader,
/// RayGenerationShader, MissShader or CallableShader; HitShaderKey is ClosestHit, AnyHit or Intersection. Everything
/// outside techniques is Slang code and is passed over. A technique that breaks the grammar is reported where it
/// breaks it and skipped - to its closing brace, a brace it did not open or the next technique - and reading goes on.
/// The rules a pass keeps besides the grammar are <see cref="EffectPassValidator"/>'s.</summary>
internal sealed class EffectParser
{
    private const string Technique = "technique";
    private const string Pass = "pass";
    private const string HitGroup = "HitGroup";

    private static readonly Dictionary<string, EffectStage> StageKeys = new()
    {
        ["VertexShader"] = EffectStage.Vertex,
        ["HullShader"] = EffectStage.Hull,
        ["DomainShader"] = EffectStage.Domain,
        ["PixelShader"] = EffectStage.Pixel,
        ["TaskShader"] = EffectStage.Task,
        ["MeshShader"] = EffectStage.Mesh,
        ["ComputeShader"] = EffectStage.Compute,
        ["RayGenerationShader"] = EffectStage.RayGeneration,
        ["MissShader"] = EffectStage.Miss,
        ["CallableShader"] = EffectStage.Callable
    };

    private static readonly Dictionary<string, EffectStage> HitShaderKeys = new()
    {
        ["ClosestHit"] = EffectStage.ClosestHit,
        ["AnyHit"] = EffectStage.AnyHit,
        ["Intersection"] = EffectStage.Intersection
    };

    private readonly EffectLexer _lexer;
    private readonly List<EffectTechniqueSource> _techniques = [];
    private readonly List<(int Start, int End)> _techniqueSpans = [];
    private readonly List<EffectCompilerMessage> _errors = [];
    private EffectToken _token;

    public EffectParser(string text, string filePath)
    {
        _lexer = new EffectLexer(text, filePath);
        _token = _lexer.ReadToken();
    }

    /// <summary>The techniques read, in the order the file has them.</summary>
    public IReadOnlyList<EffectTechniqueSource> Techniques => _techniques;

    /// <summary>Where each technique stands in the file, read or skipped: from its first character to just past its
    /// last.</summary>
    public IReadOnlyList<(int Start, int End)> TechniqueSpans => _techniqueSpans;

    /// <summary>What breaks the effect language, in the order it was found.</summary>
    public IReadOnlyList<EffectCompilerMessage> Errors => _errors;

    /// <summary>Reads every technique of the file.</summary>
    public void ParseFile()
    {
        while (_token.Kind != EffectTokenKind.End)
        {
            if (_token.IsWord(Technique))
            {
                ParseTechnique();
            }
            else
            {
                Advance();
            }
        }
    }

    private void ParseTechnique()
    {
        var start = _token;
        Advance();
        try
        {
            var name = ReadOptionalName();
            Expect('{');
            List<EffectPassSource> passes = [];
            while (!_token.IsMark('}'))
            {
                if (_token.Kind == EffectTokenKind.End)
                {
                    AddError("The technique is not closed.", start.Location);
                    _techniqueSpans.Add((start.Start, _token.Start));
                    return;
                }

                if (!_token.IsWord(Pass))
                {
                    throw new EffectSyntaxException(_token.Kind == EffectTokenKind.Word
                        ? $"'{_token.Text}' where a pass was expected"
                        : $"'{_token.DisplayText}' where a pass or the end of the technique was expected", _token.Location);
                }

                AddPass(passes, ParsePass());
            }

            var close = _token;
            Advance();
            AddTechnique(new EffectTechniqueSource(name, start.Location, passes));
            _techniqueSpans.Add((start.Start, close.End));
        }
        catch (EffectSyntaxException error)
        {
            AddError(error.Message, error.Location);
            SkipBrokenTechnique(start);
        }
    }

    private EffectPassSource ParsePass()
    {
        var start = _token;
        Advance();
        var name = ReadOptionalName();
        Expect('{');
        List<EffectStageSource> stages = [];
        List<EffectHitGroupSource> hitGroups = [];
        while (!_token.IsMark('}'))
        {
            if (_token.Kind == EffectTokenKind.End)
            {
                throw new EffectSyntaxException("The pass is not closed.", start.Location);
            }

            if (_token.IsWord(HitGroup))
            {
                AddHitGroup(hitGroups, ParseHitGroup());
            }
            else
            {
                stages.Add(ParseStage());
            }
        }

        Advance();
        var pass = new EffectPassSource(name, start.Location, stages, hitGroups);
        EffectPassValidator.Validate(pass, _errors);
        return pass;
    }

    private EffectStageSource ParseStage()
    {
        var key = _token;
        if (key.Kind != EffectTokenKind.Word)
        {
            throw new EffectSyntaxException(
                $"'{key.DisplayText}' where a stage, a hit group or the end of the pass was expected", key.Location);
        }

        if (!StageKeys.TryGetValue(key.Text, out var stage))
        {
            throw new EffectSyntaxException(
                $"'{key.Text}' is not a stage of a pass: {string.Join(", ", StageKeys.Keys)} or {HitGroup}", key.Location);
        }

        Advance();
        return new EffectStageSource(stage, ReadEntryPoint(key.Text), key.Location);
    }

    private EffectHitGroupSource ParseHitGroup()
    {
        var start = _token;
        Advance();
        if (_token.Kind != EffectTokenKind.Word)
        {
            throw new EffectSyntaxException("A hit group is named: HitGroup Name { ... }", _token.Location);
        }

        var name = _token.Text;
        Advance();
        Expect('{');
        List<EffectStageSource> shaders = [];
        while (!_token.IsMark('}'))
        {
            if (_token.Kind == EffectTokenKind.End)
            {
                throw new EffectSyntaxException("The hit group is not closed.", start.Location);
            }

            var key = _token;
            if (key.Kind != EffectTokenKind.Word || !HitShaderKeys.TryGetValue(key.Text, out var stage))
            {
                var shown = key.Kind == EffectTokenKind.Word ? key.Text : key.DisplayText;
                throw new EffectSyntaxException(
                    $"'{shown}' is not a shader of a hit group: {string.Join(", ", HitShaderKeys.Keys)}", key.Location);
            }

            Advance();
            var entryPoint = ReadEntryPoint(key.Text);
            if (shaders.Any(shader => shader.Stage == stage))
            {
                AddError($"The hit group names its {key.Text} twice.", key.Location);
            }
            else
            {
                shaders.Add(new EffectStageSource(stage, entryPoint, key.Location));
            }
        }

        Advance();
        if (shaders.Count == 0)
        {
            AddError($"The hit group '{name}' names no shader.", start.Location);
        }

        return new EffectHitGroupSource(name, start.Location, shaders);
    }

    private string ReadEntryPoint(string key)
    {
        Expect('=');
        if (_token.Kind != EffectTokenKind.Word)
        {
            throw new EffectSyntaxException($"An entry point's name after '{key} ='", _token.Location);
        }

        var entryPoint = _token.Text;
        Advance();
        Expect(';');
        return entryPoint;
    }

    private string ReadOptionalName()
    {
        if (_token.Kind != EffectTokenKind.Word)
        {
            return null;
        }

        var name = _token.Text;
        Advance();
        return name;
    }

    private void Expect(char mark)
    {
        if (!_token.IsMark(mark))
        {
            throw new EffectSyntaxException($"'{mark}' expected, not '{_token.DisplayText}'", _token.Location);
        }

        Advance();
    }

    private void SkipBrokenTechnique(EffectToken start)
    {
        _lexer.MoveTo(start.End);
        Advance();
        var depth = 0;
        while (_token.Kind != EffectTokenKind.End && !_token.IsWord(Technique))
        {
            if (_token.IsMark('{'))
            {
                depth++;
            }
            else if (_token.IsMark('}'))
            {
                if (depth == 0)
                {
                    break;
                }

                if (--depth == 0)
                {
                    var close = _token;
                    Advance();
                    _techniqueSpans.Add((start.Start, close.End));
                    return;
                }
            }

            Advance();
        }

        _techniqueSpans.Add((start.Start, _token.Start));
    }

    private void AddTechnique(EffectTechniqueSource technique)
    {
        if (_techniques.Any(other => other.Name == technique.Name))
        {
            AddError(technique.Name == null
                ? "The effect has two techniques without a name."
                : $"The effect has two techniques named '{technique.Name}'.", technique.Location);
        }

        _techniques.Add(technique);
    }

    private void AddPass(List<EffectPassSource> passes, EffectPassSource pass)
    {
        if (passes.Any(other => other.Name == pass.Name))
        {
            AddError(pass.Name == null
                ? "The technique has two passes without a name."
                : $"The technique has two passes named '{pass.Name}'.", pass.Location);
        }

        passes.Add(pass);
    }

    private void AddHitGroup(List<EffectHitGroupSource> hitGroups, EffectHitGroupSource hitGroup)
    {
        if (hitGroups.Any(other => other.Name == hitGroup.Name))
        {
            AddError($"The pass has two hit groups named '{hitGroup.Name}'.", hitGroup.Location);
        }

        hitGroups.Add(hitGroup);
    }

    private void Advance() => _token = _lexer.ReadToken();

    private void AddError(string text, SourceLocation location) =>
        _errors.Add(new EffectCompilerMessage(LogMessageType.Error, text, location));
}
