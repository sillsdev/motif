# Notebook and list-item metadata measurement

A live blank LcmCache was traversed from each root, following nonvirtual owning fields, their concrete and abstract subclasses, and their base classes. Traversal stops at StText (represented as one rich field) and at generic CmObject destinations. Fields are counted once at their declaring class; inherited copies are not counted again. This is a reachability inventory, not a count of fields that need editorial judgment.

| Kind | Classes | Fields | Structurally representable | Included by overlay | Excluded by overlay |
| --- | ---: | ---: | ---: | ---: | ---: |
| notebook/record | 3 | 44 | 41 | 31 | 13 |
| lists/item | 34 | 152 | 148 | 132 | 20 |

All measured writing-system hints were zero. Inclusion, field keys and order remain editorial decisions. Unsupported timestamps, general dates and binary values are excluded from the shipped overlays. The tables list metadata types, destination classes for owned relationships, virtual fields, and the corresponding overlay decision. Counts cover declared fields, including the explicitly excluded CmObject identity plumbing.

## Notebook

| Class | Field | Metadata type | Virtual | Overlay decision |
| --- | --- | --- | --- | --- |
| CmObject | Self | ReferenceAtom | true | exclude |
| CmObject | Owner | ReferenceAtom | true | exclude |
| CmObject | OwningFlid | Integer | false | exclude |
| CmObject | OwnOrd | Integer | false | exclude |
| CmObject | Guid | Guid | false | exclude |
| CmObject | ShortNameTSS | String | true | exclude |
| CmObject | ClassID | Integer | false | exclude |
| RnGenericRec | ShortNameTSS | String | true | exclude |
| RnGenericRec | SubrecordOf | String | true | exclude |
| RnGenericRec | IndexInOwnerTSS | String | true | exclude |
| RnGenericRec | Title | String | false | include as `title` |
| RnGenericRec | VersionHistory | MinObj → StText | false | include as `version-history` |
| RnGenericRec | Reminders | ReferenceCollection | false | include as `reminders` |
| RnGenericRec | Researchers | ReferenceCollection | false | include as `researchers` |
| RnGenericRec | Confidence | ReferenceAtom | false | include as `confidence` |
| RnGenericRec | Restrictions | ReferenceCollection | false | include as `restrictions` |
| RnGenericRec | AnthroCodes | ReferenceCollection | false | include as `anthro-codes` |
| RnGenericRec | PhraseTags | ReferenceCollection | false | include as `phrase-tags` |
| RnGenericRec | SubRecords | OwningSequence → RnGenericRec | false | include as `sub-records` |
| RnGenericRec | DateCreated | Time | false | exclude |
| RnGenericRec | DateModified | Time | false | exclude |
| RnGenericRec | CrossReferences | ReferenceCollection | false | include as `cross-references` |
| RnGenericRec | ExternalMaterials | MinObj → StText | false | include as `external-materials` |
| RnGenericRec | FurtherQuestions | MinObj → StText | false | include as `further-questions` |
| RnGenericRec | SeeAlso | ReferenceCollection | false | include as `see-also` |
| RnGenericRec | DateOfEvent | GenDate | false | exclude |
| RnGenericRec | CounterEvidence | ReferenceSequence | false | include as `counter-evidence` |
| RnGenericRec | Status | ReferenceAtom | false | include as `status` |
| RnGenericRec | SupersededBy | ReferenceCollection | false | include as `superseded-by` |
| RnGenericRec | SupportingEvidence | ReferenceSequence | false | include as `supporting-evidence` |
| RnGenericRec | Conclusions | MinObj → StText | false | include as `conclusions` |
| RnGenericRec | Hypothesis | MinObj → StText | false | include as `hypothesis` |
| RnGenericRec | ResearchPlan | MinObj → StText | false | include as `research-plan` |
| RnGenericRec | Locations | ReferenceCollection | false | include as `locations` |
| RnGenericRec | Sources | ReferenceCollection | false | include as `sources` |
| RnGenericRec | TimeOfEvent | ReferenceCollection | false | include as `time-of-event` |
| RnGenericRec | Type | ReferenceAtom | false | include as `type` |
| RnGenericRec | Description | MinObj → StText | false | include as `description` |
| RnGenericRec | Participants | OwningCollection → RnRoledPartic | false | include as `participants` |
| RnGenericRec | PersonalNotes | MinObj → StText | false | include as `personal-notes` |
| RnGenericRec | Discussion | MinObj → StText | false | include as `discussion` |
| RnGenericRec | Text | ReferenceAtom | false | include as `text` |
| RnRoledPartic | Participants | ReferenceCollection | false | include as `participants` |
| RnRoledPartic | Role | ReferenceAtom | false | include as `role` |

## List items

| Class | Field | Metadata type | Virtual | Overlay decision |
| --- | --- | --- | --- | --- |
| ChkRef | Ref | Integer | false | include as `ref` |
| ChkRef | KeyWord | String | false | include as `key-word` |
| ChkRef | Status | Integer | false | include as `status` |
| ChkRef | Rendering | ReferenceAtom | false | include as `rendering` |
| ChkRef | Location | Integer | false | include as `location` |
| ChkRendering | SurfaceForm | ReferenceAtom | false | include as `surface-form` |
| ChkRendering | Meaning | ReferenceAtom | false | include as `meaning` |
| ChkRendering | Explanation | MultiUnicode | false | include as `explanation` |
| ChkTerm | Occurrences | OwningSequence → ChkRef | false | include as `occurrences` |
| ChkTerm | SeeAlso | MultiUnicode | false | include as `see-also` |
| ChkTerm | Renderings | OwningCollection → ChkRendering | false | include as `renderings` |
| ChkTerm | TermId | Integer | false | include as `term-id` |
| CmAnnotationDefn | AllowsComment | Min | false | include as `allows-comment` |
| CmAnnotationDefn | AllowsFeatureStructure | Min | false | include as `allows-feature-structure` |
| CmAnnotationDefn | AllowsInstanceOf | Min | false | include as `allows-instance-of` |
| CmAnnotationDefn | InstanceOfSignature | Integer | false | include as `instance-of-signature` |
| CmAnnotationDefn | UserCanCreate | Min | false | include as `user-can-create` |
| CmAnnotationDefn | CanCreateOrphan | Min | false | include as `can-create-orphan` |
| CmAnnotationDefn | PromptUser | Min | false | include as `prompt-user` |
| CmAnnotationDefn | CopyCutPastable | Min | false | include as `copy-cut-pastable` |
| CmAnnotationDefn | ZeroWidth | Min | false | include as `zero-width` |
| CmAnnotationDefn | Multi | Min | false | include as `multi` |
| CmAnnotationDefn | Severity | Integer | false | include as `severity` |
| CmAnnotationDefn | MaxDupOccur | Integer | false | include as `max-dup-occur` |
| CmDomainQ | Question | MultiUnicode | false | include as `question` |
| CmDomainQ | ExampleWords | MultiUnicode | false | include as `example-words` |
| CmDomainQ | ExampleSentences | MultiString | false | include as `example-sentences` |
| CmLocation | Alias | MultiUnicode | false | include as `alias` |
| CmObject | Self | ReferenceAtom | true | exclude |
| CmObject | Owner | ReferenceAtom | true | exclude |
| CmObject | OwningFlid | Integer | false | exclude |
| CmObject | OwnOrd | Integer | false | exclude |
| CmObject | Guid | Guid | false | exclude |
| CmObject | ShortNameTSS | String | true | exclude |
| CmObject | ClassID | Integer | false | exclude |
| CmPerson | Alias | MultiUnicode | false | include as `alias` |
| CmPerson | Gender | Integer | false | include as `gender` |
| CmPerson | DateOfBirth | GenDate | false | exclude |
| CmPerson | PlaceOfBirth | ReferenceAtom | false | include as `place-of-birth` |
| CmPerson | IsResearcher | Min | false | include as `is-researcher` |
| CmPerson | PlacesOfResidence | ReferenceCollection | false | include as `places-of-residence` |
| CmPerson | Education | ReferenceAtom | false | include as `education` |
| CmPerson | DateOfDeath | GenDate | false | exclude |
| CmPerson | Positions | ReferenceCollection | false | include as `positions` |
| CmPossibility | Name | MultiUnicode | false | include as `name` |
| CmPossibility | Abbreviation | MultiUnicode | false | include as `abbreviation` |
| CmPossibility | Description | MultiString | false | include as `description` |
| CmPossibility | SubPossibilities | OwningSequence → CmPossibility | false | include as `sub-possibilities` |
| CmPossibility | SortSpec | Integer | false | include as `sort-spec` |
| CmPossibility | Restrictions | ReferenceCollection | false | include as `restrictions` |
| CmPossibility | Confidence | ReferenceAtom | false | include as `confidence` |
| CmPossibility | Status | ReferenceAtom | false | include as `status` |
| CmPossibility | DateCreated | Time | false | exclude |
| CmPossibility | DateModified | Time | false | exclude |
| CmPossibility | Discussion | MinObj → StText | false | include as `discussion` |
| CmPossibility | Researchers | ReferenceCollection | false | include as `researchers` |
| CmPossibility | HelpId | Unicode | false | include as `help-id` |
| CmPossibility | ForeColor | Integer | false | include as `fore-color` |
| CmPossibility | BackColor | Integer | false | include as `back-color` |
| CmPossibility | UnderColor | Integer | false | include as `under-color` |
| CmPossibility | UnderStyle | Integer | false | include as `under-style` |
| CmPossibility | Hidden | Min | false | include as `hidden` |
| CmPossibility | IsProtected | Min | false | include as `is-protected` |
| CmSemanticDomain | ReferringSenses | ReferenceCollection | true | exclude |
| CmSemanticDomain | LouwNidaCodes | Unicode | false | include as `louw-nida-codes` |
| CmSemanticDomain | OcmCodes | Unicode | false | include as `ocm-codes` |
| CmSemanticDomain | OcmRefs | ReferenceCollection | false | include as `ocm-refs` |
| CmSemanticDomain | RelatedDomains | ReferenceCollection | false | include as `related-domains` |
| CmSemanticDomain | Questions | OwningSequence → CmDomainQ | false | include as `questions` |
| FsClosedValue | Value | ReferenceAtom | false | include as `value` |
| FsComplexValue | Value | MinObj → FsAbstractStructure | false | include as `value` |
| FsDisjunctiveValue | Value | ReferenceCollection | false | include as `value` |
| FsFeatStruc | IsEmpty | Min | true | exclude |
| FsFeatStruc | LongNameTSS | String | true | exclude |
| FsFeatStruc | LongNameSortedTSS | String | true | exclude |
| FsFeatStruc | ChooserNameTS | String | true | exclude |
| FsFeatStruc | FeatureDisjunctions | OwningCollection → FsFeatStrucDisj | false | include as `feature-disjunctions` |
| FsFeatStruc | FeatureSpecs | OwningCollection → FsFeatureSpecification | false | include as `feature-specs` |
| FsFeatStruc | Type | ReferenceAtom | false | include as `type` |
| FsFeatStrucDisj | Contents | OwningCollection → FsFeatStruc | false | include as `contents` |
| FsFeatureSpecification | RefNumber | Integer | false | include as `ref-number` |
| FsFeatureSpecification | ValueState | Integer | false | include as `value-state` |
| FsFeatureSpecification | Feature | ReferenceAtom | false | include as `feature` |
| FsNegatedValue | Value | ReferenceAtom | false | include as `value` |
| FsOpenValue | Value | MultiUnicode | false | include as `value` |
| FsSharedValue | Value | ReferenceAtom | false | include as `value` |
| LexEntryInflType | GlossPrepend | MultiUnicode | false | include as `gloss-prepend` |
| LexEntryInflType | GlossAppend | MultiUnicode | false | include as `gloss-append` |
| LexEntryInflType | InflFeats | MinObj → FsFeatStruc | false | include as `infl-feats` |
| LexEntryInflType | Slots | ReferenceCollection | false | include as `slots` |
| LexEntryType | ReverseAbbr | MultiUnicode | false | include as `reverse-abbr` |
| LexEntryType | ReverseName | MultiUnicode | false | include as `reverse-name` |
| LexReference | OwnerType | ReferenceAtom | true | exclude |
| LexReference | ConfigTargets | ReferenceCollection | true | exclude |
| LexReference | FullDisplayText | String | true | exclude |
| LexReference | Comment | MultiString | false | include as `comment` |
| LexReference | Targets | ReferenceSequence | false | include as `targets` |
| LexReference | Name | MultiUnicode | false | include as `name` |
| LexReference | LiftResidue | Unicode | false | include as `lift-residue` |
| LexRefType | ReverseAbbreviation | MultiUnicode | false | include as `reverse-abbreviation` |
| LexRefType | MappingType | Integer | false | include as `mapping-type` |
| LexRefType | Members | OwningCollection → LexReference | false | include as `members` |
| LexRefType | ReverseName | MultiUnicode | false | include as `reverse-name` |
| MoInflAffixSlot | Affixes | ReferenceSequence | true | exclude |
| MoInflAffixSlot | Name | MultiUnicode | false | include as `name` |
| MoInflAffixSlot | Description | MultiString | false | include as `description` |
| MoInflAffixSlot | Optional | Min | false | include as `optional` |
| MoInflAffixTemplate | Name | MultiUnicode | false | include as `name` |
| MoInflAffixTemplate | Description | MultiString | false | include as `description` |
| MoInflAffixTemplate | Slots | ReferenceSequence | false | include as `slots` |
| MoInflAffixTemplate | Stratum | ReferenceAtom | false | include as `stratum` |
| MoInflAffixTemplate | Region | MinObj → FsFeatStruc | false | include as `region` |
| MoInflAffixTemplate | PrefixSlots | ReferenceSequence | false | include as `prefix-slots` |
| MoInflAffixTemplate | SuffixSlots | ReferenceSequence | false | include as `suffix-slots` |
| MoInflAffixTemplate | Final | Min | false | include as `final` |
| MoInflAffixTemplate | Disabled | Min | false | include as `disabled` |
| MoInflAffixTemplate | ProcliticSlots | ReferenceSequence | false | include as `proclitic-slots` |
| MoInflAffixTemplate | EncliticSlots | ReferenceSequence | false | include as `enclitic-slots` |
| MoInflClass | Abbreviation | MultiUnicode | false | include as `abbreviation` |
| MoInflClass | Description | MultiString | false | include as `description` |
| MoInflClass | Name | MultiUnicode | false | include as `name` |
| MoInflClass | Subclasses | OwningCollection → MoInflClass | false | include as `subclasses` |
| MoInflClass | RulesOfReferral | OwningSequence → MoReferralRule | false | include as `rules-of-referral` |
| MoInflClass | StemNames | OwningCollection → MoStemName | false | include as `stem-names` |
| MoInflClass | ReferenceForms | OwningCollection → FsFeatStruc | false | include as `reference-forms` |
| MoMorphType | Postfix | Unicode | false | include as `postfix` |
| MoMorphType | Prefix | Unicode | false | include as `prefix` |
| MoMorphType | SecondaryOrder | Integer | false | include as `secondary-order` |
| MoReferralRule | Name | MultiUnicode | false | include as `name` |
| MoReferralRule | Description | MultiString | false | include as `description` |
| MoReferralRule | Input | MinObj → FsFeatStruc | false | include as `input` |
| MoReferralRule | Output | MinObj → FsFeatStruc | false | include as `output` |
| MoStemName | Abbreviation | MultiUnicode | false | include as `abbreviation` |
| MoStemName | Description | MultiString | false | include as `description` |
| MoStemName | Name | MultiUnicode | false | include as `name` |
| MoStemName | Regions | OwningCollection → FsFeatStruc | false | include as `regions` |
| MoStemName | DefaultAffix | ReferenceAtom | false | include as `default-affix` |
| MoStemName | DefaultStem | ReferenceAtom | false | include as `default-stem` |
| PartOfSpeech | InherFeatVal | MinObj → FsFeatStruc | false | include as `inher-feat-val` |
| PartOfSpeech | EmptyParadigmCells | OwningCollection → FsFeatStruc | false | include as `empty-paradigm-cells` |
| PartOfSpeech | RulesOfReferral | OwningSequence → MoReferralRule | false | include as `rules-of-referral` |
| PartOfSpeech | InflectionClasses | OwningCollection → MoInflClass | false | include as `inflection-classes` |
| PartOfSpeech | AffixTemplates | OwningSequence → MoInflAffixTemplate | false | include as `affix-templates` |
| PartOfSpeech | AffixSlots | OwningCollection → MoInflAffixSlot | false | include as `affix-slots` |
| PartOfSpeech | StemNames | OwningCollection → MoStemName | false | include as `stem-names` |
| PartOfSpeech | BearableFeatures | ReferenceCollection | false | include as `bearable-features` |
| PartOfSpeech | InflectableFeats | ReferenceCollection | false | include as `inflectable-feats` |
| PartOfSpeech | ReferenceForms | OwningCollection → FsFeatStruc | false | include as `reference-forms` |
| PartOfSpeech | DefaultFeatures | MinObj → FsFeatStruc | false | include as `default-features` |
| PartOfSpeech | DefaultInflectionClass | ReferenceAtom | false | include as `default-inflection-class` |
| PartOfSpeech | CatalogSourceId | Unicode | false | include as `catalog-source-id` |
| PhPhonRuleFeat | Item | ReferenceAtom | false | include as `item` |

## Generic-closure footnote

If the traversal continues inside StText, StTxtPara.AnalyzedTextObjects expands the generic CmObject signature. Both roots then reach the same 193 classes and 1,066 fields (1,042 with representable structural kinds), matching the full lexicon inventory. This broad closure is not a useful measure of the notebook or list overlay itself.
