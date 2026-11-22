namespace Adamantium.Localization;

/// <summary>A phrase's argument written by a format of its own - a binding's StringFormat - in the culture the phrase
/// is written in, while its value still chooses the phrase's case. A format the phrase gives its placeholder (a
/// translator's {size:N2}) is applied to the value first, and this one around it.</summary>
public sealed class FormattedArgument(object value, string format) : IFormattable
{
    /// <summary>The value itself: what chooses the case.</summary>
    public object Value { get; } = value;

    public string ToString(string placeholderFormat, IFormatProvider provider)
    {
        var written = !string.IsNullOrEmpty(placeholderFormat) && Value is IFormattable formattable
            ? formattable.ToString(placeholderFormat, provider)
            : Value;
        return string.Format(provider, format, written);
    }

    public override string ToString() => ToString(null, System.Globalization.CultureInfo.CurrentCulture);
}
