# Restore a Motif store

`project restore` puts a backup made by [`project backup`](cmd:project%20backup) back beside the project.

```powershell
motif project restore "D:\Backups\koro-before-switch" --project "D:\Projects\Koro\Koro.fwdata"
```

Pass the backup folder (or its `motif-backup.json`). Before it changes anything the command refuses when:

- the store is open or busy in another Motif process (`project.busy`); close the runner with `motif worker stop` and repeat;
- the backup's store schema is newer than this build understands (`project.backup-newer-schema`); update Motif first;
- a file's sha256 or size differs from the receipt (`project.backup-checksum-mismatch`), checked on the copy staged beside the target;
- the folder has no readable receipt (`project.backup-invalid`), or the backup is of another project's store (`project.backup-other-project`).

Otherwise it takes the store's exclusive lease, saves the store it replaces as a backup in `<project>.motif-backups/pre-restore-<time>`, and swaps each staged file in by rename, so the store is never half written. An older backup is restored as it is; Motif upgrades it the next time it opens it. Files in the project's `trial-exports` folder that the backup does not hold are left in place, and the replaced store's copy has them. The result lists each restored file and the pre-restore backup's receipt.

The FieldWorks project file is never touched.
