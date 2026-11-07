# The editor is FieldWorks' editor; Motif uses it as part of FieldWorks

SIL.TextEditing is built for FieldWorks only, not as a general SIL text editor. Motif uses it because Motif will
live inside FieldWorks once FieldWorks moves to Avalonia 12 and .NET 10. Any program that needs several writing
systems in one text is already working with LCModel data, so there is no second audience to design for.

The field inherits everything FieldWorks text editing does today (ADR 0005), and behaves the way FieldWorks does.
The only improvements allowed are things WinForms made hard to do. A different design choice, such as following
browsers or Word where FieldWorks differs, is not an improvement under this rule. It is a separate decision, made
later, outside parity.

**Consequences:** FieldWorks words and shapes are fine in the API: runs, numbered text properties, writing systems,
styles and paragraph keys. Whether the library may reference LCModel types directly is a separate, open question,
because FieldWorks' `EngineIsolationAuditTests` keeps LCModel out of FwAvalonia.

Owner decision, 2026-11-03. This supersedes the earlier "general library with no FieldWorks concepts" proposal.

Amended by ADR 0031 (2026-11-05): the library is the shared layer of FieldWorks and Motif, not only FieldWorks' field.
