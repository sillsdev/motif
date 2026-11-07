# Projects and tests

The README has the one-line table. This page has the detail.

## Libraries

### `src/SIL.TextEditing` (netstandard2.0, net10.0)

The UI-free core. No Avalonia, no liblcm.

- The run model: `Run`, `Paragraph`, `Document`, `PropertyBag` ([The run model](run-model.md)).
- Editing: `DocumentEditor`.
- Paragraph-key rules: `Paragraph.Key`, opaque, null until the host assigns one.
- Clipboard forms (`IClipboardForm`, `ClipboardForms`) and the paste writing-system rule ([Clipboard](clipboard.md)).
- The debug interchange dump, and grapheme helpers.
- The language-service interface, `ITextLanguageService` ([Language service](spelling-and-language-service.md#language-service-adr-0030)).

### `src/SIL.TextEditing.Avalonia` (netstandard2.0, net10.0)

`RichTextField`: layout with Avalonia's `TextFormatter`, caret, selection, hit testing, bidi caret stops, IME client, copy, cut, paste and
"Copy as" through the host's clipboard forms. See [Editing](editing.md), [Rendering](rendering.md) and
[Spell checking and the language service](spelling-and-language-service.md).

### `src/SIL.TextEditing.Markdown` (netstandard2.0, net10.0)

FieldWorks Markdown and record files. See [The Markdown library](markdown.md).

### `src/SIL.TextEditing.LCModel` (netstandard2.0, net10.0)

The `ITsString` adapter and the TsString XML clipboard form. See [The LCModel adapter](lcmodel.md).

## Samples

Samples do not ship.

- **`samples/SIL.TextEditing.Playground`** (netstandard2.0, net10.0): the shared playground UI. By default a scenario picker, the
  `RichTextField`, a selection bar over the selection (bold, italic, underline, writing system, character style; under More the paragraph
  style and a picture) and the field as FieldWorks Markdown, editable and applied as one undo step. Developer mode adds the writing-system bar,
  format and undo buttons, block-feature switches (tables, lists, headings; ADR 0033), find and replace, a model tab and an event log. Both
  hosts and the integration tests use it; the tests run in developer mode unless they test the default.
- **`samples/Playground.Desktop`** (net48, net10.0): plain Avalonia desktop app showing the playground. Avalonia 11.3.17 on net48, Avalonia
  12 on net10.0 (Motif's shape).
- **`samples/Host48`** (net48): WinForms form hosting the playground through `WinFormsAvaloniaControlHost` between two WinForms TextBoxes,
  Avalonia 11.3.17: FieldWorks' shape.

The playground loads ready-made documents (Latin and Arabic, Indic and Thai shaping, NFD and surrogates, inline pictures, unknown
properties, an environment box with a language service, 200 paragraphs, many fields) and shows each as FieldWorks Markdown and as the runs the model holds.

## Test projects

| Project | Targets | What it tests |
|---|---|---|
| `tests/SIL.TextEditing.Tests` | net48, net10.0 | Unit tests for the model. |
| `tests/SIL.TextEditing.Avalonia.Tests` | net48, net10.0 | Avalonia.Headless tests for the surface (Skia, real shaping). |
| `tests/SIL.TextEditing.SecondDispatcher.Tests` | net10.0 | The tests that make a dispatcher on a second thread (Avalonia 12 allows it). |
| `tests/SIL.TextEditing.RealClipboard.Tests` | net48, net10.0 | Bytes and Arabic text through the default clipboard backend and the real Windows clipboard. Opt-in. |
| `tests/SIL.TextEditing.LCModel.Tests` | net48, net10.0 | The adapter ([details](lcmodel.md#tests)). |
| `tests/SIL.TextEditing.Markdown.Tests` | net48, net10.0 | Conformance, Pandoc agreement, random round trips ([details](markdown.md#tests)). |
| `tests/SIL.TextEditing.Playground.Tests` | net48, net10.0 | Avalonia.Headless integration tests that drive the playground the way a person would. |
| `tests/AotCheck` | net10.0 | Publishes the three net10.0 libraries native AOT and runs the Markdown, record and writing-system checks ([details](markdown.md#dependencies-trimming-and-aot)). |

### SecondDispatcher

`Dispatcher.UIThread` is process-wide, so the tests that make a dispatcher on a second thread run in a test host of their own and cannot
change it for the other tests (issue #72).

### RealClipboard

It runs in a real Avalonia window (Avalonia.Desktop), not Avalonia.Headless.

- **Opt-in.** Skipped unless `TEXTEDITING_REAL_CLIPBOARD=1` (and on any system but Windows), because it overwrites the clipboard of whoever
  runs it and needs an interactive desktop.
- On GitHub's windows-latest runner the net48 test host crashed (cause not found; Avalonia 11.3 Win32 in a non-interactive session is the
  suspect) while net10.0 passed.
- When it runs, the two target frameworks take turns through a `Global\` mutex, and a turn or a test over 60 s fails.
- If the Win32 platform cannot start, the tests say so and end.

## Build environment

Windows with the .NET 10 SDK and the .NET Framework 4.8 targeting pack. net48 builds and tests run only on Windows.

- From WSL, run the same commands through `powershell.exe`.
- The samples are built by the solution.
- The headless tests use the Windows fonts Segoe UI, Nirmala UI, Leelawadee UI and Constantia.
- A person tests with the checklist in [docs/manual-testing.md](../manual-testing.md).
