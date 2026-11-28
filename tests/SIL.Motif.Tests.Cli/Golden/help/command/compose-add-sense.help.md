# Add a sense to an entry

Add one new sense to an entry that exists. The sense points at one of the entry's own analyses, so the analysis, the alternate forms and the stem names stay shared and no homograph entry is needed for a second meaning.

```sh
motif compose-add-sense --project language.fwdata --draft tam-d001 \
  --intent '{"entry":"<entry-id>","gloss":"be extinguished","analysisWs":"en"}'
```

`entry`, `gloss` and `analysisWs` are required and the intent is closed. `definition` is an optional definition in the same writing system. `analysis` is the id of one of the entry's analyses. Without it the sense uses the entry's only analysis; an entry with several refuses and lists their ids with a ready intent, so name one and run it again. The new sense goes last.

The command refuses, and changes nothing, when:

- the entry does not exist or the id names another kind of object;
- `analysis` is not an analysis of that entry; the refusal lists the entry's analyses;
- the entry has no analysis, or more than one and `analysis` is missing;
- `analysisWs` is not an analysis writing system of the project;
- the entry already has a sense with that gloss; the refusal names it and points to `compose-edit-gloss`.

A meaning that needs its own grammatical analysis is `compose-add-morphosyntax-analysis` with `gloss`. A meaning that is a separate word is `compose-author-lexical-entry`. To move an existing sense to another analysis of the entry, use `compose-set-sense-msa`.

Add `--cite-source <text>` and optionally `--cite-locator <text>` (at most 300 characters) to cite every operation this command produces. Review the Dry Run effects before Finalize; only a person can Apply the Proposal.
