# State a hypothesis

`hypothesis add` records a rule hypothesis in the ledger from a JSON file: statement, mechanism, change, the neighbour check and the predicted forms. It returns the permanent H-n id and the hypotheses already on the same shape.

## Example

```text
motif hypothesis add --project <fwdata> --file hypothesis.json
```

The file is JSON with snake_case names that match the hypothesis record: `statement`, `mechanism`, `change`, `neighbour` (required), and optionally `pattern`, `environment`, `host_class`, `slot`, `predictions`, `falsifier`, `exceptions`, `objects_added`, `attested_types_gained` and `questions` (each `{question, predicted}`). `change`, `environment`, `host_class` and `slot` form the shape the neighbour check looks up. The ledger lives in Motif's store and never writes the FieldWorks project.

## Related commands

- [Check the neighbours of a change](cmd:hypothesis%20neighbours) is the check that runs before any new object.
