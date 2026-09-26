# Documentation and Walkthrough content plan

**In plain terms:** this is the list of what Motif's documentation will say and which short clips it will
show, before any clip is built. The written documentation comes first. Clips are planned here and built
later, once the App tests another agent is writing have landed and we have picked which of them become
documentation Walkthroughs. Nothing here is a CI test yet.

Companion to ADR 0047 and `2026-09-26-generated-help-and-walkthroughs-plan.md`. Vocabulary follows
`CONTEXT.md` and ADR 0046's table: the window says "Review changes", "Apply to FieldWorks project", "No longer
fits"; the CLI and developer pages say Proposal, Dry Run, Preflight, Drift.

## Where the words live

| Kind | Source (English) | Translated by |
| --- | --- | --- |
| Guide pages | `help/en/guide/<slug>.md` | Crowdin, as Markdown |
| Command, control and term help | `help/en/{commands,ui,terms}.json` and `help/en/<kind>/<slug>.md` | Crowdin |
| Walkthrough captions | `help/en/walkthroughs/<id>.json` | Crowdin |
| Developer docs | `docs/` and `docs/adr/`, as they are | not translated |

Guide pages obey the same rules as Help pages: plain CommonMark, `cmd:`/`term:`/`ui:` links, and
`![alt](shot:<walkthroughId>/<stepId>)` for pictures. A `shot:` link to a Walkthrough that does not exist yet
renders as its alt text, so the pages can be written now and gain pictures later.

## Site outline

### Guide — for linguists using the window

**Get started**

| Slug | Page | Covers |
| --- | --- | --- |
| `what-is-motif` | What Motif does | Measuring a grammar, trying changes safely, applying them; how it relates to FieldWorks and PanGloss |
| `install` | Installing Motif | Installer, the parser, where Motif keeps its data |
| `open-a-project` | Opening a project | Choosing a `.fwdata`, what happens the first time, FieldWorks must be closed or saved |
| `first-run-setup` | Choosing what to measure | The setup dialog and the Default Selection |
| `reading-the-overview` | Reading the Overview | Headline numbers, stale numbers, Refresh |

**Pages of the window** — one guide page each

| Slug | Page |
| --- | --- |
| `overview` | Overview |
| `texts` | Texts: Text Coverage and choosing texts |
| `try-a-word` | Try a Word: parse one word and see why |
| `timing` | Timing: which words are slow to parse |
| `warnings` | Warnings: grammar findings and what to do about them |
| `review-changes` | Review changes: what applying would do |
| `ai-handoff` | AI Handoff: giving an AI agent the context it needs |

**Everyday tasks**

| Slug | Task |
| --- | --- |
| `refresh-numbers` | Bring the numbers up to date after FieldWorks saves |
| `change-an-analysis` | Change an analysis and keep it pending |
| `replace-a-pending-change` | Replace or remove a pending change |
| `apply-to-fieldworks` | Apply your changes to the FieldWorks project |
| `when-a-change-no-longer-fits` | A change that no longer fits, and how to bring it up to date |
| `switch-projects` | Switch between projects |
| `cancel-a-long-run` | Cancel a run that is taking too long |
| `when-something-goes-wrong` | Crash reports, diagnostics, and what to send |

**Concepts** — short pages, each linking its glossary term

Baseline, Assessment, Text Coverage, Default Selection, Drift ("no longer fits"), pending changes.

### For AI agents and scripts

| Slug | Page |
| --- | --- |
| `agents/start-here` | Using Motif from an agent: `motif help --all --json`, `--json` everywhere, `llms.txt` |
| `agents/output-and-exit-codes` | The JSON envelope, failure contract and exit codes |
| `agents/measure-a-grammar` | `open`, `baseline capture`, `assess`, `stats`, `overview` |
| `agents/work-with-jobs` | `jobs list/show/cancel/requeue/move` |
| `agents/handoff` | Reading a Handoff |

### Reference — generated

Commands (every Released command; Developer commands in a separate list), Terms (from `terms.json`),
API (from XML documentation), Exit codes.

### Developers — synced from `docs/`

Architecture, building and testing, the command catalog, contracts, every ADR, translating with Crowdin,
writing a Help page, writing a Walkthrough.

## Walkthrough catalog — planned, not built

Each is 15–30 seconds, silent, one caption per step. "Source test" names the existing App test whose flow it
would reuse; it is a starting point to reconcile once the new App tests land, not a commitment. "Parser"
says whether it needs PanGloss (the suite's fake parser, or a recorded result) to show anything.

| Id | Shows | Guide page | Source test | Parser |
| --- | --- | --- | --- | --- |
| `open-project-overview` | Open a project, land on the Overview, the headline number | `open-a-project` | `W1ChooseProjectAndCaptureBaselineTests` | no |
| `first-run-default-selection` | The setup dialog, choosing what to measure | `first-run-setup` | `W1ChooseProjectAndCaptureBaselineTests` | no |
| `refresh-stale-numbers` | Stale badge after a save, press Refresh, numbers update | `refresh-numbers` | `AssessmentWalkthroughTests` | fake |
| `texts-choose-texts` | Pick texts, see Text Coverage change | `texts` | — | fake |
| `try-a-word-parses` | Type a word, see its parse | `try-a-word` | — | fake |
| `try-a-word-why-not` | A word that fails, and the reason shown | `try-a-word` | — | fake |
| `timing-slow-words` | Sort by time, open the slowest word | `timing` | — | fake |
| `warnings-filter` | Filter findings, open one | `warnings` | `ConformanceGrammarWalkthroughTests` | no |
| `change-an-analysis` | Make a change; the "not applied yet" count rises | `change-an-analysis` | — | no |
| `replace-a-pending-change` | Replace a pending change with another | `replace-a-pending-change` | `PendingChangeReplacementWalkthroughTests` | no |
| `review-changes` | Open Review changes, read what applying would write and do | `review-changes` | — | fake |
| `no-longer-fits` | FieldWorks saves underneath; the change is flagged and Apply is blocked | `when-a-change-no-longer-fits` | `FieldWorksSimulatorWalkthroughTests` | no |
| `apply-to-fieldworks` | Apply, and the confirmation | `apply-to-fieldworks` | — | no |
| `ai-handoff` | Prepare a Handoff and copy it | `ai-handoff` | `HandoffWalkthroughTests` | fake |
| `cancel-a-handoff` | Start and cancel a Handoff | `cancel-a-long-run` | `CancelHandoffWalkthroughTests` | fake |
| `cancel-an-assessment` | Start and cancel a run | `cancel-a-long-run` | `CancelAssessmentWalkthroughTests` | fake |
| `switch-projects` | Switch to another project and back | `switch-projects` | `SwitchProjectWalkthroughTests` | no |
| `restart-where-you-left-off` | Close, reopen, pending changes are still there | `switch-projects` | `RestartAndSwitchWalkthroughTests` | no |
| `copy-diagnostics` | Open diagnostics and copy them | `when-something-goes-wrong` | `DesktopSeamWalkthroughTests` | no |

Terminal clips for the agent pages (`motif help`, `overview --json`, `jobs list`) are a separate, later
format — a text recording, not a window capture — and are not planned in detail yet.

**Order to build, once unblocked:** `open-project-overview` first (no parser, proves the engine), then the
remaining no-parser clips, then the fake-parser ones.

## Open questions to settle before building clips

1. Which of the new App tests should a Walkthrough reuse, and which Walkthroughs should stay separate so a
   test refactor never silently changes a documentation clip?
2. Is the suite's fake parser realistic enough for clips that show numbers, or do those need recorded
   results from a real project?
3. Which sample project do screenshots show — the seeded blank project, or a small, shareable demo
   language with believable words?
