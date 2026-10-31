# Merge a phoneme into another

Use this command when two phonemes are really one sound that the language writes two ways, such as a vowel written `u` and `o`. The first phoneme (the source) is merged into the second (the target): every spelling of the source is added to the target after the target's own, and the source is removed. Use `compose-edit-phoneme` instead to add a spelling no phoneme has yet.

```sh
motif compose-merge-phoneme --project language.fwdata --draft sounds \
  --intent '{"source":"<phoneme-id-to-remove>","target":"<phoneme-id-to-keep>"}'
```

The intent has exactly `source` and `target`. The Dry Run lists the natural classes the source leaves, and the Proposal shows the removal of the source and each spelling added to the target.

What changes and what does not:

- The target keeps its identity, its features and its natural classes. They now describe the merged sound.
- The source leaves every natural class it is in. It is not added to the target's classes, so no class gains a member the language did not give it.
- The source's features are discarded with it. Check them before you merge.
- Each moved spelling is a new representation on the target; its identity is not the source's.
- Words and environments keep their text. A word spelled with a moved spelling reads as the target.

Refusal messages begin with a stable name:

- `phoneme-merge-same`: the source and the target are one phoneme.
- `phoneme-not-in-inventory`: a phoneme is not in the first phoneme set.
- `phoneme-merge-nothing-to-move`: the source has no spelling to move.
- `phoneme-merge-code-conflict`: a spelling of the source also belongs to another phoneme or boundary, the target included. Fix the spelling with `compose-edit-phoneme` first.
- `phoneme-merge-class-would-empty`: the source is the only member of a natural class. Edit or remove that class first.
- `phoneme-merge-referenced`: an environment writes a spelling of the source, or a rule or another object uses the source. Merging would widen what it matches, so change it first.

To take a merge back, apply `compose-split-phoneme`. The command stages operations in a Draft and does not change the saved FieldWorks project. Dry Run and Trial the Proposal, then Finalize it for a person to review and Apply.
