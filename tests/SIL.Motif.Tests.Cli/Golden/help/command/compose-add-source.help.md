# Add a Source

Stage a new Source in the project's Motif Sources list. The first one creates the list. The intent gives the id, name, versification, Context and kind; nothing is written until a person applies the pending changes.

```sh
motif compose-add-source --project language.fwdata --draft pending --intent '{"id":"WEB","name":"World English Bible","versification":"English","context":"MAT-JHN","kind":"Paratext"}'
```

The command refuses an id that is empty, has spaces, is in use or was removed (a removed Source is restored, never re-added), a versification Motif does not know, and a Context that does not read in it. To stage a Source and apply it in one step use `motif source add --apply`.
