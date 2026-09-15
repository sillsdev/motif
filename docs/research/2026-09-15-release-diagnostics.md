# Release diagnostics

Motif now tells a caller when a project store is genuinely contended and when storage access failed for
another reason. That keeps retryable locks separate from output, permission, and disk failures while
preserving the existing schema and invalid-data decisions.

## Evidence

`MotifSqliteStore` acquires `<database>.owner.lock` only while creating a fresh database. The lock is an
operating-system file lock; opening or configuring the database can fail with an ordinary `IOException`
for unrelated reasons. The command wrapper also invokes its action after opening the store, so action
output failures arrive through the same exception type.

## Bounded design

- The lock source raises `MotifStoreLockException` only for the operating system's sharing or lock
  violation codes (32 or 33) while opening or locking the ownership file. The existing exclusive open
  and delete-on-close protocol stays intact; permission and path failures remain ordinary I/O errors.
- `ProjectStoreCommand` maps that typed exception to `project.busy`/`Busy` (exit code 3).
- A plain I/O error while opening the store maps to `project.store-io`/`Refused`; a plain I/O error from
  the action maps to `project.operation-io`/`Refused`. Both retain the operating-system message and the
  `fwDataPath` fact for recovery.
- `NotSupportedException` and `InvalidDataException` retain their existing `store.unsupported` and
  `store.inconsistent` outcomes. No message text is inspected to classify locks.

## Limits

Unexpected non-I/O exceptions still escape this command boundary for the CLI's outer failure handling.
The CLI's JSON rendering of those failures is a separate surface concern.
