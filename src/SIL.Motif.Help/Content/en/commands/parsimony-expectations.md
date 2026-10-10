# Read Parsimony expectations

```sh
motif parsimony expectations --project language.fwdata [--json]
```

Reads the saved project's `parsimony-expectations/v1` projection. It lists human-confirmed [reviewed negatives](term:reviewed-negative) by case and revision, then reports FieldWorks' default-human Approved and Disapproved readings in separate sections. Native Disapproved Opinions reject one exact reading and do not become whole-word negatives.

Conflicts and unavailable identities are included in `issues`; callers must not count them as qualified evidence. This command reads the FieldWorks project and does not run PanGloss or change it.
