using Adamantium.Engine.EntityServices;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Tools;

/// <summary>
/// Outlines what is selected - all of it, round its joint silhouette - together with everything under it, so the part of a model being changed stands apart from
/// the rest; and, in another color, what a click would select under the pointer, the icon it shows by, if any, in a
/// color of its own. Works with whatever tool is in use.
/// </summary>
public class SelectionOutline : EditorProcessor
{
    public Vector4F Color { get; set; } = Colors.Orange.ToVector4();

    public Vector4F HoverColor { get; set; } = Colors.White.ToVector4();

    /// <summary>The outline of an icon under the pointer: icons are white, and a white band would blur into them.</summary>
    public Vector4F IconHoverColor { get; set; } = Colors.DeepSkyBlue.ToVector4();

    /// <summary>How wide the outline is, in points: pixels at 100% scale.</summary>
    public float Pixels { get; set; } = 2;

    public override void DrawOverlay(EditorOverlayProcessor overlay)
    {
        var selection = Tools.Selection;
        if (Tools.Hovered is { } hovered && !selection.Contains(hovered))
        {
            overlay.DrawOutline(hovered, HoverColor, Pixels);
            if (Find<EntityIcons>()?.IconOf(hovered) is { } icon)
            {
                overlay.DrawOutlineAlone(icon, IconHoverColor, Pixels);
            }
        }

        var selected = selection.Items;
        if (selected.Count > 0)
        {
            overlay.DrawOutline(selected, Color, Pixels);
        }
    }
}
