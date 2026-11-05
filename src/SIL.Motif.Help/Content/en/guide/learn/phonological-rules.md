# Phonological rules

When a sound alternation follows a regular pattern, a phonological rule can describe the change once instead of listing a separate form for every word. The choice depends on how the language works: some alternations are best represented with allomorphs, some with rules, and some with both.

English plural endings give an illustrative example of a familiar sound pattern. The ending is usually pronounced with a voiceless [s] after a voiceless sound, as in *cats*, and with [z] after a voiced sound, as in *dogs*. After sibilant sounds, as in *buses*, an extra vowel is heard. These examples show how nearby sounds can affect the way an ending is pronounced; spelling alone does not show every sound detail.

A rule says what changes and in what environment. In plain words, one part of this pattern could be described as “make the ending voiced after a voiced sound.” A well-chosen rule covers the words that share that environment. A rule that is too broad can also change words you meant to leave alone, so check examples from both sides of the pattern.

Rule order matters when one rule changes something that another rule looks for. If Rule 1 creates the sound that Rule 2 needs to see, Rule 1 must apply first. If Rule 2 applies first, it cannot act on a sound that does not exist yet. When a trace shows an unexpected output, check both the rule’s environment and whether earlier rules have already changed the relevant sound.

You can change a rule after it exists: widen its environment, make an item in it optional or repeated, move it earlier or later, turn it off to see what the grammar does without it, or delete it. A rule's environment can name a word or morpheme boundary. Input and Output each hold up to eight phoneme or natural-class items in order; unequal lengths express shortening or lengthening, and an empty side means insertion or deletion.

## One rule, several outcomes

Wolff section 5.15 gives *kabukiran* and *kabukilan* from *bukid*: a lexically selected d alternates with r or l. These need not be duplicate lexical entries. A rule can have a segment-list Output natural class containing exactly r and l, with a required exception feature on the selected stem analysis. Both outputs are available where that rule applies. Tag only the analyses supported by the source; this does not license *saylan* merely because *sayran* exists. For *lalum/lawum*, a class containing the original l and the alternative w can express retention alongside change, with the appropriate lexical and contextual restrictions.

Several right-hand sides serve a different purpose: they are ordered cases within one rule. Each has its own Output, left and right contexts, required and excluded rule features, and optional input categories. The first applicable case wins at a matching site. Two identical environments with d → r first and d → l second do not give free variation: the first hides the second. For optional lexical alternatives in one environment, use an Output class with the intended members, then check both forms in a Trial.

[Author a sound rule](cmd:compose-author-phonological-rule) and [Edit a sound rule](cmd:compose-edit-phonological-rule) let you keep several ordered cases in one rule, with lexical applicability per case. Read `motif help guide:encoding/phonological-rules --full` for the intent shape, expected-current guards and lexical alternatives. Rule features restrict applicability; they do not create an implicit dialect or register model.
