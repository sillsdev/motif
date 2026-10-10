# Edit a natural class

Use this command when a segment class contains a sound it should not contain, or needs one additional member. It stages identity-based member edits in a Draft.

```sh
motif compose-edit-natural-class --project language.fwdata --draft <name> \
  --intent '{"target":"<classId>","expectedMembers":["<phonemeId>","<phonemeId>"],"members":["<phonemeId>"]}'
```

The closed intent names an existing segment natural class, its exact current phoneme identities and the requested distinct phoneme identities. Both lists must be nonempty, the current list must still match, and every requested phoneme must belong to this project's first phoneme set. The class keeps its identity, abbreviation and subtype; a feature class cannot be converted in place.

The response lists rewrite rules, environments and natural-class insertions that still use the class. Those references keep using the edited class. If only some users should follow a new definition, create a separate class and use `compose-relink-natural-class` for the intended users. Finish the Draft, inspect its Dry Run, and run a bounded Trial on the corrected form and a productive held-out form. Only a person can Apply the Proposal.
