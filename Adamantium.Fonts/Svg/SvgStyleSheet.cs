using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Adamantium.Fonts.Svg;

// The rules of a document's <style> elements, as a CSS cascade applies them to its elements: simple selectors - a tag,
// *, .class, #id and their compounds, in comma lists - by specificity, the later of two alike winning. A rule whose
// selectors use combinators, pseudo-classes or attributes is left out, as are at-rules.
internal sealed class SvgStyleSheet
{
    private static readonly Regex Compound = new(@"^(?<tag>\*|[A-Za-z][\w-]*)?(?<part>[.#][\w-]+)*$");
    private static readonly Regex Important = new(@"!\s*important\s*$", RegexOptions.IgnoreCase);

    private readonly List<Rule> rules = [];

    private SvgStyleSheet()
    {
    }

    // The sheet of the document's style elements of CSS; null when they hold no rule.
    public static SvgStyleSheet Parse(XDocument document)
    {
        var sheet = new SvgStyleSheet();
        foreach (var style in document.Descendants().Where(element => element.Name.LocalName == "style"))
        {
            var type = ((string)style.Attribute("type"))?.Trim();
            if (type == null || type == "text/css")
            {
                sheet.Add(style.Value);
            }
        }

        return sheet.rules.Count > 0 ? sheet : null;
    }

    // What the rules set the element's property to: an !important declaration over the others, then the more specific,
    // then the later, one of a value the property does not take dropped; null when none sets it.
    public Declaration Property(XElement element, string name)
    {
        Declaration held = null;
        var heldSpecificity = (0, 0, 0);
        foreach (var rule in rules)
        {
            if (!rule.Selectors.Any(selector => selector.Matches(element)))
            {
                continue;
            }

            var specificity = rule.Selectors.Where(selector => selector.Matches(element)).Max(selector => selector.Specificity);
            foreach (var declaration in rule.Declarations)
            {
                if (!string.Equals(declaration.Name, name, StringComparison.OrdinalIgnoreCase)
                    || !SvgStyle.Valid(name, declaration.Value))
                {
                    continue;
                }

                if (held == null || (declaration.Important != held.Important
                        ? declaration.Important
                        : Compare(specificity, heldSpecificity) >= 0))
                {
                    held = declaration;
                    heldSpecificity = specificity;
                }
            }
        }

        return held;
    }

    // The declaration of a style that sets the property, as CSS picks among several: an !important one over the
    // others, the later of two alike; one of a value the property does not take is dropped.
    public static Declaration Pick(IEnumerable<Declaration> declarations, string name)
    {
        Declaration held = null;
        foreach (var declaration in declarations)
        {
            if (string.Equals(declaration.Name, name, StringComparison.OrdinalIgnoreCase)
                && SvgStyle.Valid(name, declaration.Value)
                && (held == null || declaration.Important || !held.Important))
            {
                held = declaration;
            }
        }

        return held;
    }

    // A style's declarations: name, value and whether it is !important.
    public static List<Declaration> Declarations(string block)
    {
        var declarations = new List<Declaration>();
        foreach (var text in Split(block, ';'))
        {
            var colon = text.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var value = text.Substring(colon + 1).Trim();
            var important = Important.Match(value);
            if (important.Success)
            {
                value = value.Substring(0, important.Index).Trim();
            }

            declarations.Add(new Declaration(text.Substring(0, colon).Trim(), value, important.Success));
        }

        return declarations;
    }

    private static int Compare((int Ids, int Classes, int Tags) a, (int Ids, int Classes, int Tags) b) =>
        a.Ids != b.Ids ? a.Ids.CompareTo(b.Ids)
        : a.Classes != b.Classes ? a.Classes.CompareTo(b.Classes)
        : a.Tags.CompareTo(b.Tags);

    private void Add(string css)
    {
        css = WithoutComments(css);
        var at = 0;
        while (at < css.Length)
        {
            while (at < css.Length && char.IsWhiteSpace(css[at]))
            {
                at++;
            }

            if (at >= css.Length)
            {
                break;
            }

            // A statement at-rule ends at its semicolon, a block - an at-rule's or a rule's - at its closing brace.
            var end = Next(css, at, '{', ';');
            if (end >= css.Length)
            {
                break;
            }

            if (css[end] == ';')
            {
                at = end + 1;
                continue;
            }

            var prelude = css.Substring(at, end - at).Trim();
            var close = Closing(css, end);
            if (!prelude.StartsWith("@", StringComparison.Ordinal))
            {
                var selectors = new List<Selector>();
                foreach (var text in prelude.Split(','))
                {
                    if (!Selector.TryParse(text.Trim(), out var selector))
                    {
                        selectors = null;
                        break;
                    }

                    selectors.Add(selector);
                }

                if (selectors is { Count: > 0 })
                {
                    rules.Add(new Rule(selectors, Declarations(css.Substring(end + 1, Math.Max(0, close - end - 1)))));
                }
            }

            at = close + 1;
        }
    }

    private static string WithoutComments(string css)
    {
        var text = new StringBuilder(css.Length);
        for (var i = 0; i < css.Length; i++)
        {
            if (css[i] is '"' or '\'')
            {
                var quote = css[i];
                var end = i + 1;
                while (end < css.Length && css[end] != quote)
                {
                    end += css[end] == '\\' ? 2 : 1;
                }

                end = Math.Min(end, css.Length - 1);
                text.Append(css, i, end - i + 1);
                i = end;
            }
            else if (css[i] == '/' && i + 1 < css.Length && css[i + 1] == '*')
            {
                var end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                text.Append(' ');
                i = end < 0 ? css.Length : end + 1;
            }
            else
            {
                text.Append(css[i]);
            }
        }

        return text.ToString();
    }

    // The first of the characters at or after `from` outside quotes and brackets; the text's length if none.
    private static int Next(string css, int from, char first, char second)
    {
        var depth = 0;
        for (var i = from; i < css.Length; i++)
        {
            var c = css[i];
            if (c is '"' or '\'')
            {
                i = SkipString(css, i);
            }
            else if (c is '(' or '[')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (depth == 0 && (c == first || c == second))
            {
                return i;
            }
        }

        return css.Length;
    }

    // The brace closing the block opened at `open`, past any nested in it and any in quotes; the end of the text if none.
    private static int Closing(string css, int open)
    {
        var depth = 0;
        for (var i = open; i < css.Length; i++)
        {
            if (css[i] is '"' or '\'')
            {
                i = SkipString(css, i);
            }
            else if (css[i] == '{')
            {
                depth++;
            }
            else if (css[i] == '}' && --depth == 0)
            {
                return i;
            }
        }

        return css.Length;
    }

    private static int SkipString(string css, int start)
    {
        var i = start + 1;
        while (i < css.Length && css[i] != css[start])
        {
            i += css[i] == '\\' ? 2 : 1;
        }

        return Math.Min(i, css.Length - 1);
    }

    // The text cut at the separator where it stands outside quotes and brackets.
    private static IEnumerable<string> Split(string text, char separator)
    {
        var from = 0;
        while (from <= text.Length)
        {
            var at = Next(text, from, separator, separator);
            yield return text.Substring(from, at - from);
            from = at + 1;
        }
    }

    internal sealed class Declaration(string name, string value, bool important)
    {
        public string Name { get; } = name;

        public string Value { get; } = value;

        public bool Important { get; } = important;
    }

    private sealed class Rule(List<Selector> selectors, List<Declaration> declarations)
    {
        public List<Selector> Selectors { get; } = selectors;

        public List<Declaration> Declarations { get; } = declarations;
    }

    private sealed class Selector(string tag, string id, int ids, string[] classes)
    {
        public (int Ids, int Classes, int Tags) Specificity { get; } = (ids, classes.Length, tag != null ? 1 : 0);

        // A compound selector; false for anything else, which leaves out the rule it is in.
        public static bool TryParse(string text, out Selector selector)
        {
            selector = null;
            var match = text.Length > 0 ? Compound.Match(text) : null;
            if (match is not { Success: true })
            {
                return false;
            }

            var tag = match.Groups["tag"].Success && match.Groups["tag"].Value != "*" ? match.Groups["tag"].Value : null;
            string id = null;
            var ids = 0;
            var classes = new List<string>();
            foreach (Capture part in match.Groups["part"].Captures)
            {
                var name = part.Value.Substring(1);
                if (part.Value[0] != '#')
                {
                    classes.Add(name);
                    continue;
                }

                // Two ids no element has both of: a selector that matches nothing.
                id = id == null || id == name ? name : "\0";
                ids++;
            }

            selector = new Selector(tag, id, ids, classes.ToArray());
            return true;
        }

        public bool Matches(XElement element)
        {
            if ((tag != null && element.Name.LocalName != tag) || (id != null && (string)element.Attribute("id") != id))
            {
                return false;
            }

            if (classes.Length == 0)
            {
                return true;
            }

            var own = ((string)element.Attribute("class"))?.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            return own != null && classes.All(own.Contains);
        }
    }
}
