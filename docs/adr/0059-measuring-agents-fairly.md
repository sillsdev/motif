# ADR 0059 — Measuring agents fairly

When we change what Motif tells an AI assistant, we decide whether the change helped by running real
grammar tasks on cheap and big models and comparing the results, and we only count failures that are the
agent's fault. The answers it is checked against are written and attacked by the strongest models we
have, checked by a person, and versioned, so a reported improvement is one we can trust and repeat.

**Status:** accepted, 2026-10-09. Amends the 2026-10-03 agent MCP and A/B design. Terms (Episode, Arm,
Hold, Oracle, Judge, the failure classes, Agent budget, Price to solve, Validity) are defined in the
glossary's "Evaluating agents" section.

## Context

The 2026-10-09 review of the A/B harness found its numbers could not yet be trusted:

- A grader that crashed was labelled `infrastructure_failure`, so a broken grader and a cloud outage both
  dropped Episodes from the count instead of one being scored and the other fixed.
- Lexicon grading hard-coded a word, searched the after-state JSON as text, looked targets up in the
  agent's own transcript, and failed answers whose operation count differed from the key's.
- The validity fingerprint covered tasks and graders but not the product binaries, parser or profiles,
  and the comparison arm reused the other arm's binaries.
- Cost per success excluded the cost of failed Episodes.
- The Luna judge graded Luna's own answers.

Research limited to sources from July 2025 onward (Anthropic's "Demystifying evals" of 2026-01-09,
GEPA, ACE, Meta-Harness, Self-Harness) shows current practice is a short loop of transcript reading,
smallest change and paired re-runs on a few dozen tasks, not large-sample significance testing.

## Decision

### What counts

- **Agent failure** is scored: a wrong, unappliable or unfinished answer, or running out of Agent budget.
- **Cloud failure** (the model service errored or stalled) is retried automatically and not counted.
- **Harness defect** (our grader, task or jail is wrong) is not counted; the task leaves the suite until
  it is fixed.

Attribution of a time-out follows where the time went. The Agent budget (time, turns, tokens) pauses while
the model service is stalled; 120 seconds with no output is a stall; a wall-clock cap of three times the
budget ends the Episode regardless.

**Price to solve** — total spend on scored Episodes divided by the number solved — is the headline cost,
with average cost per Episode beside it.

### Validity

Validity binds results to everything that can change an answer: the product binaries, the parser, the MCP
profiles, the shipped skills, the task set and the graders, as one fingerprint over their full contents.
Each Arm builds and fingerprints its own binaries. Validity is re-established automatically whenever any
input changes; a run on a stale fingerprint is refused. Lexicon answers are graded by reading the project
back on the control side; operation count is reported separately and never fails an answer.

### The Oracle

The Oracle is a versioned recipe: a reference library (`docs/references/`: linguistics, FieldWorks and
industry sources, as summaries, citations and excerpts, full text only when openly licensed), named
models at named effort, and written rules. It writes task prompts and answer keys and issues rulings; lanes
build the starting grammars. Every result names the Oracle version it was graded against.

- Big-tier models (Opus and Sol) rule independently first, then get one rebuttal round with evidence.
- A devil's advocate attacks every key before it is accepted; each surviving attack becomes a control in
  that task.
- The parser settles questions of fact. A linguistic disagreement goes to a person in a grill session,
  and a person spot-checks a sample of accepted keys; the count of human checks is reported.
- The claim is "agrees with the Oracle", never "correct". The model under test may sit on the Oracle;
  the result says so.

All Oracle roles load the `linguistic-consultant` skill, so they reason as the same expert.

### The Judge

The Judge is a fixed pair of big models from different families (Opus and Sol) that judge each answer
independently, quoting the answer. Agreement is the verdict; disagreement leaves the answer unscored until
a person rules. The same pair judges every Arm, and the model under test is never on it. The pair
qualifies against the Oracle's rulings before it judges a live comparison. Stored runs judged by the old
single judge are re-graded.

### The loop and the acceptance rule

Each round: sort the failures in transcripts by type; make the smallest change that addresses the
largest type; screen it on 4–6 affected tasks × 3 Episodes × 2 Arms; run the full suite of 21 tasks ×
3 × 2 = 126 Episodes; confirm on 5 fresh Episodes per Arm, never more than 10 per Arm per task.

A change ships only if all five hold:

1. The comparison was valid: frozen keys, matched conditions, Judge disputes resolved.
2. It introduces no critical contract violation, such as an unauthorized edit or an invented identity.
3. The fix holds on fresh evidence: at least two affected tasks, or the original task and a new
   counterexample.
4. No unexplained regression on the full suite, for every model the profile serves.
5. A visible gain: for a correctness fix, the failure is fixed; for an efficiency fix, correctness is
   unchanged and time or token use falls by at least 20%.

Otherwise the change is a **Hold**, recorded with the evidence that would settle it. Running more
Episodes until a change wins is not a way out. A change that helps one model and hurts another becomes a
separate profile, not an averaged win.

### Models and tiers

- **Cheap tier** gates every change: Haiku 5.5 by default, run as an ordinary `claude -p` session on the
  owner's subscription, and Luna through the ChatGPT plan. Before the first A/A run (126 Episodes, two
  identical Arms) each route passes a health check: 3 tasks × 1 Episode with no Cloud failure, the billing
  plan recorded, network evidence that only the allowed hosts were reached, the full tool list visible, and
  the median Episode time recorded. If Haiku's route fails, Luna runs and the record says why.
- **Big tier** runs once per release: 21 tasks × 1 Episode on Opus and on Sol. A task that passed at the
  previous release and now fails, or time or tool calls up more than 20%, triggers a full comparison.
  In the big-tier check one judge would be the model under test, so the other family's model judges
  alone and the record marks the verdict "single judge". Every failure, and every task whose outcome
  changed since the previous release check, goes to a person. The check only screens for regressions;
  it never accepts a change.

For Claude Episodes the jail's network allow-list is the inference broker plus Anthropic's API host; the
subscription credential is present in the jail, the answer keys never are.

### Tasks

The 21 current tasks are the development and regression suite. The next new situations (including two
cross-project tasks: copying entries between projects, and the same affix change in two projects) are
kept fresh until a change needs confirming. A private held-out set with its own starting grammars is the
final check. Affixes, MSAs, slots and templates come first.

After about three manual rounds, an agent may propose changes automatically from failure clusters; it may
edit only profiles, tool descriptions and guides, never tasks, keys, graders
or the Oracle, and a person approves every change that ships.

## Consequences

- Live comparisons stay paused until the failure classes, read-back grading, full fingerprint and Price
  to solve are built, and until the Trial result and Draft Dry Run of ADR 0058 exist.
- The broker gains no Claude API-key dependency for routine runs; the API-key route remains for CI.
- Shipped skills are part of an Arm: a skills change is measured like any other change.

## Rejected

- **Confidence intervals over hundreds of tasks.** Not current practice at this suite size, and it
  rewards running more Episodes rather than reading transcripts.
- **A single judge, or one from the family under test.** Self-preference bias; the review found it in the
  stored runs.
- **Calling the Oracle "ground truth".** It is a recipe that can be wrong; versioning it is what makes a
  correction possible.
