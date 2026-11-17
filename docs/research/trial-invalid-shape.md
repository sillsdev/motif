# Why a Trial calls a word invalid evidence

An invalid shape means the parser could not segment a spelling with the project's phoneme representations. It does not mean the wordform has the wrong identity or case.

`DigitAndHyphenRealParserTests.ForeignNamesAndAcronymsNeedTheirLettersInThePhonemeInventory` builds a disposable FieldWorks project at runtime. Its inventory contains every character in `canada`, `bocco`, `act`, `fc`, `afcon`, `eacj`, `tellack`, and `sadc` except `c`. All eight lowercase spellings return `invalidShape=true` directly from PanGloss. Adding the `c` representation through Motif's phoneme Composer makes all eight shapes valid. The fixture uses no stored wordforms and no serialized project payload.

The executable is pinned PanGloss 0.19.6, Linux x64, SHA-256 `90fc5ee1b8a7db4f380e7648609f91999829bd3ffba0a351992ec70744289924`.

This isolates an inventory explanation for the reported set: every spelling contains `c`. It rules out a Motif case-folding or wordform-identity requirement in this reproduction. The original private Swahili project's inventory was not supplied, so this is a controlled reproduction, not a claim to have inspected that inventory. Adding a representation to a real language's inventory remains a linguistic decision.

The native morphology batch records `invalidShape=true`, empty analyses and unavailable projections, and a null `refusal`. It does not identify the rejected segment or position. Motif retains a supplied refusal verbatim; otherwise it reports that the shape could not be segmented and that no segment detail was recorded. More precise native segment diagnostics belong to PanGloss; Motif must not invent a character from the spelling alone.
