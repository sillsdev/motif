# Back up a Motif store

`project backup` writes a consistent copy of a project's Motif store, and the files Motif owns for the project, into a new folder. Use it before switching to a new Motif build or doing anything else you may want to undo.

```powershell
motif project backup --project "D:\Projects\Koro\Koro.fwdata"
motif project backup --project "D:\Projects\Koro\Koro.fwdata" --to "D:\Backups\koro-before-switch" --json
```

Without `--to` the backup goes into a timestamped folder beside the store, named `<project>.motif-backup-<time>`. A folder named with `--to` must be missing or empty.

The store is copied with SQLite's online backup, so the copy is one consistent snapshot even while a runner or another command is reading or writing the store. The command takes the same shared lease every reader takes, so it refuses only when another Motif process is resetting or restoring the store (`project.busy`). If the store stays locked past the busy timeout it refuses with `cli.store-busy`; repeat the call.

The folder holds `store.sqlite`, plus `Config.motif.toml` (the project's `.motif.toml`), `store.upgrade.json` and the `trial-exports` folder when they exist, and `motif-backup.json`, the receipt. The receipt, which is also the command's JSON, lists each file's path, size and sha256, the store's schema version and the project it belongs to.

The FieldWorks project file is not included: `fwDataIncluded` is `false`. Back that up with FieldWorks' own project backup.

A project with no Motif store refuses with `project.backup-no-store`; a destination that already holds files refuses with `project.backup-destination-exists`. Restore a backup with [`project restore`](cmd:project%20restore).
