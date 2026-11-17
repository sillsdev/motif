# ADR 0021 — A Trial's review report: three levels, red flags, no verdict

A person can start with a short account of a grammar change and inspect the words behind it. Motif shows
observed concerns and limitations without recommending acceptance or assigning a quality score.

**Status:** Accepted, 2026-11-14.

## Context

A Trial needs a readable record that survives later edits and evidence cleanup. Exact comparisons against
human decisions, bounded saved Differences and exhaustive report lists serve different reading needs.

## Decisions

### D1. One Evidence set shape describes project and Trial measurements

An `EvidenceSet` names its source Baseline token, root ParseTime Assessment, replacement reruns, measurements
by kind, grammar check, measured words and project/Trial origin. One resolver supplies it to every command
read; current is resolved centrally. A Trial reuses its source Baseline's Text index and is validated by
Proposal id, intent digest, source token and exact measurement ids, never as current project evidence.
Success and refusal are separate; successful null means no current record.

A Trial record is read from existing Jobs and Assessments: Dry Run, measurement ids, scope, and independent
freshness against today's Baseline and Proposal digest. Freshness is computed, not stored as a state.
The shared latest-Trial rule chooses newest completed/running, otherwise newest stopped; a failed retry
does not hide a good retained result. `TrialStanding` supplies not tried, trying, failed, current or needs
another try with a reason, with one shared wording table. A newest result whose required evidence has been
released is unavailable, never silently replaced with an older result. Retention is ADR 0023.

Try again explicitly queues the Default Selection for a grammar change or the touched words otherwise,
follows the Job and publishes its result. Project grammar checks bind to a Baseline; Trial checks bind also
to Proposal and exact intent digest. Neither reading nor staleness triggers a rerun.

### D2. The report belongs to the measured Trial

CLI `proposal trial`, MCP `motif_trial` and `motif_difference` drill-downs present frozen Trial, Baseline,
Assessment, warning and Parsimony inputs. Bind intent digest, prerequisites, scope and evidence identities;
a Draft needs no revision. Reads never parse or refresh. An explicit one-word trace can request new
diagnostic work against the recorded candidate, labelled separately from the saved measurement.

### D3. Exactly three disclosure levels share the same counts

- **summary** fits one screen: red flags, then Incomplete when applicable, principal measures with
  denominators, scope, freshness and links.
- **failures** shows Wordforms to review, grouped by observed cause, with before/after evidence and omitted
  counts. A row need not be a failure merely because of the level's name.
- **full** offers complete paged/filterable lists, including unchanged cases, exact identities and an explicit
  one-word parse trace while its inputs remain available.

Counts never vary with disclosure level, filters or page size. Missing evidence is not zero. A cause is an
observed outcome unless producer evidence establishes more; trace participation alone does not establish cause.

### D4. Exactly five factual changes are red flags

1. **New word failures:** a formerly parsing word gets no parse, or an Approved word loses a previously
   returned Approved analysis.
2. **Confirmed correct readings lost:** each previously returned confirmed reading lost, whether confirmed
   by an Approved analysis or a project-named reference/control set. Keeping a different reading hides none.
3. **Known non-words now accepted:** a confirmed whole-word negative gains a parse. A Disapproved reading is
   not a whole-word negative.
4. **Parser work more than 20% worse:** strictly more than 20% total parser work over the same Baseline scope
   with comparable work. Show percent change, never milliseconds or raw counts in this review presentation.
5. **New warnings or Parsimony findings:** identify each new warning, measure and finding against its bound
   Baseline evidence. Do not subtract aggregate totals or invent a Parsimony score.

Label overlapping Approved-reading losses rather than adding them as distinct words. Gains and resolved
findings cancel no new losses. Existing failures remain visible. Source-validated assertions retain source
and actor and never become human Opinions. The summary always says **“No red flags means only 'not bad',
never 'good'.”** An unavailable comparison is a named limit, not evidence of no red flags. There are no
configurable extra red flags, verdict, recommendation, PASS/BLOCK result or combined ranking.

### D5. Incomplete is prominent and separate from failure

List Incomplete immediately after flags and again at failures level when required words were unmeasured
or searches stopped at work, time or memory limits. New stops, their count or percent change alone create
no red flag. An unfinished search cannot prove absence, uniqueness or loss. A forbidden parse actually
returned remains a counterexample. Distinguish observed loss, observed presence, unknown absence and
unmeasured input. Run-level memory refusals do not become invented per-word outcomes. Missing warning/
Parsimony comparisons, incompatible work and unavailable expectations remain visible limitations.

### D6. Project-named controls define questions, not acceptance policy

Named sampled-token, paradigm-cell, reference, regression and negative sets are project configuration;
Motif supplies no language-specific oracle or coverage target. Freeze names, roles, source references and
resolved members with the Trial, preserving token positions/weights and declared cell denominators. Parsing
a reference form alone does not prove its expected reading. Capture exact word/writing-system and reading
identities where missing. Membership is separate from human judgement; candidate Opinion edits cannot
improve their own frozen comparison. General comparisons annotate different contexts under ADR 0020.

### D7. The complete report index is disposable and separate from a Difference

Stream/index all requested frozen cases without changing the saved Difference's bounded shape or the older
Trial-summary Assessment. Do not duplicate full trace trees or create an external durable judgement store.
The index follows [ADR 0023](0023-one-store-beside-each-project-inside-a-byte-budget.md). Written summaries
and bounded examples survive raw-data release and upgrades; missing full lists say unavailable. Explicit
rebuild requires exact retained inputs, never today's Opinions substituted during a read.

### D8. A Difference is one frozen comparison per Proposal, Text and reference

It is a standalone saved result, not an Assessment kind. Before/after Assessment ids are provenance, not
retention roots: all necessary comparison facts are copied by value. The older `Difference` Assessment
remains the Trial summary and is not extended. Initial scope is one completed Trial and one Text; wider
scope needs a new shape/store generation. Several saved Differences remain separate, never an implicit union.

Read default-human Opinions through LibLCM, spelling through SpellingStatus and only explicitly supported
project judgements. Unknown is not Disapproved, Correct spelling is not approval, and a Disapproved reading
does not forbid a whole word. Compare exact ordered morphology under ADR 0020; labels only explain it.
Freeze Text id/title, Baseline token, policy, intent digest, each side's Assessor/parser version and hash,
grammar hash, Selection identity/hash and tokeniser, plus the retained occurrence ledger and per-word
decision witnesses. Both sides use the same captured decisions. Their changes require a new capture.

The human slice captures incorrect versus undecided spelling only, not Correct spelling, Wordform ids or
writing-system bindings. Broader capture needs a deliberate contract change. A token binds only when exactly
one of its forms was measured and exactly one Wordform shares it; ambiguous tokens are unbound, never guessed.
Measured words outside the Text are counted separately, outside the comparison.

### D9. Difference states preserve independent evidence axes

Settled state codes are `approved-unique`, `approved-with-alternatives`, `no-approved-match`, `no-parse`,
`no-approved-expectations` and `conflicting-decisions`. Unsettled states are `incomplete` (capped/timed out),
`unavailable` (refused/invalid/unprojectable evidence) and `not-measured`. For words without human decisions,
change codes `gained`, `lost`, `changed`, `reading-added` and `ambiguity-up` compare checkpoints.
`reading-added` keeps all prior readings and adds another: it remains in `delta.analysis-changed`; `changed`
means a reading was lost or replaced. `delta.*` identifiers are wire codes, not window vocabulary.

Keep expected/matched/missing Approved counts, returned Disapproved identities, observed parse count and
spelling independent. `no-approved-expectations` still carries a zero parse count when appropriate;
`no-parse` as a state requires an Approved expectation. Incomplete searches establish neither absence nor
uniqueness, but returned forbidden parses remain observed counterexamples. Conflicting or missing decisions
are limitations, never clean zero findings.

### D10. Source-validated decisions remain separate from human decisions

The default reference is `human`. Opt-in `source-validated` reads attributed decisions staged by a named
`CmAgent` with `Human = false` from Applied Proposals, never a person's evaluation/spelling status or the
compared Proposal's own staging. Those records use supported semantic Intents and a person's Apply;
importers never bypass factories/operation contracts or guess entries by gloss or lemma.

Every front end labels a source-validated Difference as not a person's. Never merge or rank it against
the human reference. Listed words name evidence tier and agent; this evidence is read from Applied
revisions per page, cached by those revisions per open project, rather than copied into the small summary.
The summary keeps the reference and a fixed-size staged-decision fingerprint. Conflicting agents produce
`conflicting-decisions` regardless of Apply order. Ill-formed/typo evidence marks incorrect only for this
reference, tied to its stored wordform id that expires with detail. Unreadable Applied revisions are named
and omitted with a warning; other evidence still counts.

`--reference` chooses CLI reference; a separate opt-in tool in the source-validated MCP profile exposes it.
Default `motif_difference_produce` remains human. Any missing Wordform/Text/Segment authoring family must
first meet the full schema, coverage, lowering, Apply/read-back, rollback, conflict and conformance contract.

### D11. Difference production is explicit and publication is atomic

`difference produce` and `difference show`, with `motif_difference_produce` and
`motif_proposal_difference`, share Commands queries. Production streams linearly with bounded publication
transactions; no partially produced result is readable. The synchronous slice publishes one complete
transaction without claim fencing; a Job wrapper is a separate scaling step. Finalize does not produce it.
Both Baseline checks refuse `difference.baseline-changed` unless captured decisions belong to the Trial's
Baseline. Repeating the same Trial/Text/Baseline/policy returns its sound saved result without writing;
a new result replaces only the same Text/reference. Decided Proposals create no new result
(`difference.proposal-decided`); deferred or reopened ones can explicitly recompute.

Summaries/matrices precede indexed bounded pages. Reads never reparse or reopen live project data.
Different parser hashes, tokenisers or longer-limit reruns warn without forbidding the comparison;
an absent parser identity is unknown, not a mismatch.

### D12. Difference summaries survive; word detail is bounded and short-lived

The summary holds fixed-size matrices, counts, bindings and limitations for the Proposal's life, within
8 KB even for thousands of words. It contains no word-growing lists. Detail goes in the same transaction
as Applied, Declined, Withdrawn, Superseded or Deferred. A deferred Proposal can make it again. Summary-only
reads return `WordDetailKept = false`, an empty page and no error. Upgrades preserve decided/deferred
summaries, marked without detail, and regenerate undecided results; summaries before readable schema 56
cannot be carried. Current upgrade retention is ADR 0023, including written review summaries.

Count all words but retain rows only for changed/categorised/limited cases. Each before/after cell,
category and limitation keeps its best 100 words: most Text occurrences first, ordinal spelling for ties.
Keep the union across groups; a cell may therefore contain more than 100 stored words. Each retained word
keeps its first 10 places in reading order and its full occurrence count. Hold a word during production
only while a group retains it. `WordsListed` and group/occurrence `Listed` fields expose actual bounds;
absent fields on an older result mean all. All front ends say “counted but not listed” with true counts.
Equal-state chips count without pretending to list unchanged words; categorised ones remain reachable.

Damaged detail preserves the summary with `WordDetailUnreadable = true` and allows explicit regeneration.
Unreadable summaries are individually named by Text/reference/time and omitted from the saved list;
the window's note is `SavedDamaged`. Without an id choose the newest readable one. If none is readable,
`difference.unreadable` names all.

### D13. The window reads the same Difference query

Every selected non-Draft Proposal's inspector shows its saved comparisons or a truthful not-tried/decided
note. **Make Difference** explicitly chooses a Text, reports progress, supports cancellation with no saved
partial result, and becomes **Make Difference Again** for an existing human comparison. Deferred results
show their surviving summary and can regain detail. The action always uses human decisions and cannot
replace an agent's source-validated comparison. While producing, hide the stale intro/reference note.

Saved Differences lists at most 50 and counts the rest; each names Text and reference. CLI/MCP take `text`
and `reference` or read newest. Chips send query-owned state/category/limitation/could-be-bad filters.
Word/place pages stop at 100 each and ask for narrower filters. Summary-only chips count without filtering.
The window never reclassifies words; the older Trial summary retains its own Texts-page presentation.

### D14. Attested and generated gains are reported separately

A gain changes from no parse Before to a parse After. It is attested if sampled in project texts or carrying
an eligible Approved analysis; otherwise it is generated exposure, including paradigm/reference/touched
forms without other evidence. Count each word once across sets. Generated forms cannot inflate attested gains.

Count Proposal-created FieldWorks objects from operations by kind: entries, senses, MSAs, allomorphs,
variants, affix processes, phonological rules, environments, exception features and other list items.
An entry's own senses/MSAs/allomorph are not counted again; an entry targeted by a variant reference counts
as a variant. Count exception features only when created then named by a host operation in the Proposal;
report their host count beside, not in, the total. Object counting needs no parser run.

Freeze `[gain-gate]` mode and `min-attested-per-object` from `<project>.motif.toml`. Modes are off, advisory
(default) and hold; default threshold is 1.5 attested types per added object. Below it is under-threshold;
hold also sets the report's hold field. Added phonological rules or affix processes are exempt as rule-added.
Incomplete cases, no objects or off never produce under-threshold. This is report-only: Apply and Readiness
do not read the hold; an autonomous Apply reads it only through a declared gate (D16). Older reports lacking
`gains` remain readable.

### D15. Review supplies no Apply authority

Reading a report grants no permission, approval token or Trial/Apply parity check. MCP never Applies.
Human Apply remains separate; autonomous CLI policy is separate. Motif's tests establish its own behavior;
the review describes grammar evidence. Parsimony's narrowly opted-in autonomous policy is in ADR 0022.

### D16. A declared gate gives autonomous Apply a verdict

The report gives people no verdict (D15). A project may also declare a gate in `<project>.motif.toml`: the named
word sets that must show no attested word lost and be fully measured, the gain-gate mode of D14, and the
`[parsimony] block-on` list of ADR 0022. A Trial then carries one gate verdict, shared by the CLI and MCP:

- **green:** nothing declared was lost and everything declared was measured.
- **loss:** a new word failure or any lost confirmed reading, unless the word is listed in the file passed to
  `--expect-loss` for intended retirements. A `--review-set none` run does not suppress this check.
- **unmeasured:** a word that had an analysis in the Baseline was stopped by a budget or timeout. The word is named.
  A stopped word with no Baseline analysis does not affect the gate. A loss outranks unmeasured, because a known loss
  is definite and actionable.

The command exits 0 on green, 2 on unmeasured and 6 on loss. A new second reading on an attested word is a hold: it
keeps the gate from being green.

`apply --autonomous` refuses unless the project declares a gate and the newest Trial's gate, for the current content
and project state, is green. `--force` stays refused in autonomous mode. This amends D15 for autonomous mode only;
a person's Apply, the window and the MCP keep the Readiness checks of
[ADR 0017](0017-dry-runs-and-trials-use-a-scratch-copy-and-apply-is-read-back.md) D4, MCP never Applies, and
advisory reports stay advisory. Blocking measures remain opt-in as in ADR 0022 D6.

## Consequences

Frozen identities prevent candidate edits or live Opinions from improving their own historical comparison.
Bounded saved rows and a disposable complete index make different size promises explicit. A reference form's
parse, an empty flag list, or an incomplete search never becomes evidence of approval.
