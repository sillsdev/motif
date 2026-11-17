# ADR 0015 — Phonology and allomorphy are authored as FieldWorks authors them

**Status:** Accepted, 2026-11-14. Retirement is an accepted authoring contract, not a claim that all
operation-family Apply capabilities or pinned engine proofs are complete.

## Context

Features, rules and listed allomorphs are related parts of one linguistic analysis. Motif must author the
FieldWorks objects, preserve their identities and human readings, expose actual engine limits and keep an
advisory finding separate from permission to erase a form or choose an analysis.

## Decisions

### D1. Phonological features use the ordinary LibLCM feature system

Create closed features and values in `LangProject.PhFeatureSystemOA` with
`compose-author-phonological-feature` and `compose-add-phonological-feature-value`, reusing generated
FsFeatureSystemFeatures/FsClosedFeatureValues operations. Require unique names/abbreviations within the system
and within each feature's values. Phoneme and feature-natural-class editors take `set` feature/value pairs and
`clear` features; update an existing specification in place and create a missing phoneme feature structure.
A feature class must keep at least one constraint. A segment class has no feature constraints and is directed
to `compose-edit-natural-class` instead.

An archiphoneme and a glottal stop are ordinary phonemes: no hidden flag or parallel inventory. The guide explains
orthographic-letter versus rule realization. `grammar.phoneme-never-used` is information only: it searches forms,
wordforms, rules, natural classes, environments and insertions. Its substring search can count a short spelling
inside a digraph and errs toward silence; it costs one lexicon/wordform pass. An inventory authored ahead of the
lexicon is legitimate. Help warns that introducing features changes how segment classes behave in rules; the
Composer does not prohibit the change. All Composers stage Draft operations; MCP never Applies.

### D2. Keep underlying distinctions until the grammar realizes spelling

Parser phonemes have nonempty representations in the loaded default-vernacular table, and parser forms remain
nonabstract even when their symbols are absent from running text. Quantity, stress, tone and segmental distinctions
that condition morphology/phonology remain distinct until explicit ordered realization or deletion rules neutralize
them. Aliases denote the same represented phoneme, never a pre-parser fold of linguistically different ones.
Rule order and effective stratum/domain are explicit. No separate orthography runtime or duplicate lexicon is added.

Assessments parse actual written words and retain every supported morphology. An unmarked underlying distinction
is neither a segmentation error nor authority to choose a sense, fold approved expectations or discard ambiguity.
Completion and unavailable identity remain separate. Claims about neutralized effects need producer-backed
derivational evidence at an explicit source/context/checkpoint, with completion facts; spelling alone proves none.
Restore lexical information only from selected evidence with exact identities and Citations, retaining unresolved
legacy forms. Citation Form may retain source spelling while parser forms carry the chosen underlying form.
Reference-safe migration uses Proposal, Dry Run and Trial. Health/examples advise, never substitute a preferred
linguistic analysis or silently become readiness gates.

Inverse deletion/neutralization can increase work and ambiguity; measure and bound it honestly rather than erase
evidenced distinctions. Stress features alone establish no syllable/mora capability. Richer unsupported domains
need upstream capability proof. The bounded unmarked-text proof uses simultaneous regular rules on Morphology,
empty Clitics/Surface, NotOnClitics=true, empty HC/Strata and HC/DelReapps=0 (the deletion-reapplication default,
not an inverse-deletion ban). It establishes no attached-clitic or multiple-cycle domain. PanGloss completes the
full chain where the hc.dll oracle exceeds 600 seconds on a retained control; XML probes and loader inspection
are not an observed combined FieldWorks export or Windows gate. Preserve those evidence limits rather than
changing mode, strata or the case list to claim parity. Source-supported listed forms remain a transitional bridge,
not a substitute for missing underlying conditioning. Language inventories and serialized payloads stay external.

### D3. Rewrite rules are edited in place with guarded sequence intent

`compose-edit-phonological-rule` replaces only named name/direction/Input/Output/left/right/rightHandSides parts.
Expected-current guards are checked before staging or minting operation ids. Keep rule identity, placement and
disabled flag; replace old contexts by delete/create in the same Proposal. Part edits preserve applicability;
complete outcome replacement supplies applicability per case. Detailed reads return actual RHS count and editable
intent only when all parts are representable; malformed contexts/constraints refuse instead of truncating.

Input and Output each contain zero to eight ordered phoneme/natural-class items, with different lengths allowed:
empty Input inserts, empty Output deletes. Eight is Motif's authoring bound, not HCLoader's ceiling. Contexts can
contain word/morpheme boundaries and optional/repeated items with minimum/maximum (-1 unbounded); boundary
repetition is refused. Iterations lower through `grammar/phIterationContext/setBounds`; these properties do not
apply to allomorph environments. Nested iterations still refuse editable read-back.

Ordering uses the fewest moves from `PhonologicalRuleOrderModel.PlanMoves`, accepting one move or a complete order.
The disabled flag prevents parser loading. Delete checks the name and removes contexts used only by that rule.
`compose-merge-boundary-markers` redirects boundary contexts from duplicate #/+ markers to the standard marker
and deletes the duplicate, refusing any other use.

### D4. Feature changes and alpha sharing follow the loader

Aligned Output `change` items carry closed feature/value pairs and optional variables, lowering to feature-class
partial structures that change only specified features and preserve the rest. Require equal Input/Output lengths
for alignment. Read-back retains Output natural-class identity rather than inlining values and losing sharing.

A rule-scoped variable has variable label, declared closed valued feature, and polarity (+ default or -).
Each creates one pooled PhFeatureConstraint shared by PlusConstrRS/MinusConstrRS. Feature use on phonemes is not
a prerequisite; single, all-negative and Output-only references are legal. Conflicting feature definitions,
duplicate/contradictory polarity and fixed-plus-variable values of one feature on an item refuse with its name.

Stored constraint ids become read-back variable names. Replacement parts reuse them to share with untouched
parts; new labels create constraints. Replacing all Input referrers recreates a constraint LibLCM collects, with a
new stored id but preserved sharing/polarity. Other deletions depend on Input deletion so surviving Output/context
references are preserved. Validate the combined intent first. Refuse variables referenced only inside repeated or
optional members of multi-item contexts: the pinned FieldWorks collector omits those iteration members. A reference
in Input, Output, an unrepeated sequence member or a single-item context makes the variable discoverable. Reads
retain stored references so an edit can repair them. At most 24 distinct variables across Input and all outcomes
fit HCLoader's Greek-name table; refuse more before minting operations.

Partial feature changes and dissimilation have FieldWorks/PanGloss evidence; the pinned PanGloss unspecified-feature
environment difference is not compensated for in Motif. Alpha Input loading is proved, but hc.dll parsing parity
is unproved where that oracle hangs. Metathesis is outside this rewrite Intent.

### D5. Right-hand sides are ordered cases, not free alternatives

`rightHandSides` is a nonempty, unbounded-in-count sequence of Output, left/right contexts, required/excluded rule
features and optional input categories. Missing contexts/applicability mean empty. Input, direction and name are
rule-wide. InputPOS restrictions include descendants. Feature ids name the existing exception features/inflection
classes. First applicable case wins, preserving FieldWorks and the pinned PanGloss semantics.

Adding/removing/reordering cases replaces the complete sequence under `expected.rightHandSides`, comparing order
and applicability identities. Deletion carries the full RHS/context graph digest, bound again before mutation so
content changes behind ids drift. An unrestricted complete single-outcome expectation may guard conversion.
Recreate RHS objects in one Proposal; alpha shares across all cases. Reuse surviving constraints, recreate collected
Input constraints and collect only old constraints with no remaining referrers, including respecting other rules
and processes. Never silently choose an outcome.

Single-case Output/left/right shorthand remains valid for authoring or an unrestricted single read, but cannot be
mixed with rightHandSides or used ambiguously to edit a multi-case rule. Single-case feature editing remains;
multi-case features use guarded full replacement. Rule Output segment classes choose no member: only shared
features apply and unspecified distinctions preserve the matched segment's values. Warn about this. Free variation
uses alternate stem forms. Affix-process insertion of a segment class can yield either member and has separate
proof; it does not establish optional rule Output. No dialect/register abstraction or optional-outcome flag is added.

### D6. Replacing listed allomorphs is one explicit Proposal

`ReplaceListedAllomorphsWithRuleIntent` combines typed class creation/reuse, complete rule authoring/placement and
`RetireAllomorphIntent` components in one Draft. Review links four groups over actual operation ids and scratch
effects: class/rule, bundle retargets retaining Opinions, other references, and alternate deletion. They are never
independently applied stages. Inputs use normalized model forms and exact ids, not display-hyphen inference,
unfinished rules, guessed destinations or a hidden property script. Reuse existing sound-system authoring and
output-only Mutation Plans, not a second execution engine.

Ordinary affix retirement covers same-entry simple prefix/suffix MoAffixAllomorph alternates. Stem is a distinct
declared capability below. Source must be AlternateForms; compatible surviving LexemeForm or alternate stays in the
same entry and is not retired. Refuse primary deletion/replacement/promotion, last loadable realization deletion,
processes, circumfixes, expansion-dependent variants, cross-entry moves, chains/cycles and identity collisions.
Broader capability needs complete conformance proofs before enabling Apply.

Role mappings are `(retiredForm, msa, explicit inflType-or-null, expansionRole) → exact replacement Form`.
Ordinary retirement admits whole only; absent InflType does not mean null. Each bundle has actual bundle,
analysis and wordform ids and exact role. Ad hoc occurrences separately declare `(rule, field, ordinal-or-null)`
destinations: no MSA/order heuristic chooses them. Contradictory role keys refuse.

### D7. Retargeting preserves identities and exposes native text effects

Keep bundle/analysis ids, MSA/InflType, bundle count/order, Sense, Category, features/glosses, full human
EvaluationsRC membership, Text uses and surface wordform spellings. Never recreate/reapprove analyses or call
parser evaluation a human Opinion. The native MorphRA setter copies replacement underlying form alternatives to
bundle Form and clears absent alternatives. Declare every changed/cleared WS value in footprint, snapshot,
Dry Run and read-back; do not restore retired surface text or alter the survivor to work around identity.

Ordinary unformatted copies and empty/default values are eligible only after package-pin proof. Guessed/pattern,
customized/rich bundle text, missing authoritative Morph identity and populated source WS values absent on the
replacement refuse until supported. Source inspection alone is not acceptance proof.

### D8. A complete frozen census precedes any deletion

Inventory the whole project, not just Selection or projected snapshot: every bundle Opinion, all ad hoc routes
including disabled/legacy/flat/grouped FirstAllomorph/RestOfAllos/Allomorphs, custom/native/unprojected references
and owned dependents. An author's reference list declares destinations, not completeness. Preserve ad hoc order
and conjunction; combine changes into one final sequence write. Repeated targets, First/Rest self-targets and
collapsed conjunctive members refuse even on disabled rules. Active prohibitions need supported exact-denotation
proof; finite witnesses cannot establish universal equivalence. Unproved/broadened restrictions name the rule and
block, never silently drop/split/disable/redesign it. Historical Notebook identity references are not transferred.

Compare the frozen census with all destinations before mutation. Refusals name retired identity, exact distinct
Approved-analysis/ad-hoc counts and unresolved rows, plus bundle, other-Opinion, custom/other-reference and sequence
counts; unsupported capabilities have distinct reasons. Explicit dependsOn edges put class before using rule,
complete rule/placement before retargets, and deletion after all relevant writes. No fictitious barrier or array
position supplies ordering. One atomic data unit contains rule/context writes, retarget/text effects, explicitly
authored judgment revisions where required and deletion. Immediately before deletion verify destinations and zero
unmanaged incoming references. LibLCM's automatic clearing is never validation.

### D9. Verification translates independent frozen expectations only

`frozen-expectations/v1` binds source Baseline, human-input digest, complete case-manifest digest and expectation
revision. Freeze all populated vernacular surface/WS cases of words with Approved readings using retired forms,
including words outside Selection and every Approved reading of those words, unchanged ones included. Retain
original ordered morphology, bundle text and human provenance. Add held-out cases and reviewed negatives.

A Proposal-scoped adapter changes only explicitly mapped Form identities at exact roles, records native text
copies separately and uses ADR 0020's unchanged morphology comparison. It cannot derive its oracle from mutated
scratch, rewrite historical Reports, join by MSA/spelling or alter guessed-text matching. Collapsed translated
readings and contradictory positive/negative translations refuse ordinary retirement. Surface/WS cases remain
separate. Meaning-changing judgment edits are explicit new Notebook revisions retaining originals; prior keeps,
reasons and negatives never transfer or reconfirm automatically.

Verification binds exact intent, Baseline, census/mapping digests, before/translated expectations, full affected-case
manifest, actual effects, paired Assessments and rebuilt Parsimony artifacts. Completed before/after searches must
produce required Approved/held-out readings with no newly produced reviewed negative. Already-missing before
readings fail the before condition. Missing capabilities, unfinished jobs and unavailable identity/WS are incomplete,
not success. Large sets use bounded jobs with one complete aggregate manifest; never sample away mandatory cases.
Absence of reviewed negatives is an explicit evidence gap.

“Now via the rule” requires producer derivation/Trace bound to the exact after signature and authored rule, or a
completed comparison disabling only that rule that loses that reading. Mere success, missing old GUIDs, aggregate
attempts or an unimplemented rule-use count prove nothing. Unchanged outside-context readings need not fire it.
Rebuild facts/evidence from the full candidate and rerun the original detector: bind finding identity, before/after
n/N, evidence digests and actual resolution. Suppression, filtering and loader drops cannot resolve redundancy.
Parse preservation and finding resolution are separate; other findings remain visible and advisory. None replaces
ordinary Readiness/Preflight.

New references, sequence/source/replacement changes, MSA/InflType/Opinions and relevant schema/judgment revisions
invalidate Preflight. Update baseline may refresh evidence or unambiguous anchors, never silently change mapping,
targets, values, intent/order or add a new destination. Re-author explicitly.

### D10. Stem retirement adds gates, not permission to bypass affix proofs

`allomorph-retirement/v2` requires hashed scope (`affix|stem`) and stemName assertions, with no v1 reader,
default/alias or upgrade path; regenerate old authoring artifacts. Expectation/review versions stay unchanged.
One entry has one scope; entries may mix scopes in one coherent rule-backed Proposal. The position assertion records
the closed morph type, never a spelling guess. Stem sources/survivors are
concrete nonabstract MoStemAllomorph of the same ordinary unbound root/stem morph type. Refuse bound forms,
clitics, phrases, particles, cross-class or differing root/stem types. Primary remains protected; keep MSAs,
senses, entry identity and retained allomorph order. Require the exact entry-owned MoStemMsa, null InflType and
whole role. Borrowed variant/affix MSAs and stored compound-expansion roles remain unavailable.

Every form asserts stemName id or explicit null (affixes null). Source and survivor must share the exact identity,
but equal ids alone do not prove effective gates: bind owner/category, normalized Regions/feature definitions,
sibling graph, MSA features/classes/restrictions, FromStemName consumers and allomorph order. Named and unnamed
allomorphs can both have exclusion gates. Changed/unavailable gates refuse; never edit those dependencies implicitly.

Bind PhoneEnv ids and resolved meanings, class extension, phoneme representations and boundaries. Stem environments
restrict the whole root and do not locate an arbitrary internal segment. Replacing different environments with a
rule requires exact conditioning correspondence and all before/after proof; never copy an environment by default,
treat an unresolved one as empty or infer a productive rule from spelling pairs. Required/excluded morphological
and ad hoc gates remain independent. Stem-edge, stratum, internal-position and outside-context controls are required;
if typed rules cannot express the domain, verification stays unavailable.

Include unchanged whole-root bundles inside compounds and all their readings. Inventory MoDeriv.StemForm and
MoCompoundRuleApp.LeftForm/RightForm as unmanaged routes; no trace writer is introduced. Inventory both directions
of entry/sense ComponentLexemes, variant types, complex-form components and compound-rule/MSA context. Refuse
borrowed/expanded realization until an explicit preservation capability exists. Independently loadable descriptive
links require pinned proof; unavailable classification refuses. Zero Approved uses is not proof of safe deletion:
freeze independently authored held-outs and seeded Approved stem/compound/Text cases, reporting zero attestation
honestly. Stem-specific proofs cover selection-gate equivalence, environment/rule correspondence, expansion census,
bundle WS text effects, save/open/rollback and frozen-reading verification. The additional required proof keys are
`stem-selection-gate-equivalence`, `stem-environment-rule-correspondence`, `stem-expansion-context-census`,
`stem-bundle-form-effects` and `frozen-stem-reading-verification`. Publishing proof names proves nothing.

Affix delivery remains first; ordinary sound-rule retirement retains CanApply=false until each scope's family proofs
are complete. A narrow standalone `motif-retire-allomorph` exception may delete an unreferenced exact duplicate stem
alternate (same morph type, spelling, StemName and PhoneEnv set in one entry). It moves no references, refuses if
any exist, and does not enable rule-based stem retirement. The
[allomorph retirement contract](../allomorph-retirement-contract.md) defines portable shapes and admission gates.

### D11. Tag by query respects FieldWorks polarity

`compose-tag-stems-by-query` evaluates category, final letters, present/absent feature and gloss predicates in one
stem pass (plus senses for glosses), emitting existing MoStemMsa.ProdRestrict add/remove operations. Summary first:
matched count, tagged count and polarity. For `gate: rule`, tag the smaller matching/complement side; ties tag
matches and require the feature. Rule required/excluded sets can express either polarity. Default `gate: affix`
tags matches even when larger: affixes only require From Exception Features. State that limit rather than invent
a negative mechanism. Untag is the same query with `action: untag`. No project default for new stems is added.

### D12. Reading identity is an advisory comparison over the Trial

`ReadingIdentityProof` uses stored before/after Trial rows, translating only exact retirement role mappings and
set-diffing affected words' form/MSA/InflType/guessed-text readings. Report words compared and every lost/gained
reading. Absent, stopped or unfinished words make it Incomplete, never Identical. It starts no parser and supplies
no rule-attribution proof. Differences are findings, not refusals: a new ambiguity can be correct. Keep this result
beside mandatory human-reading verification rather than replacing that stronger gate.

### D13. Removing an exception feature is one Draft with explicit limits

`compose-remove-exception-feature` takes feature, stale-name guard expectedName and optional keepFeature. In one
O(analyses + rules) pass it removes stem ProdRestrict memberships and each regular-rule RHS wrapper from required
and excluded sets. Environments have no such reference. It uses no incoming-reference index and parses nothing;
summary first gives stem/branch counts and required/excluded counts.

Delete the feature after removals unless keepFeature or an affix reference requires keeping it, stated in summary.
LibLCM removes its PhonRuleFeats wrapper when the possibility leaves the exception list. The delete accepts only
unheld wrappers; a still-referencing branch refuses. Never leave a wrapper without its feature. Affixes are not
edited: count them and warn that a required feature with no tagged stems makes their readings disappear; name
`compose-edit-exception-features` and `compose-edit-derivational-affix-restriction` as follow-up. Warn when removing
a branch's sole requirement broadens it to every stem. Other unhandled referrers can still refuse at Apply.

`FeatureRemovalReadingProof` is the same Trial-row comparison with no retired-form mapping. Its scope is all Trial
words, since a removed exclusion can affect any. Store it in TrialRecord.ReadingIdentity and print counts,
differences and incomplete words from `motif trial`. This is CLI/AdvancedAi Draft authoring with no agent tool,
as is tag by query; neither Applies. Replacing a feature is not a hidden option: author the new tags/rule sets first.

## Consequences

Phonology uses FieldWorks identities and supported semantics, including priority cases and real loader bounds.
Retirement preserves scoped known readings rather than proving universal grammar equivalence or sense agreement.
Reject cascade-as-validation, SwapReferences as a complete algorithm, form merging, surface-copy workarounds,
automatic mapping, MSA/spelling joins, merged translated analyses, silent negative remapping and unique evidence
kept only in disposable files. Runner keeps the caller-owned cache and persistence boundary in
[ADR 0017](0017-dry-runs-and-trials-use-a-scratch-copy-and-apply-is-read-back.md). Human judgments remain project data;
derived SQLite artifacts cannot own unique human input.
