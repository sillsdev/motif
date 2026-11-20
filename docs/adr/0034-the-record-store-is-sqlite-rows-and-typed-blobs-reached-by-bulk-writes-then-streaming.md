# ADR 0034 — The record store is SQLite rows plus typed BLOBs, reached by bulk writes and then streaming

**Status:** Accepted, 2026-11-20.

## Context

The generic record store ([record-store.md](../record-store.md)) keeps the complete FieldWorks graph on stock
`SIL.Harmony`. It is already binary on disk, yet a Cebuano import (167 MB, 401,759 native objects) took 1,789.8 s
in the earlier measured run, and a cold reference acquisition took 135.3 s. The cost was not the file format. It was
the sequence of intermediate objects, changes, list prefixes, JSON values and verification passes built around it.

Where the time went, in order of weight:

- **Replay and persistence of the initial import.** Every list member was one `InsertRecordItem`; Harmony copies the
  preceding entity for each edit, and each append re-serialised the whole visible prefix. A 34,383-paragraph list
  alone is about 591 million member visits in one pass. Quadratic work, not SQLite, dominated.
- **Repeated value handling.** Codec prepass, canonicalisation, hashing and EF converters each touched the same value.
  Changes, snapshots, content rows and the LCM graph were resident together.
- **Verification and reads.** Open hashed every content row; acquisition decoded, copied and hashed every record and
  edge; each write copied the whole record dictionary and scanned history.
- **Export.** Reopen plus export took 545.5 s through the LCM bridge in both directions.

Harmony already ran the import in one transaction and Motif already prepared its edge inserts, so neither is a
remaining fix. The planning ranges are estimates, and the import-profile measurements replace them as they arrive.

The format must preserve:

- exact native round trip: GUIDs, class, owner and owning field, every supported field, references, sequence order
  and native traversal provenance, rich-text runs, raw UTF-16, dates, binary and empty/null distinctions, native
  human and parser evaluations, custom-field identities, and complete LDML and resource bytes;
- Harmony reconstructibility: typed payloads, commit and client IDs, hybrid timestamps, authored model versions,
  tombstones, list-operation causal state, and enough history for an empty, a late and an out-of-order peer;
- the M4 query and write contract: indexed reads, immutable pins, 500-change or 1 MiB transactions that refuse rather
  than split, and atomic request and audit records;
- the largest corpus's 6.3 million Occurrences living in the paired workflow store as rebuildable generations, not as
  Harmony entities ([ADR 0023](0023-one-store-beside-each-project-inside-a-byte-budget.md),
  [ADR 0024](0024-sources-are-read-in-place-and-scripture-stays-in-paratext.md)).

## Decision

Choose design C as the destination and reach it through design A's bulk-write and read-model foundations. Stream the
saved XML into typed records, emit complete-record seed changes that an empty Motif peer can replay, and publish
only when every chunk, coverage check and resource is complete. Keep the Harmony history reconstructible. Defer
design B's independent native baseline until measured sync scale requires it.

Store the Home as SQLite row tables plus versioned typed BLOBs. Do not adopt a new general-purpose columnar engine.
Concretely, retain commits, portable changes and immutable typed values, and add GUID-keyed current-head pointers,
versioned record payloads, ordered-list state, incoming-reference rows, query tables for the word-form views, and one
publication row binding their generation, schema and roots. Lists need membership and position access without
decoding unrelated fields; index both incoming targets and owner/field lookups.

Projections are disposable; native data and history are durable. Query tables, derived record versions and
checkpoints may be rebuilt or pruned under pin rules. The authored log, list tombstones and replica causal facts may
not be, without a separately proven acknowledgement and re-bootstrap protocol. The corpus's Occurrences have their own
workflow generations, published immutably, and never become generic records.

Wire changes stay JSON, with a newly registered model version; local values may stay binary. A reader that does not
know the version refuses.

## Consequences

Slice order. Each slice lands separately with its own gate.

1. Measure and remove the quadratic import work. Prove 1k, 10k and 35k list scaling and replay equivalence.
2. Typed values and bulk persistence: encode and hash each value once, build each list once, write by prepared
   batches, and stage resources before publishing their references.
3. Persisted queries, pins and certification, coordinated with the M4 query slices.
4. Portable seed changes, with empty-peer bootstrap, late and out-of-order sync, and model-version refusal.
5. Streaming import, qualified against independent LCM reads.
6. Streaming export, reopened through fresh LCM, with the slower bridge kept until the field matrix passes.
7. Authored-store migration and M4 integration, preserving commits and outcomes and refusing newer formats.
8. Scale and recovery qualification: Cebuano cold and warm, fresh-peer bootstrap, then 6.3 million Occurrences.

Slices 1 and 2 have landed on main: perf slices `f21199dc5` and `54f65705c`, with complete-record codec version 5,
Harmony model version 1, and indexes rebuilt in the same transaction. Measured import times:

- Slice 1: Cebuano 715.7 s to 122.8 s; Indonesian 42.7 s to 14.4 s.
- Slice 2: Cebuano 84.4 s to 57.8 s (re-measured baseline); Indonesian 13.0 s to 8.3 s.
- A separate measurement: the M1 slice 4c Cebuano wrapper store import took 90.9 s on a shared machine.

Slice 1 cut Cebuano peak RSS from 17.0 to 7.6 GiB. Slice 2 left it at about 8.2 to 8.6 GB, a level set by export, so
memory is the next bound on slices 5 and 8. The Cebuano import is a small fraction of the earlier 1,789.8 s run, but it
is not ten seconds. The streaming slices carry that target.

Certification guarantee. Every read answered from a published generation checks the immutable chunk hashes and root
bindings it consumes, and a stale or mismatched certificate refuses. Full verification stays available on import,
after a crash flag, or on explicit request. A constant-time certificate comparison does not prove the whole store:
unread pages are verified only by the full pass. The roots are unkeyed, so they detect accidental corruption and
divergence; they do not authenticate against hostile rewriting. The tamper tests state this rather than claim more. No
pin holds a long SQLite transaction; pins reference immutable generations that are reclaimed safely.

Do not, to win a benchmark or for any other reason:

- weaken durability: no unsynchronised publication, no skipped atomic swap;
- drop provenance or LDML, or delete the replay checks;
- write an empty commit that holds only final snapshots, which a new peer cannot replay;
- make the corpus's Occurrences generic records, or make M4 readers decode the Home;
- split an M4 hand-analysis transaction to fit the 500-change cap, or treat XML order as proof of LCM traversal order.

Fidelity, not SQLite, is the highest risk of the streaming slices. Mapping and traversal order are release gates for
slice 5. Migration preserves old payload readers and authored history; it never reimports over an edited store.

## Alternatives considered

**A alone: keep the log and bulk-load its initial materialisation.** Safest for sync, because the log is unchanged.
It removes the per-member folding and EF population, but still builds every granular change and keeps the LCM
extraction and JSON frontend. Forecast import was 80 to 250 s, never ten. Not chosen as the endpoint. It is kept as
the foundation, which is why slices 1 to 3 are A.

**B alone: an immutable native baseline as canonical, Harmony carrying later edits.** Removes initial log
amplification and shares pages across pins, but stock Harmony sends commits, not baseline blocks. A peer would need
chunk transfer, a manifest, atomic bootstrap, resume, version refusal and a causal frontier, and the Home would stop
being Harmony alone. LexBox and shared FW Lite compatibility are not established. The cost is protocol risk with no
measured sync scale to justify it. Deferred, not rejected; reopen it when that scale appears.

**A new columnar engine for the Home.** Columnar blocks help scans, not the point edits and random reads the Home
serves. Row and B-tree structures suit it. Columnar storage may still suit the corpus's Occurrences in the workflow
store, under their own decision.

See [record-store.md](../record-store.md) for the current codec, list and resource behaviour; update it as each
slice lands.
