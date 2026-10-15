# ADR 0068 — An id's identity is its GUID; its spelling is kept for documents

Motif treats two spellings of the same id as the same object everywhere: an id written with a prefix and the same id
written without one now name one entry, one rule or one operation. A Proposal can no longer pass a check by naming one object
two ways, and nobody has to remember to compare ids carefully in each new piece of code.

**Status:** accepted, 2026-10-15. Applies AGENTS.md design rule 8; leaves ADR 0007 decision 2 unchanged.

## Context

Design rule 8 says an id's prefix is optional and carries no meaning. `CanonicalId` nevertheless compared ids by their
full spelling, so every set, dictionary, `==` and LINQ operator treated `ph-X` and `X` as two objects, unless the author
remembered to use the `ByGuid` comparer.

This produced a run of defects. Five fixes landed between 7831a6ce6 and 13ad53c5c. At least three more were still open:
- a text create whose id appears in two spellings crashed in LibLCM;
- a text create bypassed the `dependsOn` rule;
- a natural-class edit reported false drift.

A semantic scan found about 150 production sites that use default equality, plus 77 records whose synthesized equality
carries a `CanonicalId`. None of them needs "different spelling means different object" from `Equals`. Spelling is
preserved through `Value` and `Prefix`, in the canonical document, the intent digest and stored rows.

## Decision

1. **Equality compares the identity.** `CanonicalId.Equals`, `GetHashCode`, `==` and `!=` compare the 128 decoded bits.
   This means two different suffix spellings that decode to one GUID are also equal. `SpellingEquals` compares the exact text,
   for the few tests and checks that need it. `ByGuid` remains, as an alias of default equality.
2. **Ordering is always named.** `CanonicalId` no longer implements `IComparable`. Code that sorts ids names
   `CanonicalId.SpellingOrder`, the UTF-8 ordinal on the full `Value` that ADR 0007 decision 2 requires for every digest
   and canonical serialization (`requires`, `dependsOn`, prerequisite plans and effect tuples). Because nothing sorts ids
   implicitly, no sort can quietly disagree with equality without being visible at its call site.
3. **No digest changes.** The digest's canonical-order rule is unchanged, and every conformance vector stays byte-identical.
4. **Proposal parsing follows identity.**
   - Duplicate operation ids, duplicate creators of one entity, and self-`requires` are refused across spellings.
   - A `dependsOn` or `requires` entry resolves to the operation or Proposal with the same identity, however it is spelled.
5. **The text channel is separate.** Code that keys or sorts by `Value` strings (stored-row lookups, `OrderBy(id.Value)`) is
   outside this type's reach. A later pass normalizes those keys to the canonical suffix, and a narrow scan in the build keeps
   new ones out.

## Consequences

- A whole class of defect stops being expressible in code that uses ids as values.
- Sets and dictionaries keep the first spelling inserted, so an emitted spelling can depend on input order. Composers that
  must emit the authored spelling keep doing so explicitly.
- Two existing tests that pinned "different spelling is a different id" become `SpellingEquals` tests.
- Spelling-preservation asserts move to `SpellingEquals`, so a normalization regression is still caught.

## Considered

- **A separate `ObjectKey` type, enforced by a scan.** Rejected. Whether `ids.Distinct()` operates on `CanonicalId` cannot be
  seen without type information. That forces a slow Roslyn-library tool, and it leaves about 150 sites and the
  record-equality channel unguarded.
- **A Roslyn analyzer.** Not available, because analyzers must target netstandard2.0, which this repository forbids.
- **Comparing by identity in both equality and order.** Rejected. ADR 0007 requires digest order to be ordinal on the full
  spelling, for cross-language determinism. An identity order was drafted for effect tuples and dropped: it would have
  changed the footprint digest for prefixed ids.
