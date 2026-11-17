namespace Adamantium.Pdf.View3D;

/// <summary>When 3D artwork comes to life.</summary>
public enum Pdf3DActivationTime
{
    /// <summary>When the reader asks for it - a click on the poster.</summary>
    Explicitly,

    /// <summary>When its page is opened.</summary>
    PageOpened,

    /// <summary>When its page comes into view.</summary>
    PageVisible,
}
