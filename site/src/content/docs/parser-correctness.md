---
title: How parser correctness is checked
description: How Motif's parser checks use synthetic conformance fixtures and pinned sample outcomes.
---

Motif checks parser behavior against known answers so changes to parse evidence can be reviewed. The checks use both a deliberately difficult conformance grammar and sample projects with expected results.

## Conformance fixture

The `deep-optional-affix-nesting` fixture is a synthetic FieldWorks project copied from Machine's conformance suite. Its grammar has twelve optional prefix slots, each inserting `x`. The word `k` and `xxxxxxxxxxxxk` each have one parse, while `xxxxxxk` has 924 parses: one for every way to choose six of the twelve slots. Machine marks this fixture as pathological under a fifteen-second budget.

This fixture checks that parser output remains correct when many optional paths converge. It is not a real language project, and the fixture's `SOURCE.md` records its provenance.

## Sample correctness pin

Sample projects use a separate real-parser check. A test gated on PanGloss builds the fixed and broken project variants, runs `motif assess` over their Texts, and checks the outcomes against `bugs.json`: the fixed variant parses every word, while the broken variant fails exactly the declared words and reports the declared reason. The checked-in `expected.json` records word counts, parsed counts, Text Coverage, and failing words for each bug.

When PanGloss is unavailable, the real-parser test skips. A non-gated check still verifies that sample specs build, patches apply, and the resulting projects reopen. Setting `MOTIF_SAMPLES_UPDATE_EXPECTED=1` deliberately refreshes the expected numbers after a reviewed change.
