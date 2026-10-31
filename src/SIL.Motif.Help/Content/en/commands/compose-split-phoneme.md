# Split a spelling into a phoneme

Use this command to move one spelling of a phoneme into a new phoneme of its own. It undoes `compose-merge-phoneme`, and it also separates a spelling that turns out to be a real contrast.

```sh
motif compose-split-phoneme --project language.fwdata --draft sounds \
  --intent '{"source":"<phoneme-id>","representation":"o","name":"o"}'
```

The intent has exactly `source`, `representation` and `name`. The phoneme keeps its other spellings. The new phoneme has the given name and the one spelling, and no natural class and no features, so add it to its natural classes afterwards with `compose-edit-natural-class`.

Refusal messages begin with a stable name:

- `phoneme-not-in-inventory`: the source is not a phoneme of the first phoneme set.
- `phoneme-representation-not-found`: the phoneme has no such spelling.
- `phoneme-split-last-representation`: the spelling is the only way the phoneme is written, so nothing would stay behind.
- `phoneme-split-name-missing`: the new phoneme has no name.
- `phoneme-representation-referenced`: an environment writes the spelling; change that environment first.

The command stages operations in a Draft and does not change the saved FieldWorks project. Dry Run and Trial the Proposal, then Finalize it for a person to review and Apply.
