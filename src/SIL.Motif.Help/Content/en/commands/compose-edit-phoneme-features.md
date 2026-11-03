# Set a phoneme's features

Stages feature values on one phoneme that exists. Use the closed input `{ "target": "<phoneme ID>", "set": [ { "feature": "<ID>", "value": "<ID>" } ], "clear": [ "<feature ID>" ] }` with at least one of `set` and `clear`. A feature the phoneme already has takes the new value in place, a feature it lacks gains a specification, and a phoneme with no feature structure gets one. A value that does not belong to its feature, a feature named in both lists, a `clear` of a feature the phoneme does not have, and a change that alters nothing are refused. Only a person can Apply.

```sh
motif compose-edit-phoneme-features --project language.fwdata --draft features \
  --intent '{"target":"<phoneme-id>","set":[{"feature":"<place-id>","value":"<labial-id>"}]}'
```

The features come from [Author a phonological feature](cmd:compose-author-phonological-feature). To give a new phoneme its features at the start, put `features` in [Author a phoneme](cmd:compose-author-phoneme) instead. A phoneme that carries no features is matched by a natural class defined by segments, never by a class defined by features. An archiphoneme is a phoneme like any other, so it can take features too (guide archiphonemes).
