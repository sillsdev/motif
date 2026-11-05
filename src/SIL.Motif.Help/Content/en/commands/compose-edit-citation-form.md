# Edit a citation form

```sh
motif compose-edit-citation-form --project language.fwdata --draft <name> \
  --intent '{"target":"<entry-id>","writingSystem":"qaa","expectedForm":"","newForm":"páta"}'
```

Writes a selected source headword on an existing lexical entry. `expectedForm` is required, including an empty string when no Citation Form is held in that writing system. Comparison uses canonical Unicode equivalence. Set `newForm` to an empty string to clear that alternative; a nonempty replacement must contain non-whitespace text. A canonically equivalent replacement, including clearing an already absent alternative, produces no write. A missing or non-entry target, stale text or a writing system outside this project's vernacular systems refuses before staging.

Only the selected Citation Form alternative changes. Lexeme and alternate forms, their identities and references, and other writing-system alternatives stay intact. Citation Form is not parsed: retain a marked dictionary spelling here while the parser forms carry source-supported underlying distinctions. Use [Rewrite lexeme forms](cmd:compose-rewrite-lexeme-form) with `foldStressMarks=false` or [Edit an allomorph form](cmd:compose-edit-allomorph-form) for parser forms. The rewrite command's `setEmptyCitationForm` only copies the old lexeme spelling; it cannot supply a different selected source headword.

The CLI and MCP tool stage operations in a Draft. Inspect the Dry Run and Trial results before a person Applies the Proposal. No project is changed by composing. Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the selected evidence.
