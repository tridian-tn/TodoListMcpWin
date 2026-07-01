# TodoList MCP Win (An MCP server for AbstractSpoon ToDoList)

A Windows system-tray application that exposes [AbstractSpoon ToDoList](https://abstractspoon.com/)
`.tdl` files to MCP clients (Claude Desktop, Claude Code, Codex etc.) over a local **Streamable HTTP**
endpoint. It can serve any number of `.tdl` files listed in a configuration file.

The tray app - rather than a stdio MCP server - is a persistent host you
start once and leave running, with a visible status icon and quick access to its config and logs.

## Requirements

- Windows 10/11
- **To run**: both the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
  **and** the ASP.NET Core 10 Runtime (the app hosts its MCP server over Kestrel). Release downloads
  are framework-dependent, so both must be installed before running them. They are the "Desktop
  Runtime" and "ASP.NET Core Runtime" installers on that download page.
- **To build from source**: the .NET SDK 10 (it includes both runtimes).

## Install and run (from a release)

No build tools or development experience needed — install two free Microsoft runtimes, then download
and unzip the app.

1. **Install the .NET 10 runtimes.** From the [.NET 10 download page](https://dotnet.microsoft.com/download/dotnet/10.0),
   download and run **both** installers (they're free, and on the same page):
   - the **.NET Desktop Runtime** installer, and
   - the **ASP.NET Core Runtime** installer (the app hosts its MCP server over this).

   Pick the **x64** installer for a normal PC, or **Arm64** for an ARM device. You only do this once.

2. **Download the app.** Open the latest release on the
   [Releases page](https://github.com/tridian-tn/TodoListMcpWin/releases) and download the zip for
   your CPU:
   - `TodoListMcp-<version>-win-x64.zip` — most PCs (Intel/AMD).
   - `TodoListMcp-<version>-win-arm64.zip` — ARM devices (e.g. Snapdragon / Surface Pro X).

   Not sure which? It's likely to be **x64**.

3. **Extract and run.** Unzip it to a folder you'll keep (e.g. `C:\Tools\TodoListMcp` — the app runs
   from wherever you put it), then double-click **`TodoListMcp.exe`**. A check-mark icon appears in
   the system tray (bottom-right, near the clock).
   - Windows SmartScreen may warn that the publisher is unknown (the release isn't code-signed).
     Click **More info → Run anyway**.

4. **Point it at your lists.** On first launch the app writes a starter config. Right-click the tray
   icon → **Open configuration…**, add your `.tdl` files, and save. The file list is picked up live —
   no restart needed. See [Configure](#configure) for the exact format.

5. **Connect an LLM.** Follow [Connect an LLM](#connect-an-llm) for setup instructions for Claude Code, Claude Desktop, and Codex.

Optional: right-click the tray icon → **Start with Windows** so it launches automatically at logon —
see [Using the tray app](#using-the-tray-app).

> Prefer to build it yourself? See [Building from source](#building-from-source) at the end.

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
    "DefaultLogMode": "combined",
    "Files": [
      { "Alias": "work",     "Path": "D:\\Lists\\work.tdl",     "Default": true },
      { "Alias": "personal", "Path": "D:\\Lists\\personal.tdl", "LogMode": "separate" }
    ]
  }
}
```

> [!IMPORTANT]
> This is a JSON file, so each backslash in a Windows path must be **doubled**: write
> `D:\\Lists\\work.tdl`, not `D:\Lists\work.tdl`. (A single forward slash, `D:/Lists/work.tdl`, also
> works if you prefer.)

- **Alias**: the short name tool callers pass as `list`. Omit `list` in a call to use the `Default`
  file (or the only file, if just one is configured). Each alias must be unique and at most one file
  may be `Default`; the server refuses to act on ambiguous config rather than guess which file you meant.
- **Port**: loopback TCP port; the server binds `127.0.0.1`/`::1` only.
- **UseHttps**: serve over HTTPS. Off by default: the server is loopback-only, so plain HTTP never
  leaves your machine and skips the certificate step. Set `true` to enable TLS; see
  [Connect an LLM](#connect-an-llm).
- **TrustCertificate**: install the localhost certificate into your current-user Trusted Root store
  (default). First install shows a one-time Windows consent prompt; no admin needed. Node-based
  clients need one more step to honour it; see [Connect an LLM](#connect-an-llm).
- **ModifiedBy**: written to each task's `LASTMODBY` when this server changes it.
- **DefaultLogMode**: the [logged-time](#logged-time) layout for lists that don't set their own —
  `combined` (default) or `separate`. Mirrors ToDoList's global "log tasks separately" preference,
  which this server can't read, so it's configured here. Picked up live.
- **LogMode** (per file): overrides `DefaultLogMode` for one list. Omit to inherit the global default.

## HTTPS

HTTPS is off by default: the endpoint is loopback-only, so plain HTTP never leaves your machine. If
you enable it (`"UseHttps": true`), on first run the app:

1. generates a self-signed certificate for `localhost` (SAN: `localhost`, `127.0.0.1`, `::1`), valid
   5 years, persisted at `%APPDATA%\TodoListMcp\todolistmcp-localhost.pfx`;
2. with `TrustCertificate` on, installs it into your **current-user Trusted Root** store (one-time
   consent prompt) so programs that read the Windows certificate store trust it.

If you skipped the prompt, re-run it any time from the tray: **Trust HTTPS certificate (for Claude)…**.
Node-based clients (Claude Code, and the `mcp-remote` bridge) need one extra setting to honour that
certificate; see [Connect an LLM](#connect-an-llm).

## Connect an LLM

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

> [!NOTE]
> The Claude Desktop / `mcp-remote` route above is **untested** — it's the expected setup based on how
> `mcp-remote` bridges a local server, not something that's been verified end to end here. The Claude
> Code path is the tested one. If you try it, feedback is welcome.

[mcp-remote]: https://www.npmjs.com/package/mcp-remote

### Codex

With the Codex desktop application, enter the Settings, navigate to Integrations->MCP Servers and click Add Server.

* The transport is "Streamable HTTP", not "STDIO"
* Enter the URL: `http://localhost:3001/` (replace `3001` with your configured `Port`; use `https://` if `UseHttps` is enabled)
* Click Save

## Using the tray app

Launch it by double-clicking **`TodoListMcp.exe`**. (Running from a source checkout instead? See
[Building from source](#building-from-source).)

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

## Tools

| Tool | Purpose |
| --- | --- |
| `list_todo_files` | List the configured files, their aliases, and each list's effective time-log mode. |
| `get_tasks` | Full task hierarchy for a list. |
| `get_task` | One task (and its subtasks) by ID. |
| `search_tasks` | Filter by text, category, assignee, allocated-by, completion, flag, status, version, external ID, minimum priority/risk, or time estimate/spent range (in hours). |
| `add_task` | Create a task (title, notes + `commentsFormat`, priority, risk, % done, time estimate/spent, due/start date, status, version, flag, external ID, categories, assignees/allocated-by, file links, parent/index). |
| `update_task` | Change fields; only supplied parameters are touched (with explicit clear flags). Editing the notes of a task with formatted comments needs `replaceFormattedComments` — see [Task comments / notes](#task-comments--notes). |
| `complete_task` / `reopen_task` | Toggle completion (`DONEDATE` + progress). |
| `delete_task` | Remove a task and its subtree. |
| `move_task` | Re-parent and/or reorder a task. |
| `add_dependency` / `remove_dependency` | Add or remove a task-ordering dependency (`DEPENDS`) on another task in the same list, with an optional lead-in/lag in days. |
| `log_time` | Append a time-log entry to the list's `_Log.csv` sidecar (task or task-less), optionally also incrementing the task's time spent — see [Logged time](#logged-time). |
| `get_time_log` | Read time-log entries from the sidecar, filtered by task, date range, or person. |
| `update_time_log_entry` | Edit a single existing sidecar entry (identified by its current fields), changing only the values you supply. |
| `delete_time_log_entry` | Delete a single existing sidecar entry (identified by its fields). |

Every tool takes an optional `list` alias; omit it to use the default list. Tasks you locked in
ToDoList (`LOCK="1"`, surfaced as `IsLocked`) are read-only: `update_task`, `complete_task`,
`reopen_task`, `move_task`, and `delete_task` refuse them with a clear error, and `move_task` /
`delete_task` also refuse a task whose immediate parent is locked (or, for moves, a locked target
parent) — matching how ToDoList itself gates these.

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
content control's `innerText`) — and ToDoList refreshes it itself on its next save. On read, that same
source is decoded back into **`CommentsSource`** for Markdown and HTML, so a read → edit → write
round-trip is lossless; `Comments` stays the flattened mirror (what search and display use). Rich Text
and Spreadsheet stay read-only — their payloads are opaque (WordPad RTF / a ReoGrid workbook), so they
expose only the mirror and `CommentsSource` is null.

> [!WARNING]
> - **`Comments` is always the flattened plain-text mirror, _not_ the rich source** — the rendered
>   text with markup removed (HTML tags / Markdown syntax stripped). For **Markdown and HTML** the
>   editable source is recovered into **`CommentsSource`** (use that to round-trip an edit). For
>   **Rich Text and Spreadsheet** the payload is opaque, so `CommentsSource` is null and the mirror is
>   all you get. Check `CommentsFormat` to tell which you're holding.
> - **Overwriting a task's existing formatted notes is refused by default** — whatever new format
>   you supply. Pass `replaceFormattedComments: true` to replace them anyway; this **discards
>   ToDoList's existing rich `<CUSTOMCOMMENTS>` payload**. (Clearing the notes with an empty string is
>   gated the same way.)
> - Editing a formatted task's **other** fields never touches its comments — the rich payload is
>   round-tripped untouched.

To edit a Markdown or HTML task without losing its formatting, read `CommentsSource`, change it, and
write it back in the same format — opting past the overwrite guard:

```
get_task(id) → { CommentsFormat: "markdown", CommentsSource: "# Plan\n\n- **a**" , Comments: "Plan\na" }
update_task(id, comments: editedSource, commentsFormat: "markdown", replaceFormattedComments: true)
```

| Format | `COMMENTSTYPE` | Read | Author (`commentsFormat`) | On `update_task` notes |
| --- | --- | --- | --- | --- |
| Plain text | `PLAIN_TEXT` | ✅ full (`Comments`) | ✅ `plain` (default) | Replaced normally. |
| Markdown | `BAA4E079-…` | ✅ full source in `CommentsSource` (+ mirror in `Comments`) | ✅ `markdown` | Refused unless `replaceFormattedComments`. |
| HTML | `FE0B6B6E-…` | ✅ full source in `CommentsSource` (+ mirror in `Comments`) | ✅ `html` | Refused unless `replaceFormattedComments`. |
| Rich Text (RTF) | `849CF988-…` | ⚠️ flattened mirror only | ❌ | Refused unless `replaceFormattedComments` (then replaced in the format you give). |
| Spreadsheet | `BBDCAEDF-…` | ⚠️ flattened mirror only | ❌ | As above. |
| Other content control | (its GUID) | ⚠️ flattened mirror only; `CommentsFormat` is the raw id | ❌ | As above. |

## Logged time

ToDoList keeps a **time log** — a structured record of individual work sessions and manual
adjustments — in a CSV file beside the `.tdl`, named `<listname>_Log.csv` (e.g. `Tasks.tdl` →
`Tasks_Log.csv`). This is **separate from a task's `TimeSpent` attribute**: the log is an
append-style audit trail of periods, each optionally tied to a task, while `TimeSpent` is a single
rolled-up total. `log_time` appends an entry; `get_time_log` reads them back.

- **Task or task-less.** Omit `taskId` (or pass `0`) to log time against no task at all — useful for
  capturing work that isn't yet a task. An entry is valid when it has a `comment`, or non-zero
  `hours` over a valid period.
- **The period.** Pass `when` (the end of the period) and the start is derived as `when − hours`,
  mirroring ToDoList's dialog; or give explicit `from`/`to`. Omit them and the entry ends now.
- **Person** defaults to the current OS user (as ToDoList does); override with `person`.
- **`type`** is `Adjusted` (a manual entry, the default) or `Tracked` (a timer session).
- **`addToTimeSpent`** — when logging against a task, also add the hours to that task's `TimeSpent`
  (keeping the task's existing unit), mirroring the dialog's "Add to time spent" checkbox. The `.tdl`
  and the sidecar are written under the same per-list lock, sidecar first — so an interrupted write
  leaves the log entry without its `TimeSpent` bump, never an inflated `TimeSpent` with no log entry.
- **Reading back** — `get_time_log` filters by `taskId`, `person`, and a `since`/`until` date range.
  A bare `until` date is inclusive of that whole day, so `since` = `until` = today returns everything
  logged today (an explicit time on `until` is used as-is).
- **Editing / deleting** — `update_time_log_entry` and `delete_time_log_entry` change or remove a
  single existing entry. The format has no stable row ID, so you **identify the entry by its current
  fields** (`taskId`, `from`, `to`, `person`, `comment`, `hours`); these are AND-combined and must
  match **exactly one** entry — no match, or an ambiguous one (more than one), is an error rather than
  touching the wrong row. Read the entry with `get_time_log` first to get its exact fields.
  `update_time_log_entry` changes only the `new*` values you pass (any you omit keep their current
  value; pass an empty string to clear a comment/person/type) and re-serialises that row in the latest
  layout; **untouched rows
  still round-trip verbatim**. Both touch only the sidecar — a task's `TimeSpent` is never adjusted to
  follow an edit or delete. For example, to move today's entry to start at 10:00:

  ```
  update_time_log_entry(comment: "NOTHING DONE", newFrom: "2026-06-29 10:00", newTo: "2026-06-29 18:00")
  ```

### Combined vs separate layout

ToDoList can keep the log in one of two on-disk layouts, chosen by its global "log tasks separately"
preference. Since that preference lives in ToDoList's own settings (not the `.tdl`), the server can't
read it — you mirror it with [`DefaultLogMode`](#configuration) (and an optional per-list `LogMode`):

| Mode | Layout (for `Tasks.tdl`) |
| --- | --- |
| `combined` (default) | one `Tasks_Log.csv` beside the `.tdl` |
| `separate` | a `Tasks\` folder with one `<taskID>_Log.csv` per task (task-less entries → `Tasks\0_Log.csv`) |

- **Reads are mode-agnostic.** `get_time_log` (and the edit/delete selectors) always union the
  combined file **and** every per-task file, exactly as ToDoList's own analysis does — so a list logged
  in either layout, or a mix of both, reads back in full regardless of the configured mode.
- **Writes follow the mode.** `log_time` writes a new entry to the combined file, or to the task's
  per-task file in separate mode (creating the `Tasks\` folder on first use). `list_todo_files` reports
  each list's effective mode.
- **Edit/delete route to the owning file.** In separate mode an entry lives in exactly one per-task
  file; the selector must still match exactly one entry across *all* the files, and only that file is
  rewritten.
- **Mismatch warning.** If the configured mode disagrees with what's already on disk (e.g. `combined`
  but per-task files exist), the server logs a warning — it never silently guesses from the filesystem.

The sidecar is written as ToDoList writes it: UTF-16, a `TODOTIMELOG VERSION 1` line, a header row,
then tab-separated rows (`Task ID, Title, User ID, Start/End Date/Time, Time Spent (Hrs), Comment,
Type, Path, Colour`). Each file — combined or per-task — carries its own version line and header.
Existing rows — including older-format ones — are preserved verbatim when a new entry is appended, or
when another entry is edited or deleted; only a row you actually edit is rewritten (in the latest
layout).

## Concurrency note

Each operation loads the file fresh and writes atomically (temp file + replace), with a per-file
lock. **ToDoList loads a file into memory and rewrites the whole thing when it saves**, so it **may**
not see external edits made while it has the file open, and may overwrite them on its next save.
Edit a given `.tdl` from this server only while ToDoList does not have that file open (or reload it
in ToDoList afterwards).

---

The rest of this document covers the internals and building from source — you don't need any of it to
install and use the app.

## Building from source

```bash
dotnet build
dotnet test
```

Run it straight from the checkout (no need to publish a release first):

```bash
dotnet run --project src/TodoListMcp.App
```

The tray icon appears just as it does for a release build; see [Using the tray app](#using-the-tray-app)
for what the menu offers.

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
- **Logged time** is a separate concern from `TIMESPENT`: a structured CSV sidecar
  (`<listname>_Log.csv`, or per-task `<listname>\<taskID>_Log.csv` in separate mode) of individual
  time entries, not part of the `.tdl` XML. Read and appended with the same fidelity discipline
  (UTF-16, `TODOTIMELOG VERSION 1` + header, tab-separated rows, value encoding, atomic write); entries
  can also be edited and deleted, preserving every untouched row verbatim — see
  [Logged time](#logged-time).
- Notes are the **`<COMMENTS>` child element** (not an attribute), with the format in `COMMENTSTYPE`.
  This server reads tasks in **any** comment format and **authors plain text, Markdown and HTML**;
  Markdown/HTML also expose their editable source for lossless round-trips — see
  [Task comments / notes](#task-comments--notes) for exactly what happens on read and write.
- Assignees are **`<PERSON>` child elements** (or a single `ALLOCATEDTO` attribute); categories are
  **`<CATEGORY>`** likewise, and file/URL links (the "File Link" field) are **`<FILEREFPATH>`**.
  Like ToDoList, this server always **writes** these as repeated child elements — even a single value —
  and reads the legacy single-attribute form too. Exact duplicates are collapsed case-insensitively
  (as ToDoList does); file links are stored **verbatim** otherwise — no trimming or path normalisation.
  Root-level pick-lists are *not* mistaken for per-task assignments.
- Task-ordering dependencies are **`<DEPENDS>` child elements** — the element body is the local
  dependee task ID, with an optional `DEPENDSLEADIN` attribute (a lead/lag in days). Surfaced as
  `Dependencies` (id + optional lead-in) and edited with `add_dependency`/`remove_dependency`, which
  validate the dependee exists in the same list and reject self-dependency (cycle checking is left to
  ToDoList). Dependencies on a task no longer in the list are **weeded out on read** and **pruned from
  every dependent when their target is deleted** — both mirroring ToDoList (a plain round-trip keeps a
  stale reference on disk until a delete removes it). Cross-tasklist (`tasklist?id`) references are
  preserved on round-trip but not surfaced or authored.
- Recurrence is a **`<RECURRENCE>`** element encoding a rule as three coded integers (`RECURFREQ` +
  `RECURSPECIFIC1`/`2`) plus bookkeeping attributes. It is **decoded read-only** onto `Recurrence` —
  every frequency (daily/weekly/monthly/yearly and their variants) is surfaced as a machine `Pattern`,
  a human `Description`, and structured fields (interval, weekdays, day-of-month, months, etc.), with
  deprecated/unknown frequencies reported as `unsupported`. The server does **not** author recurrence,
  and completing a recurring task here writes only `DONEDATE` — it does **not** advance the series
  (that happens only inside the ToDoList app). See [`docs/recurrence-spike.md`](docs/recurrence-spike.md).
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
