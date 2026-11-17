# Tag entries with their source

Records where lexical entries came from, in the Bibliography of each entry, as the Attribution tag `<source>@<revision> <licence>`, such as `lang-sme@9ba7be7 GPL-3.0`. It is the per-entry half of how a project keeps the licence of imported data; [how you handle attribution](cmd:lexicon-import) says when to use it and what goes in the project half.

```sh
motif compose-set-entry-attribution --project language.fwdata --draft my-draft \
  --intent '{"tag":"lang-sme@9ba7be7 GPL-3.0","entries":[{"entry":"<entry-id>"},{"entry":"<entry-id>"}]}'
```

For a long list, give a text file of entries and the tag instead of `--intent` (the two cannot be combined):

```sh
motif compose-set-entry-attribution --project language.fwdata --draft my-draft \
  --entries-file entries.txt --tag "lang-sme@9ba7be7 GPL-3.0" [--analysis-ws en]
```

The file holds one entry per line, as an entry id or as a FieldWorks GUID. Blank lines and lines starting with `#` are skipped. A line that is neither an id nor a GUID, and an entry the project does not have, are refused together in one message, each with its line number, so one run lists everything to fix. Nothing is staged when any line is refused.

The tag is written in an analysis writing system: `--analysis-ws` (the intent's `analysisWs`) when given, otherwise the project's default analysis writing system; a vernacular-only writing system is refused. The entry's Bibliography keeps whatever it already holds in that writing system, and the tag follows it after `; `. The command is idempotent: an entry that already carries the tag exactly stages nothing, so a repeated run adds no second copy. A different revision of the same source is added beside the first. An entry that already carries the same source and revision under a different licence is refused, because which licence applies is for a person to decide.

Each staged operation sets the whole Bibliography alternative for that writing system, so text added in FieldWorks after the Dry Run is caught by the Proposal's anchor at Apply. The Bibliography is exported as a bibliography note in a LIFT export, so the tag survives a round trip. Changing it moves no parse, so a Trial of this Proposal measures only what it names.

The command stages a change in the Draft; it does not change the project. Add `--cite-source <text>` and optionally `--cite-locator <text>` to record a source on every operation. Only a person can Apply a Proposal.
