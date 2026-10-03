# Writing-system display contract

Motif keeps each displayed language string beside its writing-system tag, so a front end can use the project's font and direction. Display settings are captured with the Baseline and remain readable when the saved project copy is unavailable.

[ADR 0051](adr/0051-writing-system-display-follows-fieldworks.md) requires FieldWorks' settings APIs, precedence and fallbacks, with no Avalonia or App dependency in the resolution layer. WritingSystemDisplay in SIL.Motif.Contract is the shared record. Its closed schema is src/SIL.Motif.Contract/Schemas/writing-system-display.schema.json, embedded in the assembly. JSON uses the projection camel-case convention; canonical digests use RFC 8785.

| Field | Meaning |
| --- | --- |
| id, name, abbreviation | Core writing-system definition's identity and display values |
| kind | Current vernacular or analysis list membership |
| position | Zero-based list index; neither sorted nor deduplicated |
| isDefault | First member of that list |
| fontFamily, fontFeatures | Writing-system default font and verbatim feature string |
| rightToLeft | Definition's script direction |
| styleSizes | Effective point sizes, keyed by exact stylesheet name |
| styleFonts | Effective requested font family and verbatim features for every key in styleSizes |

An id may have both list memberships. Settings repeat without losing order or either default. There is no engine field.

## Resolution and precedence

The Host initializes LcmStyleSheet with the cache, language-project HVO and LangProjectTags.kflidStyles, just as [LexEntryUi does](https://github.com/sillsdev/FieldWorks/blob/089eb9027b6d81be7883c40960f0de3ffa04b699/Src/FdoUi/LexEntryUi.cs#L304-L315). Style(name) returns LibLCM's BaseStyleInfo; DefaultCharacterStyleInfo and OverrideCharacterStyleInfo(wsHandle) expose FontInfo with inheritance already resolved. Apply the common property followed by its writing-system-specific override, checking ValueIsSet before accessing Value ([IStylesheet.cs:53–90](https://github.com/sillsdev/liblcm/blob/d564a719b1cce16c25ebea53a537393cb757f5d1/src/SIL.LCModel/IStylesheet.cs#L53-L90), [BaseStyleInfo.cs:1423–1440](https://github.com/sillsdev/liblcm/blob/d564a719b1cce16c25ebea53a537393cb757f5d1/src/SIL.LCModel/DomainServices/BaseStyleInfo.cs#L1423-L1440)). Motif neither decodes ktptWsStyle nor rebuilds inheritance.

Divide effective millipoints by 1000. An absent style/property retains effective Normal; Normal without a size uses LcmStyleSheet.NormalFontSize and its FontInfo.kDefaultFontSize fallback (10 points). This mirrors [the native view's root initialization from Normal](https://github.com/sillsdev/FieldWorks/blob/089eb9027b6d81be7883c40960f0de3ffa04b699/Src/views/VwPropertyStore.cpp#L2093-L2118) and [LibLCM's fallback](https://github.com/sillsdev/liblcm/blob/d564a719b1cce16c25ebea53a537393cb757f5d1/src/SIL.LCModel/DomainServices/LcmStyleSheet.cs#L708-L724). Neither a writing-system definition's DefaultFontSize nor LDML relative size is this value. No rescaling or clamping occurs; the renderer only converts units and applies zoom.

A style's magic default-font name resolves through DefaultFontName. Nonempty effective style features win; otherwise the default-font path uses DefaultFontFeatures, while an explicit-font path uses no default features—even when it happens to name the same family ([RenderEngineFactory.cs:39–60,94–105](https://github.com/sillsdev/FieldWorks/blob/089eb9027b6d81be7883c40960f0de3ffa04b699/Src/Common/SimpleRootSite/RenderEngineFactory.cs#L39-L105)). The reader uses libpalaso's selected default font, not its own first-font choice. Direction comes from RightToLeftScript, as [the interlinear view does](https://github.com/sillsdev/FieldWorks/blob/089eb9027b6d81be7883c40960f0de3ffa04b699/Src/LexText/Interlinear/InterlinVc.cs#L1654-L1685).

Companion SharedSettings/LexiconSettings.plsx and the current user's .ulsx file travel with saved project copies and bundles because names, abbreviations and default-font selection are not all in LDML. Installed-font substitution belongs to the renderer. Features remain verbatim for Settings; rendering uses OpenType tags and ignores numeric feature IDs without notices.

## FieldWorks surfaces and styles

Citations below are pinned to FieldWorks 089eb9027b6d81be7883c40960f0de3ffa04b699. LibLCM evidence is pinned to d564a719b1cce16c25ebea53a537393cb757f5d1; libpalaso defaults are from 0750fb98daafc0e684104676e7cb22035c372b7a. The package pins are in SilVersions.props.

| FieldWorks view/data | FieldWorks style/size source | Matching Motif surface and choice | Evidence |
| --- | --- | --- | --- |
| Interlinear word, morph, lexical gloss and category lines | Root Normal font properties; no per-line named style or size override | Analyze texts lines, strips and analysis cards: Normal for each tagged constituent | [InterlinVc.cs:779–806](https://github.com/sillsdev/FieldWorks/blob/089eb9027b6d81be7883c40960f0de3ffa04b699/Src/LexText/Interlinear/InterlinVc.cs#L779-L806), [DisplayMorphBundle:1914–2010](https://github.com/sillsdev/FieldWorks/blob/089eb9027b6d81be7883c40960f0de3ffa04b699/Src/LexText/Interlinear/InterlinVc.cs#L1914-L2010), [root Normal](https://github.com/sillsdev/FieldWorks/blob/089eb9027b6d81be7883c40960f0de3ffa04b699/Src/views/VwPropertyStore.cpp#L2093-L2118) |
| Lexicon Edit lexeme/citation form and sense gloss | Normal; optional configured textStyle replaces it. Gloss's 100% rule preserves effective size | Inspector facts/headwords and Review lexical values: Normal. Dictionary styles do not apply | [LexEntryParts.xml:9–21](https://github.com/sillsdev/FieldWorks/blob/089eb9027b6d81be7883c40960f0de3ffa04b699/DistFiles/Language%20Explorer/Configuration/Parts/LexEntryParts.xml#L9-L21), [LexSenseParts.xml:526–533](https://github.com/sillsdev/FieldWorks/blob/089eb9027b6d81be7883c40960f0de3ffa04b699/DistFiles/Language%20Explorer/Configuration/Parts/LexSenseParts.xml#L526-L533), [textStyle handling](https://github.com/sillsdev/FieldWorks/blob/089eb9027b6d81be7883c40960f0de3ffa04b699/Src/Common/Controls/Widgets/InnerLabeledMultiStringView.cs#L166-L177) |
| Text body/source paragraph | Paragraph's authored named style; Normal if none. Paragraph is the standard prose style | Sentence/context excerpts use WordOccurrence.SentenceStyle; stored lines retain SentenceStyle. Capture all project styles, including custom names | [StVc.ApplyParagraphStyleProps:440–467](https://github.com/sillsdev/FieldWorks/blob/089eb9027b6d81be7883c40960f0de3ffa04b699/Src/Common/RootSite/StVc.cs#L440-L467), [Paragraph definition](https://github.com/sillsdev/FieldWorks/blob/089eb9027b6d81be7883c40960f0de3ffa04b699/DistFiles/Language%20Explorer/FlexStyles.xml#L245-L250) |
| Text title editor's language property | Root Normal; fixed 10-point setting is for the UI label, not title data | Text title values: Normal, not Title_Main. Title_Main is captured for actual styled title paragraphs | [TitleContentsPane.cs:359–423](https://github.com/sillsdev/FieldWorks/blob/089eb9027b6d81be7883c40960f0de3ffa04b699/Src/LexText/Interlinear/TitleContentsPane.cs#L359-L423) |
| Dictionary publication headword, vernacular examples and POS | Dictionary-Headword, Dictionary-Vernacular, Dictionary-POS | Captured for Settings and trace provenance. Inspector matches Lexicon Edit, not a publication renderer | [LexEntry.fwlayout:184](https://github.com/sillsdev/FieldWorks/blob/089eb9027b6d81be7883c40960f0de3ffa04b699/DistFiles/Language%20Explorer/Configuration/Parts/LexEntry.fwlayout#L184), [LexSense.fwlayout:95](https://github.com/sillsdev/FieldWorks/blob/089eb9027b6d81be7883c40960f0de3ffa04b699/DistFiles/Language%20Explorer/Configuration/Parts/LexSense.fwlayout#L95), [style definitions](https://github.com/sillsdev/FieldWorks/blob/089eb9027b6d81be7883c40960f0de3ffa04b699/DistFiles/Language%20Explorer/FlexStyles.xml#L144-L170) |
| No direct equivalent: aggregated word and grammar data | Nearest: interlinear word/morph/gloss/category lines, Normal | Word list, Lists, Matrix detail, Timing, Warnings, Try a Word and trace language strings: Normal. These show the same language data without imposing another size hierarchy | [InterlinVc fragments](https://github.com/sillsdev/FieldWorks/blob/089eb9027b6d81be7883c40960f0de3ffa04b699/Src/LexText/Interlinear/InterlinVc.cs#L779-L806). This is an explicit Motif nearest-surface decision |

The inventory includes Normal, Paragraph, Dictionary-Headword, Dictionary-Vernacular, Dictionary-POS and Title_Main, plus every project style. Absent styles resolve to effective Normal. styleFonts and styleSizes have identical keys. These describe stylesheet properties; plain-string projections do not reproduce rich-string run formatting such as bold, italic or explicit run sizes.

## Capture and tags

The Baseline summary stores the inventory in the same publication transaction as its words. The writing-systems command, Overview, Text words, open and AnalysisAggregate expose it. Store generation 40 refuses older databases with the delete-and-recreate message; there is no migration or compatibility reader.

Displayed strings retain adjacent writing-system tags even when null: null means composed, non-language or unresolved text. Alternatives use WritingSystemText records. Best-alternative tags come from chosen rich-string runs, never an assumed default. Mixed runs have no single tag. Text words group by form, wordform identity and writing system, retaining same-spelled alternatives in different systems as separate rows.

Joined glosses, aggregate breakdowns, feature notation and composed labels have null tags; consumers use individually tagged constituents. Parser-guessed strings have no authored source tag. Assessment spellings use exact populated alternatives; same spelling in multiple systems stays null. Review uncertainty tokens retain their form tag; fingerprints remain linguistic evidence.

Trace uses the same Host reader and retains features, effective style fonts and sizes alongside default font and direction. Compatibility compares these in list order; map insertion order is immaterial. Parser diagnostics are untouched.

Multilingual semantic snapshots use NFD for MultiUnicode and LibLCM NFSC for MultiString, composing only where run formatting allows. The regression test checks both and a split-format combining sequence.
