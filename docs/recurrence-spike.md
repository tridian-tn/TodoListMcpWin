# Spike: recurring tasks (`<RECURRENCE>`)

> **Status: phases 1 & 2 shipped.** Findings for
> [issue #11](https://github.com/tridian-tn/TodoListMcpWin/issues/11) (follow-up to #6, Tier 4). The
> read-only decode ships as `RecurrenceFormat.Read` → `TodoTask.Recurrence`; the constrained writer
> (`RecurrenceFormat.Build` → `set_recurrence`/`clear_recurrence`) covers the common patterns, deferring
> the `Kth`-weekday and first/last-weekday cases; and `complete_task` refuses a recurring task with
> guidance (it can't advance the series the way the app does). Verified against the
> [`abstractspoon/ToDoList_9.2`](https://github.com/abstractspoon/ToDoList_9.2) source.

## How ToDoList stores recurrence

A recurring task carries one `<RECURRENCE>` child element with a cluster of integer attributes and a
human-readable text body:

```xml
<RECURRENCE RECURFREQ="16" RECURSPECIFIC1="3" RECURSPECIFIC2="0" RECURREUSE="0"
            RECURFROM="1" RECURNUM="-1" RECURREMAINING="-1" RECURPRESERVECOMMENTS="1">Daily</RECURRENCE>
```

Read/write is `CTaskFile::GetTaskRecurrence` (`TaskFile.cpp:3031`) and `SetTaskRecurrence`
(`TaskFile.cpp:4103`). The rule is fully described by three integers — `RECURFREQ` +
`RECURSPECIFIC1` + `RECURSPECIFIC2` — plus four bookkeeping fields. **Nothing is a bit-flag at the
attribute level**: `RECURFREQ` is written as the raw enum ordinal (`SetItemValue(...FREQ, nRegularity)`,
`TaskFile.cpp:4116`), *not* the OR of flags. (The `RECURSPECIFIC*` payloads do sometimes carry
bitmasks — see below.)

### The text body is a label only — confirmed

`<RECURRENCE>`'s inner text is written from `tr.GetRegularityText()` and commented `// human readable`
(`TaskFile.cpp:4077`). `GetTaskRecurrence` never reads it back. It collapses every frequency to one of
five coarse strings — `Once` / `Daily` / `Weekly` / `Monthly` / `Yearly` (`TDCRecurrence.cpp`,
`GetRegularityText`) — so e.g. "every N weekdays" also shows as `Daily`. **Treat the body as a
display hint we regenerate; never parse it.**

## `RECURFREQ` — the frequency enum (`TDC_REGULARITY`, `IEnums.h:167`)

The on-disk value is the ordinal of `enum TDC_REGULARITY` (aliases collapse onto the base four, then
"new options" continue from 5). Computed values:

| `RECURFREQ` | Constant | Meaning |
| ---: | --- | --- |
| `-1` | `TDIR_NONE` | error sentinel (never on disk) |
| `0` | `TDIR_ONCE` | not recurring |
| `1` | `TDIR_DAILY` = `TDIR_DAY_EVERY_NDAYS` | every N days |
| `2` | `TDIR_WEEKLY` = `TDIR_WEEK_SPECIFIC_DOWS_NWEEKS` | on specific weekdays, every N weeks |
| `3` | `TDIR_MONTHLY` = `TDIR_MONTH_SPECIFIC_DAY_NMONTHS` | on day-of-month D, every N months |
| `4` | `TDIR_YEARLY` = `TDIR_YEAR_SPECIFIC_DAY_MONTHS` | on day D of month M, every year |
| `5` | `TDIR_DAY_EVERY_WEEKDAY` | every weekday (Mon–Fri) |
| `6` | `TDIR_DEPRECATED_1` | (was DAY_RECREATEAFTERNDAYS) — don't emit |
| `7` | `TDIR_DEPRECATED_2` | (was WEEK_RECREATEAFTERNWEEKS) — don't emit |
| `8` | `TDIR_MONTH_SPECIFIC_DOW_NMONTHS` | on the Kth <weekday>, every N months |
| `9` | `TDIR_DEPRECATED_3` | (was MONTH_RECREATEAFTERNMONTHS) — don't emit |
| `10` | `TDIR_YEAR_SPECIFIC_DOW_MONTHS` | on the Kth <weekday> of month(s) |
| `11` | `TDIR_DEPRECATED_4` | (was YEAR_RECREATEAFTERNYEARS) — don't emit |
| `12` | `TDIR_WEEK_EVERY_NWEEKS` | every N weeks (no specific days) |
| `13` | `TDIR_MONTH_EVERY_NMONTHS` | every N months (preserve weekday) |
| `14` | `TDIR_YEAR_EVERY_NYEARS` | every N years |
| `15` | `TDIR_MONTH_FIRSTLASTWEEKDAY_NMONTHS` | first/last weekday, every N months |
| `16` | `TDIR_DAY_EVERY_NWEEKDAYS` | every N weekdays (skips weekends) |

So the issue's `RECURFREQ="16" RECURSPECIFIC1="3"` is **every 3 weekdays** (labelled `Daily`) — not
"every 3 days" as one might guess from the body text.

## `RECURSPECIFIC1` / `RECURSPECIFIC2` — per-frequency payload

From the struct comments in `Recurrence.h` and `TDCRecurrence.h`. Both are stored as signed `int`
(`SetItemValue(...SPEC1, (int)dwSpecific1)`, `TaskFile.cpp:4117`):

| `RECURFREQ` | `RECURSPECIFIC1` | `RECURSPECIFIC2` |
| --- | --- | --- |
| `1` day-every-N-days | N (interval in days) | 0 |
| `5` every weekday | 0 | 0 |
| `16` day-every-N-weekdays | N (interval) | 0 |
| `2` weekly-on-DOWs | N (interval in weeks) | **weekday bitmask** (`DHW_*`) |
| `12` week-every-N-weeks | N (interval in weeks) | 0 |
| `3` month-on-day-N-months | N (interval in months) | day of month (1–31) |
| `13` month-every-N-months | N (interval in months) | preserve-weekday (BOOL 0/1) |
| `15` month first/last weekday | first (0) / last (≠0) | N (interval in months) |
| `8` month Kth-DOW-N-months | `LOWORD` = which (1–5), `HIWORD` = DOW (1–7) | N (interval in months) |
| `4` year-on-day-of-month | month (1–12, or `DHM_*`) | day of month (1–31) |
| `14` year-every-N-years | N (interval in years) | preserve-weekday (BOOL 0/1) |
| `10` year Kth-DOW-of-month | `LOWORD` = which (1–5), `HIWORD` = DOW (1–7) | month (1–12, or `DHM_*`) |

### Bitmasks used inside the payload (`DateHelperEnums.h`)

Weekday bitmask `DH_DAYOFWEEK` (`:53`) — used for weekly `RECURSPECIFIC2`:

| Day | Value |
| --- | ---: |
| Sunday | `0x01` (1) |
| Monday | `0x02` (2) |
| Tuesday | `0x04` (4) |
| Wednesday | `0x08` (8) |
| Thursday | `0x10` (16) |
| Friday | `0x20` (32) |
| Saturday | `0x40` (64) |
| every day | `0x7F` (127) |

Month bitmask `DH_MONTH` (`:69`) starts at `0x10` "to be distinguishable from plain month indices
(1–12)": January `0x10`, February `0x20`, … December `0x8000`. The yearly frequencies accept **either**
a plain month index (1–12) **or** this bitmask in the month slot — reads must handle both.

The `LOWORD`/`HIWORD` "which/DOW" packing (freqs 8 and 10) is `which | (dow << 16)`.

## The bookkeeping attributes

| Attribute | Field | Enum / meaning | Default |
| --- | --- | --- | --- |
| `RECURFROM` | `nRecalcFrom` (`TDC_RECURFROMOPTION`, `IEnums.h:211`) | `0` done-date, `1` due-date, `2` start-date | `1` (due) |
| `RECURREUSE` | `nReuse` (`TDC_RECURREUSEOPTION`, `IEnums.h:204`) | `0` reuse task, `1` create new, `2` ask | `0` (reuse) |
| `RECURNUM` | `m_nNumOccur` | total occurrences; `-1` = infinite | `-1` |
| `RECURREMAINING` | `m_nRemainingOccur` | occurrences left; `-1` = infinite | `-1` |
| `RECURPRESERVECOMMENTS` | `bPreserveComments` | keep comments across recurrence (BOOL) | `1` |

On read (`TaskFile.cpp:3043`) `FREQ`/`REUSE`/`SPEC1`/`SPEC2` are unconditional; `FROM`/`NUM`/
`REMAINING`/`PRESERVECOMMENTS` are read only `if HasItem(...)`, falling back to the defaults above. On
write (`SetTaskRecurrence`) all eight are emitted.

## How a recurring task advances on completion — **not our concern, and a real gotcha**

The recurrence *engine* (`CRecurrence::GetNextOccurence`, `Recurrence.cpp`) lives above the file
layer. It runs inside the app (`CToDoCtrl`) when a task is marked done: depending on `RECURREUSE` it
either resets the same task's dates to the next occurrence (reuse) or spawns a fresh copy, and
decrements `RECURREMAINING`. **We should not reimplement any of this** — only read/write the rule.

Consequence for this server: our `CompleteTask` just stamps `DONEDATE`
([`TodoListDocument.cs:546`](../src/TodoListMcp.Core/TodoListDocument.cs)). Writing `DONEDATE` on a
recurring task through the file **does not advance it** — no next instance is created, `RECURREMAINING`
is untouched. Advancement only happens when completion goes through the ToDoList UI. This is a
behavioural mismatch worth documenting for whoever picks this up: completing a recurring task via the
MCP server silently ends the series from ToDoList's perspective until the app re-opens/edits it.

## Risks

- **Invalid rules are easy to write and fail silently.** A frequency whose `SPEC1`/`SPEC2` don't match
  its scheme (e.g. weekly with no weekday bits, or a bogus day-of-month) may be dropped or coerced by
  ToDoList. `CRecurrence::IsValidRegularity` gates this internally, so a writer must mirror those
  checks or round-trip-test every emitted shape against a real install.
- **Two encodings share slots** (plain month vs. `DHM_*`; the `LOWORD`/`HIWORD` pack) — a naive reader
  will misinterpret them.
- **Deprecated ordinals (6, 7, 9, 11)** can appear in old files; a reader must tolerate them (surface
  as "unsupported/legacy") and a writer must never emit them.
- Completion mismatch above.

## Recommendation — scope

Do it in two tiers, read-first:

1. **Read-only decode (all frequencies).** Cheap and safe: parse the seven integers into a structured
   shape + a friendly description, tolerating deprecated/unknown ordinals. No round-trip risk. This
   alone answers "does this task repeat, how, and how often" for every existing file.
2. **Constrained writer for the common cases.** Support the shapes an LLM will actually ask for and
   that have unambiguous payloads:
   - every N days (`1`), every weekday (`5`), every N weekdays (`16`)
   - weekly on given weekdays, every N weeks (`2`); every N weeks (`12`)
   - monthly on day-of-month, every N months (`3`); every N months (`13`)
   - yearly on month+day (`4`); every N years (`14`)

   **Defer** the `nth-weekday` (`8`, `10`) and first/last-weekday (`15`) patterns — the
   `LOWORD`/`HIWORD` packing and mask ambiguity make them error-prone for little demand. Always write
   the full eight attributes with the defaults above, regenerate the label body via the same
   five-string mapping, and validate against `IsValidRegularity`'s rules before writing.

**Do not** implement completion-advancement. If anything, `complete_task` on a recurring task should
warn (or refuse) rather than silently orphan the series — a small follow-up worth its own decision.

## Verified source references

- `Core/Shared/Recurrence.h` — `enum RECURRENCE_REGULARITY`; struct comment table for `dwSpecific1/2`.
- `Core/Interfaces/IEnums.h:167` — `TDC_REGULARITY` (`TDIR_*`, the on-disk `RECURFREQ`); `:204`
  `TDC_RECURREUSEOPTION`; `:211` `TDC_RECURFROMOPTION`.
- `Core/Shared/DateHelperEnums.h:53` — `DH_DAYOFWEEK` (`DHW_*`); `:69` `DH_MONTH` (`DHM_*`).
- `Core/ToDoList/TaskFile.cpp:3031` — `GetTaskRecurrence` (read); `:4103` `SetTaskRecurrence` (write);
  `:4077` label body written from `GetRegularityText()`.
- `Core/ToDoList/TDCRecurrence.cpp` — `GetRegularityText` (five-string collapse); `Set/GetRegularity`.
- `Core/Shared/Recurrence.cpp` — `GetNextOccurence` (the advance engine we won't touch).
