using System.Collections.Generic;
using System.Linq;

namespace Adamantium.EffectsCompiler;

/// <summary>An effect file read: its techniques, what in them breaks the effect language, and its code for Slang - the
/// file with every technique blanked out, each character but line breaks a space, so what Slang reports stands on the
/// file's own lines and columns. The language and how a broken technique is skipped are <see cref="EffectParser"/>'s.
/// </summary>
public sealed class EffectSource
{
    private EffectSource(IReadOnlyList<EffectTechniqueSource> techniques, string code,
        IReadOnlyList<EffectCompilerMessage> errors)
    {
        Techniques = techniques;
        Code = code;
        Errors = errors;
    }

    /// <summary>The techniques in the order the file has them.</summary>
    public IReadOnlyList<EffectTechniqueSource> Techniques { get; }

    /// <summary>The file with its techniques blanked out.</summary>
    public string Code { get; }

    /// <summary>What breaks the effect language, in the order the file has it.</summary>
    public IReadOnlyList<EffectCompilerMessage> Errors { get; }

    /// <summary>Reads <paramref name="text"/>, the effect file at <paramref name="filePath"/> - the path its errors
    /// stand in.</summary>
    public static EffectSource Read(string text, string filePath)
    {
        text ??= string.Empty;
        var parser = new EffectParser(text, filePath);
        parser.ParseFile();
        var errors = parser.Errors.OrderBy(error => error.Location.Line).ThenBy(error => error.Location.Column).ToList();
        return new EffectSource(parser.Techniques, BlankTechniques(text, parser.TechniqueSpans), errors);
    }

    private static string BlankTechniques(string text, IReadOnlyList<(int Start, int End)> techniqueSpans)
    {
        var code = text.ToCharArray();
        foreach (var (start, end) in techniqueSpans)
        {
            for (var i = start; i < end; i++)
            {
                if (code[i] is not ('\n' or '\r'))
                {
                    code[i] = ' ';
                }
            }
        }

        return new string(code);
    }
}
