# Add a phonological feature value

Stages one more value for a closed phonological feature that exists, such as `velar` for `place`. Use the closed input `{ "feature": "<canonical ID>", "name": "velar", "abbreviation": "vel" }`; `abbreviation` is optional. The feature must belong to the phonological feature system, not the morphosyntactic one. A value name or abbreviation the feature already has is refused. Only a person can Apply.

To create the feature in the first place use [Author a phonological feature](cmd:compose-author-phonological-feature). Phonemes and natural classes take the new value through [Set a phoneme's features](cmd:compose-edit-phoneme-features) and [Edit a natural class's features](cmd:compose-edit-natural-class-features).

The feature may have been created earlier in this Draft. In one [batch](cmd:compose-batch), use `"feature": "@identity"`; a later feature specification can use this add-value entry's own `as` label for its one created value. Across calls, use `"feature": "lookup:phonological-feature:identity"`, and bind the new value with `lookup:phonological-feature-value:identity|slash`. A name or abbreviation that matches several objects is refused.
