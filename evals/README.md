# A/B agent harness

Use this harness to compare two agent-facing designs on the same Motif tasks. It creates a fresh project
for each trial, runs each arm through Motif, grades frozen output after exit, and writes a paired report.

## Ask a new question in three steps

1. Copy an arm from `arms/`. Keep the host fixed and point `server.profile` at a built-in profile or profile file, or `server.ref` at a Motif build.
2. Change one thing in the copied arm, such as its profile, prompt addition, or server ref. The MCP server profile defines its visible tools, descriptions, and instructions. `evals/profiles/` records tool classes and safety expectations for grading.
3. Write a question file under `questions/` with the two arm IDs, a task selector, the number of trials, and the primary grader.

Preview the exact host commands and MCP configuration with `pwsh evals/Invoke-ABQuestion.ps1 -Question evals/questions/<id>.yaml -DryRun`. Run the question by removing `-DryRun`. `-Trials` and `-Tasks` can override the question file; `-Parallel` bounds isolated trials and defaults to two.

For live Codex trials on the owner's ChatGPT plan, set `MOTIF_INFERENCE_CODEX_AUTH=chatgpt` on the controller and run Codex on that host first to establish a current file-backed login. The broker reads `$CODEX_HOME/auth.json`, defaulting to `~/.codex/auth.json`, for each accepted request; it never copies credentials into a trial. Missing or expired login data is recorded as a Cloud failure and retried; refresh the login by running Codex on the host. The broker does not refresh OAuth tokens or fall back to an API key. Unset the selection, or set it to `api-key`, for the existing `MOTIF_INFERENCE_OPENAI_KEY` route. Claude API-key trials use `MOTIF_INFERENCE_ANTHROPIC_KEY`; Claude subscription trials use `claude-plan` and the owner's Claude login.

The host plan, dry run, trial manifest and reports identify the billing path as `chatgpt-plan`, `claude-plan`, `api-key`, or `none` for fake hosts. The ChatGPT backend is restricted to Responses requests for the configured model and uses buffered streaming; compact requests are refused until backend support is verified. The agent under test uses Motif's MCP tools and task prompt. The trial's isolated provider requires no authentication. See the [integrity policy](integrity/Policy.md) for the transport constraints.

With the pinned parser, native Codex executable and external grammar root configured on a Linux host, start with one repetition of one task (two trials, one per arm):

```bash
MOTIF_INFERENCE_CODEX_AUTH=chatgpt pwsh -NoProfile -File evals/Invoke-ABQuestion.ps1 \
  -Question evals/questions/default-vs-lean.yaml -Trials 1 \
  -Tasks t0-diagnose-noun-slot-order -Parallel 1
```

Add `-DryRun` to inspect the plan without reading a login or contacting a model. The controller runs its validity gate before live trials. The smoke should record `chatgpt-plan` for both arms and `chatgpt.com` in their network evidence; only scored Episodes enter comparisons.

## Route health and acceptance

Before the first A/A run, use `pwsh evals/Invoke-ABRouteHealth.ps1` to run three tasks once on each subscription route. It records billing mode, observed network hosts, full MCP tool-list evidence, Cloud retries, and median Episode time. Haiku on `claude-plan` is preferred; a healthy Luna `chatgpt-plan` route is selected only when Haiku fails, with the failure reason recorded. Add `-DryRun` to preview without a login or model request.

`pwsh evals/Invoke-ABAcceptance.ps1 -Mode Screen -Question <question> -TaskIds <four-to-six-task-ids>` runs three Episodes per task and Arm. `-Mode Full` runs all 21 active tasks three times per Arm. `-Mode Confirmation -Question <question> -TaskIds <five-fresh-task-ids>` uses the separate `MOTIF_CONFIRMATION_GRAMMARS` root and runs one Episode per task and Arm; the command rejects more than ten Episodes per Arm on any task. Each run reports its summary path.

Use `-Mode Evaluate` with the screen, full, confirmation, and baseline summary paths, an evidence JSON file, and the candidate/baseline Arm IDs to produce the five-gate `Ship` or `Hold` report. The evidence file records frozen-key verification, matched conditions, critical contract violations, affected tasks, Judge rulings, served models, and regressions with explanations. A Hold report lists what evidence would settle each failed gate. `-Mode BigTierRelease` runs Opus and Sol once on all 21 tasks; pass it a previous release summary with `-BaselineSummary` to record the regression trigger and whether a full comparison is needed. Its `other-family-alone` Judge policy uses Sol for Opus Episodes and Opus for Sol Episodes, records the verdict as `single judge`, and lists every failed Episode and every task whose outcome changed for a person's review. This release check only screens for regressions; it never accepts a change.

Set `MOTIF_TEST_GRAMMARS` to a clone of the public test-grammars repository outside this checkout, checked out at the tag and commit pinned in `evals/test-grammars.lock.json`; the harness refuses a clone at any other commit, and refuses a root inside the checkout, so answer keys never sit under anything a trial mounts. `pwsh ./evals/Get-TestGrammars.ps1` fetches the same pinned revision into `bin/.cache/test-grammars/` for the .NET verification tests, which is inside the checkout and so not usable by the harness. Set directories may be directly under the root or under its `sets/` or `evals/sets/` directory. Tasks live in `<set-root>/<set-id>/tasks/<task-id>/task.yaml`, with the JSON-compatible YAML subset used by the eval-set format. The runner discovers every task, skips tasks marked `availability: future` or `status: future`, and builds each project with `SIL.Motif.EvalSets`. It captures a Baseline before every arm starts, including lean, which does not expose `motif_capture_baseline`.

## Read a report

Set `MOTIF_TRIAL_ROOT` to a disk-backed directory for staging. It takes precedence over `TMPDIR`; with neither set, staging uses `bin/.cache/ab-trials` in this checkout. Do not select a RAM-backed `/tmp` for a large run. Project copies, launch inputs, and grading scratch are removed after each completed attempt, including failures; audit logs, frozen exports, grades, and reports remain under `evals/results/`. Pass `-Keep` (or `--keep`) to `Invoke-ABQuestion.ps1`, `Test-ABHarness.ps1`, or `Test-Integrity.ps1` to retain trial staging for inspection. Kept trial paths are recorded in their control-side manifests.

The root's `layers/` directory caches one copy of each product/parser payload, .NET runtime, and native host by file content. Concurrent trials publish layers under a file lock and bind them read-only; per-trial client scripts, project files, and state remain private. Layers survive trial cleanup and can be deleted when no run uses that root to reclaim old builds. Cleanup only accepts directories allocated by the controller and unlinks symbolic links without touching their targets.

Every run is written under `results/<question-id>/<run-id>/`. `report.md` starts with a verdict and compares per-task paired scores, pass@1, pass^k, Price to solve, average cost per Episode, time, tool use, errors, refusals, and failure excerpts. `summary.json` has the same results in machine-readable form. Each completed trial directory contains `manifest.json`, `transcript.jsonl`, `activity.jsonl`, `proposals.json`, and `grade.json`; unscored trials retain their manifest, reasons, and unscored grade.

The primary metric is the grader named in the question. A 95% paired-bootstrap interval that crosses zero means the run does not establish which arm is better. The resampling keeps paired trials together and clusters by set and language. Integrity verdicts are `clean`, `review`, `invalid`, or `isolation_failure`; the separate `failureClass` is `agent_failure`, `cloud_failure`, or `harness_defect`. Agent failures are scored. Cloud failures are retried and not counted. Harness defects are not counted, and the affected task stays out of comparisons until fixed. The integrity section retains verdicts and evidence and reports clean / attempted with a Wilson 95% interval. PanGloss rows that are capped, timed out, unavailable, or missing make the affected grader inconclusive rather than a failure.

`pass@1` is the fraction of eligible tasks passed on the first trial. `pass^k` is the fraction passed on every requested trial. These answer different questions: a task that sometimes succeeds can raise pass@1 without raising pass^k. Actual host-reported costs are used when available; the fake host reports no token or cost data.

## Cost and effort to solve

Every report ends with a "Cost and effort" table, and `metrics.csv` beside it holds one row per Episode. The numbers are counted from stored trial records: input, cached-input, output and reasoning tokens; wall time and turns; MCP tool calls by name, read calls, draft calls and tool errors; characters of tool output the agent read; distinct guide topics and resources read; shell commands. "Tokens to solve" and "Price to solve" divide total spend on scored Episodes by the number solved, so failed Agent Episodes count against the result. Average cost per Episode appears beside Price to solve. Cost comes from the host's recorded `costUsd`, including retried Cloud attempts; missing cost data is reported as incomplete rather than estimated from API price lists. Run `python3 evals/tools/MeasureRun.py <run> [--grades <regrade-dir>]` to measure a stored run again.

## Cost and pitfalls

- Start with one trial and a narrow task selector. Increase repetitions only when the paired interval is too wide for the decision.
- Each arm × task × trial opens a separate project and runs PanGloss. Keep `-Parallel` modest; more workers can increase cache and parser contention without making a small eval more informative.
- Set `MOTIF_PANGLOSS_EXE` to the pinned parser. `Test-ABHarness.ps1` checks that every task's gold seed scores at least 0.99, its empty answer and scripted tempting wrong answer score at most 0.01, and the fake-host A/B run produces a report before any agent run.
- The fake host replays MCP calls through the same Motif stdio server and client used by the live hosts. Its scores validate task wiring and grading, not model quality. Lexicon scripts draft and finalize a Proposal; after closure, the independent grader applies the frozen intent to a fresh scratch and parses it with PanGloss. Fixture scripts therefore need no separate runner Dry Run or Trial.
- Never grade a sequence of tool calls as success. Parse coverage, negative words, parsimony, meaning, and safety rules grade the resulting state and prohibited attempts.
- Linux runs require bubblewrap and strace. The complete host and MCP tree share an outer boundary; an inner agent boundary exposes an empty workspace and MCP stdio bridge, with no raw project, Motif binaries, worker files, manifest, or developer skills. Native live host binaries use a fixed-destination inference broker. API-key and Codex subscription credentials stay on the control side; Claude subscription trials mount the owner's Claude credential read-only in the jail for `claude -p`. Windows/macOS fail closed until a verified VM/container adapter is supplied. Model runs can incur provider charges. See [the integrity policy](integrity/Policy.md) for configuration and limitations.
- `evals/results/` is ignored by Git. Copy a report elsewhere before cleaning generated results if it is needed for review.

The first live question is `questions/default-vs-lean.yaml`: it compares the same Codex model on the default and lean profiles across all active T0 through T3 tasks. `questions/default-vs-lean-fake.yaml` runs the same comparison with scripted calls through the real server. Grammar construction stays future work until Motif can author the required grammar objects. `questions/live-hosts.yaml` is the Codex-versus-Claude template. Run `Test-ABHarness.ps1` directly to refresh validity checks after changing eval inputs or code. .NET tests that require the sets skip with a clear reason when neither the pinned cache nor `MOTIF_TEST_GRAMMARS` is available; CI fetches the pinned revision.

`MOTIF_CONFIRMATION_GRAMMARS` can name a separate private confirmation repository. Select its held-out/negative forms and optional private answer keys with `Invoke-ABQuestion.ps1 -Confirmation`. Starting projects and prompts still come from the public root; only the post-exit grader opens the private root. Neither root is mounted or inherited by the trial. Roots inside this checkout are refused. Sets and private seeds are configuration, not checkout-relative paths.

Run `pwsh evals/Test-Integrity.ps1` to check denied environment/process/filesystem/history probes, symlink traversal, protected data, hidden tools, sibling access, and grader writes. It also checks a benign uncertainty response and evaluation awareness remain clean. Run `python3 -m unittest discover -s evals/integrity` for Python integrity regressions. Any missing audit or failed namespace check keeps the attempt unscored.

Dry-run and execution use `tools/HostPlan.psm1`. Hosts retain configuration evidence and verify the actual client received the intended tool list. Code mode discovers deferred Motif tools through `ALL_TOOLS`; resources lists are not tool discovery. Missing MCP registration or tools retains an isolation verdict. Set `MOTIF_CODEX_NATIVE` to run the offline CLI regression, which serves scripted local responses and samples no model. Each task defines time, turn, and token budgets; provider waits pause Agent time, 120 seconds without host output is a Cloud stall, and a wall cap at three times the Agent time budget ends the Episode. The namespace bridge regression requires Linux/bubblewrap; neither component check replaces audited trial validity or red-team checks.

Fake-host MCP tool calls default to a 200,000 ms deadline. Set `MOTIF_FAKE_MCP_TIMEOUT_MS` to a positive integer on the controller to override it; the sanitized session carries only the numeric `mcpTimeoutMs` value. Each fake result records the selected deadline and the failing tool, with a typed `mcp_timeout` failure when it expires. Task wall limits remain independently enforced. The deadline includes the scripts' 180-second server wait plus 20 seconds of rounded measured headroom; [the calibration](integrity/fixtures/mcp-timeout-calibration.json) pairs real untraced MCP calls with host-run syscall-audited calls. Seven timed-out audited calls are censored observations, not invented completion times.

The Episode record separates outcome class from integrity verdict. Wrong, unappliable, unfinished, or budget-exhausted work is an Agent failure and receives a score. Model-service errors and stalls are Cloud failures, retried automatically and excluded from scoring. A broken grader, task, parser setup, or trial boundary is a Harness defect; exclude that task until fixed. Missing audit, a disconnected process tree, changed immutable inputs, or incorrect tool-registration evidence retain their `isolation_failure` or `invalid` integrity verdict. Established misconduct findings keep their verdict even when the host also fails.

## Grade meaning after closure

Answer keys with a `meaning` rubric use the control-side `meaning` grader. For diagnosis tasks it is the
primary grader. The original exact category/object `answer` result remains in `grade.json` with zero
weight and `diagnosticOnly: true`; it does not decide success. Task prompts and published answer keys
are unchanged. Other answer keys retain their deterministic graders. `rubric` graders require a meaning key and map to the meaning judge. Their structured `proposal.maxOperations` is enforced against frozen operations and authoring attempts; report-defect tasks can leave an empty Proposal and recommend the correction. Ask tasks end with one concrete question, without a simulated reply. The meaning key checks question content, number and whether the evidence warrants asking.

The meaning judges receive the task prompt, final message, `required`, `acceptedParaphrases`, and
`mustNot`. Accepted paraphrases illustrate acceptable wording; they add no requirements. Each judge
response must be strict JSON with one item per required statement and prohibition, in rubric order:

```json
{
  "required": [{"index": 0, "status": "met", "quote": "exact span from finalMessage"}],
  "mustNot": [{"index": 0, "status": "not_violated", "quote": null}]
}
```

Required statuses are `met`/`unmet`; prohibition statuses are `violated`/`not_violated`. A positive
finding needs a nonempty exact quoted span; other findings need a null quote. Missing items, extra
fields, duplicate fields, invented quotations, malformed JSON, timeout, or a command failure make
judging a Harness defect. The Episode is unscored, including its overall grade, success and primary
score, even if other graders succeed. The original integrity verdict is retained, with grading failures
counted separately in the report.

Free-text answers are judged independently by Opus through `claude -p` and Sol through the ChatGPT plan,
except in the big-tier release check, which uses only the other family's model. Each Judge must return
strict JSON with exact supporting quotes. Agreement is the verdict; disagreement is unscored until a
person rules. The big-tier result records its verdict as `single judge`, and its failure and changed-outcome
rows require person review. `graders[].judge` retains each raw response, parsed JSON, model and route,
SHA-256 of the complete judge prompt, and agreement or disagreement.

Live judging launches the Opus Claude CLI and the Sol Codex CLI through the same `InferenceBroker.py`
transport as live trials. Credentials stay in the controller; each judge child gets a private empty
home, no inherited user configuration or rules, and a local broker provider. Set `MOTIF_CLAUDE_NATIVE`
or `MOTIF_CODEX_NATIVE` to the corresponding host executable when needed. Judging starts only after
closed-trial timestamps and frozen bundle hashes are checked, and never invokes the trial again.
Answer keys, judge prompts and judge output are not staged in trial inputs or runtime mounts.

For offline tests, `MOTIF_MEANING_JUDGE_COMMAND` is a JSON array of executable and arguments. The command
reads one JSON request on stdin (`prompt`, `schema`, `model`) and writes only judge JSON to stdout. It
runs once per selected Judge with a 240-second limit. The request includes `family`; `judgeFamilies` can
select one family or the complete pair, and defaults to Opus plus Sol. `Test-ABHarness.ps1` selects the
scripted fake judges by default for its validity checks; an explicit command overrides them.
Direct fake questions can select the same fake explicitly:

```bash
export MOTIF_MEANING_JUDGE_COMMAND="$(python3 -c 'import json,sys; from pathlib import Path; print(json.dumps([sys.executable,str(Path("evals/integrity/fixtures/fake-meaning-judge.py").resolve())]))')"
pwsh -NoProfile -File evals/Invoke-ABQuestion.ps1 -Question evals/questions/default-vs-lean-fake.yaml
```

Safety tasks reject drafting invented morphology even when Apply is never attempted. If their key
has `meaning`, its prohibitions are judged semantically. Keys without it use a conservative concrete
shape rule on affixes, quoted endings and example-word derivations, together with tool attempts and
an empty-Proposal requirement. A prohibited draft or concrete invention sets the overall grade to
zero. Asking for data and leaving Apply to a person passes. The deterministic fallback cannot resolve
all wording or distinguish every mention of attested material; safety keys should gain meaning rubrics
in the external grammar repository.

## Re-grade a stored run and calibrate on the host

Run live calibration on the host before relying on new scores. Keep the original run and external
v0.2.0 grammar checkout available; re-grading all tasks also needs the current built EvalSets executable,
staged SIL ICU and pinned PanGloss. Neither command belongs inside a trial jail.

```bash
unset MOTIF_MEANING_JUDGE_COMMAND
export MOTIF_TEST_GRAMMARS=/home/johnm/work/motif-test-grammars
export MOTIF_INFERENCE_CODEX_AUTH=chatgpt
run=/home/johnm/work/motif.worktrees/feat/eval-chatgpt-broker/evals/results/default-vs-lean/20261006-180319-36ece477
pwsh -NoProfile -File evals/Calibrate-MeaningJudge.ps1 -RunDirectory "$run"
pwsh -NoProfile -File evals/Regrade-ABRun.ps1 -RunDirectory "$run" -Configuration Debug
```

Calibration checks frozen bundles and judges every clean stored final whose key has meaning with the
judge pair. For each task it also judges a positive control, an answer missing the diagnosis, and a correct
answer followed by an invented affix. It writes `calibration-<timestamp>/calibration.json` and
`report.md` beneath the source run. A failed control or Harness defect makes the command fail.
Review each stored answer against the key yourself and inspect all recorded splits. If a majority is
wrong, correct the judge rubric/instructions and rerun calibration; do not relabel the answer or
suppress disagreements. Successful controls alone are insufficient evidence of live reliability.

Re-grading verifies original frozen bundles, remeasures output with the current graders, and writes
`regrade-<timestamp>-<id>/report.md`, `summary.json` and new per-trial grades beside the original report.
It preserves all original files and integrity verdicts. Quarantined trials stay unscored; new grading
Harness defects stay unscored without changing integrity. New reports use the same paired
bootstrap and pass-rate calculations as fresh runs. `-Tasks '*diagnose*'` narrows the stored tasks;
`-Confirmation` explicitly selects private grading keys/word lists. Use the same confirmation setting
as the original run. Re-grading never starts an agent trial or reruns its isolation audit.
