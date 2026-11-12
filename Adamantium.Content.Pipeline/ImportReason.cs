namespace Adamantium.Content.Pipeline;

/// <summary>Why an asset was imported into its asset file.</summary>
public enum ImportReason
{
    /// <summary>It had no asset file yet.</summary>
    New,

    /// <summary>Its source was written since.</summary>
    SourceChanged,

    /// <summary>Its importer or settings are not the ones its asset file was made with.</summary>
    SettingsChanged,

    /// <summary>Its asset file could not be read.</summary>
    Unreadable,

    /// <summary>It was asked for, up to date or not.</summary>
    Asked
}
