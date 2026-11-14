# Decide a hypothesis

`hypothesis decide` marks a hypothesis passed, failed or superseded with its decision. A pass needs a PASS test, a GDR link and no passed neighbour with another mechanism.

## Example

```text
motif hypothesis decide H-12 --project <fwdata> --status passed --decision "Rule stands as the default; 3 stems tagged."
```

`--status` is `passed`, `failed` or `superseded`, and `--decision` is required. A pass needs the latest test to be PASS and a GDR link or a stated reason for none (see [link](cmd:hypothesis%20link)). If another hypothesis already passed on the same shape with a different mechanism, the pass is refused unless `--supersedes` names it. A decided hypothesis changes only by being superseded.

## Related commands

- [Check the neighbours of a change](cmd:hypothesis%20neighbours) is the check that runs before any new object.
