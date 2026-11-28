# Edit a natural class's features

Stages a change to the feature values that define one natural class, so the class matches the phonemes that carry them. Use the closed input `{ "target": "<natural class ID>", "set": [ { "feature": "<ID>", "value": "<ID>" } ], "clear": [ "<feature ID>" ] }` with at least one of `set` and `clear`. A constraint on a feature the class already has takes the new value in place. The class must keep at least one constraint, because a feature class with none cannot condition an environment; define a new class instead of emptying one. A segment class has no feature constraints and is refused: change its members with [Edit a natural class](cmd:compose-edit-natural-class). Only a person can Apply.

```sh
motif compose-edit-natural-class-features --project language.fwdata --draft classes \
  --intent '{"target":"<class-id>","set":[{"feature":"<place-id>","value":"<labial-id>"}]}'
```

The response lists the environments, rules and insertions that use the class (`RelatedObjects`); the change reaches each of them. Create a class by features with [Author a natural class](cmd:compose-author-natural-class) (`features`).
