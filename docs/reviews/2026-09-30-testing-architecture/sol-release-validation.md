# Release validation follow-up

Release confidence needs real parser execution and current screenshots and videos, while ordinary developer machines can still skip unavailable external tools. Independent review checked that the new gate separates those cases.

## Findings and disposition

The video requirement initially leaked into a synthetic screenshot unit test, causing release validation to fail before any missing encoder could be assessed. Sol identified this as P1; `04be46f6` scopes both clip flags for that unit test and checks restoration. Actual authored replay still requires videos during release validation. The full integrated release run remains pending.

The reviewed pin helper verifies release version/RID/SHA-256 and permits Windows verification of Unix artifact bytes. Its synthetic probe covered all four pinned RIDs on Windows. This does not establish complete cross-target packaging: existing package staging still uses Unix mode APIs for Unix targets on Windows, a pre-existing limitation.

## Verification

The parent pin probe passed all four RID cases. The required-integration verifier found all five critical tests Passed in fresh parent TRX files and classified the 22 skips as one known capability gap, 16 platform/privilege cases and five opt-in artifact harnesses.

The first parent real-CLI site run passed 18 tests, failed one and skipped none. The failure was `Invalid help entry: agents/handoff`, confirming that the separate in-progress site-core adaptation must accept the shared catalog's nested Guide routes. Synthetic fixture success alone is not accepted as completion.

Release helper commits are integrated as `70e8c172` and `99487ab9`; the clip-isolation correction is `04be46f6`. The parent still must run same-configuration release validation with fresh Help export, screenshots, videos, API XML and sample output after the remaining code and authored content are integrated. No site was published.