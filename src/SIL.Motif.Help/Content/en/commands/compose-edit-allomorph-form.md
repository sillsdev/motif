# Edit an allomorph form

```sh
motif compose-edit-allomorph-form --project language.fwdata --draft <name> \
  --intent '{"entry":"<entry-id>","target":"<alternate-id>","writingSystem":"qaa","expectedForm":"pata","newForm":"pātaʔ"}'
```

Selects one existing ordinary stem or affix alternate by `target` and expected owning `entry`. Its current text in `writingSystem` must match `expectedForm` by canonical Unicode equivalence. A moved or deleted target, a different owner, a lexeme form, an affix process, a writing system outside this project's vernacular systems, or stale text refuses the edit before anything is staged. Set `newForm` to an empty string to clear that alternative; a nonempty replacement must contain non-whitespace text. A canonically equivalent replacement, including clearing an already absent alternative, produces no write.

The Form write preserves the allomorph ID, alternate order, morph type, abstract flag, sound environments, stem name, inflection classes, required features, references and other writing-system alternatives. Underlying does not mean abstract: FieldWorks skips ordinary abstract forms. Every underlying phoneme needs a nonempty representation; ordered rules realize distinctions omitted by running spelling.

For lexeme forms use [Rewrite lexeme forms](cmd:compose-rewrite-lexeme-form) with `foldStressMarks=false` and explicit `newForm`. Preserve a selected marked headword with [Edit a citation form](cmd:compose-edit-citation-form).

The CLI and MCP tool stage operations in a Draft. Inspect the Dry Run and Trial results before a person Applies the Proposal. No project is changed by composing. Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the selected evidence.
