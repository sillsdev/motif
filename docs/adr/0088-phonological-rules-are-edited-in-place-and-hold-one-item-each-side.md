# 0088. Phonological rules are edited in place and hold sequences of up to eight items

A regular rewrite rule can be edited, reordered, switched off and deleted without recreating it. Its Input and
Output each hold up to eight ordered phoneme or natural-class items, and their lengths may differ. An empty Input
means insertion; an empty Output means deletion. Contexts may hold optional or repeated items and word or morpheme
boundaries.

**Status:** accepted; amended 2026-11-05 for sequence rules and safe read-back. The ADR number and filename are
retained so existing citations continue to resolve; the title and decision below state the amended behavior.

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
4. **Sequences of up to eight items each side.** Input and Output each accept zero to eight phoneme or natural-class
   items in order. The lengths may differ, including two items becoming one and one item becoming two. An output
   natural class produces any member of the class; it does not preserve whichever segment matched the input.
   FieldWorks' HermitCrab and PanGloss support these shapes, as the amendment evidence below records.
5. **Merge a duplicate boundary marker.** `compose-merge-boundary-markers` points every boundary context that uses a
   stray second `#` or `+` marker at the standard marker and deletes the copy, which clears the refusal that blocked
   rules with a boundary on a project that held two `+` markers. It refuses a marker used by anything but a boundary
   context.
6. **Representable edit intents only.** A detailed rule read returns its actual right-hand-side count and an intent
   suitable for an edit guard only when the rule has exactly one right-hand side and every context is representable.
   A natural-class context with positive or negative alpha constraints, including one inside an iteration or
   sequence, yields no editable intent. The edit guard refuses before constructing operations, preserving those
   references. Rules with multiple right-hand sides are read with `intent: null` and refused for editing until a
   later slice adds subrule authoring and guards; no right-hand side is silently selected or discarded.

## Consequences

- A rule chain (class to nasal, archiphoneme deleted before a nasal, archiphoneme to the velar nasal elsewhere) is
  authored in any order and then reordered. Segment natural classes also work in featureless grammars with
  PanGloss 0.15.2.
- Rule features and inflection classes still go through `compose-edit-phonological-rule-features`.

## Amendment, 2026-11-05: sequence rules and safe read-back

The director's `solb2b-hc-gate.md`, retained with the harness-resume evidence, replaces the original one-item
ceiling and its reading of Black (2025, footnote 66). Procedurally built LibLCM projects were exported through
FieldWorks GenerateHCConfig and parsed by hc.dll, PanGloss 0.15.2 on the export, and PanGloss on the saved project.
The engines agreed on 1-to-1, 2-to-2, 2-to-1 and 1-to-2 rules, with positive parses and rejected controls. The
extended gate also proves natural classes in sequences, left and right environments, all three directions and
multiple right-hand sides. HCLoader loops over every Input, Output and right-hand-side item without a one-item cap.

The eight-item limit is the Motif authoring bound, matching contexts; it is not a FieldWorks loader ceiling.
Multiple right-hand sides are proven in FieldWorks and PanGloss but need a separate subrule intent and edit-guard
design. Alpha authoring likewise remains outside this intent: read-back must refuse a lossy representation rather
than offer it for a guarded edit.
