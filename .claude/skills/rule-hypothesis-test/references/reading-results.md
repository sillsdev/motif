# Reading a control-test result

Thresholds are provisional. The script defaults (20 hits, 2x the best control) come from two calibration cases;
re-fit them when a third case exists and write the new values here.

| Case (CEB audit, 2026-11-13) | Rule hits | Controls | Reading |
|---|---:|---|---|
| d>r before -an/-un, 732 d-final stems | 194 | 43-62 (l, n, g, t, h, s) | PASS: over 3x the best control |
| hC to Ch metathesis, 133 CVhVC stems | 42 | 23-50 (s 50, t 44, k 38) | FAIL: a control beats it |

- **Margin** = rule hits minus best control hits. Judge the best control, not the average.
- **Chance-match trap.** A predicted form can be a word by accident. For CVCVC syncope, 559 untagged stems
  matched a real word; the raw attestation count did not separate the rule from chance. Use the controls, and
  count only held-out forms absent from discovery.
- **Minimum N.** Under 20 attested hits the verdict is UNDECIDED, even with a large ratio. A rule is FAIL
  below that only when a control itself reaches the minimum and beats it.
- **Controls.** Same class as the target letter (consonant for consonant), nearest in place or manner first,
  drawn from the letters the language uses; override with `--control-letters`. Controls that are not plausible
  letters make PASS too easy. The calibration rows above used l, n, g, t, h, s; re-run d>r with the script and
  update the thresholds (#172) before trusting a PASS.
- **Exit codes.** 0 PASS, 1 FAIL, 2 UNDECIDED.
- **Attested only.** Generated forms that no book attests are overgeneration exposure; report them beside the
  hit count and never add them to it.
- **Absence is not a negative.** A predicted form missing from the held-out books is not evidence against the
  rule; only controls scoring higher, a Trial loss, or a speaker's rejection count against it.
- **Polarity.** If most evidenced members follow the rule, the rule is the default and the minority is tagged.
  d>r: about 95% of evidenced stems alternate (161 tagged + 28 untagged = 189 of 199, against 10 that keep d).
