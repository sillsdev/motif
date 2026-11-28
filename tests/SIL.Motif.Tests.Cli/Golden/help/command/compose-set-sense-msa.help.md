# Point a sense at an analysis

Point an existing sense at another analysis of the same entry. The sense keeps its identity, glosses, definitions, examples and sub-senses.

```sh
motif compose-set-sense-msa --project language.fwdata --draft ceb-d003 \
  --intent '{"sense":"<sense-id>","analysis":"<analysis-id>"}'
```

Both fields are required and the intent is closed. `analysis` must be an analysis the sense's own entry already has, and must suit the entry's lexeme form: a stem analysis for a stem, an affix analysis for an affix. A sub-sense may be pointed too.

The command refuses, and changes nothing, when:

- the sense does not exist or the id names another kind of object;
- `analysis` is not an analysis of the sense's entry; the refusal lists the entry's analyses and names `compose-add-morphosyntax-analysis` for one the entry lacks;
- the sense already uses that analysis;
- a stem analysis would go on an affix, or an affix analysis on a stem;
- the sense is the last to use its analysis and a parser analysis (morph bundle) or an ad hoc prohibition still refers to it.

When no other sense uses the old analysis, LibLCM removes it, and the operation's rationale says so, naming any slot membership that goes with it. The Dry Run shows the sense and its entry; run a Trial to see which parses change.

Use this editor through the CLI or the MCP tool `motif_set_sense_msa`. Add `--cite-source <text>` and optionally `--cite-locator <text>` (at most 300 characters) to cite the operation. Review the Dry Run effects before Finalize; only a person can Apply the Proposal.
