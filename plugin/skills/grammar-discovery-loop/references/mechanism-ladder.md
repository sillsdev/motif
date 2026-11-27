# Mechanism ladder (Andy Black)

Fix an incorrect parse with the tightest principled mechanism that works. Climb only when the lower rung fails
its test. Every hypothesis and decide record names the rung used and the rungs tried, with the result of
each. Guidance and its sources: [black-guidance.md](black-guidance.md).

1. Categories and co-occurrence (what attaches to what).
2. Allomorph environments: slash and underscore notation, ORed per allomorph; allomorph order is disjunctive;
   unique natural-class abbreviations. Put the context on the allomorph, because narrowing a rule's left context
   alone can leave over-generation untouched (10-N1).
3. Inflection classes and features, when the variation is lexical rather than phonological; stem names when
   conditioned by inflection features.
4. Exception features (productivity restrictions, stem ProdRestrict): "not true features", used where a
   combination is simply incorrect. Tag the fewest objects among what FieldWorks can express: use the feature
   where the rule or allomorph can require or exclude it. An affix restriction is positive only (the stem must
   carry the feature), so where the majority must carry the tag, record that as a FieldWorks limitation in the
   hypothesis record and the Guide and invent no negative mechanism.
5. Separate templates or entries, including a separate clitic entry (one clitic per orthographic word may carry
   the clitic type; others are proclitic or enclitic in order).
6. Ad hoc co-occurrence rules, tightest first: adjacent before or after, then somewhere, then anywhere.
   Last resort; a group of them hints at a missing principled mechanism.

**Cost check.** Weigh the knock-on cost of a template or derivation-versus-inflection call before making it: name the
later entries, slots and templates it forces.

Black sets no numeric cap and no decision rule between rule and list, and says nothing on nonword testing;
the loop's controls and size delta fill that gap. Avoid null allomorphs without need (parser speed).
Constructs a real FieldWorks project cannot create are out of scope.
