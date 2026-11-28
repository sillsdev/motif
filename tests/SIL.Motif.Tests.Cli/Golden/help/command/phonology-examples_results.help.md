# Read phonology check results

`phonology-examples results` reads the stored result of one phonology check: a page of per-example summaries, or one complete example. A check is advisory evidence about a saved Example Set against a [Baseline](term:baseline); it never changes Readiness and Apply ignores it.

## When to use it

Use this after a check has finished to see, for each example, whether the expected surface form was `agree` (observed), `disagree` (a completed search without it) or `unavailable` (no usable measurement, such as a missing parser or lexical context). Unavailable is never a pass. The command reads stored rows only: it does not capture the project or run PanGloss.

## Example

```powershell
motif phonology-examples results --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" <assessmentId> --json
motif phonology-examples results --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" <assessmentId> --example front-vowel --json
```

## What it prints

A page lists each example with its disposition, completion, observed form and how many rules applied, in authored order. A response with a continuation cursor has another page; pass that cursor as JSON with `--continuation`. A summary lists at most 32 applied rule ids and says how many it left out. `--example <id>` prints the complete retained example with every ordered, visited and applied rule, and `--out <path.json>` also writes that JSON to a file. `--example` cannot be combined with `--page-size` or `--continuation`.

## Related commands

- [Show a saved phonology revision](cmd:phonology-examples%20show) reads the examples a check measured.
- [List saved phonology examples](cmd:phonology-examples%20list) finds each set's current revision.
