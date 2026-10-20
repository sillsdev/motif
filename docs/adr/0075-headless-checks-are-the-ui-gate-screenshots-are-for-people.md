# ADR 0075 — Headless checks are the UI gate; screenshots are for people

**In plain terms:** Motif decides whether a window change is right by running checks that measure the layout, colours
and controls without a picture. Screenshots are still taken, but only so a person can look at them. A screenshot never
decides whether work is ready or can land.

Status: accepted (2026-10-20). Amends ADR 0053 (see its 2026-10-20 amendment).

## Context

Screenshot tests became the expensive part of the UI gate. They ran for up to two minutes without output and were
killed as hangs, until each long case was split per theme and width (91d20f9ba). The pixel comparison was cut down
(c7cac5158) and screenshots were made opt-in, off by default (1403e23a7). By then the headless checks covered what the
pictures were guarding: layout invariants over every populated page, with the full window grid at System level
(d6705fab4), AA contrast of text and fills (cb681350b), and clipped text on every captured screen (588db5cb0), beside
the reachability and automation-id tests. Two documents (ADR 0053 and `docs/development-workflow.md`) still said a UI
change was not DONE without `MOTIF_SCREENSHOTS`, which contradicted AGENTS.md.

FieldWorks' [ADR 0003](https://github.com/sillsdev/FieldWorks/blob/main/Docs/architecture/adr/0003-geometric-assertions-not-pixel-diff.md)
makes the same choice: geometric assertions, not pixel diffs. It states that its tests do not check contrast. Motif's do.

## Decision

1. **The headless tests are the UI gate.** The layout invariants (quick tier in the default run, full grid at System
   level), reachability, contrast and automation-id tests in the normal test run decide whether UI, layout, progress,
   shared-control or window-copy work is READY, DONE and may land.
2. **Screenshots are opt-in.** `MOTIF_SCREENSHOTS` stays unset for batch gates and READY validation. It is set for a
   person's visual review (for example a Windows visual review), for website media and for walkthrough video.
3. **No pixel-diff gating.** No test fails because a picture differs from an earlier one. A defect a screenshot shows
   becomes a headless invariant, so the gate catches it next time.
4. **The capture harness stays.** `Capture-Screens.ps1`, the size matrix and the walkthrough infrastructure are kept.

## Consequences

A screenshot is still the way to judge what the checks cannot: whether a screen looks right, reads well and matches the
FieldWorks guide. Reviewers ask for captures when they want to look, and the report then lists the capture states. A
missing capture never makes validation incomplete. A missing parser, a failed gate or a missing final result still does.

A visual problem that no headless check can name is not caught by the gate. Such a problem is fixed and a new invariant
is added, rather than a pixel baseline.
