# Rewrite lexeme forms

```sh
motif compose-rewrite-lexeme-form --project language.fwdata --draft <name> \
  --intent '{"setEmptyCitationForm":true,"foldStressMarks":true,"entries":[{"entryGuid":"<id>","writingSystem":"ceb","expectedForm":"bágo"}]}'
```

Rewrites the lexeme form of existing entries, many in one Draft. Each entry names the writing system and the `expectedForm` it holds now; a different current form refuses the whole batch and stages nothing. Give `newForm` for each entry, or set `foldStressMarks` and omit it.

`foldStressMarks` computes the new form from `expectedForm`: decompose to NFD, remove only U+0300 (grave), U+0301 (acute), U+0302 (circumflex), U+0304 (macron) and U+030C (caron), recompose to NFC. Every other mark, such as the tilde of n-tilde, is kept. In Wolff's Cebuano orthography the grave marks a final glottal stop and the acute marks length, so the marks carry meaning; that is why `setEmptyCitationForm` matters.

`setEmptyCitationForm` keeps the old spelling: an entry whose citation form is empty in that writing system gets its old lexeme form as the citation form. A citation form already there is never changed. An entry whose form would not change is skipped.

The command stages writes in the Draft. It does not change the FieldWorks project. Finish the Draft, inspect its Dry Run and Assess a word list before and after; only a person can Apply a Proposal.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on the operations this command produces.
