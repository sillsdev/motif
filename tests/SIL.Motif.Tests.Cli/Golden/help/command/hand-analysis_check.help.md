# Check a Hand Analysis

Use `hand-analysis check --project <project.fwdata> --input <file.json> --json` to validate an asserted reading of one Occurrence against a saved FieldWorks project. The file uses `motif.hand-analysis/1`: exactly one Scripture or Text anchor, explicit `approve: true`, the Wordform's writing system and case form, and ordered pieces with request-local `pieceKey` values. A piece can retain typed form, independently nullable lexical links, and optional variant provenance. Unknown and irrelevant fields are refused with JSON-pointer errors.

The check reads a private copy. It resolves portable ids with class and ownership checks, looks up the Wordform by exact NFD writing system and case form, and reports allomorph matches within an entry. Marker defaults follow the project's morph-type list and FieldWorks' disambiguation rules. Unknown markers retain their spelling and warn while using the project's stem GUID. An acknowledged allomorph-family mismatch reports the compatible grammatical info it would require; the check creates none.

`LexiconPolicy` defaults to `AnalysisOnly`; `SyncSinglePieceGlossAndCategory` describes a future single-piece annotation transaction. Add New Sense requests a distinct sense; variants can request an entry, form and reference without inventing a sense or grammatical info. Request-local creation references are validated and reported without allocating identities.

This command records no approval, lexical creation, Proposal or Receipt. It is not a [Dry Run](term:dry-run) or a guarantee about a later write. A saved project cannot verify Home revisions, occurrence heads or external Scripture context, so those checks are reported unavailable. The hand-analysis writers and MCP surface are not exposed.
