# Change a Source's versification

Stage a new versification for a Source. Its present Context must still read in the new one.

```sh
motif compose-set-source-versification --project language.fwdata --draft pending --intent '{"source":"WEB","versification":"Original"}'
```

The command refuses a versification Motif does not know.
