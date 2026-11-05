Stages Gloss, the short translation equivalent of an existing sense, in a Draft.

```
motif compose-edit-gloss --project language.fwdata --draft <name> \
  --intent '{"target":"<senseId>","alternatives":[{"writingSystem":"en","expectedText":"<current gloss>","text":"<short equivalent>"}]}'
```

Each alternative checks the current plain text against `expectedText` in that writing system before staging anything. Use an empty expected string for an empty alternative, and `"text":""` for a guarded clear. Canonically equivalent Unicode spellings compare equal. A stale alternative refuses the whole edit. Unmentioned writing systems stay as they are.

A gloss is usually a few words for interlinear text. Put the full meaning in [Definition](cmd:compose-edit-definition). Motif allows the text FieldWorks accepts and imposes no word-count limit. The advanced `add-set-gloss` command stages an unguarded semantic operation; this composer checks the authored expectation first.

Dry Run and Trial the Draft, then Finalize for review. Staging changes no saved FieldWorks data. Only a person can Apply.
