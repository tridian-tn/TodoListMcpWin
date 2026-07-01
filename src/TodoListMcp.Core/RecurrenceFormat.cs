using System.Globalization;
using System.Xml.Linq;
using TodoListMcp.Core.Model;

namespace TodoListMcp.Core;

/// <summary>
/// Decodes a ToDoList &lt;RECURRENCE&gt; element into a <see cref="TaskRecurrence"/>. The rule is three
/// integers — RECURFREQ (the frequency enum), RECURSPECIFIC1 and RECURSPECIFIC2 (a per-frequency
/// payload) — plus RECURFROM / RECURREUSE / RECURNUM / RECURREMAINING / RECURPRESERVECOMMENTS.
///
/// All constants and the per-frequency interpretation are verified against the
/// abstractspoon/ToDoList_9.2 source (Core/Interfaces/IEnums.h, Core/Shared/Recurrence.h,
/// Core/Shared/DateHelperEnums.h, Core/ToDoList/TaskFile.cpp); see <c>docs/recurrence-spike.md</c>.
/// This is read-only: it tolerates deprecated and unknown frequencies rather than authoring anything.
/// </summary>
public static class RecurrenceFormat
{
    // RECURFREQ values: ordinals of enum TDC_REGULARITY (IEnums.h). These are stored verbatim, not
    // as bit-flags — the aliases collapse onto the first four, then the "new options" run from 5.
    private const int FreqOnce = 0;
    private const int FreqEveryNDays = 1;                    // TDIR_DAILY / TDIR_DAY_EVERY_NDAYS
    private const int FreqWeeklyOnDays = 2;                  // TDIR_WEEKLY / TDIR_WEEK_SPECIFIC_DOWS_NWEEKS
    private const int FreqMonthlyOnDay = 3;                  // TDIR_MONTHLY / TDIR_MONTH_SPECIFIC_DAY_NMONTHS
    private const int FreqYearlyOnDate = 4;                  // TDIR_YEARLY / TDIR_YEAR_SPECIFIC_DAY_MONTHS
    private const int FreqEveryWeekday = 5;                  // TDIR_DAY_EVERY_WEEKDAY
    private const int FreqMonthlyByWeekday = 8;              // TDIR_MONTH_SPECIFIC_DOW_NMONTHS
    private const int FreqYearlyByWeekday = 10;              // TDIR_YEAR_SPECIFIC_DOW_MONTHS
    private const int FreqEveryNWeeks = 12;                  // TDIR_WEEK_EVERY_NWEEKS
    private const int FreqEveryNMonths = 13;                 // TDIR_MONTH_EVERY_NMONTHS
    private const int FreqEveryNYears = 14;                  // TDIR_YEAR_EVERY_NYEARS
    private const int FreqMonthlyFirstLastWeekday = 15;      // TDIR_MONTH_FIRSTLASTWEEKDAY_NMONTHS
    private const int FreqEveryNWeekdays = 16;               // TDIR_DAY_EVERY_NWEEKDAYS
    // 6, 7, 9, 11 are deprecated frequencies — surfaced as "unsupported", never authored.

    // Occurrence counts use -1 for "infinite".
    private const int OccursInfinitely = -1;

    // DH_DAYOFWEEK bit flags (DateHelperEnums.h), the weekly RECURSPECIFIC2 payload.
    private static readonly (int Bit, string Name)[] WeekdayBits =
    {
        (0x01, "Sunday"), (0x02, "Monday"), (0x04, "Tuesday"), (0x08, "Wednesday"),
        (0x10, "Thursday"), (0x20, "Friday"), (0x40, "Saturday"),
    };

    // DH_MONTH bit flags (DateHelperEnums.h) start at 0x10 so they can't be confused with a plain
    // month index (1–12); the yearly month slot carries either form.
    private static readonly (int Bit, string Name)[] MonthBits =
    {
        (0x0010, "January"), (0x0020, "February"), (0x0040, "March"), (0x0080, "April"),
        (0x0100, "May"), (0x0200, "June"), (0x0400, "July"), (0x0800, "August"),
        (0x1000, "September"), (0x2000, "October"), (0x4000, "November"), (0x8000, "December"),
    };

    private static readonly string[] MonthNames =
    {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    };

    // OLE_DAYOFWEEK: Sunday = 1 .. Saturday = 7 (DateHelperEnums.h), the HIWORD of the by-weekday spec.
    private static readonly string[] OleDayNames =
    {
        "", "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday",
    };

    // The LOWORD of the by-weekday spec: 1–4 are ordinals, 5 is "last".
    private static readonly string[] WhichNames = { "", "first", "second", "third", "fourth", "last" };

    /// <summary>
    /// Decodes the &lt;RECURRENCE&gt; child of a task element, or null when the task has no recurrence
    /// element or is set to "once" (not recurring).
    /// </summary>
    public static TaskRecurrence? Read(XElement task)
    {
        var e = task.Element("RECURRENCE");
        if (e is null) return null;

        var freq = ReadInt(e, "RECURFREQ", FreqOnce);
        if (freq is FreqOnce or < 0) return null;   // not recurring (TDIR_ONCE / TDIR_NONE)

        var spec1 = ReadInt(e, "RECURSPECIFIC1", 0);
        var spec2 = ReadInt(e, "RECURSPECIFIC2", 0);

        var (pattern, description, fields) = Decode(freq, spec1, spec2);

        return new TaskRecurrence
        {
            Pattern = pattern,
            Description = description,
            Interval = fields.Interval,
            DaysOfWeek = fields.DaysOfWeek,
            DayOfMonth = fields.DayOfMonth,
            Months = fields.Months,
            WeekOfMonth = fields.WeekOfMonth,
            Weekday = fields.Weekday,
            PreserveWeekday = fields.PreserveWeekday,
            RecalculateFrom = ReadInt(e, "RECURFROM", 1) switch
            {
                0 => "doneDate",
                2 => "startDate",
                _ => "dueDate",
            },
            OnRecur = ReadInt(e, "RECURREUSE", 0) switch
            {
                1 => "createNew",
                2 => "ask",
                _ => "reuse",
            },
            TotalOccurrences = NullIfInfinite(ReadInt(e, "RECURNUM", OccursInfinitely)),
            RemainingOccurrences = NullIfInfinite(ReadInt(e, "RECURREMAINING", OccursInfinitely)),
            PreserveComments = ReadInt(e, "RECURPRESERVECOMMENTS", 1) != 0,
        };
    }

    private readonly record struct Fields(
        int? Interval = null,
        IReadOnlyList<string>? DaysOfWeek = null,
        int? DayOfMonth = null,
        IReadOnlyList<string>? Months = null,
        string? WeekOfMonth = null,
        string? Weekday = null,
        bool? PreserveWeekday = null);

    private static (string Pattern, string Description, Fields Fields) Decode(int freq, int spec1, int spec2)
    {
        switch (freq)
        {
            case FreqEveryNDays:
                return ("everyNDays", EveryText("day", spec1), new Fields(Interval: spec1));

            case FreqEveryWeekday:
                return ("everyWeekday", "Every weekday (Monday to Friday)", new Fields());

            case FreqEveryNWeekdays:
                return ("everyNWeekdays", EveryText("weekday", spec1), new Fields(Interval: spec1));

            case FreqWeeklyOnDays:
            {
                var days = DecodeWeekdays(spec2);
                var text = days.Count > 0
                    ? $"Weekly on {string.Join(", ", days)}{EverySuffix("week", spec1)}"
                    : $"Weekly{EverySuffix("week", spec1)}";
                return ("weeklyOnDays", text, new Fields(Interval: spec1, DaysOfWeek: days));
            }

            case FreqEveryNWeeks:
                return ("everyNWeeks", EveryText("week", spec1), new Fields(Interval: spec1));

            case FreqMonthlyOnDay:
                return ("monthlyOnDay",
                    $"Monthly on day {spec2}{EverySuffix("month", spec1)}",
                    new Fields(Interval: spec1, DayOfMonth: spec2));

            case FreqEveryNMonths:
            {
                var preserve = spec2 != 0;
                return ("everyNMonths", EveryText("month", spec1) + PreserveSuffix(preserve),
                    new Fields(Interval: spec1, PreserveWeekday: preserve));
            }

            case FreqMonthlyByWeekday:
            {
                var (which, weekday) = DecodeWhichDow(spec1);
                return ("monthlyByWeekday",
                    $"Monthly on the {which} {weekday}{EverySuffix("month", spec2)}",
                    new Fields(Interval: spec2, WeekOfMonth: which, Weekday: weekday));
            }

            case FreqMonthlyFirstLastWeekday:
            {
                var which = spec1 == 0 ? "first" : "last";
                return ("monthlyOnFirstLastWeekday",
                    $"Monthly on the {which} weekday{EverySuffix("month", spec2)}",
                    new Fields(Interval: spec2, WeekOfMonth: which));
            }

            case FreqYearlyOnDate:
            {
                var months = DecodeMonths(spec1);
                var monthText = months.Count > 0 ? string.Join(", ", months) : "the given month";
                return ("yearlyOnDate",
                    $"Yearly on day {spec2} of {monthText}",
                    new Fields(DayOfMonth: spec2, Months: months));
            }

            case FreqEveryNYears:
            {
                var preserve = spec2 != 0;
                return ("everyNYears", EveryText("year", spec1) + PreserveSuffix(preserve),
                    new Fields(Interval: spec1, PreserveWeekday: preserve));
            }

            case FreqYearlyByWeekday:
            {
                var (which, weekday) = DecodeWhichDow(spec1);
                var months = DecodeMonths(spec2);
                var monthText = months.Count > 0 ? string.Join(", ", months) : "the given month";
                return ("yearlyByWeekday",
                    $"Yearly on the {which} {weekday} of {monthText}",
                    new Fields(WeekOfMonth: which, Weekday: weekday, Months: months));
            }

            default:
                // Deprecated (6/7/9/11) or an unknown future frequency — surface without decoding.
                return ("unsupported", $"Unsupported or legacy recurrence (RECURFREQ={freq})", new Fields());
        }
    }

    /// <summary>
    /// Builds a &lt;RECURRENCE&gt; element from a <see cref="SetRecurrenceRequest"/>, validated against
    /// ToDoList's own rules so an invalid rule is rejected here rather than silently dropped by the app.
    /// Emits the raw TDIR ordinal, the per-frequency spec payload (incl. DHW/DHM bitmasks), the five
    /// bookkeeping attributes and the human-readable label body — matching what ToDoList writes.
    /// </summary>
    public static XElement Build(SetRecurrenceRequest req)
    {
        var (freq, spec1, spec2, label) = Encode(req);

        var occurrences = req.Occurrences;
        if (occurrences is int n && n < 1)
            throw new ArgumentException("Occurrences must be at least 1.", nameof(req.Occurrences));
        var num = occurrences ?? OccursInfinitely;

        // Attribute order matches ToDoList's own output.
        return new XElement("RECURRENCE",
            new XAttribute("RECURFREQ", freq),
            new XAttribute("RECURSPECIFIC1", spec1),
            new XAttribute("RECURSPECIFIC2", spec2),
            new XAttribute("RECURREUSE", req.OnRecur switch
            {
                RecurrenceReuse.CreateNew => 1,
                RecurrenceReuse.Ask => 2,
                _ => 0,
            }),
            new XAttribute("RECURFROM", req.RecalcFrom switch
            {
                RecurrenceRecalcFrom.DoneDate => 0,
                RecurrenceRecalcFrom.StartDate => 2,
                _ => 1,
            }),
            new XAttribute("RECURNUM", num),
            new XAttribute("RECURREMAINING", num),
            new XAttribute("RECURPRESERVECOMMENTS", req.PreserveComments ? 1 : 0),
            label);
    }

    private static (int Freq, int Spec1, int Spec2, string Label) Encode(SetRecurrenceRequest req)
    {
        switch (req.Pattern)
        {
            case RecurrencePattern.EveryNDays:
                return (FreqEveryNDays, PositiveInterval(req), 0, "Daily");
            case RecurrencePattern.EveryWeekday:
                return (FreqEveryWeekday, 0, 0, "Daily");
            case RecurrencePattern.EveryNWeekdays:
                return (FreqEveryNWeekdays, PositiveInterval(req), 0, "Daily");
            case RecurrencePattern.WeeklyOnDays:
                // The days are the primary spec; the every-N-weeks interval defaults to 1.
                return (FreqWeeklyOnDays, OptionalInterval(req), EncodeWeekdays(req.DaysOfWeek), "Weekly");
            case RecurrencePattern.EveryNWeeks:
                return (FreqEveryNWeeks, PositiveInterval(req), 0, "Weekly");
            case RecurrencePattern.MonthlyOnDay:
                // The day-of-month is the primary spec; the every-N-months interval defaults to 1.
                return (FreqMonthlyOnDay, OptionalInterval(req), DayOfMonth(req), "Monthly");
            case RecurrencePattern.EveryNMonths:
                return (FreqEveryNMonths, PositiveInterval(req), 0, "Monthly");
            case RecurrencePattern.YearlyOnDate:
                return (FreqYearlyOnDate, EncodeMonths(req.Months), DayOfMonth(req), "Yearly");
            case RecurrencePattern.EveryNYears:
                return (FreqEveryNYears, PositiveInterval(req), 0, "Yearly");
            default:
                throw new ArgumentOutOfRangeException(nameof(req.Pattern), req.Pattern, "Unsupported recurrence pattern.");
        }
    }

    private static int PositiveInterval(SetRecurrenceRequest req) =>
        req.Interval is int n && n > 0
            ? n
            : throw new ArgumentException($"Pattern '{req.Pattern}' requires an interval of at least 1.", nameof(req.Interval));

    /// <summary>Interval for patterns where it's secondary (weekly-on-days, monthly-on-day): defaults to
    /// 1 when omitted, but a supplied value must still be ≥ 1.</summary>
    private static int OptionalInterval(SetRecurrenceRequest req) =>
        req.Interval switch
        {
            null => 1,
            int n when n > 0 => n,
            _ => throw new ArgumentException($"Pattern '{req.Pattern}' interval must be at least 1.", nameof(req.Interval)),
        };

    private static int DayOfMonth(SetRecurrenceRequest req) =>
        req.DayOfMonth is int d && d is >= 1 and <= 31
            ? d
            : throw new ArgumentException($"Pattern '{req.Pattern}' requires a day of month between 1 and 31.", nameof(req.DayOfMonth));

    private static int EncodeWeekdays(IReadOnlyList<string>? days)
    {
        if (days is null || days.Count == 0)
            throw new ArgumentException("Weekly recurrence requires at least one day of the week.", nameof(days));
        var mask = 0;
        foreach (var name in days)
        {
            var bit = WeekdayBits.FirstOrDefault(d => Matches(d.Name, name)).Bit;
            if (bit == 0) throw new ArgumentException($"Unknown day of week '{name}'.", nameof(days));
            mask |= bit;
        }
        return mask;
    }

    private static int EncodeMonths(IReadOnlyList<string>? months)
    {
        if (months is null || months.Count == 0)
            throw new ArgumentException("Yearly recurrence requires at least one month.", nameof(months));
        var mask = 0;
        foreach (var name in months)
        {
            var bit = MonthBits.FirstOrDefault(m => Matches(m.Name, name)).Bit;
            if (bit == 0) throw new ArgumentException($"Unknown month '{name}'.", nameof(months));
            mask |= bit;
        }
        return mask;
    }

    /// <summary>Matches a day/month name case-insensitively by full name or a prefix of at least three
    /// letters (e.g. "Mon", "Mond", "Monday" all match Monday). Safe here because day and month names
    /// are unambiguous from their first three letters.</summary>
    private static bool Matches(string canonical, string input)
    {
        var s = input?.Trim() ?? "";
        return s.Equals(canonical, StringComparison.OrdinalIgnoreCase)
            || (s.Length >= 3 && canonical.StartsWith(s, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> DecodeWeekdays(int mask) =>
        WeekdayBits.Where(d => (mask & d.Bit) != 0).Select(d => d.Name).ToList();

    /// <summary>Decodes the yearly month slot: a plain index (1–12) or a DH_MONTH bitmask.</summary>
    private static IReadOnlyList<string> DecodeMonths(int value)
    {
        if (value is >= 1 and <= 12) return new[] { MonthNames[value - 1] };
        return MonthBits.Where(m => (value & m.Bit) != 0).Select(m => m.Name).ToList();
    }

    /// <summary>Unpacks the by-weekday spec: LOWORD = which (1–5, 5 = last), HIWORD = OLE DOW (1–7).</summary>
    private static (string Which, string Weekday) DecodeWhichDow(int spec)
    {
        var which = spec & 0xFFFF;
        var dow = (spec >> 16) & 0xFFFF;
        var whichName = which is >= 1 and <= 5 ? WhichNames[which] : which.ToString(CultureInfo.InvariantCulture);
        var dowName = dow is >= 1 and <= 7 ? OleDayNames[dow] : "weekday";
        return (whichName, dowName);
    }

    private static string EveryText(string unit, int n) =>
        n <= 1 ? $"Every {unit}" : $"Every {n} {unit}s";

    private static string EverySuffix(string unit, int n) =>
        n <= 1 ? "" : $", every {n} {unit}s";

    private static string PreserveSuffix(bool preserveWeekday) =>
        preserveWeekday ? ", preserving weekday" : "";

    private static int? NullIfInfinite(int n) => n < 0 ? null : n;

    private static int ReadInt(XElement e, string name, int fallback)
    {
        var s = (string?)e.Attribute(name);
        return !string.IsNullOrWhiteSpace(s)
            && int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v : fallback;
    }
}
