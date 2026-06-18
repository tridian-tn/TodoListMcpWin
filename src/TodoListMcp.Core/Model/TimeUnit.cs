using System.Globalization;

namespace TodoListMcp.Core.Model;

/// <summary>
/// A ToDoList effort unit (for TIMEESTIMATE/TIMESPENT). The file stores each as a single
/// letter; ToDoList does not use seconds for these fields. Note the gotcha: minutes is
/// <c>I</c> and months is <c>M</c>.
/// </summary>
public enum TimeUnit
{
    Minutes,   // I
    Hours,     // H  (ToDoList's default when no unit is stored)
    Days,      // D
    Weekdays,  // K
    Weeks,     // W
    Months,    // M
    Years,     // Y
}

/// <summary>Parsing, file-code mapping and hour-normalisation for <see cref="TimeUnit"/>.</summary>
public static class TimeUnits
{
    /// <summary>The single-letter code ToDoList writes to *UNITS attributes.</summary>
    public static char ToFileCode(TimeUnit unit) => unit switch
    {
        TimeUnit.Minutes => 'I',
        TimeUnit.Hours => 'H',
        TimeUnit.Days => 'D',
        TimeUnit.Weekdays => 'K',
        TimeUnit.Weeks => 'W',
        TimeUnit.Months => 'M',
        TimeUnit.Years => 'Y',
        _ => 'H',
    };

    /// <summary>The lowercase word surfaced on the read model, e.g. "hours".</summary>
    public static string ToWord(TimeUnit unit) => unit.ToString().ToLowerInvariant();

    /// <summary>Maps a stored file code back to a unit; null for anything unrecognised.</summary>
    public static TimeUnit? FromFileCode(char code) => char.ToUpperInvariant(code) switch
    {
        'I' => TimeUnit.Minutes,
        'H' => TimeUnit.Hours,
        'D' => TimeUnit.Days,
        'K' => TimeUnit.Weekdays,
        'W' => TimeUnit.Weeks,
        'M' => TimeUnit.Months,
        'Y' => TimeUnit.Years,
        _ => null,
    };

    /// <summary>
    /// Parses a unit from a single-letter code (I/H/D/K/W/M/Y) or a friendly word, case-insensitively.
    /// Seconds are intentionally rejected — ToDoList does not store them for estimate/spent.
    /// </summary>
    public static bool TryParse(string? text, out TimeUnit unit)
    {
        unit = TimeUnit.Hours;
        if (string.IsNullOrWhiteSpace(text)) return false;
        switch (text.Trim().ToLowerInvariant())
        {
            case "i": case "min": case "mins": case "minute": case "minutes": unit = TimeUnit.Minutes; return true;
            case "h": case "hr": case "hrs": case "hour": case "hours": unit = TimeUnit.Hours; return true;
            case "d": case "day": case "days": unit = TimeUnit.Days; return true;
            case "k": case "weekday": case "weekdays": unit = TimeUnit.Weekdays; return true;
            case "w": case "wk": case "wks": case "week": case "weeks": unit = TimeUnit.Weeks; return true;
            case "m": case "mo": case "mon": case "mth": case "mths": case "month": case "months": unit = TimeUnit.Months; return true;
            case "y": case "yr": case "yrs": case "year": case "years": unit = TimeUnit.Years; return true;
            default: return false;
        }
    }

    /// <summary>
    /// Converts a value to hours using ToDoList's default working week (8h day, 5-day week,
    /// 4.348 weeks/month, 12 months/year). A custom working week isn't stored per task, so this
    /// is a fixed convention used only for comparing mixed-unit values in search.
    /// </summary>
    public static double ToHours(double value, TimeUnit unit) => value * (unit switch
    {
        TimeUnit.Minutes => 1.0 / 60.0,
        TimeUnit.Hours => 1.0,
        TimeUnit.Days => 8.0,
        TimeUnit.Weekdays => 8.0,
        TimeUnit.Weeks => 40.0,        // 5 working days
        TimeUnit.Months => 173.92,     // 4.348 weeks * 40h
        TimeUnit.Years => 2087.04,     // 12 months
        _ => 1.0,
    });

    /// <summary>Formats a value the way ToDoList writes time amounts (plain decimal, 8 places).</summary>
    public static string Format(double value) =>
        value.ToString("0.00000000", CultureInfo.InvariantCulture);
}
