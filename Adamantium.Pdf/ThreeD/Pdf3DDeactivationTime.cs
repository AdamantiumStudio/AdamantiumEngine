namespace Adamantium.Pdf.ThreeD;

/// <summary>When 3D artwork goes back to its poster.</summary>
public enum Pdf3DDeactivationTime
{
    /// <summary>When the reader asks for it.</summary>
    Explicitly,

    /// <summary>When its page is closed.</summary>
    PageClosed,

    /// <summary>When its page leaves the view.</summary>
    PageInvisible,
}
