# Change an affix's kind

Use this command when an affix was entered as one kind and the evidence says another: inflectional (it fills a slot of a template), derivational (it makes a word of a category from a word of another), or unclassified (not yet analysed). Choosing and changing later is the normal path, so the change is made in place and can be changed back with the same command.

```sh
motif compose-change-affix-kind --project language.fwdata --draft kind \
  --intent '{"msa":"<affix-msa-id>","kind":"derivational","toCategory":"<category-id>"}'
```

The intent has `msa`, the id of the affix's current grammatical info, and `kind`: `inflectional`, `derivational` or `unclassified`. `toCategory` belongs to `derivational` only and names the category of the word the affix makes; when it is absent the affix keeps its attaching category on both sides. `slots` belongs to `inflectional` only and names existing slots of the affix's category or an ancestor; an inflectional affix applies only in a template slot, so a change to inflectional without `slots` is allowed but reported. An affix already of the kind asked for stages nothing.

What the change does: it creates the grammatical info of the new kind with the attaching category, points every sense that used the old one at it, moves every ad hoc morpheme prohibition and every parser-analysis morph bundle that referred to the old one to the new one (in the same place in the prohibition's list), and deletes the old one. The new grammatical info has a new id, so use that id to change the kind back. Exception features of the attaching side carry between inflectional and derivational. The form is not touched, so a reduplication or other process stays as authored.

What the new kind cannot hold is listed in the Dry Run as related objects, never refused: the slots of an inflectional affix, a derivational affix's From and To inflection classes, To exception features, stem name, features and a To category different from the attaching one, an inflectional affix's features, and inflection classes on the form when the affix stops being inflectional. To restore it, give the same slots, classes or `toCategory` when changing back.

A moved prohibition or morph bundle is listed in the Dry Run too, because it now names an affix of another kind: the prohibition still bans the same affix, but it has no effect while an inflectional affix fills no slot, and approved analyses now state the affix as the new kind. The only refusals rest on an invalid model: the grammatical info is a stem's, not an affix's, or its entry has no affix form, or a slot belongs to another category, or an object of some other class that Motif cannot point at another grammatical info still refers to the old one and would be left pointing at nothing.

A change to derivational lists a `Derivation: From -> To` line naming both categories in the composed summary, and its Dry Run warns when the project has no stem, subcategory stem or derivation that makes the From category, or when nothing takes the To category (or a subcategory) as input, which usually means the categories are the wrong way round.

The command stages operations in a Draft and does not change the saved FieldWorks project. Dry Run and Trial the Proposal on words the affix should and should not parse, then Finalize it for a person to review and Apply.
