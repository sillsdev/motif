---
name: grammar-discovery-loop
description: >-
  Method for building a grammar from a New Testament and speakers, with no reference grammar: mine patterns,
  widen a neighbouring rule, state a testable hypothesis, test against letter-swap controls, ask speakers
  word questions, encode default-first, and record the hypothesis and its Grammar Design Record. Load for any
  grammar-building package, any batch of same-shape entries, variants or tags, and any rule or affix change.
---

# Grammar discovery loop

The unit of work is a **hypothesis**: a rule plus the forms it predicts. A package is the mechanical result of
an accepted hypothesis, never a list of unparsed words. Lists are the cheapest thing to pass a gate and
the thing that grows a grammar without understanding it; the loop forces the broader rule to be tried first.

**Roles.** The person who answers questions is a speaker, who sees words and parses only. Linguists read
Grammar Design Records (GDRs) and comment on the Proposals that change them (GDRs take no comments). Rule edits happen behind the scenes; the GDR is the reviewable record.

**Sources.** There is no dictionary or reference grammar. The evidence is the New Testament (split into a
discovery set, a held-out set nobody mines, and a small dev set), the live grammar, and speakers.

## Steps

1. **Mine** the discovery set, not failures. Recurring partials and paradigm tables, affix candidates ranked by
   distinct stems, alternation candidates from near-pairs (stem plus affix expected, surface differs by one
   edit at the boundary). Output: a pattern ledger ranked by token mass. No grammar edit. Done when each
   candidate has a pattern (not a word list), its stems, and its token mass. Never open held-out books.
2. **Neighbour check.** Query the hypothesis ledger and the live rules for the same change, environment, host
   class or slot. Done when the result is written down: a neighbour exists (the hypothesis is "widen rule X"
   or "X and this are one rule"), or none, with the query used.
3. **State the hypothesis** using [references/hypothesis-record.md](references/hypothesis-record.md): the
   broadest statement that fits, the mechanism, at least 20 predicted forms beyond the ones that suggested it,
   the falsifier, expected exceptions. Pick the mechanism with the ladder in
   [references/mechanism-ladder.md](references/mechanism-ladder.md).
4. **Test.** Run `rule-hypothesis-test`: held-out hit rate against 5 letter-swap controls, plus a Trial on a
   rig copy. Done when it returns PASS, FAIL or UNDECIDED with numbers.
5. **Ask speakers** only what the corpus cannot settle, as word judgements: "Is *X* a word? Does it mean *Y*?",
   minimal pairs ("*A* or *B*, which do you say?"), "these six behave alike; does *Z* belong?". Batches of 5-10, chosen by the
   answer's power to change the rule's scope or polarity, each carrying a private predicted answer. Questions
   never contain the words feature, class, MSA, rule, allomorph, slot or cap. Store answers as approved
   analyses and `reviewed-negative` judgments. Ask before encoding when an answer could flip polarity.
6. **Encode default-first.** When most evidenced members follow the rule, the rule is the default. Tag the
   minority by the encoding that tags the fewest objects among those FieldWorks can express: a stem exception
   feature (ProdRestrict) where the rule or allomorph can require or exclude it. FieldWorks affix restrictions
   take only positive requirements; where that forces the majority to carry the tag, record it in the
   hypothesis record and the GDR as a FieldWorks limitation and invent no negative mechanism (ADR 0112).
   Start permissive and restrict on speaker-rejected forms. A list is allowed only when a FAIL is on record
   for that shape, or the fact is lexical (TAKESMU case in
   [references/hypothesis-record.md](references/hypothesis-record.md)). Done when the encoding follows the
   verdict, the count of tagged objects is stated against the alternatives, and any FieldWorks limitation is
   recorded.
7. **Update the GDR in the same Proposal** and record. Follow
   [references/gdr-sync.md](references/gdr-sync.md): every rule or affix process this Proposal touches has its
   GDR section changed in the Proposal itself, with live examples. Read
   [references/black-guidance.md](references/black-guidance.md) before writing the section. Then append the
   hypothesis, tests, verdict and size delta to the ledger. Done when the Proposal carries the rule change,
   its GDR change and the ledger row together, Black's guidance was consulted, and the GDR names the
   mechanism rung chosen and why a lower rung did not do.
8. **Sweep weekly**: disable, Trial, bisect, delete over alternates, variants, one-host features and tags that
   a rule context already implies. Done when removed objects, identical parses and newly opened hypotheses are
   reported.

## Hard rules

- A batch of 5 or more same-shape objects needs a recorded rule attempt (its `rule-hypothesis-test` result and
  why it failed) before the batch.
- Gains are attested held-out types. Generated forms are reported as overgeneration exposure, never as gains.
- Names and loans from the aligned source NT are lexicon, reported apart from grammar progress.
- Size delta (objects added per attested held-out type gained) is reported for every package.
- The reviewer's first question is "what broader rule was tried, and was a neighbour merged?"

## Completion

A hypothesis is finished when its ledger row has a verdict (the ledger is future work, #174; until then
`hypotheses.jsonl` beside the project), its encoding follows the verdict (rule for PASS;
list only after FAIL or for a lexical fact; speaker questions queued for UNDECIDED), and its GDR matches the
rules as Trial left them. A brief for a hypothesis-first package uses
[references/brief-template.md](references/brief-template.md).
