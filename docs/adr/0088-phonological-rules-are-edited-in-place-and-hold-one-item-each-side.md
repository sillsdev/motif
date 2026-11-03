# 0088. Phonological rules are edited in place, and hold one item on each side

A regular rewrite rule can now be edited, reordered, switched off and deleted without recreating it, and its
contexts may hold optional or repeated items and word or morpheme boundaries. A rule still changes one item into one
item, or deletes or inserts one.

**Status:** accepted.

## Decision

1. **Edit in place.** `compose-edit-phonological-rule` replaces only the named parts (name, direction, input,
   output, left, right). The rule keeps its identity, place in the order and features. An `expected` guard names the
   current value of each part being changed; a mismatch stages nothing. Contexts are replaced by deleting the old
   ones and creating the new ones in the same Proposal.
2. **Order, disable, delete.** `compose-order-phonological-rules` plans the fewest moves with
   `PhonologicalRuleOrderModel.PlanMoves`, from one move or a whole order. `compose-set-phonological-rule-disabled`
   writes the rule's Disabled flag (a disabled rule is not loaded by the parser). `compose-delete-phonological-rule`
   deletes a rule and the contexts only it uses, after checking its name.
3. **Optional and repeated context items.** A context item may be `optional`, or carry `minimum` and `maximum`
   (`-1` is unbounded). It becomes a LibLCM iteration context around the item, with bounds written by
   `grammar/phIterationContext/setBounds`. A boundary cannot repeat. Allomorph environments do not take these
   properties.
4. **One item each side.** Input and output stay at zero or one item. Black (2025, footnote 66) says more "will not do
   what you hope"; two-into-one changes are allomorphs. Multi-item rules are not built.
5. **Merge a duplicate boundary marker.** `compose-merge-boundary-markers` points every boundary context that uses a
   stray second `#` or `+` marker at the standard marker and deletes the copy, which clears the refusal that blocked
   rules with a boundary on a project that held two `+` markers. It refuses a marker used by anything but a boundary
   context.

## Consequences

- A rule chain (class to nasal, archiphoneme deleted before a nasal, archiphoneme to the velar nasal elsewhere) is
  authored in any order and then reordered. PanGloss ignores natural classes in a rewrite rule of a project with no
  phonological features (sillsdev/pangloss#120), so the chain's proof uses a featured grammar.
- Rule features and inflection classes still go through `compose-edit-phonological-rule-features`.
