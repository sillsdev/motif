---
title: How parser correctness is checked
description: How Motif checks parser integration and sample outcomes, and where grammar conformance runs.
---

Motif checks its parser integration and synthetic sample outcomes; PanGloss owns parser grammar conformance and parser-scale benchmarks.

## Sample outcome checks

Sample projects use a separate real-parser check. A test gated on PanGloss builds fixed and broken project variants, runs `motif assess` over their Texts, and checks the outcomes against `bugs.json`. The checks pin declared failure-word sets for failure bugs and parser-work increases for slow bugs; they do not establish a parser-reported cause for every declared bug. The checked-in `expected.json` records word counts, parsed counts, Text Coverage, and failing words for each bug.

When PanGloss is unavailable, the real-parser test skips. A non-gated check still verifies that sample specs build, patches apply, and the resulting projects reopen. Setting `MOTIF_SAMPLES_UPDATE_EXPECTED=1` deliberately refreshes the expected numbers after a reviewed change.
