namespace Adamantium.Content.Pipeline;

/// <summary>What kind of value an import setting takes.</summary>
public enum ImportSettingKind
{
    /// <summary>On or off: <c>true</c> or <c>false</c>.</summary>
    Toggle,

    /// <summary>A number, written in the invariant culture.</summary>
    Number,

    /// <summary>One of the setting's <see cref="ImportSetting.Choices"/>, by its value.</summary>
    Choice
}
