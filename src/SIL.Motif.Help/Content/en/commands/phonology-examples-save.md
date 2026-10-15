# Save phonology examples

`phonology-examples save` records underlying forms, expected surface forms and the grammar context for each example as an immutable revision, with an optional citation on each. A saved set is advisory evidence: it never changes Readiness and Apply ignores it. Saving does not change the FieldWorks project.

## When to use it

Use this to keep a set of underlying forms and expected surface forms, with their sources, next to the project. To revise a set, save it again with its exact current revision id as `expectedRevisionId`.

## Example

Create a UTF-8 JSON file with a `name` and an `examples` array. Each example has an `id`, `underlying`, one or more `expected` forms, and a `context` tagged with `kind`. An optional `citation` has a `source` and `locator`.

```json
{
  "name": "Vowel harmony",
  "examples": [
    {
      "id": "front-vowel",
      "underlying": "a-ku",
      "expected": ["e-ki"],
      "context": {"kind": "hcXmlEntry", "entryId": "entry-17"},
      "citation": {"source": "Field notes", "locator": "example 12"}
    }
  ]
}
```

Then save it:

```powershell
motif phonology-examples save --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --file examples.json --json
```

## What it prints

The response names the saved set and revision, including its executable and cited revision digests. A citation-only edit creates a new cited revision while leaving the executable digest unchanged.

## Related commands

- [List saved phonology examples](cmd:phonology-examples%20list) shows each set's current revision.
- [Show a saved phonology revision](cmd:phonology-examples%20show) reads its examples and citations.
