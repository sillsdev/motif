Stages Definition, the full meaning of an existing sense, in a Draft.

```
motif compose-edit-definition --project language.fwdata --draft <name> \
  --intent '{"target":"<senseId>","alternatives":[{"writingSystem":"en","expectedText":"<current meaning>","text":"<full meaning>"}]}'
```

Use the sense's canonical identity, not its entry's identity. Each alternative checks the current plain text in that writing system against `expectedText` before staging anything. Give `"expectedText":""` when it has no text. Canonically equivalent Unicode spellings compare equal. Every alternative must match; a stale value refuses the entire edit.

Give `"text":""` to clear an alternative. Other writing systems stay as they are. Text is preserved, including whitespace; Motif sets no word-count limit. Gloss is the short translation equivalent, while Definition holds the full meaning; use [Edit Gloss](cmd:compose-edit-gloss) to revise the short equivalent separately.

Dry Run and Trial the Draft, then Finalize for review. Staging changes no saved FieldWorks data. Only a person can Apply.
