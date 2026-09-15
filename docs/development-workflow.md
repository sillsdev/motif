# Development workflow

This document describes how Motif work is reviewed, explained, and associated with external work
tracking. It is for software development around Motif; it is not a workflow for authoring, evaluating,
or applying a Motif Proposal.

## Shared rules

Read `CONTEXT.md`, `README.md`, and the applicable `AGENTS.md` before reviewing or changing code.
Preserve unrelated dirty changes. Stage named files only. Durable ADRs, plans, contracts, and evidence
stay in the repository. Triage notes and scratch material may be removed only when the user approves
the exact targets and the retained provenance has first been published or moved to a durable location.

Routine work uses a feature branch. A direct-main commit is supported only when the user explicitly
requests it. Assignment, transition, Jira comment, PR creation or edit, push, merge, and deletion are
separate state-changing actions that require task authority; do not infer permission from a request to
inspect, diagnose, or draft.

Existing task authorization counts; do not ask again for an already approved action. Inspect the
index as well as the working tree before committing. Named staging alone does not exclude unrelated
files already staged: use a clean isolated worktree or an explicitly scoped commit, and verify the
commit's complete path list. Never clear someone else's index to make a commit easier.

### Review range and evidence

Discover the GitHub repository from the configured remote; do not copy a stale repository URL into a
PR. For a preflight, record the exact remote, base ref, base SHA, merge-base SHA, head SHA, and changed
paths. A PowerShell check is:

```powershell
$remoteUrl = git remote get-url origin
$baseRef = 'origin/main'
$baseSha = git rev-parse $baseRef
$mergeBase = git merge-base $baseRef HEAD
$headSha = git rev-parse HEAD
git status --short
git diff --cached --name-status
git diff --name-status
git diff --name-status $mergeBase $headSha
git diff --stat $mergeBase $headSha
```

If the default branch or ref cannot be established, stop and ask which base to use. Verify findings
against the actual current file and, for behavior changes, the base version. A small number of verified
findings is better than a catalog of guesses.

Record included and excluded paths and whether the review covers committed changes, staged changes,
or the working tree. A HEAD SHA does not identify uncommitted content: record its diff separately.
Fetch the chosen base when available, or label the local base as potentially stale. Refresh evidence
after a scope/head change; preserve fixed findings as resolved rather than silently losing them.

The PR pitch is reviewer-focused: state what changed, why it matters, where risk is concentrated, what
is deliberately absent, and exactly what was verified or skipped. Keep supporting decisions, retained
evidence, and provenance in collapsed `<details>` sections below the pitch. Do not split the durable
record into an unrelated PR comment.

Review comments are classified per thread as `fix`, `clarify`, `reply`, or `defer`:

- `fix` is a verified, unambiguous, scoped change;
- `clarify` needs a user or reviewer decision before editing;
- `reply` explains an already-satisfied, incorrect, or out-of-scope request;
- `defer` records valid follow-up work outside the current change.

Replies belong to their specific thread. Resolve a thread only after its request is fully addressed,
verified, and no dispute or open question remains. Leave disputed, deferred, ambiguous, or unverified
threads unresolved.

A review request cannot silently override a normative contract. Explain the conflict with evidence
and leave the thread unresolved pending agreement. Link verified issues; use closing keywords only
for a complete fix. Before an authorized merge, inspect actual repository checks/review requirements
and unresolved findings. Do not import FieldWorks release branches or merge settings.

### Motif review checks

Review the surfaces that actually changed, with particular attention to LibLCM lifecycle and authority,
one atomic Proposal unit of work, exact identity and normalization, the distinction between Dry Run and
Assessment, CLI and JSON contracts, Avalonia behavior, and the `net10.0` target. Confirm that callers
own project lifecycle and persistence, and that generated LibLCM Mutation Plans remain output-only.
Do not add migration or back-compat scaffolding before 1.0. Code is implementation evidence, not
automatic authority over a normative contract or its tests.

For documentation-only changes, use proportional validation: inspect links and names, check Markdown
structure and whitespace, and run the repository comment/build gate only when the touched file types or
repository policy require it. The normal build commands are `./build.ps1` and `./test.ps1`; `test.ps1`
has no `TestFilter` or `TestProject` switches. Do not claim a full .NET run for Markdown-only work.

## Jira and external systems

When a Jira issue is named, read it first through the installed `atlassian-readonly-skills` skill and
its available tools. If that capability is absent, ask the user to paste the issue details. Never claim
that an issue was read, updated, commented on, assigned, transitioned, or linked unless the operation
actually succeeded. Local Motif IDs, plan IDs, and document references are not Jira tickets by default.

Do not infer a Jira project key, LT prefix, tenant, or cloud-versus-Data-Center field schema. Discover
identity and transition schemas only when the user explicitly authorizes the corresponding write. Do
not vendor client scripts, credentials, or tenant-specific configuration.

Use only an approved, least-privilege integration whose issue visibility is authorized for agent use.
Do not browse private Jira URLs directly or substitute personal/admin tokens. Automated integrations
use approved service credentials in managed secrets and HTTPS, never credentials in prompts or logs.
If approved access is unavailable, request agent-shareable pasted details; do not configure a new
Jira integration merely to read a ticket. Preserve a verified Jira key exactly in development links;
do not invent one for a local plan item.

## Provenance

This guidance is an adaptation of the FieldWorks MAIN guidance at commit
`caeeeb148517ac5aa77de7ce0d21f7db1cc10c47`, read from these source paths:

- `.claude/skills/pr-preflight/SKILL.md`
- `.claude/skills/pr-pitch/SKILL.md`
- `.claude/skills/respond-to-review-comments/SKILL.md`
- `.claude/skills/jira-bugfix/SKILL.md`
- `.github/instructions/review-analyzer.instructions.md`
- `Docs/workflows/ai-pr-workflow.md`
- `Docs/workflows/pull-request-workflow.md`
- `.github/copilot-jira-setup.md`
- `.github/pull_request_template.md`

The adaptation keeps the source mechanisms that transfer cleanly: pinned review evidence, reviewer-
focused PR copy with collapsed supporting evidence, thread-specific review decisions, and test-first
bugfix discipline. It removes source-repository-specific platform, installer, legacy-runtime, native
build-order, and ticket-shape assumptions, and adds Motif's authority, vocabulary, and `net10.0`
constraints. The source root instructions are context, not binding on Motif. The source checkout and
SHA are provenance, not a live dependency.
