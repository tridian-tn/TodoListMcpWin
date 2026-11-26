using System.Globalization;
using System.Xml.Linq;
using TodoListMcp.Core.Model;

namespace TodoListMcp.Core;

/// <summary>
/// Reads and writes ToDoList custom attributes: the list-level <c>&lt;CUSTOMATTRIBDEF&gt;</c>
/// definitions and the per-task <c>&lt;CUSTOMATTRIB&gt;</c> values.
///
/// Format facts (verified against AbstractSpoon ToDoList 9.2, <c>IEnums.h</c> / <c>TaskFile.cpp</c> /
/// <c>TDCCustomAttribData.cpp</c>):
///  - A definition is a <c>&lt;CUSTOMATTRIBDEF&gt;</c> element under the tasklist root with attributes
///    ID, LABEL, COLTITLE, TYPE, COLALIGN, FEATURES, ENABLED and (for list types) LISTDATA.
///  - TYPE packs a data type in its low byte (<c>0x00ff</c>) and a list type in its high byte
///    (<c>0xff00</c>).
///  - LISTDATA is the default items joined by '\n', then a '\t', then (auto lists only) the
///    auto-collected items joined by '\n'.
///  - A task value is a <c>&lt;CUSTOMATTRIB ID VALUE VALUEDISPLAY&gt;</c> child; multi-select values
///    newline-join their items in VALUE. VALUEDISPLAY is a derived mirror ToDoList recomputes on load.
///  - VALUE holds whatever string the <c>TDCCADATA</c> struct would carry (ToDoList writes it
///    verbatim), so each data type has its own encoding — see <see cref="Encode"/>.
/// </summary>
internal static class CustomAttributeFormat
{
    private const int DataMask = 0x00ff;
    private const int ListMask = 0xff00;

    /// <summary>
    /// How ToDoList formats a number inside a custom value: C's <c>"%lf"</c>, so six decimal places.
    /// Deliberately not the eight places the task date attributes use — those go through a different
    /// code path (<c>CTaskFile</c>'s own date writer rather than <c>TDCCADATA::Set</c>).
    /// </summary>
    private const string NumberFormat = "0.000000";

    /// <summary>
    /// The date forms accepted for a date field. A closed set on purpose: a locale-ambiguous value
    /// like 12/01/2026 would silently mean two different days depending on who read it.
    /// </summary>
    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
        "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF",
    };

    // Task value element/attribute names.
    private const string ValueElement = "CUSTOMATTRIB";
    private const string IdAttr = "ID";
    private const string ValueAttr = "VALUE";
    private const string ValueDisplayAttr = "VALUEDISPLAY";
    // Derived attributes ToDoList recomputes; dropped on write so we never leave a stale mirror.
    private static readonly string[] DerivedAttrs = { ValueDisplayAttr, "CALC", "CALCDISPLAY" };

    /// <summary>The multi-select list separator ToDoList uses inside a VALUE / LISTDATA field.</summary>
    private const char ItemSep = '\n';

    /// <summary>Reads the list's custom-field definitions (empty when the list defines none).</summary>
    public static IReadOnlyList<CustomAttributeDefinition> ReadDefinitions(XElement root)
    {
        // 9.2 writes <CUSTOMATTRIBDEF>; fall back to the legacy <CUSTOMATTRIB> root element only when
        // no modern defs are present, so a task-level element never leaks in (those live under TASK).
        var defs = root.Elements("CUSTOMATTRIBDEF").ToList();
        if (defs.Count == 0)
            defs = root.Elements("CUSTOMATTRIB").ToList();

        return defs.Select(ReadDefinition).Where(d => d is not null).Select(d => d!).ToList();
    }

    /// <summary>A case-insensitive lookup of the list's definitions by ID; keyed as ToDoList keys them (upper-case).</summary>
    public static IReadOnlyDictionary<string, CustomAttributeDefinition> DefinitionMap(XElement root)
    {
        var map = new Dictionary<string, CustomAttributeDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in ReadDefinitions(root))
            map[d.Id] = d;
        return map;
    }

    private static CustomAttributeDefinition? ReadDefinition(XElement e)
    {
        var id = ((string?)e.Attribute(IdAttr))?.Trim();
        if (string.IsNullOrEmpty(id)) return null;

        var rawType = (int?)e.Attribute("TYPE") ?? 0;
        var listType = ToListType(rawType);
        return new CustomAttributeDefinition
        {
            Id = id.ToUpperInvariant(),
            Label = Nullify((string?)e.Attribute("LABEL")),
            ColumnTitle = Nullify((string?)e.Attribute("COLTITLE")),
            DataType = ToDataType(rawType),
            ListType = listType,
            Enabled = ((int?)e.Attribute("ENABLED") ?? 0) != 0,
            ListItems = listType == CustomFieldListType.None
                ? Array.Empty<string>()
                : DecodeListData((string?)e.Attribute("LISTDATA")),
            RawType = rawType,
        };
    }

    private static CustomFieldDataType ToDataType(int type) => (type & DataMask) switch
    {
        0x00 => CustomFieldDataType.String,
        0x01 => CustomFieldDataType.Date,
        0x02 => CustomFieldDataType.Integer,
        0x03 => CustomFieldDataType.Double,
        0x04 => CustomFieldDataType.Bool,
        0x05 => CustomFieldDataType.Icon,
        0x06 => CustomFieldDataType.FileLink,
        0x07 => CustomFieldDataType.TimePeriod,
        0x08 => CustomFieldDataType.Fraction,
        0x09 => CustomFieldDataType.Calculation,
        _ => CustomFieldDataType.Unknown,
    };

    private static CustomFieldListType ToListType(int type) => (type & ListMask) switch
    {
        0x0000 => CustomFieldListType.None,
        0x0100 => CustomFieldListType.Auto,
        0x0200 => CustomFieldListType.Fixed,
        0x0300 => CustomFieldListType.AutoMulti,
        0x0400 => CustomFieldListType.FixedMulti,
        _ => CustomFieldListType.None,
    };

    /// <summary>
    /// Decodes a LISTDATA payload into its list items. The default items precede a '\t'; for auto lists
    /// the auto-collected items follow it. Both are surfaced (deduped, order preserved) so the caller
    /// sees every value the field offers.
    /// </summary>
    private static IReadOnlyList<string> DecodeListData(string? listData)
    {
        if (string.IsNullOrEmpty(listData)) return Array.Empty<string>();
        var tab = listData.IndexOf('\t');
        var defaultPart = tab < 0 ? listData : listData[..tab];
        var autoPart = tab < 0 ? "" : listData[(tab + 1)..];

        var items = new List<string>();
        foreach (var part in new[] { defaultPart, autoPart })
        {
            if (part.Length == 0) continue;
            foreach (var item in part.Split(ItemSep))
                if (item.Length > 0) items.Add(item);
        }
        return items.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Reads a task's custom-field values. Multi-select values are split into their items per the
    /// list's definitions (<paramref name="defs"/>); a value whose field is unknown or scalar is kept
    /// whole. Empty values are skipped, mirroring ToDoList's read.
    /// </summary>
    public static IReadOnlyList<CustomFieldValue> ReadValues(
        XElement task, IReadOnlyDictionary<string, CustomAttributeDefinition> defs)
    {
        var values = new List<CustomFieldValue>();
        foreach (var e in task.Elements(ValueElement))
        {
            var id = ((string?)e.Attribute(IdAttr))?.Trim();
            if (string.IsNullOrEmpty(id)) continue;
            var value = (string?)e.Attribute(ValueAttr);
            if (string.IsNullOrEmpty(value)) continue;

            id = id.ToUpperInvariant();
            var multi = defs.TryGetValue(id, out var def) && def.IsMultiValued;
            var items = multi
                ? value.Split(ItemSep).Where(v => v.Length > 0).ToArray()
                : new[] { value };

            values.Add(new CustomFieldValue
            {
                Id = id,
                Value = value,
                Values = items,
                Display = Nullify((string?)e.Attribute(ValueDisplayAttr)),
            });
        }
        return values;
    }

    /// <summary>
    /// A custom-field assignment that has been checked and encoded: <see cref="Id"/> is the resolved
    /// (upper-cased) field key and <see cref="Value"/> the exact string to store, or null to clear the
    /// field.
    /// </summary>
    public readonly record struct PreparedCustomField(string Id, string? Value);

    /// <summary>
    /// Checks one assignment and encodes its value the way ToDoList stores that field's type, so a
    /// caller can pass a natural value — a date as <c>2026-12-01</c>, a time period as <c>3h</c>, a
    /// bool as <c>true</c> — instead of the on-disk encoding. An empty or null
    /// <paramref name="value"/> prepares a clear. Throws an <see cref="ArgumentException"/> when the
    /// field is undefined on the list, is a calculation, or carries a value that doesn't fit its type.
    /// Separating this from <see cref="Apply"/> is what lets a whole request be checked before any of
    /// it is written, so a rejected field can't leave a half-applied update.
    /// </summary>
    public static PreparedCustomField Prepare(
        string id, string? value, IReadOnlyDictionary<string, CustomAttributeDefinition> defs)
    {
        var def = ValidateSettable(id, defs);
        return new PreparedCustomField(
            def.Id, string.IsNullOrEmpty(value) ? null : Encode(def, value));
    }

    /// <summary>
    /// Writes a prepared assignment onto a task. A null value removes the field's element(s). An
    /// existing element is updated in place — its position and any attributes this server doesn't own
    /// are preserved — with only the derived mirrors (VALUEDISPLAY/CALC/CALCDISPLAY) dropped so
    /// ToDoList recomputes them and never shows a stale value; a new element is created only when the
    /// task has none. This mirrors ToDoList's own <c>SetTaskCustomAttributeData</c> (get-or-add the
    /// element, then overwrite VALUE).
    /// </summary>
    public static void Apply(XElement task, PreparedCustomField field)
    {
        if (string.IsNullOrEmpty(field.Id))
        {
            throw new ArgumentException(
                "A prepared custom field must carry a field ID; build it with Prepare.", nameof(field));
        }

        var existing = ExistingValueElements(task, field.Id);

        // An empty value clears, exactly as a null one does: ToDoList skips an empty VALUE on read,
        // so writing one would leave a dead element behind instead of removing the field.
        if (string.IsNullOrEmpty(field.Value))
        {
            foreach (var e in existing) e.Remove();
            return;
        }

        if (existing.Count > 0)
        {
            // Keep the first element (its place among siblings and any other attributes ride through);
            // collapse any duplicates, matching ToDoList's one-element-per-field invariant.
            var e = existing[0];
            for (var i = 1; i < existing.Count; i++) existing[i].Remove();
            e.SetAttributeValue(ValueAttr, field.Value);
            foreach (var derived in DerivedAttrs) e.SetAttributeValue(derived, null);
            return;
        }

        task.Add(new XElement(ValueElement,
            new XAttribute(IdAttr, field.Id),
            new XAttribute(ValueAttr, field.Value)));
    }

    /// <summary>
    /// Encodes a caller's value into the string ToDoList stores for that field's type (each case
    /// verified against the matching <c>TDCCADATA</c> accessor in 9.2). String, icon and file-link
    /// fields are stored as given, and so is any data type a newer ToDoList introduces — an unknown
    /// type falls back to verbatim rather than erroring, so a field this server has never heard of
    /// still works.
    /// </summary>
    private static string? Encode(CustomAttributeDefinition def, string value)
    {
        // A list field's values are its own declared items — strings whatever the data byte says — so
        // they're stored as given, with a fixed list's membership checked.
        if (def.IsList) return EncodeListSelection(def, value);

        return def.DataType switch
        {
            CustomFieldDataType.Date => EncodeDate(def, value),
            CustomFieldDataType.TimePeriod => EncodeTimePeriod(def, value),
            CustomFieldDataType.Bool => EncodeBool(def, value),
            CustomFieldDataType.Integer => EncodeInteger(def, value),
            CustomFieldDataType.Double => EncodeDouble(def, value),
            CustomFieldDataType.Fraction => EncodeFraction(def, value),
            _ => value,
        };
    }

    /// <summary>
    /// A date is stored as an OLE-automation serial, the same convention as DUEDATE and friends. A
    /// value that's already a number is taken as a serial, so one read back off a task round-trips
    /// unchanged; anything else has to be one of <see cref="DateFormats"/>.
    /// </summary>
    private static string EncodeDate(CustomAttributeDefinition def, string value)
    {
        var text = value.Trim();
        if (TryParseNumber(text, out var serial))
        {
            if (!IsDateSerial(serial))
            {
                throw Invalid(def, value, "a date after 1899-12-30, as yyyy-MM-dd or an OLE serial");
            }
            return FormatNumber(serial);
        }
        if (DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out var date))
        {
            return FormatNumber(date.ToOADate());
        }

        throw Invalid(def, value, "a date as yyyy-MM-dd or yyyy-MM-ddTHH:mm");
    }

    /// <summary>
    /// Whether a serial is a date that can be read back: positive (the reader treats a non-positive
    /// serial as no date at all) and inside the OLE range, so a number far outside it is refused
    /// rather than written for ToDoList to choke on.
    /// </summary>
    private static bool IsDateSerial(double serial)
    {
        if (serial <= 0) return false;
        try
        {
            DateTime.FromOADate(serial);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// A time period is stored as the amount, a colon, then the unit letter. The letter has to come
    /// last because ToDoList reads the unit off the end of the string. The unit can be given as a
    /// letter or a word ("3h", "90 mins", "2.5:D"); a bare number is taken as hours, which is
    /// ToDoList's own default when no unit is stored. The amount is clamped at zero, the same way
    /// <c>SetTime</c> clamps the task's own estimate and time spent — ToDoList's UI can't produce a
    /// negative period.
    /// </summary>
    private static string EncodeTimePeriod(CustomAttributeDefinition def, string value)
    {
        var text = value.Trim();
        string amountText, unitText;

        var colon = text.LastIndexOf(':');
        if (colon >= 0)
        {
            amountText = text[..colon];
            unitText = text[(colon + 1)..];
        }
        else
        {
            // Everything up to the first letter is the amount; the rest names the unit.
            var split = 0;
            while (split < text.Length && !char.IsLetter(text[split])) split++;
            amountText = text[..split];
            unitText = text[split..];
        }

        if (!TryParseNumber(amountText, out var amount))
        {
            throw Invalid(def, value, "a time period, e.g. 3h, 90 mins or 2.5:D");
        }

        var unit = TimeUnit.Hours;
        if (unitText.Trim().Length > 0 && !TimeUnits.TryParse(unitText, out unit))
        {
            throw Invalid(def, value, "a time period whose unit is I/H/D/K/W/M/Y or a word like hours");
        }

        return $"{FormatNumber(Math.Max(0, amount))}:{TimeUnits.ToFileCode(unit)}";
    }

    /// <summary>
    /// ToDoList stores true as a single "+" and false as no value at all — its reader takes any
    /// non-empty value as true — so false clears the field, which reads back as false either way.
    /// </summary>
    private static string? EncodeBool(CustomAttributeDefinition def, string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "y" or "1" or "+" or "on" => "+",
            "false" or "no" or "n" or "0" or "off" => null,
            _ => throw Invalid(def, value, "true or false"),
        };

    /// <summary>
    /// An integer is stored with "%d", so it's re-emitted in canonical form. ToDoList reads these
    /// with <c>_ttoi</c>, which is 32-bit too, so a whole number that doesn't fit is refused — and
    /// told it's out of range rather than that it isn't whole.
    /// </summary>
    private static string EncodeInteger(CustomAttributeDefinition def, string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
        {
            return i.ToString(CultureInfo.InvariantCulture);
        }

        var digits = value.Trim().TrimStart('+', '-');
        if (digits.Length > 0 && digits.All(char.IsAsciiDigit))
        {
            throw Invalid(def, value, $"a whole number between {int.MinValue} and {int.MaxValue}");
        }
        throw Invalid(def, value, "a whole number");
    }

    /// <summary>A decimal is stored with "%lf" — see <see cref="NumberFormat"/>.</summary>
    private static string EncodeDouble(CustomAttributeDefinition def, string value) =>
        TryParseNumber(value, out var d)
            ? FormatNumber(d)
            : throw Invalid(def, value, "a number");

    /// <summary>
    /// A fraction is stored as the text itself — "3/4" — which ToDoList splits on the slash; one with
    /// no slash it falls back to reading as a plain number. There's no numeric re-format for this
    /// type (no <c>TDCCADATA::Set</c> overload takes a fraction), so both forms are kept as given
    /// once they're checked to be parseable, only losing any spaces around the slash.
    /// </summary>
    private static string EncodeFraction(CustomAttributeDefinition def, string value)
    {
        var text = value.Trim();
        var slash = text.IndexOf('/');

        if (slash < 0)
        {
            return TryParseNumber(text, out _)
                ? text
                : throw Invalid(def, value, "a fraction such as 3/4, or a number");
        }

        var numerator = text[..slash].Trim();
        var denominator = text[(slash + 1)..].Trim();
        if (!TryParseNumber(numerator, out _) || !TryParseNumber(denominator, out var den) || den == 0)
        {
            throw Invalid(def, value, "a fraction such as 3/4");
        }

        return $"{numerator}/{denominator}";
    }

    /// <summary>
    /// Checks a selection against a fixed list's declared values and returns it in the definition's own
    /// casing, so what's stored matches an item exactly. Only a fixed list has a closed set — an auto
    /// list accumulates whatever gets typed into it — and a fixed list that declares no values yet is
    /// left alone rather than rejecting everything. A multi-select selection is checked item by item,
    /// duplicates collapse as they do for categories and assignees, and one that selects nothing at
    /// all clears the field rather than storing an empty value.
    /// </summary>
    private static string? EncodeListSelection(CustomAttributeDefinition def, string value)
    {
        // Callers are told to newline-join items for a multi-select field, so doing it to a
        // single-select one is an easy mistake and worth naming rather than reporting the whole
        // string as an unknown value.
        if (!def.IsMultiValued && value.Contains(ItemSep))
        {
            throw new ArgumentException(
                $"Custom field '{def.Id}' holds a single value, so newline-joined items can't be "
                + "stored in it (that form is for multi-select fields).",
                nameof(value));
        }

        if (def.ListType is not (CustomFieldListType.Fixed or CustomFieldListType.FixedMulti)
            || def.ListItems.Count == 0)
        {
            return value;
        }

        var items = def.IsMultiValued
            ? value.Split(ItemSep).Where(i => i.Length > 0).ToArray()
            : new[] { value };

        var resolved = new List<string>(items.Length);
        foreach (var item in items)
        {
            var match = def.ListItems.FirstOrDefault(
                i => string.Equals(i, item.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new ArgumentException(
                    $"'{item.Trim()}' is not one of the values custom field '{def.Id}' allows "
                    + $"({string.Join(", ", def.ListItems)}).",
                    nameof(value));
            }
            resolved.Add(match);
        }

        // Resolving to the definition's casing first turns a case-only duplicate into an exact one,
        // so this collapses the same cases SetMulti does for CATEGORY and ALLOCATEDTO.
        var unique = resolved.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return unique.Count == 0 ? null : string.Join(ItemSep, unique);
    }

    /// <summary>
    /// Parses a number for a custom value, requiring a finite result. The finite check matters:
    /// "NaN", "Infinity" and "-Infinity" all parse under <see cref="NumberStyles.Float"/> and would
    /// then be written as that literal text, which ToDoList's own reader takes as zero — so they're
    /// refused here rather than stored as a number-shaped field holding words. The style allows
    /// surrounding whitespace, so callers needn't trim first.
    /// </summary>
    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);

    private static string FormatNumber(double value) =>
        value.ToString(NumberFormat, CultureInfo.InvariantCulture);

    private static ArgumentException Invalid(CustomAttributeDefinition def, string value, string expected) =>
        new($"'{value}' isn't a valid {def.DataType} value for custom field '{def.Id}'; "
            + $"expected {expected}.",
            nameof(value));

    /// <summary>
    /// Checks that a custom field can be set at all — the ID is non-empty, the field is defined on the
    /// list, and it is not a calculation — throwing an <see cref="ArgumentException"/> otherwise.
    /// Returns the definition, whose <see cref="CustomAttributeDefinition.Id"/> is the resolved
    /// (upper-cased) key, so the caller needn't look it up again.
    /// </summary>
    private static CustomAttributeDefinition ValidateSettable(
        string id, IReadOnlyDictionary<string, CustomAttributeDefinition> defs)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A custom field ID must not be empty.", nameof(id));
        var key = id.Trim().ToUpperInvariant();
        if (!defs.TryGetValue(key, out var def))
            throw new ArgumentException(
                $"No custom field '{id}' is defined on this list. Define it in ToDoList first, "
                + $"or check the ID (known: {(defs.Count == 0 ? "none" : string.Join(", ", defs.Keys))}).",
                nameof(id));
        if (def.DataType == CustomFieldDataType.Calculation)
            throw new ArgumentException(
                $"Custom field '{key}' is a calculation; its value is derived by ToDoList and can't be set.",
                nameof(id));
        return def;
    }

    private static IReadOnlyList<XElement> ExistingValueElements(XElement task, string upperId) =>
        task.Elements(ValueElement)
            .Where(e => string.Equals(((string?)e.Attribute(IdAttr))?.Trim(), upperId, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string? Nullify(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
