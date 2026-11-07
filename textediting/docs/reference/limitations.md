# Known limitations

What the library does not do today. Planned work is in the README's [Roadmap](../../README.md#roadmap).

## Accessibility

- The field gives screen readers what FieldWorks gives them today (ADR 0017): through Avalonia's automation peers it reports a name set by
  the host, an Edit role, its text as the value, its read-only state and its bounds.
- There is no UI Automation `TextPattern` (caret, selection, ranges and attributes). It is deferred indefinitely (issue #8).
- Inside a net48 `WinFormsAvaloniaControlHost`, Avalonia's UIA subtree was empty in the spike (unexplained). FieldWorks' WinForms host needs
  its own small fix so that the Avalonia accessibility tree is visible at all (ADR 0017).

## Input

- Real IME and Keyman interaction are untested. The headless tests drive the IME client directly.
- Missing: scrolling inside long fields (the host scrolls; see [host hooks](editing.md#host-hooks-adr-0021-adr-0025)), drag and drop
  (issue #5), touch.
- The word rule is deliberately simple and not UAX #29 word breaking; Thai and Chinese are not split. See
  [Selection](editing.md#selection).

## Undo

Undo is a host stand-in (`SimpleUndoStack`); no LCModel glue exists. See [The run model](run-model.md#the-host-owns-undo).

## Avalonia faults

The layout works around several Avalonia faults, on one or both versions. They and their costs are listed in
[Avalonia versions](avalonia-versions.md#avalonia-faults-pinned-by-tests).
