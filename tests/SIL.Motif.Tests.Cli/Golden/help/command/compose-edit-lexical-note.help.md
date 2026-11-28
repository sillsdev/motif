Stages an entry Note or one of the named sense notes shown in FieldWorks Lexicon Edit.

```
motif compose-edit-lexical-note --project language.fwdata --draft <name> \
  --intent '{"target":"<entryId>","field":"note","alternatives":[{"writingSystem":"en","expectedText":"","text":"<example word and explanation>"}]}'
```

`field` chooses a FieldWorks field:

| Value | FieldWorks name | Target |
| --- | --- | --- |
| `note` | Note | Entry |
| `anthropology-note` | Anthropology Note | Sense |
| `discourse-note` | Discourse Note | Sense |
| `general-note` | General Note | Sense |
| `grammar-note` | Grammar Note | Sense |
| `phonology-note` | Phonology Note | Sense |
| `semantics-note` | Semantics Note | Sense |
| `sociolinguistics-note` | Sociolinguistics Note | Sense |

Each writing-system alternative must give `writingSystem`, `expectedText` and `text`. The current plain text must match the expectation; canonically equivalent Unicode spellings compare equal. An empty expectation means no current text. Empty replacement text clears that alternative. A stale alternative refuses the entire edit, and unmentioned writing systems stay as they are.

Record example words for affix entries in Note. FieldWorks has no built-in Note on stem allomorphs, affix allomorphs or affix processes: record an allomorph's spelling and example in its owning entry's Note. No artificial allomorph field is created. Project custom fields and the separate Extended Notes collection are different fields.

Dry Run and Trial the Draft, then Finalize for review. Staging changes no saved FieldWorks data. Only a person can Apply.
