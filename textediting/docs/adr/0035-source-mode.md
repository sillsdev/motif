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
