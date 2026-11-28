# Confirm a reviewed negative

```sh
motif parsimony negative confirm --project language.fwdata --draft <name> \
  --intent '<closed intent JSON>' \
  --confirm "I confirm this form or reading is forbidden in the stated context." [--json]
```

This HumanOnly command stages a human-confirmed surface or exact reading negative as Notebook operations in a Draft Proposal. Supply the Notebook `recordTypeId`, stable `caseId`, vernacular `writingSystem`, `form`, `context`, and a `target` with discriminator `"type":"surface"` or `"type":"reading"`. A reading target carries an ordered `morphs` array; each `identity` names the exact `form`, `msa`, and optional `inflType` portable IDs. `wordformId` and `analysisId` are optional, so the negative can exist without a wordform or grammar owner. `reason`, human actor fields, timestamp, and source `reportId` are optional.

Example surface target:

```json
{
  "recordTypeId": "record-type/<22-character-base64url-id>",
  "caseId": "case_0000000000000000000002",
  "writingSystem": "qaa",
  "form": "example",
  "context": "the stated environment",
  "target": { "kind": "surface" }
}
```

The command appends the existing Notebook create, description, and reserved-field operations to the Draft. Review the Draft with `dry-run`, then a person uses `apply` to save the judgment in FieldWorks. Until Apply succeeds, the negative is not part of the saved project. Run `parsimony expectations` to read it back.
