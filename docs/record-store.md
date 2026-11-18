# Generic record store

`SIL.Motif.RecordStore` stores the complete FieldWorks object graph on the stock
`SIL.Harmony` package. `SIL.Motif.RecordStore.Lcm` adapts caller-owned LCM caches to
that graph and restores fresh `.fwdata` projects through the pinned LCM serializer.
It sits beneath the shared command layer; it does not introduce a second command API.

Each LCM object keeps its FieldWorks GUID, concrete class, owner and owning field.
Scalar fields, multilingual alternatives and list members are separate changes.
Import commits have at most 500 changes, including a create that crosses commits.
Lists currently use integer positions behind `IRecordList`. They do not provide
concurrent list editing semantics. Commit times are UTC, floored to whole microseconds
before Harmony compares them and advances its logical counter.

Writing systems are independent records with stable identity derived from their tag,
complete LDML bytes (including unrecognized content), and a binding to the LDML
collations. Project settings and linked resources have separate records carrying their SHA-256.
Their bytes live outside the change log in `<store path>.resources/`; export verifies
every content hash. Copy that folder along with the SQLite store. Backups,
locks, caches and Motif's workflow database are excluded. Export validates relative
resource paths and requires a fresh output directory.

The library exposes import, detached reads, replay and export. It exposes no delete
operation: entry deletion needs the incoming-reference index and an explicit usage
warning transaction before any links can change. This library does not authorize
writing into a project shared with stock FW Lite.

## Round-trip gate

The test project always creates a disposable LCM project with the shared generic
fixture builders. The gate compares all objects and fields, ownership, list order,
rich-text runs and properties, raw UTF-16, schema and project-resource bytes after
store close/reopen, export and a fresh LCM load. The only field exemption is the
project-relative `LinkedFilesRootDir` relocation. No broad field or class exemptions
are allowed.

Private project gates never download or commit owner data. Set either environment
variable to an external **copy** of a `.fwdata` project; tests copy it again into their
private temporary root before loading it:

```powershell
$env:MOTIF_RECORD_STORE_INDONESIAN = '/private/copies/indonesian/indonesian.fwdata'
pwsh -NoProfile -File ./test.ps1 -Project SIL.Motif.Tests.RecordStore
```

`MOTIF_RECORD_STORE_CEBUANO` enables the larger manual/nightly gate in the same suite.
An unset variable is reported as a named skip. A configured missing or invalid input
fails. To require Indonesian on a validation runner, use
`tools/Test-RecordStoreRoundTrip.ps1 -IndonesianCopy <path>`; it refuses absent inputs
before invoking the suite. Public CI runs the generated-project gate; private-data
CI must provision these copies externally.

Import deduplication, the incoming-reference index, static versioned built-in layout
tables, portable custom-field remapping and edit-size/performance budgets have
separate milestone slice exits. The current format is an internal development
contract and must not be used for a shared-project migration.

For an existing writing system whose LDML is already on disk, save its definition
before calling `ImportAsync`. Unsaved edits are refused, so graph fields and LDML
cannot silently describe different project states. New writing-system definitions
without a disk file are serialized from the supplied cache.

Replacing positional lists with M2 also requires replacing the replay implementation
used by `InsertRecordItem`, as well as the `RecordStore.Lists` reader.
