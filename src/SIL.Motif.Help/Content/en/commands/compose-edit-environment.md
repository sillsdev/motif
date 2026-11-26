# Edit a phonological environment

Rewrites the left context, the right context or both of an existing environment in place. The environment keeps its identity, so every allomorph that names it keeps its link and needs no re-pointing.

```sh
motif compose-edit-environment --project language.fwdata --draft envfix \
  --intent '{"environment": "<environment-id>", "expectedText": "/ ா _ #",
             "left": [{"naturalClass": "<class-id>"}]}'
```

The intent has `environment`, `expectedText` and at least one of `left` and `right`; it is closed. A side you give replaces that whole side; a side you leave out is kept. Each context names exactly one `phoneme`, `naturalClass`, `boundary` (`"word"`, at the outer edge) or `boundaryMarker`, as in `compose-author-environment`. A phoneme respelling, a phoneme-to-class change or a swap is a new `left` or `right`. `expectedText` must match the current string or name, so a stale target is refused.

A kept side is read back from the stored string and must render exactly as it is stored. If it does not (optional groups, a phoneme with several spellings, a morpheme boundary), give both sides. Give both sides to repair an environment whose string no longer parses.

The Dry Run lists the allomorphs and rules that hold the environment. The edit is one operation, so one Proposal carries it atomically.

Refusal messages begin with a stable name:

- `environment-edit-empty`: neither `left` nor `right` is given.
- `environment-edit-not-found`: the id is not an environment of the phonological data.
- `environment-edit-stale`: the environment no longer carries `expectedText`.
- `environment-edit-invalid`: a context names an unknown phoneme or natural class, a morpheme boundary, or the result would have no context.
- `environment-edit-unparseable`: the side kept as it is does not read back; give both sides.
- `environment-edit-unchanged`: the new string equals the current one.

In FieldWorks: Grammar, Phonology, Environments, select the environment, edit its pattern.

Use this editor through the CLI. Add `--cite-source <text>` and optionally `--cite-locator <text>` (at most 300 characters) to cite the operation. Only a person can Apply the Proposal.
