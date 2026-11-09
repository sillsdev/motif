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

### `src/SIL.TextEditing.Project` (netstandard2.0, net10.0)

Read-only adapters over an already-loaded `LcmCache` (ADR 0031):

- `ProjectStyles` implements `IStyleResolver`, `IStyleCatalog` and `IKnownStyles`. It reads the project's
  `IStStyle` paragraph/character types, resolves based-on inheritance through liblcm's stylesheet, and applies the
  requested writing system's inherited font overrides through liblcm's resolved font-info API. Common properties
  use the LCModel adapter's `TsStringConversion.ToBag`. Default-font markers resolve to the writing system's font.
  Missing styles return null; an unknown writing-system id uses common style properties. Malformed inheritance
  cycles throw `InvalidOperationException` before liblcm traverses them.
- `ProjectWritingSystems` implements `IWritingSystemMapper`, `IWritingSystemDetails`, `IKnownWritingSystems`
  and `IWritingSystemHandles`. Defaults come from the cache; choices are the distinct analysis and vernacular
  writing systems, including configured systems outside the current lists. Details use the actual language tag,
  direction and default font. A system known only to the factory is not a project choice. Handle conversion
  delegates to `WritingSystemHandles.FromFactory`, so the same instance also serves `TsStringConversion`.

```csharp
var styles = new ProjectStyles(cache);
var writingSystems = new ProjectWritingSystems(cache);
var options = new MarkdownOptions { Styles = styles, WritingSystems = writingSystems };
// Use styles as the field's IStyleResolver and writingSystems for ITsString conversion too.
```

The host owns loading, ICU initialization, cache lifetime, thread affinity, units of work and persistence. Serialize
reads on the cache's owning thread while it is alive. The adapters do not change or dispose the cache; lists, bags
and descriptions are detached results. Reads see project edits on the next call; the host invalidates field
layouts when styles or writing-system definitions change. Names and ids use the project's exact spelling.

The project references the LCModel and Markdown libraries plus full `SIL.LCModel` at
`$(SilLCModelPackageVersion)`. Its direct `SIL.Core.Desktop` reference uses `$(SilCoreDesktopVersion)` to select
L10NSharp's netstandard2.0 asset instead of the Framework-only transitive version (owner decision, ADR 0031).
These pins come from `SilVersions.props`. There is no Avalonia reference, and the project is not trimmable or
AOT-compatible. It is intentionally excluded from `tests/AotCheck`.

Project paths, metadata-derived record schemas, record export/import and project language services remain planned.

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
| `tests/SIL.TextEditing.Project.Tests` | net48, net10.0 | Styles, writing systems and Markdown/TsString integration over real memory-only caches, built with public liblcm APIs. |
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

The Project tests stage normalization data and Windows x64 ICU DLLs from their existing transitive packages.
On Linux/macOS, supply a SIL ICU 70 installation with `TEXTEDITING_SIL_ICU_STAGE=/path/to/icu` when running them.
The fixture uses an offline, process-private SLDR cache; its private-cache overload is internal to libpalaso and
accessed by reflection in test setup only. The cache itself is created and edited solely through public liblcm APIs.
No FieldWorks project data is stored in the repository.

Windows with the .NET 10 SDK and the .NET Framework 4.8 targeting pack. net48 builds and tests run only on Windows.

- From WSL, run the same commands through `powershell.exe`.
- The samples are built by the solution.
- The headless tests use the Windows fonts Segoe UI, Nirmala UI, Leelawadee UI and Constantia.
- A person tests with the checklist in [docs/manual-testing.md](../manual-testing.md).
