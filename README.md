# TodoList MCP Win (An MCP server for AbstractSpoon ToDoList)

A Windows system-tray application that exposes [AbstractSpoon ToDoList](https://abstractspoon.com/)
`.tdl` files to MCP clients (Claude Desktop, Claude Code, etc.) over a local **Streamable HTTP**
endpoint. It can serve any number of `.tdl` files listed in a configuration file.

The tray app - rather than a stdio MCP server - is a persistent host you
start once and leave running, with a visible status icon and quick access to its config and logs.

## Solution layout

| Project | Target | Role |
| --- | --- | --- |
| `src/TodoListMcp.Core` | `net10.0` | Format-faithful `.tdl` read/write engine. No Windows/UI dependencies, so it is fully unit-testable. |
| `src/TodoListMcp.App` | `net10.0-windows` | WinForms tray icon + ASP.NET Core host running the MCP server, plus the MCP tool surface. |
| `tests/TodoListMcp.Core.Tests` | `net10.0` | xUnit suite covering the TDL operations, including a round-trip against a real ToDoList file. |
| `tests/TodoListMcp.App.Tests` | `net10.0-windows` | xUnit suite covering the App layer's list-alias resolution (default, single-file, ambiguous, unknown, and empty-config paths). |

### Why a separate Core library

All `.tdl` correctness lives in `TodoListMcp.Core` with **no** UI or hosting dependencies. That keeps
the format logic unit-testable in isolation and means the tray/host layer is a thin adapter.

## ToDoList format fidelity

The engine mirrors how ToDoList actually stores data (verified against a real export, FILEFORMAT 12):

- **UTF-16 (LE, with BOM)** on save, declaring `encoding="utf-16"`.
- Dates are **OLE-automation serials** (`DateTime.FromOADate` / `ToOADate`).
- **`PRIORITY` and `RISK` are the native 0-10 scales** (`-2` = none), with no lossy bucketing.
- **`STATUS`, `VERSION`, `EXTERNALID`, `ALLOCATEDBY`** (free text), the **`FLAG`** marker, and
  **`STARTDATE`** are read and written alongside due date: single-value attributes mirroring how
  ToDoList stores them.
- **Effort** is a value + unit pair: `TIMEESTIMATE`/`TIMEESTUNITS` and `TIMESPENT`/`TIMESPENTUNITS`
  (a plain decimal in the unit, *not* an OLE serial). Units are single letters: `I` minutes, `H` hours
  (default), `D` days, `K` weekdays, `W` weeks, `M` months, `Y` years. The tools also accept the words.
  The derived `CALC*` rollups are ToDoList's to compute, so they're left untouched and never written.
- Notes are the **`<COMMENTS>` child element** (not an attribute), with the format in `COMMENTSTYPE`.
  This server reads tasks in **any** comment format but **authors only plain text** — see
  [Task comments / notes](#task-comments--notes) for exactly what happens on read and write.
- Assignees are **`<PERSON>` child elements** (or a single `ALLOCATEDTO` attribute); categories are
  **`<CATEGORY>`** likewise. Root-level pick-lists are *not* mistaken for per-task assignments.
- Completion is detected from **`DONEDATE`** (the source of truth). ToDoList's calculated
  **`GOODASDONE`** flag (set by the "treat parents with all subtasks completed as done" option) is
  surfaced read-only as `IsGoodAsDone`, and kept in sync when this server completes/reopens a task.
- ToDoList's **`LOCK`** (read-only) marker is surfaced read-only as `IsLocked`, and the server
  honours it the way ToDoList does: it **refuses to update, complete, reopen, move, or delete a
  locked task**, refuses to **move or delete a task whose immediate parent is locked**, and refuses
  to **move a task into a locked parent**. (A locked *descendant* does not block deleting or moving
  an ancestor, and editing an unlocked child of a locked parent is allowed — mirroring ToDoList.)
- `POS`/`POSSTRING` are renumbered to match document order on structural edits, using the same scheme
  ToDoList writes in live files (it orders by document order; the stored values are derived).
- Unknown attributes/elements are **preserved** across a load → modify → save round-trip (mutations
  edit the loaded XML tree in place rather than regenerating it).

## Task comments / notes

ToDoList stores a task's comments in up to three parts: a plain-text `<COMMENTS>` element (always
present — it's what search and CSV export use), a `COMMENTSTYPE` format id, and, for non-plain
formats, an opaque `<CUSTOMCOMMENTS>` payload that holds the actual rich content. The format is a
pluggable "content control". This server **authors plain text, Markdown, and HTML** (pass
`commentsFormat` to `add_task`/`update_task`) and reports each task's format on read as
`CommentsFormat`.

When you author Markdown or HTML, the source is stored in `<CUSTOMCOMMENTS>` exactly as ToDoList
encodes it (base64 of the UTF-16LE source bytes, no BOM), with a plain-text mirror in `<COMMENTS>`.
The mirror matches how ToDoList derives it — the rendered text with markup removed (ToDoList uses the
content control's `innerText`) — and ToDoList refreshes it itself on its next save. Rich Text and
Spreadsheet stay read-only — their payloads are opaque (WordPad RTF / a ReoGrid workbook).

> [!WARNING]
> - **Reading a formatted task gives you the flattened plain-text mirror, _not_ the rich source.**
>   Check `CommentsFormat` — anything other than `plain` means the `Comments` text is the rendered
>   text with markup removed (HTML tags / Markdown syntax stripped), not what you'd edit in ToDoList.
> - **Overwriting a task's existing formatted notes is refused by default** — whatever new format
>   you supply. Pass `replaceFormattedComments: true` to replace them anyway; this **discards
>   ToDoList's existing rich `<CUSTOMCOMMENTS>` payload**. (Clearing the notes with an empty string is
>   gated the same way.)
> - Editing a formatted task's **other** fields never touches its comments — the rich payload is
>   round-tripped untouched.

| Format | `COMMENTSTYPE` | Read | Author (`commentsFormat`) | On `update_task` notes |
| --- | --- | --- | --- | --- |
| Plain text | `PLAIN_TEXT` | ✅ full | ✅ `plain` (default) | Replaced normally. |
| Markdown | `BAA4E079-…` | ⚠️ flattened mirror | ✅ `markdown` | Refused unless `replaceFormattedComments`. |
| HTML | `FE0B6B6E-…` | ⚠️ flattened mirror | ✅ `html` | Refused unless `replaceFormattedComments`. |
| Rich Text (RTF) | `849CF988-…` | ⚠️ flattened mirror | ❌ | Refused unless `replaceFormattedComments` (then replaced in the format you give). |
| Spreadsheet | `BBDCAEDF-…` | ⚠️ flattened mirror | ❌ | As above. |
| Other content control | (its GUID) | ⚠️ flattened mirror; `CommentsFormat` is the raw id | ❌ | As above. |

## Requirements

- Windows 10/11
- **To run**: both the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
  **and** the ASP.NET Core 10 Runtime (the app hosts its MCP server over Kestrel). Release downloads
  are framework-dependent, so both must be installed before running them. They are the "Desktop
  Runtime" and "ASP.NET Core Runtime" installers on that download page.
- **To build from source**: the .NET SDK 10 (it includes both runtimes).

## Build & test

```bash
dotnet build
dotnet test
```

The app icon (a white check mark on a blue rounded square) is drawn in code by `TrayIconFactory`,
which is the single source of truth. The tray icon is rendered from that code at runtime; the
committed `src/TodoListMcp.App/Resources/App.ico` is the **executable** icon (Explorer, taskbar,
Alt-Tab), generated from the same drawing, so the two always match. Regenerate the asset with:

```bash
TodoListMcp.exe --write-icon src/TodoListMcp.App/Resources/App.ico
```

### Versioning

The version is derived from Git on every build by [MinVer](https://github.com/adamralph/minver)
(configured in `Directory.Build.props`). A commit tagged `vX.Y.Z` builds as exactly `X.Y.Z`; any
other commit builds as the next patch pre-release with the number of commits since the tag as height
(e.g. `0.0.3-alpha.0.5`), and the commit hash is appended to the informational version
(`0.0.3-alpha.0.5+74cab42…`). So tagged release commits carry a clean version and every other build
identifies its commit. The running build's version is shown in the tray menu under **About TodoList
MCP…**, and in the file's Product version. To cut a release, tag and push — the
[release workflow](.github/workflows/release.yml) reads the tag through MinVer:

```bash
git tag v1.2.3 && git push origin v1.2.3
```

A source tree with no `.git` directory (e.g. an extracted zip) has no tags to read, so it falls back
to `0.0.0-alpha.0`.

## Configure

On first launch the app writes a starter config to:

```
%APPDATA%\TodoListMcp\config.json
```

Edit it (tray menu → **Open configuration…**). Changes to the file list (aliases, paths, default)
and `ModifiedBy` are picked up live. `Port`, `UseHttps`, and `TrustCertificate` are read once at
startup, so changing those needs an app restart.

```json
{
  "TodoListMcp": {
    "Port": 3001,
    "UseHttps": false,
    "TrustCertificate": true,
    "ModifiedBy": "TodoListMcp",
    "Files": [
      { "Alias": "work",     "Path": "D:\\Lists\\work.tdl",     "Default": true },
      { "Alias": "personal", "Path": "D:\\Lists\\personal.tdl" }
    ]
  }
}
```

- **Alias**: the short name tool callers pass as `list`. Omit `list` in a call to use the `Default`
  file (or the only file, if just one is configured). Each alias must be unique and at most one file
  may be `Default`; the server refuses to act on ambiguous config rather than guess which file you meant.
- **Port**: loopback TCP port; the server binds `127.0.0.1`/`::1` only.
- **UseHttps**: serve over HTTPS. Off by default: the server is loopback-only, so plain HTTP never
  leaves your machine and skips the certificate step. Set `true` to enable TLS; see
  [Connect Claude](#connect-claude).
- **TrustCertificate**: install the localhost certificate into your current-user Trusted Root store
  (default). First install shows a one-time Windows consent prompt; no admin needed. Node-based
  clients need one more step to honour it; see [Connect Claude](#connect-claude).
- **ModifiedBy**: written to each task's `LASTMODBY` when this server changes it.

## HTTPS

HTTPS is off by default: the endpoint is loopback-only, so plain HTTP never leaves your machine. If
you enable it (`"UseHttps": true`), on first run the app:

1. generates a self-signed certificate for `localhost` (SAN: `localhost`, `127.0.0.1`, `::1`), valid
   5 years, persisted at `%APPDATA%\TodoListMcp\todolistmcp-localhost.pfx`;
2. with `TrustCertificate` on, installs it into your **current-user Trusted Root** store (one-time
   consent prompt) so programs that read the Windows certificate store trust it.

If you skipped the prompt, re-run it any time from the tray: **Trust HTTPS certificate (for Claude)…**.
Node-based clients (Claude Code, and the `mcp-remote` bridge) need one extra setting to honour that
certificate; see [Connect Claude](#connect-claude).

## Run

```bash
dotnet run --project src/TodoListMcp.App
```

A tray icon appears. Right-click for: server URL, the configured lists, copy URL, open
configuration, open log folder, **Start with Windows**, and exit, plus trust HTTPS certificate when
HTTPS is enabled. Logs roll daily under `%APPDATA%\TodoListMcp\logs`.

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

## Connect Claude

The server speaks MCP over **Streamable HTTP** at the root path: `http(s)://localhost:<Port>/`,
port `3001` by default. It listens on loopback only, so it's reachable from programs on this machine
but not from the network.

### HTTP or HTTPS?

Because the endpoint is loopback-only, **plain HTTP is the simplest option and nothing leaves your
computer**, so it's the default. HTTPS also works, but its self-signed certificate needs an extra
step for Node-based clients, so reach for it only if you specifically want TLS locally.

- **HTTP** (default): use `http://localhost:3001/`.
- **HTTPS**: set `"UseHttps": true`, restart the app, follow the certificate step below, and use
  `https://localhost:3001/`.

### Claude Code

Register the server with the `http` transport, at *user* scope so it's available from every
directory (the default scope is local to wherever you run the command):

```bash
claude mcp add --transport http --scope user todolist http://localhost:3001/
```

`--transport http` selects MCP's Streamable HTTP transport for both schemes; the URL (`http://` vs
`https://`) decides whether TLS is used. Claude Code loads MCP servers at startup, so open a fresh
session and confirm:

```bash
claude mcp get todolist        # Status: ✔ Connected
```

The tools (`get_tasks`, `add_task`, …) are then available in any session.

#### HTTPS with Claude Code: the Node certificate step

Claude Code runs on Node.js, which validates TLS against **its own bundled CA list and ignores the
Windows certificate store by default**. So even with `TrustCertificate: true` (which installs the
certificate into the Windows Trusted Root store), Node rejects the self-signed certificate:

```
todolist: https://localhost:3001/ (HTTP) - ✘ Failed to connect
# DEPTH_ZERO_SELF_SIGNED_CERT: self-signed certificate
```

Tell Node to use the Windows store with `--use-system-ca` (added in Node 23.8.0 and backported to the
current v22 and v24 lines; upgrade Node if it isn't recognised), then fully restart Claude Code so
it picks up the variable:

```powershell
setx NODE_OPTIONS "--use-system-ca"
```

`claude mcp get todolist` should now report **Connected**. (This makes every Node process on your
account read the Windows store, which is harmless.) To avoid certificates altogether, use the `http://` URL
instead.

### Claude Desktop

Claude Desktop's **custom connectors** are for *remote* MCP servers: Claude reaches them from
Anthropic's infrastructure, not from your computer, so a `localhost` URL entered there won't
connect. Bridge this local server instead with [`mcp-remote`][mcp-remote], a small Node proxy, in
`claude_desktop_config.json` (open it from **Settings → Developer → Edit Config**):

```json
{
  "mcpServers": {
    "todolist": {
      "command": "npx",
      "args": ["mcp-remote", "http://localhost:3001/"]
    }
  }
}
```

The bridge keeps the connection local. Because it runs on Node, the **same certificate step as Claude
Code** applies if you point it at an `https://` URL: keep `TrustCertificate` on and set
`NODE_OPTIONS=--use-system-ca` before launching Claude Desktop. Using the `http://` URL above skips
this entirely. Restart Claude Desktop after editing the file.

[mcp-remote]: https://www.npmjs.com/package/mcp-remote

## Tools

| Tool | Purpose |
| --- | --- |
| `list_todo_files` | List the configured files and their aliases. |
| `get_tasks` | Full task hierarchy for a list. |
| `get_task` | One task (and its subtasks) by ID. |
| `search_tasks` | Filter by text, category, assignee, allocated-by, completion, flag, status, version, external ID, minimum priority/risk, or time estimate/spent range (in hours). |
| `add_task` | Create a task (title, notes + `commentsFormat`, priority, risk, % done, time estimate/spent, due/start date, status, version, flag, external ID, categories, assignees/allocated-by, parent/index). |
| `update_task` | Change fields; only supplied parameters are touched (with explicit clear flags). Editing the notes of a task with formatted comments needs `replaceFormattedComments` — see [Task comments / notes](#task-comments--notes). |
| `complete_task` / `reopen_task` | Toggle completion (`DONEDATE` + progress). |
| `delete_task` | Remove a task and its subtree. |
| `move_task` | Re-parent and/or reorder a task. |

Every tool takes an optional `list` alias; omit it to use the default list. Tasks you locked in
ToDoList (`LOCK="1"`, surfaced as `IsLocked`) are read-only: `update_task`, `complete_task`,
`reopen_task`, `move_task`, and `delete_task` refuse them with a clear error, and `move_task` /
`delete_task` also refuse a task whose immediate parent is locked (or, for moves, a locked target
parent) — matching how ToDoList itself gates these.

## Concurrency note

Each operation loads the file fresh and writes atomically (temp file + replace), with a per-file
lock. **ToDoList loads a file into memory and rewrites the whole thing when it saves**, so it **may**
not see external edits made while it has the file open, and may overwrite them on its next save.
Edit a given `.tdl` from this server only while ToDoList does not have that file open (or reload it
in ToDoList afterwards).
