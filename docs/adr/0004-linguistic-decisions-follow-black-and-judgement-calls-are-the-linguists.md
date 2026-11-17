# 0004. Linguistic decisions follow Black; judgement calls are the linguist's

**Status:** Accepted, 2026-11-14.

## Context

Analyses such as derivational versus inflectional are often matters of judgement. Validity checks must protect
the project without imposing a preferred linguistic analysis or treating a measurement as a verdict.

## Decisions

### D1. Black is the linguistic source

Linguistic guidance, Help, Parsimony recipes and Grammar Health wording follow H. Andrew Black's conceptual
introduction (2025) and Parser Workshop guidance (2026). When Motif differs, correct Motif. The retained
conceptual introduction and its licensing exception are specified by
[ADR 0002](0002-grammar-data-never-belongs-in-motif.md); the workshop full text is not shipped.

Black treats derivational versus inflectional, including reduplication, as a judgement call along a continuum.
His criteria are questions and recommendations, not hard rules. Advice against an unspecified affix kind is
about spurious analyses and parser cost: choosing and later changing one's choice is preferable to leaving
it open, but the advice is not a ban.

### D2. The maker owns the judgement call

A linguist or agent may choose and record why, without asking a person first. The reason belongs in the Guide
section's **Decisions** part ([ADR 0005](0005-the-grammar-guide.md)); bounded applied grammar-review judgements
use the project records of [ADR 0003](0003-motif-writes-only-what-fieldworks-can-create.md). Motif does not
second-guess a call; a person may decline the resulting Proposal.

### D3. Allow every valid FieldWorks model shape

Authoring and editing support the constructs, morph types and affix kinds LibLCM allows, within ADR 0003's
FieldWorks-producible boundary. Refusals must prevent invalid data: dangling references, wrong ownership,
fields absent from a class or shapes FieldWorks' loader cannot read. Unusual analyses and Motif preferences
do not justify refusal. A parser-specific limitation is reported as such, never disguised as a model ban.

Reduplicating affix processes may be inflectional, derivational or unclassified. A bracket-pattern
reduplication that FieldWorks loads but PanGloss does not yet support remains writable with the parser's
warning. Form and MSA kind are independent. An entry can own several MSAs, each producing its own rule or stem,
with senses pointing to either. A derivational From Inflection Class is one class, admitting its subclasses;
a set of host classes is expressed as several analyses on the entry, not an invented set field. A circumfix
is a paired construction, so editing one half cannot stand for changing the pair. Clitics have no
must-attach property to author. A one-input, one-output affix process may be stored even where the loaders
skip it; the Dry Run warns. An empty derivational destination category is model-valid and must not become
a preference-based refusal.

### D4. Grammar Health, Parsimony and Guide checks advise

Findings may warn, rank, ask or suggest a repair. Ordinary Grammar Health, Parsimony and Guide findings
never block a Draft, composer, Proposal or Apply; severity is not validity and a ruling records
“I meant it”. Recipes name the options and evidence and end by asking the linguist. Only the closed,
explicitly opted-in autonomous Parsimony policy in ADR 0022 decision 6 can hold work on its named findings;
Consultant-mode windows stay advisory and Guide checks never gate. Other Report kinds may participate in
an explicit project regression policy under ADR 0020. No finding grants Apply authority.

### D5. Motif presents evidence without deciding which grammar is best

PanGloss generates; Motif orchestrates, stores and presents; the linguist or external assistant judges.
The improvement loop, objective function, accept rule and stall limit belong outside Motif. The CLI must be
complete enough for that loop to drive it through structured results. Motif can report a regression and
Readiness without rendering a linguistic verdict or ranking candidate grammars as the winner.

### D6. Attribution never manufactures human agreement

No approval Decision or human/AI verdict record exists. Proposals and review marks name no author or
reviewer ([ADR 0007](0007-a-proposal-has-one-lifecycle-a-conversation-citations-and-one-apply-path.md)). Where
attribution still exists, such as Comments and confirmed reviewed negatives, it is explicit and cannot
turn an agent's choice into a human's. Comment attribution is a reader's label, never Apply authority.

## Consequences

Review asks what invalid model each refusal prevents. Advice keeps Black's reasoning and citation instead
of turning it into a prohibition. Data validity, measured evidence and the analyst's judgement remain
separate. Repository decision-publication rules belong in AGENTS.md rather than this linguistic contract.
