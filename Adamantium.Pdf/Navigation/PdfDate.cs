using System;
using System.Globalization;

namespace Adamantium.Pdf.Navigation;

/// <summary>PDF dates (ISO 32000-2, 7.9.4): D:YYYYMMDDHHmmSS and the offset from UTC, each part after the year
/// optional - reading stops at the first part that is not there. A date written the ISO 8601 way, as some files do,
/// is read that way.</summary>
public static class PdfDate
{
    /// <summary><paramref name="time"/> as a PDF date: D:YYYYMMDDHHmmSS and the offset from UTC.</summary>
    public static string Format(DateTimeOffset time)
    {
        var offset = time.Offset;
        var sign = offset < TimeSpan.Zero ? '-' : offset == TimeSpan.Zero ? 'Z' : '+';
        var text = "D:" + time.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + sign;
        return sign == 'Z' ? text : text + $"{Math.Abs(offset.Hours):D2}'{Math.Abs(offset.Minutes):D2}";
    }

    /// <summary>The date <paramref name="text"/> writes; null when it writes none.</summary>
    public static DateTimeOffset? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var s = text.Trim();
        if (s.StartsWith("D:", StringComparison.Ordinal))
        {
            s = s[2..];
        }
        else if (s.Length >= 10 && s[4] == '-' && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                     DateTimeStyles.AssumeUniversal, out var iso))
        {
            return iso;
        }

        var at = 0;
        var stopped = false;
        int Read(int digits, int fallback)
        {
            if (stopped || at + digits > s.Length || !int.TryParse(s.AsSpan(at, digits), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var value))
            {
                stopped = true;
                return fallback;
            }

            at += digits;
            return value;
        }

        var year = Read(4, -1);
        if (year < 1)
        {
            return null;
        }

        var month = Read(2, 1);
        var day = Read(2, 1);
        var hour = Read(2, 0);
        var minute = Read(2, 0);
        var second = Read(2, 0);
        var offset = TimeSpan.Zero;
        if (!stopped && at < s.Length && s[at] is '+' or '-')
        {
            var sign = s[at++] == '-' ? -1 : 1;
            var offsetHours = Read(2, 0);
            if (at < s.Length && s[at] == '\'')
            {
                at++;
            }

            var offsetMinutes = Read(2, 0);
            offset = TimeSpan.FromMinutes(sign * (Math.Min(offsetHours, 14) * 60 + Math.Min(offsetMinutes, 59)));
        }

        month = Math.Clamp(month, 1, 12);
        var local = new DateTime(year, month, Math.Clamp(day, 1, DateTime.DaysInMonth(year, month)),
            Math.Min(hour, 23), Math.Min(minute, 59), Math.Min(second, 59));
        try
        {
            return new DateTimeOffset(local, offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new DateTimeOffset(local, TimeSpan.Zero);
        }
    }
}
