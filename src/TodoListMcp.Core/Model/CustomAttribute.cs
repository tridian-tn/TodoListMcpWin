namespace TodoListMcp.Core.Model;

/// <summary>
/// The data type of a custom attribute — the low byte of ToDoList's <c>TYPE</c> field
/// (<c>TDCCA_DATAMASK</c>). <see cref="Unknown"/> covers a data-type value this server doesn't
/// recognise (e.g. one added by a newer ToDoList); such a field is still surfaced and round-tripped.
/// </summary>
public enum CustomFieldDataType
{
    String,
    Date,
    Integer,
    Double,
    Bool,
    Icon,
    FileLink,
    TimePeriod,
    Fraction,
    Calculation,
    Unknown,
}

/// <summary>
/// The list behaviour of a custom attribute — the high byte of ToDoList's <c>TYPE</c> field
/// (<c>TDCCA_LISTMASK</c>). <see cref="None"/> is a plain scalar field; the auto/fixed variants are
/// single-select drop-downs and the <c>*Multi</c> variants are multi-select (values newline-joined
/// on disk).
/// </summary>
public enum CustomFieldListType
{
    None,
    Auto,
    Fixed,
    AutoMulti,
    FixedMulti,
}

/// <summary>
/// A read projection of a list-level <c>&lt;CUSTOMATTRIBDEF&gt;</c> — one custom field defined on the
/// tasklist. Read-only: this server surfaces and edits task <em>values</em>, it does not create or
/// edit the definitions themselves (do that in the ToDoList app).
/// </summary>
public sealed class CustomAttributeDefinition
{
    /// <summary>The field's unique ID (ToDoList upper-cases these on disk), e.g. "MR". The key used to read/write a task's value.</summary>
    public required string Id { get; init; }

    /// <summary>The field's display label (the ATTRIBUTE name shown in ToDoList's editor); null when unset.</summary>
    public string? Label { get; init; }

    /// <summary>The column header title, when different from the label; null when unset.</summary>
    public string? ColumnTitle { get; init; }

    /// <summary>The field's data type.</summary>
    public CustomFieldDataType DataType { get; init; }

    /// <summary>The field's list behaviour.</summary>
    public CustomFieldListType ListType { get; init; }

    /// <summary>True for the multi-select list types (a task may hold several values).</summary>
    public bool IsMultiValued => ListType is CustomFieldListType.AutoMulti or CustomFieldListType.FixedMulti;

    /// <summary>True when the field is any kind of list (a scalar field is <see cref="CustomFieldListType.None"/>).</summary>
    public bool IsList => ListType != CustomFieldListType.None;

    /// <summary>
    /// The list's values: the fixed set of allowed values for a fixed list, or the seed/auto-collected
    /// values for an auto list. Empty for a non-list field. (ToDoList's LISTDATA "default" items; the
    /// separate auto-accumulated items are folded in for auto lists.)
    /// </summary>
    public IReadOnlyList<string> ListItems { get; init; } = Array.Empty<string>();

    /// <summary>Whether the field is enabled on the list.</summary>
    public bool Enabled { get; init; }

    /// <summary>The raw <c>TYPE</c> integer as stored, for diagnostics.</summary>
    public int RawType { get; init; }
}

/// <summary>
/// A read projection of one of a task's custom-field values (a <c>&lt;CUSTOMATTRIB&gt;</c> element).
/// </summary>
public sealed class CustomFieldValue
{
    /// <summary>The field's ID (matches a <see cref="CustomAttributeDefinition.Id"/>), e.g. "MR".</summary>
    public required string Id { get; init; }

    /// <summary>
    /// The raw on-disk value. For a multi-select field this is the newline-joined list of items; see
    /// <see cref="Values"/> for the split form.
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// The value split into items: the individual selections for a multi-select field, or a single
    /// element for a scalar field. Splitting follows the field's definition (only multi-select fields
    /// are split on newline), so a scalar value that happens to contain a newline is kept whole.
    /// </summary>
    public IReadOnlyList<string> Values { get; init; } = Array.Empty<string>();

    /// <summary>
    /// ToDoList's derived human-readable rendering of the value (the VALUEDISPLAY attribute), when the
    /// app has written one; null otherwise. Display-only — <see cref="Value"/> is the source of truth.
    /// </summary>
    public string? Display { get; init; }
}
