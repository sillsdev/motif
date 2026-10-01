# Why a grammar is slow

A grammar can find the right analysis and still make the parser do too much work. Optional slots, broad environments, duplicated affixes and empty affixes can each give it more paths to explore, but none of them is always costly: measure before you change anything.

An optional slot lets the parser consider a form both with and without that slot. Broad environments let a rule apply in many places, creating more possible continuations. An empty affix adds no visible letters, so the parser can add it without consuming part of the word, though PanGloss often handles these cheaply: in the synthetic Bantu-style sample, 160 empty allomorphs added no measurable work. A duplicated real affix is different; there, copying one object marker into a second slot multiplied the work.

Use [Timing](cmd:timing) after [Parse all words](term:parse-all-words) to locate recorded parse time. **Timing** groups time first by kind of rule, then by rule, and then shows slow words. Start with **Slowest** or **All**, select a costly rule, and look at the words that use it. A single unusually slow word may point to its stem or environment; a costly rule affecting many words may be a broader pattern to review.

Check that each optional slot is truly optional, each environment names the sounds it should, and each affix appears only once. Change one thing at a time and measure again. If the search stopped at a step limit, more time will not fix that outcome; choose a larger step limit for the rerun. A timing result shows where work was spent, while your language knowledge helps decide whether the grammar can be made more specific.
