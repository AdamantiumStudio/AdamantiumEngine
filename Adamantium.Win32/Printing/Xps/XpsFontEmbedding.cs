namespace Adamantium.Win32.Printing.Xps;

/// <summary>How a font is embedded (XPS_FONT_EMBEDDING): obfuscated, and restricted as a font's license allows it only
/// to be previewed and printed.</summary>
public enum XpsFontEmbedding
{
    Normal = 1,
    Obfuscated = 2,
    Restricted = 3,
    RestrictedUnobfuscated = 4,
}
