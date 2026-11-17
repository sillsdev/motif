# State the project's source

States once, in the project's Description, which source, revision and licence imported data came from. The notice reads `Attribution: <source>@<revision> <licence>`, such as `Attribution: lang-sme@9ba7be7 GPL-3.0`. It is the project half of how a project keeps the licence of imported data; [how you handle attribution](cmd:lexicon-import) says when to use it, and [compose-set-entry-attribution](cmd:compose-set-entry-attribution) tags the entries.

```sh
motif compose-set-project-attribution --project language.fwdata --draft my-draft \
  --intent '{"tag":"lang-sme@9ba7be7 GPL-3.0"}'
```

The notice is written in an analysis writing system: `analysisWs` when given, otherwise the project's default analysis writing system. It follows whatever the Description already holds in that writing system, on a new line, and the existing text is kept. The command is idempotent: a Description that already states the notice exactly stages nothing. A different revision of the same source is added as a second notice. A Description that already states the same source and revision under a different licence is refused, because which licence applies is for a person to decide.

The change is an edit of the whole Description for that writing system, so a change made in FieldWorks after the Dry Run is caught by the Proposal's anchor at Apply. It moves no parse.

The command stages a change in the Draft; it does not change the project. Add `--cite-source <text>` and optionally `--cite-locator <text>` to record a source on the operation. Only a person can Apply a Proposal.
