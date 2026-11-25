# Back up a Motif store

`store backup` writes a consistent copy of a project's Motif store, and the files Motif owns for the project, into a new folder. It is the backup you take before switching Motif versions, or before anything else you may want to undo. It backs up only the Motif store: the project's `.fwdata` file is not included.

```powershell
motif store backup --project "D:\Projects\Koro\Koro.fwdata"
motif store backup --project "D:\Projects\Koro\Koro.fwdata" --to "D:\Backups\koro-before-switch" --json
```

Without `--to` the backup goes into a timestamped folder beside the store, named `<project>.motif-backup-<time>`. A folder named with `--to` must be missing or empty, and must not be the store file or lie inside a folder Motif owns: the store's `trial-exports` folder or `<project>.motif-backups` (`store.backup-destination-owned`), so a backup never contains a backup.

The store is copied with SQLite's online backup, so the copy is one consistent snapshot even while a runner or another command is reading or writing the store. The command takes the same shared lease every reader takes, so it refuses only when another Motif process is resetting or restoring the store (`project.busy`). If the store stays locked past the busy timeout it refuses with `cli.store-busy`; repeat the call.

The folder holds `store.sqlite`, plus `Config.motif.toml` (the project's `.motif.toml`), `store.upgrade.json` and the `trial-exports` folder when they exist, and `motif-backup.json`, the receipt. The receipt, which is also the command's JSON, lists each file's path, size and sha256, the store's schema version and the project it belongs to: its LibLCM project GUID (when the project file has one), its identity and its path.

The FieldWorks project file is not included: `fwDataIncluded` is `false`. Back that up with FieldWorks' own store backup.

A project with no Motif store refuses with `store.backup-no-store`; a destination that already holds files refuses with `store.backup-destination-exists`. Restore a backup with [`store restore`](cmd:store%20restore).
