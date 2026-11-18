# SIL.ListCrdt

An MIT list CRDT: Fugue position ordering with Kleppmann-style last-writer-wins
moves, stable string item identities, and permanent delete-wins tombstones.
The net10.0 library has no project or package dependencies. No package is published.

```csharp
var replica = new ListReplica("unique-session-id");
var first = replica.InsertAt(0, "item-a");
var second = replica.InsertAt(1, "item-b");
var move = replica.MoveToIndex("item-a", 1);
```

Replicas must have unique names and exclusive author ownership. Persist operations
before acknowledging edits. A new session may use a fresh replica name, or resume
its previous name only after loading every operation it previously authored.
`ListState.Apply`/`ListReplica.Receive` accept reordered and duplicate delivery.
Missing origins/items are buffered until their dependencies arrive. The library
is not thread safe; callers serialize access.

`Move` and `MoveToIndex` return null when the item already occupies the requested
live position. Broadcast only non-null operations. Indexes are zero-based; a move's
target is its final index after removing the item from its previous position.
Tombstoned item identities cannot be reused. Moves received for deleted items
retain their position nodes without reviving the item. Undo uses a fresh item ID.
A block move is a batch of individual moves with a shared timestamp, not an atomic
range move in the presence of concurrent edits. `MoveBlock` returns an empty array
when the block already occupies the requested contiguous live placement, even with
retained tombstones or abandoned position nodes between its items.

`OpJson` encodes schema-versioned operations. Counters and Lamport times use decimal
strings to preserve 64-bit integers. `CanonicalJson` supports the operation domain
only, not arbitrary RFC 8785 floating-point values. See NOTICE for paper citations.

Fugue's backward runs may interleave. Loro has a different concurrent move/delete
policy, and historical raw moves beside abandoned slots may order differently.
The test projects preserve both characterizations. Visible index lookup is linear;
this release does not promise sub-millisecond authoring on arbitrary long lists.
The pending buffer and operation history are retained without compaction. Only
validated, authenticated operation streams should be fed to this foundation.

## Row storage seam

`NodeRowEncoding.Encode(operation)` returns a `ListNodeRow(Version, Key, Json)`.
Scope `Key` by your store's list identity, such as `(owner ID, field ID, Key)`.
The key is the unambiguous JSON tuple `[replica, decimalCounter]`, unrelated to
visible order. Persist the returned row in the same transaction as its enclosing
commit. Resending the identical row is idempotent; reusing an ID with different
content is invalid. There is no whole-sequence JSON array to rewrite.

`state.ExportRows()` includes every accepted operation, including those still
waiting for missing dependencies. `NodeRowEncoding.Restore(rows)` accepts rows in
any order. Genesis has one row per item. A move adds one position row and retains
the old anchor; a delete adds one tombstone row. Losing moves remain available
through `state.LosingMoves` (all superseded moves, including sequential ones;
causal conflict-copy decisions belong to the enclosing store). This is a complete,
append-only encoding with retained history, not a compact checkpoint format.

Baseline positions from `ListState.FromInitial` use the reserved empty replica
name and consecutive counters at Lamport zero. All participants must use the
identical ordered baseline; choose a nonempty unique author replica name.
