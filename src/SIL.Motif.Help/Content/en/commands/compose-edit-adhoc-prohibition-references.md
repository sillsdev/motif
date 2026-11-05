# Retarget a prohibition

`compose-edit-adhoc-prohibition-references` stages new references for one flat allomorph or morpheme ad hoc prohibition. MCP offers `motif_edit_adhoc_prohibition_references`; it stages a Draft and leaves Apply to a person.

Supply `target`, `expected` and `references`. Both state objects require `kind` (`allomorph` or `morpheme`), `first` (a canonical id, or null), ordered `rest` ids, ordered legacy `allomorphs` ids (empty for morpheme prohibitions), integer `adjacency`, and Boolean `disabled`. Adjacency uses FieldWorks' stored values: 0 anywhere, 1 somewhere to right, 2 somewhere to left, 3 adjacent to right, 4 adjacent to left. Every expected value must match the project as the Draft's earlier changes leave it.

In FieldWorks, `first` is **Key Allomorph** or **Key Morpheme**, and `rest` is **Other Allomorph(s)** or **Other Morpheme(s)**. `disabled` is the inverse of **Active**: `disabled: true` means Active is off, and `disabled: false` means Active is on.

The replacement keeps kind, adjacency, Disabled and the number and order of occurrences. Each new allomorph belongs to the same entry as the allomorph it replaces; each new morpheme is a grammatical analysis of the same entry as its predecessor. Unknown objects, wrong classes and cross-entry destinations refuse. Enabled prohibitions can be retargeted; a Trial measures the changed restriction.

Removing a morpheme reference can make LibLCM delete an unused grammatical analysis and clear its stored word-analysis references, as FieldWorks does. The Dry Run lists every such deletion for review before Apply; changed cleanup effects make Apply refuse as stale.

A process allomorph can point to a plain alternate authored earlier in the same Draft. Stage these edits before [deleting the process](cmd:compose-delete-affix-process). To remove the restriction instead, [delete the prohibition](cmd:compose-delete-adhoc-prohibition).
