# ADR 0030 — All automated testing is headless

**Status:** Accepted, 2026-11-14.

## Context

People and agents share the machine while tests run. Visible windows, real input and the real clipboard disturb
people and make tests depend on desktop activity. Layout geometry, reachability, contrast and automation checks
provide reproducible evidence without controlling the desktop. This applies to Motif and SIL.TextEditing in
`textediting/`, including installed-package checks.

## Decisions

### D1. Every automated test runs off screen

UI tests use Avalonia's headless platform. OS input, IME, clipboard, focus, screen readers and file dialogs pass
through seams that tests fake. No automated test may open a visible window, take foreground or focus, send real
keyboard/mouse input, read/write the real clipboard, or start another program that shows a window. Starting the
App headlessly in-process, including the `--smoke` start check, is allowed.

### D2. There is no opt-in path for real-window automated tests

No category, trait, preflight or environment variable restores a real-desktop test, including in a full solution,
nightly or release run. A guard fails test projects that use a real platform backend (`UseWin32`,
`UsePlatformDetect`, `UseX11`, `UseAvaloniaNative`) or real OS input/clipboard APIs. Real-input and real-clipboard
tests are converted to headless checks of the testable part or replaced by manual checklist rows.

### D3. Real-OS behavior is checked manually

Each behavior that only the real OS can demonstrate has a row in the
[TextEditing manual checklist](../../textediting/docs/manual-testing.md), or a Motif checklist when needed. A
person runs it and records the result. Agents do not launch the real App to verify a change; the owner looks at
it, or explicitly asks for that action. A capture requiring a real window happens only on a person's request,
outside tests and gates. Native caption, DPI, scrollbar, input or clipboard observations cannot be inferred from
WSL or headless success.

### D4. Headless assertions are the UI acceptance gate

For UI, layout, progress, shared-control and window-copy work, layout invariants, reachability, AA text/fill
contrast, clipped-text and automation-id checks determine READY, DONE and landing. The quick layout tier runs in
the normal developer loop; System coverage supplies the full grid according to the test/slow-run contracts.
Final-source validation runs `pwsh ./test.ps1 -All -Configuration Release` with `MOTIF_PANGLOSS_EXE` pointing to
the executable pinned in `pangloss-release.json`. Report source SHA, parser path/version, skipped categories and
any Windows-only follow-up. A missing parser, failed gate or absent final result means incomplete validation.

### D5. Captures are optional evidence for people, never pixel-diff gates

Leave `MOTIF_SCREENSHOTS` unset for batch gates and READY validation. Enable captures for requested visual review,
website media or Walkthrough video. Retain `tools/Capture-Screens.ps1`, the size matrix and Walkthrough
infrastructure. When captures are requested, regenerate, review and diff them and report completed capture states.
No test fails because a picture differs from an earlier picture. Missing captures alone never make validation
incomplete. A defect seen in a capture becomes a headless invariant so future runs catch it without a picture.

## Consequences

Full automated runs are safe while people work, including for the sealed textediting library. Fakes preserve
repeatable coverage; behavior needing a real OS moves to recorded manual checks. Separate desktops and CI-only
real-window tests are not exceptions. Headless rendering can still expose render-pass defects.

Screenshots let people judge appearance, readability and agreement with the FieldWorks guide. The automated gate
cannot catch a visual problem it has no invariant for; fixing such a problem includes adding that invariant.
Walkthrough media follows [ADR 0029](0029-help-walkthroughs-and-the-docs-site-come-from-one-source.md), and
installed-package replay follows [ADR 0031](0031-motif-ships-as-one-velopack-payload-per-runtime.md) under the same
headless restriction.
