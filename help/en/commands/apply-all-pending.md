# Apply pending changes

`motif apply --all-pending` applies all changes in the current pending Draft Proposal to the FieldWorks project in one unit of work. Motif records a Receipt when it writes changes.

FieldWorks releases the project before calling Motif. Reload the project after a successful response with `applied: true`; reload and check the project after `apply.reconciliation-needed`, because a write may have happened. A no-op does not need a reload, and an uncertain change stays pending for review.