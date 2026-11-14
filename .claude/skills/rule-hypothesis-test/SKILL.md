---
name: rule-hypothesis-test
description: >-
  Test a grammar rule hypothesis against letter-swap controls on held-out books and return PASS, FAIL or
  UNDECIDED. Use before encoding any new rule, process, feature or tag, before any list of 5 or more
  same-shape objects, and when reviewing a package that claims a rule was tried.
---

# Rule hypothesis test

A rule earns its place when its predictions beat chance on words nobody built it from. Chance is measured, not
assumed: the same prediction is rerun with other letters, and the rule must clear every one of them. This is the
smallest step of `grammar-discovery-loop`; that skill calls this one at its step 4.

## Inputs

- The hypothesis record (statement, mechanism, falsifier; see `grammar-discovery-loop/references/hypothesis-record.md`).
- **Held-out types**: whole NT books the discovery agents never read. Discovery types are the books mined.
- The stems the rule applies to, and the suffixes or contexts it fires in.

## Steps

1. **Predict beyond the targets.** List the forms the rule generates for stems *other than* the ones that
   suggested it. Done when at least 20 predicted forms exist, or the shortfall is recorded (the verdict is then
   UNDECIDED at best).
2. **Run the control test.** For a boundary change "stem-final X becomes Y before suffix S":
   `python3 scripts/control_test.py --stems … --suffixes … --change X:Y --heldout … --discovery … --json`.
   A hit is a predicted form attested in held-out and absent from discovery. The script reruns with 5 control
   letters in place of Y and prints rule hits, control hits, margin and a verdict. A rule that is not a
   one-letter boundary change goes through the same count: write its predicted forms, write 5 controls that
   change one thing (the letter, the environment, the suffix), and count held-out hits for each.
3. **Add the Trial.** On a rig copy, run a Trial of the encoded rule: attested losses, new readings on words that
   already parsed, work steps. Done when all three are reported; any attested loss is a FAIL until explained.
4. **Read the margin** with [references/reading-results.md](references/reading-results.md) (thresholds, the
   chance-match trap, minimum N), then write the verdict.

## Completion

Exactly one verdict, with the numbers that produced it:

- **PASS**: rule hits clear every control by the margin, Trial shows 0 attested losses.
- **FAIL**: a control matches or beats the rule, or an attested loss stands. Record it; a failed rule attempt
  is what permits a list for that shape.
- **UNDECIDED**: below minimum N or inside the margin. List the 5-10 word questions that would decide it
  (word judgements and minimal pairs only; `grammar-discovery-loop` step 5 asks them).

Append the verdict, counts and the script command to the hypothesis ledger before encoding anything.
