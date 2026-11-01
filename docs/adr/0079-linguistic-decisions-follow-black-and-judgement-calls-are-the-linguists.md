# ADR 0079 — Linguistic decisions follow Black, and a judgement call belongs to the linguist or agent who makes it

**In plain terms:** Where linguists disagree, or where the line between two analyses is blurry, Motif lets a person or an agent choose and write down why. Motif only says no when the result would not be a valid FieldWorks project, never because it prefers another analysis.

Status: accepted

## Decision

1. **Black is the source.** Motif's linguistic guidance, Help text, Parsimony recipes and Grammar Health wording follow H. Andrew Black's conceptual introduction (2025) and the 2026 Parser Workshop notes, both kept verbatim under `docs/parser-help/workflow/sources/`. Where Motif guidance differs from Black, Black wins and the guidance is corrected.
2. **A judgement call is the maker's.** Black marks some choices as judgement: derivational versus inflectional, and the same choice for a reduplication affix. A linguist or an agent may make such a call. Motif records the call with a rationale (the existing decision and judgement records) and does not second-guess it.
3. **Whatever LibLCM can hold, Motif allows.** If the FieldWorks data model can store a construct, Motif's authoring and editing verbs must be able to write it, on every morph type and every affix kind the model allows.
4. **A refusal must rest on an invalid model.** Motif refuses an operation only when the result would break the project: a dangling reference, a wrong owner, a field the class does not have, a shape the parser's loader cannot read at all. "We prefer another analysis" and "this is unusual" are not reasons to refuse.
5. **Health and Parsimony advise, they never forbid.** Grammar Health findings and Parsimony findings may warn, rank, ask the linguist, or suggest a repair. They do not block an Apply, a Draft or a composer, and a finding's severity is not a validity check. Recording a disposition is how a linguist says "I meant it".
6. **Decisions reach `main` directly.** Changes to ADRs, `AGENTS.md`, `CLAUDE.md` (where a repository has one) and `CONTEXT.md` are committed straight to `main`, fast-forward only, with no pull request or branch review. They are the project's visibility and messaging mechanism: a decision is made when it is on `main`, where every person and agent reads it. This repository has no `CLAUDE.md`; `AGENTS.md` is the agent instructions file.

## What Black says (the evidence)

- **It is a judgement.** Introduction 2.1.5: "the range from derivational to inflectional is a continuum", some affixes "float somewhere in the middle", and the criteria "are not hard and fast rules" (`black-flex-conceptual-intro-fulltext.txt:2101-2107`, PDF page 39, printed page 31). Workshop L02: "Not always clear cut!", "In-between cases, we get to decide" (`black-parser-workshop-2026-L02-fulltext.txt:562-567`). In FLEx the choice is binary, "you have to decide", and "you might decide to go with whichever works best for the parser" (line 567).
- **Black's own worked cases split.** English *-ly* and comparative *-er* have inflection-like traits but "most would" call them derivational; Bantu verb extensions are often thought inflectional but Black found "treating them as derivational is simpler" (workshop lines 599-613). A consultant "can help you think it through" (line 615).
- **The criteria are questions, not rules.** Bickford's table in 2.1.5 and the four workshop questions (set membership, stated position, outside the less constrained affixes, grammatical meaning; lines 643-649) lead to "inflectional" or "derivational" as a recommendation.
- **Leaving it open is the one discouraged option.** An unspecified affix type "isn't recommended" because the parser then proposes "all sorts of crazy analyses" and runs longer; choosing, then changing later, "is better" (workshop line 524). This is advice about cost, not a ban.
- **Reduplication takes the same call.** Both reduplication recipes list "Determine whether the reduplication morpheme is derivational or inflectional" as a step to be made by the analyst (introduction 3.2.1 step 2, line 3295, PDF page 60; 3.2.2 step 1, line 3350, PDF page 61). Black gives no rule that reduplication is one or the other; his Tagalog example marks imperfective aspect by reduplication (1.1.7, line 600).
- **Related guidance in the repository** already states the same stance: `docs/parser-help/workflow/affix-status-and-spurious-parses.md` ("a modeling commitment") and, for the data model, `docs/research/grammar-authoring/liblcm-recipes.md` section 3 and section 11.

## What the code establishes

Checked against FieldWorks `HCLoader.cs`, PanGloss `compile/affixes.rs`, and a private scratch run (see the gaps note for the table):

- LibLCM stores the grammatical kind on the entry's MSA (`IMoInflAffMsa`, `IMoDerivAffMsa`, `IMoUnclassifiedAffixMsa`) and the form separately (`IMoAffixProcess` or `IMoAffixAllomorph`, with any affix `MorphType`). Nothing in the model ties a reduplicating form to one MSA kind.
- FieldWorks' loader builds an affix-process rule for each of the three MSA kinds from the same form-loading code. PanGloss builds a process allomorph without looking at the MSA kind. So reduplication written as an affix process parses as inflectional, derivational or unclassified. Reduplication written as a bracket-pattern string on an ordinary affix allomorph (`[C^1][V^1]-`) loads in FieldWorks' loader but is rejected by PanGloss with a warning today; that is a parser limit, recorded as such, and Motif must not turn it into its own refusal.
- Motif already lets `compose-author-affix-process` choose any of the three kinds. The remaining refusals are listed in `_briefs/harness-resume-2026-10-23/judgement-call-gaps.md`.

## Existing Motif behaviour that conflicts

These stay until each gap is fixed in its own change, and each fix should cite this ADR:

- ~~No composer changes an existing affix between inflectional and derivational, although Black's advice is to pick one and change it later.~~ Closed in 1569bfe8e (G1).
- ~~`compose-edit-morph-type` accepts only stem-family morph types; an affix whose type is wrong (prefix versus suffix, infix versus circumfix) cannot be corrected in place.~~ Closed in c884e2795 (G2).
- ~~`compose-edit-affix-inflection-classes` cannot reach a reduplicating affix process, so an inflectional reduplication affix's class restriction can be set only when it is authored.~~ Closed in bec69734b (G3).
- ~~The process composer allows only exact copies and single inserted phonemes, while LibLCM and PanGloss also hold insert-by-class and modify-from-input, which partial reduplication with changed sounds needs.~~ Closed in b8ae3c178 (G4).
- ~~`compose-author-phonological-rule` demands a left context, although LibLCM and FieldWorks' loader allow a rule with none.~~ Closed in 17b91c7aa.
- A derivational MSA must name a destination category; LibLCM allows it empty.
- ~~`compose-change-affix-kind` refuses when an ad-hoc prohibition refers to the old MSA.~~ Closed: it moves prohibitions and morph bundles to the new MSA.
- ~~Open: a circumfix is refused in `compose-edit-morph-type` and `compose-edit-affix-inflection-classes` because Motif has no two-part circumfix authoring yet, not because of LibLCM.~~ Authoring is closed by `compose-author-circumfix`, which stages the circumfix entry with its prefix and suffix parts as FieldWorks holds them. The two edit composers still refuse a single form edited into or out of a circumfix, because that one form is half of a pair.
- ~~Open: the affix-process composers refuse a modify on the remaining stem, avoidable because FieldWorks' UI allows it and HermitCrab's ModifyFromInput applies to every node in the captured range.~~ Closed in cd0f93fb7. An affix process with one input and one output is allowed, but FieldWorks and PanGloss skip it when loading, so the Dry Run warns.

## Consequences

- Guidance and Help that say what an analyst "should" choose between derivational and inflectional become advice with Black cited, never a refusal. The existing advice to avoid "unclassified" stays, worded as Black words it (a cost warning).
- Review of a Motif change asks one question about each refusal: what invalid model does this prevent? If the answer is a preference, the refusal is removed or becomes a warning.
- Agents may make and record a judgement call without asking a person first. A person can still reject the Proposal at review; that is the check, not a pre-emptive block.
- A construct the parser cannot yet read (bracket-pattern reduplication in PanGloss) is still writable. The parser's warning, not Motif's refusal, tells the linguist it has no effect yet.
- Because decisions land directly on `main`, a decision is visible to every lane at once; code that carries it still goes through the normal gate.
- Parsimony recipes keep naming the options and the evidence, and keep ending in "ask the linguist".
