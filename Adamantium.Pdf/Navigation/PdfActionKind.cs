namespace Adamantium.Pdf.Navigation;

/// <summary>What an action does (ISO 32000-2, 12.6.4), of those a viewer follows.</summary>
public enum PdfActionKind
{
    /// <summary>One the viewer does not follow.</summary>
    Other,

    /// <summary>To a destination in this document (<c>/GoTo</c>).</summary>
    GoTo,

    /// <summary>To a destination in another file (<c>/GoToR</c>).</summary>
    GoToRemote,

    /// <summary>To an address, as a web page (<c>/URI</c>).</summary>
    Uri,

    /// <summary>Opens a file or an application (<c>/Launch</c>).</summary>
    Launch,

    /// <summary>A viewer's own step: next page, previous, first, last (<c>/Named</c>).</summary>
    Named,

    /// <summary>A script (<c>/JavaScript</c>), which a viewer may refuse to run.</summary>
    JavaScript,
}
