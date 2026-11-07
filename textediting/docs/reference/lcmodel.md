# The LCModel adapter

`src/SIL.TextEditing.LCModel` (netstandard2.0, net10.0) is the LCModel adapter (ADR 0016). It references `SIL.LCModel.Core`.

**It is not trimmable or AOT-compatible, and it is deliberately not in `tests/AotCheck`.** It is for FieldWorks' and Motif's LCModel-side
host code, not Motif's GUI.

## Conversion

`TsStringConversion` maps `ITsString`, and an IStText-shaped list of paragraphs, to runs and a `Document` and back. It carries every int
property (with its variation) and every string text property by number, ObjData and NamedStyle included.

- The host supplies the writing-system handle mapping (`IWritingSystemHandles`) and the paragraph keys.
- A property liblcm cannot load back is left out of the `ITsString`, and reported to an optional callback (with none, it is dropped silently); the run model's bag keeps it either way. The callback,
  the `DroppedProperty` shape and the finding ids are in [Findings: LCModel adapter](../formats/findings.md#lcmodel-adapter). These
  refusals retire once FieldWorks pins a liblcm with the fixes (issue #159).

## Clipboard XML

`TsStringClipboard` reads and writes the XML of one `ITsString` through LCModel's own `TsStringSerializer`.

- A multi-paragraph copy joins paragraphs with "; " (a parameter), as FieldWorks' own copy does.
- The serializer needs no ICU setup (Hangul and composition-exclusion strings tested, net48 and net10.0).
- `TsStringClipboardForm` is that XML as a clipboard form a host registers on a field (ADR 0029). It has its own id and format name,
  `SIL.TextEditing.TsStringXml`, so it can sit beside a host's "TsString" form, and the host adds the id to the paste order if it wants it.
  Its details are in [Clipboard: TsString XML](../formats/clipboard.md#tsstring-xml).

## It is not FieldWorks' "TsString" wire format

FieldWorks stores a .NET-serialised `TsStringWrapper` (a class in its own SimpleRootSite assembly, which this package does not reference or
copy) under "TsString". Nothing here writes raw XML under "TsString", and an unreadable payload under any name is skipped so paste falls
through to the next form.

FieldWorks interop is the host's job and uses two hooks:

1. `RichTextField.ClipboardBackend` (`IClipboardBackend`: `SetAsync(plain, formats)`, `GetAsync(formatName)`, `GetTextAsync()`; Avalonia's
   clipboard by default) lets FieldWorks' WinForms host keep the clipboard itself, so it can put the serialised `TsStringWrapper` under
   "TsString" and read it with `GetData(...) as TsStringWrapper`, as it does today.
2. A form's payload is an `object` (a string, a `byte[]` or a host object) that passes through the backend untouched. So FieldWorks
   registers its own `IClipboardForm` with the id and format name "TsString" whose `Write` returns the wrapper object (built from
   `TsStringClipboard.FragmentToXml`) and whose `Read` unwraps it (`TsStringClipboard.FromXmlToFragment`).

See [Clipboard: FieldWorks TsString](../formats/clipboard.md#fieldworks-tsstring).

## Tests

`tests/SIL.TextEditing.LCModel.Tests` (net48, net10.0): round trips of the adapter against real `TsStrFactory` strings, edits that leave
undrawn properties alone, and the clipboard XML. To test against a local liblcm build, see `AGENTS.md`.
