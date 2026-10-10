# Rules for accepting a key

A key should distinguish the assistant’s reasoning from facts the parser can settle, so a plausible
explanation cannot hide a broken grammar or an unsupported language judgment.

## Freeze the packet

Before any ruling, identify the task and exact prompt, persona/job, starting grammar digest, Known
project identifiers, content and Baseline, approved morphologies, Reviewed negatives and source of each
judgment. Name affected affixes, MSAs, slots and templates first. Include declared dependencies, target
identities, limits and permission scope. Real project data stays out of public artifacts.

Freeze product binaries per Arm, parser executable/version/SHA-256 (using `pangloss-release.json`),
parser mode and Assessment scope, task-set tag/commit (`evals/test-grammars.lock.json`), MCP profiles,
shipped skills, grader inputs and recipe hashes. An unavailable required artifact prevents activation;
there are no placeholder hashes in an accepted packet. Library summaries are context; freeze a passage
and locator when the task depends on its wording. Record all unexercised constructs and parser warnings.

## Independent first rulings

Opus and Sol, at the manifest’s big-tier effort, load the consultant and receive the identical packet.
Each produces the [ruling record](rulings.md) before seeing the other’s response, an Arm answer or score.
The task author may draft the prompt and candidate key; both first rulers must audit rather than assume it.
Preserve both raw responses and timestamps; truncation or malformed output requires packet repair, not
an inferred vote. Expose multiple acceptable solutions and deliberate insufficiency-of-evidence tasks.

## Exactly one rebuttal round

Once both first rulings are saved, reveal them to each other. Each gets one evidence-bearing rebuttal:
identify the strongest disputed criterion, give a concrete counterexample, cite a Trace step, grammar
object, read-back record or source locator, and submit its revised ruling. Save the originals and changes.
No further persuasion rounds, rerolls until agreement or majority voting. Any new evidence is appended
and fingerprinted; altered task semantics create a new key version, not an unnoticed correction.

## Attack every key

Run fresh devil’s-advocate sessions for Opus and Sol against every candidate key, including keys whose
rulers agreed. Attack omissions, valid alternative solutions, wrong negatives, incompletion, terminology,
identity/scope, unauthorized edits and circular parser expectations. Give the attacker the complete key
and evidence; it must produce an executable or answer-level counterexample, not a rhetorical objection.

A surviving attack is one the curator cannot refute from the frozen evidence. Every surviving attack
becomes a named control in that task: a tempting wrong answer that must fail, a valid alternative that
must pass, an ask-the-person boundary, or a grammar/parse contrast checked control-side. Bind its attack
ID, inputs, expected behavior and evidence. If no reliable control can be built, hold that key out.
Recheck all controls after amendment. Save refuted attacks too, with the refuting evidence.

## Settle facts and language choices

The pinned parser settles whether that mode and completed search confirms an analysis, given its exact
project and limits. Check import/load warnings and actual confirmed identities. FST proposals alone do
not settle confirmation. An interrupted or skipped search does not demonstrate absence. Control-side
LibLCM read-back settles exact stored effects, including normalized values and writing systems;
operation count is review burden, not a pass condition.

If the parser or read-back contradicts the packet, repair the task as a Harness defect before comparison.
A valid packet exposing an agent’s wrong result is an Agent failure. Cloud failures pause the Agent budget
and are retried under ADR 0059; neither Cloud failures nor Harness defects become agent failures.

A parser cannot decide whether an unattested reading is linguistically acceptable. Remaining linguistic
disputes go to a qualified person in a grill session: show competing interpretations, the contrast that
would discriminate them, current evidence and consequences. Record their decision and rationale; if they
need elicitation, leave the point unresolved and the key unaccepted. Do not infer a decision from silence.

## Human spot-check rule

This is a Motif v1 operating choice, not a statistically validated error bound. For the initial suite of
21 keys, check **all 21**. For later versions with N accepted candidate keys, independently spot-check
`min(N, max(21, ceil(N / 4)))` keys, stratified by task family and selected using sorted key IDs and a
recorded random seed before reading model outcomes. Add every disputed, amended-after-attack and critical
contract key not already sampled. Report random-sample count, additional targeted count, unique total,
reviewer identity/qualification and decisions. Zero checks means zero activation, not implied approval.

The reviewer sees both initial and revised rulings, controls and evidence, including unanimous cases.
An audit error holds the affected family, repairs its keys and controls, and reviews every key in that
family before accepting the version. Counts alone do not establish error rates or expertise.

## Freeze and report

The curator accepts only keys with resolved atomic criteria, explicit prohibitions, applicable checklist
IDs, valid positive/empty/tempting-wrong controls, all surviving attacks covered and the human requirement
satisfied. A recipe release records its content digest, accepted key digests and actual human-check count.
A changed recipe/key creates a new result series: regrade both Arms’ frozen answers with the same qualified
Judge recipe; rerun agents only when task inputs changed. Preserve old records without migrations.

Results say **“agrees with Oracle v1”**, name the exact recipe digest, report Oracle/model overlap, Judge
models/effort, raw pair disagreement, resolved decisions and all excluded/unscored counts. No claim of
linguistic correctness follows merely from agreement. The acceptance loop and 21-task comparison budget
remain those of ADR 0059; this lane does not replace them with large-sample significance gates.
