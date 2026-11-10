# Source mode: one field shown and edited as its FieldWorks Markdown

A host can switch one rich field into **source mode**, where the field shows and edits its FieldWorks Markdown (ADR 0028),
and back to **rich mode**. This settles questions E-1 to E-4 of the editor-integration plan (issue #57 in Motif). The
rest of that plan (VS Code, the language server) waits until after FieldWorks Markdown 1.0.

**Owner decisions** (2026-11-10, Motif #73):

1. **The source view is a second `RichTextField`** holding the Markdown as plain monospaced text with a Markdown
   language service: the checker's findings are underlined in place, with the message on hover. A plain text box was
   rejected because it would lose the field's input handling (IME, Keyman, grapheme movement) and its undo.
2. **The switch covers one field.** The field with focus flips; the record's other fields stay in rich mode. A
   toggle on the field's selection bar and Ctrl+Shift+M switch it; a strip says "Editing as Markdown" with "Back to
   rich text", and the change is announced to screen readers. Opening a whole record as one `.fw.md` document comes
   later, as its own feature.
3. **Links are shown in full.** The `{…guid=…}` attribute stays in the text, drawn muted; nothing is hidden, so an
   edit cannot break a link in a way the person cannot see.
4. **Undo.** Inside source mode, undo works on the Markdown text. Returning to rich mode is **one undo step** in the
   rich field ("Edit as Markdown"), so one undo restores the field as it was before. Source-mode undo history does not
   carry into rich mode. This narrows ADR 0003 only in that the source view's own text history lives until the switch
   back; what reaches the host is that one step.
5. **Leaving with errors.** A checker error (Markdown that cannot become valid FieldWorks text) keeps the field in
   source mode with the findings shown; "Throw away my Markdown changes" is always offered. Warnings never block. The
   host receives nothing until the text is clean.
6. **No new write route.** On leaving, the Markdown is imported through the same reader and checker as paste and the
   record reader, and the host stages it as it stages any rich edit (in Motif, `text/setParagraphs` or
   `text/setRichString`, Motif ADR 0095).
7. **The conflict viewer stays rich-only.** If the host reports that the field changed underneath while it is in
   source mode, the host returns it to rich mode first (after the person fixes or throws away errors).

Owner decision, 2026-11-10.

## Defaults (first version, #133)

Not owner decisions; names and choices the decision above does not fix. Each can change without a new ADR.

**API.** The smallest that serves a FieldWorks-style host and Motif:
- `RichTextField.SourceForm` (`ISourceForm?`, null by default: no source mode, Ctrl+Shift+M passes to the host). A host offers source
  mode by setting it, with `new FieldWorksMarkdownSourceForm(options)` given the same `MarkdownOptions` as its Markdown clipboard form.
  Changing it in source mode throws the source away.
- `EnterSourceMode()`, `LeaveSourceMode()` and `DiscardSourceChanges()`, for a host's own toggle (the playground's selection bar has
  "Markdown"); `IsSourceMode`; `SourceView` (the second field, to read its `LanguageDiagnostics`); `SourceModeChanged`
  (`SourceModeChangedEventArgs`: `IsSourceMode`, `Refused`, `Announcement`, the sentence a host gives its screen reader, since Avalonia
  has no live region). `EnterSourceMode` is false for a read-only field, text that may not be edited, or text the form cannot write.
  `LeaveSourceMode` is false, and the field stays in source mode, when the reading has an error, or when the host made the field
  read-only meanwhile (the Markdown is kept, not lost).
- In the UI-free core: `ISourceForm` (`Name`, `Write`, `Read`, `AreEquivalent`), `SourceReading` (paragraphs, diagnostics, tokens,
  `HasErrors`), `SourceTokenKinds.Muted`, and `SourceFormLanguageService`, the form's reader as an ADR 0030 language service; and
  `DocumentEditor.ReplaceParagraphs(paragraphs, same, label)`, the one undo step. The Markdown form lives in
  `SIL.TextEditing.Markdown`, so the field keeps no reference to Markdig. `TokenStyle.Mute` draws the muted GUIDs.

**Behaviour.**
- The strip ("Editing as Markdown", "Back to rich text", "Throw away my Markdown changes", and the error count when a return is
  refused) and the source view live inside the field, as its child, so a host needs no wrapper and its layout does not change. The
  strip's buttons carry their own template, so a host with no theme (WinForms) shows them.
- The source is the Markdown body without front matter (the host's options name the writing systems), one line per source-view
  paragraph, in a monospaced font at the size of the field's first writing system. A field of one paragraph (`SingleParagraph`) uses
  the inline grammar of a record file's one-paragraph field: no paragraph syntax, and its style and paragraph properties stay as they
  were. The keyboard is not switched in source mode (the source is not in any of the text's writing systems).
- The source view is isolated: its undo history is its own and ends with the switch back, it has no clipboard forms other than plain
  text, and no spelling. Findings are the Markdown checker's, marked as ADR 0030 diagnostics (`fwmd.*` ids, source `fwmd`), with a
  finding's replacement as its fix.
- Leaving reads the text synchronously. Source that says what the field already holds commits nothing, so a round trip adds no undo
  step. Otherwise the paragraphs are prepared as pasted ones are (block features flattened, ADR 0033; tabs; the host's normalisation
  form), the paragraphs at the start and the end the form calls equivalent are left as they were (objects, keys and all), and in the
  stretch between each paragraph takes the key of the old one at its place (ADR 0006); an added paragraph has no key until the host
  gives it one. All of it is one delta labelled "Edit as Markdown".
- A host that changes the field's text itself (a conflict, a sync) returns the field to rich mode first, with `LeaveSourceMode` or
  `DiscardSourceChanges` (decision 7); the field does not watch for it.
