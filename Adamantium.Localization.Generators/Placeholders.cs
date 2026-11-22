using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Adamantium.Localization.Generators;

internal static class Placeholders
{
    /// <summary>The placeholders of a phrase in the order they first appear: of its text, or of all its cases and the one
    /// whose value picks the case, which a case need not show.</summary>
    public static bool TryRead(LanguageEntry entry, out List<string> names, out string error)
    {
        names = new List<string>();
        var read = new List<string>();
        foreach (var text in entry.Texts)
        {
            if (!TryRead(text, read, out error))
            {
                return false;
            }

            foreach (var name in read)
            {
                if (!names.Contains(name))
                {
                    names.Add(name);
                }
            }
        }

        if (entry.Chooser != null && !names.Contains(entry.Chooser))
        {
            names.Add(entry.Chooser);
        }

        error = null;
        return true;
    }

    public static bool TryRead(string text, List<string> names, out string error)
    {
        names.Clear();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                if (i + 1 < text.Length && text[i + 1] == '{')
                {
                    i++;
                    continue;
                }

                var end = text.IndexOf('}', i + 1);
                if (end < 0)
                {
                    error = LanguageMessages.BraceNotClosed();
                    return false;
                }

                var name = NameOf(text.Substring(i + 1, end - i - 1));
                if (!SyntaxFacts.IsValidIdentifier(name))
                {
                    error = LanguageMessages.PlaceholderNoName(text.Substring(i + 1, end - i - 1));
                    return false;
                }

                if (!names.Contains(name))
                {
                    names.Add(name);
                }

                i = end;
            }
            else if (text[i] == '}')
            {
                if (i + 1 < text.Length && text[i + 1] == '}')
                {
                    i++;
                    continue;
                }

                error = LanguageMessages.BraceNotOpened();
                return false;
            }
        }

        error = null;
        return true;
    }

    public static string ToIndexed(string text, List<string> order)
    {
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if ((c == '{' || c == '}') && i + 1 < text.Length && text[i + 1] == c)
            {
                result.Append(c).Append(c);
                i++;
                continue;
            }

            if (c != '{')
            {
                result.Append(c);
                continue;
            }

            var end = text.IndexOf('}', i + 1);
            var inside = text.Substring(i + 1, end - i - 1);
            var name = NameOf(inside);
            var rest = inside.Substring(inside.IndexOf(name, System.StringComparison.Ordinal) + name.Length);
            result.Append('{').Append(order.IndexOf(name)).Append(rest).Append('}');
            i = end;
        }

        return result.ToString();
    }

    public static string Unescape(string text) => text.Replace("{{", "{").Replace("}}", "}");

    public static string ParameterName(string name) =>
        SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;

    private static string NameOf(string inside)
    {
        var cut = inside.IndexOfAny([',', ':']);
        return (cut < 0 ? inside : inside.Substring(0, cut)).Trim();
    }
}
