# 0088. Phonological rules are edited in place and hold sequences of up to eight items

A regular rewrite rule can be edited, reordered, switched off and deleted without recreating it. Its Input and
Output each hold up to eight ordered phoneme or natural-class items, and their lengths may differ. An empty Input
means insertion; an empty Output means deletion. Contexts may hold optional or repeated items and word or morpheme
boundaries.

**Status:** accepted; amended 2026-11-05 for sequence rules, feature changes, alpha read-back and ordered right-hand sides. The ADR number and filename are
retained so existing citations continue to resolve; the title and decision below state the amended behavior.

## Decision

1. **Edit in place.** `compose-edit-phonological-rule` replaces only the named parts (name, direction, input,
   output, left, right, rightHandSides). The rule keeps its identity and place in the order; individual-part edits
   preserve applicability, while a guarded outcome-sequence replacement supplies each case's applicability explicitly. An `expected` guard names the
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
   items in order. The lengths may differ, including two items becoming one and one item becoming two. An Output
   segment-list natural class produces any member. An aligned feature-class Output changes the specified features
   on the matched segment and preserves the rest.
   FieldWorks' HermitCrab and PanGloss support these shapes, as the amendment evidence below records.
5. **Merge a duplicate boundary marker.** `compose-merge-boundary-markers` points every boundary context that uses a
   stray second `#` or `+` marker at the standard marker and deletes the copy, which clears the refusal that blocked
   rules with a boundary on a project that held two `+` markers. It refuses a marker used by anything but a boundary
   context.
6. **Representable edit intents only.** A detailed rule read returns its actual right-hand-side count and an intent
   suitable for an edit guard when every right-hand side and context is representable.
   Positive and negative alpha references, including those inside sequences and iterations, read back with their
   shared constraint identities. A malformed constraint or unrepresentable context yields no editable intent.
   The edit guard refuses before constructing operations when the expected parts differ. Every right-hand side
   is retained in order, including its Output, contexts, required/excluded rule features and input categories.

## Consequences

- A rule chain (class to nasal, archiphoneme deleted before a nasal, archiphoneme to the velar nasal elsewhere) is
  authored in any order and then reordered. Segment natural classes also work in featureless grammars with
  PanGloss 0.15.2.
- Rule features and inflection classes belong to each right-hand side. The single-outcome feature command remains a shorthand; complete outcome-sequence edits supply the sets per case.

## Amendment, 2026-11-05: sequence rules and safe read-back

The director's `solb2b-hc-gate.md`, retained with the harness-resume evidence, replaces the original one-item
ceiling and its reading of Black (2025, footnote 66). Procedurally built LibLCM projects were exported through
FieldWorks GenerateHCConfig and parsed by hc.dll, PanGloss 0.15.2 on the export, and PanGloss on the saved project.
The engines agreed on 1-to-1, 2-to-2, 2-to-1 and 1-to-2 rules, with positive parses and rejected controls. The
extended gate also proves natural classes in sequences, left and right environments, all three directions and
multiple right-hand sides. HCLoader loops over every Input, Output and right-hand-side item without a one-item cap.

The eight-item limit is the Motif authoring bound, matching contexts; it is not a FieldWorks loader ceiling.
Multiple right-hand sides are proven in FieldWorks and PanGloss with separate environments. Their authoring and
edit guards follow the further amendment below, as do feature changes and alpha read-back.

## Amendment, 2026-11-05: feature changes and alpha variables

7. **A feature change preserves the rest of a segment.** An Output `change` item contains closed feature/value
   pairs, optionally with alpha references. It lowers to a `PhSimpleContextNC` referring to a `PhNCFeatures` partial
   feature structure, as FieldWorks builds it. A change requires equal Input and Output lengths so there is an
   aligned matched segment. Read-back retains the Output's `naturalClass` identity and alpha references rather
   than inlining the class's feature values, so a round-trip edit preserves sharing with other users of the class.
8. **Alpha is rule-scoped.** A `variables` reference contains `variable`, `feature` and `polarity` (`+` by default,
   or `-` for disagreement). It attaches to a feature natural class in Input or contexts, or to an Output change.
   One variable creates one `PhFeatureConstraint` in the phonological data's unordered pool; contexts share it
   through `PlusConstrRS` and `MinusConstrRS`. The feature must be declared, closed and valued in the project's phonological feature system;
   occurrence on phonemes is not required. Single references, all-negative references and Output-only references
   are allowed: HCLoader loads each polarity independently without imposing a binding-site condition. Conflicting features,
   duplicate or contradictory polarity on one item, and fixed and variable values of the same feature on one item
   are refused with the variable or feature named.
9. **Stored identities preserve sharing through partial edits.** LibLCM stores no authored variable label.
   Read-back therefore uses each constraint's canonical identity as its variable name. Replacement parts reuse
   those identifiers to share with untouched parts; new local labels create new constraints. When every referrer
   is in replaced Input, the constraint is recreated because LibLCM collects it with the last old Input reference.
   Sharing and polarity survive, while the recreated constraint has a new stored identity. Other context deletions
   depend on Input deletions, preserving constraints retained by Output or environments. The final combined
   intent is validated before any operation identities are minted.
   Nested iterations and malformed alpha constraints still refuse editable read-back.
10. **Refuse variables the pinned FieldWorks loader cannot discover.** LibLCM's `PhRegularRule.CollectVars`
   visits simple natural-class members of a sequence but omits iteration members. HCLoader builds its variable
   dictionary exclusively from `rule.FeatureConstraints`, then indexes it for each context reference. Authoring
   and edits therefore refuse a variable whose references are all inside optional or repeated items of multi-item
   contexts, with that loader limitation named. A reference in Input, Output, a non-repeated sequence item or a
   single-item context makes it discoverable and remains allowed. Read-back retains the stored references so an
   edit can repair the shape. FieldWorks' editor can store the omitted shape; it does not register its variables
   elsewhere, and its loader cannot load it. This is a pinned LibLCM/HCLoader limitation, not a linguistic policy.

The director's `solb3-hc-gate.md` records FieldWorks GenerateHCConfig and hc.dll evidence: partial feature changes
preserve place, alpha copies place across a morpheme boundary, and binary MinusConstr expresses dissimilation.
PanGloss 0.15.2's default engine agrees on consonant-initial stems. Its unspecified-feature environment difference
is tracked as PanGloss #180 and is not compensated for in Motif. Alpha on Input loads through FieldWorks and runs
on PanGloss, but hc.dll parsing parity is unproven because the oracle hangs. Metathesis remains outside this intent.


The loader policy follows the local FieldWorks source: `PhonologicalFeatureChooserDlg.UpdateFeatureConstraints`
creates either polarity independently, `HCLoader.LoadRewriteRule` builds variables from `FeatureConstraints`,
`GetVariables` loads either polarity without reference-count or binding-site checks, and
`LoadFeatureSystem` loads all declared closed features independently of phoneme usage.
`RegRuleFormulaControl.SetContextOccurrence` and `RuleFormulaControl.CurrentContext` allow variables on repeated
items, but LibLCM's sequence collector omits them; no editor-only registration repairs that omission.

## Amendment, 2026-11-05: ordered right-hand sides and lexical alternatives

11. **Complete per-case intent.** `rightHandSides` contains one or more ordered objects with `output`, `left`,
    `right`, `requiredFeatures`, `excludedFeatures` and optional `inputCategories`. Input, direction and name stay
    rule-wide. Each case requires Output; missing contexts or applicability sets mean empty. Feature ids refer to
    the same exception features or inflection classes as the existing feature composer. Input categories preserve
    FieldWorks' InputPOSes restrictions, including category descendants. The nonempty sequence has no case-count
    ceiling in authoring, read-back or replacement.
12. **Expected-current sequence replacement.** An edit replaces the complete sequence to add, remove or reorder
    cases, guarded by `expected.rightHandSides`. The guard checks every case in order and its applicability sets
    by identity. The deletion payload retains the complete RHS/context graph digest, and Apply binds that
    graph in its pre-mutation footprint so later content drift refuses. A complete unrestricted single-outcome
    `output`, `left`, `right` expectation can guard conversion to the sequence form. The rule keeps its identity, order and disabled flag; its RHS objects are recreated
    in one Proposal. Alpha scope spans the whole rule; one lowering shares constraint identities across outcomes
    and preserves stored constraints, recreating those collected when their last referrers are replaced Input.
    RHS-only constraints remain reusable; after replacement, old constraints with no remaining referrers are
    collected. Constraints still used by this rule, another rule or a process are kept.
13. **Preserve FieldWorks priority; alternatives come from alternate forms.** Multiple right-hand sides are ordered
    cases, not free variation. FieldWorks HCLoader loads them in order; Machine's `RewriteRuleSpec.MatchSubrule`
    returns on the first applicable environment. The default PanGloss 0.15.2 engine likewise gives ara, not ala,
    from ada with overlapping d → r then d → l cases. Deleting the first enables the second. Two overlapping
    cases therefore cannot provide the proposed optional-outcome proof. An Output segment-list class {r,l}
    is applied as the union of its members' features, leaving one underspecified segment, and yields only r.
    Free variation is authored as alternate forms of the stem on the entry. This union behaviour holds for rule
    Output only; an affix-process insertion of a multi-member class still yields either member
    (`RealParserAffixProcessOutputKindTests`). No optional flag or dialect/register
    abstraction is invented. The requested overlapping-RHS semantics require a separate engine/model decision.
14. **Respect the loader's variable-name bound.** FieldWorks HCLoader names alpha constraints with a fixed
    24-element Greek-name table. The combined Input and all outcomes therefore admit at most 24 distinct
    variables. Validation refuses a larger set before minting operations; 24 is allowed. This is a loader bound,
    independent of the number of right-hand sides.
15. **Compatibility without ambiguity.** The existing `output`, `left`, `right` authoring shorthand still creates
    one case. It cannot be mixed with `rightHandSides`, and is ambiguous for editing a multi-case rule. Detailed
    reads keep that shorthand for an unrestricted single case; otherwise they return the complete sequence.
    Single-case feature edits remain available, while multiple-case features are edited through the guarded
    sequence. No outcome is silently selected or discarded.

## Related decision

[ADR 0090](0090-underlying-distinctions-survive-until-the-grammar-realizes-the-written-form.md) keeps represented underlying distinctions available to morphology and phonology until ordered rules realize the written form. Source spellings may remain in Citation Form; parsing Assessments use the actual written word and preserve ambiguity.
