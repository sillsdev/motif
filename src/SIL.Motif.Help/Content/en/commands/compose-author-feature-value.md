# Author a feature value

Stages a closed feature specification in an existing or earlier-staged feature structure, including a stem's MsFeatures or an inflectional affix's InflFeats. Inflectional affix selections are word requirements. Only a person can Apply.

Use the closed input `{ "featStruc": "<canonical ID>", "feature": "<canonical ID>" }`, adding `value` only when choosing a symbolic value. Each reference names an exact canonical identity; a supplied value must belong to that exact closed feature. Omit `value` to author a feature-only specification; null is refused. A second specification for the same feature is refused.

For InflFeats, the feature must currently belong to the project's morphosyntactic feature system and fit any declared structure type. The feature structure, feature and value may already exist or have been authored earlier in the same Draft. Use the structure identity returned by [authoring grammatical features](cmd:compose-author-feature-structure).

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.
