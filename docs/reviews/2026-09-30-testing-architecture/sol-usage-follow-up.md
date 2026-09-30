# Explicit action usage follow-up

Usage should count each action a person initiates once, even when it is refused or cancelled. Automatic refreshes and the internal work one action triggers should not add entries.

## Accepted corrections

Independent Sol review of `d03c49b3` against `ccf3fab1` accepted explicit word-set and rule-selection scopes. The rerun scope begins before user-facing validation and remains alive across all selected words and cancellation, suppressing nested Assess entries. Argument records contain shapes and list sizes rather than values. Shared automatic evidence and focused reads remain unrecorded. No new privacy or scope-lifecycle findings were reported.

## Final selector correction

Four explicit selectors still bypass the scoped handlers: picked words, text lists, checked words and a matrix cell. They reach `LoadScopeAsync` through shared helpers and record zero entries on success or refusal. Add scopes at their explicit command wrappers while keeping the shared helpers unrecorded. Owner correction ef65dd99 scopes the four explicit wrappers. All eight added success/refusal cases failed before the fix and passed afterward. Independent final Sol review accepted it with no findings.

## Verification limits

The reviewer inspected source and behavioral tests without running builds or tests. The owner full wrapper passed 3,662 tests, failed none and skipped 61 on its parserless base. All three usage commits are integrated in the parent as 3ee2aef0, 80c118d7 and fa5aac53. Parent build and hygiene pass; combined real-parser verification remains pending.
