# Edit the Notes of existing text segments

Sets or replaces the Notes of segments in Texts that already exist, in bulk, without creating a new Text. Each edit names a Text (its name, or its id when two Texts share a name), a segment or a word, and the Note. The segment then holds exactly that one Note: the first existing Note keeps its identity and its text in the analysis writing system is replaced, and any other Notes of the segment are removed. Free and Literal Translations, words and the baseline are untouched.

```sh
motif compose-edit-text-notes --project language.fwdata --draft my-draft \
  --intent '{"edits":[{"text":"Tracking","word":"balay","note":"loan: Spanish"},{"text":"Tracking","segment":12,"note":"proper name"}]}'
```

`segment` is the 1-based number of the segment counted across all paragraphs of the Text. `word` is a form in NFD; it must occur in exactly one segment of that Text, otherwise the edit is refused and names the segment numbers to choose from. Two edits for one segment are refused. Notes are written in `analysisWs` when given, otherwise in the project's default analysis writing system.

For thousands of edits, put the same intent in a file and use `compose-batch`, so the command line stays short:

```sh
motif compose-batch --project language.fwdata --draft my-draft --intents notes.json
# notes.json: {"intents":[{"verb":"compose-edit-text-notes","intent":{"edits":[...]}}]}
```

Or give a tab-separated file of `Text<TAB>segment-number-or-word<TAB>Note` lines (a first line starting `Text` is skipped; a locator that is a whole number is a segment, anything else a word):

```sh
motif compose-edit-text-notes --project language.fwdata --draft my-draft --notes-tsv notes.tsv [--analysis-ws en]
```

Each staged operation remembers the Notes it started from. If a segment's Notes change before Apply, that Apply is refused (`text-notes.stale-source`) and you compose the edit again. The Dry Run lists every segment changed; a Trial reparses nothing, because the baseline is unchanged. Only a person can Apply the Proposal. Add `--cite-source <text>` and optionally `--cite-locator <text>` to record a source on every operation.
