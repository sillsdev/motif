# Rulings and keys

Readers need to see what each model believed, the evidence it used and what changed after challenge.
These records preserve that evidence independently of the eventual decision.

## Key record

A finalized key has: `oracle_version`, `recipe_sha256`, `key_id`, `key_sha256`, `task_id`, exact task
prompt, persona/job, `packet_sha256`, referenced artifact digests, acceptable solutions, required atomic
`criteria`, explicit `prohibitions`, attack-to-control mappings, parser fact locators, unresolved questions,
raw ruling references and human-review references. Criteria have a stable local ID, checklist ID,
statement, evidence needed and valid alternatives. Prohibitions have ID, checklist ID, forbidden behavior
and `critical` boolean. State is `candidate`, `held` or `accepted`; only accepted keys grade comparisons.
A key with intentionally missing language evidence can require a targeted question as its valid answer.

## Oracle ruling record

Record: version/digest, key/task/packet identifiers, role, provider-resolved model and effort (or human
identity), phase (`first`, `rebuttal`, `attack`, `curation`, `human`), independent-session identifier,
`decision` (`accept`, `amend`, `hold`), applicable checklist findings, each proposed atomic criterion,
acceptable alternatives, prohibited answers, evidence locators, uncertainty and human questions.

For rebuttals include the prior ruling digest, challenged criterion and changed claims. For attacks
include attack ID, precise counterexample, expected result, evidence and disposition. For curation include
every surviving attack’s control ID. A human decision records the competing interpretations, rationale,
remaining evidence needs and scope; it never fabricates parser results.

## Judge response record

The same response shape applies to Opus and Sol. [judge-ruling.schema.json](judge-ruling.schema.json)
is closed; keys and criterion sets must also be checked against the supplied packet.

```json
{
  "format": 1,
  "oracle_version": "v1",
  "case_id": "optional-marking:valid",
  "judge": "opus",
  "criteria": [
    {
      "id": "needed-question",
      "status": "met",
      "quote": "Can bare lum also mean plural?",
      "reason": "Asks the contrast the packet leaves unknown."
    }
  ],
  "prohibitions": [
    {
      "id": "assert-optional",
      "status": "clear",
      "quote": null,
      "reason": "Does not assert a plural reading for the bare form."
    }
  ],
  "decision": "pass"
}
```

Criteria statuses: `met`, `not_met`, `insufficient_evidence`. Prohibition statuses: `clear`, `violated`,
`insufficient_evidence`. Every `met` or `violated` finding has an exact nonempty substring of the frozen
answer, plus reasoning. `not_met` can have a null quote for omission, or an exact contradictory quote.
A `clear` prohibition has a null quote and explanation of the checked absence. A malformed record,
wrong/missing/duplicate criterion ID or invented quote is invalid output, never a vote.

Decision is `fail` if any prohibition is violated or required criterion not met; otherwise `unscored` if
any required fact is insufficient; otherwise `pass`. Repair incomplete evidence before accepting a fail
that depends on the missing portion. A supplied answer’s own epistemic uncertainty is not automatically
an unscored Judge decision: apply the task’s required question/claim, or mark its omission `not_met`.

Opus/Sol responses are stored independently with a trusted envelope: recipe/key/answer/packet hashes,
provider/model/effort, session identifiers, token use and time. The Judge cannot set that trusted metadata.
Agreement requires matching criterion statuses and prohibition statuses **and** the overall decision,
not matching prose or selected quotes. Any substantive split leaves the answer unscored for a person.
A person’s final record quotes both rulings and the answer, cites the frozen key and gives a decision.

## Comparison result

Record recipe and Judge versions/digests, exact models/effort, task/key pins, full Validity fingerprint,
Oracle overlap with agent model, independent votes, human decisions, unscored/excluded reason counts,
Agent failures, Cloud failures, Harness defects and Price to solve. Price to solve includes spending on
all scored Episodes; report no finite estimate when none solved. A Judge defect regrades the same frozen
answer; it never reruns the agent to get a better answer.
