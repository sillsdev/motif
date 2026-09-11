# PanGloss handoff: produce evidence for Motif Correctness

Enable Motif to tell a linguist whether PanGloss can reproduce the morphology of every approved
analysis on a word form. A word receiving some parse is insufficient: a different allomorph or
grammatical analysis must not count as agreement.

**Status: the shared producer and Motif comparison are implemented and verified in the task worktrees.**
**Broader parser compatibility remains INCOMPLETE — we are not done with every parser path.** The linked
verification ledger records the passed gates and explicit coverage limits. Finding one working case
does not establish full compatibility or complete any word's search.

For each word whose search hits a limit, display **INCOMPLETE — parsing did not finish**, with the
step-limit or time-limit reason and any partial analyses retained. Finding one analysis, or even every
approved analysis, cannot mark that word complete. Continue the remaining words independently.

This is an implementation handoff, not a claim that every parser path has verified Correctness support.
PanGloss source was inspected at `c659ccaaec7e887993077808446979adc6e7c4ca` on 2026-09-09.
Recheck the current checkout and its instructions before editing; work is continuing there.

## Active implementation

PanGloss can emit ordered source morphology, and Motif can retain and compare it with all approved readings. Real LibLCM tests reproduce two approved same-owner allomorph readings and an independently authored inflectional variant through the process boundary. Motif's full gate passed 1,651 tests with 19 skips against the verified PanGloss executable; all five affected PanGloss package gates and the workspace check passed. These results establish the supported scope recorded in the ledger, not complete parser compatibility.

The active contract and verification ledger are in [the shared parse morphology plan](../superpowers/plans/2026-09-10-shared-parse-morph.md). PanGloss's `docs/parse-analysis-format.md` and `docs/fieldworks-parse-analysis-v1.schema.json` describe `batch --analyses` and its closed JSONL profile. The old TSV remains available for Machine conformance; it cannot establish allomorph-level agreement.

Motif freezes approved expectations from the retained source and stores raw morphology, both limit flags, unavailable reasons, and approved-reading match evidence per word. `Correctness` reports, Assessment comparisons, and regression checks consume those records. Fabricated roots lacking authored source identities remain explicitly unavailable.

The source inspection table below records the original gaps, not the current implementation status. Use the active plan's evidence ledger for current results and open acceptance work.

## Required consumer contract

Motif already defines what agreement means. The new evidence must support that definition without
reconstructing identities from display strings or treating missing information as a match.

The binding decisions are in the Motif repository:

- [ADR 0027](../adr/0027-what-counts-as-the-same-word-analysis.md): compare morph-bundle count and
  order, then each bundle's `MorphRA` (allomorph), `MsaRA` (grammatical-analysis record), and
  `InflTypeRA` (inflection type), including FieldWorks' guessed-string handling. Sense and word-level
  category do not gate agreement. The claim is "the parser agrees about the morphology."
- [ADR 0038](../adr/0038-expectations-are-fieldworks-approved-analyses.md): expectations are the set
  of approved analyses on a FieldWorks word form. A word can have several approved readings. Motif
  keys cases by stable word-form identity and compares analyses by content, not `WfiAnalysis` GUID.

Motif owns extracting and freezing those expectations. PanGloss must accept caller-owned cases and
return stable case IDs, structured analysis evidence, and truthful completion information. Preserve
separate cases even when their surface strings match. Do not require PanGloss to open or edit a
FieldWorks project or a Motif database.

## Original implementation gaps

The report infrastructure is reusable, but the current analysis identity loses information needed
for the comparison. The first implementation task is preserving that information through parsing.

All paths below are relative to the PanGloss repository.

| Location | Finding and required investigation |
| --- | --- |
| `rust/crates/pg-parse/src/identity.rs` | `pangloss.machine-word-analysis/v1` contains ordered MSA/source keys, root index, and category. It lacks selected allomorph and per-bundle inflection-type identity. |
| `rust/crates/pg-rules/src/word.rs` | `MorphRecord` retains internal allomorph and morpheme IDs. Trace these through every result-producing path. |
| `rust/crates/pg-parse/src/morpher.rs` | `structured_analysis` reads the ordered morph records but discards their allomorph IDs when constructing `WordAnalysis`. |
| `rust/crates/pg-parse/src/lib.rs` | `WordAnalysis` exposes morpheme ordinals and word-wide MPR data, not the required stable bundle tuples. Guessed and supplied-root paths also need explicit treatment. |
| `rust/crates/pg-grammar/src/model.rs` and `stats_identity.rs` | Preserve authored allomorph identity through compiled definitions; owner/index or structural statistics keys cannot substitute for the source GUID. |
| `rust/crates/pg-grammar/src/compile/lexicon.rs` | Inflection types feed MPR sets alongside other features. Preserve which inflection type belongs to which emitted bundle; a word-wide set cannot establish that alignment. Inspect variant entries and affix paths in particular. |
| `rust/crates/pg-assess/src/suite.rs`, `report.rs`, `outcome.rs` | Reuse suite validation, typed outcomes, report construction, and digest validation where their semantics fit. Their existence does not mean a producer is available. |
| `docs/grammar-assessment-schemas.md` and `rust/crates/pg-cli/src/assess.rs` | The grammar/corpus assessment producer was removed. Retained comparison/investigation commands consume existing artifacts. A supported producer still needs to be implemented. |

Motif's `AutomaticAnalysis.cs` already says its automatic and approved-analysis digests are not
comparable. Its current `CorrectnessCoverage.Compute` merely counts nonempty analysis lists. Do not
use that implementation as the acceptance oracle. Likewise, ADR 0027's historical claim that the
coarse identity cannot falsely agree about morphology is contradicted by the missing allomorph field;
the ADR's explicit bundle-matching decision is the required behavior.

## Implementation deliverables

Deliver a supported producer and enough semantic evidence for a downstream consumer to reproduce
the comparison. Establish identity fidelity before expanding the command surface. The owner's subsequent
direction is to use Machine's conformance format or justify updating that shared format; a separate
Motif-only analysis format is not the default. See the shared-contract findings below.

1. **Retain source identity throughout the engine.** Carry authored allomorph, MSA, and optional
   inflection-type identity through import, compilation, parse state, result projection, and any
   deduplication. A deduplication step must not collapse results that differ in a comparison field.
   Trace both roots and affixes, including variant-entry paths.
2. **Define a versioned structured identity.** Emit ordered bundles with the information above.
   Distinguish a genuinely absent optional inflection type from unavailable provenance. Specify
   guessed-string representation and matching using `ParseAnalysis.MatchesIWfiAnalysis` in FieldWorks
   as the reference; do not make a null key a wildcard. Document treatment of supplied roots and HC XML
   inputs whose source keys are not LibLCM GUIDs. Unsupported evidence must be explicitly refused or
   classified as uncomparable. Keep sense/category metadata outside the Motif morphology gate.
3. **Extend the shared conformance producer.** PanGloss already produces Machine-style batch TSV;
   its missing legacy Assessment command does not require restoring a separate producer. Prefer extending
   the shared batch contract to emit the required analysis evidence, with Machine's oracle and readers
   updated together. Bind each result unambiguously to its caller-owned case. Update `--describe` for
   any changed invocation; the exact wire revision remains a design decision.
4. **Preserve completion and execution evidence.** Distinguish complete/no-analysis, complete/analyses,
   capped, timed-out, invalid, and not-attempted cases as appropriate. Retain partial analyses when
   available without claiming the search completed. Finding one analysis or every approved match must
   neither stop the declared search early nor mark it complete. Emit per-case completion separately from
   findings. Continue with the remaining cases after a per-word cap or timeout; each case retains its own
   status. Motif must prominently show `INCOMPLETE — parsing did not finish` on affected words and
   summarize their count. The owner requires summaries to lead with completed/incomplete counts;
   percentages are secondary and explicitly name their denominator. Batch termination is distinct
   from completion of each word's search.
   Accept explicit step and wall-clock budgets and
   report effective values. Motif's owner-approved default step budget is 200000 per word; it must
   not be silently ignored.
   Emit source/model/tool provenance only where actually known, and define exactly what each digest
   covers. Do not fabricate semantic fingerprints or substitute unrelated byte hashes for them.
5. **Publish schemas, examples, and executable conformance tests.** Version changed shapes honestly;
   do not label enriched evidence as the existing v1 identity. Supply an input suite and an actual
   emitted report usable by Motif's strict consumer. Document errors, cancellation, incomplete-result
   handling, canonicalization, identity ordering, and digest verification.

Avoid introducing an engine selector or fast/accurate aliases. Motif needs no legacy-format migration
or compatibility reader for this integration; follow PanGloss's own applicable repository rules for
its other consumers. Statistics JSON and the current lossy signatures cannot establish the required
morphology comparison. An enriched shared conformance format can fulfill this handoff.

## Shared contract with Machine conformance

The same parser evidence should serve conformance grammars and Motif. The existing conformance
signature is useful but loses distinctions that both consumers could test with a richer shared result.

Machine was inspected at `d3b7643d7eee8e0471fb586874337a3d04d6296e`. Paths here are relative to Machine:

The findings below establish the batch-signature path only. A subsequent Luna xhigh search located the
richer Machine-to-FieldWorks identity path described below, but did not locate a second serialized
conformance format. The proposed wire revision remains a design proposal, not an approved new format.

- `conformance/README.md` makes `words.yaml` the canonical authored cases and the generated manifest
  discovery/provenance only. Do not introduce a second authored corpus merely to integrate Motif.
- `conformance/PROTOCOL.md` defines `idx, word, ms, status, signature` batch TSV. This is different from
  the old `pangloss.machine-word-analysis/v1` JSON profile discussed above.
- `SignatureFormat.BuildSignature` in `src/SIL.Machine.Morphology.HermitCrab.Tool/SignatureFormat.cs`
  projects each ordered allomorph to `a.Morpheme.Id`, then appends the final surface shape. Thus two
  results with the same morpheme chain and final shape have identical signatures even when their
  selected allomorphs differ. Multiplicity detects a count change, not a substitution at equal count.
  This is a projection limitation demonstrated by the code, not a claim of a reproduced parser defect.
- `src/SIL.Machine.Morphology.HermitCrab.Conformance/SignatureTsv.cs` accepts only `ok` and `SKIPPED`
  rows. CAP and timeout rows emitted by PanGloss do not survive that reader as explicit word outcomes.
- `Diff.cs` compares exact status/signature multisets grouped by surface word. Motif must retain stable
  word-form case identity and test inclusion of approved morphologies; additional unapproved readings
  are not automatically incorrect. Shared evidence does not require these two comparison policies to
  become identical.

Recommended shared revision, pending design agreement: preserve ordered morpheme and selected
allomorph source keys, optional per-part inflection-type evidence, final surface, and multiplicity;
represent complete, capped, timed-out and not-attempted outcomes explicitly, with partial findings.
Use authored XML identifiers for synthetic conformance grammars and authored FieldWorks identifiers
where supplied by that input. Do not substitute runtime-generated allomorph GUIDs or array positions
for stable source identity. Absence and unsupported provenance must remain distinguishable.

Update the Machine schema, oracle projection, fixture loader/materializer and comparator alongside
PanGloss's emitter and conformance reader, then consume that shared evidence in Motif. Add a fixture
where distinct allomorphs yield the same morpheme chain and surface, and a mixed completed/CAP/timeout
fixture. Preserve the existing surface-sensitive conformance checks. FieldWorks-specific inflection-type
preservation also needs an import-path fixture; an XML grammar alone cannot establish that mapping.

The TSV versus structured serialization choice is still open. The requirement is one shared analysis
evidence contract exercised by conformance, not a parallel Motif-only representation or automatic
regeneration of expected outputs to bless a changed oracle.

### Existing richer Machine-to-FieldWorks path

Machine's results already let FieldWorks recover which forms and grammatical analyses were selected.
Reuse that reference behavior when exposing shared evidence rather than inventing a new morphology model.

Machine's `src/SIL.Machine/Morphology/WordAnalysis.cs` is another representation: ordered `IMorpheme`
objects, root index, and category. Its equality compares morpheme IDs; it does not expose the chosen
allomorphs. The richer path uses HermitCrab `Word` results and their ordered allomorphs directly.

In the sibling FieldWorks repository, `Src/LexText/ParserCore/HCParser.cs`, `GetMorphs` reads the form
references from allomorph properties and the MSA/inflection-type references from morpheme properties.
It resolves these cache-local HVOs to LibLCM objects. `ParseResult.cs` then represents ordered
`ParseMorph` values containing `Form`, `Msa`, optional `InflType`, and `GuessedString`; those objects
carry source GUIDs, and `ParseMorph.GetHashCode` reads them explicitly. A hash code is not a portable
identity or serialized contract.

FieldWorks also emits and consumes XML views containing form/MSA references, but the inspected XML
uses HVOs, not portable GUID strings. The search found no serializer making `ParseMorph` the Machine
conformance wire format. Thus the missing shared output should be described as exposing already-known
source evidence, while checking PanGloss preserves the equivalent data; do not claim Machine's engine
has no way to retain it. Circumfix and guessed-form handling in `GetMorphs` also need to be respected.

## Acceptance evidence

The real parser must distinguish a correct analysis from a plausible but different one. Unit tests
of JSON serialization alone cannot establish that identity survived the engine.

- A real parse emits the expected allomorph/MSA/inflection-type tuple for every bundle.
- Two selected allomorphs sharing the same MSA remain distinguishable in emitted identity and digests.
- Different MSAs, bundle order/count, and inflection types remain distinguishable; absent inflection
  type is represented faithfully. Include a variant-entry case, not only ordinary roots.
- Two approved readings for one word can be represented and checked independently. Returning one
  unrelated analysis cannot establish agreement with either expected reading.
- Guessed forms follow the reference matching semantics and cannot accidentally match an arbitrary
  authored allomorph. Supplied-root behavior is covered or explicitly refused for this profile.
- Stable source identities survive recompilation that changes dense ordinals. Display-label changes
  do not change an otherwise identical morphology identity.
- Complete empty results, CAP, timeout, invalid cases, and cancellation remain distinguishable.
  An expected analysis missing from an incomplete search is not reported as proven absent.
- Finding one analysis does not terminate the declared search. Hitting a limit after finding every
  approved analysis still emits an incomplete case and an accurate incomplete count in the summary.
- A mixed batch continues after a per-word CAP or timeout, parses later cases, and preserves each case's
  independent completion status. Completing the batch cannot turn incomplete cases into completed ones.
- Duplicate surface strings with different case IDs remain separate. Unknown profiles, malformed
  identities, unresolved source keys, and incorrect artifact digests are rejected explicitly.
- The real executable emits the documented artifact, and `--describe` agrees with its invocation.
  Run the repository-prescribed build/test commands and report exactly which checks ran.

Start with a small real-parser fixture proving the same-MSA/different-allomorph distinction end to
end, then the inflection-type/variant path. Those are the main semantic risks. The first fixture is
a checkpoint, not a substitute for the complete acceptance list.

## Resolved consumer policy

One word may finish parsing while lacking the source identities needed to compare its analyses. Valid findings for other words remain useful and must be retained.

The owner accepted the per-word default: retain valid findings and mark an affected word's comparison unavailable, with its reason. Missing evidence cannot establish that an approved reading is absent. If all approved readings are already matched, an additional unprojectable reading does not negate those positive matches; its diagnostic remains visible. A cap or timeout always leaves the word incomplete, including when every approved reading was found.

Whole-artifact validation remains strict: malformed identities, unknown contracts, missing cases, or incorrect digests refuse the artifact rather than producing partial Correctness measurements.

## Implementation return to Motif

The shared producer and consumer now exist in the task worktrees. The active plan records the exact gates and the remaining coverage limits; the first successful fixture does not establish every parser path.

- PanGloss: `.claude/worktrees/pangloss-shared-parse-morph`, branch `feat/shared-parse-morph`, based on `19554f63ed1fdbe9e7243f473ef430f7d053473a`.
- Motif: `.claude/worktrees/ai-handoff`, branch `feat/ai-handoff`.
- Producer: `batch <grammar.fwdata> <words.txt> <out.tsv> --analyses <analyses.jsonl>`, profile `fieldworks-parse-analysis/v1`.
- Consumer: strict sidecar validation and artifact retention, frozen approved expectations, per-word match and completion evidence, persistence, reports, comparisons, regression checks, and readiness checks.
- Unsupported aggregate view: `motif analyses --assessment` returns `assessment.aggregate-unavailable` for ordered morphology; use its correctness report. No legacy digests are fabricated.
- Direct XML and fabricated runtime roots without authoritative FieldWorks source identities remain explicitly unavailable. Machine's older TSV harness has not adopted the richer JSONL profile.

These are uncommitted worktree changes. The supported real-parser integration tests and managed gate commands are listed in the [shared morphology plan](../superpowers/plans/2026-09-10-shared-parse-morph.md).
