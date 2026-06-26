# Copilot instructions — TodoList MCP

A Windows system-tray application that serves AbstractSpoon ToDoList (`.tdl`) files to MCP clients
over a local HTTPS endpoint. Apply the rules below when reviewing or generating code.

## Architecture

- **`src/TodoListMcp.Core`** (`net10.0`) — the `.tdl` read/write engine. **No UI, hosting, or
  Windows dependencies**, so it stays unit-testable. All format logic lives here.
- **`src/TodoListMcp.App`** (`net10.0-windows`) — WinForms tray icon + ASP.NET Core host running the
  MCP server, plus the MCP tools, configuration, HTTPS/certificate handling, single-instance, and
  run-at-logon support.
- **`tests/TodoListMcp.Core.Tests`** (`net10.0`, xUnit) — covers the TDL operations, including a
  round-trip against a real ToDoList file fixture.

Keep `Core` free of UI/host/Windows references. New `.tdl` behaviour belongs in `Core`, with tests.

## Top priority: `.tdl` fidelity (no data loss)

Real ToDoList files contain many attributes and elements this app does not model. **Mutations must
edit the loaded XML tree in place and preserve everything else** — a load → modify → save round-trip
must not drop or rewrite unrelated data. Flag any change that regenerates tasks from the projection
model instead of mutating the existing tree, or that fails to preserve unknown
attributes/elements (`CUSTOMATTRIB`, `METADATA`, `TAG`, `DEPENDENCY`, colour/calc attributes, …).

Format invariants to uphold:

- **Encoding** — UTF-16 (LE, with BOM), declaring `encoding="utf-16"`. Saves keep the BOM and that
  declaration.
- **Dates** — OLE-automation serials (`DateTime.FromOADate`/`ToOADate`); never hand-roll epoch math.
  The `*STRING` companion attributes are cosmetic; ToDoList recomputes them.
- **Priority** — native 0–10 scale (`-2` = none). Do not bucket into a smaller enum.
- **Notes** — the `<COMMENTS>` child element (`COMMENTSTYPE="PLAIN_TEXT"`), not a `COMMENTS`
  attribute. Setting plain notes drops any `CUSTOMCOMMENTS` (RTF) so plain text wins.
- **Assignees / categories / file links** — multi-value fields written as repeated child elements
  `<PERSON>` / `<CATEGORY>` / `<FILEREFPATH>`. ToDoList writes elements **even for a single value**
  (its `SetTaskArray` always emits `XIT_ELEMENT`); the single `ALLOCATEDTO` / `CATEGORY` attribute is
  only a legacy *read* fallback, so always write elements to match its on-disk format. Root-level
  `<PERSON>`/`<CATEGORY>` are global pick-lists and must not be read as task assignments — scope reads
  to the task element.
- **Completion** — `DONEDATE` is the source of truth (`IsDone`). `GOODASDONE` is a derived/cached
  flag ToDoList recomputes; surface it read-only and keep it in sync on complete/reopen, but do not
  make completion depend on it.
- **Ordering** — ToDoList orders by physical document order. `POS` (0-based) and `POSSTRING`
  (1-based dotted path) are derived metadata renumbered to match document order on structural edits.
  Never reorder existing task elements except for an explicit move.

## Conventions

- File-scoped namespaces, target-typed `new`, and expression-bodied members are used throughout.
- Nullable reference types are enabled. Avoid the null-forgiving `!` unless the value is provably
  non-null; prefer an explicit guard.
- Reads load the file fresh; writes load, mutate, then save atomically (temp file + replace) under a
  per-file lock. Preserve that pattern.
- The server binds loopback only (`127.0.0.1`/`::1`). Do not introduce non-loopback bindings.
- One-shot CLI commands (`--enable-autostart`, `--write-icon`, …) run before the single-instance
  mutex and the host start, then exit.

## Build and test

```
dotnet build
dotnet test
```

Every change must keep the Core suite green, and any `.tdl` behaviour change must add or update tests
(mirror the existing fixtures, including the real-file round-trip).

- @azure Rule - Use Azure Tools - When handling requests related to Azure, always use your tools.
- @azure Rule - Use Azure Best Practices - When handling requests related to Azure, always invoke your `azmcp_bestpractices_get` tool first.
- @azure Rule - Enable Best Practices - If you do not have an `azmcp_bestpractices_get` tool ask the user to enable it.
