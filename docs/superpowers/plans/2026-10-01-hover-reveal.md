# Hover reveal for secondary row actions: implementation plan

At rest, rows show their main information. Secondary links and menus appear when a person hovers, tabs into the row, or opens it, so every action stays reachable while pages are calmer to scan.

> **For agentic workers:** Execute this plan inline in the requested fix/hover-reveal worktree. Steps use checkbox syntax for tracking.

**Goal:** Use one token component to reveal secondary row controls by pointer hover, keyboard focus within the row, or the row open state.

**Architecture:** revealOnHover marks a secondary button or link; its hoverReveal Border host hides it with opacity at rest and restores opacity for hover, focus-within and the open state. Shared word rows and Analyze texts apply the classes; component-state tests cover each state and screenshots cover every affected page.

**Tech Stack:** C#, Avalonia AXAML, xUnit, PowerShell build/test scripts.

---

### Task 1: Pin the shared interaction contract

This test change proves the new class starts hidden without removing the control from keyboard or automation access.

**Files:**
- Modify: tests/SIL.Motif.Tests.App/App/ControlContracts/ComponentStateContractCases.cs
- Modify: tests/SIL.Motif.Tests.App/App/ComponentStyleTests.cs
- Modify: tests/SIL.Motif.Tests.App/App/WordRowControlTests.cs

- [ ] Add component states for opacity at rest, pointerover, focus-within and open using revealOnHover beneath hoverReveal.
- [ ] Add a focused test that the hidden control remains visible, tab-stoppable and named, and reveals after keyboard focus.
- [ ] Add a shared WordRow assertion that its three named next steps stay tab-stoppable while hidden at rest.
- [ ] Run the repo test script with the required sandbox environment and confirm the new hidden-opacity expectations fail before production edits.

### Task 2: Apply the one shared component rule

The component owns reveal behavior, while each row only marks its host and secondary controls.

**Files:**
- Modify: src/SIL.Motif.App/Tokens/Components/HoverReveal.axaml
- Modify: src/SIL.Motif.App/Views/WordRow.axaml
- Modify: src/SIL.Motif.App/Views/ResultsInTextPanel.axaml

- [ ] Extend the hoverReveal component selectors so revealOnHover controls use the hidden-opacity token at rest and the visible-opacity token for pointerover, focus-within and open.
- [ ] Add hoverReveal to the shared word row and revealOnHover to Open in text, Try a Word and Word Analyses.
- [ ] Add revealOnHover to Analyze texts' per-word Fix menus and retain the visible primary marking action. Keep the word card's existing extra links on the same class.
- [ ] Run ./build.ps1 and ./test.ps1; preserve any unrelated failures in the report.

### Task 3: Capture the affected places

The captures show the page with its secondary controls resting and the same controls after pointer hover.

**Files:**
- Modify: tests/SIL.Motif.Tests.App/App/PageScreenshots.cs
- Modify: tests/SIL.Motif.Tests.App/App/StateScreenshots.cs

- [ ] Add a resting screenshot for What changed and hover captures for Lists, Timing, Review changes and What changed; Matrix and Analyze already have hover stages.
- [ ] Run the screenshot capture with MOTIF_SCREENSHOTS set to _briefs/fix-shots/lane-hover-reveal/, MOTIF_DEVELOPER_COMMANDS=1 and the pinned PanGloss executable.
- [ ] Inspect representative rest and hover images at both widths and themes.

### Task 4: Report the inventory and finish

The report gives B6 and B10 the class name and records controls that stay visible by design.

**Files:**
- Modify: _briefs/report-lane-hover-reveal.md

- [ ] Record the per-page inventory, including the visible Review Undo and Check again actions as an owner question.
- [ ] Record test results, the screenshot folder and any visual limitations.
- [ ] Commit the completed plan, tests and implementation in small local commits. Do not push or merge.
