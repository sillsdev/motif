# Copy puts TsString, FieldWorks Markdown and plain text on the clipboard; paste prefers TsString

**Copy** puts these on the clipboard:
- FieldWorks' TsString format, as FieldWorks does today;
- FieldWorks Markdown, with its front matter (ADR 0028);
- plain text, in NFC (ADR 0018).

**Paste** prefers TsString, then FieldWorks Markdown, then plain text, so the field behaves as FieldWorks does. The
order is a setting the host can change. The host registers the clipboard forms, so the field itself takes no
dependency on liblcm or on the Markdown converter.

A writing system that the target project lacks is mapped to the writing system of the selection, as FieldWorks does,
and the user sees a notice that it happened.

A **Copy as ▸** menu forces one form: FieldWorks Markdown or plain text.

**Considered options:** HTML and RTF for pasting into word processors (FieldWorks never offered them, and nothing in
the planned scope needs them; either can be added later without changing the format); keeping the field's own
`SIL.TextEditing.Runs.v1` XML (FieldWorks Markdown now carries everything it did, as a documented format, so Runs.v1
stays only as a test and debug dump).

**Consequences:** FieldWorks' "TsString" clipboard format is a .NET-serialised `TsStringWrapper` that lives in FieldWorks' own SimpleRootSite, not in
liblcm, so the field and the LCModel adapter neither reference nor copy it. The adapter's `TsStringClipboardForm` is TsString XML under its own id and format name, so it can sit beside a host's "TsString" form. FieldWorks interop is the host's
job and uses two hooks the field provides: a host-replaceable clipboard (`IClipboardBackend`; Avalonia's by default), so FieldWorks' WinForms host can keep the
clipboard itself and put the serialised wrapper under "TsString" with its own calls; and an `object` payload on `IClipboardForm` (a string, a `byte[]` or a host object, passed to
the backend untouched), so FieldWorks registers a form under "TsString" whose `Write` returns the wrapper (built from the adapter's XML) and whose `Read` unwraps it. Nothing writes raw
XML under "TsString", and a payload a form cannot read is skipped so paste falls through. A TsString copy of several paragraphs joins them with "; " and pastes as one paragraph, as FieldWorks does; with TsString first in the
paste order, a copy of several paragraphs from one field to another therefore loses its paragraph breaks unless the FieldWorks Markdown form is tried first. Markdown on the clipboard always carries its front matter, so unmarked text never takes its writing
system from the target project's defaults. This refines ADR 0016, which put only the TsString format in the adapter
package. FieldWorks Markdown is a back end first (agents, files, drafting); on the clipboard it is an extra form, not
the default.

Owner decision, 2026-11-04.

Amended by ADR 0031 (2026-11-05): "No HTML or RTF" is reversed; Word-compatible HTML and RTF copy and paste are planned (design pending).
