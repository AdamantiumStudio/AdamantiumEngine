using System.Linq;

namespace Adamantium.Core.Shared;

/// <summary>What a generator makes of a file's folder: the namespace a view or a language table in it is generated
/// under.</summary>
public static class PathExtension
{
    /// <summary>The folder <paramref name="path"/> as a namespace: <c>Views/Main-Window</c> as <c>Views.Main_Window</c>,
    /// a segment starting with a digit given a leading underscore.</summary>
    public static string ToNamespace(this string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var cleanedPath = path.Replace('/', '.').Replace('\\', '.').Trim('.');
        cleanedPath = cleanedPath.Replace('-', '_').Replace(' ', '_');

        var segments = cleanedPath.Split('.')
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        for (var i = 0; i < segments.Count; i++)
        {
            if (char.IsDigit(segments[i][0]))
            {
                segments[i] = "_" + segments[i];
            }
        }

        return string.Join(".", segments);
    }
}
