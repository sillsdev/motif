# M4 Review changes implementation plan

Review changes will let a linguist understand each pending decision and undo it safely before Apply. The work groups decisions by their effect, reuses the existing analysis marks, and keeps Analyze texts responsible for text navigation.

## 1. Group the pending changes

The first slice gives each pending change one designed group and a stable position in that group. Tests cover titles, transition classification, accepted-set source, uncertain overlays, sorting, and group Undo; M7's shared `GroupId` remains the unit removed for accepted sets.

## 2. Wire each item's actions

The second slice connects Undo, Check again, context expansion, and Go to text to existing page and command seams. Tests cover project switches during Undo and exact occurrence selection without moving Analyze texts ownership into Review.

## 3. Render the grouped review

The third slice replaces the flat and throwaway uncertain cards with the ten ordered groups. It binds the existing OpinionMark, Staged, and HoverReveal components and keeps secondary actions keyboard reachable.

## 4. Exercise real command and window paths

The fourth slice uses a seeded synthetic project and the real CommandClient to cover staging, item and group Undo, and Apply blocking while uncertain. One existing startup walkthrough covers two change kinds, their now/after marks, one Undo, and Apply with the fake parser held.

## 5. Verify and report

The final slice runs the repository build and focused tests, records their totals and any environment limits, and writes the requested report. Each finished implementation slice gets a small commit with the requested co-author trailer; nothing is pushed or merged.
