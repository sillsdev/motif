# Decide an analysis

```sh
motif compose-edit-analysis-decision --project language.fwdata --draft <name> \
  --intent '{"decision":"approve","analysis":"<analysisId>"}'
```

The closed intent records one human decision. For `approve`, `disapprove` or `unknown`, name the stored analysis by its exact canonical identity in `analysis`; `unknown` removes an earlier approval or disapproval and is refused when the analysis has none. For `misspelled`, name the wordform in `wordform` instead: `{"decision":"misspelled","wordform":"<wordformId>"}`.

The command uses the same operations a collected word change does. Finish the Draft, inspect its Dry Run, and review it. Only a person can Apply the Proposal.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.
