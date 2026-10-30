# Delete a sense

Delete one sense, top-level or sub-sense, together with its sub-senses and examples. Use it when the meaning is kept somewhere else, for example a derived word that was folded into its root as a sense and now has an entry of its own. For a sense with nothing in it, `compose-delete-empty-sense` is the stricter command.

```sh
motif compose-delete-sense --project language.fwdata --draft ceb-d003 \
  --intent '{"target":"<sense-id>","owner":"<entry-id>"}'
```

`target` and `owner` are required and the intent is closed. `owner` is the entry that owns a top-level sense, or the parent sense of a sub-sense, so a wrong id is refused instead of deleted.

What goes: the sense, every sub-sense and example under it, and the entry's grammatical analysis that only these senses used. An analysis is removed only when no remaining sense uses it and nothing else, such as a parser analysis, refers to it. FieldWorks leaves such an analysis in the entry; here it goes so the entry keeps no unused analysis. The Dry Run lists the deleted sense and its owner's remaining senses, and for the sense and each deleted sub-sense its gloss and definition in every writing system, its notes, source, scientific name, semantic domains, status, examples with their translations, and pictures (file and caption), plus each deleted analysis, so a reviewer sees exactly what goes. Semantic domains and status are shown by name. A writing system the project cannot resolve is shown by its numeric handle.

The command refuses, and changes nothing, when:

- the sense is the only sense of its entry, as FieldWorks does, because an entry must keep a sense;
- anything refers to the sense or to one of its sub-senses or examples: a lexical relation or cross-reference, a component or primary lexeme of a variant or complex-form reference, a reversal entry that lists the sense, or a morph bundle of a parser analysis; the message names the referrer, and the reference must be removed or retargeted first;
- `owner` is not the sense's owner.

A Draft may delete several senses; each is composed against the Draft's earlier steps, so a second deletion from the same entry sees the first one's result.

Use this editor through the CLI. Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite every operation this command produces. Review the Dry Run effects before Finalize; only a person can Apply the Proposal.
