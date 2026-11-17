using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.View3D;

/// <summary>When 3D artwork comes to life and goes back to its poster, and what a viewer shows round it (ISO 32000-1,
/// 13.6.3, table 301, <c>3DA</c>).</summary>
public sealed class Pdf3DActivation
{
    private const string ActivateKey = "A";
    private const string DeactivateKey = "D";
    private const string ToolbarKey = "TB";
    private const string NavigationKey = "NP";
    private const string PageOpened = "PO";
    private const string PageVisible = "PV";
    private const string DeactivateExplicitly = "XD";
    private const string PageInvisible = "PI";

    /// <summary>When it comes to life (<c>A</c>): when asked (<c>XA</c>, the default), when its page opens, or when the
    /// page comes into view.</summary>
    public Pdf3DActivationTime Activates { get; init; } = Pdf3DActivationTime.Explicitly;

    /// <summary>When it goes back to its poster (<c>D</c>): when asked, when its page closes (the default), or when the
    /// page leaves the view.</summary>
    public Pdf3DDeactivationTime Deactivates { get; init; } = Pdf3DDeactivationTime.PageClosed;

    /// <summary>Whether a viewer shows its toolbar while it is alive (<c>TB</c>); true by default.</summary>
    public bool ShowsToolbar { get; init; } = true;

    /// <summary>Whether a viewer shows the panel of its parts (<c>NP</c>); false by default.</summary>
    public bool ShowsNavigation { get; init; }

    /// <summary>The activation of a <c>3DA</c> dictionary, the format's defaults for none.</summary>
    public static Pdf3DActivation From(PdfObject value, IPdfObjectResolver resolver)
    {
        if (resolver.Resolve(value) is not PdfDictionary dictionary)
        {
            return new Pdf3DActivation();
        }

        return new Pdf3DActivation
        {
            Activates = PdfValues.Name(dictionary[ActivateKey], resolver) switch
            {
                PageOpened => Pdf3DActivationTime.PageOpened,
                PageVisible => Pdf3DActivationTime.PageVisible,
                _ => Pdf3DActivationTime.Explicitly,
            },
            Deactivates = PdfValues.Name(dictionary[DeactivateKey], resolver) switch
            {
                DeactivateExplicitly => Pdf3DDeactivationTime.Explicitly,
                PageInvisible => Pdf3DDeactivationTime.PageInvisible,
                _ => Pdf3DDeactivationTime.PageClosed,
            },
            ShowsToolbar = PdfValues.Boolean(dictionary[ToolbarKey], resolver, true),
            ShowsNavigation = PdfValues.Boolean(dictionary[NavigationKey], resolver),
        };
    }
}
