# Initialize a project

Prepare a FieldWorks project to hold human judgments about Parsimony recommendations. Motif creates one reserved Notebook field and saves it in the project after you confirm.

```text
motif project initialize --project <fwdata> --confirm "Initialize this project to work with Motif Proposals?" [--json]
```

This command is available when Advanced AI mode is on. The confirmation text must match exactly. Initialization does not create a judgment, a Notebook record, a Proposal, or a writing system.

The project must have an analysis writing system. If Motif finds an incompatible or duplicate definition with the reserved name, resolve it deliberately in FieldWorks and run initialization again. Motif does not rename or repair an existing definition.

Motif keeps a byte-checked recovery copy of the original project before it saves. Motif cannot undo a save, so the project is recoverable from that copy. If Motif cannot establish whether the save completed, the refusal names the recovery copy and gives the restore step: Close FieldWorks, then copy the recovery copy over the project file. Motif never restores the project by itself. A successful run that keeps a copy names it but gives no restore step, because restoring would undo the initialization. Inspect the saved project before you restore or retry.
