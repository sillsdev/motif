# Screenshots

Pictures of the playground (`samples/SIL.TextEditing.Playground`) for the README and the docs. Each is regenerated, not hand-made.

## The headless ones

Rendered by `tests/SIL.TextEditing.Playground.Tests/ScreenshotGenerator.cs`: a 1280 x 800 headless Avalonia 12 window (net10.0, Skia, Fluent light theme, the playground's own fonts: Segoe UI, Nirmala UI, Leelawadee UI), cropped to the field and the tabs beside it (Markdown, or Model for the run dump). The test is skipped unless `TEXTEDITING_SCREENSHOTS=1`, so the normal test run never writes here.

```powershell
$env:TEXTEDITING_SCREENSHOTS=1; dotnet test tests\SIL.TextEditing.Playground.Tests -c Release -f net10.0 --filter ScreenshotGenerator
```

- `bidi-selection.png`: Latin and Arabic in one paragraph, one selection running across the direction changes ("Latin and Arabic" scenario), the selection bar over it, and the same text as FieldWorks Markdown beside it.
- `selection-bar.png`: the selection bar over French text with More open: bold, italic, underline, writing system, character style, then paragraph style and Picture.
- `indic-thai-shaping.png`: Hindi, Tamil and Thai shaped correctly, with a Devanagari conjunct selected as one cluster.
- `nfd-surrogates.png`: composed and decomposed accents, stacked marks, and emoji (surrogate pairs and a ZWJ family), with the emoji selected.
- `inline-pictures.png`: inline picture objects (U+FFFC) in left-to-right and right-to-left text, selected along with the text around them.
- `unknown-properties-inspector.png`: runs that carry properties the editor does not understand (9001, 9002), a paragraph style and a character style, and, in developer mode, the Model tab showing the runs and their property numbers.
- `environment-box.png`: the phonological-environment box, one line with coloured chips and a squiggle on a class the language service rejects.
- `environment-fix-menu.png`: right-click on a problem in the environment box: the service's message, its fix ("Insert the closing parenthesis") and the Insert items.
- `spelling-squiggles.png`: red squiggles on misspelled words, blue on a word that glues two writing systems, nothing on do-not-check or read-only text.
- `spelling-context-menu.png`: the context menu on a misspelled word: the word, its suggestions, Find, Cut, Copy, Paste and Copy as.
- `copy-as-submenu.png`: the "Copy as" submenu open (plain text, a debug dump of the runs, and a disabled Markdown entry when no converter is registered).
- `block-features.png`: a heading, a bulleted list and a table (as table-styled paragraphs, one column for now) with the Tables, Lists and Headings switches visible and on.
- `long-document.png`: the 200-paragraph scenario scrolled to the caret in the middle, mixed writing systems; the Markdown tab shows the document as FieldWorks Markdown from its start.
- `many-fields.png`: fifty small fields in one window, in English, French, Swahili and Arabic.

## The real windows

`capture-hosts.ps1` builds and starts each host through `run.ps1`, moves its window to a fixed place and size on the screen, copies those pixels with `System.Drawing`, saves the PNG and closes the host. It needs an unlocked interactive desktop; the window is made topmost so nothing else is in the pixels. Run it from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs\images\capture-hosts.ps1
```

- `host-winforms-fieldworks-shape.png`: `samples/Host48`, FieldWorks' shape: a WinForms form on net48 (Avalonia 11.3.17) with the playground between two WinForms TextBoxes.
- `desktop-window.png`: `samples/Playground.Desktop`, the plain Avalonia window on net10.0 (Avalonia 12).
