using System.Xml.Linq;
using TodoListMcp.Core.Model;

namespace TodoListMcp.Core;

/// <summary>
/// Reads and writes ToDoList custom attributes: the list-level <c>&lt;CUSTOMATTRIBDEF&gt;</c>
/// definitions and the per-task <c>&lt;CUSTOMATTRIB&gt;</c> values.
///
/// Format facts (verified against AbstractSpoon ToDoList 9.2, <c>IEnums.h</c> / <c>TaskFile.cpp</c>):
///  - A definition is a <c>&lt;CUSTOMATTRIBDEF&gt;</c> element under the tasklist root with attributes
///    ID, LABEL, COLTITLE, TYPE, COLALIGN, FEATURES, ENABLED and (for list types) LISTDATA.
///  - TYPE packs a data type in its low byte (<c>0x00ff</c>) and a list type in its high byte
///    (<c>0xff00</c>).
///  - LISTDATA is the default items joined by '\n', then a '\t', then (auto lists only) the
///    auto-collected items joined by '\n'.
///  - A task value is a <c>&lt;CUSTOMATTRIB ID VALUE VALUEDISPLAY&gt;</c> child; multi-select values
///    newline-join their items in VALUE. VALUEDISPLAY is a derived mirror ToDoList recomputes on load.
/// </summary>
internal static class CustomAttributeFormat
{
    private const int DataMask = 0x00ff;
    private const int ListMask = 0xff00;

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
    /// Sets (or clears) a task's value for one custom field. A null/empty <paramref name="value"/>
    /// removes the field's element(s). An existing element is updated in place — its position and any
    /// attributes this server doesn't own are preserved — with only the derived mirrors
    /// (VALUEDISPLAY/CALC/CALCDISPLAY) dropped so ToDoList recomputes them and never shows a stale
    /// value; a new element is created only when the task has none. This mirrors ToDoList's own
    /// <c>SetTaskCustomAttributeData</c> (get-or-add the element, then overwrite VALUE). Throws when
    /// the field has no definition on the list, or when it is a calculation field (derived by ToDoList).
    /// </summary>
    public static void SetValue(
        XElement task, string id, string? value,
        IReadOnlyDictionary<string, CustomAttributeDefinition> defs)
    {
        var key = ValidateSettable(id, defs);
        var existing = ExistingValueElements(task, key);

        if (string.IsNullOrEmpty(value))
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
            e.SetAttributeValue(ValueAttr, value);
            foreach (var derived in DerivedAttrs) e.SetAttributeValue(derived, null);
            return;
        }

        task.Add(new XElement(ValueElement,
            new XAttribute(IdAttr, key),
            new XAttribute(ValueAttr, value)));
    }

    /// <summary>
    /// Validates that a custom field can be set — the ID is non-empty, the field is defined on the
    /// list, and it is not a calculation — throwing an <see cref="ArgumentException"/> otherwise.
    /// Returns the resolved (upper-cased) key. Used to check every assignment up front so a rejected
    /// field can't leave a half-applied update.
    /// </summary>
    public static string ValidateSettable(string id, IReadOnlyDictionary<string, CustomAttributeDefinition> defs)
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
        return key;
    }

    private static IReadOnlyList<XElement> ExistingValueElements(XElement task, string upperId) =>
        task.Elements(ValueElement)
            .Where(e => string.Equals(((string?)e.Attribute(IdAttr))?.Trim(), upperId, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string? Nullify(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
