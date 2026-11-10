using System.Collections.Generic;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;
using Keys = Adamantium.Pdf.Graphics.PdfOptionalContentKeys;

namespace Adamantium.Pdf.Graphics;

/// <summary>Which optional content of a document is shown (ISO 32000-2, 8.11): its groups' states by the default
/// configuration - the base state, then <c>/ON</c> and <c>/OFF</c>, then the usage the configuration applies for the
/// view or for print - and what a group or membership dictionary named by <c>/OC</c> makes of them. A document without
/// optional content shows everything. Visibility expressions are walked on an explicit stack, no deeper than
/// <see cref="MaxExpressionDepth"/>; one nested deeper is taken as visible.</summary>
public sealed class PdfOptionalContent
{
    public const int MaxExpressionDepth = 64;

    private const int MaxExpressionItems = 100_000;

    private readonly PdfDocument _document;
    private readonly HashSet<PdfDictionary> _off = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfDictionary, bool> _memberships = new(ReferenceEqualityComparer.Instance);

    private PdfOptionalContent(PdfDocument document)
    {
        _document = document;
    }

    /// <summary>Whether the document has optional content at all.</summary>
    public bool IsPresent { get; private set; }

    public static PdfOptionalContent Of(PdfDocument document, PdfOptionalContentUse use)
    {
        var content = new PdfOptionalContent(document);
        if (document.Resolve(document.Catalog?[Keys.OCProperties]) is not PdfDictionary properties)
        {
            return content;
        }

        content.IsPresent = true;
        var groups = Dictionaries(properties[Keys.OCGs], document);
        var configuration = document.Resolve(properties[Keys.D]) as PdfDictionary ?? new PdfDictionary();
        if (PdfValues.Name(configuration[Keys.BaseState], document) == Keys.OFF)
        {
            foreach (var group in groups)
            {
                content._off.Add(group);
            }
        }

        foreach (var group in Dictionaries(configuration[Keys.ON], document))
        {
            content._off.Remove(group);
        }

        foreach (var group in Dictionaries(configuration[Keys.OFF], document))
        {
            content._off.Add(group);
        }

        content.ApplyUsage(configuration, use);
        return content;
    }

    /// <summary>Whether content marked with <paramref name="membership"/> is shown: a group, a membership dictionary, or
    /// null for content no group governs.</summary>
    public bool IsVisible(PdfObject membership)
    {
        if (!IsPresent || _document.Resolve(membership) is not PdfDictionary dictionary)
        {
            return true;
        }

        if (PdfValues.Name(dictionary[PdfKeys.Type], _document) != Keys.OCMD)
        {
            return !_off.Contains(dictionary);
        }

        if (!_memberships.TryGetValue(dictionary, out var visible))
        {
            visible = Membership(dictionary);
            _memberships[dictionary] = visible;
        }

        return visible;
    }

    private void ApplyUsage(PdfDictionary configuration, PdfOptionalContentUse use)
    {
        var (eventName, category, state) = use == PdfOptionalContentUse.View
            ? (Keys.View, Keys.View, Keys.ViewState)
            : (Keys.Print, Keys.Print, Keys.PrintState);
        if (_document.Resolve(configuration[Keys.AS]) is not PdfArray applications)
        {
            return;
        }

        foreach (var item in applications)
        {
            if (_document.Resolve(item) is not PdfDictionary application ||
                PdfValues.Name(application[Keys.Event], _document) != eventName ||
                !Names(application[Keys.Category]).Contains(category))
            {
                continue;
            }

            foreach (var group in Dictionaries(application[Keys.OCGs], _document))
            {
                var usage = _document.Resolve(group[Keys.Usage]) as PdfDictionary;
                var said = _document.Resolve(usage?[category]) as PdfDictionary;
                switch (PdfValues.Name(said?[state], _document))
                {
                    case Keys.ON:
                        _off.Remove(group);
                        break;
                    case Keys.OFF:
                        _off.Add(group);
                        break;
                }
            }
        }
    }

    private bool Membership(PdfDictionary membership)
    {
        if (_document.Resolve(membership[Keys.VE]) is PdfArray expression)
        {
            return Evaluate(expression);
        }

        var groups = Dictionaries(membership[Keys.OCGs], _document);
        if (groups.Count == 0)
        {
            return true;
        }

        var on = 0;
        foreach (var group in groups)
        {
            if (!_off.Contains(group))
            {
                on++;
            }
        }

        return PdfValues.Name(membership[Keys.P], _document) switch
        {
            Keys.AllOn => on == groups.Count,
            Keys.AnyOff => on < groups.Count,
            Keys.AllOff => on == 0,
            _ => on > 0,
        };
    }

    // An expression is [/And e...], [/Or e...] or [/Not e], each e a group or an expression; one that holds itself
    // ends at the depth limit, and the items read at all are capped.
    private bool Evaluate(PdfArray root)
    {
        var frames = new Stack<PdfVisibilityFrame>();
        var items = 0;
        bool? result = null;
        if (!Open(root, frames))
        {
            return true;
        }

        while (frames.Count > 0)
        {
            var frame = frames.Pop();
            if (result is { } operand)
            {
                frame.Take(operand);
                result = null;
            }

            if (frame.Next >= frame.Expression.Count || frame.Decided || ++items > MaxExpressionItems)
            {
                result = frame.Result;
                continue;
            }

            var item = _document.Resolve(frame.Expression[frame.Next++]);
            frames.Push(frame);
            if (item is PdfArray nested)
            {
                if (frames.Count >= MaxExpressionDepth || !Open(nested, frames))
                {
                    frame.Take(true);
                }
            }
            else
            {
                frame.Take(item is not PdfDictionary group || !_off.Contains(group));
            }
        }

        return result ?? true;
    }

    private bool Open(PdfArray expression, Stack<PdfVisibilityFrame> frames)
    {
        if (expression.Count == 0)
        {
            return false;
        }

        var name = PdfValues.Name(expression[0], _document);
        if (name is not (Keys.And or Keys.Or or Keys.Not))
        {
            return false;
        }

        frames.Push(new PdfVisibilityFrame(expression, name));
        return true;
    }

    private List<string> Names(PdfObject value)
    {
        var names = new List<string>();
        if (_document.Resolve(value) is PdfArray array)
        {
            foreach (var item in array)
            {
                if (PdfValues.Name(item, _document) is { } name)
                {
                    names.Add(name);
                }
            }
        }
        else if (PdfValues.Name(value, _document) is { } single)
        {
            names.Add(single);
        }

        return names;
    }

    private static List<PdfDictionary> Dictionaries(PdfObject value, PdfDocument document)
    {
        var dictionaries = new List<PdfDictionary>();
        switch (document.Resolve(value))
        {
            case PdfArray array:
                foreach (var item in array)
                {
                    if (document.Resolve(item) is PdfDictionary dictionary)
                    {
                        dictionaries.Add(dictionary);
                    }
                }

                break;
            case PdfDictionary single:
                dictionaries.Add(single);
                break;
        }

        return dictionaries;
    }
}
