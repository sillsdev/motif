# Record a hypothesis test

`hypothesis test` records a rule-hypothesis-test result for a hypothesis from the JSON that control_test.py prints, with optional Trial numbers. A PASS needs 20 predicted forms and no attested loss.

## Example

```text
python3 scripts/control_test.py ... --json > control.json
motif hypothesis test H-12 --project <fwdata> --file control.json --command "control_test.py ..."
```

The file is the JSON `control_test.py --json` prints (`predicted`, `rule_hits`, `controls`, `margin`, `verdict`), optionally with `trial_attested_losses`, `trial_new_readings`, `trial_work_steps` and `command`. A PASS with fewer than 20 predicted forms, or with an attested Trial loss, is refused.

## Related commands

- [Check the neighbours of a change](cmd:hypothesis%20neighbours) is the check that runs before any new object.
