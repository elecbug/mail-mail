using System.Globalization;
using System.Text.RegularExpressions;

public static class RFC2822TimeConverter
{
    // Try-parse entry: returns false if parsing fails
    public static bool TryParseRfc2822Like(string input, out DateTimeOffset dto)
    {
        dto = default;
        if (string.IsNullOrWhiteSpace(input)) return false;

        // 1) Drop trailing parenthetical zone like "(UTC)" or "(KST)"
        string cleaned = Regex.Replace(input.Trim(), @"\s*\([^)]+\)\s*$", "", RegexOptions.CultureInvariant);

        // 1-ADD) Normalize textual zero-offset zones (GMT/UTC/UT) and 'Z' to '+00:00'
        // This lets us keep using the "zzz" format without adding many literal patterns.
        cleaned = Regex.Replace(cleaned, @"\b(?:GMT|UTC|UT)\b", "+00:00", RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, @"(?<=\d)\s*Z\b", " +00:00", RegexOptions.CultureInvariant); // e.g., "... 15:00:45Z" -> "... 15:00:45 +00:00"

        // 2) Normalize "+0000" -> "+00:00" (colon is required by .NET for offsets)
        cleaned = Regex.Replace(cleaned, @"([+-]\d{2})(\d{2})(?=\s|$)", "$1:$2", RegexOptions.CultureInvariant);

        // 3) Try parse with common patterns
        string[] formats =
        {
            "ddd, dd MMM yyyy HH':'mm':'ss zzz",
            "dd MMM yyyy HH':'mm':'ss zzz",
            "ddd, dd MMM yyyy HH':'mm zzz",
            "dd MMM yyyy HH':'mm zzz"
        };

        return DateTimeOffset.TryParseExact(
            cleaned,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out dto
        );
    }

    public static DateTimeOffset ConvertToTimeZone(DateTimeOffset source, string timeZoneId)
    {
        var tz = FindTimeZoneSmart(timeZoneId);
        var localInTz = TimeZoneInfo.ConvertTimeFromUtc(source.UtcDateTime, tz);
        var offset = tz.GetUtcOffset(localInTz);
        return new DateTimeOffset(localInTz, offset);
    }

    public static string ToIsoUtc(DateTimeOffset dto) => dto.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
    public static string ToReadableWithOffset(DateTimeOffset dto) => dto.ToString("yyyy-MM-dd HH:mm:ss zzz");

    private static TimeZoneInfo FindTimeZoneSmart(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { /* fallthrough */ }

        var map = new (string win, string iana)[]
        {
            ("Korea Standard Time", "Asia/Seoul"),
            ("Tokyo Standard Time", "Asia/Tokyo"),
            ("China Standard Time", "Asia/Shanghai"),
            ("Eastern Standard Time", "America/New_York"),
            ("Pacific Standard Time", "America/Los_Angeles"),
            ("GMT Standard Time", "Europe/London"),
            ("UTC", "UTC")
        };

        foreach (var (win, iana) in map)
        {
            if (id.Equals(win, StringComparison.OrdinalIgnoreCase))
                try { return TimeZoneInfo.FindSystemTimeZoneById(iana); } catch { }
            if (id.Equals(iana, StringComparison.OrdinalIgnoreCase))
                try { return TimeZoneInfo.FindSystemTimeZoneById(win); } catch { }
        }

        throw new TimeZoneNotFoundException($"Time zone '{id}' was not found on this system.");
    }
}