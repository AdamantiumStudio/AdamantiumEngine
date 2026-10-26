using System.Collections.Generic;
using System.Linq;

namespace Adamantium.EffectsCompiler.Compiler;

/// <summary>An effect file read: its techniques and passes - the entry point each stage of a pass runs - and its code with
/// them blanked out, line for line, for Slang. A technique is <c>technique Name { pass Name { VertexShader = Main; } }</c>,
/// its name and its passes' optional; a pass names its stages - VertexShader, HullShader, DomainShader, PixelShader,
/// TaskShader, MeshShader, ComputeShader, RayGenerationShader, MissShader, CallableShader - and a ray-tracing pass its
/// <c>HitGroup Name { ClosestHit = ...; AnyHit = ...; Intersection = ...; }</c>. A pass runs one kind of pipeline:
/// graphics (a vertex shader, a pixel shader, hull and domain shaders together), mesh (a mesh shader, a task and a pixel
/// shader), compute, or ray tracing (one ray generation shader, any misses, callables and hit groups). What breaks the
/// rules is logged where it stands.</summary>
internal sealed class EffectSource
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

    private static readonly string[] HitGroupKeys = ["ClosestHit", "AnyHit", "Intersection"];

    private readonly string _text;
    private readonly string _filePath;
    private readonly EffectCompilerLogger _logger;
    private readonly char[] _code;
    private readonly List<int> _lineStarts = [0];
    private readonly List<EffectTechniqueSource> _techniques = [];
    private int _at;

    private EffectSource(string text, string filePath, EffectCompilerLogger logger)
    {
        _text = text ?? string.Empty;
        _filePath = filePath;
        _logger = logger;
        _code = _text.ToCharArray();
        for (var i = 0; i < _text.Length; i++)
        {
            if (_text[i] == '\n')
            {
                _lineStarts.Add(i + 1);
            }
        }
    }

    /// <summary>The techniques in the order the file has them.</summary>
    public IReadOnlyList<EffectTechniqueSource> Techniques => _techniques;

    /// <summary>The file with its techniques blanked out - every character but line breaks a space - so what Slang
    /// reports stands on the file's own lines and columns.</summary>
    public string Code { get; private set; }

    /// <summary>Reads <paramref name="text"/>, the effect file at <paramref name="filePath"/>, logging what breaks the
    /// rules to <paramref name="logger"/>.</summary>
    public static EffectSource Read(string text, string filePath, EffectCompilerLogger logger)
    {
        var source = new EffectSource(text, filePath, logger);
        source.ReadFile();
        source.Code = new string(source._code);
        return source;
    }

    private void ReadFile()
    {
        while (_at < _text.Length)
        {
            var c = _text[_at];
            if (SkipComment())
            {
                continue;
            }

            if (c is '"' or '\'')
            {
                SkipQuoted(c);
            }
            else if (c == '#' && StartsALine(_at))
            {
                SkipDirective();
            }
            else if (IsWordStart(c) && (_at == 0 || !IsWordPart(_text[_at - 1])))
            {
                var start = _at;
                if (ReadWord() == Technique && !ReadTechnique(start))
                {
                    return;
                }
            }
            else
            {
                _at++;
            }
        }
    }

    private bool ReadTechnique(int start)
    {
        SkipSpace();
        var name = AtWord() ? ReadWord() : null;
        if (!Expect('{'))
        {
            return false;
        }

        var passes = new List<EffectPassSource>();
        while (true)
        {
            SkipSpace();
            if (Peek() == '}')
            {
                _at++;
                break;
            }

            var at = _at;
            var word = AtWord() ? ReadWord() : null;
            if (word != Pass)
            {
                return Fail(at, word == null
                    ? $"'{Shown(at)}' where a pass or the end of the technique was expected"
                    : $"'{word}' where a pass was expected");
            }

            var pass = ReadPass(at);
            if (pass == null)
            {
                return false;
            }

            if (pass.Name != null && passes.Any(other => other.Name == pass.Name))
            {
                _logger.Error($"The technique has two passes named '{pass.Name}'.", pass.Location);
            }

            passes.Add(pass);
        }

        var technique = new EffectTechniqueSource(name, Where(start), passes);
        if (name != null && _techniques.Any(other => other.Name == name))
        {
            _logger.Error($"The effect has two techniques named '{name}'.", technique.Location);
        }

        _techniques.Add(technique);
        Blank(start, _at);
        return true;
    }

    private EffectPassSource ReadPass(int start)
    {
        SkipSpace();
        var name = AtWord() ? ReadWord() : null;
        if (!Expect('{'))
        {
            return null;
        }

        var stages = new List<EffectStageSource>();
        var groups = new List<EffectHitGroupSource>();
        while (true)
        {
            SkipSpace();
            if (Peek() == '}')
            {
                _at++;
                break;
            }

            var at = _at;
            var key = AtWord() ? ReadWord() : null;
            if (key == HitGroup)
            {
                var group = ReadHitGroup(at);
                if (group == null)
                {
                    return null;
                }

                if (groups.Any(other => other.Name == group.Name))
                {
                    _logger.Error($"The pass has two hit groups named '{group.Name}'.", group.Location);
                }

                groups.Add(group);
            }
            else if (key != null && StageKeys.TryGetValue(key, out var stage))
            {
                var entryPoint = ReadAssignedName(key);
                if (entryPoint == null)
                {
                    return null;
                }

                stages.Add(new EffectStageSource(stage, entryPoint, Where(at)));
            }
            else
            {
                Fail(at, key == null
                    ? $"'{Shown(at)}' where a stage, a hit group or the end of the pass was expected"
                    : $"'{key}' is not a stage of a pass: {string.Join(", ", StageKeys.Keys)} or {HitGroup}");
                return null;
            }
        }

        var pass = new EffectPassSource(name, Where(start), stages, groups);
        Check(pass);
        return pass;
    }

    private EffectHitGroupSource ReadHitGroup(int start)
    {
        SkipSpace();
        var nameAt = _at;
        if (!AtWord())
        {
            Fail(nameAt, "A hit group is named: HitGroup Name { ... }");
            return null;
        }

        var name = ReadWord();
        if (!Expect('{'))
        {
            return null;
        }

        var entryPoints = new Dictionary<string, string>();
        while (true)
        {
            SkipSpace();
            if (Peek() == '}')
            {
                _at++;
                break;
            }

            var at = _at;
            var key = AtWord() ? ReadWord() : null;
            if (key == null || !HitGroupKeys.Contains(key))
            {
                Fail(at, $"'{key ?? Shown(at)}' is not a shader of a hit group: {string.Join(", ", HitGroupKeys)}");
                return null;
            }

            var entryPoint = ReadAssignedName(key);
            if (entryPoint == null)
            {
                return null;
            }

            if (entryPoints.ContainsKey(key))
            {
                _logger.Error($"The hit group names its {key} twice.", Where(at));
            }

            entryPoints[key] = entryPoint;
        }

        if (entryPoints.Count == 0)
        {
            _logger.Error($"The hit group '{name}' names no shader.", Where(start));
        }

        entryPoints.TryGetValue(HitGroupKeys[0], out var closestHit);
        entryPoints.TryGetValue(HitGroupKeys[1], out var anyHit);
        entryPoints.TryGetValue(HitGroupKeys[2], out var intersection);
        return new EffectHitGroupSource(name, closestHit, anyHit, intersection, Where(start));
    }

    private string ReadAssignedName(string key)
    {
        if (!Expect('='))
        {
            return null;
        }

        SkipSpace();
        var at = _at;
        if (!AtWord())
        {
            Fail(at, $"An entry point's name after '{key} ='");
            return null;
        }

        var name = ReadWord();
        return Expect(';') ? name : null;
    }

    private void Check(EffectPassSource pass)
    {
        foreach (var twice in pass.Stages
                     .Where(stage => stage.Stage is not (EffectStage.Miss or EffectStage.Callable))
                     .GroupBy(stage => stage.Stage)
                     .Where(group => group.Count() > 1))
        {
            _logger.Error($"The pass names its {twice.Key} shader twice.", twice.ElementAt(1).Location);
        }

        var stages = new HashSet<EffectStage>(pass.Stages.Select(stage => stage.Stage));
        var rayTracing = pass.HitGroups.Count > 0 || stages.Overlaps([EffectStage.RayGeneration, EffectStage.Miss, EffectStage.Callable]);
        var mesh = stages.Overlaps([EffectStage.Task, EffectStage.Mesh]);
        var compute = stages.Contains(EffectStage.Compute);
        var graphics = stages.Overlaps([EffectStage.Vertex, EffectStage.Hull, EffectStage.Domain]);
        var kinds = new[] { (graphics, "graphics"), (mesh, "mesh"), (compute, "compute"), (rayTracing, "ray tracing") }
            .Where(kind => kind.Item1)
            .Select(kind => kind.Item2)
            .ToList();
        if (stages.Count == 0 && pass.HitGroups.Count == 0)
        {
            _logger.Error("The pass runs no shader.", pass.Location);
        }
        else if (kinds.Count > 1)
        {
            _logger.Error($"A pass runs one kind of pipeline, not {string.Join(" and ", kinds)}.", pass.Location);
        }
        else if (rayTracing)
        {
            if (pass.Stages.Count(stage => stage.Stage == EffectStage.RayGeneration) != 1)
            {
                _logger.Error("A ray-tracing pass has one ray generation shader.", pass.Location);
            }

            if (stages.Contains(EffectStage.Pixel))
            {
                _logger.Error("A ray-tracing pass has no pixel shader.", pass.Location);
            }
        }
        else if (compute)
        {
            if (stages.Contains(EffectStage.Pixel))
            {
                _logger.Error("A compute pass has no pixel shader.", pass.Location);
            }
        }
        else if (mesh)
        {
            if (!stages.Contains(EffectStage.Mesh))
            {
                _logger.Error("A mesh pass has a mesh shader.", pass.Location);
            }
        }
        else if (!stages.Contains(EffectStage.Vertex))
        {
            _logger.Error("A graphics pass has a vertex shader.", pass.Location);
        }
        else if (stages.Contains(EffectStage.Hull) != stages.Contains(EffectStage.Domain))
        {
            _logger.Error("Hull and domain shaders go together.", pass.Location);
        }
    }

    private bool Expect(char c)
    {
        SkipSpace();
        if (Peek() != c)
        {
            return Fail(_at, $"'{c}' expected, not '{Shown(_at)}'");
        }

        _at++;
        return true;
    }

    private bool Fail(int at, string message)
    {
        _logger.Error(message, Where(at));
        return false;
    }

    private SourceLocation Where(int index)
    {
        var line = _lineStarts.BinarySearch(index);
        if (line < 0)
        {
            line = ~line - 1;
        }

        return new SourceLocation(_filePath, line + 1, index - _lineStarts[line] + 1);
    }

    private string Shown(int at) => at < _text.Length ? _text[at].ToString() : "the end of the file";

    private char Peek() => _at < _text.Length ? _text[_at] : '\0';

    private bool AtWord() => _at < _text.Length && IsWordStart(_text[_at]);

    private string ReadWord()
    {
        var start = _at;
        while (_at < _text.Length && IsWordPart(_text[_at]))
        {
            _at++;
        }

        return _text.Substring(start, _at - start);
    }

    private void SkipSpace()
    {
        while (_at < _text.Length)
        {
            if (char.IsWhiteSpace(_text[_at]))
            {
                _at++;
            }
            else if (!SkipComment())
            {
                return;
            }
        }
    }

    private bool SkipComment()
    {
        if (_at + 1 >= _text.Length || _text[_at] != '/')
        {
            return false;
        }

        if (_text[_at + 1] == '/')
        {
            while (_at < _text.Length && _text[_at] != '\n')
            {
                _at++;
            }

            return true;
        }

        if (_text[_at + 1] == '*')
        {
            var end = _text.IndexOf("*/", _at + 2, System.StringComparison.Ordinal);
            _at = end < 0 ? _text.Length : end + 2;
            return true;
        }

        return false;
    }

    private void SkipQuoted(char quote)
    {
        _at++;
        while (_at < _text.Length && _text[_at] != quote && _text[_at] != '\n')
        {
            _at += _text[_at] == '\\' ? 2 : 1;
        }

        _at++;
    }

    private void SkipDirective()
    {
        while (_at < _text.Length && _text[_at] != '\n')
        {
            _at += _text[_at] == '\\' ? 2 : 1;
        }
    }

    private bool StartsALine(int index)
    {
        for (var i = index - 1; i >= 0 && _text[i] != '\n'; i--)
        {
            if (!char.IsWhiteSpace(_text[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void Blank(int start, int end)
    {
        for (var i = start; i < end && i < _code.Length; i++)
        {
            if (_code[i] is not ('\n' or '\r'))
            {
                _code[i] = ' ';
            }
        }
    }

    private static bool IsWordStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsWordPart(char c) => char.IsLetterOrDigit(c) || c == '_';
}
