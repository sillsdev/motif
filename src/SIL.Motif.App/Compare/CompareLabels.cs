namespace SIL.Motif.App.Compare;

/// <summary>The other version's attribution and the actions of a comparison; defaults describe a FieldWorks edit.</summary>
public sealed record CompareLabels(string OtherName = "FieldWorks", string OtherTag = ChangeWords.FieldWorksTag)
{
    public string Explanation { get; init; } = CompareWindow.Explanation;
    public string Cancel { get; init; } = ChangeWords.DecideLater;
    public string StartAgain { get; init; } = ChangeWords.StartAgain;
    public string UseOther => $"Use {OtherName}";
    public string UseOtherForAll => $"Use {OtherName} for all";
}
