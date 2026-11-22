using System.Globalization;

namespace Adamantium.Localization;

/// <summary>How a language writes dates, times and numbers in this application, where it departs from the language's own
/// rules. Unset members keep the language's rules.</summary>
public sealed class LanguageFormat
{
    public string ShortDate { get; init; }

    public string LongDate { get; init; }

    public string ShortTime { get; init; }

    public string LongTime { get; init; }

    public string DecimalSeparator { get; init; }

    public string GroupSeparator { get; init; }

    public DayOfWeek? FirstDayOfWeek { get; init; }

    internal void ApplyTo(CultureInfo culture)
    {
        var dates = culture.DateTimeFormat;
        if (ShortDate != null)
        {
            dates.ShortDatePattern = ShortDate;
        }

        if (LongDate != null)
        {
            dates.LongDatePattern = LongDate;
        }

        if (ShortTime != null)
        {
            dates.ShortTimePattern = ShortTime;
        }

        if (LongTime != null)
        {
            dates.LongTimePattern = LongTime;
        }

        if (FirstDayOfWeek is { } firstDay)
        {
            dates.FirstDayOfWeek = firstDay;
        }

        var numbers = culture.NumberFormat;
        if (DecimalSeparator != null)
        {
            numbers.NumberDecimalSeparator = DecimalSeparator;
            numbers.CurrencyDecimalSeparator = DecimalSeparator;
            numbers.PercentDecimalSeparator = DecimalSeparator;
        }

        if (GroupSeparator != null)
        {
            numbers.NumberGroupSeparator = GroupSeparator;
            numbers.CurrencyGroupSeparator = GroupSeparator;
            numbers.PercentGroupSeparator = GroupSeparator;
        }
    }
}
