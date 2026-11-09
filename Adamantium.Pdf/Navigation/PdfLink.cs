using System.Collections.Generic;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Text;

namespace Adamantium.Pdf.Navigation;

/// <summary>A link on a page (ISO 32000-2, 12.5.6.5): the area that follows it, and what following it does.</summary>
public sealed class PdfLink
{
    internal PdfLink(PdfRectangle rect, IReadOnlyList<PdfQuad> quads, PdfAction action)
    {
        Rect = rect;
        Quads = quads;
        Action = action;
    }

    /// <summary>The area, in default user space.</summary>
    public PdfRectangle Rect { get; }

    /// <summary>The quadrilaterals inside <see cref="Rect"/> that are the link when it is given them - a link over
    /// text that breaks across lines; empty otherwise.</summary>
    public IReadOnlyList<PdfQuad> Quads { get; }

    /// <summary>What following it does; null for a link that goes nowhere.</summary>
    public PdfAction Action { get; }

    /// <summary>Whether <paramref name="point"/> follows the link.</summary>
    public bool Contains(PdfPoint point)
    {
        if (point.X < Rect.Left || point.X > Rect.Right || point.Y < Rect.Bottom || point.Y > Rect.Top)
        {
            return false;
        }

        if (Quads.Count == 0)
        {
            return true;
        }

        foreach (var quad in Quads)
        {
            if (quad.Contains(point))
            {
                return true;
            }
        }

        return false;
    }
}
