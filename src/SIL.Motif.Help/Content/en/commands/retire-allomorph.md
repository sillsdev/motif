# Retire or replace a form

Use this command to stage one ordinary prefix or suffix retirement in a Draft. Read the entry with detailed `motif_lexicon` output to copy its form identities and semantic digests. The closed intent names each retired form, its exact surviving form, and every bundle or ad hoc reference that must move.

```sh
motif retire-allomorph --project language.fwdata --draft remove-duplicate --intent '{"format":"motif-retire-allomorph","version":1,"retirement":{...}}'
```

For a listed allomorph replacement with a sound rule, use the `motif-allomorph-retirement` version 2 intent. It names the natural class, rewrite rule, each retired form, every destination, and the operation bindings in the same Proposal:

```sh
motif retire-allomorph --project language.fwdata --draft replace-listed --intent '{"format":"motif-allomorph-retirement","version":2,"naturalClass":{...},"rule":{...},"retirements":[...],"operations":[...]}'
```

The four displayed parts stay in that one Proposal: the sound class and rule; analyses whose bundle Morph moves, including the copied bundle Form alternatives; other references that move; and the retired forms. The dependency graph orders the writes. Dry Run reads back both Morph and native Form effects; no display order changes mutation order, and no part can be applied on its own.

The reference census refuses an incomplete or changed view, an unsupported reference, a form that no longer matches its authored identity, or an owned dependent that deletion would remove. When a destination is missing, the refusal names the number of distinct Approved analyses and ad hoc rules still unresolved, plus other references. Declare every destination and run Dry Run again.

Review the paired before and after reading evidence for every affected Approved reading. The review keeps each FieldWorks Opinion and surface spelling beside its parser result and rule attribution. Missing identity, parser completion, read-back, or rule trace is shown as unavailable; it is never counted as a preserved reading. Check that the detector finding is resolved in the rebuilt after Report, separately from whether a disposition is Suppressed, before a person Applies the whole Proposal.
