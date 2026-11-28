# Link a hypothesis

`hypothesis link` links a hypothesis to the Proposals that carry its rule change and to the Guide section it changed, or states why no Guide section changed.

## Example

```text
motif hypothesis link H-12 --project <fwdata> --proposal <proposalId> --guide GDR-8 --guide-section Examples
```

`--proposal` takes one or more Proposal ids separated by commas. `--guide` and `--guide-section` go together; `--guide-none` gives the reason no Guide section changed and excludes them. A Proposal is not looked up, because Proposals can be archived.

## Related commands

- [Check a change's neighbours](cmd:hypothesis%20neighbours) is the check that runs before any new object.
