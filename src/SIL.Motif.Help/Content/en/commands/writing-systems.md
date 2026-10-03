# Show writing systems

`motif writing-systems --project <fwdata> [--json]` reads the current [Baseline](term:baseline)'s writing systems without opening the FieldWorks project. Before the first capture, it reports that no Baseline exists.

Each current vernacular and analysis list keeps FieldWorks' order and default. The response includes the id, name, abbreviation, requested default font family, verbatim feature string, direction, and each style's effective font, features and point size. A writing system in both lists appears once per membership; positions in JSON count from zero.

Normal supplies interlinear and Lexicon Edit fields. Source sentences use their paragraph's authored style, or Normal when none is authored. Dictionary-Headword, Dictionary-Vernacular and Dictionary-POS describe dictionary publication. The inventory includes every project style; a missing style uses that writing system's effective Normal settings. Normal without a size uses FieldWorks' 10-point default. Sizes contain no window units or zoom.

Change fonts, features and direction in FieldWorks: **Format > Set up Vernacular Writing Systems…** or **Format > Set up Analysis Writing Systems…**, then the **Font** or **General** tab. Change style settings through **Format > Styles…**. Run [baseline capture](cmd:baseline%20capture) again to update the recorded settings.

The font name is the project's request, even if the font is not installed on this computer. Displaying these settings does not check font availability. Settings retains the feature string verbatim; the window applies OpenType feature tags.
