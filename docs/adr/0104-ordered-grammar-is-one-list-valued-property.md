# 0104. Ordered grammar is one list-valued property per owner

**In plain terms:** For phonological rule order and similar sequences, the order *is* the meaning. Two people
reordering at once must not quietly produce an order neither of them chose; the clash must surface.

**Status:** proposed, 2026-11-10. Addresses the defect that [ADR 0013](0013-harmony-is-the-change-mechanism.md)
named against Harmony's last-writer-wins `SetOrderChange`.

## Decision

1. Each **Ordered grammar** sequence is stored as one property on its owner, holding the full ordered list of
   member GUIDs. Examples: a stratum's phonological rules, a template's slots, an affix process's input and output,
   and a rule's alpha variables. It is not stored as a `double Order` on each member.
2. A change sets the whole list, but **staleness is judged by relative order, not by the whole value** (owner, grill
   Q16, 2026-11-11). A Proposal records which members it moved, inserted or removed. When it is applied, a
   concurrent change to the list makes it stale only if it altered the relative order of a touched member and any
   other member. For example, with a Proposal that edits item 5:
   - item 7 moving to 9 leaves 5 before both, so it is not stale;
   - item 4 moving to 8 crosses 5, so it is stale.
   When it is not stale, the Proposal's moves are replayed onto the new list (a three-way merge of the
   permutations). An edit to another member's own fields never makes a list stale.
3. **Membership and order are checked by the Build** (ADR 0105):
   - A member that exists but is missing from its owner's list is a build error ("rule not in order").
   - A listed GUID that is deleted or missing is a build error.
   - A duplicate is a build error.
   For example, a concurrent insert of a rule and a reorder of the same stratum produces "rule not in order", not a silent drop.
4. **Unordered or merely displayed sequences** keep FW Lite's `double Order`: senses, examples, and ordering a
   linguist only sees in a list.

## Consequences

- Concurrent direct edits (outside Proposals) to one ordered list keep only the later one. This is acceptable
  because ordered grammar is edited through Proposals (open: is that a rule or a habit?).
- The 56 positional, 3 index-as-identity and 2 feeding fields in the manifest each need a classification:
  list-valued or order-valued. Index-as-identity (alpha variables) also needs its list frozen against a member's
  GUID, not its position.

## Alternatives

- **A list CRDT (RGA or similar) upstream in Harmony:** it keeps both inserts but still cannot know linguistic
  intent, so a feeding order would still need a test.
- **`double Order` with midpoint insertion, as FW Lite does today:** concurrent inserts get equal values and are
  interleaved by id. The result is valid but unintended, and nothing reports it.

## Amendment, 2026-11-12: definitions from the spike

The spike `sb-spike-order` (19 tests, including three 1000-seed property tests; [spike-order.md](../source-build/spike-order.md))
pinned down what decision 2 left open:

1. **Moved** means the common members outside a longest common subsequence of base and mine: the fewest members to
   relocate. Ties break deterministically.
2. **Not placeable is stale.** If head's changes leave no position that satisfies all of mine's before and after
   constraints for a touched member, the result is stale and goes to Update baseline. It is never placed by a guess.
3. Placement uses **all** of mine's before and after constraints, not only the predecessor.
4. When both sides insert at the same spot, **mine goes after head's.** A Proposal is replayed onto head, as in a rebase.
5. Both sides inserting the **same GUID** (possible with deterministic GUIDs) is stale unless both put it in the same
   relative position.
6. A duplicate GUID in any list is invalid input. A missing member is checked against the owner's member set, which the
   Build supplies, and is a build error (ADR 0105).
