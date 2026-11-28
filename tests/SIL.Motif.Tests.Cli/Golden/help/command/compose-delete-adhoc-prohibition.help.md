# Delete a prohibition

`compose-delete-adhoc-prohibition` stages deletion of one flat allomorph or morpheme ad hoc prohibition. MCP offers `motif_delete_adhoc_prohibition`; only a person can Apply.

Supply `target` and the complete `expected` state: `kind`, nullable `first`, ordered `rest` and legacy `allomorphs` arrays, integer `adjacency`, and Boolean `disabled`, using the same shape as [Retarget a prohibition](cmd:compose-edit-adhoc-prohibition-references). The expected state must match, including disabled or legacy occurrences. Unknown targets, groups and stale state refuse.

The Dry Run names the deleted prohibition and every grammatical analysis and stored word-analysis reference that native FieldWorks cleanup deletes. Referenced entries and forms are kept; Apply refuses as stale if the cleanup effects differ from the reviewed Dry Run. Stage the deletion before [deleting a process graph](cmd:compose-delete-affix-process) or `compose-delete-lexical-entry` that it currently refers to. A remaining native or custom incoming reference to the prohibition blocks deletion and is named.
