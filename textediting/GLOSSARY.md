# SIL.TextEditing

The rich text field FieldWorks uses to edit multi-writing-system text, and that Motif uses inside FieldWorks.

## Text

**Run**:
A stretch of text in one writing system with one set of text properties.
_Avoid_: span, segment (a segment is an interlinear unit in FieldWorks)

**Paragraph**:
A sequence of runs with its own paragraph properties and a paragraph key.

**Paragraph key**:
The host's identity for a paragraph, kept through edits by FieldWorks' split and join rules.
_Avoid_: paragraph ID, GUID

**Text property**:
A numbered formatting or data value on a run or paragraph, using FieldWorks' property numbers.

**Carried property**:
A text property the field keeps unchanged through every edit but does not draw, because FieldWorks does not draw it either.
_Avoid_: opaque, unknown, lossy

**Embedded object**:
A picture, link or other object that sits inline in a run as a single placeholder character.
_Avoid_: ORC (in prose)

**Hard line break**:
A line break inside a paragraph (U+2028), which Shift+Enter inserts. It is not a paragraph separator.
_Avoid_: soft return, soft break

**Editable place**:
A stretch of insertion points in one paragraph where editing is allowed, with no read-only text inside it. Tab moves between editable places.

**Read-only text**:
Text the user cannot change: a run whose editable property is not-editable, or any text in a field whose read-only flag is on. It can still be selected and copied. Semi-editable text can be changed but not formatted.
_Avoid_: locked, disabled

## Writing systems

**Writing system**:
A language written in one particular way, with its own font, direction and keyboard, as libpalaso defines it.
_Avoid_: WS (in prose), language, script

**Analysis writing system**:
A writing system used to describe and translate the language being studied.

**Vernacular writing system**:
A writing system of the language being studied.
_Avoid_: grammar (for vernacular)

**Default analysis**, **default vernacular**:
The first analysis, or the first vernacular, writing system of a project.

## Roles

**Host**:
The application that owns the data, the undo history, the styles and the keyboards, and embeds fields: FieldWorks, Motif or the playground.

**Field**:
One `RichTextField`: one editable text on screen.
_Avoid_: editor (for a single control), text box

## Editing

**Undo step**:
One thing the host undoes with one Undo: a run of consecutive typing, or a single command such as a delete or a paste. Each delta carries the number of the step it belongs to (ADR 0023).
_Avoid_: undo group (in prose; `UndoGroup` is the member name), undo unit

**Block features**:
The set of block kinds (tables, lists, headings) a field, or a rich field of a record schema, may hold. The default is all of them. When a kind is off, the host's commands for it are refused and a paste or Markdown import turns a block of it into plain paragraphs (ADR 0033).
_Avoid_: block settings, block flags

**Flatten**:
To turn a block the field does not allow into plain paragraphs: a table into one paragraph per row with the cells joined by a space, a bar and a space, a list item or a heading into a plain paragraph. Paste and the record reader's fix do it with the same code.
_Avoid_: strip, unformat

**Block recogniser**:
The one place that says whether a paragraph is a table row, a list item or a heading, by the host's style names (ADR 0033).

**Leave request**:
The event a field raises, with a direction, when a key such as Tab or an arrow cannot move the caret any further inside it, so the host can move focus (ADR 0021).
_Avoid_: focus request, tab-out

## Spelling

**Spelling mark**:
A squiggle under a stretch of one paragraph that the field's spell checking found wrong: red for a misspelled word or a word with an embedded object, blue for a word it cannot judge because it mixes writing systems or has unchecked text inside.
_Avoid_: error, underline (that is a text property)

**Spell checker**:
The host's dictionary for one writing system, behind `ISpellChecker`. The field never loads a dictionary.

## Language service

**Language service**:
The host's explanation of what a box's text means to the notation it is written in (an environment, a morph breakdown), behind `ITextLanguageService`: tokens, diagnostics, completions, hover and whether an insert is allowed here. The field draws and applies the answers and never writes them into the text (ADR 0030).
_Avoid_: parser, validator (the service may be neither)

**Token chip**:
The colour the field draws behind a stretch of a notation's text, by the kind of token the language service gave it. It is decoration: the text and its runs are unchanged.
_Avoid_: highlight (that is find), object (an embedded object is a different thing)

**Diagnostic**:
A problem or note the language service found in a stretch of a box's text, drawn as a squiggle like a spelling mark and carried with an optional exact fix. It is a view: it is not in the document.
_Avoid_: error (a diagnostic can be a warning), spelling mark (that is the spell checker's)

## Markdown

**FieldWorks Markdown**:
The Markdown form of FieldWorks text: a strict profile of Pandoc Markdown that people and agents read and edit, and that the clipboard carries (ADR 0028).
_Avoid_: GDR Markdown (for the general format), FWMD (in prose)

**GDR Markdown**:
A Grammar Design Record written in FieldWorks Markdown, following the GDR template.
_Avoid_: GDR profile, GDR format

**Project path**:
The path of a FieldWorks object in the designed project tree, used in links.

**Record file**:
One FieldWorks record, or part of one, as one file: YAML front matter in a strict profile holding the record's objects and fields, FieldWorks Markdown in each rich value, and for some record kinds a Markdown body (ADR 0032).
_Avoid_: entry file, YAML export

**Record schema**:
The data that says which record kinds, object types and fields a record file may have and what kind of value each field holds, so the reader types every value by its path and not by YAML's rules (ADR 0032).
_Avoid_: model (that is liblcm's), JSON Schema (that is what the record schema publishes for editors)

**Body field**:
The one rich field of a record kind written as the Markdown body below the front matter, such as a notebook record's description. A lexical entry has none.
_Avoid_: main field, content

## Repository

**Sealed folder**:
SIL.TextEditing as one folder inside a host repository (first `textediting/` in Motif), with its own build, solution,
tests and documents. The host references into it; it references nothing outside itself except the host's shared-settings
file and `SilVersions.props` (ADR 0034).
_Avoid_: subtree, submodule, vendored copy

**Avalonia line**:
One of the two builds of every library: netstandard2.0 with Avalonia 11.3 (FieldWorks on .NET Framework 4.8), or
net10.0 with Avalonia 12 (Motif) (ADR 0009).
_Avoid_: flavour, variant

**Pull out**:
Copy the sealed folder into another repository (its own again, or FieldWorks) and make it build there (ADR 0034).
_Avoid_: migrate back, extract
