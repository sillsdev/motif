# 0009. Assistants reach Motif through one agent API

**Status:** Accepted, 2026-11-14.

**Amended 2026-11-17:** Hand-analysis annotation and its full-fidelity Home boundary.

## Context

An assistant needs to discover a linguistic Composer, draft a change, measure it, read what happened and
revise before handing it over. The command line and MCP must teach the same workflow. Handoff supplies
context to other chat clients; a bundled tunnel lets ChatGPT reach the same local server.

## Decisions

### D1. One catalog defines the CLI and MCP renderings

The agent API consists of Released and AdvancedAi catalog commands whose agent class is not HumanOnly.
Autonomous commands, Developer commands and HumanOnly effects do not become tools. CLI verbs and MCP tools render one definition
and response, including the same refusal code, reason, message, detail and next step. No capability or
interpretation belongs to one rendering alone. Every command-bearing skill and guide shows both routes.
Descriptions derive from catalog metadata, Help Title/Description and request-field documentation, never
handwritten copies. Glossary terminology binds tool descriptions too; profile verbosity reaches every
handler. There is no separate tool-group design.

Apply, discard and store deletion remain unavailable to MCP. “Finalize” is used consistently, not finish,
submit or commit. Individual older drafting/removal commands have AdvancedAi exposure; the default profile
consolidates them under the tools below rather than exposing a primitive route.

`discard-draft` and `discard-empty-drafts` hard-delete Drafts that were never finalized. They stay HumanOnly with no
MCP tool, and they refuse the pending-changes Draft, which belongs to the person's window. They sit on the Autonomous
audience, so an autonomous grammar loop that probes with throwaway Drafts reaches them with `MOTIF_AUTONOMOUS=1` or
`--autonomous`, and the Autonomous playbook carries a probe-loop note. The Autonomous audience is a catalog surface of
its own beside Released, Advanced AI and Developer: it holds the commands an autonomous run drives (the Draft
lifecycle, the hypothesis ledger), is listed in help with its switch, and, like Developer commands, is never a tool.
`MOTIF_DEVELOPER_COMMANDS` stays for true developer diagnostics only; a refusal for a command that moved names the
Autonomous switch.

All Proposal writes continue through the one Apply orchestration and its evidence gates. Hand analysis has
its own bounded semantic annotation command, shared by CLI and MCP, with no Proposal or Apply Receipt.
An Annotation command class permits only these immediate actions and conveys no grammar Apply power.
CLI can record explicitly asserted speaker or analyst intent. Audit names the actual caller and supplied
source; it must never relabel an agent as a person. Automatic parser intake approves nothing.
MCP describes annotation as an immediate write and requires the host's ordinary write confirmation.
Supplied labels are not verified identity, and an MCP caller cannot claim a person as its operator.

### D2. The default profile has seven always-loaded tools

| Tool | Effect | Role |
| --- | --- | --- |
| `motif_projects` | read | List Known projects |
| `motif_read` | read | Project `view`: overview, grammar, lexicon, word, texts, proposals |
| `motif_find_composer` | read | Find a Composer by linguistic goal/Subject with schema, example and Subject Help |
| `motif_try_word` | read | Read a word against the project, or a Proposal's collected Trial result |
| `motif_results` | read | Page a Proposal's Trial results by aspect |
| `motif_proposal` | write | `action`: start, rationale, trial, finalize, withdraw |
| `motif_compose` | write | One or more Composer steps and operations to remove |

Server instructions begin with a short complete map. Reads never write, preserving hosts' write-confirmation
behaviour. Each action/view accepts only its arguments; irrelevant arguments refuse. A single compose step
and a batch use one call. The full evaluation profile exposes one tool per Composer and the older separate
tools to measure equivalence; it is not required for normal sessions and no other profile is retained.
Request and Comment capabilities remain on the shared catalog under ADRs 0007 and 0008.

The completed hand-analysis surface adds a distinct annotation tool to the default profile; it never
hides live writes behind the Draft compose tool. Until the writer and Home prerequisites are implemented,
only the CLI read-only check is exposed; there is no hand-analysis MCP route.

### D3. Advanced AI mode gates all tools

With the mode off, `motif mcp` can start but offers no tools; a call explains how to enable it. Enabling the
mode in the window also connects assistants. An overview names missing prerequisites such as no Baseline,
no worker or mode off, rather than presenting plausible empty project data.

### D4. Every call names a Known project; the server has no current project

The MCP server starts without `--project`. Each project-specific tool takes `project`, identifying a Known
project; project discovery lists them. Only a person adds a Known project through Motif's open action or
CLI; no agent tool adds one. One conversation may work across projects, but a Proposal belongs to exactly
one. Repeating a change in two projects makes separate Proposals, each Dry Run, Trialled, Finalized and
Applied independently, with each Rationale naming the other.

### D5. Draft measurement is bound to content and Baseline

A Draft can be Dry Run and Trialled. Evidence names its content digest and the project Baseline and counts
only for that exact pair. Finalizing unchanged content preserves the evidence. Applying a different
Proposal changes the project, making older evidence stale. There is no merge other than Apply.
Stored evidence lacking required identity keys is refused, not silently reinterpreted.

The assistant loop is draft, Dry Run as part of Trial, inspect results, revise, Finalize. The workflow
resource and skills are generated or tested against the real tools. Finalize is not a prerequisite for
checking a Draft.

### D6. A Trial collects the Proposal's evidence once

A Trial includes Dry Run, grammar warnings and Parsimony, measured-word parses and timings, standing
against human decisions, changes from Baseline, and parser explanations for changed or still-failing
words. A repeat creates a new Trial. Result reads compute nothing live on the Proposal. `motif_results`
offers `summary`, `warnings`, `changes`, `decisions`, `parsimony`, `timing`, `word` and `object`, all paged
as needed. An uncollected aspect says **not collected**; a word outside the Trial says **not measured**
and explains how to include it. A Proposal word read uses collected evidence, not a fresh parse of its
scratch copy. Tracing and inspecting the current project remain available.

Inline Trial summary orders: words that should not parse but now do; words that parsed but no longer do;
cases the parser did not finish; other changed analyses; counts. Incomplete work is never called rejection.
The full Difference is paged by category. A project Assessment may also be run without a Proposal.
Readiness uses current, exactly bound evidence; latest-Trial selection and report shape follow ADRs 0020
and 0021 rather than an MCP-specific rule.

### D7. Find Composers by Subject and linguistic goal

Discovery uses `motif_find_composer`, and authoring `motif_compose`, without depending on host tool search.
A Composer's linguistic-intent name is its CLI verb and compose-step `verb`. Subjects form a closed catalog:
grammar Subjects are Phenomena (sound inventory, natural classes, rules/environments, conditioned
allomorphy, templates/slots, agreement/features, noun class/gender, inflection classes, stem alternation,
infixation and reduplication); other Subjects cover lexicography, description, texts/analyses and Parsimony
rulings. A new Subject arrives with its first Composer and Help page explaining order and pitfalls.

Each Composer supplies translatable example goals searched with its Title and Description. Missing Subject,
Help or example goals fails the build. A Composer is never language-specific. Agents never stage Layer 0
primitives, including behind a Trial; no gated escape route exists. The long tail is served by adding
Composers with linguistic policy, under ADR 0013.

### D8. A Composer is declared once and verified through the real write path

When existing operation kinds suffice, add the Runner intent record, closed parser and lowering step;
refuse unknown properties and compare IDs by identity. Add one Commands definition with verb, provenance
name, closed field shape, exposure, Subjects, replacement rules, batch label and related objects as needed.
Add Help Title/Description, field documentation, two to four language-independent goals and a Help page.
Tests cover positive cases, refusals, Dry Run and Apply read-back on a blank project, and real-parser
behaviour when parsing is the point.

Generate CLI usage/dispatch, catalog entry, compose membership/schema, discovery results, full-profile tool
and descriptions from that definition. A new operation family also needs closed schema, semantics,
validation/lowering, preview effects, Apply/read-back, conflict/update-baseline behaviour, snapshot/diff,
positive/negative/rollback/round-trip/conformance coverage and a manifest row. New Composers should take days,
not weeks, without weakening those requirements. Test inputs follow
[ADR 0002](0002-grammar-data-never-belongs-in-motif.md).

### D9. Responses have consistent names and actionable refusals

Tools use `motif_<verb>` or `motif_<verb>_<noun>` and camelCase arguments; `proposal` and `project` mean the
same thing everywhere. Return IDs exactly with prefixes; `detail` is concise by default or detailed.
Responses above about 4000 tokens page instead of inlining. Proposal label, Rationale, Comments and
operation citations follow [ADR 0007](0007-a-proposal-has-one-lifecycle-a-conversation-citations-and-one-apply-path.md).
Every Proposal response says whether a person commented since Rationale was set and tells the agent to
read it when needed; no server-initiated turn is assumed.

Refusal codes come from one closed registry mapping reason, hint and next step across CLI, MCP and Help.
Name the missing precondition and the call that satisfies it in the caller's rendering. A read unable to
look must refuse with why, never return zeros or an empty list as if it looked. Obvious successful next
steps appear in `next`.

### D10. Command material in skills is generated; playbooks are authored once

Generate command routes on both renderings, the Subject index and CLI/tool crosswalk. Hand-author one
playbook: choose a Subject, cite source, start with a Rationale, read the person's Comments, compose, Trial,
read results, Finalize and record evidence, explaining how returned IDs feed later calls. Build validation
rejects unresolved tools, verbs and Help links.

### D11. Surface changes are versioned and measured

Before 1.0 renames have no aliases or deprecated arguments; post-1.0 stability is a later decision.
Release notes include generated tool-list differences. Composer retirement/merging is free because stored
Proposals contain operations and only name Composers as provenance.

Each Subject has a small public-test-grammar task across differing language types, evaluated by
[ADR 0010](0010-measuring-agents-fairly.md). Record calls/errors, turns, tokens, wall time, refusal codes
and whether the expected Composer was found. Grade the resulting grammar, never the call sequence.
Real-session activity stays local and records tool, Composer, refusal code and whether the next call used
the tool named by the previous `next`; arguments are not compared. Read earlier log lines with missing
fields blank. `motif activity` reads the named session log; a default log for every session is undecided.
Nothing is sent elsewhere.

### D12. Handoff has a small core with explicit exceptions

The flat core is `grammar.json`, `texts.json`, `assessment.json`,
`parse_grammar_texts_assessment.py` and `handoff.md`. A missing Assessment is valid: omit its file and say
so. All selected Texts share one `texts.json`, keyed by GUID/title, rather than a directory per Text.
The `.fwdata` never travels; this Handoff serves grammar fixes rather than arbitrary project editing.
JSON files are valid whole JSON with one compact record per line, pretty-printed around records so `grep`
returns a whole record and `json.load` works. The Python helper uses only the standard library and provides
filtering, trace routines and `--help`. Open Requests add `requests.json` under
[ADR 0008](0008-requests-how-a-person-asks-claude-for-a-proposal.md); traces have the explicit exception below.
“Five core files” does not forbid these additions or require an absent Assessment.

### D13. Orient the chat reader briefly and the person before sending

`handoff.md` is at most 100 lines: orientation, basic manifest, what each file is, one grep example and one
Python call per file. Full explanations live in the helper's Help and pinned format documents. The window
generates a pasted header per run, about five bullets, naming language and project, explaining FieldWorks
to a new reader, the questions “why did this word not parse?” and “why is parsing slow?”, and pinned links.
No additional chain-of-custody account is required for the chat reader. State what leaves the machine in
the window beside the drag tiles, where the person chooses to send it, not inside the file afterwards.

### D14. Format documents stay with their owner, linked at a pinned ref

PanGloss owns `docs/formats/grammar-format.md`, `trace-format.md` and `hc-mechanics.md`, with an outside-reader
README. Motif owns `docs/handoff/flextext-json-format.md`, `assessment-format.md` and its parser-help material.
Do not copy full format explanations into every Handoff. Links serve raw Markdown, pinned to a release tag
or branch when no release exists, never rendered blob pages. Official releases require a git tag cut
before release. Acceptance checks that a fresh model can actually fetch these documents; missing remote
documents cannot be replaced by assuming the model already knows the formats.

### D15. Measured parsing and explanatory tracing retain distinct meanings

An Assessment collection has a merged timed batch phase, then selected traced/unmerged word re-parses in
the same logical run. All comparable timings come from the batch; tracing may amplify the search and is
bounded separately. Selection includes typed words, Texts and entries; typed words need not exist in the
project. Trace choices include typed words, the N slowest and all words of selected Texts. Show count and
cost (parser steps with measured time); impose no fixed trace-count ceiling. When narrowing, prefer typed
words, then failures/limit-hits, then slowest words.

Publish the retained invocation after the batch and before traces. Traces join as available. Cancelling
traces preserves the Assessment marked partial with counts. A trace's own cap/timeout cannot lose batch
evidence. Keep the full parser tree when supplied, with per-word outcome, recorded steps, completion,
distinct failure reasons and deepest named rule; do not prune to presumed important branches. Linked or
inline trace material is addressed from the word's Assessment record. The contained invocation contract
belongs to ADR 0019.

Do not promise partial trees from a killed process: the parser writes its tree only on completion.
A clean exit does not prove that a hidden step cap was not reached. Without an explicit parser completion
signal, report only what is known, never `complete` by inference; batch tracing remains an upstream
capability rather than a second public run.

### D16. One-word Handoffs preserve the diagnostic and its Baseline

A fresh one-word Assessment Handoff measures, then traces against the retained grammar. It adds
`traces/<encoded-word>.trace.json`, including Motif host capture; `assessment.json` references it and its
summary. `handoff.md` names the encoded file and its Python example tries that filename beside the other
files before the nested location, supporting flat chat uploads. Try a Word instead exports the displayed
diagnostic unchanged with its captured Baseline via the private reader, even if a newer Baseline exists.
That route runs neither another Assessment nor another trace and omits `assessment.json`.

Preserve PanGloss fields and summarize only reason codes actually in the tree, not neighbouring events.
Use `completion: "unknown"` for clean exit without cap information, `incomplete` for an explicit stop and
`not-run` for an invalid word shape. **Copy for a chat model** includes instructions, one-line summary and
the complete diagnostic. Released `motif trace` and `motif trace --load` have Help and the same typed
interpretation for live and saved diagnostics.

### D17. ChatGPT reaches local stdio MCP through a supervised bundled tunnel

Ship pinned OpenAI `tunnel-client` with adjacent cloudflared in the per-runtime Velopack payload. On explicit
Connect, Motif launches its hidden uncatalogued `motif tunnel-supervise` for each `init` and `run`. It
forwards tunnel output; tunnel-client wraps adjacent `motif mcp` using a private temporary local-stdio
profile. Do not expose the machine as a separate public server or run two clients for one tunnel ID:
replicas cannot route stdio sessions.

On Windows the supervisor enters a kill-on-close Job before starting the tunnel, which inherits membership.
On Unix it creates a separate session/process group first; Linux also makes it a child subreaper. Watch
the owner Motif PID and process-start identity. Owner death, closed input, tunnel exit, Disconnect,
desktop-session close and unexpected supervisor exit invoke cleanup. On Unix send group SIGTERM then
SIGKILL after a grace period only while the recorded leader's start identity matches. If unverifiable,
enumerate and signal only members whose resolved executables are inside the recorded bundle directory.
Linux uses `/proc` groups/executables and kernel start ticks; macOS uses `proc_listpgrppids`/`proc_pidpath`
from [Apple libproc](https://github.com/apple-oss-distributions/xnu/blob/main/libsyscall/wrappers/libproc/libproc.h).

Keep a locked run record per owner with owner PID/start, tunnel PID/start and actual executable; Unix also
records group, leader identity and resolved bundle directory before tunnel start. Startup and pre-Connect
recovery stop only a dead owner's matching group, independently of the current install path. Never
interrupt a live owner. With verified leader, retain the record until no live group member remains; with
unverifiable leader, leave unrelated members alone and drop it when no verified bundled executable remains.
On Windows the Job kills the supervisor/tree. Tunnel-client may restart a failed stdio child; Motif never
automatically restarts a failed tunnel. A later Connect starts fresh.

### D18. Tunnel credentials and package inputs have explicit boundaries

The runtime key has Tunnels Read and Use access. Store it only in Windows Credential Manager, macOS
Keychain or Linux Secret Service. Pass it via supervisor to the tunnel in `CONTROL_PLANE_API_KEY`, never
arguments, Motif files or logs. If Linux libsecret/Secret Service is absent, hold it only in memory and
explain that re-entry is needed. Profiles name the variable, never embed its literal value.

Pin every supported archive and verify SHA-256 before extraction; a missing pin blocks packaging. Keep
both executables adjacent. Include tunnel-client's Apache-2.0 licence/NOTICE and cloudflared's licence
information and upstream third-party report. Upstream contracts are the
[OpenAI secure MCP tunnels guide](https://developers.openai.com/api/docs/guides/secure-mcp-tunnels) and
[tunnel-client configuration](https://github.com/openai/tunnel-client/blob/master/docs/configuration.md); packaging cannot assume an unpinned installed client.

## Open

Live account validation has not established eligible ChatGPT plan tiers, Plus/Pro write-tool permission,
desktop-app connector support or caller identity visible to Motif. Plan and workspace policy may affect
availability. This ADR does not claim a hands-on account/tunnel test. Post-1.0 tool stability and a default
activity log remain undecided.

## Consequences

The same semantic work is discoverable and readable through CLI, local assistants and the remote connector.
Trials remain the sole source of Proposal evidence, while Handoff carries enough context for other chat
clients. Host limitations produce explicit missing/unverified results rather than plausible answers or
invented trace completeness. Credentials and supervised processes remain bound to their owning installation.
