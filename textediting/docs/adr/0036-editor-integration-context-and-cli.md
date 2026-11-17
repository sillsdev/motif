# Editor integration uses front matter for context and one fwmd executable

FieldWorks Markdown can be checked and formatted outside either host, using the same readers as the field and record files
(ADRs 0028 and 0032). Source mode is already settled by ADR 0035 (E-1 to E-4). This record releases the context, location and
executable decisions needed by the standalone CLI; the rest of the editor-integration plan remains parked.

**Owner decisions** (2026-11-16):

- **E-9: project context comes from front matter first, then an export-written manifest.** The front matter supplies
  `analysis`, `vernacular` and `writingSystems`. A manifest written during record export will later supply project context;
  that work belongs with R1b. The CLI does not need to open a liblcm project to check a file.
- **E-12: the CLI lives at `textediting/tools/fwmd`.** It stays inside the sealed folder (ADR 0034), beside the shared
  format implementation and fixtures, under the same MIT and clean-room rules (ADR 0010).
- **E-15: one `fwmd` binary.** It provides `check` and `format` now and may later add `lsp`. A separate language-server
  executable is not required by this design; this decision does not release LSP implementation.
- **Config v1 is front matter plus an optional explicit `--config fwmd.json`.** Config supplies analysis and vernacular
  defaults, writing systems and styles; front matter overrides its defaults and writing-system details. This is not automatic manifest discovery. The
  export-written manifest is deferred to R1b.

**Still parked** (Motif #57): E-5 to E-8, E-10, E-11, E-13 and E-14. Whole-record source views, the VS Code client/server
architecture, JSON-RPC implementation, editor-server distribution, language id, preview, publishing destinations and npm package
approval are not decided here. The historical research recommendations for them remain proposals. A live project-backed editor
mode is also future work, outside config v1.

**Defaults** (implementation choices, changeable without a new ADR):

- `check` accepts paths, recursive directories or stdin, with text or JSON findings; `format` writes canonical text to stdout,
  rewrites with `--write`, or compares with `--check`.
- `--schema <file.json>` loads serialized `RecordSchema` data for full record checks. Without it, check reads the frame only,
  and format skips record files. No built-in project schema is inferred.
- The CLI's internal document service dispatches record files to `RecordFileReader` and other text to `MarkdownConverter`.
  A public document service, source map and LSP adapter are not introduced by this decision.
- Native AOT is a publish-time choice through `-p:FwmdAot=true`; the normal build does not request native compilation.

**Considered:** waiting for a manifest or live project loader would delay checks that already have enough context in the file.
An explicit config supplies the missing style and writing-system inventory until export writes a manifest. Separate CLI and server
binaries would duplicate packaging without a present need. The original editor-integration draft bundled these decisions with VS Code
and preview work; releasing only these three keeps those proposals open.

**Consequences:** the CLI can ship without an editor extension, liblcm or new editor packages. Front matter remains useful alone;
a config adds project facts without loading a project. Manifest content and discovery need their own R1b design. Nothing here
changes ADR 0035's field source mode or authorizes the parked editor features.

Owner decision, 2026-11-16; context from editor-integration research report 21 and its draft ADR.
