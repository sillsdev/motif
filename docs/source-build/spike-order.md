# Spike: ADR 0104 ordered-grammar stale check and merge
Repo: ~/work/sb-spike-order (commit 96c50fe). Code: OrderSpike/OrderedList.cs.

Definitions
- Touched = removed + inserted + moved. Moved = common members outside an LCS of the two orders (minimal set to relocate). LCS ties break deterministically (earlier base member kept).
- IsStale: head removed a member I touched (both removing the same member converges, not stale); both inserted the same new GUID; or a touched member and any other member that survive in head have a different relative order than in base.
- Merge: head minus my touched members; then each touched surviving member goes in mine's order, right after the last member mine puts before it, and it must stay before every member mine puts after it. Else status Unplaceable. Same-spot inserts: mine first (not commutative).
- Statuses: Ok, Stale, Unplaceable, Invalid. Invalid = duplicate GUID in any list, or (with optional headMembers) a member missing from head's list or listed but absent.

Tests: 19 total, 19 pass (3 property tests x 1000 seeds each). They cover the owner's three examples, same-spot insert, concurrent removal of an untouched member, my removal plus their reorder (stale when they move the removed member, ok otherwise), duplicate in head, missing member, and the unplaceable case. In the not-stale property test, expected members and head order for untouched members hold, and ok > 100 of 1000.

ADR ambiguities
1. "Moved" is undefined. Swapping neighbours or moving a block has several minimal touched sets, and the stale verdict depends on the pick (see Contradicting_straddle test).
2. Not stale does not guarantee a placement exists. Mine puts 5 between 8 and 9, and head moves 8 after 9: no pair involving 5 changed, yet no position works. I return Unplaceable. The ADR should treat it as stale or define a fallback.
3. Inserts and moves have no base position. The ADR does not say whether placement is anchored to the predecessor or to all neighbours. I used all of mine's before/after constraints; predecessor-only gives wrong results (5 landed before 7 in example 1).
4. Same-spot concurrent inserts: no rule. Mine-first is arbitrary and order-dependent.
5. "Missing member" needs the member universe, which a list alone cannot supply, so Merge takes headMembers.
6. Both sides removing, or both inserting, the same member: unspecified. I made removal convergent and insertion stale.
7. Moving a member and head deleting an untouched neighbour: fine here, but it is not stated.

Command: cd ~/work/sb-spike-order && dotnet test

READY 96c50fe
