# Clipboard in the field (ADR 0029)

The clipboard forms, the paste order, the writing-system rule, "Copy as" and the backend interface are documented in
[docs/formats/clipboard.md](../formats/clipboard.md). This page is the field's side, in brief.

- The host registers the clipboard forms the field copies and pastes. The field takes no dependency on liblcm or the Markdown converter.
- `RichTextField.ClipboardForms` holds `IClipboardForm`s: an id, a display name, a clipboard format name, `Write` and `Read` over an
  `object` payload (a string, a `byte[]` or a host object), and a `PayloadKind` saying which, so the backend reads the format back as that
  and never guesses text from bytes. The clipboard forms types (`IClipboardForm`, `ClipboardForms`) and the paste writing-system rule live in
  the UI-free `SIL.TextEditing`.
- The clipboard itself is behind `RichTextField.ClipboardBackend` (`IClipboardBackend`), which is Avalonia's clipboard unless the host sets
  its own. Payloads pass through it untouched. The default backend stores strings and `byte[]`s, each read back as the kind the form
  declares. A host object needs a host backend, and `RichTextField.ClipboardFormsNotStored` tells the host which forms the default backend
  could not store. See [Backends](../formats/clipboard.md#backends).
- Plain text is built in. `CopyAsAsync` returns false for a form whose payload is not text.
- **Copy** puts every registered form on the clipboard and the plain text in NFC. A form that throws or has nothing to say is left out.
- **Paste** tries `ClipboardForms.PasteOrder` in turn, by default TsString, FieldWorks Markdown, plain text (FieldWorks' own preference),
  and uses the first form the clipboard holds and that reads. The list is the host's to change; a form that is not in it is never read.
- **Writing systems the target lacks** take the writing system of the selection the text replaces, as FieldWorks does. The field asks
  `RichTextField.KnowsWritingSystem` (by default: its `WritingSystems` registry, and a registry with no entries accepts everything), and
  raises `WritingSystemsMapped` after the paste so the host can show a notice.
- **Copy as** (`RichTextField.CopyAsAsync(formId)`) puts one form on the clipboard as text: the plain text, or any registered form whose
  payload is a string, such as FieldWorks Markdown (planned, #168). `IClipboardForm.OffersCopyAs` only decides which forms
  `ClipboardForms.CopyAsForms` lists for a menu. The playground's context menu has a "Copy as" submenu.
- `SIL.TextEditing.Runs.v1` is no longer on the clipboard. `Interchange` stays as a test and debug dump, and `InterchangeClipboardForm`
  lets tests and the playground carry formatting without liblcm or the Markdown library.

## FieldWorks interop

FieldWorks interop is the host's job and uses two hooks, the backend and a host-object payload. They are described in
[The LCModel adapter](lcmodel.md#it-is-not-fieldworks-tsstring-wire-format) and
[Clipboard: FieldWorks TsString](../formats/clipboard.md#fieldworks-tsstring).

A FieldWorks host also registers the FieldWorks Markdown form. The Markdown library is to provide it (issue #168); only the registration
point and the ids `ClipboardForms.FieldWorksMarkdownId` and `ClipboardForms.TsStringId` exist today.

## The real clipboard

`tests/SIL.TextEditing.RealClipboard.Tests` puts bytes and Arabic text through the default backend and the real Windows clipboard. See
[Projects and tests](projects.md#test-projects).
