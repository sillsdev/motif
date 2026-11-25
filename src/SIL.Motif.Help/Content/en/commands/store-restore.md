# Restore a Motif store

`store restore` puts a backup made by [`store backup`](cmd:store%20backup) back beside the project.

```powershell
motif store restore "D:\Backups\koro-before-switch" --project "D:\Projects\Koro\Koro.fwdata"
```

Pass the backup folder (or its `motif-backup.json`). Before it changes anything the command refuses when:

- the store is open or busy in another Motif process (`project.busy`); close the runner with `motif worker stop` and repeat;
- the backup's store schema is newer than this build understands (`store.restore-newer-schema`); update Motif first;
- a file's sha256 or size differs from the receipt (`store.restore-checksum-mismatch`), checked on the copy staged beside the target;
- the folder has no readable receipt, the receipt names a file outside the backup, lists a file twice or lists a file the folder lacks (`store.restore-invalid`);
- the backup is of another project (`store.restore-other-project`). The receipt records the project's LibLCM project GUID and its identity; a restore matches the GUID, or the identity when either file has no GUID. The path in the receipt is only information, so a project that was moved or renamed restores fine. Pass `--force` to restore a backup of a different project anyway.

Otherwise it takes the store's exclusive lease, stages every file beside its target, and folds the old store's write-ahead log into its main file so no committed transaction lives only in a sidecar (if that cannot finish, the restore refuses with `project.store-io` and changes nothing). Nothing is deleted. The old store, with its `-wal`, `-shm` and `-journal` files, and every other file it replaces or the backup lacks (the project's `.motif.toml`, the upgrade report, the `trial-exports` files) are moved into `<project>.motif-backups/pre-restore-<time>/moved`, and the staged files are renamed into place. That move is the only copy of the old store (its raw files reproduce it exactly), so nothing is stored twice, and a store too damaged to read is moved aside the same way, so a good backup can still replace it. The result lists each restored file, each file moved aside with its reason (`replaced` or `not-in-backup`), and the pre-restore folder.

A restore that fails while the process lives puts everything back and names the pre-restore folder. If the process dies partway, the project is in one of these states, and running the same restore again finishes it, into the same pre-restore folder (marked `restore-incomplete` until it finishes), and removes the leftover `*.restore-*` staged files:

1. Before anything moved: the old project is untouched.
2. Other owned files swapped, the old store not yet moved: the old store beside the new config and trial files.
3. The `-wal`, `-shm` and `-journal` files moved first, the old store not yet: the old store is live and complete, since its WAL was folded in first; no orphan `-wal` is left at the path.
4. The old store moved too, the new store not yet renamed: there is no store and no sidecar at the path, the complete old files are in `moved`, and the complete new store is staged. Motif sees no store, never a half one.
5. The new store renamed: done.

An older backup is restored as it is; Motif upgrades it the next time it opens it. The FieldWorks project file is never touched.
