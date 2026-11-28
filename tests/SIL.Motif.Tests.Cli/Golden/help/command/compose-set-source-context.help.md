# Change a Source's Context

Stage the books and chapters a Source covers. The Context is read in the Source's versification.

```sh
motif compose-set-source-context --project language.fwdata --draft pending --intent '{"source":"WEB","context":"MAT-ACT"}'
```

The command refuses a Context that does not read in the versification.
