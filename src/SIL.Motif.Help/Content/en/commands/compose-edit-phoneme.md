# Edit a phoneme's representations

Use this command to change how an existing phoneme is written. A multigraph such as `ng`, `ts` or `dy` is one representation of one phoneme, so add it to the phoneme it spells; do not author a second phoneme for it. Check the inventory first, and use `compose-author-phoneme` only for a sound the inventory lacks.

```sh
motif compose-edit-phoneme --project language.fwdata --draft sounds \
  --intent '{"target":"<phoneme-id>","add":[{"representation":"ng"}]}'
```

The intent has `target` and at least one of `add` and `respell`. Each `add` item is `{"representation":"..."}` and is appended after the phoneme's current codes. Each `respell` item is `{"from":"...","to":"..."}` and rewrites that code in place, keeping its identity.

Refusal messages begin with a stable name:

- `phoneme-edit-empty`: nothing to add or respell.
- `phoneme-not-in-inventory`: the target is not a phoneme of the first phoneme set.
- `phoneme-representation-invalid`: a representation is blank or holds whitespace or environment syntax.
- `phoneme-representation-duplicate`: a representation already belongs to a phoneme or boundary, or is named twice, so no duplicate segment is created.
- `phoneme-representation-not-found`: a `from` is not a representation of the phoneme.
- `phoneme-representation-referenced`: an environment writes the `from` spelling; change that environment first.

Removing a representation and editing phonological features are not supported. The command stages operations in a Draft and does not change the saved FieldWorks project. Dry Run and Trial the Proposal, then Finalize it for a person to review and Apply.
