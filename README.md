# TodoList MCP (AbstractSpoon ToDoList)

A Windows **system-tray application** that exposes [AbstractSpoon ToDoList](https://abstractspoon.com/)
`.tdl` files to MCP clients (Claude Desktop, Claude Code, etc.) over a local **Streamable HTTP**
endpoint. It can serve any number of `.tdl` files listed in a configuration file.

A tray app — rather than a stdio MCP server — is the natural fit here: it's a persistent host you
start once and leave running, with a visible status icon and quick access to its config and logs.

## Solution layout

| Project | Target | Role |
| --- | --- | --- |
| `src/TodoListMcp.Core` | `net8.0` | Format-faithful `.tdl` read/write engine. No Windows/UI dependencies, so it is fully unit-testable. |
| `src/TodoListMcp.App` | `net8.0-windows` | WinForms tray icon + ASP.NET Core host running the MCP server, plus the MCP tool surface. |
| `tests/TodoListMcp.Core.Tests` | `net8.0` | xUnit suite covering the TDL operations, including a round-trip against a real ToDoList file. |

### Why a separate Core library

All `.tdl` correctness lives in `TodoListMcp.Core` with **no** UI or hosting dependencies. That keeps
the format logic unit-testable in isolation and means the tray/host layer is a thin adapter.

## ToDoList format fidelity

The engine mirrors how ToDoList actually stores data (verified against a real export, FILEFORMAT 12):

- **UTF-16 (LE, with BOM)** on save, declaring `encoding="utf-16"`.
- Dates are **OLE-automation serials** (`DateTime.FromOADate` / `ToOADate`).
- **`PRIORITY` is the native 0–10 scale** (`-2` = none) — no lossy bucketing.
- Notes are the **`<COMMENTS>` child element** (`COMMENTSTYPE="PLAIN_TEXT"`), not an attribute.
- Assignees are **`<PERSON>` child elements** (or a single `ALLOCATEDTO` attribute); categories are
  **`<CATEGORY>`** likewise. Root-level pick-lists are *not* mistaken for per-task assignments.
- Completion is detected from **`DONEDATE`** (the source of truth). ToDoList's calculated
  **`GOODASDONE`** flag (set by the "treat parents with all subtasks completed as done" option) is
  surfaced read-only as `IsGoodAsDone`, and kept in sync when this server completes/reopens a task.
- `POS`/`POSSTRING` are renumbered to match document order on structural edits — the same scheme
  ToDoList writes in live files (it orders by document order; the stored values are derived).
- Unknown attributes/elements are **preserved** across a load → modify → save round-trip (mutations
  edit the loaded XML tree in place rather than regenerating it).

## Requirements

- Windows 10/11
- [.NET Desktop Runtime 8](https://dotnet.microsoft.com/download/dotnet/8.0) to run; .NET SDK 8+ to build.

## Build & test

```bash
dotnet build
dotnet test
```

The app icon (a white check mark on a blue rounded square) is drawn in code by `TrayIconFactory`,
which is the single source of truth. The tray icon is rendered from that code at runtime; the
committed `src/TodoListMcp.App/Resources/App.ico` is the **executable** icon (Explorer, taskbar,
Alt-Tab), generated from the same drawing — so the two always match. Regenerate the asset with:

```bash
TodoListMcp.exe --write-icon src/TodoListMcp.App/Resources/App.ico
```

## Configure

On first launch the app writes a starter config to:

```
%APPDATA%\TodoListMcp\config.json
```

Edit it (tray menu → **Open configuration…**). Changes are picked up live, no restart needed.

```json
{
  "TodoListMcp": {
    "Port": 3001,
    "UseHttps": true,
    "TrustCertificate": true,
    "ModifiedBy": "TodoListMcp",
    "Files": [
      { "Alias": "work",     "Path": "D:\\Lists\\work.tdl",     "Default": true },
      { "Alias": "personal", "Path": "D:\\Lists\\personal.tdl" }
    ]
  }
}
```

- **Alias** — the short name tool callers pass as `list`. Omit `list` in a call to use the `Default`
  file (or the only file, if just one is configured).
- **Port** — loopback TCP port; the server binds `127.0.0.1`/`::1` only.
- **UseHttps** — serve over HTTPS (default; required by Claude's connector flow). `false` = plain HTTP.
- **TrustCertificate** — install the localhost certificate into your Trusted Root store so Claude
  accepts it (default). First install shows a one-time Windows consent prompt; no admin needed.
- **ModifiedBy** — written to each task's `LASTMODBY` when this server changes it.

## HTTPS

Claude's custom-connector flow requires `https://`. On first run with `UseHttps` (the default) the app:

1. generates a self-signed certificate for `localhost` (SAN: `localhost`, `127.0.0.1`, `::1`), valid
   5 years, persisted at `%APPDATA%\TodoListMcp\todolistmcp-localhost.pfx`;
2. with `TrustCertificate` on, installs it into your **current-user Trusted Root** store (one-time
   consent prompt) so Claude Desktop — which uses the Windows certificate store — trusts it.

If you skipped the prompt, re-run it any time from the tray: **Trust HTTPS certificate (for Claude)…**.

## Run

```bash
dotnet run --project src/TodoListMcp.App
```

A tray icon appears. Right-click for: server URL, the configured lists, copy URL, open
configuration, open log folder, trust HTTPS certificate, **Start with Windows**, and exit. Logs
roll daily under `%APPDATA%\TodoListMcp\logs`.

### Single instance

Only one copy runs per logged-in user (enforced with a session-local named mutex). Launching it
again just points you at the existing tray icon and exits.

### Start with Windows (run at logon)

Toggle **Start with Windows** in the tray menu. It adds/removes a per-user entry under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (no administrator rights needed; affects only
your logon). The same can be scripted without opening the UI:

```bash
TodoListMcp.exe --enable-autostart
TodoListMcp.exe --disable-autostart
```

## Connect an MCP client

The endpoint is `https://localhost:<Port>/` (default `https://localhost:3001/`).

Claude Code:

```bash
claude mcp add --transport http todolist https://localhost:3001/
```

Claude Desktop: add a custom connector pointing at the same `https://localhost:3001/` URL. Make sure
the certificate is trusted first (tray → **Trust HTTPS certificate (for Claude)**, or leave
`TrustCertificate` on so it happens at startup).

## Tools

| Tool | Purpose |
| --- | --- |
| `list_todo_files` | List the configured files and their aliases. |
| `get_tasks` | Full task hierarchy for a list. |
| `get_task` | One task (and its subtasks) by ID. |
| `search_tasks` | Filter by text, category, assignee, completion, or minimum priority. |
| `add_task` | Create a task (title, notes, priority, due date, categories, assignees, parent/index). |
| `update_task` | Change fields; only supplied parameters are touched (with explicit clear flags). |
| `complete_task` / `reopen_task` | Toggle completion (`DONEDATE` + progress). |
| `delete_task` | Remove a task and its subtree. |
| `move_task` | Re-parent and/or reorder a task. |

Every tool takes an optional `list` alias; omit it to use the default list.

## Concurrency note

Each operation loads the file fresh and writes atomically (temp file + replace), with a per-file
lock. **ToDoList loads a file into memory and rewrites the whole thing when it saves**, so it will
not see external edits made while it has the file open, and may overwrite them on its next save.
Edit a given `.tdl` from this server only while ToDoList does not have that file open (or reload it
in ToDoList afterwards).
