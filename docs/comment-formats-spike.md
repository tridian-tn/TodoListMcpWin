# Spike: authoring HTML & Markdown comments

Part 3 of [issue #25](https://github.com/tridian-tn/TodoListMcpWin/issues/25). Findings + go/no-go for
letting this server *write* formatted comments, not just preserve/flatten them. Verified against the
[`abstractspoon/ToDoList_9.2`](https://github.com/abstractspoon/ToDoList_9.2) source.

## How ToDoList stores a formatted comment

A task's comments are up to three parts:

- `<COMMENTS>` — the plain-text mirror (`GetTextContent()` of the content control). Used for search,
  sort and CSV export.
- `COMMENTSTYPE` — the format id (`PLAIN_TEXT` or a content-control GUID).
- `<CUSTOMCOMMENTS>` — the authoritative rich payload, **`Base64Encode(control.GetContent())`** with
  **no compression at the file layer** (`CTaskFile::SetTaskCustomComments`, `TaskFile.cpp:2138`; the
  encoder is `CBinaryData::Base64Encode`). Whatever bytes the content control emits are simply
  base64'd into the element.

So feasibility comes down to: *what bytes does each control's `GetContent()` return?*

## The key finding

Both text-native controls return **UTF-16LE bytes of their source string, no BOM, no compression**:

| Format | GUID (`COMMENTSTYPE`) | `GetContent()` (→ `<CUSTOMCOMMENTS>`) | `GetTextContent()` (→ `<COMMENTS>`) |
| --- | --- | --- | --- |
| Markdown | `BAA4E079-268B-4B9B-B7C8-6D15CCF058A2` | `Encoding.Unicode.GetBytes(InputText)` — the **markdown source** | `OutputText` — rendered/plain text |
| HTML | `FE0B6B6E-2B61-4AEB-AA0D-98DBE5942F02` | `Encoding.Unicode.GetBytes(InnerHtml)` — the **HTML source** | `InnerText` — tag-stripped text |

(`MDContentControlCore.cs:55`, `TDLHtmlEditorControl.cs:193`.) `Encoding.Unicode` is UTF-16LE
**without** a BOM; `SetContent` does `GetString(content).TrimEnd('\0').Trim()`.

This means `<CUSTOMCOMMENTS>` for these two formats is just:

```
Convert.ToBase64String(Encoding.Unicode.GetBytes(source))   // source = markdown or HTML string
```

No RTF control word soup, no zlib, no binary container. **Fully producible from a string.**

## Sync contract

`<CUSTOMCOMMENTS>` is the source of truth that ToDoList loads into the editor; `<COMMENTS>` is the
mirror it *regenerates* from `GetTextContent()` the next time the task is edited and saved in the app.
So an authored comment must set:

- `COMMENTSTYPE` = the format GUID
- `<CUSTOMCOMMENTS>` = `base64(UTF-16LE(source))`
- `<COMMENTS>` = a best-effort plain-text rendering (for *our* search/export and other tools)

The `<COMMENTS>` mirror need only be "good enough": ToDoList displays the rich content from
`<CUSTOMCOMMENTS>` regardless, and corrects the mirror on its next in-app save. For Markdown the
source itself is a reasonable mirror; for HTML we'd strip tags.

## Go / No-Go

| Format | Verdict | Why |
| --- | --- | --- |
| **Markdown** | ✅ **GO — do first** | Trivial encoding; LLMs emit markdown natively; the source doubles as a usable plain mirror. |
| **HTML** | ✅ **GO** | Same trivial encoding; needs a tag-stripper for the `<COMMENTS>` mirror. |
| Rich Text (RTF) | ❌ no-go | Opaque WordPad RTF; large effort, low payoff. |
| Spreadsheet | ❌ no-go | `unvell.ReoGrid` serialized workbook — structured/opaque. |

## Proposed follow-up implementation (separate issue)

- Tool surface: add `commentsFormat` (`plain` default / `markdown` / `html`) to `add_task` and
  `update_task`; when non-plain, write the triple above instead of forcing `PLAIN_TEXT` in
  `SetComments`. The Part 1 `replaceFormattedComments` guard still governs overwriting an existing
  formatted payload.
- A small encoder helper: `source → base64(UTF-16LE)`; an HTML→text stripper for the mirror.
- Round-trip tests: author markdown/HTML, assert `<CUSTOMCOMMENTS>` decodes back to the source and
  `COMMENTSTYPE`/`<COMMENTS>` are correct; confirm ToDoList opens the result (manual check against a
  real install).
- Open question to settle during implementation: exactly how ToDoList derives the Markdown
  `OutputText` mirror (rendered vs. raw) — affects only mirror fidelity, not whether it works.
