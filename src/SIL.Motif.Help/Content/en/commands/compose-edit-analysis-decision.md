# Decide an analysis

```sh
motif compose-edit-analysis-decision --project language.fwdata --draft <name> \
  --intent '{"decision":"approve","analysis":"<analysisId>"}'
```

The closed intent records one human decision. For `approve`, `disapprove` or `unknown`, name the stored analysis by its exact canonical identity in `analysis`; `unknown` removes an earlier approval or disapproval and is refused when the analysis has none. For `misspelled`, name the wordform in `wordform` instead: `{"decision":"misspelled","wordform":"<wordformId>"}`. A misspelling is word-level: it sets the wordform's spelling status to Incorrect. Record the intended form with it, as `intendedForm` with its `form` and the `writingSystem` tag, and a `note` saying why: `{"decision":"misspelled","wordform":"<wordformId>","intendedForm":{"form":"kita","writingSystem":"ceb"},"note":"Bible 2019 reads kita here"}`. The form and note are kept as decision evidence on the operation (tier `misspelled`) and read back with `motif evidence show <wordformId>`. A writing system the project lacks, a blank form, or a form equal to the wordform's own spelling is refused.

To store a parser reading as a new analysis in the unknown state, name the wordform and the reading's ordered morphs by exact identity: `{"decision":"add-reading","wordform":"<wordformId>","morphs":[{"form":"<allomorphId>","msa":"<msaId>","inflType":"<inflTypeId>"}]}`. Each morph names at least one of `form`, `msa`, `inflType` or `guessedString`. A reading that already has a stored analysis is refused. The result lists the new analysis in `createdObjects`; use that identity to approve or disapprove it in the same Draft.

To clear a mark, use `{"decision":"clear-misspelled","wordform":"<wordformId>","note":"..."}`. It returns the spelling to undecided, keeps the note as evidence, and is refused when the wordform is not marked misspelled. To correct a mark, clear it, then mark it again with the right intended form.

The command uses the same operations a collected word change does. Finish the Draft, inspect its Dry Run, and review it. A person Applies the Proposal. An Autonomous run (`--autonomous` or `MOTIF_AUTONOMOUS=1`) may Apply it too, with `--message`, once the newest Trial's declared gate is green; that is the same gate every other edit meets.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.
