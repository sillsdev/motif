# Rewrite lexeme forms

```sh
motif compose-rewrite-lexeme-form --project language.fwdata --draft <name> \
  --intent '{"foldStressMarks":false,"entries":[{"entryGuid":"<id>","writingSystem":"qaa","expectedForm":"pata","newForm":"pātaʔ"}]}'
```

Rewrites the lexeme forms of existing entries, many in one Draft, preserving each form's identity, references, conditions and other writing-system alternatives. Each entry names its writing system and the `expectedForm` it holds now; a different current form refuses the whole batch and stages nothing. Give an explicit `newForm` with `foldStressMarks=false` to restore an underlying distinction from selected evidence.

A parser form may carry represented segments, vowel quantity or lexical stress that running spelling omits. Keep the form nonabstract and each phoneme's representation nonempty. Morphology and sound rules use those distinctions before final ordered rules realize the unmarked written word. Marked dictionary spellings may stay in Citation Form. Restoring information requires source evidence; the command cannot infer missing stops or quantity from plain spelling.

The initial proof targets unmarked running text using simultaneous regular rules ordered on Morphology, with empty Clitics and Surface strata, `NotOnClitics=true`, empty `HC/Strata` and `HC/DelReapps=0` (the FieldWorks default). A value of 0 still permits inverse deletion. Attached clitics and multiple cycles require separately proved domains. PanGloss 0.15.2 completes the generic `patʔa` → `pat-a` proof; the reference hc.dll hangs beyond 600 seconds on `pat-a` with the full simultaneous chain, so parity for that case is unproved.

The legacy `foldStressMarks` option computes the new form from `expectedForm` and requires `newForm` to be omitted: decompose to NFD, remove only U+0300 (grave), U+0301 (acute), U+0302 (circumflex), U+0304 (macron) and U+030C (caron), recompose to NFC. Every other mark, such as the tilde of n-tilde, is kept. This is a destructive spelling fold, not a phonological realization. In Wolff's Cebuano notation acute marks length, grave marks a following glottal stop, and caron marks exceptional stress. Do not fold a distinction needed by the grammar; retaining it only in Citation Form cannot condition parsing.

`setEmptyCitationForm` copies the old lexeme spelling into an empty Citation Form in that writing system. It leaves an existing Citation Form alone and skips an entry whose lexeme form would not change. To preserve a different selected source headword, use [Edit a citation form](cmd:compose-edit-citation-form). To edit an existing ordinary alternate, use [Edit an allomorph form](cmd:compose-edit-allomorph-form).

The command stages writes in the Draft. Finish the Draft, inspect its Dry Run and Trial the actual written words; only a person can Apply a Proposal. A spelling-only result cannot prove quantity or stress erased before output.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on the operations this command produces.
