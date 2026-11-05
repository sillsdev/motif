# Merge entries

Merge one entry into another so the two become one entry. Use it when two entries are the same word, for example a duplicate left by an import or a spelling variant that is better kept as an alternate form. It is the Merge Entry of FieldWorks, run by LibLCM's own entry merge.

```sh
motif compose-merge-lexical-entries --project language.fwdata --draft duplicates \
  --intent '{"entry":"<entry-to-merge-id>","into":"<entry-that-stays-id>"}'
```

`entry` and `into` are required and the intent is closed. The entry named by `entry` ceases to exist; the entry named by `into` stays.

What the merge does, as FieldWorks does it with no text lost:

- The senses, grammatical info (analyses) and alternate forms of the merged entry are added to the entry that stays.
- When the two lexeme forms differ in text, the merged entry's lexeme form becomes an alternate form of the entry that stays; when they are equal, they become one form.
- Every reference to the merged entry, such as a variant, complex form, lexical relation or parser analysis, points at the entry that stays. References between the two entries themselves are dropped, because an entry cannot refer to itself.
- Equal alternate forms and equal analyses become one.
- Text fields that differ are joined; a field that only the merged entry has is copied.

The command refuses, and changes nothing, when `entry` and `into` are the same entry, when either id is not an entry, when the two lexeme forms are different kinds of form (for example a plain affix form and an affix process), or when they have different morph types. The message names both entries and the difference; make them agree first, for example with `compose-edit-morph-type`, or keep both entries.

The Dry Run lists the merged entry's headword, forms, senses, analyses and entry references and the entry they join, and shows the merged entry leaving the lexicon.

Use this editor through the CLI or the matching MCP tool, which only stages. Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite every operation this command produces. Review the Dry Run effects before Finalize; only a person can Apply the Proposal.
