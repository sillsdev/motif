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
- `phoneme-representation-invalid`: a representation is empty after removing display dotted circles, or is reserved by the parser for a boundary or null segment.
- `phoneme-representation-duplicate`: a representation already belongs to a phoneme or boundary, or is named twice, so no duplicate segment is created.
- `phoneme-representation-not-found`: a `from` is not a representation of the phoneme.
- `phoneme-representation-referenced`: an environment writes the `from` spelling; change that environment first.

To fold one phoneme into another use `compose-merge-phoneme`, and to move a spelling out again use `compose-split-phoneme`. To set the phonological features of a phoneme use [Set a phoneme's features](cmd:compose-edit-phoneme-features). The command stages operations in a Draft and does not change the saved FieldWorks project. Dry Run and Trial the Proposal, then Finalize it for a person to review and Apply.

Representations are literal, so `(`, `)` and `/` can be added or respelled without environment escaping. [Author a phoneme](cmd:compose-author-phoneme) describes literal storage and the singleton-class route for textual environments. Keep case aliases such as `c`/`C` and `ng`/`Ng`/`NG` on the same phoneme so they share its features. Set a distinct feature bundle with [Set a phoneme's features](cmd:compose-edit-phoneme-features) when a literal must not unify with other sounds.
