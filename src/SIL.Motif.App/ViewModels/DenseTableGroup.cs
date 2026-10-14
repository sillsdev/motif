namespace SIL.Motif.App.ViewModels;

/// <summary>
/// A heading row in a dense table: a label and a ready-made count text such as "3 findings". A page that opts
/// in to the shared dense table puts one of these before each group of its rows in the list it binds. The
/// heading itself cannot be selected or focused; any child actions remain reachable by pointer and keyboard.
/// </summary>
/// <param name="Label">The group's name, shown at the start of the heading.</param>
/// <param name="CountText">The group's size in the page's own words, shown at the end of the heading.</param>
public sealed record DenseTableGroup(string Label, string CountText) : IDenseTableGroupHeading;
