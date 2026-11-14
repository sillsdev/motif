# Link a hypothesis to Proposals and a GDR

`hypothesis link` links a hypothesis to the Proposals that carry its rule change and to the GDR section it changed, or states why no GDR changed.

## Example

```text
motif hypothesis link H-12 --project <fwdata> --proposal <proposalId> --gdr GDR-8 --gdr-section Examples
```

`--proposal` takes one or more Proposal ids separated by commas. `--gdr` and `--gdr-section` go together; `--gdr-none` gives the reason no GDR changed and excludes them. A Proposal is not looked up, because Proposals can be archived.

## Related commands

- [Check the neighbours of a change](cmd:hypothesis%20neighbours) is the check that runs before any new object.
