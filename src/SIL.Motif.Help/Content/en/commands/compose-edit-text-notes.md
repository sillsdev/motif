# Edit the Notes of existing text segments

Sets or replaces the Notes of segments in Texts that already exist, in bulk, without creating a new Text. Each edit names a Text (its name, or its id when two Texts share a name), a segment or a word, and the Note. The segment then holds exactly that one Note: the first existing Note keeps its identity and its text in the analysis writing system is replaced, and any other Notes of the segment are removed, with whatever text they hold in other writing systems. Free and Literal Translations, words and the baseline are untouched.

```sh
motif compose-edit-text-notes --project language.fwdata --draft my-draft \
  --intent '{"edits":[{"text":"Tracking","word":"balay","note":"loan: Spanish"},{"text":"Tracking","segment":12,"note":"proper name"}]}'
```

`segment` is the 1-based number of the segment counted across all paragraphs of the Text. `word` is a form in NFD; it must occur in exactly one segment of that Text, otherwise the edit is refused and names the segment numbers to choose from. Two edits for one segment are refused. Notes are written in `analysisWs` when given, otherwise in the project's default analysis writing system.

For thousands of edits, use one intent with many edits, not one intent per edit; either put the intent in a file and give a tab-separated file, or use a single `compose-batch` entry whose intent holds all the edits. Thousands of separate intents are slow, because each one is composed on its own.

Give a tab-separated file of `Text<TAB>segment-number-or-word<TAB>Note` lines instead of `--intent` (the two cannot be combined):

```sh
motif compose-edit-text-notes --project language.fwdata --draft my-draft --notes-tsv notes.tsv [--analysis-ws en]
```

The file may start with the exact header line `Text<TAB>Locator<TAB>Note` (any capitalization), which is skipped; a first line that differs from it is data. A locator of digits that is a whole number is a segment, anything else a word, so a numeric word is addressed by its segment. There is no quoting: a cell holds no tab or line break, and quote marks stay in the Note. Blank lines are skipped.

All unresolved or ambiguous edits are reported together, each named by its edit number (or TSV line), so one run lists everything to fix.

Each staged operation remembers every Note's text in every writing system. If a segment's Notes change in any of them before Apply, that Apply is refused (`text-notes.stale-source`) and you compose the edit again. The Dry Run lists every segment changed; a Notes-only Proposal changes no grammar and no parse, so its Trial measures only the words of the edited segments and matches the baseline. Only a person can Apply the Proposal. Add `--cite-source <text>` and optionally `--cite-locator <text>` to record a source on every operation.
