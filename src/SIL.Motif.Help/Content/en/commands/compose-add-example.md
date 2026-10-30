# Add an example to a sense

Add one new example sentence to a sense, with its text in one or more writing systems and optional translations. To bring an example over from another sense, use `compose-move-example`.

```sh
motif compose-add-example --project language.fwdata --draft ceb-d005 --intent '{
  "sense":"<sense-id>",
  "text":[{"ws":"<vernacular-tag>","text":"..."}],
  "translations":[{"type":"free","text":[{"ws":"<analysis-tag>","text":"..."}]}]
}'
```

`sense` and `text` are required and the intent is closed. `text` and each translation's `text` are lists of `{ws, text}` objects with one entry per writing system and no empty text. Every `ws` must be a vernacular or analysis writing system of the project. `reference` is an optional reference string. A translation's `type` is `free` (the default), `literal` or `back`. `after` or `before`, never both, name an example of the sense to place the new one next to; without either it goes last.

The new example and each translation get fresh identities when the Draft is composed.

The command refuses, and changes nothing, when:

- the sense does not exist or the id names another kind of object;
- a writing system is not one of the project's, or one is given twice in the same text, or a text is empty;
- `after` or `before` is not an example of the sense.

The sense may be created by an earlier step of the same Draft. The Dry Run lists the sense's examples before and after, and shows the whole new example in every writing system with its translations, and the sense with its entry and gloss.

Use this editor through the CLI. Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite every operation this command produces. Review the Dry Run effects before Finalize; only a person can Apply the Proposal.
