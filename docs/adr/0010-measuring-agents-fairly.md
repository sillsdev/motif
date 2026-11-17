# 0010. Measuring agents fairly

**Status:** Accepted, 2026-11-14.

## Context

An agent-surface change is useful only if matched real grammar tasks show a repeatable gain. Broken grading,
cloud outages, stale product inputs and self-judging models must not masquerade as agent performance.

## Decisions

### D1. Score agent failures; separate cloud failures and harness defects

A wrong, unappliable or unfinished answer, or exhausted Agent budget, is a scored agent failure. A cloud
service error/stall is retried automatically and excluded. A grader, task or jail defect removes the task
from the suite until fixed; it is not an agent failure.

The Agent budget is turns (model requests counted by the inference broker for every host), tool calls
(counted at the MCP bridge, excluding repeated running-job polls), and tokens (input with cached input
counted once, plus output, as the provider reports). Default token budget is 1,500,000. Time is not budget:
host load must not make slow tools an agent failure. No upstream answer for 180 seconds is a Cloud failure.
A wall-time safety kill is at least an hour; hitting it is a Harness defect, with the Episode discarded
and rerun once. Record host load at start and end. Episodes scored under a different token-count/budget
rule are not comparable.

### D2. Price to solve includes failed scored Episodes

Headline **Price to solve** is total spend on scored Episodes divided by solved Episodes. Report average
cost per Episode beside it; excluding failed attempts would understate the price.

### D3. Validity fingerprints every input capable of changing the answer

Hash full contents of product binaries, parser, MCP profiles, shipped skills, tasks and graders as one
fingerprint. Each Arm builds and fingerprints its own binaries. Re-establish validity automatically on any
input change; refuse stale runs. Grade lexicon answers by control-side project read-back, not transcript
search or a hard-coded word. Report operation counts separately; a count differing from a key is not
failure when the resulting grammar is right.

### D4. The Oracle is a versioned expert recipe, never ground truth

The Oracle includes the reference library in `docs/references/` (linguistics, FieldWorks and industry
sources as summaries, citations and excerpts, full text only when openly licensed), named models at named
effort and written rules. It writes prompts/keys and issues rulings; lanes build starting grammars.
Every result records its Oracle version. Inputs and licences obey
[ADR 0002](0002-grammar-data-never-belongs-in-motif.md).

Big-tier models Opus and Sol rule independently, then have one rebuttal round with evidence. A devil's
advocate attacks every key; surviving attacks become task controls. Parser results settle facts. Linguistic
disagreements go to a person in a grill session; a person spot-checks accepted keys and the count is
reported. All Oracle roles load `linguistic-consultant`. Say **agrees with the Oracle**, never **correct**.
A model under test may sit on the Oracle only with that participation disclosed.

### D5. A qualified cross-family pair judges every Arm

The Judge is a fixed pair of big models from different families, Opus and Sol, independently judging and
quoting each answer. Agreement is the verdict; disagreement leaves it unscored until a person rules.
The tested model is never on the pair. Qualify against Oracle rulings before a live comparison, use the
same pair for both Arms and re-grade stored single-judge runs.

### D6. A short paired loop and fresh confirmation decide whether a change ships

Use a paired t interval over task means, or set/language cluster means with at least two clusters,
calibrated by an A/A false-positive test. Read transcripts and group failures; make the smallest change
addressing the largest type. Screen 4–6 affected tasks × 3 Episodes × 2 Arms, run the 21-task suite × 3 × 2
(126 Episodes), then confirm with five fresh Episodes per Arm, never more than ten per Arm per task.

All five conditions must hold:

1. Frozen keys, matched conditions and resolved Judge disputes make the comparison valid.
2. There is no critical contract violation, including unauthorised edits or invented identities.
3. Fresh evidence confirms the fix on two affected tasks, or the original plus a new counterexample.
4. The full suite has no unexplained regression for every model served by the profile.
5. A correctness failure is fixed, or an efficiency change preserves correctness while reducing time
   or tokens at least 20%.

Otherwise record **Hold** with the evidence needed to settle it. Do not keep sampling until a win appears.
A change helping one model while harming another needs separate profiles, not an averaged win.

### D7. Cheap models gate changes; big models screen releases

Cheap tier gates every change: Haiku 5.5 by default as an ordinary `claude -p` subscription session, and
Luna through the ChatGPT plan. Before the first 126-Episode A/A, each route passes three tasks × one Episode
with no Cloud failure, billing plan recorded, network evidence limited to allowed hosts, full tool list
visible and median Episode time recorded. If Haiku's route fails, use Luna and record why.

Big tier screens each release with 21 tasks × one Episode on Opus and Sol. A previously passing task now
failing, or time/tool calls rising over 20%, triggers a full comparison. When a Judge would itself be the
tested model, the other family judges alone, explicitly labelled **single judge**. Every failure and
changed task outcome goes to a person. This screen never accepts a change.

For Claude Episodes the jail permits the inference broker and Anthropic API host; subscription credentials
are present and answer keys absent. Routine runs add no Claude API-key dependency; that route remains for
CI.

### D8. Development tasks and fresh confirmation inputs remain separate

The 21 tasks form the development/regression suite. Keep new situations fresh until confirmation, including
cross-project entry copying and the same affix change in two projects. A private held-out set with its own
starting grammars is the final check; affixes, MSAs, slots and templates come first.

After about three manual rounds, an agent may propose profile, description and guide changes from failure
clusters. It may never edit tasks, keys, graders or the Oracle; a person approves each shipped change.
Shipped skills are Arm inputs and are measured like any other surface change.

### D9. Live comparisons require trustworthy infrastructure

Keep live comparisons paused until failure classification, control-side read-back grading, full validity
fingerprinting, Price to solve and the Draft measurement/Trial-result capabilities of
[ADR 0009](0009-assistants-reach-motif-through-one-agent-api.md) are available. This is a prerequisite for
trustworthy evidence, not a claim that a live comparison has been performed.

## Consequences

A result distinguishes agent quality from infrastructure and names the exact product and expert recipe
behind its grade. Failure transcripts drive small changes; fresh confirmation and a held-out set constrain
overfitting. Model-specific wins cannot conceal regressions on other supported models.
