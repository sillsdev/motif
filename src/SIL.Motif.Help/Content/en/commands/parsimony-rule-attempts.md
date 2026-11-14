# Check a batch for same-shape additions

`parsimony --rule-attempts --plan <intents.json>` reads the intents file you would give `compose-batch` and groups the variants it adds by shape. A shape is the variant type, the environments and the spelling change from the main form, such as a lost final vowel. Five or more variants of one shape need a recorded rule attempt: the rule you tried, its prediction test, and why it failed. Without one, the check reports the group.

## Example

```powershell
motif parsimony --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --rule-attempts --plan ".\variants.json" --json
```

The answer lists each group of five or more with its shape, its size and whether an attempt is on record. It reads the intents and the main entries' forms from the current Baseline. It runs no parse and changes nothing.

The check is advice. It holds a package only when the project opts in, in `<project>.motif.toml`:

```toml
[parsimony]
block-on = ["P-variant-same-shape"]
```

A hold clears when the hypothesis ledger holds a passed or failed hypothesis on the shape: record it with `hypothesis add --change "<shape key from the report>"` (or the correspondence, such as `V>@final`), test it, and decide it.

The same finding appears in the Trial review summary. A hold there is a report line; Apply does not read it.

## Related commands

- [Review grammar parsimony](cmd:parsimony) queues a single measure without waiting.
- [Run every Parsimony measure](cmd:parsimony%20--measure%20all%20--wait) includes the variant-shape count.
