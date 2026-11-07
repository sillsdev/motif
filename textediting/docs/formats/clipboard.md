# Clipboard

A field copies a selection in several **clipboard forms** at once and pastes the best one the clipboard holds (ADR 0029). The host
registers the forms. The field itself depends on neither liblcm nor the Markdown library.

## The forms

| Form | Id | Clipboard format name | Payload | Provided by | Default paste order | "Copy as" | Status |
|---|---|---|---|---|---|---|---|
| FieldWorks TsString | `TsString` (`ClipboardForms.TsStringId`) | `TsString` | a host object: FieldWorks' .NET-serialised `TsStringWrapper` | the FieldWorks host | 1st | no | The id ships. The host writes the form and a clipboard backend that can store an object. |
| FieldWorks Markdown | `FieldWorks Markdown` (`ClipboardForms.FieldWorksMarkdownId`) | chosen by the form | text: [FieldWorks Markdown](fieldworks-markdown.md) with its front matter | the host, around `MarkdownConverter` | 2nd | yes (planned, #168) | Planned, #168. Only the id ships; no form class ships yet. A host can write its own. |
| Plain text | `text/plain` (`ClipboardForms.PlainTextId`) | the system's text format | text: the paragraphs' text joined with `\n`, in NFC | built into the field | 3rd | always | Shipped. |
| TsString XML | `SIL.TextEditing.TsStringXml` (`TsStringClipboard.XmlFormatName`) | `SIL.TextEditing.TsStringXml` | text: the XML of one `ITsString` | `TsStringClipboardForm` in `SIL.TextEditing.LCModel` | not in it; the host adds the id | no | Shipped. |
| Runs dump | `SIL.TextEditing.Runs.v1` | `SIL.TextEditing.Runs.v1` (`Interchange.ClipboardFormat`) | text: `Interchange` XML | `InterchangeClipboardForm` in `SIL.TextEditing` | not in it | yes | Shipped, for tests and debugging only. |

### FieldWorks TsString

FieldWorks puts a .NET-serialised `TsStringWrapper` on the clipboard under `TsString`. That class lives in FieldWorks' own SimpleRootSite
assembly. This library neither references nor copies it, and nothing here writes under `TsString`. A FieldWorks host registers its own
form with the id and format name `TsString`:

- its `Write` builds the wrapper from `TsStringClipboard.FragmentToXml`;
- its `Read` unwraps it and calls `TsStringClipboard.FromXmlToFragment`;
- its host clipboard backend stores and reads the object, since Avalonia's clipboard cannot.

A TsString copy of several paragraphs joins them with `"; "`. Under the default paste order TsString is read first, so such a copy,
pasted into another field, arrives as one paragraph, as in FieldWorks. To keep the paragraph breaks, a host registers a FieldWorks Markdown
form (planned, #168) and puts its id before `TsString` in `PasteOrder`.

### FieldWorks Markdown (planned, #168)

The payload is a whole FieldWorks Markdown text **with its front matter**, so unmarked text never takes its writing system from the
target project's defaults. Read it with `MarkdownConverter.Parse` and write it with `MarkdownConverter.ToMarkdown`. FieldWorks Markdown
is a back end first (agents, files, drafting). On the clipboard it is planned as an extra form, not the default (planned, #168).

### Plain text

Every copy puts the plain text on the clipboard: the paragraphs' text joined with `\n`, in NFC (ADR 0018). The field stores NFD; text
meant for other programs is NFC. The rich forms keep the stored form.

### TsString XML

`TsStringClipboardForm` carries the XML of one `ITsString`, written by liblcm's own `TsStringSerializer`. It is **not** FieldWorks' wire
format, so it uses its own id and format name, `SIL.TextEditing.TsStringXml`, and can sit beside a host's `TsString` form.

- Copy joins the paragraphs with `WriteParagraphSeparator` (default `"; "`, as FieldWorks does).
- Paste gives one paragraph, as FieldWorks does, unless `PasteParagraphSeparator` is set; then it splits there.
- A writing system the host's handles do not know keeps its id during the read, so the paste rule below can map it.
- A property liblcm cannot load back is left out and reported to `OnDropped` (see [Findings](findings.md#lcmodel-adapter)).
- The id is not in the default paste order. A host that wants it adds it.

### Runs dump (tests and debugging)

`Interchange.ToXml` writes every run, writing system and property, with nothing lost. ADR 0029 retired it from the user's clipboard;
`InterchangeClipboardForm` exists so that tests and the playground can carry formatting without liblcm or the Markdown library. Its
shape, from `src/SIL.TextEditing/Interchange.cs`:

| Element | Attributes |
|---|---|
| `Runs` | the root |
| `Para` | `style`, `rtl` (`1` or `0`), `defws`, then one attribute per property: `i_<number>="<variation>:<value>"` for an integer property, `s_<number>="<text>"` for a string property |
| `Run` | `ws`, `objdata` when the run has object data, and the property attributes; the text is the content |

A backslash, a control character, a lone surrogate and U+FFFC are escaped as `\\` and `\uXXXX` in text and string values, so any XML
reader gets the exact UTF-16 back. `Interchange.Json` is a JSON dump of a document for exact comparison in tests; it is not a clipboard
form.

## Copy

- `RichTextField.CopyAsync` asks every registered form for its payload and puts them all on the clipboard with the plain text.
- A form that returns null, returns an empty string or throws is left out. The others still go.
- The backend reports the format names it could not store. The field raises `ClipboardFormsNotStored` with them.

## Paste

`RichTextField.PasteAsync` tries the ids of `ClipboardForms.PasteOrder` in turn and uses the first form that the clipboard holds and that
reads.

- The default order is FieldWorks' own: `TsString`, `FieldWorks Markdown`, `text/plain`.
- The host may replace or edit the list. An id with no registered form is skipped. A form whose id is not in the list is never read.
- A payload a form cannot read (null, or an exception) is skipped, so paste falls through to the next form.
- The editor normalises the pasted text to the form the host stores (`Document.Normalization`; NFD for FieldWorks).
- A block the field does not allow is flattened, and the field raises `BlocksFlattened` (ADR 0033).

## The writing-system rule

Text in a writing system the target project lacks takes the writing system of the selection it replaces, as FieldWorks does
(`ClipboardWritingSystems.MapMissing`).

- The field asks `RichTextField.KnowsWritingSystem`. By default that is its `WritingSystems` registry, and an empty registry accepts every
  writing system.
- After a paste that mapped something, the field raises `WritingSystemsMapped` with the missing ids and the target, so the host can show a
  notice.
- A run with no writing-system id takes the target's too, but is not listed as missing.
- The rule runs for every rich form. Plain text has no writing system; it is pasted as typed text is.

## Copy as

`RichTextField.CopyAsAsync(formId)` puts one form on the clipboard as text: the plain text (`text/plain`), or any registered form
whose payload is a string, such as FieldWorks Markdown (planned, #168). It returns false for an unknown id, for a form whose payload is
not a string, and for a form with nothing to say. `OffersCopyAs` does not limit `CopyAsAsync`; it only decides which forms
`ClipboardForms.CopyAsForms` lists for a menu.

## Backends

The field reads and writes the clipboard only through `IClipboardBackend` (`RichTextField.ClipboardBackend`):

| Member | Does |
|---|---|
| `SetAsync(plainText, forms)` | Replaces the clipboard with the plain text and the named payloads. Returns the names it could not store. |
| `GetAsync(formatName, kind)` | The payload under a name, read as the form's `ClipboardPayloadKind`, or null. Must not throw on data it does not understand. |
| `GetTextAsync()` | The plain text, or null. |

- Payloads pass through untouched. A form says what it holds (`PayloadKind`): `Text` (a string), `Bytes` (a `byte[]`) or `Object` (a
  host object). The backend reads a format back as that kind and never guesses text from bytes.
- The default backend is Avalonia's clipboard of the field's window (`AvaloniaClipboardBackend`). It stores strings and `byte[]`s. It
  cannot store a host object and reports it as not stored.
- FieldWorks' WinForms host plugs in its own backend, so it can keep the clipboard itself and store the `TsStringWrapper` object.

## Planned: Word HTML and RTF

> **Planned. Nothing in this section is built.** It says what the owner has decided, so that integrators know what is coming.

The owner decided on 2026-11-05 (recorded as planned additions in ADR 0031):

- Copy writes Word-compatible HTML and RTF. This reverses ADR 0029's "no HTML or RTF". TsString stays first on paste between FieldWorks
  fields.
- Paste reads HTML only. Whether to read RTF is decided after testing with Word.
- Paste from Word keeps meaning (emphasis, links, styles, language, writing system) and drops fonts, sizes and colours. A host setting
  allows full formatting.
- Writing systems travel as character styles named `<Style>[lang=<ws>]`, as FieldWorks' Word export does. A host switch turns this off.
- Host callbacks for footnotes and pictures are defined in the first version's API and built right after it.
- The paste order becomes TsString, FieldWorks Markdown, HTML, RTF, plain text.
- There is no "Copy as HTML source".
- HTML is read with the AngleSharp package, if it passes the AOT check.
- One new project, `SIL.TextEditing.WordInterchange`, holds the importer and the exporter.

Not decided yet: the forms' ids and format names. The Windows clipboard names are `HTML Format` and `Rich Text Format`. Both forms
would carry bytes, so neither could be used with "Copy as". `IClipboardForm` and `IClipboardBackend` need no change.

## Planned: tables on the clipboard

> **Planned.** See [Tables](tables.md) for the storage and editing design.

- On the Markdown side a table stays a standard pipe table.
- Tab-separated text pasted at a paragraph boundary becomes a table, and "Paste as table" is offered too.
- Today a paste into a field that does not allow tables already flattens a table to one plain paragraph per row, cells joined by
  ` | ` (ADR 0033).
