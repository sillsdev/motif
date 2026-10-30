# Move an example to another sense

Move one example sentence, with its translations, from its sense to another sense. The target sense may belong to a different entry. Use it when a duplicate meaning is about to be deleted with `compose-delete-sense` but holds examples the surviving meaning lacks.

```sh
motif compose-move-example --project language.fwdata --draft ceb-d004 \
  --intent '{"example":"<example-id>","toSense":"<sense-id>"}'
```

`example` and `toSense` are required and the intent is closed. `after` or `before`, never both, name an example of the target sense to place the moved one next to; without either it goes last.

The example keeps its identity, text in every writing system, reference and translations; only its owning sense changes, as LibLCM allows. FieldWorks itself offers no such move, so nothing here copies the example: the old sense no longer has it.

The command refuses, and changes nothing, when:

- the example or the sense does not exist, or the ids name other kinds of object;
- the target sense is the sense that already owns the example;
- anything outside the example refers to it, such as an annotation or a relation; what such a reference means can depend on the sense that owns the example, so the message names the referrer and it must be removed or retargeted first;
- `after` or `before` is not an example of the target sense.

The target sense may be created by an earlier step of the same Draft. The Dry Run lists, for the old and the new sense, their examples before and after, and shows the full example in every writing system with its translations, the old sense with its entry and gloss, and the target sense with its entry and gloss.

Use this editor through the CLI. Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite every operation this command produces. Review the Dry Run effects before Finalize; only a person can Apply the Proposal.
