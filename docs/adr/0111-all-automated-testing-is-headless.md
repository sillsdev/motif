# 0111. All automated testing is headless

**In plain terms:** tests never open windows on the desktop, take the keyboard or mouse, or use the real clipboard.
Everything a test checks runs off screen. What only a real screen can show, a person checks from a short manual list.

**Status:** accepted, 2026-11-12 (owner). Applies to Motif and to SIL.TextEditing (`textediting/`). Extends [ADR 0075](0075-headless-checks-are-the-ui-gate-screenshots-are-for-people.md):
there, headless checks are the UI gate; here, headless is the only way any automated test runs.

## Context

On 2026-11-12 a worker ran the whole SIL.TextEditing test solution on Windows. Its input-driver tests opened real
windows and drove real input, so windows kept popping up on the owner's desktop while the owner worked. The machine
is shared: people and agents work on it while tests run. A test that takes focus or input can also fail when someone
types, and it disturbs the person who is typing.

## Decision

1. **Every automated test runs headless.** UI tests use the Avalonia headless platform. Anything that reaches the
   operating system goes through a seam the test fakes: input, IME, clipboard, focus, screen readers, file dialogs.
   No automated test may:
   - open a visible window;
   - take the foreground or focus;
   - send real keyboard or mouse input;
   - read or write the real clipboard;
   - start a program that shows a window. A child process that runs headless, like the App's `--smoke` start, is allowed.
2. **No opt-in route for real-window tests.** There is no category, trait or environment variable that brings them
   back into a run, so a full-solution run is always safe on a shared desktop.
3. **What only the real OS can show is a manual check.** Each case gets one row in the manual checklist (textediting
   #67, and a Motif one if needed). A person runs it and records the run. The automated test is deleted or turned
   into a headless test of the part that does not need the OS.
4. **Agents never launch the real app to verify a change.** A headless test is the check. Looking at the change in
   the real app is the owner's step, or an explicit request.
5. **Screen captures follow ADR 0075 and are for people.** A capture that needs a real window runs only when a person
   asks for it, never inside a test run or a gate.
6. **Enforced by a guard test.** It fails when a test project uses a real platform backend (`UseWin32`,
   `UsePlatformDetect`, `UseX11`, `UseAvaloniaNative`) or calls real OS input or clipboard APIs.

## Consequences

- The tests that break this rule today (inventory of origin/main, 2026-11-12):
  - `SIL.TextEditing.InputDriver.Tests`: 11 tests that use real `SendInput` and `SetForegroundWindow`. They are
    gated only by a preflight.
  - `SIL.TextEditing.RealClipboard.Tests`: 3 tests on the real clipboard, gated by `TEXTEDITING_REAL_CLIPBOARD=1`.
    Decision 2 removes that gate.

  Each is converted to a headless test or replaced by a manual row. Motif's own test projects are already headless.
- Converting these tests is work. Some coverage of real Win32
  input moves from the automated suite to manual rows. It returns only through better fakes.
- Full-solution test runs are safe at any time, on any machine, while people work.
- Headless rendering finds render-pass bugs too. The `RichTextField` invalidation during render was found this way.

## Alternatives

- **Gate real-window tests behind a category or flag:** rejected by the owner ("all testing headless"). A gate is
  forgotten, and the next full run pops windows again.
- **Run real-window tests on a separate desktop or CI only:** there is no such machine today, and CI already runs
  headless.
